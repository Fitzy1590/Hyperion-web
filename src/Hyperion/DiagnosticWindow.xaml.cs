using System.Windows;
using Hyperion.ViewModels;

namespace Hyperion;

public partial class DiagnosticWindow : Window
{
    public event Action? ContinueAnywayRequested;

    public DiagnosticWindow(DiagnosticViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    void OnContinueClick(object sender, RoutedEventArgs e) => ContinueAnywayRequested?.Invoke();
}
