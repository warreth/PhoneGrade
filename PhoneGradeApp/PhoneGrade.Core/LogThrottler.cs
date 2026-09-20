using System;
using System.Collections.Concurrent;

namespace PhoneGrade.Core;

/// <summary>
/// Throttles duplicate log messages by content hash within a configurable time window.
/// Prevents log spam from repeated polling errors and routine tool output.
/// </summary>
public static class LogThrottler
{
    private static readonly ConcurrentDictionary<string, DateTime> _lastLoggedAt = new();
    
    // Default throttle window for general logs
    private static readonly TimeSpan _defaultThrottleWindow = TimeSpan.FromSeconds(5);
    
    // Longer throttle window for noisy idle polling messages (e.g. "no device found", "List of devices attached")
    private static readonly TimeSpan _pollingSpamWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checks if a message should be logged based on throttle window.
    /// Returns true if the message should be logged (first occurrence or throttle window expired).
    /// </summary>
    public static bool ShouldLog(string message, LogSource source)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        string key = $"{source}:{message}";
        DateTime now = DateTime.UtcNow;

        TimeSpan window = IsIdlePollingMessage(message) ? _pollingSpamWindow : _defaultThrottleWindow;

        if (_lastLoggedAt.TryGetValue(key, out var lastTime))
        {
            if (now - lastTime < window)
            {
                // Still within throttle window, suppress
                return false;
            }
        }

        // Log it and update timestamp
        _lastLoggedAt[key] = now;
        return true;
    }

    private static bool IsIdlePollingMessage(string message)
    {
        return message.Contains("List of devices attached", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No device found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Critical USB tools", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("device list refresh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Clears all throttle state (useful for tests or explicit reset).</summary>
    public static void Reset() => _lastLoggedAt.Clear();
}
