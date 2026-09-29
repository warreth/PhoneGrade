using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace PhoneGrade.UI.Web;

/// <summary>One test the phone has reported on, with the moment it reported it.</summary>
public sealed class PwaStepRecord
{
    [JsonPropertyName("testId")]
    public string TestId { get; set; } = "";

    [JsonPropertyName("testName")]
    public string TestName { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>
    /// When the phone finished the test, in UTC. This is what makes a replay
    /// distinguishable from a fresh run, see <see cref="PwaProgressStore"/>.
    /// </summary>
    [JsonPropertyName("reportedAt")]
    public DateTimeOffset ReportedAt { get; set; }
}

/// <summary>
/// What the phone is doing right now, for one device session.
///
/// The phone is the only place that knows this, and it can reload at any moment:
/// a locked screen, a browser that dropped the tab, a page opened from the home
/// screen. Without a copy on the desktop, every one of those restarts the suite
/// from the first step and the operator has to walk the phone through all of it
/// again.
/// </summary>
public sealed class PwaProgressSnapshot
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("started")]
    public bool Started { get; set; }

    [JsonPropertyName("finished")]
    public bool Finished { get; set; }

    [JsonPropertyName("currentTestId")]
    public string? CurrentTestId { get; set; }

    [JsonPropertyName("currentTestName")]
    public string? CurrentTestName { get; set; }

    /// <summary>Every step reported so far, in the order the phone reported them.</summary>
    [JsonPropertyName("steps")]
    public List<PwaStepRecord> Steps { get; set; } = new();

    [JsonPropertyName("totalTests")]
    public int TotalTests { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore]
    public int CompletedCount => Steps.Count;

    /// <summary>The step ids already settled, which is what a reload skips back over.</summary>
    public IReadOnlyDictionary<string, PwaStepRecord> ByTestId()
    {
        var map = new Dictionary<string, PwaStepRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps) map[step.TestId] = step;
        return map;
    }
}

/// <summary>
/// Server-side memory of how far a phone got, so a reload can pick up where it
/// left off.
///
/// A phone that loses its page keeps a local copy too, but localStorage is
/// per-browser and per-origin, and the origin changes when the operator switches
/// between the LAN address and the localhost tunnel. The desktop copy is the one
/// both sides can see, so it is also what the desktop shows while the operator
/// walks away from the phone.
///
/// A step is only accepted when it is newer than what is already stored for that
/// test. The phone queues results while it is offline and replays them on
/// reconnect, and without that check a replayed result from before a reload
/// would overwrite a newer one, so a step the operator had already retried would
/// silently go back to its earlier verdict.
/// </summary>
public sealed class PwaProgressStore
{
    private readonly ConcurrentDictionary<string, PwaProgressSnapshot> _bySession =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Current state for a session, or an empty snapshot if it is unknown.</summary>
    public PwaProgressSnapshot Get(string? sessionId)
    {
        string key = Normalize(sessionId);
        if (_bySession.TryGetValue(key, out var snapshot)) return snapshot;

        return new PwaProgressSnapshot { SessionId = key };
    }

    /// <summary>
    /// Records that a test has started. Returns false when a later step for the
    /// same test has already been recorded, which means this start belongs to a
    /// run the phone has moved on from.
    /// </summary>
    public bool RecordStart(string? sessionId, string? testId, string? testName, int totalTests)
    {
        string key = Normalize(sessionId);
        var snapshot = _bySession.GetOrAdd(key, k => new PwaProgressSnapshot { SessionId = k });

        lock (snapshot)
        {
            if (totalTests > 0) snapshot.TotalTests = totalTests;
            snapshot.Started = true;
            snapshot.Finished = false;
            snapshot.CurrentTestId = testId ?? "";
            snapshot.CurrentTestName = testName ?? "";
            snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return true;
    }

    /// <summary>
    /// Records a finished test. Returns false when the step is older than what is
    /// already stored for that test, which means it is a replay rather than news.
    /// </summary>
    public bool RecordStep(string? sessionId, PwaStepRecord step, int totalTests)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.TestId)) return false;

        string key = Normalize(sessionId);
        var snapshot = _bySession.GetOrAdd(key, k => new PwaProgressSnapshot { SessionId = k });

        lock (snapshot)
        {
            if (totalTests > 0) snapshot.TotalTests = totalTests;
            snapshot.Started = true;

            int existing = snapshot.Steps.FindIndex(s =>
                string.Equals(s.TestId, step.TestId, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                // Equal timestamps mean the same step arriving twice, which the
                // offline replay produces. Accepting it would be harmless, but
                // treating it as a fresh result is what would reorder the list.
                if (step.ReportedAt <= snapshot.Steps[existing].ReportedAt) return false;
                snapshot.Steps[existing] = step;
            }
            else
            {
                snapshot.Steps.Add(step);
            }

            snapshot.UpdatedAt = DateTimeOffset.UtcNow;

            // The current step is finished, so there is no longer one in flight.
            if (string.Equals(snapshot.CurrentTestId, step.TestId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CurrentTestId = null;
                snapshot.CurrentTestName = null;
            }

            return true;
        }
    }

    /// <summary>Marks the suite as done, unless a later result has already arrived.</summary>
    public bool RecordFinish(string? sessionId, DateTimeOffset reportedAt)
    {
        string key = Normalize(sessionId);
        var snapshot = _bySession.GetOrAdd(key, k => new PwaProgressSnapshot { SessionId = k });

        lock (snapshot)
        {
            // A suite_complete replayed from the offline queue arrives with the
            // moment it was first sent, so an older one must not reopen a suite
            // that has already been retired below.
            if (snapshot.Finished && reportedAt < snapshot.UpdatedAt) return false;

            snapshot.Finished = true;
            snapshot.Started = true;
            snapshot.CurrentTestId = null;
            snapshot.CurrentTestName = null;
            snapshot.UpdatedAt = reportedAt;
            return true;
        }
    }

    /// <summary>Clears a session, for when a different device is put on the bench.</summary>
    public void Reset(string? sessionId)
    {
        _bySession.TryRemove(Normalize(sessionId), out _);
    }

    /// <summary>Clears every session.</summary>
    public void Clear() => _bySession.Clear();

    private static string Normalize(string? sessionId)
        => string.IsNullOrWhiteSpace(sessionId) ? "UNKNOWN" : sessionId.Trim();
}
