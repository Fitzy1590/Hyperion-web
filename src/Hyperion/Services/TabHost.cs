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
    Func<TorrentStreamer>? torrentFactory = null) : IAsyncDisposable
{
    sealed class Entry(WebView2 view, BrowserTabViewModel tab, Action undo)
    {
        public WebView2 View { get; } = view;
        public BrowserTabViewModel Tab { get; } = tab;
        public Action Undo { get; } = undo;
    }

    readonly Dictionary<Guid, Entry> _tabs = [];
    readonly SemaphoreSlim _streamLock = new(1, 1);
    TorrentStreamer? _stream;
    string? _streamAuthority; // host:port of the page the stream owner tab should stay on: the magnet page while starting, the stream once serving
    Guid? _streamTabId; // the tab that owns the stream (set when it starts, so a pending start is covered too)
    CancellationTokenSource? _startCts;
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
            if (e.Uri.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = StreamTorrentAsync(tab, core, e.Uri);
            }
            else if (_streamTabId == tab.Id && !IsStreamPage(e.Uri))
            {
                _ = StopStreamAsync(); // the stream's tab is leaving the stream page
            }
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
        if (_streamTabId == tab.Id) _ = StopStreamAsync(); // closing the stream's tab disconnects the stream
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

    /// <summary>Streams a magnet link or .torrent path into an already-open tab (used for external activations).</summary>
    public Task StreamAsync(BrowserTabViewModel tab, string source) =>
        _tabs.TryGetValue(tab.Id, out var e) ? StreamTorrentAsync(tab, e.View.CoreWebView2, source) : Task.CompletedTask;

    /// <summary>Each torrent gets a fresh streamer; the previous stream is disposed and a still-starting older one is cancelled.</summary>
    async Task StreamTorrentAsync(BrowserTabViewModel tab, CoreWebView2 core, string magnet)
    {
        if (torrentFactory is null)
        {
            Notifier.Show("Torrent streaming unavailable", "No torrent engine is installed in this build.");
            return;
        }

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _startCts, cts)?.Cancel(); // supersede a magnet that is still fetching metadata
        cts.CancelAfter(TimeSpan.FromMinutes(2));
        _streamTabId = tab.Id;
        _streamAuthority = Uri.TryCreate(core.Source, UriKind.Absolute, out var page) ? page.Authority : null;

        await _streamLock.WaitAsync();
        try
        {
            if (_stream is { } old)
            {
                _stream = null;
                await old.DisposeAsync();
            }

            var next = torrentFactory();
            try
            {
                var url = await next.StartAsync(magnet, cts.Token);
                _stream = next;
                _streamAuthority = url.Authority;
                if (_streamTabId == tab.Id) core.Navigate(url.ToString()); // skip if the tab was closed meanwhile
            }
            catch
            {
                await next.DisposeAsync();
                throw;
            }
        }
        catch (OperationCanceledException) when (!ReferenceEquals(Volatile.Read(ref _startCts), cts))
        {
            // superseded by a newer magnet link; nothing to report
        }
        catch (OperationCanceledException)
        {
            Notifier.Show("Torrent stream timed out", "No peers supplied the torrent's file list within 2 minutes.");
        }
        catch (Exception ex)
        {
            // Fire-and-forget from an event handler: report every failure (including native load errors) to the user.
            Notifier.Show("Torrent stream failed", ex.Message);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    bool IsStreamPage(string url) =>
        _streamAuthority is not null && Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Authority == _streamAuthority;

    /// <summary>Abandons a pending start and disposes the stream: closes the HTTP server, peer connections and temp files.</summary>
    async Task StopStreamAsync()
    {
        Interlocked.Exchange(ref _startCts, null)?.Cancel();
        await _streamLock.WaitAsync();
        try
        {
            _streamTabId = null;
            _streamAuthority = null;
            if (_stream is { } s)
            {
                _stream = null;
                await s.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            Notifier.Show("Torrent stream cleanup failed", ex.Message);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _startCts, null)?.Cancel();
        if (_stream is { } s)
        {
            _stream = null;
            await s.DisposeAsync();
        }
    }
}
