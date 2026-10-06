using System.Reflection;
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

    public static async Task CheckAndApplyAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
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
