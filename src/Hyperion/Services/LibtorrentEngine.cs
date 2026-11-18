using Hyperion.Core.Torrent;
using ltnet;

namespace Hyperion.Services;

/// <summary>
/// <see cref="ITorrentEngine"/> over libtorrent-rasterbar 1.2 via the libtorrent-net wrapper (x64, mixed-mode C++/CLI).
/// The wrapper has no piece-level API (no piece deadlines, no have-piece map), so this engine downloads the chosen
/// file sequentially and serves reads from the partially written file on disk. Forward seeks beyond the
/// downloaded prefix wait for the download to catch up.
/// </summary>
public sealed class LibtorrentEngine : ITorrentEngine
{
    const long SafetyMarginBytes = 16 << 20;
    const int PieceGranularity = 1; // file_progress flag: count only completed pieces

    Session? _session;
    TorrentHandle? _handle;
    string? _saveDir;
    string? _videoPath;
    int _fileIndex;
    long _length;

    public async Task<TorrentVideo> OpenAsync(string magnetOrTorrentPath, CancellationToken ct)
    {
        _saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hyperion", "torrents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_saveDir);

        _session = new Session();
        using var atp = new AddTorrentParams { save_path = _saveDir };
        if (magnetOrTorrentPath.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            using var ec = new ErrorCode();
            new MagnetUri().parse_magnet_uri(magnetOrTorrentPath, atp, ec);
            if (ec.value() != 0) throw new InvalidOperationException("Invalid magnet link: " + ec.message());
        }
        else
        {
            atp.ti = new TorrentInfo(magnetOrTorrentPath);
        }

        _handle = _session.add_torrent(atp);
        _handle.set_sequential_download(true);

        while (true) // a magnet link has no file list until metadata arrives from peers
        {
            using var status = _handle.status();
            if (status.has_metadata) break;
            await Task.Delay(250, ct);
        }

        using var info = _handle.torrent_file();
        using var files = info.files();
        var list = new List<(string Name, long Size)>(files.num_files());
        for (var i = 0; i < files.num_files(); i++) list.Add((files.file_name(i), files.file_size(i)));

        _fileIndex = TorrentHeuristics.PickVideoFile(list);
        if (_fileIndex < 0) throw new InvalidOperationException("The torrent contains no files.");

        for (var i = 0; i < list.Count; i++) _handle.file_priority(i, i == _fileIndex ? 7 : 0); // skip everything else
        _length = list[_fileIndex].Size;
        _videoPath = files.file_path(_fileIndex, _saveDir);
        return new TorrentVideo(list[_fileIndex].Name, _length);
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken ct)
    {
        if (offset >= _length) return 0;
        while (true)
        {
            var readable = TorrentHeuristics.ReadableBytes(CompletedBytes(), _length, SafetyMarginBytes);
            if (offset < readable && File.Exists(_videoPath))
            {
                var want = (int)Math.Min(destination.Length, readable - offset);
                await using var fs = new FileStream(_videoPath!, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
                fs.Seek(offset, SeekOrigin.Begin);
                var n = await fs.ReadAsync(destination[..want], ct);
                if (n > 0) return n;
            }
            await Task.Delay(250, ct); // wait for the sequential download to reach this offset
        }
    }

    long CompletedBytes() => _handle!.file_progress(PieceGranularity)[_fileIndex];

    public ValueTask DisposeAsync()
    {
        if (_handle is not null) _session?.remove_torrent(_handle);
        _handle?.Dispose();
        _session?.Dispose();
        if (_saveDir is not null)
        {
            try { Directory.Delete(_saveDir, recursive: true); }
            catch (IOException) { /* files may still be locked by the native session; cache is disposable */ }
            catch (UnauthorizedAccessException) { /* same */ }
        }
        return ValueTask.CompletedTask;
    }
}
