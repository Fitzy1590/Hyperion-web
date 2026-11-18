using System.Text.Json;

namespace Hyperion.Core.Media;

public static class ShortcutAction
{
    public const string Speed = "speed";
    public const string FastForward = "fastforward";
    public const string Seek = "seek";
    public const string FullScreen = "fullscreen";
    public const string FullWindow = "fullwindow";
}

/// <summary>
/// One page shortcut. <paramref name="Mode"/> is "+" (add <paramref name="Value"/> to the current speed) or "=" (set it);
/// seek always adds <paramref name="Value"/> seconds, fast forward is "hold the key to play at <paramref name="Value"/>x".
/// <paramref name="Key"/> is a combo from <see cref="KeyCodeMap.Combo"/>.
/// </summary>
public sealed record ShortcutRule(bool Enabled, string Action, string Mode, string Key, double Value);

public sealed class MediaSettings
{
    public const string Add = "+";
    public const string Set = "=";

    public ShortcutRule[] Shortcuts { get; set; } = DefaultShortcuts();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hyperion", "media-settings.json");

    public static ShortcutRule[] DefaultShortcuts() =>
    [
        new(true, ShortcutAction.Speed, Add, "KeyS", -0.1),
        new(true, ShortcutAction.Speed, Set, "Digit4", 1.7),
        new(true, ShortcutAction.Speed, Add, "KeyW", 0.1),
        new(false, ShortcutAction.FastForward, Set, "KeyG", 3),
        new(true, ShortcutAction.Seek, Add, "KeyQ", -5),
        new(true, ShortcutAction.Seek, Add, "KeyE", 5),
        new(true, ShortcutAction.Seek, Add, "KeyA", -1),
        new(true, ShortcutAction.Seek, Add, "KeyD", 1),
        new(true, ShortcutAction.Seek, Add, "KeyZ", -60),
        new(true, ShortcutAction.Seek, Add, "KeyX", 60),
        new(true, ShortcutAction.Seek, Add, "Digit2", -10),
        new(true, ShortcutAction.Seek, Add, "Digit3", 10),
        new(true, ShortcutAction.Seek, Add, "KeyR", 25),
        new(true, ShortcutAction.FullScreen, Add, "KeyF", 0),
        new(true, ShortcutAction.FullWindow, Add, "Shift+KeyF", 0),
    ];

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
