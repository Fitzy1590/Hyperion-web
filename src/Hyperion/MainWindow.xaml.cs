using System.Windows;
using System.Windows.Input;
using Hyperion.Core.Media;
using Hyperion.Services;
using Hyperion.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hyperion;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = App.Services.GetRequiredService<MainViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Drawer.DataContext = App.Services.GetRequiredService<MediaSettingsViewModel>();
        App.Services.GetRequiredService<GlobalSpeedEngine>().OsdRequested += osd => Osd.Show(osd.Text);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
                Drawer.Visibility = _vm.IsSettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        };
        Loaded += async (_, _) =>
        {
            await App.Services.GetRequiredService<TabHost>().InitializeAsync(Surface);
            await _vm.StartAsync();
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
