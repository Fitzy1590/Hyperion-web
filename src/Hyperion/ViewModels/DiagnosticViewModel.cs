using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyperion.Core.Diagnostics;

namespace Hyperion.ViewModels;

public partial class DiagnosticViewModel : ObservableObject
{
    readonly DiagnosticBootstrap _bootstrap;
    IReadOnlyList<CheckResult> _results;

    public DiagnosticViewModel(DiagnosticBootstrap bootstrap, IReadOnlyList<CheckResult> results)
    {
        _bootstrap = bootstrap;
        _results = results;
        Rows = [.. results.Select(Row)];
    }

    public ObservableCollection<string> Rows { get; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AutoFixCommand))]
    private bool isBusy;

    [ObservableProperty] private string message = "Some components need attention.";

    /// <summary>Raised when no check is in the Failed state any more.</summary>
    public event Action? Healthy;

    static string Row(CheckResult r) =>
        $"{(r.Status switch { CheckStatus.Healthy => "✅", CheckStatus.Warning => "⚠️", _ => "❌" })}  {r.Name} - {r.Detail}";

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task AutoFixAsync()
    {
        IsBusy = true;
        Message = "Downloading and installing… accept the Windows prompt if it appears.";
        try
        {
            await _bootstrap.AutoFixAsync(_results);
            _results = await _bootstrap.RunAsync();
            Rows.Clear();
            foreach (var r in _results) Rows.Add(Row(r));
            if (_results.All(r => r.Status != CheckStatus.Failed)) Healthy?.Invoke();
            else Message = "Some problems remain. A restart may be required, or the item needs a manual fix.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or IOException)
        {
            Message = "Auto-fix failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    bool CanFix() => !IsBusy;
}
