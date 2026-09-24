using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhoneGrade.Core;

/// <summary>Represents the data retrieved from the connected device.</summary>
public class DeviceData : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _identifier = "NOID";
    public string Identifier 
    { 
        get => _identifier; 
        set { if (_identifier != value) { _identifier = value; OnPropertyChanged(); } } 
    }

    private string _batteryHealth = "NOBATT";
    public string BatteryHealth 
    { 
        get => _batteryHealth; 
        set { if (_batteryHealth != value) { _batteryHealth = value; OnPropertyChanged(); } } 
    }

    private string _color = "NOCOLOR";
    public string Color 
    { 
        get => _color; 
        set { if (_color != value) { _color = value; OnPropertyChanged(); } } 
    }

    private string _storage = "NOSTORAGE";
    public string Storage 
    { 
        get => _storage; 
        set { if (_storage != value) { _storage = value; OnPropertyChanged(); } } 
    }

    private string _model = "NOMODEL";
    public string Model 
    { 
        get => _model; 
        set { if (_model != value) { _model = value; OnPropertyChanged(); } } 
    }

    private string _quality = "NOQUALITY";
    public string Quality 
    { 
        get => _quality; 
        set 
        { 
            if (_quality != value) 
            { 
                _quality = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SelectedGrade));
                OnPropertyChanged(nameof(GradeDisplay));
            } 
        } 
    }

    private string _payMethod = "NOPAY";
    public string PayMethod 
    { 
        get => _payMethod; 
        set 
        { 
            if (_payMethod != value) 
            { 
                _payMethod = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SelectedInvoiceMethod));
                OnPropertyChanged(nameof(InvoiceMethodDisplay));
            } 
        } 
    }

    /// <summary>Aliases for MVVM compatibility</summary>
    public string SelectedGrade
    {
        get => Quality;
        set => Quality = value;
    }

    public string GradeDisplay => Quality switch
    {
        "A" => "KLASSE A",
        "B" => "KLASSE B",
        "C" => "KLASSE C",
        "NOQUALITY" or "" => "Niet beoordeeld",
        _ => $"KLASSE {Quality}"
    };

    public string SelectedInvoiceMethod
    {
        get => PayMethod;
        set => PayMethod = value;
    }

    public string InvoiceMethodDisplay => PayMethod switch
    {
        "Marge" => "Marge (0% BTW)",
        "BTW" => "BTW (21%)",
        "NOPAY" or "" => "Niet opgegeven",
        _ => PayMethod
    };
    public string DeviceId { get; set; } = "NODEVICEID";
    public string ProductType { get; set; } = "";          // raw, e.g. iPhone14,2 — used by diagnostics
    public string? IosVersion { get; set; }

    // Extended battery metrics
    public int BatteryCycleCount { get; set; }
    public int BatteryDesignCapacity { get; set; }
    public int BatteryCurrentCapacity { get; set; }
    public string BatterySerialNumber { get; set; } = "";

    // Factory / original component serials
    public string OriginalBatterySerialNumber { get; set; } = "";
    public string DisplaySerialNumber { get; set; } = "";
    public string CoverGlassSerialNumber { get; set; } = "";
    public string FrontCameraSerialNumber { get; set; } = "";
    public string RearCameraSerialNumber { get; set; } = "";
    public string MotherboardSerialNumber { get; set; } = "";
    public string TouchIdFaceIdSerialNumber { get; set; } = "";
    public string BluetoothMacAddress { get; set; } = "";
    public string WifiMacAddress { get; set; } = "";
    public string CellularAddress { get; set; } = "";

    // FMI Verification Source
    public string FmiVerificationSource { get; set; } = "Lokaal"; // "Lokaal" of "Via Server (API)"

    // Component audit verification list
    public List<ComponentStatus> ComponentChecks { get; set; } = [];

    /// <summary>Interactive web test results recorded during mobile test session.</summary>
    public InteractiveTestSuiteResult? InteractiveTests { get; set; }

    // Security & Lock Status
    public SecurityServices.ActivationLockService.ActivationLockStatus? ActivationLock { get; set; }
    public SecurityServices.ActivationLockService.CarrierLockStatus? CarrierLockIOS { get; set; }
    public SecurityServices.FrpLockService.CarrierLockStatus? CarrierLockAndroid { get; set; }
    public SecurityServices.BlacklistCheckService.BlacklistStatus? Blacklist { get; set; }

    /// <summary>Merges interactive test results into diagnostic issue list.</summary>
    public List<DiagnosticIssue> MergeInteractiveResults(List<DiagnosticIssue>? existingIssues = null)
    {
        var issues = existingIssues != null ? new List<DiagnosticIssue>(existingIssues) : new List<DiagnosticIssue>();
        if (InteractiveTests == null) return issues;

        foreach (var test in InteractiveTests.Tests)
        {
            if (test.Status == TestStatus.Failed)
            {
                issues.Add(new DiagnosticIssue
                {
                    Title = $"Interactieve test gefaald: {test.Name}",
                    Explanation = string.IsNullOrWhiteSpace(test.Notes)
                        ? $"De {test.Name.ToLowerInvariant()} test is op het mobiele toestel als mislukt gemarkeerd."
                        : test.Notes,
                    Fix = $"Controleer hardwareonderdeel behorend bij {test.Name} (display, touch, audio, camera of sensoren).",
                    Level = Severity.Error
                });
            }
            else if (test.Status == TestStatus.Passed)
            {
                issues.Add(new DiagnosticIssue
                {
                    Title = $"Interactieve test geslaagd: {test.Name}",
                    Explanation = string.IsNullOrWhiteSpace(test.Notes)
                        ? $"De {test.Name.ToLowerInvariant()} test is succesvol doorlopen."
                        : test.Notes,
                    Fix = "Geen actie nodig.",
                    Level = Severity.Ok
                });
            }
            else if (test.Status == TestStatus.Skipped)
            {
                issues.Add(new DiagnosticIssue
                {
                    Title = $"Interactieve test overgeslagen: {test.Name}",
                    Explanation = "Deze test is handmatig of wegens ontbrekende sensor overgeslagen.",
                    Fix = "Optioneel handmatig hertesten.",
                    Level = Severity.Warning
                });
            }
        }

        return issues;
    }
}

