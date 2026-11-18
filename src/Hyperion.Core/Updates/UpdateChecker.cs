using System.Security.Cryptography;
using System.Text.Json;

namespace Hyperion.Core.Updates;

public sealed record ReleaseInfo(Version Version, string Tag, Uri MsixUrl, Uri Sha256Url, string Notes);

/// <summary>
/// Checks the latest GitHub Release and downloads its MSIX. A release is only offered if it carries both
/// <c>*.msix</c> and <c>*.msix.sha256</c> assets hosted under this repository's own release URLs.
/// Installing is left to Windows (App Installer), which enforces the package signature.
/// </summary>
public sealed class UpdateChecker(HttpClient http, string owner = "Fitzy1590", string repo = "Hyperion-web")
{
    /// <summary>"v1.2.3" -> 1.2.3.0; null if it is not a version tag.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    public static string? ParseSha256(string text)
    {
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    bool IsTrusted(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com"
        && u.AbsolutePath.StartsWith($"/{owner}/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    public ReleaseInfo? SelectUpdate(string releaseJson, Version current)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var r = doc.RootElement;
        static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
        if (Flag(r, "draft") || Flag(r, "prerelease")) return null;

        var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (ParseTag(tag) is not { } version || version <= current) return null;
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        Uri? msix = null, sha = null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || url is null || !IsTrusted(url)) continue;
            if (name.EndsWith(".msix.sha256", StringComparison.OrdinalIgnoreCase)) sha = new Uri(url);
            else if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)) msix = new Uri(url);
        }
        if (msix is null || sha is null) return null;
        var notes = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new ReleaseInfo(version, tag!, msix, sha, notes);
    }

    public async Task<ReleaseInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var resp = await SendAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();
        return SelectUpdate(await resp.Content.ReadAsStringAsync(ct), current);
    }

    /// <summary>Downloads the MSIX into <paramref name="directory"/> and verifies its SHA-256; deletes it on mismatch.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string directory,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string? expected;
        using (var shaResp = await SendAsync(release.Sha256Url.ToString(), ct))
        {
            shaResp.EnsureSuccessStatusCode();
            expected = ParseSha256(await shaResp.Content.ReadAsStringAsync(ct));
        }
        if (expected is null) throw new InvalidDataException("The release checksum file is malformed.");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(release.MsixUrl.LocalPath));
        using (var resp = await SendAsync(release.MsixUrl.ToString(), ct, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(path);
            throw new InvalidDataException("The downloaded update failed its checksum check and was discarded.");
        }
        return path;
    }

    Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct,
        HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Hyperion-Updater"); // GitHub's API rejects requests without a User-Agent
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        return http.SendAsync(req, option, ct);
    }
}
