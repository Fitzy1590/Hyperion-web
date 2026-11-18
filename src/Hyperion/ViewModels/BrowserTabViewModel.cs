using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyperion.ViewModels;

public partial class BrowserTabViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle), nameof(Initial))]
    private string title = "New Tab";

    [ObservableProperty]
    private string url = "";

    [ObservableProperty]
    private string? faviconUri;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool isPinned;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool isSuspended;

    /// <summary>Letter shown behind the favicon, so icon-only tabs stay recognisable when the icon cannot be decoded.</summary>
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";

    public string DisplayTitle => (IsSuspended ? "💤 " : "") + (IsPinned ? "📌 " : "") + Title;
}
