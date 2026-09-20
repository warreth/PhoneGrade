using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using PhoneGrade.Core;

namespace PhoneGrade.UI.ViewModels;

public class DiagnosticCheckViewModel : ReactiveObject
{
    private readonly DiagnosticCheckItem _item;
    private readonly Func<string, Task> _onFixRequested;

    public string Category => _item.Category;
    public string Title => _item.Title;
    public DiagnosticSeverity Severity => _item.Severity;
    public string Message => _item.Message;
    public string? Resolution => _item.Resolution;
    public string? FixActionKey => _item.FixActionKey;
    public bool IsFixable => _item.IsFixable;

    public string SeverityColor => Severity switch
    {
        DiagnosticSeverity.Pass => "#2ECC71",    // Success Green
        DiagnosticSeverity.Warning => "#F39C12", // Warning Amber
        DiagnosticSeverity.Fail => "#E74C3C",    // Danger Red
        _ => "#3498DB"                           // Info Blue
    };

    public string SeverityLabel => Severity switch
    {
        DiagnosticSeverity.Pass => "OK",
        DiagnosticSeverity.Warning => "WARN",
        DiagnosticSeverity.Fail => "MISSING",
        _ => "INFO"
    };

    private bool _isFixing;
    public bool IsFixing
    {
        get => _isFixing;
        set => this.RaiseAndSetIfChanged(ref _isFixing, value);
    }

    public bool IsPass => Severity == DiagnosticSeverity.Pass;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;
    public bool IsFail => Severity == DiagnosticSeverity.Fail;

    public ReactiveCommand<Unit, Unit>? FixCommand { get; }

    public DiagnosticCheckViewModel(DiagnosticCheckItem item, Func<string, Task> onFixRequested)
    {
        _item = item;
        _onFixRequested = onFixRequested;

        if (IsFixable)
        {
            FixCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (FixActionKey == null) return;
                IsFixing = true;
                try
                {
                    await _onFixRequested(FixActionKey);
                }
                finally
                {
                    IsFixing = false;
                }
            });
        }
    }
}

public class TroubleshootViewModel : ReactiveObject
{
    public ObservableCollection<DiagnosticCheckViewModel> Checks { get; } = new();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        set => this.RaiseAndSetIfChanged(ref _isInstalling, value);
    }

    private int _installProgress;
    public int InstallProgress
    {
        get => _installProgress;
        set => this.RaiseAndSetIfChanged(ref _installProgress, value);
    }

    private string _installStatusText = "";
    public string InstallStatusText
    {
        get => _installStatusText;
        set => this.RaiseAndSetIfChanged(ref _installStatusText, value);
    }

    private string _overallStatus = "Click 'Run Diagnostics' to scan your system for USB tools, drivers, and connected devices.";
    public string OverallStatus
    {
        get => _overallStatus;
        set => this.RaiseAndSetIfChanged(ref _overallStatus, value);
    }

    private bool _hasFixableIssues;
    public bool HasFixableIssues
    {
        get => _hasFixableIssues;
        set => this.RaiseAndSetIfChanged(ref _hasFixableIssues, value);
    }

    private string _rawReportText = "";

    public ReactiveCommand<Unit, Unit> RunDiagnosticsCommand { get; }
    public ReactiveCommand<Unit, Unit> FixAllCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyReportCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportReportCommand { get; }

    public TroubleshootViewModel()
    {
        RunDiagnosticsCommand = ReactiveCommand.CreateFromTask(RunDiagnosticsAsync);
        FixAllCommand = ReactiveCommand.CreateFromTask(FixAllIssuesAsync);

        CopyReportCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var clipboard = desktop.MainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(_rawReportText);
                    SystemEventLogger.Info(LogSource.Desktop, "Diagnostic report copied to clipboard.");
                }
            }
        });

        ExportReportCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var exportPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"phonegrade-diagnostic-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");

                await System.IO.File.WriteAllTextAsync(exportPath, _rawReportText);
                SystemEventLogger.Info(LogSource.Desktop, $"Diagnostic report exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.Desktop, $"Failed to export diagnostic report: {ex.Message}");
            }
        });
    }

    public async Task RunDiagnosticsAsync()
    {
        IsRunning = true;
        InstallStatusText = "Scanning system hardware, tools, and background services...";

        try
        {
            var report = await TroubleshootService.RunFullDiagnosticsAsync();
            _rawReportText = report.ToFormattedText();
            OverallStatus = report.OverallStatus;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Checks.Clear();
                foreach (var check in report.Checks)
                {
                    Checks.Add(new DiagnosticCheckViewModel(check, ExecuteFixAsync));
                }
                HasFixableIssues = Checks.Any(c => c.IsFixable && c.Severity != DiagnosticSeverity.Pass);
            });
        }
        catch (Exception ex)
        {
            OverallStatus = $"Diagnostic scan error: {ex.Message}";
            SystemEventLogger.Error(LogSource.Diagnostic, $"Diagnostic scan error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            InstallStatusText = "";
        }
    }

    private async Task ExecuteFixAsync(string actionKey)
    {
        IsInstalling = true;
        InstallProgress = 0;
        InstallStatusText = "Starting fix...";

        var progress = new Progress<(int Percent, string Message)>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                InstallProgress = p.Percent;
                InstallStatusText = p.Message;
            });
        });

        try
        {
            bool success = await ToolInstallerService.ExecuteFixAsync(actionKey, progress);
            if (success)
            {
                InstallStatusText = "Fix applied successfully. Rescanning...";
                await Task.Delay(1000);
                await RunDiagnosticsAsync();
            }
            else
            {
                InstallStatusText = "Fix could not be completed automatically. See resolution steps.";
            }
        }
        catch (Exception ex)
        {
            InstallStatusText = $"Error applying fix: {ex.Message}";
            SystemEventLogger.Error(LogSource.Desktop, $"Error applying fix: {ex.Message}");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private async Task FixAllIssuesAsync()
    {
        var fixableItems = Checks.Where(c => c.IsFixable && c.Severity != DiagnosticSeverity.Pass).ToList();
        if (fixableItems.Count == 0) return;

        IsInstalling = true;

        foreach (var item in fixableItems)
        {
            if (item.FixActionKey == null) continue;
            InstallStatusText = $"Fixing: {item.Title}...";
            await ExecuteFixAsync(item.FixActionKey);
        }

        IsInstalling = false;
        await RunDiagnosticsAsync();
    }
}
