using Microsoft.Toolkit.Uwp.Notifications;

namespace Hyperion.Services;

public static class Notifier
{
    public static void Show(string title, string text) =>
        new ToastContentBuilder().AddText(title).AddText(text).Show();
}
