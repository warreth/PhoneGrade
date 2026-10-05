namespace PhoneGrade.Core.SecurityServices;

/// <summary>
/// Integrity checks for Android devices.
///
/// Apple exposes the serial of every part and an AST2 validator to compare it
/// against the factory record. Android has no such interface, so a per-part
/// comparison is not possible on it. These checks cover what every Android
/// device does expose, and report the rest as unknown rather than as a match.
///
/// A part check is never invented: an Android handset reports none of its
/// component serials, so claiming "all original" would be a statement about
/// nothing.
/// </summary>
public static class AndroidIntegrityChecks
{
    /// <summary>Builds the component rows shown in the OEM audit panel.</summary>
    public static List<ComponentStatus> Build(AndroidDeviceFacts facts)
    {
        var checks = new List<ComponentStatus>
        {
            BuildLockState(facts),
            BuildVbmetaState(facts),
            BuildSystemImage(facts),
            BuildWarrantyBit(facts),
            BuildVerifiedBoot(facts),
        };

        return checks;
    }

    /// <summary>
    /// ro.boot.flash.locked is set by the bootloader itself, before Android runs,
    /// so it cannot be spoofed by software on the device. 0 means the bootloader
    /// was unlocked at some point, which is what rooting and custom ROMs require.
    /// </summary>
    private static ComponentStatus BuildLockState(AndroidDeviceFacts facts)
    {
        var check = new ComponentStatus
        {
            Name = "Bootloader",
            SerialRead = DescribeLockState(facts.FlashLocked),
        };

        switch (facts.FlashLocked.Trim())
        {
            case "1":
                check.Status = ComponentStatusType.Passed;
                check.Description = "Bootloader vergrendeld";
                check.Details = "De bootloader is vergrendeld, het systeem is ongewijzigd.";
                break;
            case "0":
                check.Status = ComponentStatusType.Failed;
                check.Description = "Bootloader ontgrendeld";
                check.Details = "De bootloader is ontgrendeld. Rooten of een custom ROM was mogelijk.";
                break;
            default:
                check.Status = ComponentStatusType.Unknown;
                check.Description = "Onbekend";
                check.Details = "Deze toestel meldt de bootloaderstatus niet.";
                break;
        }

        return check;
    }

    /// <summary>ro.boot.vbmeta.device_state, the AVB view on the same question.</summary>
    private static ComponentStatus BuildVbmetaState(AndroidDeviceFacts facts)
    {
        string state = facts.VbmetaDeviceState.Trim();
        string lowered = state.ToLowerInvariant();

        var check = new ComponentStatus { Name = "Vbmeta", SerialRead = state };

        if (lowered == "locked")
        {
            check.Status = ComponentStatusType.Passed;
            check.Description = "Vbmeta vergrendeld";
            check.Details = "De vbmeta-partitie is vergrendeld en door de fabrikant ondertekend.";
        }
        else if (lowered == "unlocked")
        {
            check.Status = ComponentStatusType.Failed;
            check.Description = "Vbmeta ontgrendeld";
            check.Details = "De vbmeta-partitie is ontgrendeld, de fabriekssleutel is vervangen.";
        }
        else
        {
            check.Status = ComponentStatusType.Unknown;
            check.Description = "Onbekend";
            check.Details = "Deze toestel meldt de vbmeta-status niet.";
        }

        return check;
    }

    /// <summary>
    /// The build fingerprint ends in the signing tag. A stock user build is
    /// "user/release-keys"; a rooted or custom system shows up as userdebug, eng,
    /// test-keys or a custom tag, because it is not signed by the manufacturer.
    /// </summary>
    private static ComponentStatus BuildSystemImage(AndroidDeviceFacts facts)
    {
        string fingerprint = facts.BuildFingerprint.Trim();
        var check = new ComponentStatus { Name = "Systeemimage", SerialRead = ShortFingerprint(fingerprint) };

        if (fingerprint.Length == 0)
        {
            check.Status = ComponentStatusType.Unknown;
            check.Description = "Onbekend";
            check.Details = "Deze toestel meldt geen buildfingerprint.";
            return check;
        }

        if (fingerprint.EndsWith(":user/release-keys", StringComparison.Ordinal))
        {
            check.Status = ComponentStatusType.Passed;
            check.Description = "Originele fabriekssoftware";
            check.Details = "De build is ondertekend met de fabriekssleutel van de fabrikant.";
            return check;
        }

        string tail = fingerprint[(fingerprint.LastIndexOf(':') + 1)..];
        check.Status = ComponentStatusType.Failed;
        check.Description = "Aangepast systeem";
        check.Details = $"De build is niet ondertekend met de fabriekssleutel ({tail}). Geroot of custom ROM.";
        return check;
    }