/// <summary>Status of an interactive test module.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum TestStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped
}

/// <summary>A single interactive test result (e.g. Touch, Display, Audio).</summary>
public class InteractiveTestResult
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public TestStatus Status { get; set; } = TestStatus.Pending;

    [System.Text.Json.Serialization.JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("details")]
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>Payload transmitted when mobile runner completes all tests.</summary>
public class InteractiveTestSuiteResult
{
    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("deviceUdid")]
    public string DeviceUdid { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("allPassed")]
    public bool AllPassed => Tests.Count > 0 && Tests.All(t => t.Status == TestStatus.Passed);

    [System.Text.Json.Serialization.JsonPropertyName("tests")]
    public List<InteractiveTestResult> Tests { get; set; } = new();
}

/// <summary>Live message streamed over WebSocket during testing.</summary>
public class DeviceSessionMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "ping", "pong", "init", "test_start", "test_progress", "test_complete", "suite_complete"

    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("testId")]
    public string? TestId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("testName")]
    public string? TestName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public TestStatus? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("progress")]
    public double? Progress { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("payload")]
    public InteractiveTestSuiteResult? Payload { get; set; }
}

public enum ComponentStatusType
{
    Match,
    Mismatch,
    Untrusted,
    Unknown,
    Failed,
    Passed
}

public class ComponentStatus
{
    public required string Name { get; set; }

    public string? SerialRead { get; set; }

    public string? SerialOriginal { get; set; }

    /// <summary>
    /// Verification outcome. The PWA reports this field as "state", so the JSON
    /// name is pinned to keep the browser payload and the desktop model in sync.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ComponentStatusType Status { get; set; } = ComponentStatusType.Unknown;

    public string? Description { get; set; }

    public string? Details { get; set; }
}

/// <summary>A diagnostic finding for the device, e.g. from a panic log.</summary>
public class DiagnosticIssue
{
    public required string Title { get; init; }
    /// <summary>Plain-language explanation of what the finding means.</summary>
    public required string Explanation { get; init; }
    /// <summary>What to check / replace first.</summary>
    public required string Fix { get; init; }
    /// <summary>OK / Warning / Error. Errors block the auto flow; warnings show a hint.</summary>
    public Severity Level { get; init; } = Severity.Warning;
    /// <summary>Raw sensor identifier or hardware failure code if applicable.</summary>
    public string? SensorCode { get; init; }
}

public enum Severity { Ok, Warning, Error }
