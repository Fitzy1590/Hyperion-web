using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Hyperion.Views;

public partial class MediaOsdBadge : UserControl
{
    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    public MediaOsdBadge()
    {
        InitializeComponent();
        _hide.Tick += (_, _) => { _hide.Stop(); Visibility = Visibility.Collapsed; };
    }

    public void Show(string text)
    {
        Label.Text = text;
        Visibility = Visibility.Visible;
        _hide.Stop();
        _hide.Start();
    }
}
