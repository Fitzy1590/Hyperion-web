using Hyperion.Core.Downloads;
using Hyperion.Core.Media;
using Hyperion.Core.Navigation;
using Hyperion.Core.Tabs;
using Hyperion.Core.Torrent;
using Xunit;

namespace Hyperion.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("", "https://start.me")]
    [InlineData("  ", "https://start.me")]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("example.com/path", "https://example.com/path")]
    [InlineData("localhost:3000", "http://localhost:3000")]
    [InlineData("magnet:?xt=urn:btih:abc", "magnet:?xt=urn:btih:abc")]
    public void Normalizes(string input, string expected) => Assert.Equal(expected, UrlNormalizer.Normalize(input));

    [Fact]
    public void SearchesForPlainText() =>
        Assert.Equal("https://www.google.com/search?q=hello%20world", UrlNormalizer.Normalize("hello world"));
}

public class KeyCodeMapTests
{
    [Theory]
    [InlineData(0xDD, "BracketRight")]
    [InlineData(0xDB, "BracketLeft")]
    [InlineData(0x31, "Digit1")]
    [InlineData(0x41, "KeyA")]
    [InlineData(0x70, "F1")]
    [InlineData(0x27, "ArrowRight")]
    public void MapsVirtualKeys(int vk, string code) => Assert.Equal(code, KeyCodeMap.FromVirtualKey(vk));

    [Fact]
    public void UnknownKeyIsNull() => Assert.Null(KeyCodeMap.FromVirtualKey(0x10)); // Shift alone

    [Fact]
    public void ComboUsesCanonicalOrder() =>
        Assert.Equal("Ctrl+Alt+Shift+Digit1", KeyCodeMap.Combo(true, true, true, false, "Digit1"));
}

public class GlobalSpeedEngineTests
{
    [Fact]
    public void DefaultsMatchSpec()
    {
        var rules = new MediaSettings().Shortcuts;
        Assert.Contains(new ShortcutRule(true, ShortcutAction.Speed, MediaSettings.Set, "Digit4", 1.7), rules);
        Assert.Contains(new ShortcutRule(true, ShortcutAction.Seek, MediaSettings.Add, "KeyQ", -5), rules);
        Assert.Contains(new ShortcutRule(true, ShortcutAction.Seek, MediaSettings.Add, "KeyZ", -60), rules);
        Assert.Contains(new ShortcutRule(false, ShortcutAction.FastForward, MediaSettings.Set, "KeyG", 3), rules);
        Assert.Equal(rules.Length, rules.Select(r => r.Key).Distinct().Count()); // no default shortcut shadows another
    }

    [Fact]
    public void ConfigListsOnlyEnabledRulesWithKeys()
    {
        var settings = new MediaSettings
        {
            Shortcuts =
            [
                new(true, ShortcutAction.Speed, MediaSettings.Add, "KeyW", 0.1),
                new(false, ShortcutAction.Seek, MediaSettings.Add, "KeyE", 5),
                new(true, ShortcutAction.Seek, MediaSettings.Add, "", 5),
            ],
        };
        using var doc = System.Text.Json.JsonDocument.Parse(new GlobalSpeedEngine(settings).BuildConfigJson());
        var rules = doc.RootElement.GetProperty("rules");
        Assert.Equal(1, rules.GetArrayLength());
        Assert.Equal("KeyW", rules[0].GetProperty("key").GetString());
        Assert.Equal("speed", rules[0].GetProperty("action").GetString());
        Assert.Equal(0.1, rules[0].GetProperty("value").GetDouble());
    }

    [Theory]
    [InlineData("""{"type":"fullwindow","value":1}""", true)]
    [InlineData("""{"type":"fullwindow","value":0}""", false)]
    public void HandleRaisesFullWindowChanged(string json, bool expected)
    {
        var engine = new GlobalSpeedEngine(new MediaSettings());
        bool? on = null;
        engine.FullWindowChanged += v => on = v;
        engine.Handle(json);
        Assert.Equal(expected, on);
    }

    [Theory]
    [InlineData("""{"type":"osd","kind":"speed","value":1.3}""", "⚡ 1.3x Speed")]
    [InlineData("""{"type":"osd","kind":"seek","value":10}""", "⏩ +10s Seek")]
    [InlineData("""{"type":"osd","kind":"seek","value":-30}""", "⏪ -30s Seek")]
    public void HandleRaisesOsd(string json, string expected)
    {
        var engine = new GlobalSpeedEngine(new MediaSettings());
        string? text = null;
        engine.OsdRequested += o => text = o.Text;
        engine.Handle(json);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void GarbageMessagesAreIgnored() => Assert.Null(GlobalSpeedEngine.Parse("not json"));

    [Fact]
    public void SettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var rule = new ShortcutRule(true, ShortcutAction.Seek, MediaSettings.Add, "KeyJ", 7);
            new MediaSettings { Shortcuts = [rule] }.Save(path);
            Assert.Equal(new[] { rule }, MediaSettings.Load(path).Shortcuts);
        }
        finally { File.Delete(path); }
    }
}

