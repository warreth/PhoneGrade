using System.Text;

namespace PhoneGrade.Core;

/// <summary>
/// Fills a DYMO label template with the values of an inspection.
///
/// A .dymo file is undocumented XML that DYMO ships no schema for, so the only
/// mechanism that needs nothing installed on the operator's machine is to take a
/// template DYMO itself wrote and substitute the sentinels in it as text. That
/// works, and it works in one specific way: the file must not be re-serialised
/// afterwards. DYMO's own deserializer rejects the empty-element shorthand every
/// .NET XML writer emits, so &lt;Color/&gt; has to arrive as
/// &lt;Color&gt; &lt;/Color&gt; with a space in it. Rewriting the document to change a
/// value is what breaks printing; substituting into the bytes on disk is what keeps
/// it working.
/// </summary>
public static class DymoTemplate
{
    /// <summary>
    /// The sentinels a template may carry, and the field each one fills. The names
    /// are spelled out rather than derived, because a template is a file somebody
    /// edited in a text editor and the sentinel is the contract with them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<LabelFields, string>> Sentinels =
        new Dictionary<string, Func<LabelFields, string>>(StringComparer.Ordinal)
        {
            ["IDENTIFIER"] = f => f.Identifier,
            ["MODEL"] = f => f.Model,
            ["PCOLOR"] = f => f.Color,
            ["BATTERY"] = f => f.Battery,
            ["QUALITY"] = f => f.Grade,
            ["PAYM"] = f => f.PayMethod,
            ["STORAGE"] = f => f.Storage,
            ["MEMORY"] = f => f.Memory,
            ["CYCLES"] = f => f.BatteryCycles,
            ["FAULTS"] = f => f.Faults.Summary,
            ["LOCKS"] = f => f.Faults.LockLine,
            ["DETAIL"] = f => LabelLayout.DetailLine(f),
            ["SPEC"] = f => LabelLayout.TextLine(f),
        };

    /// <summary>
    /// The sentinels that depend on the stock and on the barcode mode.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Sentinels"/> because these cannot be worked out from
    /// the values alone: whether a code fits depends on how wide the roll is, and
    /// what goes in it depends on the mode the operator chose. They are named for the
    /// two barcode objects a template carries, so one file serves every mode: an
    /// empty barcode object prints nothing, which is how a two barcode template
    /// prints a label with one code on it.
    /// </remarks>
    public const string FirstBarcodeSentinel = "BARCODE1";

    /// <summary>The second barcode object's sentinel.</summary>
    public const string SecondBarcodeSentinel = "BARCODE2";

    /// <summary>The element a template declares its symbology in.</summary>
    public const string FormatElement = "BarcodeFormat";

    /// <summary>
    /// What the template declares, as a symbology this app knows.
    /// </summary>
    /// <remarks>
    /// Asked of the template rather than of the settings, and that is the whole reason
    /// this exists. A .dymo file carries the symbology inside its barcode objects, and
    /// DYMO's own software draws the file rather than this app: putting a Code128
    /// payload into a template that declares Code39 produces a file that is well
    /// formed and prints a barcode the shop's scanner cannot read.
    ///
    /// A template that declares neither of the two symbologies this app can write is
    /// reported as Code39, because that is the one every template in the wild
    /// declares and guessing the other way would silently misreport the file.
    /// </remarks>
    /// <param name="templateText">The template as it is on disk.</param>
    public static LabelCodeSymbology DeclaredSymbology(string templateText)
    {
        // Only the barcode objects' own declarations, so a mention of the word in a
        // text field somewhere in the file cannot decide what the codes are.
        foreach (string declared in FormatElements(templateText))
            if (declared.Contains("128", StringComparison.OrdinalIgnoreCase))
                return LabelCodeSymbology.Code128;

        return LabelCodeSymbology.Code39;
    }

    /// <summary>
    /// Whether this app can write the symbology a template declares.
    /// </summary>
    /// <remarks>
    /// False for a template that asks for something else, such as QR or EAN. Such a
    /// template is not a failure: the operator gets the label they configured and the
    /// app says plainly that its own settings do not apply to it, rather than
    /// substituting a payload the template will draw differently.
    /// </remarks>
    public static bool Supports(LabelCodeSymbology symbology) =>
        symbology is LabelCodeSymbology.Code39 or LabelCodeSymbology.Code128;

