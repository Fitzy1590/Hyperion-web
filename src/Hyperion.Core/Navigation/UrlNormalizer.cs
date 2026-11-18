namespace Hyperion.Core.Navigation;

public static class UrlNormalizer
{
    public const string Home = "https://start.me";

    public static string Normalize(string? input)
    {
        var s = input?.Trim();
        if (string.IsNullOrEmpty(s)) return Home;
        if (s.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return s;
        if (s.Contains("://") && Uri.TryCreate(s, UriKind.Absolute, out var abs)
            && abs.Scheme is "http" or "https" or "file")
            return abs.ToString();

        var host = s.Split('/', 2)[0];
        if (!s.Contains(' ') && (host.Contains('.') || host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            return (host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http://" : "https://") + s;

        return "https://www.google.com/search?q=" + Uri.EscapeDataString(s);
    }
}
