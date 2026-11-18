using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyperion.Core.Navigation;
using Hyperion.Core.Tabs;
using Hyperion.Services;

namespace Hyperion.ViewModels;

public partial class MainViewModel : ObservableObject
{
    readonly TabHost _host;
    readonly TabSuspenderService _suspender;

    public MainViewModel(TabHost host, TabSuspenderService suspender)
    {
        _host = host;
        _suspender = suspender;
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

    public Task StartAsync() => OpenTabAsync(UrlNormalizer.Home);

    partial void OnSelectedTabChanged(BrowserTabViewModel? value)
    {
        if (value is null) return;
        _host.Show(value);
        AddressText = value.Url;
    }

    partial void OnSuspendAfterMinutesChanged(int value) => _suspender.SuspendAfter = TimeSpan.FromMinutes(value);

    public async Task OpenTabAsync(string url)
    {
        var tab = new BrowserTabViewModel { Url = url };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserTabViewModel.Url) && tab == SelectedTab) AddressText = tab.Url;
        };
        Tabs.Add(tab);
        await _host.OpenAsync(tab, url);
        SelectedTab = tab;
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