public class TabSuspenderTests
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    static (TabSuspenderService svc, Clock clock, Guid id, Func<int> suspendCalls) Setup(bool pinned = false, bool playing = false)
    {
        var clock = new Clock();
        var svc = new TabSuspenderService(clock) { SuspendAfter = TimeSpan.FromMinutes(5) };
        var id = Guid.NewGuid();
        var calls = 0;
        svc.Register(id, () => pinned, () => playing, () => { calls++; return Task.FromResult(true); }, () => { });
        return (svc, clock, id, () => calls);
    }

    [Fact]
    public async Task SuspendsInactiveTabAfterThreshold()
    {
        var (svc, clock, id, calls) = Setup();
        svc.Deactivate(id);
        clock.Now += TimeSpan.FromMinutes(4);
        await svc.SweepAsync();
        Assert.Equal(0, calls());
        clock.Now += TimeSpan.FromMinutes(2);
        await svc.SweepAsync();
        Assert.Equal(1, calls());
    }

    [Fact]
    public async Task NeverSuspendsActivePinnedOrPlayingTabs()
    {
        foreach (var (pinned, playing, active) in new[] { (true, false, false), (false, true, false), (false, false, true) })
        {
            var (svc, clock, id, calls) = Setup(pinned, playing);
            if (active) svc.Activate(id); else svc.Deactivate(id);
            clock.Now += TimeSpan.FromHours(2);
            await svc.SweepAsync();
            Assert.Equal(0, calls());
        }
    }

    [Fact]
    public async Task ActivateResumesSuspendedTab()
    {
        var clock = new Clock();
        var svc = new TabSuspenderService(clock) { SuspendAfter = TimeSpan.FromMinutes(5) };
        var id = Guid.NewGuid();
        var resumed = false;
        var changes = new List<bool>();
        svc.SuspendedChanged += (_, s) => changes.Add(s);
        svc.Register(id, () => false, () => false, () => Task.FromResult(true), () => resumed = true);
        svc.Deactivate(id);
        clock.Now += TimeSpan.FromMinutes(10);
        await svc.SweepAsync();
        svc.Activate(id);
        Assert.True(resumed);
        Assert.Equal(new[] { true, false }, changes);
    }
}

public class StreamRingBufferTests
{
    [Fact]
    public void WrapsAroundAndReadsByFileOffset()
    {
        var ring = new StreamRingBuffer(8);
        ring.Reset(100);
        Assert.Equal(6, ring.Write([1, 2, 3, 4, 5, 6]));
        ring.TrimBefore(104); // drops 1..4
        Assert.Equal(104, ring.StartOffset);
        Assert.Equal(6, ring.Write([7, 8, 9, 10, 11, 12, 13])); // only 6 free -> wraps
        Assert.Equal(0, ring.Free);

        var dst = new byte[8];
        Assert.Equal(8, ring.Read(104, dst));
        Assert.Equal(new byte[] { 5, 6, 7, 8, 9, 10, 11, 12 }, dst);
        Assert.Equal(0, ring.Read(103, dst));
        Assert.Equal(0, ring.Read(112, dst));
    }
}

public class DownloadManagerTests
{
    [Fact]
    public void PlanCoversEveryByteWithoutOverlap()
    {
        var segs = DownloadManager.Plan(100_000_007, 32);
        Assert.Equal(32, segs.Count);
        Assert.Equal(0, segs[0].Start);
        Assert.Equal(100_000_006, segs[^1].End);
        for (var i = 1; i < segs.Count; i++) Assert.Equal(segs[i - 1].End + 1, segs[i].Start);
    }

    [Fact]
    public void PlanLimitsConnectionsForSmallFiles() => Assert.Single(DownloadManager.Plan(500, 16));

