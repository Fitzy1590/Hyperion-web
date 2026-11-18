using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyperion.Core.Navigation;
using Hyperion.Core.Tabs;
using Hyperion.Core.Updates;
using Hyperion.Services;

namespace Hyperion.ViewModels;

public partial class MainViewModel : ObservableObject
{
    readonly TabHost _host;
    readonly TabSuspenderService _suspender;
    readonly UpdateChecker _updates;
    ReleaseInfo? _pendingUpdate;

    public MainViewModel(TabHost host, TabSuspenderService suspender, UpdateChecker updates)
    {
        _host = host;
        _suspender = suspender;
        _updates = updates;
        _host.SuspensionChanged += () => SuspendedCount = Tabs.Count(t => t.IsSuspended);
        _host.NewTabRequested += url => _ = OpenTabAsync(url);
    }

    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];
    public int[] SuspendOptions => TabSuspenderService.AllowedMinutes;

    [ObservableProperty] private BrowserTabViewModel? selectedTab;
    [ObservableProperty] private string addressText = "";
    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private int suspendAfterMinutes = 15;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))]
    private int suspendedCount;

    public string StatusText => $"🧹 RAM optimizer: {SuspendedCount} idle tab(s) suspended";

    [ObservableProperty] private string updateText = "";
    [ObservableProperty] private bool updateAvailable;

    static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    public async Task StartAsync()
    {
        await OpenTabAsync(UrlNormalizer.Home);
        _ = CheckForUpdatesAsync(); // handles its own failures
    }

    async Task CheckForUpdatesAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (await _updates.CheckAsync(CurrentVersion, cts.Token) is not { } release) return;
            _pendingUpdate = release;
            UpdateText = $"⬆ Hyperion {release.Version.ToString(3)} is available";
            UpdateAvailable = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Offline, rate-limited or unexpected response: not worth interrupting the user; the next launch retries.
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is not { } release) return;
        try
        {
            UpdateText = "Downloading update…";
            var path = await _updates.DownloadAsync(release, Path.Combine(Path.GetTempPath(), "HyperionUpdate"));
            // Windows App Installer shows the update prompt and enforces the package signature.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            UpdateText = "Update ready: confirm in the installer window.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or System.ComponentModel.Win32Exception)
        {
            UpdateText = "Update failed: " + ex.Message;
        }
    }

    partial void OnSelectedTabChanged(BrowserTabViewModel? value)
    {
        if (value is null) return;
        _host.Show(value);
        AddressText = value.Url;
    }

    partial void OnSuspendAfterMinutesChanged(int value) => _suspender.SuspendAfter = TimeSpan.FromMinutes(value);

    public async Task<BrowserTabViewModel> OpenTabAsync(string url)
    {
        var tab = new BrowserTabViewModel { Url = url };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserTabViewModel.Url) && tab == SelectedTab) AddressText = tab.Url;
        };
        Tabs.Add(tab);
        await _host.OpenAsync(tab, url);
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Opens a magnet link or .torrent file (validated by the caller) in a new tab and starts streaming it.</summary>
    public async Task OpenExternalAsync(string source)
    {
        var tab = await OpenTabAsync(UrlNormalizer.Home);
        await _host.StreamAsync(tab, source);
    }

    [RelayCommand]
    private Task NewTabAsync() => OpenTabAsync(UrlNormalizer.Home);

    [RelayCommand]
    private void Navigate()
    {
        if (SelectedTab is { } t) _host.Navigate(t, UrlNormalizer.Normalize(AddressText));
    }

    [RelayCommand]
    private void GoHome()
    {
        if (SelectedTab is { } t) _host.Navigate(t, UrlNormalizer.Home);
    }

    [RelayCommand]
    private async Task CloseTabAsync(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        _host.Close(tab);
        Tabs.Remove(tab);
        SuspendedCount = Tabs.Count(t => t.IsSuspended);
        if (Tabs.Count == 0) await OpenTabAsync(UrlNormalizer.Home);
        else if (SelectedTab == tab || SelectedTab is null) SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    [RelayCommand]
    private void TogglePin(BrowserTabViewModel tab) => tab.IsPinned = !tab.IsPinned;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;
}
