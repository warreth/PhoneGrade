namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// Core workflow states for the kiosk single-window UI.
/// </summary>
public enum AppWorkflowState
{
    /// <summary>Waiting for a device to be connected via USB.</summary>
    Idle,

    /// <summary>Device connected: reading specs and running interactive tests with PWA QR code.</summary>
    Active,

    /// <summary>Testing complete: final grade, component audit, and label printing action.</summary>
    Summary
}
