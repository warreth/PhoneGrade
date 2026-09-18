using System;
using System.Collections.Generic;

namespace PhoneGrade.Core;

/// <summary>
/// Log event levels matching standard logging conventions.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// Log event source categorization for filtering in the Logs tab.
/// </summary>
public enum LogSource
{
    Desktop,     // Desktop backend operations
    UsbDetector, // USB device detection and state changes
    WebSocket,   // WebSocket connection events
    PwaClient,   // PWA client-side logs
    Diagnostic   // Diagnostics and panic log analysis
}

/// <summary>
/// A single log event with timestamp, level, source, and context.
/// Streamed over WebSocket to the desktop Logs tab in real-time.
/// </summary>
public class LogEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public LogSource Source { get; set; } = LogSource.Desktop;
    public string Message { get; set; } = "";
    public string? SessionId { get; set; } // Optional: correlate with test session
    public Dictionary<string, string>? Context { get; set; } // Optional: device UDID, connection state, etc
}

/// <summary>
/// System-wide event logger that emits real-time events for USB detection,
/// daemon status, connection state changes, and diagnostics.
/// Complements ToolRunner.Log (disk file) with real-time UI updates.
/// Thread-safe event publisher.
/// </summary>
public static class SystemEventLogger
{
    private static readonly object EventLock = new();

    // Public events that subscribers (MainWindow, ViewModel) can listen to
    public static event EventHandler<LogEvent>? LogEventEmitted;

    /// <summary>
    /// Emit a log event to all subscribers (real-time UI updates).
    /// Thread-safe. Does not throw on subscriber errors.
    /// </summary>
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

        lock (EventLock)
        {
            try
            {
                LogEventEmitted?.Invoke(null, logEvent);
            }
            catch (Exception ex)
            {
                // Never crash the logger on subscriber exceptions
                System.Diagnostics.Debug.WriteLine($"SystemEventLogger subscriber error: {ex.Message}");
            }
        }
    }

    /// <summary>Convenience: Log at Info level from a source with optional context.</summary>
    public static void Info(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Info, source, message, sessionId, context);

    /// <summary>Convenience: Log at Warning level.</summary>
    public static void Warning(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Warning, source, message, sessionId, context);

    /// <summary>Convenience: Log at Error level.</summary>
    public static void Error(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Error, source, message, sessionId, context);

    /// <summary>Convenience: Log at Debug level.</summary>
    public static void Debug(LogSource source, string message, string? sessionId = null, Dictionary<string, string>? context = null)
        => Log(LogLevel.Debug, source, message, sessionId, context);
}
