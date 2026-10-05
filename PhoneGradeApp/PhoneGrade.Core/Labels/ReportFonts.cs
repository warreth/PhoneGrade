using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PhoneGrade.Core;

/// <summary>
/// The font the report PDFs are drawn in.
///
/// A PDF has to carry its own font data. Which font it is therefore a property of
/// the file rather than of the machine that made it, and a report drawn in
/// whatever the shop happens to have installed comes out looking wrong on the
/// next machine and unreadable on one with nothing at all.
///
/// QuestPDF ships one font inside its own assembly and registers it as Lato, which
/// covers Latin, Greek and Cyrillic, so it covers Dutch and English and every
/// accent a device description is likely to carry. This registers that one family
/// and nothing else: one family means one line height and one set of metrics, so
/// a table does not reflow between the machine that wrote it and the one that
/// reads it.
/// </summary>
public static class ReportFonts
{
    private static readonly object _gate = new();
    private static bool _ready;

    /// <summary>The family every report is drawn in.</summary>
    public const string Family = "Lato";

    /// <summary>
    /// Prepares the font manager. Called before every document is drawn, and safe
    /// to call twice, because two exports in a row would otherwise each try to
    /// register the same family and the second one would throw.
    /// </summary>
    public static void Ensure()
    {
        if (_ready) return;

        lock (_gate)
        {
            if (_ready) return;

            QuestPDF.Settings.License = LicenseType.Community;

            // The system fonts stay on as a safety net: a machine with no font of
            // our own still produces a readable report, in whatever it has, rather
            // than a failed export. The report itself is drawn in Lato, which is
            // the point of registering one.
            QuestPDF.Settings.UseSystemFonts = true;
            _ready = true;
        }
    }

    /// <summary>
    /// Whether the family can actually be drawn. A font that is missing fails
    /// when a document is drawn rather than when it is set up, which turns a
    /// missing font into an export failure with a stack trace in it.
    /// </summary>
    public static bool IsAvailable()
    {
        Ensure();
        return FontManager.GetRegisteredFonts()
            .Any(font => string.Equals(font.FamilyName, Family, StringComparison.Ordinal));
    }

    /// <summary>
    /// The family to draw in, or null to let the font manager decide. Null is the
    /// right answer when the family is unavailable: the document still draws, in
    /// whatever is there, which is a worse report rather than no report.
    /// </summary>
    public static string? Resolve() => IsAvailable() ? Family : null;
}