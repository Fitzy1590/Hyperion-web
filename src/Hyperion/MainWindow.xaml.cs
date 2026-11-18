using System.Windows;
using System.Windows.Input;
using Hyperion.Core.Media;
using Hyperion.Services;
using Hyperion.ViewModels;
using Hyperion.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hyperion;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = App.Services.GetRequiredService<MainViewModel>();
    readonly MediaOsdBadge _osd = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Drawer.DataContext = App.Services.GetRequiredService<MediaSettingsViewModel>();
        App.Services.GetRequiredService<GlobalSpeedEngine>().OsdRequested += osd => _osd.Show(osd.Text, Surface);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsSettingsOpen) or nameof(MainViewModel.IsChromeVisible))
                Drawer.Visibility = _vm.IsSettingsOpen && _vm.IsChromeVisible ? Visibility.Visible : Visibility.Collapsed;
            if (e.PropertyName == nameof(MainViewModel.Immersive)) ApplyImmersive();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _vm.Immersive == ImmersiveMode.None) return;
            _vm.ExitImmersiveCommand.Execute(null); // fallback for when focus is in WPF; the page handles Esc itself
            e.Handled = true;
        };
        Loaded += async (_, _) =>
        {
            _osd.Owner = this; // WPF only allows an owner that has already been shown
            await App.Services.GetRequiredService<TabHost>().InitializeAsync(Surface);
            await _vm.StartAsync();
            ((App)Application.Current).SetExternalHandler(_vm.OpenExternalAsync); // also opens a link we were launched with
        };
    }

    (WindowStyle Style, ResizeMode Resize, WindowState State)? _normal; // window shape to restore after full screen / full window

    /// <summary>Full screen maximizes; full window keeps the size. Both drop the title bar and borders (the chrome rows hide via the ViewModel).</summary>
    void ApplyImmersive()
    {
        if (_vm.Immersive == ImmersiveMode.None)
        {
            if (_normal is not { } n) return;
            _normal = null;
            WindowStyle = n.Style;
            ResizeMode = n.Resize;
            WindowState = n.State;
            return;
        }
        _normal ??= (WindowStyle, ResizeMode, WindowState);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        if (_vm.Immersive == ImmersiveMode.FullScreen) WindowState = WindowState.Maximized;
    }

    void OnOmniBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTabViewModel tab) _vm.CloseTabCommand.Execute(tab);
    }

    void OnPinClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTabViewModel tab) _vm.TogglePinCommand.Execute(tab);
    }
}
