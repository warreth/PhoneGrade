using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PhoneGrade.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public enum LogSource
{
    Desktop,     
    UsbDetector, 
    WebSocket,   
    PwaClient,   
    Diagnostic,
    System
}

public class LogEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public LogSource Source { get; set; } = LogSource.Desktop;
    public string Message { get; set; } = "";
    public string? SessionId { get; set; } 
    public Dictionary<string, string>? Context { get; set; } 
}

public static class SystemEventLogger
{
    private static readonly object EventLock = new();
    private static readonly object FileLock = new();
    
    private const int MaxInMemoryLogs = 2000;
    private static readonly ConcurrentQueue<LogEvent> _logBuffer = new();
    
    private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10MB
    
    public static string LogDir =>
        Environment.GetEnvironmentVariable("AUTODYMO_LOG_DIR") is { Length: > 0 } customDir
            ? customDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneGrade", "logs");

    public static string LogFilePath => Path.Combine(LogDir, "phonegrade.log");
    public static string ToolRunnerLogFilePath => Path.Combine(LogDir, "toolrunner.log");

    public static event EventHandler<LogEvent>? LogEventEmitted;

    public static IReadOnlyList<LogEvent> GetRecentLogs()
    {
        return _logBuffer.ToArray();
    }

    public static void Log(LogLevel level, LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
    {
        var logEvent = new LogEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Source = source,
            Message = message,
            SessionId = sessionId,
            Context = context
        };

        // 1. Append to in-memory ring buffer
        _logBuffer.Enqueue(logEvent);
        while (_logBuffer.Count > MaxInMemoryLogs)
        {
            _logBuffer.TryDequeue(out _);
        }

        // 2. Append to physical log file on disk
        WriteToFile(logEvent);

        // 3. Emit real-time event to UI
        lock (EventLock)
        {
            try
            {
                LogEventEmitted?.Invoke(null, logEvent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemEventLogger subscriber error: {ex.Message}");
            }
        }
    }

    private static void WriteToFile(LogEvent logEvent)
    {
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(LogFilePath))
                {
                    var fileInfo = new FileInfo(LogFilePath);
                    if (fileInfo.Length > MaxLogSizeBytes)
                    {
                        string oldPath = Path.Combine(LogDir, "phonegrade.log.1");
                        if (File.Exists(oldPath)) File.Delete(oldPath);
                        File.Move(LogFilePath, oldPath);
                    }
                }
                
                string formatted = $"[{logEvent.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] [{logEvent.Level}] [{logEvent.Source}] {logEvent.Message}";
                if (logEvent.SessionId != null) formatted += $" (Session: {logEvent.SessionId})";
                
                File.AppendAllText(LogFilePath, formatted + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch 
        { 
            // Logging to disk failed (permissions/locked), ignore so app doesn't crash
        }
    }
    
    public static void ClearLogs()
    {
        while (_logBuffer.TryDequeue(out _)) { }
        try { lock (FileLock) { if (File.Exists(LogFilePath)) File.Delete(LogFilePath); } } catch { }
    }

    public static void Info(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
    {
        if (source == LogSource.UsbDetector && !LogThrottler.ShouldLog(message, source)) return;
        Log(LogLevel.Info, source, message, sessionId, context);
    }

    public static void Warning(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
    {
        if (source == LogSource.UsbDetector && !LogThrottler.ShouldLog(message, source)) return;
        Log(LogLevel.Warning, source, message, sessionId, context);
    }

    public static void Error(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
    {
        if (source == LogSource.UsbDetector && !LogThrottler.ShouldLog(message, source)) return;
        Log(LogLevel.Error, source, message, sessionId, context);
    }

    public static void Debug(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Debug, source, message, sessionId, context);
        
    public static void Critical(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Critical, source, message, sessionId, context);
}

// Map standard Microsoft.Extensions.Logging to our core Logger
public class PhoneGradeLoggerProvider : ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new PhoneGradeLogger(categoryName);
    }

    public void Dispose() { }
}

public class PhoneGradeLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly string _categoryName;
    private readonly LogSource _source;

    public PhoneGradeLogger(string categoryName)
    {
        _categoryName = categoryName;
        _source = DetermineSourceCategory(categoryName);
    }

    private static LogSource DetermineSourceCategory(string categoryName)
    {
        if (categoryName.Contains("UsbDetector", StringComparison.OrdinalIgnoreCase)) return LogSource.UsbDetector;
        if (categoryName.Contains("WebSocket", StringComparison.OrdinalIgnoreCase) || 
            categoryName.Contains("Kestrel", StringComparison.OrdinalIgnoreCase) ||
            categoryName.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)) return LogSource.WebSocket;
        if (categoryName.Contains("Pwa", StringComparison.OrdinalIgnoreCase)) return LogSource.PwaClient;
        if (categoryName.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)) return LogSource.Diagnostic;
        
        return LogSource.System;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        var message = formatter(state, exception);
        if (exception != null)
        {
            message += Environment.NewLine + exception.ToString();
        }

        var ourLevel = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Debug => LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => LogLevel.Info,
            Microsoft.Extensions.Logging.LogLevel.Warning => LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => LogLevel.Critical,
            _ => LogLevel.Info
        };

        // Only prefix category name if it's a raw Microsoft/System log that got mapped to System
        if (_source == LogSource.System && !string.IsNullOrWhiteSpace(_categoryName))
        {
            message = $"[{_categoryName}] {message}";
        }

        SystemEventLogger.Log(ourLevel, _source, message);
    }
}
