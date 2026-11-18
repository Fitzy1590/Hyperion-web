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
        var s = new MediaSettings();
        Assert.Equal(0.1, s.SpeedStep);
        Assert.Equal(new double[] { 1, 2, 3, 4, 5 }, s.Presets);
        Assert.Equal(new[] { 2, 5, 10, 30, 60 }, s.SeekSeconds);
        Assert.Equal("BracketRight", s.Bindings[MediaSettings.SpeedUp]);
        Assert.Equal("BracketLeft", s.Bindings[MediaSettings.SpeedDown]);
        Assert.Equal("Digit3", s.Bindings["preset.3"]);
    }

    [Fact]
    public void ConfigMapsComboToAction()
    {
        var json = new GlobalSpeedEngine(new MediaSettings()).BuildConfigJson();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("speed.up", doc.RootElement.GetProperty("keys").GetProperty("BracketRight").GetString());
        Assert.Equal(60, doc.RootElement.GetProperty("seeks")[4].GetInt32());
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
            new MediaSettings { SpeedStep = 0.25 }.Save(path);
            Assert.Equal(0.25, MediaSettings.Load(path).SpeedStep);
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
