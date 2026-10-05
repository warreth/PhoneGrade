namespace PhoneGrade.Core;

/// <summary>
/// The faults worth printing on the label, decided in one place.
///
/// The label is a shop tag: it is read by whoever takes the phone next, and the
/// one thing they cannot do is open the report. So a fault has to fit on the
/// paper, which means it cannot be the sentence the report uses. Every fault is
/// reduced here to a code short enough to sit beside a grade letter, and the
/// reduction is done once so the .dymo file, the label PDF and the preview cannot
/// disagree about what is wrong with a phone.
///
/// Codes are grouped by what they are, because an operator reading a label counts
/// them: two codes of one kind become "2x NON-OEM" rather than two entries, and a
/// count is faster to read and shorter to fit than a list.
/// </summary>
public sealed record LabelFaults
{
    /// <summary>Nothing wrong that the label could say.</summary>
    public static LabelFaults None { get; } = new()
    {
        NotOriginal = [],
        FailedTests = [],
        Locks = [],
    };

    /// <summary>Component checks that did not match their factory serial.</summary>
    public required IReadOnlyList<string> NotOriginal { get; init; }

    /// <summary>Interactive tests that failed, by their own names.</summary>
    public required IReadOnlyList<string> FailedTests { get; init; }

    /// <summary>
    /// The locks and blocks that stop the phone working for the next owner.
    /// Empty for a phone that is free of them, which is the only thing that makes
    /// a phone easy to sell, so it is worth the label saying so by omission.
    /// </summary>
    public required IReadOnlyList<string> Locks { get; init; }

    /// <summary>True when nothing at all was found wrong.</summary>
    public bool IsClean => NotOriginal.Count == 0 && FailedTests.Count == 0 && Locks.Count == 0;

    /// <summary>
    /// Everything that is wrong, grouped and counted.
    ///
    /// Empty for a clean phone rather than the word "OK": a label with a gap in it
    /// reads as "nothing to report", which is what it means, and spends none of
    /// the paper saying so twice.
    /// </summary>
    public string Summary => string.Join(" ", Parts(includeLocks: true));

    /// <summary>
    /// The faults without the locks.
    ///
    /// A separate line from <see cref="LockLine"/> because the locks are set larger
    /// and sit lower on the label. Counting them twice would spend the room the
    /// larger setting needs and say the same thing in two places.
    /// </summary>
    public string FaultsOnly => string.Join(" ", Parts(includeLocks: false));

    private IEnumerable<string> Parts(bool includeLocks)
    {
        if (NotOriginal.Count > 0) yield return $"{NotOriginal.Count}x NON-OEM";
        if (FailedTests.Count > 0) yield return string.Join(" ", FailedTests.Select(FaultCodes.Short));
        if (includeLocks)
            foreach (string lockCode in Locks) yield return lockCode;
    }

    /// <summary>
    /// The locks on their own, so they can be given their own line.
    ///
    /// Separate because they are the one fault that costs a shop the sale: a
    /// FRP-locked phone that is activated by the next owner wipes itself, and the
    /// label is the last place that could have said so.
    /// </summary>
    public string LockLine => string.Join(" ", Locks);

    /// <summary>Whether the label has to carry the locks line at all.</summary>
    public bool HasLocks => Locks.Count > 0;
}

/// <summary>
/// Reads the faults off an inspection.
///
/// Every source the label can draw on, in one place, so that adding a fault is a
/// change to this class rather than to three renderers.
/// </summary>
public static class LabelFaultReader
{
    /// <summary>
    /// What the label says about one inspection.
    /// </summary>
    /// <param name="data">what the phone reported</param>
    /// <param name="tests">
    /// Whether the interactive results are included. They are a fault a shop acts
    /// on, but they are also the one thing that changes with a re-test, so a shop
    /// that only grades the hardware can leave them off the label.
    /// </param>
    /// <param name="locks">Whether the locks and blocks are printed.</param>
    public static LabelFaults From(DeviceData data, bool tests = true, bool locks = true)
    {
        var notOriginal = new List<string>();
        foreach (ComponentStatus component in data.ComponentChecks ?? [])
            if (component.Status is ComponentStatusType.Mismatch or ComponentStatusType.Untrusted)
                notOriginal.Add(component.Name);

        var failed = new List<string>();
        if (tests && data.InteractiveTests?.Tests is { } suite)
            foreach (InteractiveTestResult test in suite)
                if (test.Status == TestStatus.Failed) failed.Add(test.Name);

        var blockages = new List<string>();
        if (locks)
        {
            if (data.FactoryResetProtection == SecurityServices.FrpLockService.FrpLockStatus.Locked)
                blockages.Add(FaultCodes.Frp);
            if (data.ActivationLock == SecurityServices.ActivationLockService.ActivationLockStatus.Locked)
                blockages.Add(FaultCodes.ActivationLock);
            if (data.CarrierLockIOS?.IsCarrierLocked == true || data.CarrierLockAndroid?.IsCarrierLocked == true)
                blockages.Add(FaultCodes.CarrierLock);
            if (data.Blacklist?.IsBlacklisted == true) blockages.Add(FaultCodes.Blacklisted);
        }

        return new LabelFaults
        {
            NotOriginal = notOriginal,
            FailedTests = failed,
            Locks = blockages,
        };
    }
}

/// <summary>
/// The short codes a fault wears on the label.
///
/// A label has room for three characters of a word, not six. These are the codes,
/// and they are here rather than inline because the same word has to read the same
/// way on the .dymo file, on the PDF and on the preview, and three copies of a
/// spelling is three chances to spell it three ways.
/// </summary>
public static class FaultCodes
{
    public const string Frp = "FRP!";
    public const string ActivationLock = "ACT!";
    public const string CarrierLock = "CARRIER!";
    public const string Blacklisted = "BLACKLIST!";

    /// <summary>
    /// A failed test's own name, shortened to what fits.
    ///
    /// Upper case and clipped at six characters: the report has the full name, and
    /// a clipped code is still recognisable to the person who ran the test, while a
    /// full name is off the edge of the label and recognisable to nobody.
    /// </summary>
    public static string Short(string name)
    {
        string cleaned = new string(name
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToUpperInvariant)
            .ToArray());

        return cleaned.Length <= 6 ? cleaned : cleaned[..6];
    }
}
