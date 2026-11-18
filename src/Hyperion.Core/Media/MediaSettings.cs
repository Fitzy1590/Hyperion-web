using System.Text.Json;

namespace Hyperion.Core.Media;

public sealed class MediaSettings
{
    public const string SpeedUp = "speed.up";
    public const string SpeedDown = "speed.down";
    public const string Preset = "preset.";
    public const string SeekForward = "seek.fwd.";
    public const string SeekBack = "seek.back.";

    public double SpeedStep { get; set; } = 0.1;
    public double[] Presets { get; set; } = [1, 2, 3, 4, 5];
    public int[] SeekSeconds { get; set; } = [2, 5, 10, 30, 60];

    /// <summary>action id -> combo (see <see cref="KeyCodeMap.Combo"/>).</summary>
    public Dictionary<string, string> Bindings { get; set; } = DefaultBindings();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hyperion", "media-settings.json");

    public static Dictionary<string, string> DefaultBindings()
    {
        var b = new Dictionary<string, string> { [SpeedUp] = "BracketRight", [SpeedDown] = "BracketLeft" };
        for (var i = 1; i <= 5; i++)
        {
            b[Preset + i] = "Digit" + i;
            b[SeekForward + i] = "Alt+Digit" + i;
            b[SeekBack + i] = "Alt+Shift+Digit" + i;
        }
        return b;
    }

    public static MediaSettings Load(string? path = null)
    {
        try
        {
            var p = path ?? DefaultPath;
            return File.Exists(p)
                ? JsonSerializer.Deserialize<MediaSettings>(File.ReadAllText(p)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(); // unreadable/corrupt settings fall back to defaults
        }
    }

    public void Save(string? path = null)
    {
        var p = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
