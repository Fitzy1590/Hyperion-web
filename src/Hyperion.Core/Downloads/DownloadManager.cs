using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Hyperion.Core.Downloads;

public sealed class Segment
{
    public long Start { get; set; }
    public long End { get; set; }
    /// <summary>Next byte to fetch; everything before it is on disk.</summary>
    public long Next { get; set; }
}

public sealed record DownloadProgress(long BytesDone, long TotalBytes, double BytesPerSecond);

public sealed record DownloadOptions
{
    /// <summary>Clamped to 16-32 for large files.</summary>
    public int Connections { get; init; } = 16;
    public int MaxRetries { get; init; } = 8;
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Parallel HTTP Range downloader with auto-resume (<c>.part</c> + <c>.hyperion</c> state file) and a global bandwidth cap.</summary>
public sealed class DownloadManager(HttpClient http)
{
    public const int MinConnections = 16;
    public const int MaxConnections = 32;

    sealed record ResumeState(string Url, long Length, string? ETag, List<Segment> Segments);

    sealed class Counter { public long Done; }

    sealed class Throttle
    {
        long _limit, _sent;
        Stopwatch _sw = Stopwatch.StartNew();

        public long Limit
        {
            get => Volatile.Read(ref _limit);
            set { Volatile.Write(ref _limit, value); Interlocked.Exchange(ref _sent, 0); _sw = Stopwatch.StartNew(); }
        }

        public async ValueTask WaitAsync(int bytes, CancellationToken ct)
        {
            var limit = Limit;
            if (limit <= 0) return;
            var total = Interlocked.Add(ref _sent, bytes);
            var delay = TimeSpan.FromSeconds((double)total / limit) - _sw.Elapsed;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        }
    }

    readonly Throttle _throttle = new();

    /// <summary>Global cap in bytes/second across all downloads; 0 = unlimited.</summary>
    public long MaxBytesPerSecond
    {
        get => _throttle.Limit;
        set => _throttle.Limit = value;
    }

    /// <summary>Raised with the final file path when a download completes.</summary>
    public event Action<string>? Completed;

    public static IReadOnlyList<Segment> Plan(long length, int connections, long minSegmentBytes = 1 << 20)
    {
        if (length <= 0) return [];
        var maxByLength = Math.Max(1, (length + minSegmentBytes - 1) / minSegmentBytes);
        var count = (int)Math.Min(Math.Max(1, connections), maxByLength);
        var size = (length + count - 1) / count;
        var list = new List<Segment>(count);
        for (long start = 0; start < length; start += size)
            list.Add(new Segment { Start = start, End = Math.Min(start + size, length) - 1, Next = start });
        return list;
    }

    public async Task<string> DownloadAsync(
        Uri url, string destinationPath, DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        options ??= new();
        var part = destinationPath + ".part";
        var statePath = destinationPath + ".hyperion";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        var (length, etag, ranges) = await ProbeAsync(url, ct);
        if (!ranges || length <= 0)
        {
            await DownloadSingleAsync(url, destinationPath, progress, ct);
            Completed?.Invoke(destinationPath);
            return destinationPath;
        }

        var connections = Math.Clamp(options.Connections, MinConnections, MaxConnections);
        var state = LoadState(statePath, part, url, length, etag)
                    ?? new ResumeState(url.ToString(), length, etag, [.. Plan(length, connections)]);

        using var handle = File.OpenHandle(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, FileOptions.Asynchronous);
        RandomAccess.SetLength(handle, length);

        var counter = new Counter { Done = state.Segments.Sum(s => s.Next - s.Start) };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = state.Segments.Select(s => WorkerAsync(url, handle, s, counter, options, cts.Token)).ToList();
        var all = Task.WhenAll(workers);
        var reporter = ReportAsync(state, statePath, counter, length, progress, all, cts.Token);

        try
        {
            await all;
        }
        catch
        {
            cts.Cancel(); // stop remaining workers; state file is kept so the next call resumes
            throw;
        }
        finally
        {
            try { await reporter; } catch (OperationCanceledException) { /* shutting down */ }
            SaveState(statePath, state);
        }

        handle.Dispose();
        File.Move(part, destinationPath, overwrite: true);
        File.Delete(statePath);
        progress?.Report(new DownloadProgress(length, length, 0));
        Completed?.Invoke(destinationPath);
        return destinationPath;
    }

    async Task<(long Length, string? ETag, bool Ranges)> ProbeAsync(Uri url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(0, 0) } };
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var etag = resp.Headers.ETag?.Tag;
        if (resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange?.Length is { } total)
            return (total, etag, true);
        return (resp.Content.Headers.ContentLength ?? -1, etag, false);
    }

    async Task DownloadSingleAsync(Uri url, string destinationPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        var part = destinationPath + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                await _throttle.WaitAsync(n, ct);
                done += n;
                progress?.Report(new DownloadProgress(done, total, 0));
            }
        }
        File.Move(part, destinationPath, overwrite: true);
    }

    async Task WorkerAsync(Uri url, SafeFileHandle handle, Segment seg, Counter counter, DownloadOptions o, CancellationToken ct)
    {
        var attempt = 0;
        while (seg.Next <= seg.End)
        {
            try
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(seg.Next, seg.End) } };
                stall.CancelAfter(o.StallTimeout);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                if (resp.StatusCode != HttpStatusCode.PartialContent)
                    throw new HttpRequestException($"Expected 206 Partial Content, got {(int)resp.StatusCode}.");

                await using var stream = await resp.Content.ReadAsStreamAsync(stall.Token);
                var buf = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    while (seg.Next <= seg.End)
                    {
                        stall.CancelAfter(o.StallTimeout);
                        var want = (int)Math.Min(buf.Length, seg.End - seg.Next + 1);
                        var n = await stream.ReadAsync(buf.AsMemory(0, want), stall.Token);
                        if (n == 0) break;
                        await RandomAccess.WriteAsync(handle, buf.AsMemory(0, n), seg.Next, ct);
                        seg.Next += n;
                        Interlocked.Add(ref counter.Done, n);
                        attempt = 0;
                        await _throttle.WaitAsync(n, ct);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
                if (seg.Next <= seg.End) throw new IOException("Connection closed before the segment finished.");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                                       && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                if (++attempt > o.MaxRetries) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500 * (1 << attempt), 15000)), ct);
            }
        }
    }

    static async Task ReportAsync(ResumeState state, string statePath, Counter counter, long total,
        IProgress<DownloadProgress>? progress, Task finished, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long last = Interlocked.Read(ref counter.Done);
        var lastT = sw.Elapsed;
        while (!finished.IsCompleted)
        {
            await Task.WhenAny(finished, Task.Delay(1000, ct));
            var done = Interlocked.Read(ref counter.Done);
            var now = sw.Elapsed;
            var speed = (done - last) / Math.Max(0.001, (now - lastT).TotalSeconds);
            (last, lastT) = (done, now);
            progress?.Report(new DownloadProgress(done, total, speed));
            SaveState(statePath, state);
        }
    }

    static void SaveState(string path, ResumeState state)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(state)); }
        catch (IOException) { /* best effort; the next tick retries */ }
    }

    static ResumeState? LoadState(string statePath, string partPath, Uri url, long length, string? etag)
    {
        if (!File.Exists(statePath) || !File.Exists(partPath)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(statePath));
            return s is not null && s.Url == url.ToString() && s.Length == length && s.ETag == etag ? s : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null; // unusable state: start over
        }
    }
}
