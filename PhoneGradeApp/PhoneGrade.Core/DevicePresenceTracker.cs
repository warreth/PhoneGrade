using System;

namespace PhoneGrade.Core;

/// <summary>
/// Decides when a device has really been unplugged.
/// <para>
/// The device list is polled every couple of seconds, and it comes back empty
/// far more often than phones are actually pulled out of the cable: adb
/// restarts, lockdownd stalls, the phone re-enumerates while charging, a
/// dongle is unplugged. Tearing down an inspection on the first empty list
/// throws away the whole session over a hiccup, so an empty list has to hold
/// for <see cref="DefaultGrace"/> before it counts as a disconnect.
/// </para>
/// </summary>
public sealed class DevicePresenceTracker
{
    /// <summary>Three polling intervals, so a hiccup that clears within a poll or two is ignored.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(7.5);

    private readonly TimeSpan _grace;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _absentSince;

    public DevicePresenceTracker(TimeSpan grace, Func<DateTimeOffset> clock)
    {
        if (grace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(grace));
        _grace = grace;
        _clock = clock;
    }

    public DevicePresenceTracker() : this(DefaultGrace, () => DateTimeOffset.UtcNow) { }

    /// <summary>How long the device has been absent, or null while one is connected.</summary>
    public TimeSpan? AbsentFor => _absentSince is { } since ? _clock() - since : null;

    /// <summary>True once the empty device list has lasted longer than the grace period.</summary>
    public bool IsUnplugged => _absentSince is { } since && _clock() - since >= _grace;

    /// <summary>
    /// Feeds a refresh result in. Repeating the same observation is harmless,
    /// which matters because the polling loop and the USB event both report.
    /// </summary>
    public void Report(int deviceCount)
    {
        if (deviceCount > 0) _absentSince = null;
        else _absentSince ??= _clock();
    }

    /// <summary>Forgets any absence, for use when a session starts deliberately.</summary>
    public void Reset() => _absentSince = null;
}
