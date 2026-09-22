using System;
using System.Collections.Concurrent;

namespace PhoneGrade.Core;

/// <summary>Represents the processing state of a device session.</summary>
public enum DeviceSessionState
{
    NotStarted,
    ReadingOrActive,
    DisconnectedMidTest,
    Completed
}

/// <summary>Represents a cached session record for a device with its hardware data.</summary>
public record DeviceSession(
    string Udid, 
    DateTime UpdatedAt, 
    DeviceSessionState State,
    DeviceData? Data = null,
    int SavedProgress = 0);

/// <summary>
/// Manages device testing sessions by UDID/Serial.
/// Preserves device state when unpinned or disconnected mid-test to resume on reconnect.
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

    /// <summary>Checks if a device has an active or saved session.</summary>
    public static bool HasStartedOrCompleted(string udid)
    {
        if (string.IsNullOrWhiteSpace(udid)) return false;
        return _sessions.TryGetValue(udid, out var session) && session.State != DeviceSessionState.NotStarted;
    }

    /// <summary>Marks a device as currently active/being read.</summary>
    public static void MarkStarted(string udid, DeviceData? data = null)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions[udid] = new DeviceSession(udid, DateTime.UtcNow, DeviceSessionState.ReadingOrActive, data);
    }

    /// <summary>Preserves session state on disconnect for resumption.</summary>
    public static void PreserveDisconnectedSession(string udid, DeviceData data, int progress)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions[udid] = new DeviceSession(udid, DateTime.UtcNow, DeviceSessionState.DisconnectedMidTest, data, progress);
        SystemEventLogger.Info(LogSource.Desktop, $"Sessie bewaard voor heraansluiting: {udid}", udid);
    }

    /// <summary>Tries to retrieve a preserved session for resumption.</summary>
    public static bool TryGetPreservedSession(string udid, out DeviceSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(udid)) return false;
        if (_sessions.TryGetValue(udid, out var s) && s.State == DeviceSessionState.DisconnectedMidTest)
        {
            session = s;
            return true;
        }
        return false;
    }

    /// <summary>Updates session hardware data with selected grade and invoice method.</summary>
    public static void UpdateSessionData(string udid, DeviceData data)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions.AddOrUpdate(udid, 
            u => new DeviceSession(u, DateTime.UtcNow, DeviceSessionState.ReadingOrActive, data),
            (u, existing) => new DeviceSession(u, DateTime.UtcNow, existing.State, data, existing.SavedProgress));
    }

    /// <summary>Marks a device test session as completed.</summary>
    public static void MarkCompleted(string udid, DeviceData? data = null)
    {
        if (string.IsNullOrWhiteSpace(udid)) return;
        _sessions[udid] = new DeviceSession(udid, DateTime.UtcNow, DeviceSessionState.Completed, data);
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
