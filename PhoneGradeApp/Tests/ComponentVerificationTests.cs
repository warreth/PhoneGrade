using Xunit;
using PhoneGrade.Core;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class ComponentVerificationTests
{
    [Fact]
    public void ComponentVerificationResult_InitializesWithDefaults()
    {
        var result = new ComponentVerificationService.ComponentVerificationResult();
        Assert.Empty(result.ComponentChecks);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ComponentVerificationResult_CanAddWarnings()
    {
        var result = new ComponentVerificationService.ComponentVerificationResult
        {
            Warnings = 
            [
                "AST2: Display niet geverifieerd als origineel Apple onderdeel",
                "AST2: Batterij niet geverifieerd als origineel Apple onderdeel"
            ]
        };

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("Display", result.Warnings[0]);
        Assert.Contains("Batterij", result.Warnings[1]);
    }

    [Fact]
    public void ParseAST2Messages_DetectsDisplayMessage()
    {
        string diagnostics = "Important Display Message: Unable to verify this iPhone has a genuine Apple display";
        var messages = ComponentVerificationService.ParseAST2Messages(diagnostics);

        Assert.NotEmpty(messages);
        Assert.Contains("Important Display Message", messages);
    }

    [Fact]
    public void ParseAST2Messages_DetectsBatteryMessage()
    {
        string diagnostics = "Important Battery Message: Unable to verify this iPhone has a genuine Apple battery";
        var messages = ComponentVerificationService.ParseAST2Messages(diagnostics);

        Assert.NotEmpty(messages);
        Assert.Contains("Important Battery Message", messages);
    }

    [Fact]
    public void ParseAST2Messages_DetectsMultipleMessages()
    {
        string diagnostics = @"
            Important Display Message
            Important Battery Message
            Important Camera Message
        ";
        var messages = ComponentVerificationService.ParseAST2Messages(diagnostics);

        Assert.Equal(3, messages.Count);
        Assert.Contains("Important Display Message", messages);
        Assert.Contains("Important Battery Message", messages);
        Assert.Contains("Important Camera Message", messages);
    }

    [Fact]
    public void ParseAST2Messages_EmptyInput_ReturnsEmpty()
    {
        var messages = ComponentVerificationService.ParseAST2Messages("");
        Assert.Empty(messages);

        messages = ComponentVerificationService.ParseAST2Messages(null!);
        Assert.Empty(messages);
    }

    [Fact]
    public void ComponentStatus_MismatchScenario()
    {
        var check = new ComponentStatus
        {
            Name = "Batterij",
            SerialRead = "ABC123",
            SerialOriginal = "XYZ789",
            Status = ComponentStatusType.Mismatch
        };

        Assert.Equal(ComponentStatusType.Mismatch, check.Status);
        Assert.NotEqual(check.SerialRead, check.SerialOriginal);
    }

    [Fact]
    public void ComponentStatus_UntrustedScenario()
    {
        var check = new ComponentStatus
        {
            Name = "Scherm (LCM)",
            SerialRead = "ABC123",
            SerialOriginal = "ABC123",
            Status = ComponentStatusType.Untrusted
        };

        Assert.Equal(ComponentStatusType.Untrusted, check.Status);
        Assert.Equal(check.SerialRead, check.SerialOriginal);
    }
}