    /// <summary>What each BarcodeFormat element says.</summary>
    private static IEnumerable<string> FormatElements(string templateText)
    {
        foreach (System.Text.RegularExpressions.Match element in
                 System.Text.RegularExpressions.Regex.Matches(
                     templateText,
                     @"<(?:[A-Za-z0-9]+:)?BarcodeFormat\b[^>]*>(?<value>.*?)</(?:[A-Za-z0-9]+:)?BarcodeFormat>",
                     System.Text.RegularExpressions.RegexOptions.Singleline |
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            yield return element.Groups["value"].Value;
    }

    /// <summary>
    /// What could not be barcoded and is printed as words instead.
    /// </summary>
    /// <remarks>
    /// Its own sentinel so a template can put it on the label as text. On the two
    /// small multi purpose rolls a fifteen digit identifier is wider than the paper
    /// at a bar width a scanner reads, and it has to appear somewhere: a label that
    /// cannot be scanned has to be readable by eye.
    /// </remarks>
    public const string SpelledSentinel = "SPELLED";

    /// <summary>
    /// Every name this app fills, including the ones that depend on the stock.
    /// </summary>
    /// <remarks>
    /// Held apart from <see cref="Sentinels"/> so that a template asking for a
    /// barcode is not reported as carrying a field this app knows nothing about.
    /// </remarks>
    public static IReadOnlyList<string> KnownSentinels { get; } =
        [.. Sentinels.Keys, FirstBarcodeSentinel, SecondBarcodeSentinel, SpelledSentinel];

    /// <summary>Every sentinel this app can fill, given a stock and a barcode mode.</summary>
    private static IReadOnlyDictionary<string, Func<LabelFields, string>> ValuesFor(
        LabelLayout layout, LabelBarcodeMode mode, LabelCodeSymbology symbology)
    {
        var values = new Dictionary<string, Func<LabelFields, string>>(
            Sentinels, StringComparer.Ordinal);

        values[FirstBarcodeSentinel] =
            fields => Barcodes(layout, mode, symbology, fields).Barred.ElementAtOrDefault(0) ?? "";
        values[SecondBarcodeSentinel] =
            fields => Barcodes(layout, mode, symbology, fields).Barred.ElementAtOrDefault(1) ?? "";
        values[SpelledSentinel] =
            fields => string.Join(" ", Barcodes(layout, mode, symbology, fields).Spelled);

        return values;
    }

    /// <summary>
    /// What the barcodes carry on this stock in this mode.
    /// </summary>
    /// <remarks>
    /// The label PDF and the preview ask the same question of the same type, so the
    /// .dymo file cannot come out carrying something different from the label the
    /// operator was shown.
    /// </remarks>
    private static (IReadOnlyList<string> Barred, IReadOnlyList<string> Spelled) Barcodes(
        LabelLayout layout, LabelBarcodeMode mode, LabelCodeSymbology symbology,
        LabelFields fields) =>
        new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields, symbology))
            .On(layout.Stock, mode, symbology, fields.IsIdentifiable);

    /// <summary>
    /// Substitutes the values into the template text.
    ///
    /// The template is read as text on purpose, see the note on the class. Every
    /// value is XML-escaped on the way in, because a model called
    /// "iPhone 15 &amp; 16" would otherwise produce a file the service rejects with a
    /// line number that points at nothing useful.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The template carries none of the fields this app fills. That is almost
    /// always a template that was replaced by a label saved from DYMO with the
    /// values already merged in, which then prints the last device inspected,
    /// forever, on every device after it.
    /// </exception>
    /// <param name="layout">
    /// The stock and the barcode mode it goes with, because whether a code fits and
    /// what it carries are not questions the values alone can answer.
    /// </param>
    /// <param name="symbology">
    /// Not taken. The symbology is read from the template and cannot be set from
    /// outside, which is the only way to be right: DYMO draws this file rather than
    /// this app, so a payload encoded for a symbology the template does not declare
    /// prints as a barcode the shop's scanner cannot read. A setting that could
    /// override it would be a setting whose only correct value happened to be the one
    /// already in force.
    /// </param>
    public static DymoFillResult Fill(string templateText, LabelFields fields,
        LabelLayout? layout = null, LabelBarcodeMode mode = LabelBarcodeMode.Identifier,
        ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;
        var unknown = UnknownFields(templateText).ToList();

        IReadOnlyDictionary<string, Func<LabelFields, string>> values =
            ValuesFor(layout ?? LabelLayout.Address, mode, DeclaredSymbology(templateText));

        if (!values.Keys.Any(name => Mentions(templateText, name)))
            throw new InvalidDataException(words.TemplateHasNoFields);

        var result = new StringBuilder(templateText.Length + 64);
        int copied = 0;

        while (copied < templateText.Length)
        {
            string? hit = null;
            foreach (string sentinel in values.Keys)
            {
                if (!templateText.AsSpan(copied).StartsWith(sentinel.AsSpan(), StringComparison.Ordinal))
                    continue;

                // The longest sentinel wins where two start the same way, so a
                // template carrying both PAYM and PAYMETHOD fills PAYMETHOD rather
                // than PAYM followed by the letters ETHOD.
                if (hit is null || sentinel.Length > hit.Length) hit = sentinel;
            }

            if (hit is null)
            {
                result.Append(templateText[copied]);
                copied++;
                continue;
            }

            result.Append(Escape(values[hit](fields)));
            copied += hit.Length;
        }

        return new DymoFillResult(result.ToString(), unknown);
    }

