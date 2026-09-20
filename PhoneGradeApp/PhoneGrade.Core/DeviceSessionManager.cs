using System;
using System.Collections.Concurrent;

namespace PhoneGrade.Core;

/// <summary>Represents the processing state of a device session.</summary>
public enum DeviceSessionState
{
    NotStarted,
    ReadingOrActive,
    Completed
}

/// <summary>Represents a session record for a device.</summary>
public record DeviceSession(string Udid, DateTime UpdatedAt, DeviceSessionState State);

/// <summary>
/// Manages device testing sessions by UDID/Serial.
/// Prevents automatic re-testing of the same device while in progress or once finished.
/// </summary>
public static class DeviceSessionManager
{
    private static readonly ConcurrentDictionary<string, DeviceSession> _sessions = new();

    /// <summary>Checks if a device has already completed testing.</summary>
    public static bool IsDeviceCompleted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return false;
        return _sessions.TryGetValue(udid, out var session) && session.State == DeviceSessionState.Completed;
    }

    /// <summary>Checks if a device is currently active or completed.</summary>
    public static bool HasStartedOrCompleted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return false;
        return _sessions.TryGetValue(udid, out var session) && session.State != DeviceSessionState.NotStarted;
    }

    /// <summary>Marks a device as currently active/being read.</summary>
    public static void MarkStarted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions[udid] = new DeviceSession(udid, DateTime.UtcNow, DeviceSessionState.ReadingOrActive);
    }

    /// <summary>Marks a device test session as completed.</summary>
    public static void MarkCompleted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions[udid] = new DeviceSession(udid, DateTime.UtcNow, DeviceSessionState.Completed);
        SystemEventLogger.Info(LogSource.Desktop, $"Marked device session as completed: {udid}", udid);
    }

    /// <summary>Resets the session for a device (e.g. on manual retest or device unplug).</summary>
    public static void ResetDevice(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        if (_sessions.TryRemove(udid, out _))
        {
            SystemEventLogger.Info(LogSource.Desktop, $"Reset device session for retesting: {udid}", udid);
        }
    }

    /// <summary>Clears all device sessions.</summary>
    public static void ClearAll() => _sessions.Clear();
}