namespace PhoneGrade.Core;

/// <summary>Represents the data retrieved from the connected device.</summary>
public class DeviceData
{
    public string Identifier { get; set; } = "NOID";       // serial number or IMEI
    public string BatteryHealth { get; set; } = "NOBATT";
    public string Color { get; set; } = "NOCOLOR";
    public string Storage { get; set; } = "NOSTORAGE";
    public string Model { get; set; } = "NOMODEL";
    public string Quality { get; set; } = "NOQUALITY";
    public string PayMethod { get; set; } = "NOPAY";
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

    // Component audit verification list
    public List<ComponentStatus> ComponentChecks { get; set; } = [];

    /// <summary>Interactive web test results recorded during mobile test session.</summary>
    public InteractiveTestSuiteResult? InteractiveTests { get; set; }

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
    Unknown
}

public class ComponentStatus
{
    public required string Name { get; init; }
    public string? SerialRead { get; init; }
    public string? SerialOriginal { get; init; }
    public ComponentStatusType Status { get; init; } = ComponentStatusType.Unknown;
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
