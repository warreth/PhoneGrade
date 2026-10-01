using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// Checks that what the installer packs is what the app goes looking for.
///
/// The connector is bundled by the release workflow and found by
/// ToolInstallerService in the idevice-tools directory beside the exe. The two
/// halves live in files that no test otherwise opens, so a rename on either side
/// would go unnoticed until an installed copy came up without a connector and a
/// phone lost its secure origin with nothing in the scan to say why.
/// </summary>
public class ReleaseBundlesConnectorTests
{
    private static string Workflow => RepoPath.Read(".github", "workflows", "release.yml");

    [Theory]
    [InlineData("win-x64", true, false, Architecture.X64)]
    [InlineData("osx-x64", false, true, Architecture.X64)]
    [InlineData("osx-arm64", false, true, Architecture.Arm64)]
    [InlineData("linux-x64", false, false, Architecture.X64)]
    public void TheInstallerBundlesTheAssetThisAppDownloads(
        string rid, bool isWindows, bool isMac, Architecture architecture)
    {
        (string asset, string written) = CaseFor(rid);

        Assert.Equal(ToolInstallerService.CloudflaredAssetName(isWindows, isMac, architecture), asset);

        // The app asks for one fixed name in the tools directory, so the installer
        // has to write the archive's contents under that name rather than under
        // whatever the download happened to be called.
        Assert.Equal(isWindows ? "cloudflared.exe" : "cloudflared", written);
    }

    /// <summary>
    /// A platform the matrix builds for but the bundling case does not know about
    /// reaches the "no cloudflared build" arm and fails the release outright, which
    /// is the right thing to do and the wrong thing to find out on release day.
    /// </summary>
    [Fact]
    public void EveryPlatformTheReleaseBuildsForIsOneItKnowsHowToBundle()
    {
        string workflow = Workflow;

        foreach (Match match in Regex.Matches(workflow, @"^\s*rid:\s*(\S+)\s*$", RegexOptions.Multiline))
        {
            string rid = match.Groups[1].Value;
            Assert.True(Regex.IsMatch(CloudflaredStep, $@"^\s*{Regex.Escape(rid)}\)\s*asset=",
                    RegexOptions.Multiline),
                $"The release builds {rid}, but nothing bundles a connector for it.");
        }
    }

    [Fact]
    public void TheConnectorIsBundledIntoTheDirectoryTheAppResolvesFrom()
    {
        // ToolRunner looks in AppContext.BaseDirectory/<ToolsDirName>, and vpk packs
        // the publish directory whole, so the path the workflow writes to is the
        // whole of the link between the two.
        Assert.Contains(
            "\"${{ matrix.packDir }}/" + ToolRunner.ToolsDirName + "\"",
            CloudflaredStep);
    }

    [Fact]
    public void TheConnectorIsBundledBeforeTheInstallerIsPacked()
    {
        int bundle = Workflow.IndexOf("- name: Bundle the tunnel connector", StringComparison.Ordinal);
        int pack = Workflow.IndexOf("- name: Build Velopack installer", StringComparison.Ordinal);

        Assert.True(bundle > 0, "The release workflow has no step that bundles the tunnel connector.");
        Assert.True(pack > bundle,
            "The connector has to be on disk before the installer is packed, or it is left out of it.");
    }

    /// <summary>
    /// The name written for this platform has to be the name this app looks up on
    /// this platform, checked here rather than argued about: the workflow only says
    /// cloudflared.exe for Windows and cloudflared elsewhere, and the constant is
    /// what decides where the file is opened from.
    /// </summary>
    [Fact]
    public void TheConnectorIsWrittenUnderTheNameThisAppOpens()
    {
        string rid = OperatingSystem.IsWindows()
            ? "win-x64"
            : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

        (_, string written) = CaseFor(rid);

        Assert.Equal(ToolInstallerService.CloudflaredExecutableName, written);
    }

    /// <summary>The asset a platform downloads and the name it is written under.</summary>
    private static (string Asset, string Written) CaseFor(string rid)
    {
        string step = CloudflaredStep;

        Match match = Regex.Match(
            step,
            $@"^\s*{Regex.Escape(rid)}\)\s*asset=""([^""]+)"";\s*out=""([^""]+)""",
            RegexOptions.Multiline);

        Assert.True(match.Success, $"The bundling step has no case for {rid}.");
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    /// <summary>The bundling step, from its heading to the next one.</summary>
    private static string CloudflaredStep
    {
        get
        {
            string workflow = Workflow;
            int start = workflow.IndexOf("Bundle the tunnel connector", StringComparison.Ordinal);
            Assert.True(start >= 0, "The release workflow has no step that bundles the tunnel connector.");

            int end = workflow.IndexOf("- name:", start, StringComparison.Ordinal);
            return workflow.Substring(start, (end > start ? end : workflow.Length) - start);
        }
    }
}
