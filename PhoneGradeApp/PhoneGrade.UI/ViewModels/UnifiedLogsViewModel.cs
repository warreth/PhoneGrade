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

namespace PhoneGrade.UI.ViewModels;

public class LogEventViewModel : ReactiveObject
{
    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public LogSource Source { get; }
    public string Message { get; }
    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    
    // Hex colors mapped in the View (via Converter or style)
    public string LevelColor => Level switch 
    {
        LogLevel.Error => "#F0564A", // DangerColor
        LogLevel.Warning => "#F5A623", // WarnColor
        LogLevel.Debug => "#9B9BA6", // TextDimColor
        _ => "#4F8CFF" // AccentColor
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

    // Filters
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

    // Actions
    public ReactiveCommand<Unit, Unit> ClearLogsCommand { get; }
    public ReactiveCommand<Unit, string> CopyLogsCommand { get; }

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
                    LogLevel.Error => err,
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

        ClearLogsCommand = ReactiveCommand.Create(() => _logSource.Clear());
        CopyLogsCommand = ReactiveCommand.Create(() => 
        {
            var sb = new StringBuilder();
            foreach (var log in _filteredLogs)
            {
                sb.AppendLine($"[{log.FormattedTime}] [{log.Level}] [{log.Source}] {log.Message}");
            }
            return sb.ToString();
        });

        // Subscribe to SystemEventLogger
        SystemEventLogger.LogEventEmitted += OnSystemLogEmitted;
    }

    private void OnSystemLogEmitted(object? sender, LogEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logSource.Add(new LogEventViewModel(e));
            // Auto-prune if too large to prevent memory leaks (keep last 5000)
            if (_logSource.Count > 5000)
            {
                _logSource.RemoveRange(0, 1000);
            }
        });
    }

    public void AddLog(LogEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logSource.Add(new LogEventViewModel(e));
        });
    }

    public void Dispose()
    {
        SystemEventLogger.LogEventEmitted -= OnSystemLogEmitted;
    }
}
