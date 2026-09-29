using Velopack;
using Velopack.Sources;

namespace PhoneGrade.UI;

/// <summary>Checks GitHub releases for a newer version and installs it.
/// Only acts on Velopack-installed apps; dev runs and portable copies skip silently.</summary>
public static class AutoUpdater
{
    // The repository was renamed from Auto-Dymo-Label to PhoneGrade. GitHub
    // still redirects the old path, but the updater should not depend on a
    // redirect that can be dropped whenever the old name is reused.
    private const string RepoUrl = "https://github.com/warreth/PhoneGrade";

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