    /// <summary>
    /// Samsung and a few others keep a one-way fuse that trips when non-original
    /// parts are fitted. It cannot be reset, so it is the closest thing Android has
    /// to Apple's parts check, and it is only present on those devices.
    /// </summary>
    private static ComponentStatus BuildWarrantyBit(AndroidDeviceFacts facts)
    {
        string bit = facts.WarrantyBit.Trim();
        var check = new ComponentStatus { Name = "Warrantybit", SerialRead = bit.Length > 0 ? bit : "Niet aanwezig" };

        if (bit.Length == 0)
        {
            check.Status = ComponentStatusType.Unknown;
            check.Description = "Niet beschikbaar";
            check.Details = "Deze fabrikant publiceert geen warrantybit. Alleen Samsung en enkele anderen.";
        }
        else if (IsFactoryBit(bit))
        {
            check.Status = ComponentStatusType.Passed;
            check.Description = "Warrantybit intact";
            check.Details = "De fabrikantbit is niet geactiveerd.";
        }
        else
        {
            check.Status = ComponentStatusType.Failed;
            check.Description = "Warrantybit geactiveerd";
            check.Details = "De fabrikantbit staat aan: er zijn niet-Originele onderdelen gebruikt.";
        }

        return check;
    }

    /// <summary>
    /// True when the warranty bit reads as the all-zero value a factory phone
    /// carries.
    ///
    /// The spellings differ per manufacturer: "0x0" is the usual one, Samsung also
    /// writes "0x00", and a few builds print a bare zero. Matching one spelling
    /// exactly called a clean phone a repaired one, which is an accusation about
    /// parts that were never touched. Anything that is not a zero in every
    /// position means the fuse has tripped.
    /// </summary>
    private static bool IsFactoryBit(string bit)
    {
        string digits = bit.Trim();
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) digits = digits[2..];
        return digits.Length > 0 && digits.All(c => c == '0');
    }

    /// <summary>
    /// Verified boot state, reported as information only.
    ///
    /// It is deliberately not a verdict. A stock, locked and unmodified Pixel 8 Pro
    /// reports "yellow", so failing on anything other than a fully green state
    /// would mark a clean device as tampered. That is why the check never returns
    /// a failing status: an unlocked device is already caught by the two rows above.
    /// </summary>
    private static ComponentStatus BuildVerifiedBoot(AndroidDeviceFacts facts)
    {
        string state = facts.VerifiedBootState.Trim();
        var check = new ComponentStatus
        {
            Name = "Verified Boot",
            SerialRead = state.Length > 0 ? state : "Niet gerapporteerd",
            Status = ComponentStatusType.Unknown,
            Description = "Indicatie",
        };

        check.Details = state.ToLowerInvariant() switch
        {
            "green" => "Volledig geverifieerd volgens de fabriekssleutel.",
            "yellow" => "Vergrendeld, maar met een eigen sleutel. Komt ook voor op een schone toestel.",
            "orange" => "Bootloader ontgrendeld.",
            "red" => "Verificatie mislukt, mogelijk wijzigingen aan de systeemimage.",
            _ => "Deze toestel rapporteert geen verified boot state.",
        };

        return check;
    }

    private static string DescribeLockState(string raw) => raw.Trim() switch
    {
        "1" => "Vergrendeld",
        "0" => "Ontgrendeld",
        _ => "Onbekend",
    };

    /// <summary>Keeps the build id readable without printing the whole fingerprint.</summary>
    private static string ShortFingerprint(string fingerprint)
    {
        if (fingerprint.Length == 0) return "Onbekend";

        var parts = fingerprint.Split('/');
        return parts.Length >= 3 ? parts[1] : fingerprint;
    }
}
