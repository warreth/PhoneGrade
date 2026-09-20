using System;
using System.Reactive;
using System.Text;
using ReactiveUI;
using PhoneGrade.Core;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PhoneGrade.UI.ViewModels;

public class TroubleshootViewModel : ReactiveObject
{
    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => this.RaiseAndSetIfChanged(ref _isRunning, value); }

    private string _reportText = "Click 'Run Diagnostics' to scan your system for USB tools, drivers, and connected devices.";
    public string ReportText { get => _reportText; set => this.RaiseAndSetIfChanged(ref _reportText, value); }

    public ReactiveCommand<Unit, Unit> RunDiagnosticsCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyReportCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportReportCommand { get; }

    public TroubleshootViewModel()
    {
        RunDiagnosticsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsRunning = true;
            ReportText = "Running full hardware and driver diagnostics, please wait...";
            
            try
            {
                var report = await TroubleshootService.RunFullDiagnosticsAsync();
                ReportText = report.ToFormattedText();
            }
            catch (Exception ex)
            {
                ReportText = $"Diagnostic scan failed:\n{ex.Message}\n{ex.StackTrace}";
                SystemEventLogger.Error(LogSource.Diagnostic, $"Diagnostic scan exception: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        });

        CopyReportCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var clipboard = desktop.MainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(ReportText);
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
                    
                await System.IO.File.WriteAllTextAsync(exportPath, ReportText);
                SystemEventLogger.Info(LogSource.Desktop, $"Diagnostic report exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.Desktop, $"Failed to export diagnostic report: {ex.Message}");
            }
        });
    }
}
