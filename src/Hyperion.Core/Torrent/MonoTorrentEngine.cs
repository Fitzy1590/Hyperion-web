using MonoTorrent;
using MonoTorrent.Client;

namespace Hyperion.Core.Torrent;

/// <summary>
/// <see cref="ITorrentEngine"/> over MonoTorrent's streaming API. Its stream is seekable: reading at a position that
/// is not downloaded yet re-prioritises the pieces covering it, and the first and last pieces are buffered up front
/// (so MP4s with the index at the end start immediately). MonoTorrent allows one open stream per torrent, so reads
/// are serialised and the stream is repositioned when the caller jumps.
/// </summary>
public sealed class MonoTorrentEngine : ITorrentEngine
{
    readonly SemaphoreSlim _gate = new(1, 1);
    ClientEngine? _engine;
    TorrentManager? _manager;
    Stream? _stream;
    long _length;
    string? _workDir;

    public async Task<TorrentVideo> OpenAsync(string magnetOrTorrentPath, CancellationToken ct)
    {
        _workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hyperion", "torrents", Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(_workDir, "data");
        Directory.CreateDirectory(saveDir);

        var settings = new EngineSettingsBuilder { CacheDirectory = Path.Combine(_workDir, "cache") }.ToSettings();
        _engine = new ClientEngine(settings);

        _manager = magnetOrTorrentPath.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            ? await _engine.AddStreamingAsync(MagnetLink.Parse(magnetOrTorrentPath), saveDir)
            : await _engine.AddStreamingAsync(magnetOrTorrentPath, saveDir);
        await _manager.StartAsync();
        await _manager.WaitForMetadataAsync(ct); // a magnet link has no file list until peers supply it

        var files = _manager.Files;
        var index = TorrentHeuristics.PickVideoFile([.. files.Select(f => (f.Path, f.Length))]);
        if (index < 0) throw new InvalidOperationException("The torrent contains no files.");

        for (var i = 0; i < files.Count; i++)
            if (i != index) await _manager.SetFilePriorityAsync(files[i], Priority.DoNotDownload);

        var file = files[index];
        _length = file.Length;
        _stream = await _manager.StreamProvider!.CreateStreamAsync(file, true, ct); // prebuffers first and last piece
        return new TorrentVideo(Path.GetFileName(file.Path), _length);
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken ct)
    {
        if (offset >= _length) return 0;
        await _gate.WaitAsync(ct);
        try
        {
            if (_stream!.Position != offset) _stream.Seek(offset, SeekOrigin.Begin); // makes MonoTorrent fetch these pieces next
            return await _stream.ReadAsync(destination, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose(); // the stream must be closed before the torrent is stopped
        if (_engine is not null)
        {
            await _engine.StopAllAsync();
            _engine.Dispose();
        }
        if (_workDir is not null)
        {
            try { Directory.Delete(_workDir, recursive: true); }
            catch (IOException) { /* files may still be flushing; the cache is disposable */ }
            catch (UnauthorizedAccessException) { /* same */ }
        }
        _gate.Dispose();
    }
}
