using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Hyperion.Views;

/// <summary>
/// HUD badge. WebView2 is HWND-hosted and draws over WPF content, so the badge is a click-through,
/// non-activating owned window positioned over the top-right corner of the video surface.
/// </summary>
public partial class MediaOsdBadge : Window
{
    const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    public MediaOsdBadge()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GwlExStyle, GetWindowLong(h, GwlExStyle) | WsExTransparent | WsExNoActivate | WsExToolWindow);
        };
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
    }

    public void Show(string text, FrameworkElement anchor)
    {
        Label.Text = text;
        var source = PresentationSource.FromVisual(anchor);
        if (source?.CompositionTarget is null) return;
        var topRight = source.CompositionTarget.TransformFromDevice.Transform(
            anchor.PointToScreen(new Point(anchor.ActualWidth, 0)));
        if (!IsVisible) Show();
        UpdateLayout();
        Left = topRight.X - ActualWidth - 16;
        Top = topRight.Y + 16;
        _hide.Stop();
        _hide.Start();
    }
}
