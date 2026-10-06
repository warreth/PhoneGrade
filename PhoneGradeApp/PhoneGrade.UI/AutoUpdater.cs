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

            // The release notes are written before the update is applied, because
            // applying it replaces this binary and restarts the process. Whatever
            // is not on disk by then is gone, and the operator gets a new version
            // with no idea what changed in it.
            RecordNotes(update);

            await mgr.DownloadUpdatesAsync(update);
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch
        {
            // Offline, rate limit, incomplete release assets: never block startup.
        }
    }

    /// <summary>
    /// Saves the notes of the version about to be installed.
    ///
    /// Velopack carries both a Markdown and an HTML form on the release. The
    /// Markdown is kept because it is what a person wrote; <see cref="ReleaseChangelog"/>
    /// flattens either into plain lines for a read-only text block. A release
    /// packaged without notes carries an empty string, and an empty string is
    /// not written at all, so an update that ships no notes does not put an empty
    /// panel in front of the operator on the next start.
    /// </summary>
    private static void RecordNotes(UpdateInfo update)
    {
        string version = "";
        string? notes = null;

        try
        {
            var target = update.TargetFullRelease;
            if (target is null) return;

            version = target.Version?.ToString() ?? "";
            notes = string.IsNullOrWhiteSpace(target.NotesMarkdown)
                ? target.NotesHTML
                : target.NotesMarkdown;
        }
        catch
        {
            // A release entry the updater cannot read fully is still worth
            // installing; only the note about it is lost.
        }

        PhoneGrade.Core.ReleaseChangelog.Record(version, notes);
    }
}
