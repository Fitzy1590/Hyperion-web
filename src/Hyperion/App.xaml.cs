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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // the diagnostic window may close before the main one opens

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
            .AddSingleton(_ => new TorrentStreamer(CreateTorrentEngine()))
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

    void ShowMain()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        MainWindow = main;
        main.Closed += async (_, _) => await ((IAsyncDisposable)Services).DisposeAsync(); // singletons include IAsyncDisposable
        main.Show();
    }
}
