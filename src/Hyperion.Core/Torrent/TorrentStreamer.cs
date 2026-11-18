using System.Net;
using System.Net.Sockets;

namespace Hyperion.Core.Torrent;

public sealed record TorrentVideo(string Name, long Length);

/// <summary>
/// Seam for the libtorrent-rasterbar binding. Implementations must enable sequential download for the chosen
/// video file and make <see cref="ReadAsync"/> prioritise the pieces covering the requested range.
/// </summary>
public interface ITorrentEngine : IAsyncDisposable
{
    /// <summary>Adds the magnet/.torrent, picks the largest video file and starts sequential download.</summary>
    Task<TorrentVideo> OpenAsync(string magnetOrTorrentPath, CancellationToken ct);

    /// <summary>Reads from the video file, waiting until the covering pieces have downloaded. Returns 0 at EOF.</summary>
    ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken ct);
}

/// <summary>
/// Streams a torrent's video over a loopback HTTP server (Range requests) through a 250 MB RAM ring buffer
/// that prefetches ahead of the playhead. Serves the file as-is (progressive), not HLS.
/// </summary>
public sealed class TorrentStreamer(ITorrentEngine engine, int ringBytes = TorrentStreamer.DefaultRingBytes) : IAsyncDisposable
{
    public const int DefaultRingBytes = 250 * 1024 * 1024;
    const int ChunkBytes = 1 << 20;
    const int KeepBehindBytes = 16 << 20;
    const int HeaderPrefetchBytes = 8 << 20;

    readonly StreamRingBuffer _ring = new(ringBytes);
    readonly object _sync = new();
    readonly string _token = Guid.NewGuid().ToString("N");
    HttpListener? _listener;
    CancellationTokenSource? _serverCts, _prefetchCts;
    TorrentVideo? _video;
    long _epoch;

    public async Task<Uri> StartAsync(string magnetOrTorrentPath, CancellationToken ct)
    {
        if (_listener is not null) throw new InvalidOperationException("A stream is already running.");
        _video = await engine.OpenAsync(magnetOrTorrentPath, ct);

        var port = FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _serverCts = new CancellationTokenSource();
        _ = ServeAsync(_listener, _serverCts.Token);

        RestartPrefetch(0);
        var target = (int)Math.Min(HeaderPrefetchBytes, _video.Length);
        while (true) // wait for the container header to be buffered before handing out the URL
        {
            lock (_sync) if (_ring.Count >= target) break;
            await Task.Delay(100, ct);
        }
        return new Uri($"http://localhost:{port}/{_token}/{Uri.EscapeDataString(_video.Name)}");
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    void RestartPrefetch(long offset)
    {
        long epoch;
        CancellationTokenSource cts;
        lock (_sync)
        {
            epoch = ++_epoch;
            _ring.Reset(offset);
            _prefetchCts?.Cancel();
            _prefetchCts = cts = new CancellationTokenSource();
        }
        _ = PrefetchAsync(epoch, cts.Token);
    }

    async Task PrefetchAsync(long epoch, CancellationToken ct)
    {
        var length = _video!.Length;
        var chunk = new byte[ChunkBytes];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long pos;
                lock (_sync) { if (_epoch != epoch) return; pos = _ring.EndOffset; if (_ring.Free < ChunkBytes || pos >= length) pos = -1; }
                if (pos < 0) { await Task.Delay(50, ct); continue; }

                var n = await engine.ReadAsync(pos, chunk, ct);
                if (n == 0) { await Task.Delay(50, ct); continue; }
                lock (_sync)
                {
                    if (_epoch != epoch || _ring.EndOffset != pos) continue; // playhead jumped; drop stale data
                    _ring.Write(chunk.AsSpan(0, n));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // restarted or stopped
        }
    }

    async ValueTask<int> ReadAtAsync(long offset, Memory<byte> dst, CancellationToken ct)
    {
        lock (_sync)
        {
            var n = _ring.Read(offset, dst.Span);
            if (n > 0)
            {
                _ring.TrimBefore(offset - KeepBehindBytes); // free space behind the playhead for more prefetch
                return n;
            }
        }
        // Outside the buffered window (seek): refill from the new position and serve this read directly.
        RestartPrefetch(offset);
        return await engine.ReadAsync(offset, dst, ct);
    }

    async Task ServeAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; } // listener stopped
            _ = HandleAsync(ctx, ct);
        }
    }

    async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var video = _video!;
        var res = ctx.Response;
        try
        {
            if (!ctx.Request.Url!.AbsolutePath.StartsWith($"/{_token}/", StringComparison.Ordinal))
            {
                res.StatusCode = 404;
                return;
            }

            long start = 0, end = video.Length - 1;
            var partial = false;
            if (ctx.Request.Headers["Range"] is { } range && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = range[6..].Split('-', 2);
                if (parts[0].Length > 0) start = long.Parse(parts[0]);
                if (parts.Length > 1 && parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), end);
                if (parts[0].Length == 0 && parts.Length > 1 && parts[1].Length > 0) { start = Math.Max(0, video.Length - long.Parse(parts[1])); end = video.Length - 1; }
                partial = true;
            }
            if (start > end || start >= video.Length)
            {
                res.StatusCode = 416;
                res.Headers["Content-Range"] = $"bytes */{video.Length}";
                return;
            }

            res.StatusCode = partial ? 206 : 200;
            res.ContentType = ContentTypeFor(video.Name);
            res.Headers["Accept-Ranges"] = "bytes";
            if (partial) res.Headers["Content-Range"] = $"bytes {start}-{end}/{video.Length}";
            res.ContentLength64 = end - start + 1;

            var buf = new byte[256 * 1024];
            for (var pos = start; pos <= end;)
            {
                var n = await ReadAtAsync(pos, buf.AsMemory(0, (int)Math.Min(buf.Length, end - pos + 1)), ct);
                if (n == 0) break;
                await res.OutputStream.WriteAsync(buf.AsMemory(0, n), ct);
                pos += n;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or OperationCanceledException or FormatException)
        {
            // Players abort connections when seeking; malformed Range headers just end the response.
        }
        finally
        {
            try { res.Close(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { /* already closed */ }
        }
    }

    static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream",
    };

    public async ValueTask DisposeAsync()
    {
        _serverCts?.Cancel();
        _prefetchCts?.Cancel();
        _listener?.Close();
        await engine.DisposeAsync();
    }
}
