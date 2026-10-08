using System;
using System.Collections.Generic;
using PhoneGrade.Core.Licensing;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the machine identity that activation is bound to.
///
/// The value decides whether a seat can be found again on the next launch, so the
/// faults worth testing are not "does it return something" but "is it the same
/// thing twice" and "is it nothing when the machine cannot be identified". An
/// identity that changes between two runs locks a paying customer out of their own
/// shop, and an empty one that still activates turns the seat limit off.
///
/// The platform reads are injected, so the macOS and Linux branches are exercised
/// from a Windows machine without pretending to be another operating system: what
/// is under test is which source is preferred and what its value becomes, which is
/// the same question on every platform. The real read is covered separately, by
/// asking this machine twice.
/// </summary>
public class MachineFingerprintTests
{
    private const string RegistryGuid = "cd89566b-242d-427a-9ec1-95ad5e93a8ad";
    private const string RegistryGuidAsSent = "pg-cd89566b-242d-427a-9ec1-95ad5e93a8ad";
    // Written without dashes, the way /etc/machine-id writes it, and expected
    // back in the canonical UUID shape: the same machine must never reach the
    // vendor as two different strings depending on which file was read.
    private const string MachineId = "0123456789abcdef0123456789abcdef";
    private const string MachineIdAsSent = "pg-01234567-89ab-cdef-0123-456789abcdef";

    // ---- which source is used ------------------------------------------------------

    [Fact]
    public void Resolve_OnWindows_UsesTheRegistryGuidAndPrefixesIt()
    {
        var source = new MachineIdentitySource { WindowsMachineGuid = () => RegistryGuid };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.WindowsMachineGuid);

