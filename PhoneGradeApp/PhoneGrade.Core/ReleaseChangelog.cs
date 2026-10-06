using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneGrade.Core;

/// <summary>
/// What changed in the version that is running, so the shell can show it.
///
/// An update replaces the binaries and restarts, so there is no window open at the
/// moment the new version appears to tell the operator what they now have. The
/// release notes are read from the release on GitHub, which is where they are
/// written from, and cached here so the panel has something to show with no network
/// and on the next open.
///
/// The file is deliberately plain text rather than an Avalonia type: it is written
/// before any UI exists and read after, and it must not be able to fail to
/// deserialize because a field changed.
/// </summary>
public sealed class ReleaseChangelog
{
    /// <summary>
    /// Where the releases live.
    ///
    /// The updater needs this to ask whether there is anything newer and the
    /// changelog needs it to fetch the notes for the version that is running, so
    /// there is one answer rather than two that can drift apart.
    /// </summary>
    public const string RepositoryUrl = "https://github.com/warreth/PhoneGrade";

    /// <summary>Version that was installed, as Velopack reported it.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// The release notes, one entry per line. Both the HTML and the Markdown
    /// form are available on the update; the Markdown is stored because it is
    /// what a person wrote and what reads correctly in a plain text block.
    /// </summary>
    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    /// <summary>When the update was applied, UTC.</summary>
    [JsonPropertyName("appliedAt")]
    public DateTimeOffset AppliedAt { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Where the file lives. Next to settings.json rather than inside the install
    /// directory, because an update replaces the install directory and anything
    /// written into it would either be wiped or need elevation to write.
    ///
    /// It follows the licensing store's own directory, which is the one place in
    /// the product that already answers "where does per-machine state live" and
    /// already honours the AUTODYMO_SETTINGS_DIR override, so a test that
    /// redirects settings cannot leave a note behind in a real profile.
    /// </summary>
    public static string FilePath => Path.Combine(
        Licensing.TrialStateStore.DefaultSettingsDirectory, FileName);

    /// <summary>Name of the file holding the pending changelog.</summary>
    public const string FileName = "changelog.json";

    /// <summary>
    /// Records notes for a freshly installed version. Anything unwritable is
    /// swallowed: an update that succeeded but could not write a note must not be
    /// reported as a failed update, because the retry would reinstall it.
    /// </summary>
    public static void Record(string? version, string? notes, string? path = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(notes)) return;

            string text = Normalise(notes!);
            if (text.Length == 0) return;

            var changelog = new ReleaseChangelog
            {
                Version = version ?? "",
                Notes = text,
                AppliedAt = DateTimeOffset.UtcNow,
            };

            string target = path ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(
                target,
                JsonSerializer.Serialize(changelog, Options),
                Encoding.UTF8);
        }
        catch (Exception)
        {
            // Offline, read-only directory, disk full: the update itself is what
            // matters, not the note about it.
        }
    }

    /// <summary>
    /// Reads the pending changelog and clears it in the same call, so it is shown
    /// once and once only. A file that will not parse returns null and is cleared
    /// anyway: a note that cannot be shown is not worth failing a launch over, and
    /// leaving it would put the same note in front of the operator on every start
    /// from then on.
    /// </summary>
    public static ReleaseChangelog? Take(string? path = null)
    {
        string target = path ?? FilePath;
        try
        {
            if (!File.Exists(target)) return null;

            string json = File.ReadAllText(target, Encoding.UTF8);
            Clear(target);

            var changelog = JsonSerializer.Deserialize<ReleaseChangelog>(json, Options);
            if (changelog is null || string.IsNullOrWhiteSpace(changelog.Notes))
                return null;

            return changelog;
        }
        catch (Exception)
        {
            Clear(target);
            return null;
        }
    }

    /// <summary>Removes the pending changelog, whatever went wrong with it.</summary>
    public static void Clear(string? path = null)
    {
        try
        {
            string target = path ?? FilePath;
            if (File.Exists(target)) File.Delete(target);
        }
        catch (Exception)
        {
            // Best effort. A file that cannot be deleted is overwritten next time.
        }
    }

    /// <summary>
    /// Reads the changelog without clearing it.
    ///
    /// <see cref="Take"/> exists because the notes used to be a one-shot message
    /// about an update that had just happened. Now that the notes come from the
    /// release on GitHub they answer a question an operator can ask at any time,
    /// which is "what changed in the build I am running", and that question has to
    /// be answerable more than once.
    /// </summary>
    public static ReleaseChangelog? Read(string? path = null)
    {
        string target = path ?? FilePath;
        try
        {
            if (!File.Exists(target)) return null;

            var changelog = JsonSerializer.Deserialize<ReleaseChangelog>(
                File.ReadAllText(target, Encoding.UTF8), Options);

            return changelog is null || string.IsNullOrWhiteSpace(changelog.Notes) ? null : changelog;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The page a person reads when they want the notes in full.</summary>
    public static string ReleasePageUrl(string version) =>
        $"{RepositoryUrl}/releases/tag/v{version.TrimStart('v')}";

    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// The release notes for one version, read from the release page itself.
    ///
    /// The release body is the canonical copy: the same text is on the GitHub
    /// release, in the package the update installs, and on the website, and they
    /// are written from one input by the release workflow. Reading it means the app
    /// can be asked what it contains on any launch rather than only on the launch
    /// after an update installed itself, and it means an operator who skipped the
    /// panel the first time can still get to it.
    ///
    /// Nothing is thrown. A machine with no network, a rate limit, a missing tag or
    /// a release with an empty body all come back as null, and the caller falls back
    /// to whatever was cached.
    /// </summary>
    public static async Task<ReleaseChangelog?> FetchAsync(
        string? version,
        HttpMessageHandler? handler = null,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        string url = $"{RepositoryUrl.Replace("https://github.com", "https://api.github.com")}"
            + $"/releases/tags/v{version.Trim().TrimStart('v')}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // GitHub refuses a request with no user agent, and says so with a 403.
            request.Headers.UserAgent.ParseAdd("PhoneGrade");

            HttpResponseMessage response = handler is null
                ? await Shared.SendAsync(request, cancellation).ConfigureAwait(false)
                : await new HttpClient(handler) { Timeout = Shared.Timeout }
                    .SendAsync(request, cancellation).ConfigureAwait(false);

            using (response)
            {
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("body", out var body)) return null;

                string notes = Normalise(body.GetString() ?? "");
                if (notes.Length == 0) return null;

                var changelog = new ReleaseChangelog
                {
                    Version = version.Trim().TrimStart('v'),
                    Notes = notes,
                    AppliedAt = DateTimeOffset.UtcNow,
                };

                // Cached so the second open of the panel, and an open with no network,
                // both still have something to show.
                Record(changelog.Version, changelog.Notes);
                return changelog;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Flattens release notes into plain lines for a read-only text block.
    ///
    /// Velopack offers the notes as HTML and as Markdown. HTML is stripped here
    /// rather than rendered, because a WebView for three lines of changelog is a
    /// dependency, a scripting surface and a hole in a kiosk application; and
    /// Markdown is not written by hand here because the notes are authored as
    /// plain bullet lines and stripping the few markers that appear is enough.
    /// </summary>
    public static string Normalise(string markdownOrHtml)
    {
        if (string.IsNullOrWhiteSpace(markdownOrHtml)) return "";

        string text = markdownOrHtml.Replace("\r\n", "\n").Replace('\r', '\n');

        // The HTML form Velopack renders wraps each line in a paragraph or list
        // item. Taking the tags out rather than the whole document keeps the
        // author's line breaks.
        if (text.Contains('<', StringComparison.Ordinal))
        {
            /* Velopack renders the notes to a full HTML document. Taking the tags
             * out keeps the author's line breaks, but the document furniture
             * (html, head, body) is not a line of notes and is dropped first, or
             * the first line the operator reads is "html body". */
            text = StripTags(DropDocumentFurniture(text));
        }

        var lines = text.Split('\n');
        var kept = new System.Collections.Generic.List<string>();

        foreach (string raw in lines)
        {
            /* Trim, not TrimEnd. The notes are written on whichever machine cut
             * the release, so a mixed-ending source leaves a lone \r behind, and
             * that survives into a read-only text block as a line running into
             * the next one. Trimming both ends takes it with the whitespace an
             * indented bullet left behind. */
            string line = raw.Trim();

            // Markdown list bullets and heading marks become nothing, the text
            // stays. A line that was only a bullet is dropped rather than kept as
            // an empty row.
            /* Both spaces and tabs, because a bullet followed by a tab is the same intent
             * and produced a line starting with a literal tab otherwise. */
            if (line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("-", StringComparison.Ordinal) && line.Length > 1 && char.IsWhiteSpace(line[1])
                || line.StartsWith("* ", StringComparison.Ordinal)
                || line.StartsWith("+ ", StringComparison.Ordinal))
            {
                line = line[1..].TrimStart();
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal)
                || line.StartsWith("## ", StringComparison.Ordinal)
                || line.StartsWith("# ", StringComparison.Ordinal))
            {
                int hash = line.IndexOf(' ') + 1;
                line = line[hash..].Trim();
            }

            line = line.Replace("**", "").Replace("__", "").Trim();

            if (line.Length > 0) kept.Add(line);
        }

        return string.Join(Environment.NewLine, kept);
    }

    /// <summary>
    /// Drops the parts of an HTML document that are structure rather than content.
    ///
    /// Whatever sits inside head is metadata and never a release note, and body is
    /// the only part worth reading. Without this the first line shown would be
    /// the document's own tag names, because the tags are stripped but their
    /// contents are not.
    /// </summary>
    private static string DropDocumentFurniture(string html)
    {
        const string head = "<head";
        const string body = "<body";

        int headAt = html.IndexOf(head, StringComparison.OrdinalIgnoreCase);
        if (headAt >= 0)
        {
            int headEnd = html.IndexOf("</head>", headAt, StringComparison.OrdinalIgnoreCase);
            html = headEnd >= 0
                ? html[..headAt] + html[(headEnd + "</head>".Length)..]
                : html[..headAt];
        }

        int bodyAt = html.IndexOf(body, StringComparison.OrdinalIgnoreCase);
        if (bodyAt < 0) return html;

        int bodyEnd = html.IndexOf("</body>", bodyAt, StringComparison.OrdinalIgnoreCase);
        return bodyEnd >= 0
            ? html[bodyAt..bodyEnd]
            : html[bodyAt..];
    }

    /// <summary>
    /// Removes tags and decodes the handful of entities a release note is likely
    /// to contain. Not a general HTML parser, and not pretending to be: it drops
    /// anything that looks like a tag and leaves the text between them.
    /// </summary>
    private static string StripTags(string html)
    {
        var result = new StringBuilder(html.Length);
        bool inside = false;

        foreach (char c in html)
        {
            if (c == '<') { inside = true; continue; }
            if (c == '>') { inside = false; result.Append(' '); continue; }
            if (!inside) result.Append(c);
        }

        return result.ToString()
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.Ordinal);
    }
}
