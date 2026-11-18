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
            if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
                Drawer.Visibility = _vm.IsSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        };
        Loaded += async (_, _) =>
        {
            _osd.Owner = this; // WPF only allows an owner that has already been shown
            await App.Services.GetRequiredService<TabHost>().InitializeAsync(Surface);
            await _vm.StartAsync();
            ((App)Application.Current).SetExternalHandler(_vm.OpenExternalAsync); // also opens a link we were launched with
        };
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
