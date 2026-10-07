using System;
using PhoneGrade.UI;
using PhoneGrade.UI.Models;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// Covers the beta channel on both sides of it.
///
/// The two halves are in files nothing else joins up. The release workflow decides which
/// channel a build goes on by reading a tag, and the app decides whether to look at that
/// channel by reading a setting. Nothing in the repository connects those two answers, so
/// a change to either half that breaks the other is a build that works and an update that
/// never arrives, or the reverse: a beta offered to a shop that did not ask for one.
///
/// The first group reads the workflow, the second reads the app. Neither runs Velopack,
/// so the tests are fast and need no network.
/// </summary>
public class BetaChannelTests
{
    private static string Workflow => RepoPath.Read(".github", "workflows", "release.yml");

    // ============ The build side: which channel a build lands on ============

    /// <summary>
    /// A tag carrying a prerelease part goes on the beta channel.
    ///
    /// Read off the tag rather than off the tick on the dispatch form, so a hand-run
    /// build that forgets the box still lands where beta users are watching rather than
    /// on the stable channel, which is the failure that matters.
    /// </summary>
    [Fact]
    public void ATagThatNamesAPrereleaseIsBuiltAsOne()
    {
        string workflow = Workflow;

        Assert.Contains("case \"$TAG\" in *-*) IS_PRERELEASE=true ;; esac", workflow);
        Assert.Contains("CHANNEL_SUFFIX=\"-beta\"", workflow);
    }

    /// <summary>
    /// The suffix goes onto the platform channel rather than replacing it.
    ///
    /// Replacing it would put a beta for Windows on the same channel as a stable for
    /// Linux, and a shop would be offered the other platform's build.
    /// </summary>
    [Fact]
    public void TheBetaChannelIsThePlatformChannelWithASuffix()
    {
        Assert.Contains("--channel ${{ matrix.channel }}", Workflow);
        Assert.Contains("--channelSuffix", Workflow);
    }

    /// <summary>
    /// A stable tag builds with no suffix at all, not with an empty one.
    ///
    /// Passing an empty suffix is not the same as passing none as far as the package
    /// name goes, and a channel called "win-" is not a channel anybody is subscribed to.
    /// The suffix is only passed when it holds something, which is what the ${VAR:+...}
    /// form does.
    /// </summary>
    [Fact]
    public void AStableTagAddsNoSuffixAtAll()
    {
        string workflow = Workflow;

        Assert.Contains("CHANNEL_SUFFIX=\"\"", workflow);
        Assert.Contains("${CHANNEL_SUFFIX:+--channelSuffix \"$CHANNEL_SUFFIX\"}", workflow);
    }

    /// <summary>
    /// The workflow offers a prerelease flag on a hand run.
    ///
    /// Without it, a tag that does not yet say beta cannot be built as one from the
    /// dispatch form, and the shop that asked for the beta has to wait for a tag push.
    /// </summary>
    [Fact]
    public void AHandRunCanSayThatThisIsAPrerelease()
    {
        string workflow = Workflow;

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("prerelease:", workflow);
    }

    /// <summary>
    /// The tag is decided once, before the four builds start.
    ///
    /// Creating a tag inside the matrix means four runners ask the repository the same
    /// question at the same moment, all four see no, and all four push. The second one
    /// to arrive fails the run on a tag the first one already made, and the release ends
    /// up red with three good packages on it.
    ///
    /// So the version work is a separate job the build waits on. The check is the wait,
    /// not the job name: a prepare job nobody depends on has the same problem as none.
    /// </summary>
    [Fact]
    public void TheTagIsSettledBeforeAnyOfTheFourBuildsStart()
    {
        string workflow = Workflow;

        Assert.Contains("needs: prepare", workflow);

        // And the tag is pushed in that job rather than in the matrix, which is the
        // half that makes the wait matter.
        int pushed = workflow.IndexOf("git push origin \"refs/tags/$TAG\"", StringComparison.Ordinal);
        Assert.True(pushed > 0, "the tag should be pushed by the prepare job");

        int firstBuild = workflow.IndexOf("- name: Publish app", StringComparison.Ordinal);
        Assert.True(
            pushed < firstBuild,
            "the tag has to exist before the packages are published, not after");
    }