    [Fact]
    public async Task DownloadsInParallelFromRangeServer()
    {
        var data = new byte[5 * 1024 * 1024 + 123];
        new Random(42).NextBytes(data);
        using var server = new RangeServer(data);
        var dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            var mgr = new DownloadManager(new HttpClient());
            await mgr.DownloadAsync(server.Url, dest, new DownloadOptions { Connections = 16 });
            Assert.Equal(data, await File.ReadAllBytesAsync(dest));
            Assert.False(File.Exists(dest + ".part"));
            Assert.False(File.Exists(dest + ".hyperion"));
            Assert.True(server.MaxConcurrent > 1);
        }
        finally { File.Delete(dest); }
    }

    sealed class RangeServer : IDisposable
    {
        readonly System.Net.HttpListener _l = new();
        readonly byte[] _data;
        int _active;
        public int MaxConcurrent;
        public Uri Url { get; }

        public RangeServer(byte[] data)
        {
            _data = data;
            var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            Url = new Uri($"http://localhost:{port}/f.bin");
            _l.Prefixes.Add($"http://localhost:{port}/");
            _l.Start();
            _ = Task.Run(LoopAsync);
        }

        async Task LoopAsync()
        {
            while (_l.IsListening)
            {
                System.Net.HttpListenerContext c;
                try { c = await _l.GetContextAsync(); } catch (Exception) { return; }
                _ = Task.Run(async () =>
                {
                    var now = Interlocked.Increment(ref _active);
                    int seen;
                    while (now > (seen = MaxConcurrent) && Interlocked.CompareExchange(ref MaxConcurrent, now, seen) != seen) { }
                    try
                    {
                        long s = 0, e = _data.Length - 1;
                        if (c.Request.Headers["Range"] is { } r)
                        {
                            var p = r[6..].Split('-');
                            s = long.Parse(p[0]);
                            if (p[1].Length > 0) e = Math.Min(long.Parse(p[1]), e);
                            c.Response.StatusCode = 206;
                            c.Response.Headers["Content-Range"] = $"bytes {s}-{e}/{_data.Length}";
                        }
                        c.Response.ContentLength64 = e - s + 1;
                        await Task.Delay(50); // make overlap observable
                        await c.Response.OutputStream.WriteAsync(_data.AsMemory((int)s, (int)(e - s + 1)));
                    }
                    finally { c.Response.Close(); Interlocked.Decrement(ref _active); }
                });
            }
        }

        public void Dispose() => _l.Close();
    }
}

public class TorrentHeuristicsTests
{
    [Fact]
    public void PicksLargestVideoOverLargerNonVideo()
    {
        var files = new[] { ("sample.txt", 10L), ("movie.MKV", 900L), ("extras.zip", 5000L), ("trailer.mp4", 100L) };
        Assert.Equal(1, TorrentHeuristics.PickVideoFile(files));
    }

    [Fact]
    public void FallsBackToLargestFileWhenNoVideo() =>
        Assert.Equal(1, TorrentHeuristics.PickVideoFile(new[] { ("a.bin", 1L), ("b.bin", 2L) }));

    [Fact]
    public void EmptyTorrentHasNoFile() => Assert.Equal(-1, TorrentHeuristics.PickVideoFile(Array.Empty<(string, long)>()));

    [Theory]
    [InlineData(100, 1000, 30, 70)]
    [InlineData(20, 1000, 30, 0)]
    [InlineData(1000, 1000, 30, 1000)]
    public void ReadableBytesHoldsBackMarginUntilComplete(long done, long length, long margin, long expected) =>
        Assert.Equal(expected, TorrentHeuristics.ReadableBytes(done, length, margin));
}

public class ExternalLaunchTests
{
    [Theory]
    [InlineData("magnet:?xt=urn:btih:abc123&dn=x")]
    [InlineData("\"magnet:?xt=urn:btih:abc123\"")]
    [InlineData("MAGNET:?XT=urn:btih:abc123")]
    public void AcceptsMagnetLinks(string arg) => Assert.NotNull(ExternalLaunch.Accept(arg));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/a.torrent")]
    [InlineData("magnet:?dn=no-hash")]
    [InlineData("magnet:xt=urn:btih:abc")]
    [InlineData("relative\\file.torrent")]
    [InlineData("C:\\definitely\\missing\\file.torrent")]
    [InlineData("C:\\Windows\\notepad.exe")]
    public void RejectsEverythingElse(string? arg) => Assert.Null(ExternalLaunch.Accept(arg));

    [Fact]
    public void RejectsOverlongInput() =>
        Assert.Null(ExternalLaunch.Accept("magnet:?xt=urn:btih:" + new string('a', 9000)));

    [Fact]
    public void AcceptsExistingTorrentFileByFullPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".TORRENT");
        File.WriteAllText(path, "x");
        try
        {
            Assert.Equal(path, ExternalLaunch.Accept($"\"{path}\""));
            Assert.Equal(path, ExternalLaunch.AcceptFirst(new[] { "--flag", path }));
        }
        finally { File.Delete(path); }
    }
}
