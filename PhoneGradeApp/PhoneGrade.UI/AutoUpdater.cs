using System.Reflection;
using PhoneGrade.UI.Models;
using Velopack;
using Velopack.Sources;

namespace PhoneGrade.UI;

/// <summary>
/// Finds a newer build on GitHub and installs it.
///
/// Only acts on Velopack-installed apps; a development run and a portable copy skip
/// silently. The notes for whatever version is running are not this class's job:
/// they are read from the release itself, so an operator can ask for them on any
/// launch rather than only on the one after an update.
/// </summary>
public static class AutoUpdater
{
    // The repository was renamed from Auto-Dymo-Label to PhoneGrade. GitHub still
    // redirects the old path, but the updater should not depend on a redirect that
    // can be dropped whenever the old name is reused.
    private const string RepoUrl = PhoneGrade.Core.ReleaseChangelog.RepositoryUrl;

    /// <summary>
    /// The version this build is, or an empty string when it cannot be worked out.
    ///
    /// Read from the assembly rather than from Velopack, because a portable copy has
    /// no Velopack to ask and still has notes on the release page. The workflow
    /// stamps the version onto the assembly, so this is the same string the tag
    /// carries.
    /// </summary>
    public static string RunningVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // The SDK appends the source revision to the informational version.
                int plus = informational.IndexOf('+', StringComparison.Ordinal);
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Whether this build is itself a beta.
    ///
    /// Read off the version rather than asked, so a shop that installed a beta by hand
    /// keeps receiving betas even after it has turned the setting off. Turning it off
    /// means "stop offering me new ones", not "downgrade me and hide what I am".
    ///
    /// A shop on a beta that switches the setting off goes back to the stable line on
    /// its next update. There is no way back down from inside the app, and pretending
    /// otherwise would need an installer that moves someone backwards, which is a
    /// downgrade with all the risk of one.
    /// </summary>
    public static bool IsPrerelease() => IsPrereleaseVersion(RunningVersion());

    /// <summary>Whether a version string names a prerelease, by the SemVer rule.</summary>
    public static bool IsPrereleaseVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && version.Contains('-', StringComparison.Ordinal);

    /// <summary>
    /// Whether to look at the beta channel as well as the stable one.
    /// </summary>
    /// <remarks>
    /// An either way, not an override, and the order matters. A shop that ticked the
    /// box wants betas. A shop already running one keeps wanting them after unticking,
    /// because the alternative is a silent drop back onto the stable line with nothing
    /// on screen saying so, which is a worse outcome than being on a build the shop
    /// already chose to run.
    ///
    /// Only a shop that was never offered a beta and never asked for one stays off it.
    /// </remarks>
    public static bool WantsPrereleases(AppSettings? settings) =>
        IsPrerelease() || (settings?.IncludePrereleases ?? false);

    /// <summary>
    /// Checks for an update and installs it.
    ///
    /// The third argument to GithubSource is whether prereleases count. It is the shop's
    /// own choice, not the author's: a shop that asked for beta gets beta, and a shop
    /// that did not is never offered one. That is the whole safety of a test channel,
    /// because a prerelease is not a stable build that happens to be new.
    /// </summary>
    public static async Task CheckAndApplyAsync(AppSettings? settings = null)
    {
        try
        {
            bool wantsPrereleases = WantsPrereleases(settings);

            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, wantsPrereleases));
            if (!mgr.IsInstalled) return; // dev run or portable zip: nothing to update

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return;

            await mgr.DownloadUpdatesAsync(update);
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch
        {
            // Offline, rate limit, incomplete release assets: never block startup.
        }
    }
}