    /// <summary>
    /// Everything downstream reads one tag rather than working it out again.
    ///
    /// A version derived in one step and a version derived in another are two answers
    /// that agree until somebody edits one of them. So the tag, the version and the
    /// channel suffix are named once at job level and every step reads those names.
    /// </summary>
    [Fact]
    public void EveryBuildStepReadsTheOneTag()
    {
        string workflow = Workflow;
        int jobLevel = workflow.IndexOf("TAG: ${{ needs.prepare.outputs.tag }}", StringComparison.Ordinal);

        Assert.True(jobLevel > 0, "the tag should be named once for the build job");

        // Inside a shell script an expression is pasted into the text before the shell
        // sees it, so a version with a quote in it would end the string. A shell reads
        // the name instead, out of the environment the job level set up. The two steps
        // that need the version are the ones that check for this.
        Assert.Contains("-p:Version=\"$APP_VERSION\"", workflow);
        Assert.Contains("VERSION=\"$APP_VERSION\"", workflow);

        // The same reason for not reaching for github.sha inside a script. The tag is
        // already made by then, and a runner that guessed at a different commit would
        // be building something the release does not point at.
        Assert.DoesNotContain("${{ github.sha }}", workflow);
    }

    // ============ The app side: whether to look at that channel ============

    /// <summary>
    /// A shop that asked for the beta gets it.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheSettingDecidesWhetherBetasAreOffered(bool ticked, bool expected) =>
        Assert.Equal(expected, AutoUpdater.WantsPrereleases(new AppSettings { IncludePrereleases = ticked }));

    /// <summary>
    /// No settings at all reads as no, rather than as an exception or as yes.
    ///
    /// The updater runs before there is a view model to ask, and an upgrade from a build
    /// that never had the key in its settings file must not turn a shop into a beta
    /// user by accident.
    /// </summary>
    [Fact]
    public void AbsentSettingsMeanNoBetas() => Assert.False(AutoUpdater.WantsPrereleases(null));

    /// <summary>
    /// A shop that installed a beta by hand keeps receiving betas.
    ///
    /// Both answers here are the same in every build the test suite runs, because the
    /// suite itself is not a prerelease. So what is being checked is the rule rather
    /// than the outcome: a stable build that asks still gets them, and this is the
    /// branch a stable build sits in.
    /// </summary>
    [Fact]
    public void AShopOnTheBetaChannelIsRecognisedByItsOwnVersion()
    {
        Assert.False(AutoUpdater.IsPrerelease());
        Assert.True(AutoUpdater.WantsPrereleases(new AppSettings { IncludePrereleases = true }));
    }

    /// <summary>
    /// What counts as a prerelease is a hyphen after the numbers, which is the SemVer
    /// rule rather than a list of suffixes. A tag written as v1.0-beta1 is the case that
    /// matters, and so is a name like 1.0.0-preview.2.
    /// </summary>
    [Theory]
    [InlineData("1.0.0-beta1", true)]
    [InlineData("1.0.0-beta1.2", true)]
    [InlineData("1.0.0-preview.3", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.1.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AVersionSaysWhetherItIsABeta(string? version, bool expected) =>
        Assert.Equal(expected, AutoUpdater.IsPrereleaseVersion(version));

    /// <summary>
    /// The setting survives a save and a load.
    ///
    /// A tick that is not written down is a tick the operator has to make again on every
    /// start, and a beta channel that reverts on restart is not a channel.
    /// </summary>
    [Fact]
    public void TheChoiceIsRemembered()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"beta-channel-{Guid.NewGuid():N}");
        string? original = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", dir);

            new AppSettings { IncludePrereleases = true }.Save();
            Assert.True(AppSettings.Load().IncludePrereleases);

            new AppSettings { IncludePrereleases = false }.Save();
            Assert.False(AppSettings.Load().IncludePrereleases);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", original);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* a scratch directory that will not go is not a test failure */ }
        }
    }

    /// <summary>
    /// A fresh install is not offered a beta.
    ///
    /// The default matters more here than it usually does. An unticked box that defaults
    /// to on would put untested builds in front of shops that never asked for them, and
    /// there is no way for them to tell from the outside.
    /// </summary>
    [Fact]
    public void NobodyIsPutOnABetaWithoutAsking() => Assert.False(new AppSettings().IncludePrereleases);
}