    /// <summary>
    /// Fields the template asks for that this app does not fill. Not a failure: a
    /// shop's own template may carry its company name, and refusing to print it
    /// over that would be worse than telling the operator it is not being replaced.
    ///
    /// Only the bodies of text and barcode objects are looked at. Anything looser
    /// picks up the template's own geometry, and a label is full of geometry: a
    /// note listing coordinates and tag names is a note nobody reads, and one that
    /// is always long teaches an operator to skip the line that would have mattered.
 /// </summary>
    public static IReadOnlyList<string> UnknownFields(string templateText)
    {
     var unknown = new List<string>();

  try
    {
     System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Parse(templateText);

            foreach (System.Xml.Linq.XElement element in document.Descendants())
            {
      if (element.Name.LocalName is not ("Text" or "DataString")) continue;

                foreach (string word in FieldWords(element.Value))
     if (!KnownSentinels.Any(sentinel => sentinel.StartsWith(word, StringComparison.Ordinal)))
        unknown.Add(word);
       }
        }
      catch (System.Xml.XmlException)
        {
      // A template that will not parse cannot be filled either, and the fill is
      // about to say so. Saying nothing extra here is right.
        }

        return [.. unknown.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The upper case words in one piece of text. Letters only: a run with a digit
    /// in it is a measurement or an object suffix, never a field.
    /// </summary>
    private static IEnumerable<string> FieldWords(string text)
    {
        int index = 0;
  while (index < text.Length)
        {
   if (!char.IsUpper(text[index])) { index++; continue; }

            int start = index;
 while (index < text.Length && (char.IsUpper(text[index]) || char.IsDigit(text[index])))
       index++;

    string word = text[start..index];
            if (word.Length >= 4 && !word.Any(char.IsDigit))
                yield return word;
}
    }

    /// <summary>Whether the template carries this sentinel anywhere in its text.</summary>
    private static bool Mentions(string templateText, string sentinel)
    {
        int index = templateText.IndexOf(sentinel, StringComparison.Ordinal);
        while (index >= 0)
        {
            if (!IsInsideTag(templateText, index)) return true;
            index = templateText.IndexOf(sentinel, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// The runs of upper case letters that sit in a text node rather than in a tag.
    /// The XML tags are upper case too, and they are not fields.
    /// </summary>
    private static IEnumerable<string> Words(string templateText)
    {
        int index = 0;
        while (index < templateText.Length)
        {
            if (templateText[index] == '<')
            {
                int close = templateText.IndexOf('>', index);
                if (close < 0) yield break;
                index = close + 1;
                continue;
            }

            int start = index;
            while (index < templateText.Length && (char.IsUpper(templateText[index]) || char.IsDigit(templateText[index])))
                index++;
            if (index > start) yield return templateText[start..index];

            if (index < templateText.Length && templateText[index] != '<') index++;
        }
    }

    /// <summary>
    /// Escapes a value on its own, for a caller that is building label text rather
    /// than filling a template. Same five characters, same reason.
    /// </summary>
    public static string EscapeValue(string value) => Escape(value);

    /// <summary>Whether the offset falls between a &lt; and the &gt; that closes it.</summary>
    private static bool IsInsideTag(string text, int offset)
    {
        int open = text.LastIndexOf('<', offset);
        if (open < 0) return false;
        return text.IndexOf('>', open) >= offset;
    }

    /// <summary>
    /// Escapes a value for an XML text node. These five are the ones XML itself
    /// defines; the accents and the euro sign the template already contains travel
    /// as written.
    /// </summary>
    internal static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}

/// <summary>
/// The filled template, plus what the template asked for that nothing filled.
/// </summary>
/// <param name="Text">The label file, ready to be written as it stands.</param>
/// <param name="UnfilledFields">
/// Names in the template that no value was put into, so the operator can be told
/// before the label is printed rather than after.
/// </param>
public sealed record DymoFillResult(string Text, IReadOnlyList<string> UnfilledFields);
