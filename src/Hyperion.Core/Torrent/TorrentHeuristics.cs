namespace Hyperion.Core.Torrent;

/// <summary>Pure rules used by the libtorrent engine; kept here so they can be unit-tested without native code.</summary>
public static class TorrentHeuristics
{
    static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi"];

    /// <summary>Index of the largest video file, or the largest file overall if none looks like video; -1 if empty.</summary>
    public static int PickVideoFile(IReadOnlyList<(string Name, long Size)> files)
    {
        var best = -1;
        var bestVideo = -1;
        for (var i = 0; i < files.Count; i++)
        {
            if (best < 0 || files[i].Size > files[best].Size) best = i;
            var isVideo = VideoExtensions.Contains(Path.GetExtension(files[i].Name), StringComparer.OrdinalIgnoreCase);
            if (isVideo && (bestVideo < 0 || files[i].Size > files[bestVideo].Size)) bestVideo = i;
        }
        return bestVideo >= 0 ? bestVideo : best;
    }

    /// <summary>
    /// Bytes of a sequentially-downloaded file that are safe to read from disk. libtorrent-net exposes only
    /// completed-bytes per file (no per-piece map), and requests several pieces at once, so the downloaded
    /// prefix can have small holes near its end; hold back <paramref name="safetyMargin"/> until the file is complete.
    /// </summary>
    public static long ReadableBytes(long completedBytes, long fileLength, long safetyMargin) =>
        completedBytes >= fileLength ? fileLength : Math.Max(0, completedBytes - safetyMargin);
}
