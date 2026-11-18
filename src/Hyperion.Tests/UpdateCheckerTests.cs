using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hyperion.Core.Updates;
using Xunit;

namespace Hyperion.Tests;

public class UpdateCheckerTests
{
    const string Base = "https://github.com/Fitzy1590/Hyperion-web/releases/download/v1.2.0/";

    sealed class Stub(Func<string, (HttpStatusCode, byte[])> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var (code, body) = respond(r.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });
        }
    }

    static string Release(string tag = "v1.2.0", bool draft = false, bool pre = false,
        string msixUrl = Base + "Hyperion_1.2.0.0_x64.msix", bool withSha = true)
    {
        var sha = withSha ? $$""",{"name":"Hyperion_1.2.0.0_x64.msix.sha256","browser_download_url":"{{Base}}Hyperion_1.2.0.0_x64.msix.sha256"}""" : "";
        return $$"""{"tag_name":"{{tag}}","draft":{{draft.ToString().ToLower()}},"prerelease":{{pre.ToString().ToLower()}},"body":"notes","assets":[{"name":"Hyperion_1.2.0.0_x64.msix","browser_download_url":"{{msixUrl}}"}{{sha}}]}""";
    }

    static UpdateChecker Checker(Func<string, (HttpStatusCode, byte[])>? respond = null) =>
        new(new HttpClient(new Stub(respond ?? (_ => (HttpStatusCode.NotFound, [])))));

    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("v1.2.3.4", "1.2.3.4")]
    public void ParsesTags(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsNonVersionTags(string? tag) => Assert.Null(UpdateChecker.ParseTag(tag));

    [Fact]
    public void OffersNewerRelease()
    {
        var r = Checker().SelectUpdate(Release(), new Version(1, 0, 0, 0));
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 2, 0, 0), r!.Version);
        Assert.EndsWith(".msix.sha256", r.Sha256Url.ToString());
    }

    [Fact] public void IgnoresSameOrOlder() => Assert.Null(Checker().SelectUpdate(Release(), new Version(1, 2, 0, 0)));
    [Fact] public void IgnoresDrafts() => Assert.Null(Checker().SelectUpdate(Release(draft: true), new Version(1, 0, 0, 0)));
    [Fact] public void IgnoresPrereleases() => Assert.Null(Checker().SelectUpdate(Release(pre: true), new Version(1, 0, 0, 0)));
    [Fact] public void RequiresChecksumAsset() => Assert.Null(Checker().SelectUpdate(Release(withSha: false), new Version(1, 0, 0, 0)));

    [Theory]
    [InlineData("https://evil.example.com/Fitzy1590/Hyperion-web/releases/download/v1/x.msix")]
    [InlineData("https://github.com/someone-else/repo/releases/download/v1/x.msix")]
    [InlineData("http://github.com/Fitzy1590/Hyperion-web/releases/download/v1/x.msix")]
    public void RejectsForeignAssetUrls(string url) =>
        Assert.Null(Checker().SelectUpdate(Release(msixUrl: url), new Version(1, 0, 0, 0)));

    [Fact]
    public async Task NoReleasesMeansNoUpdate() => Assert.Null(await Checker().CheckAsync(new Version(1, 0, 0, 0)));

    [Fact]
    public async Task DownloadsAndVerifiesChecksum()
    {
        var payload = Encoding.UTF8.GetBytes("pretend msix");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLower();
        var checker = Checker(url => url.EndsWith(".sha256")
            ? (HttpStatusCode.OK, Encoding.UTF8.GetBytes($"{hash}  Hyperion_1.2.0.0_x64.msix"))
            : (HttpStatusCode.OK, payload));
        var rel = checker.SelectUpdate(Release(), new Version(1, 0, 0, 0))!;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = await checker.DownloadAsync(rel, dir);
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ChecksumMismatchDeletesFileAndThrows()
    {
        var checker = Checker(url => url.EndsWith(".sha256")
            ? (HttpStatusCode.OK, Encoding.UTF8.GetBytes(new string('0', 64)))
            : (HttpStatusCode.OK, [1, 2, 3]));
        var rel = checker.SelectUpdate(Release(), new Version(1, 0, 0, 0))!;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(rel, dir));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
