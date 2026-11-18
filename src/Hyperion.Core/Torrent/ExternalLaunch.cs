namespace Hyperion.Core.Torrent;

/// <summary>Validates arguments handed to the app by Windows (magnet: protocol, .torrent file association) or another instance.</summary>
public static class ExternalLaunch
{
    const int MaxLength = 8192;

    /// <summary>The magnet link or full .torrent path if <paramref name="arg"/> is something we will open; otherwise null.</summary>
    public static string? Accept(string? arg)
    {
        var s = arg?.Trim().Trim('"');
        if (string.IsNullOrEmpty(s) || s.Length > MaxLength) return null;
        if (s.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) && s.Contains("xt=", StringComparison.OrdinalIgnoreCase))
            return s;
        if (s.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(s) && File.Exists(s))
            return s;
        return null;
    }

    public static string? AcceptFirst(IEnumerable<string> args) => args.Select(Accept).FirstOrDefault(a => a is not null);
}
