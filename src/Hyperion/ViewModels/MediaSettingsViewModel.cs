using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyperion.Core.Media;

namespace Hyperion.ViewModels;

public partial class ValueSlot : ObservableObject
{
    public ValueSlot(string label, double initial) { Label = label; value = initial; }
    public string Label { get; }
    [ObservableProperty] private double value;
}

public partial class HotkeyRow : ObservableObject
{
    public HotkeyRow(string action, string label, string initial) { Action = action; Label = label; combo = initial; }
    public string Action { get; }
    public string Label { get; }
    [ObservableProperty] private string combo;
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

    [ObservableProperty] private double speedStep;
    [ObservableProperty] private HotkeyRow? capturing;
    [ObservableProperty] private string statusText = "";

    public ObservableCollection<ValueSlot> Presets { get; } = [];
    public ObservableCollection<ValueSlot> SeekSeconds { get; } = [];
    public ObservableCollection<HotkeyRow> Hotkeys { get; } = [];

    void Load(MediaSettings s)
    {
        SpeedStep = s.SpeedStep;
        Presets.Clear();
        SeekSeconds.Clear();
        Hotkeys.Clear();
        for (var i = 0; i < s.Presets.Length; i++) Presets.Add(new($"Preset {i + 1} (x)", s.Presets[i]));
        for (var i = 0; i < s.SeekSeconds.Length; i++) SeekSeconds.Add(new($"Seek slot {i + 1} (s)", s.SeekSeconds[i]));

        void Row(string action, string label) =>
            Hotkeys.Add(new(action, label, s.Bindings.GetValueOrDefault(action, "")));
        Row(MediaSettings.SpeedUp, "Speed up");
        Row(MediaSettings.SpeedDown, "Speed down");
        for (var i = 1; i <= s.Presets.Length; i++) Row(MediaSettings.Preset + i, $"Preset {i}");
        for (var i = 1; i <= s.SeekSeconds.Length; i++) Row(MediaSettings.SeekForward + i, $"Seek forward, slot {i}");
        for (var i = 1; i <= s.SeekSeconds.Length; i++) Row(MediaSettings.SeekBack + i, $"Seek back, slot {i}");
    }

    [RelayCommand]
    private void BeginCapture(HotkeyRow row)
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
        row.Combo = KeyCodeMap.Combo(ctrl, alt, shift, meta, code);
        Capturing = null;
        StatusText = "";
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = new MediaSettings
        {
            SpeedStep = Math.Clamp(SpeedStep, 0.05, 1),
            Presets = [.. Presets.Select(p => Math.Clamp(p.Value, 0.1, 16))],
            SeekSeconds = [.. SeekSeconds.Select(p => (int)Math.Max(1, Math.Round(p.Value)))],
            Bindings = Hotkeys.Where(h => h.Combo.Length > 0).ToDictionary(h => h.Action, h => h.Combo),
        };
        _engine.Update(s);
        await _video.RefreshAsync();
        StatusText = "Saved. Reload open tabs to apply.";
    }

    [RelayCommand]
    private void Reset() => Load(new MediaSettings());
}
