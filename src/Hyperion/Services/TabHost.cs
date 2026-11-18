using Hyperion.Core.Downloads;
using Hyperion.Core.Media;
using Hyperion.Core.Tabs;
using Hyperion.Core.Torrent;
using Hyperion.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hyperion.Services;

/// <summary>Owns the WebView2 controls for all tabs and wires per-tab services. ViewModels talk to this, never to controls.</summary>
public sealed class TabHost(
    VideoInjectionService video,
    TabSuspenderService suspender,
    DownloadManager downloads,
    TorrentStreamer? torrent = null)
{
    sealed class Entry(WebView2 view, BrowserTabViewModel tab, Action undo)
    {
        public WebView2 View { get; } = view;
        public BrowserTabViewModel Tab { get; } = tab;
        public Action Undo { get; } = undo;
    }

    readonly Dictionary<Guid, Entry> _tabs = [];
    Grid? _surface;
    CoreWebView2Environment? _env;
    bool _wired;

    public event Action? SuspensionChanged;
    public event Action<string>? NewTabRequested;

    public async Task InitializeAsync(Grid surface)
    {
        _surface = surface;
        _env = await BrowserEnvironmentFactory.CreateAsync();
        if (_wired) return;
        _wired = true;
        suspender.SuspendedChanged += (id, suspended) =>
        {
            if (_tabs.TryGetValue(id, out var e)) e.Tab.IsSuspended = suspended;
            SuspensionChanged?.Invoke();
        };
        suspender.Start();
    }

    public async Task OpenAsync(BrowserTabViewModel tab, string url)
    {
        var view = new WebView2 { Visibility = Visibility.Collapsed };
        _surface!.Children.Add(view);
        await view.EnsureCoreWebView2Async(_env);
        var core = view.CoreWebView2;

        EventHandler<object> title = (_, _) => tab.Title = core.DocumentTitle;
        EventHandler<CoreWebView2SourceChangedEventArgs> source = (_, _) => tab.Url = core.Source;
        EventHandler<object> favicon = (_, _) => tab.FaviconUri = core.FaviconUri;
        EventHandler<CoreWebView2NavigationStartingEventArgs> navStarting = (_, e) =>
        {
            if (!e.Uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            _ = StreamMagnetAsync(core, e.Uri);
        };
        EventHandler<CoreWebView2NavigationCompletedEventArgs> navDone = (_, e) =>
        {
            if (!e.IsSuccess) tab.Title = $"Error: {e.WebErrorStatus}";
        };
        EventHandler<CoreWebView2DownloadStartingEventArgs> download = (_, e) =>
        {
            // Hand the download to the parallel engine. Cookies/auth headers of the page are not forwarded (known limitation).
            var uri = new Uri(e.DownloadOperation.Uri);
            var path = e.ResultFilePath;
            e.Cancel = true;
            _ = RunDownloadAsync(uri, path);
        };
        EventHandler<CoreWebView2NewWindowRequestedEventArgs> newWindow = (_, e) =>
        {
            e.Handled = true;
            NewTabRequested?.Invoke(e.Uri);
        };
        EventHandler<CoreWebView2ProcessFailedEventArgs> failed = (_, e) =>
        {
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) core.Reload();
        };

        core.DocumentTitleChanged += title;
        core.SourceChanged += source;
        core.FaviconChanged += favicon;
        core.NavigationStarting += navStarting;
        core.NavigationCompleted += navDone;
        core.DownloadStarting += download;
        core.NewWindowRequested += newWindow;
        core.ProcessFailed += failed;
        await video.AttachAsync(core);

        _tabs[tab.Id] = new Entry(view, tab, () =>
        {
            core.DocumentTitleChanged -= title;
            core.SourceChanged -= source;
            core.FaviconChanged -= favicon;
            core.NavigationStarting -= navStarting;
            core.NavigationCompleted -= navDone;
            core.DownloadStarting -= download;
            core.NewWindowRequested -= newWindow;
            core.ProcessFailed -= failed;
            video.Detach(core);
        });

        suspender.Register(
            tab.Id,
            isPinned: () => tab.IsPinned,
            isPlayingMedia: () => core.IsDocumentPlayingAudio || video.IsPlaying(core),
            suspendAsync: async () => view.Visibility == Visibility.Collapsed && await core.TrySuspendAsync(),
            resume: core.Resume);

        core.Navigate(url);
    }

    public void Show(BrowserTabViewModel tab)
    {
        foreach (var (id, e) in _tabs)
        {
            var on = id == tab.Id;
            if (on) suspender.Activate(id);
            else if (e.View.Visibility == Visibility.Visible) suspender.Deactivate(id);
            e.View.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void Navigate(BrowserTabViewModel tab, string url)
    {
        if (_tabs.TryGetValue(tab.Id, out var e)) e.View.CoreWebView2.Navigate(url);
    }

    public void Close(BrowserTabViewModel tab)
    {
        if (!_tabs.Remove(tab.Id, out var e)) return;
        suspender.Unregister(tab.Id);
        e.Undo();
        _surface!.Children.Remove(e.View);
        e.View.Dispose();
    }

    async Task RunDownloadAsync(Uri uri, string path)
    {
        try
        {
            var file = await downloads.DownloadAsync(uri, path);
            Notifier.Show("Download complete", Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Notifier.Show("Download failed", $"{Path.GetFileName(path)}: {ex.Message}");
        }
    }

    async Task StreamMagnetAsync(CoreWebView2 core, string magnet)
    {
        if (torrent is null)
        {
            Notifier.Show("Torrent streaming unavailable", "No libtorrent engine is installed in this build.");
            return;
        }
        try
        {
            var stream = await torrent.StartAsync(magnet, CancellationToken.None);
            core.Navigate(stream.ToString());
        }
        catch (Exception ex)
        {
            // Fire-and-forget from an event handler: report every failure (including native load errors) to the user.
            Notifier.Show("Torrent stream failed", ex.Message);
        }
    }
}
