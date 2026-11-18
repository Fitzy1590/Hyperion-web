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

    public void Update(MediaSettings s)
    {
        Settings = s;
        s.Save();
    }

    /// <summary>JSON consumed by the injected script: step, presets, seek seconds and combo -> action map.</summary>
    public string BuildConfigJson()
    {
        var keys = new Dictionary<string, string>();
        foreach (var (action, combo) in Settings.Bindings) keys[combo] = action; // last binding wins on conflicts
        return JsonSerializer.Serialize(
            new { step = Settings.SpeedStep, presets = Settings.Presets, seeks = Settings.SeekSeconds, keys }, Camel);
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
