using System.Net.Http;
using System.Windows;
using Hyperion.Core.Diagnostics;
using Hyperion.Core.Downloads;
using Hyperion.Core.Media;
using Hyperion.Core.Tabs;
using Hyperion.Core.Torrent;
using Hyperion.Core.Updates;
using Hyperion.Services;
using Hyperion.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hyperion;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    SingleInstance? _instance;
    string? _launchArg;                 // magnet/.torrent this process was started with
    readonly Queue<string> _pending = new(); // links that arrived before the main window was ready
    Func<string, Task>? _openExternal;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // the diagnostic window may close before the main one opens

        // Windows starts a new process per magnet:/.torrent activation: hand it to the running instance instead.
        _instance = new SingleInstance($"Hyperion.Browser.{Environment.UserName}");
        var launch = ExternalLaunch.AcceptFirst(e.Args);
        if (!_instance.IsFirst)
        {
            await _instance.SendToFirstAsync(launch ?? ""); // empty = just bring the window forward
            Shutdown();
            return;
        }
        _launchArg = launch;
        _instance.Listen(line => Dispatcher.InvokeAsync(() => HandleExternal(line)));

        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var bootstrap = new DiagnosticBootstrap(http);
        var results = await bootstrap.RunAsync();

        Services = new ServiceCollection()
            .AddSingleton(http)
            .AddSingleton(_ => MediaSettings.Load())
            .AddSingleton<GlobalSpeedEngine>()
            .AddSingleton<VideoInjectionService>()
            .AddSingleton<TabSuspenderService>()
            .AddSingleton<DownloadManager>()
            .AddSingleton(sp => new UpdateChecker(sp.GetRequiredService<HttpClient>()))
            .AddSingleton<Func<TorrentStreamer>>(_ => () => new TorrentStreamer(CreateTorrentEngine()))
            .AddSingleton<TabHost>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<MediaSettingsViewModel>()
            .BuildServiceProvider();

        if (results.Any(r => r.Status == CheckStatus.Failed))
        {
            var vm = new DiagnosticViewModel(bootstrap, results);
            var win = new DiagnosticWindow(vm);
            vm.Healthy += () => { win.Close(); ShowMain(); };
            win.ContinueAnywayRequested += () => { win.Close(); ShowMain(); };
            win.Show();
        }
        else
        {
            ShowMain();
        }
    }

    /// <summary>MonoTorrent by default (seek-aware streaming); set HYPERION_TORRENT_ENGINE=libtorrent for the libtorrent-net engine.</summary>
    static ITorrentEngine CreateTorrentEngine() =>
        string.Equals(Environment.GetEnvironmentVariable("HYPERION_TORRENT_ENGINE"), "libtorrent", StringComparison.OrdinalIgnoreCase)
            ? new LibtorrentEngine()
            : new MonoTorrentEngine();

    void HandleExternal(string line)
    {
        if (MainWindow is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        if (ExternalLaunch.Accept(line) is not { } source) return; // anything else is ignored
        if (_openExternal is { } open) _ = RunExternalAsync(open, source);
        else _pending.Enqueue(source);
    }

    /// <summary>Called by the main window once tabs can be created; opens anything that arrived earlier.</summary>
    public void SetExternalHandler(Func<string, Task> open)
    {
        _openExternal = open;
        if (_launchArg is { } first) { _launchArg = null; _pending.Enqueue(first); }
        while (_pending.TryDequeue(out var source)) _ = RunExternalAsync(open, source);
    }

    static async Task RunExternalAsync(Func<string, Task> open, string source)
    {
        try { await open(source); }
        catch (Exception ex) { Notifier.Show("Could not open the link", ex.Message); } // fire-and-forget: always tell the user
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    void ShowMain()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        MainWindow = main;
        main.Closed += async (_, _) => await ((IAsyncDisposable)Services).DisposeAsync(); // singletons include IAsyncDisposable
        main.Show();
    }
}
