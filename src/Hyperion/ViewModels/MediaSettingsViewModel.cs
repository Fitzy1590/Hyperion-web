using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyperion.Core.Media;

namespace Hyperion.ViewModels;

public partial class ShortcutRow : ObservableObject
{
    public ShortcutRow(ShortcutRule r)
    {
        Action = r.Action;
        enabled = r.Enabled;
        mode = r.Mode;
        key = r.Key;
        value = r.Value;
    }

    public string Action { get; }
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string mode;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayKey))] private string key;
    [ObservableProperty] private double value;

    public string DisplayKey => Key.Length == 0 ? "Set key" : Key;
    public bool HasMode => Action == ShortcutAction.Speed;
    public bool HasValue => Action is ShortcutAction.Speed or ShortcutAction.FastForward or ShortcutAction.Seek;

    public string Label => Action switch
    {
        ShortcutAction.Speed => "Speed",
        ShortcutAction.FastForward => "Fast forward (hold)",
        ShortcutAction.Seek => "Seek (s)",
        ShortcutAction.FullScreen => "Full screen",
        ShortcutAction.FullWindow => "Full window",
        _ => Action,
    };

    public ShortcutRule ToRule() => new(Enabled, Action, Mode, Key, Value);
}

public partial class MediaSettingsViewModel : ObservableObject
{
    readonly GlobalSpeedEngine _engine;
    readonly VideoInjectionService _video;

    public MediaSettingsViewModel(GlobalSpeedEngine engine, VideoInjectionService video)
    {
        _engine = engine;
        _video = video;
        Load(engine.Settings);
    }

    [ObservableProperty] private ShortcutRow? capturing;
    [ObservableProperty] private string statusText = "";

    public ObservableCollection<ShortcutRow> Shortcuts { get; } = [];

    void Load(MediaSettings s)
    {
        Shortcuts.Clear();
        foreach (var r in s.Shortcuts) Shortcuts.Add(new(r));
    }

    [RelayCommand]
    private void Add(string action)
    {
        var (mode, value) = action switch
        {
            ShortcutAction.Speed => (MediaSettings.Add, 0.1),
            ShortcutAction.FastForward => (MediaSettings.Set, 3),
            ShortcutAction.Seek => (MediaSettings.Add, 10),
            _ => (MediaSettings.Add, 0),
        };
        var row = new ShortcutRow(new(true, action, mode, "", value));
        Shortcuts.Add(row);
        BeginCapture(row);
    }

    [RelayCommand]
    private void Remove(ShortcutRow row) => Shortcuts.Remove(row);

    [RelayCommand]
    private void ToggleMode(ShortcutRow row) =>
        row.Mode = row.Mode == MediaSettings.Add ? MediaSettings.Set : MediaSettings.Add;

    [RelayCommand]
    private void BeginCapture(ShortcutRow row)
    {
        Capturing = row;
        StatusText = $"Press a key for “{row.Label}” (Esc cancels)";
    }

    /// <summary>Called by the view with the pressed key; returns true when the key was consumed.</summary>
    public bool OnKeyCaptured(bool ctrl, bool alt, bool shift, bool meta, int virtualKey)
    {
        if (Capturing is not { } row) return false;
        if (virtualKey == 0x1B) { Capturing = null; StatusText = ""; return true; }
        if (KeyCodeMap.FromVirtualKey(virtualKey) is not { } code) return true; // modifier alone or unsupported key
        row.Key = KeyCodeMap.Combo(ctrl, alt, shift, meta, code);
        Capturing = null;
        StatusText = "";
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _engine.Update(new MediaSettings { Shortcuts = [.. Shortcuts.Select(r => r.ToRule())] });
        await _video.RefreshAsync();
        StatusText = "Saved. Reload open tabs to apply.";
    }

    [RelayCommand]
    private void Reset() => Load(new MediaSettings());
}
