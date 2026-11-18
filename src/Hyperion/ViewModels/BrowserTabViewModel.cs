using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyperion.ViewModels;

public partial class BrowserTabViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string title = "New Tab";

    [ObservableProperty]
    private string url = "";

    [ObservableProperty]
    private string? faviconUri;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool isPinned;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool isSuspended;

    public string DisplayTitle => (IsSuspended ? "💤 " : "") + (IsPinned ? "📌 " : "") + Title;
}
