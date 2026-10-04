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
using PhoneGrade.UI.Services;

namespace PhoneGrade.UI.ViewModels;

public class DiagnosticCheckViewModel : ReactiveObject
{
    private readonly DiagnosticCheckItem _item;
    private readonly Func<string, Task> _onFixRequested;

    public string Category => _item.Category;

    /// <summary>
    /// The category as the operator reads it. The value itself stays an
    /// identifier, because the scan decides from it whether iOS and Android
    /// are ready, and that decision has to hold whatever language is on.
    /// </summary>
    public string CategoryLabel => _item.Category switch
    {
        "iOS" => LocalizationManager.GetString("Diag_CategoryIOS"),
        "Android" => LocalizationManager.GetString("Diag_CategoryAndroid"),
        "Connection" => LocalizationManager.GetString("Diag_CategoryConnection"),
        "Service" => LocalizationManager.GetString("Diag_CategoryService"),
        "Hardware" => LocalizationManager.GetString("Diag_CategoryHardware"),
        _ => _item.Category
    };

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

    /// <summary>
    /// What the pill spells out, in the language in use.
    /// </summary>
    public string SeverityLabel => Severity switch
    {
        DiagnosticSeverity.Pass => LocalizationManager.GetString("Diag_SeverityPass"),
        DiagnosticSeverity.Warning => LocalizationManager.GetString("Diag_SeverityWarning"),
        DiagnosticSeverity.Fail => LocalizationManager.GetString("Diag_SeverityFail"),
        _ => LocalizationManager.GetString("Diag_SeverityInfo")
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

    /// <summary>
    /// Neither good nor bad, and the one severity with no colour of its own,
    /// which is why it has to ask for the neutral pill by name: the pill
    /// stylesheet paints the other three and leaves anything else bare.
    /// </summary>
    public bool IsInfo => Severity == DiagnosticSeverity.Info;

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
        set
        {
            this.RaiseAndSetIfChanged(ref _isRunning, value);
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            this.RaiseAndSetIfChanged(ref _isInstalling, value);
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    /// <summary>Whether the panel is working on something at this moment.</summary>
    public bool IsBusy => _isRunning || _isInstalling;

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

    private string _overallStatus = LocalizationManager.GetString("Troubleshoot_ClickToScan");
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

    private bool _hasResults;

    /// <summary>True once a scan has put something in <see cref="Checks"/>.</summary>
    public bool HasResults
    {
        get => _hasResults;
        set
        {
            this.RaiseAndSetIfChanged(ref _hasResults, value);
            this.RaisePropertyChanged(nameof(AwaitingFirstScan));
        }
    }

    /// <summary>The other side of <see cref="HasResults"/>, for what the panel
    /// shows before anything has been scanned.</summary>
    public bool AwaitingFirstScan => !_hasResults;

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
        InstallStatusText = LocalizationManager.GetString("Troubleshoot_Scanning");

        try
        {
            var report = await TroubleshootService.RunFullDiagnosticsAsync();
            await Dispatcher.UIThread.InvokeAsync(() => PublishReport(report));
        }
        catch (Exception ex)
        {
            OverallStatus = string.Format(
                LocalizationManager.GetString("Troubleshoot_ScanError"), ex.Message);
            SystemEventLogger.Error(LogSource.Diagnostic, $"Diagnostic scan error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            InstallStatusText = "";
        }
    }

    /// <summary>
    /// Puts a finished report on the panel: the rows, the line at the top, the
    /// note about what it found, and which of the buttons the operator now
    /// gets. The scan hands its report over through this and nothing else, so
    /// the panel can also be filled without one, which is the only way to
    /// photograph or test the state it lands in.
    /// </summary>
    public void PublishReport(TroubleshootReport report)
    {
        Localise(report);

        _rawReportText = report.ToFormattedText();
        OverallStatus = report.OverallStatus;

        Checks.Clear();
        foreach (var check in report.Checks)
        {
            Checks.Add(new DiagnosticCheckViewModel(check, ExecuteFixAsync));
        }

        HasFixableIssues = Checks.Any(c => c.IsFixable && c.Severity != DiagnosticSeverity.Pass);
        HasResults = Checks.Count > 0;
    }

    /// <summary>
    /// Puts the operator's language onto the report.
    ///
    /// The scan runs in Core, which cannot reach the language files, so it
    /// hands over a key with the values to fill in and the sentence is put
    /// together here. A key nobody defined leaves whatever the item already
    /// carries alone, so a wording mistake reads as English on a Dutch screen
    /// rather than as a key drawn in the row.
    /// </summary>
    private static void Localise(TroubleshootReport report)
    {
        report.OverallStatus = Wording(report.OverallStatusKey, report.OverallStatus, Array.Empty<string>());

        foreach (var check in report.Checks)
        {
            check.Title = Wording(check.TitleKey, check.Title, Array.Empty<string>());
            check.Message = Wording(check.MessageKey, check.Message, check.MessageArgs);

            if (check.ResolutionKey is not null)
            {
                check.Resolution = Wording(check.ResolutionKey, check.Resolution ?? "", check.ResolutionArgs);
            }
        }
    }

    private static string Wording(string? key, string alreadyThere, string[] args)
    {
        if (key is null)
        {
            return alreadyThere;
        }

        string template = LocalizationManager.GetString(key);
        if (template == key)
        {
            return alreadyThere;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }

    private async Task ExecuteFixAsync(string actionKey)
    {
        IsInstalling = true;
        InstallProgress = 0;
        InstallStatusText = LocalizationManager.GetString("Troubleshoot_FixStarting");

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
                InstallStatusText = LocalizationManager.GetString("Troubleshoot_FixApplied");
                await Task.Delay(1000);
                await RunDiagnosticsAsync();
            }
            else
            {
                InstallStatusText = LocalizationManager.GetString("Troubleshoot_FixManual");
            }
        }
        catch (Exception ex)
        {
            InstallStatusText = string.Format(
                LocalizationManager.GetString("Troubleshoot_FixError"), ex.Message);
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
            InstallStatusText = string.Format(
                LocalizationManager.GetString("Troubleshoot_Fixing"), item.Title);
            await ExecuteFixAsync(item.FixActionKey);
        }

        IsInstalling = false;
        await RunDiagnosticsAsync();
    }
}
