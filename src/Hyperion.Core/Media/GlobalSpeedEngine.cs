using System.Globalization;
using System.Text.Json;

namespace Hyperion.Core.Media;

public sealed record MediaMessage(string Type, string? Kind, double Value);

public sealed record MediaOsd(string Text);

/// <summary>Holds media hotkey settings, builds the config for the injected script and turns script messages into OSD text.</summary>
public sealed class GlobalSpeedEngine(MediaSettings settings)
{
    static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    public MediaSettings Settings { get; private set; } = settings;
    public event Action<MediaOsd>? OsdRequested;
    public event Action<bool>? FullWindowChanged;

    public void Update(MediaSettings s)
    {
        Settings = s;
        s.Save();
    }

    /// <summary>JSON consumed by the injected script: the enabled shortcut rules (the script maps key -> rule, last one wins).</summary>
    public string BuildConfigJson()
    {
        var rules = Settings.Shortcuts
            .Where(r => r.Enabled && r.Key.Length > 0)
            .Select(r => new { action = r.Action, mode = r.Mode, key = r.Key, value = r.Value });
        return JsonSerializer.Serialize(new { rules }, Camel);
    }

    public static MediaMessage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("type", out var t)) return null;
            var kind = r.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var value = r.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble()
                : r.TryGetProperty("value", out var b) && b.ValueKind == JsonValueKind.True ? 1 : 0;
            return new MediaMessage(t.GetString() ?? "", kind, value);
        }
        catch (JsonException)
        {
            return null; // pages can post arbitrary messages; ignore anything that is not ours
        }
    }

    public MediaMessage? Handle(string json)
    {
        var m = Parse(json);
        if (m is { Type: "osd" } && FormatOsd(m) is { } text) OsdRequested?.Invoke(new MediaOsd(text));
        if (m is { Type: "fullwindow" }) FullWindowChanged?.Invoke(m.Value > 0);
        return m;
    }

    public static string? FormatOsd(MediaMessage m) => m.Kind switch
    {
        "speed" => $"⚡ {m.Value.ToString("0.0#", CultureInfo.InvariantCulture)}x Speed",
        "seek" when m.Value >= 0 => $"⏩ +{m.Value.ToString("0", CultureInfo.InvariantCulture)}s Seek",
        "seek" => $"⏪ -{Math.Abs(m.Value).ToString("0", CultureInfo.InvariantCulture)}s Seek",
        _ => null,
    };
}