        Assert.True(fingerprint.IsAvailable);
        Assert.Equal(RegistryGuidAsSent, fingerprint.Value);
        Assert.Equal(MachineFingerprint.Source.WindowsMachineGuid, fingerprint.Origin);
    }

    [Fact]
    public void Resolve_OnMacOS_UsesThePlatformUuid()
    {
        var source = new MachineIdentitySource { MacPlatformUuid = () => RegistryGuid };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.MacPlatformUuid);

        Assert.True(fingerprint.IsAvailable);
        Assert.Equal(RegistryGuidAsSent, fingerprint.Value);
        Assert.Equal(MachineFingerprint.Source.MacPlatformUuid, fingerprint.Origin);
    }

    [Fact]
    public void Resolve_OnLinux_PrefersEtcMachineIdOverTheDbusOne()
    {
        // Both files exist on most Linux machines. Picking a different one per
        // launch would look like a new machine every time, so the order is fixed.
        var source = new MachineIdentitySource
        {
            LinuxMachineId = () => MachineId,
            DbusMachineId = () => RegistryGuid.Replace("-", "")
        };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.LinuxMachineId);

        Assert.Equal(MachineIdAsSent, fingerprint.Value);
        Assert.Equal(MachineFingerprint.Source.LinuxMachineId, fingerprint.Origin);
    }

    [Fact]
    public void Resolve_OnLinux_FallsBackToDbusWhenTheEtcFileSaysNothing()
    {
        var source = new MachineIdentitySource
        {
            LinuxMachineId = () => null,
            DbusMachineId = () => MachineId
        };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.LinuxMachineId);

        Assert.True(fingerprint.IsAvailable);
        Assert.Equal(MachineIdAsSent, fingerprint.Value);
        Assert.Equal(MachineFingerprint.Source.DbusMachineId, fingerprint.Origin);
    }

    [Fact]
    public void Resolve_WithNothingReadable_SaysUnavailableRatherThanEmpty()
    {
        // The distinction is the whole point: an empty fingerprint that still
        // activated would let anybody bypass the seat limit by breaking the read.
        var source = new MachineIdentitySource
        {
            WindowsMachineGuid = () => null,
            MacPlatformUuid = () => null,
            LinuxMachineId = () => null,
            DbusMachineId = () => null
        };

        foreach (MachineFingerprint.Source platform in new[]
        {
            MachineFingerprint.Source.WindowsMachineGuid,
            MachineFingerprint.Source.MacPlatformUuid,
            MachineFingerprint.Source.LinuxMachineId
        })
        {
            MachineFingerprint fingerprint = MachineFingerprint.Resolve(source, platform);

            Assert.False(fingerprint.IsAvailable);
            Assert.Equal("", fingerprint.Value);
            Assert.Equal(MachineFingerprint.Source.None, fingerprint.Origin);
        }
    }

    // ---- what is not allowed to become an identity -----------------------------------

    [Theory]
    [InlineData("WORKSTATION-07")]                       // a computer name
    [InlineData("shop-floor-bench-3")]                   // a name a shop would choose
    [InlineData("00-1B-63-84-45-E6")]                   // a network adapter address
    [InlineData("Samsung SSD 990 PRO")]                 // a disk description
    [InlineData("W1234567890")]                         // a Dell service tag
    [InlineData("abcd")]                                // far too short to be an id
    [InlineData("0123456789abcdef0123456789abcde")]      // one character short
    [InlineData("0123456789abcdef0123456789abcdef0")]    // one character long
    [InlineData("0123456789abcdef0123456789abcdeZ")]     // one character is not hex
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_AValueThatIsNotA32CharacterHexId_RefusesIt(string? candidate)
    {
        // A computer name can be all hex characters by accident, so the rule is
        // narrow on purpose: only a full 32 character hex id is an identity.
        // The dbus fallback is stubbed out as well, or a Linux runner with a
        // real /var/lib/dbus/machine-id would answer for a candidate that was
        // supposed to be refused, and the test would pass on Windows only.
        var source = new MachineIdentitySource
        {
            LinuxMachineId = () => candidate,
            DbusMachineId = () => null
        };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.LinuxMachineId);

        Assert.False(fingerprint.IsAvailable);
        Assert.Equal("", fingerprint.Value);
    }

    [Fact]
    public void Resolve_OnWindows_IgnoresTheSourcesThatDoNotBelongToWindows()
    {
        // Each platform reads only its own identifier. Reading two and preferring
        // one would mean the answer depends on which files happen to be present.
        var source = new MachineIdentitySource
        {
            WindowsMachineGuid = () => null,
            LinuxMachineId = () => MachineId,
            DbusMachineId = () => MachineId
        };

        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.WindowsMachineGuid);

        Assert.False(fingerprint.IsAvailable);
    }

    [Fact]
    public void TryFormat_TreatsTheWaysOperatingSystemsWriteOneIdAsTheSameId()
    {
        // Windows writes braces on some builds, macOS writes uppercase, and the
        // Linux files carry a trailing newline. All three are one machine.
        string[] sameIdWrittenFourWays =
        {
            "cd89566b-242d-427a-9ec1-95ad5e93a8ad",
            "{CD89566B-242D-427A-9EC1-95AD5E93A8AD}",
            "  cd89566b-242d-427a-9ec1-95ad5e93a8ad  ",
            "cd89566b-242d-427a-9ec1-95ad5e93a8ad\n"
        };

        var formatted = new List<string>();
        foreach (string raw in sameIdWrittenFourWays)
        {
            Assert.True(MachineFingerprint.TryFormat(raw, out string value));
            formatted.Add(value);
        }

        Assert.Single(new HashSet<string>(formatted));
        Assert.Equal(RegistryGuidAsSent, formatted[0]);
    }

    [Fact]
    public void TryFormat_AnAllHexNameOfExactly32CharactersIsAcceptedAndThatIsTheLimit()
    {
        // Worth being explicit about where the check stops: 32 hex characters are
        // accepted whatever their source. It cannot be tightened further without
        // asking the operating system to vouch for the value, and the length plus
        // hex rule is what keeps the four values the app must never send (host
        // name, user name, disk serial, MAC address) out in practice.
        Assert.True(MachineFingerprint.TryFormat("abcdef0123456789abcdef0123456789", out string value));
        Assert.Equal("pg-abcdef01-2345-6789-abcd-ef0123456789", value);
    }

    // ---- stability, which is what protects a paying customer ---------------------------

    [Fact]
    public void Current_IsTheSameValueWhenAskedTwice()
    {
        // The real read, on the machine running the test. If this ever fails, every
        // activated machine loses its seat on the next launch.
        MachineFingerprint again = MachineFingerprint.Resolve(
            new MachineIdentitySource(), PlatformOfThisMachine());

        Assert.True(MachineFingerprint.Current.IsAvailable,
            "this machine reports no readable identity, so the real read is not covered");
        Assert.Equal(MachineFingerprint.Current.Value, again.Value);
        Assert.Equal(MachineFingerprint.Current.Origin, again.Origin);
    }

    [Fact]
    public void Current_IsOneObjectSoThePanelAndTheGateCannotDisagree()
    {
        // The panel, the gate and the cipher all read the same value. Reading the
        // operating system per property would let a mid-session difference show as
        // two different machines on one screen.
        Assert.Same(MachineFingerprint.Current, MachineFingerprint.Current);
    }

    [Fact]
    public void ShortValue_IsTheLastFourCharactersAndSaysNothingWhenUnavailable()
    {
        var source = new MachineIdentitySource { LinuxMachineId = () => MachineId };
        MachineFingerprint fingerprint = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.LinuxMachineId);

        Assert.Equal("cdef", fingerprint.ShortValue);
        Assert.Equal("", MachineFingerprint.Unavailable.ShortValue);
    }

    // ---- the cipher key ---------------------------------------------------------------

    [Fact]
    public void CipherMaterial_IsTheFingerprintWhenThereIsOne()
    {
        // The cipher keys on this, so a settings file copied to another computer
        // stops decrypting there and the free scan count cannot travel.
        var source = new MachineIdentitySource { LinuxMachineId = () => MachineId };
        MachineFingerprint available = MachineFingerprint.Resolve(
            source, MachineFingerprint.Source.LinuxMachineId);

        Assert.Equal(MachineIdAsSent, available.CipherMaterial);
        Assert.NotEqual(MachineFingerprint.Unavailable.CipherMaterial, available.CipherMaterial);
    }

    [Fact]
    public void CipherMaterial_WithoutAFingerprintStillKeysOnSomethingPerMachine()
    {
        // The ten free scans must not depend on the operating system handing out a
        // machine id, so an unreadable machine keys on the name pair instead. It
        // cannot activate, so only the free count rides on this.
        string material = MachineFingerprint.Unavailable.CipherMaterial;

        Assert.Equal($"{Environment.MachineName}|{Environment.UserName}", material);
        Assert.NotEqual(
            material,
            new MachineFingerprint { IsAvailable = true, Value = "pg-a-different-machine" }.CipherMaterial);
    }

    private static MachineFingerprint.Source PlatformOfThisMachine()
    {
        if (OperatingSystem.IsWindows()) return MachineFingerprint.Source.WindowsMachineGuid;
        if (OperatingSystem.IsMacOS()) return MachineFingerprint.Source.MacPlatformUuid;
        return MachineFingerprint.Source.LinuxMachineId;
    }
}