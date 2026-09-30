namespace PhoneGrade.Core.Licensing;

/// <summary>What the licensing gate decides for one scan attempt.</summary>
public enum ScanAuthorization
{
    /// <summary>An active Pro license: the scan runs and the free count is not touched.</summary>
    AllowedPro,

    /// <summary>Free tier with scans left: the scan runs and consumes exactly one scan.</summary>
    AllowedTrial,

    /// <summary>Free tier exhausted without a valid license: the scan must not run.</summary>
    LimitReached
}
