using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hyperion.ViewModels;

namespace Hyperion.Views;

public partial class MediaSettingsOverlay : UserControl
{
    public MediaSettingsOverlay() => InitializeComponent();

    MediaSettingsViewModel? Vm => DataContext as MediaSettingsViewModel;

    void OnRemapClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutRow row })
        {
            Vm?.BeginCaptureCommand.Execute(row);
            Focus();
        }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var m = Keyboard.Modifiers;
        if (Vm.OnKeyCaptured(m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Alt),
                m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Windows), KeyInterop.VirtualKeyFromKey(key)))
            e.Handled = true;
    }
}
