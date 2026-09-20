using System;
using System.Collections.Concurrent;

namespace PhoneGrade.Core;

/// <summary>
/// Throttles duplicate log messages by content hash within a configurable time window.
/// Prevents log spam from repeated polling errors.
/// </summary>
public static class LogThrottler
{
    private static readonly ConcurrentDictionary<string, DateTime> _lastLoggedAt = new();
    private static readonly TimeSpan _throttleWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Checks if a message should be logged based on throttle window.
    /// Returns true if the message should be logged (first occurrence or throttle window expired).
    /// </summary>
    public static bool ShouldLog(string message, LogSource source)
    {
        string key = $"{source}:{message}";
        DateTime now = DateTime.UtcNow;

        if (_lastLoggedAt.TryGetValue(key, out var lastTime))
        {
            if (now - lastTime < _throttleWindow)
            {
                // Still within throttle window, suppress
                return false;
            }
        }

        // Log it and update timestamp
        _lastLoggedAt[key] = now;
        return true;
    }

    /// <summary>Clears all throttle state (useful for tests or explicit reset).</summary>
    public static void Reset() => _lastLoggedAt.Clear();
}
