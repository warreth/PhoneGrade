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
    public static DymoFillResult Fill(string templateText, LabelFields fields,
        ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;
        var unknown = UnknownFields(templateText).ToList();

        if (!Sentinels.Keys.Any(name => Mentions(templateText, name)))
            throw new InvalidDataException(words.TemplateHasNoFields);

        var result = new StringBuilder(templateText.Length + 64);
        int copied = 0;

        while (copied < templateText.Length)
        {
            string? hit = null;
            foreach (string sentinel in Sentinels.Keys)
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

            result.Append(Escape(Sentinels[hit](fields)));
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
     if (!Sentinels.Keys.Any(sentinel => sentinel.StartsWith(word, StringComparison.Ordinal)))
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
