using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using PhoneGrade.Core;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PhoneGrade.UI.ViewModels;

public class LogEventViewModel : ReactiveObject
{
    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public LogSource Source { get; }
    public string Message { get; }
    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    
    public string LevelColor => Level switch 
    {
        LogLevel.Critical => "#DC143C",
        LogLevel.Error => "#F0564A",
        LogLevel.Warning => "#F5A623",
        LogLevel.Debug => "#9B9BA6",
        _ => "#4F8CFF"
    };

    public LogEventViewModel(LogEvent e)
    {
        Timestamp = e.Timestamp;
        Level = e.Level;
        Source = e.Source;
        Message = e.Message;
    }
}

public class UnifiedLogsViewModel : ReactiveObject
{
    private readonly SourceList<LogEventViewModel> _logSource = new();
    private readonly ReadOnlyObservableCollection<LogEventViewModel> _filteredLogs;

    public ReadOnlyObservableCollection<LogEventViewModel> Logs => _filteredLogs;

    private bool _showDebug = true;
    public bool ShowDebug { get => _showDebug; set => this.RaiseAndSetIfChanged(ref _showDebug, value); }

    private bool _showInfo = true;
    public bool ShowInfo { get => _showInfo; set => this.RaiseAndSetIfChanged(ref _showInfo, value); }

    private bool _showWarning = true;
    public bool ShowWarning { get => _showWarning; set => this.RaiseAndSetIfChanged(ref _showWarning, value); }

    private bool _showError = true;
    public bool ShowError { get => _showError; set => this.RaiseAndSetIfChanged(ref _showError, value); }

    private string _searchQuery = "";
    public string SearchQuery { get => _searchQuery; set => this.RaiseAndSetIfChanged(ref _searchQuery, value); }

    public ReactiveCommand<Unit, Unit> ClearLogsCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLogsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportLogsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLogFolderCommand { get; }

    public UnifiedLogsViewModel()
    {
        var filterPredicate = this.WhenAnyValue(
            x => x.ShowDebug, x => x.ShowInfo, x => x.ShowWarning, x => x.ShowError, x => x.SearchQuery,
            (debug, info, warn, err, query) => (Func<LogEventViewModel, bool>)(log =>
            {
                bool levelMatch = log.Level switch
                {
                    LogLevel.Debug => debug,
                    LogLevel.Info => info,
                    LogLevel.Warning => warn,
                    LogLevel.Error or LogLevel.Critical => err,
                    _ => true
                };

                if (!levelMatch) return false;

                if (string.IsNullOrWhiteSpace(query)) return true;
                return log.Message.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                       log.Source.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
            }));

        _logSource.Connect()
            .Filter(filterPredicate)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _filteredLogs)
            .Subscribe();

        ClearLogsCommand = ReactiveCommand.Create(() => 
        {
            _logSource.Clear();
            SystemEventLogger.ClearLogs();
        });
        
        CopyLogsCommand = ReactiveCommand.CreateFromTask(async () => 
        {
            var sb = new StringBuilder();
            foreach (var log in _filteredLogs)
            {
                sb.AppendLine($"[{log.FormattedTime}] [{log.Level}] [{log.Source}] {log.Message}");
            }
            
            var text = sb.ToString();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var clipboard = desktop.MainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            }
        });

        ExportLogsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var exportPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"phonegrade-logs-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
                    
                var sb = new StringBuilder();
                foreach (var log in _logSource.Items)
                {
                    sb.AppendLine($"[{log.FormattedTime}] [{log.Level}] [{log.Source}] {log.Message}");
                }
                
                await System.IO.File.WriteAllTextAsync(exportPath, sb.ToString());
                SystemEventLogger.Info(LogSource.Desktop, $"Logs exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.Desktop, $"Failed to export logs: {ex.Message}");
            }
        });

        OpenLogFolderCommand = ReactiveCommand.Create(() =>
        {
            try
            {
                var logDir = SystemEventLogger.LogDir;
                System.IO.Directory.CreateDirectory(logDir);
                
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start("explorer", logDir);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", logDir);
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start("xdg-open", logDir);
                }
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.Desktop, $"Failed to open log folder: {ex.Message}");
            }
        });

        // Subscribe to real-time events
        SystemEventLogger.LogEventEmitted += OnSystemLogEmitted;
        
        // Replay existing logs from ring buffer
        var recentLogs = SystemEventLogger.GetRecentLogs();
        foreach (var logEvent in recentLogs)
        {
            _logSource.Add(new LogEventViewModel(logEvent));
        }
    }

    private void OnSystemLogEmitted(object? sender, LogEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logSource.Add(new LogEventViewModel(e));
            if (_logSource.Count > 5000)
            {
                _logSource.RemoveRange(0, 1000);
            }
        });
    }
}
