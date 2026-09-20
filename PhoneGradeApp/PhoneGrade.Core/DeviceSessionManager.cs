using System;
using System.Collections.Concurrent;

namespace PhoneGrade.Core;

/// <summary>Represents a completed test session for a device.</summary>
public record DeviceSession(string Udid, DateTime CompletedAt, bool WasSuccessful);

/// <summary>
/// Manages device testing sessions by UDID/Serial.
/// Prevents automatic re-testing of the same device unless explicitly retested or replugged.
/// </summary>
public static class DeviceSessionManager
{
    private static readonly ConcurrentDictionary<string, DeviceSession> _completedSessions = new();

    /// <summary>Checks if a device has already completed testing in this session.</summary>
    public static bool IsDeviceCompleted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return false;
        return _completedSessions.ContainsKey(udid);
    }

    /// <summary>Marks a device test session as completed.</summary>
    public static void MarkCompleted(string udid, bool wasSuccessful = true)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _completedSessions[udid] = new DeviceSession(udid, DateTime.UtcNow, wasSuccessful);
        SystemEventLogger.Info(LogSource.Desktop, $"Marked device session as completed: {udid}", udid);
    }

    /// <summary>Resets the session for a device (e.g. on manual retest or device unplug).</summary>
    public static void ResetDevice(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        if (_completedSessions.TryRemove(udid, out _))
        {
            SystemEventLogger.Info(LogSource.Desktop, $"Reset device session for retesting: {udid}", udid);
        }
    }

    /// <summary>Clears all completed device sessions.</summary>
    public static void ClearAll() => _completedSessions.Clear();
}
