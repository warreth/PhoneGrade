using System;
using System.Collections.Generic;
using System.Linq;

namespace PhoneGrade.Core;

/// <summary>
/// Grading rules applied on top of the grade the technician or the settings
/// picked. Keeping them here means the selection flow, the finishing flow and
/// the tests all read one definition instead of repeating the condition.
/// </summary>
public static class GradePolicy
{
    /// <summary>Prefix on the component checks raised by the PWA capability scanner.</summary>
    public const string MissingApiPrefix = "API Missing:";

    /// <summary>True when the device failed any check raised for a missing browser API.</summary>
    public static bool HasMissingBrowserApis(IEnumerable<ComponentStatus>? checks) =>
        checks is not null &&
        checks.Any(c => c.Status == ComponentStatusType.Failed &&
                        c.Name.StartsWith(MissingApiPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Applies the missing API penalty: a device that cannot run every mandatory
    /// browser API is capped at grade B, since grade A implies full function.
    /// Any other grade passes through untouched.
    /// </summary>
    public static string ApplyMissingApiPenalty(string? quality, IEnumerable<ComponentStatus>? checks) =>
        quality == "A" && HasMissingBrowserApis(checks) ? "B" : quality ?? "";
}
