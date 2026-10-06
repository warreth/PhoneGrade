namespace PhoneGrade.Core;

/// <summary>
/// The Code39 barcode alphabet.
///
/// Code39 is the symbology the DYMO template uses for the identifier, and it is
/// the only one that can be drawn without a symbology library on the machine. The
/// standard encodes each character as nine elements, alternating bar and space,
/// of which exactly three are wide. The table below is the standard's own
/// published pattern list.
///
/// Three properties of it are load bearing, and each is asserted by the tests:
///
/// every character has exactly three wide elements out of nine, which is what
/// makes this Code39 rather than any other nine element symbology;
///
/// no two characters share a pattern, which is what stops one character from
/// being read as another and filing a device under the wrong record;
///
/// the two arrays are the same length, because the index into one picks the row
/// in the other and a table that is a row short is an out of range read at print
/// time.
///
/// The order of the alphabet is therefore part of the data rather than a matter
/// of taste, and neither array may be sorted without the other. A table of this
/// kind is the sort of thing that gets edited by hand, and a character swapped
/// for its neighbour does not show up as a fault on the label: it shows up as a
/// device filed under a different serial.
/// </summary>
public static class Code39
{
    /// <summary>
    /// The characters, in the order their patterns are filed: the ten digits, then
    /// the symbols and the space, then the twenty six letters, then the asterisk
    /// which is the start and stop marker rather than a data character.
    /// </summary>
    private const string Alphabet = "0123456789-. $/+%ABCDEFGHIJKLMNOPQRSTUVWXYZ*";

    /// <summary>
    /// Every character Code39 can carry, in the order their patterns are filed.
    ///
    /// Exposed so a test can walk the whole table without keeping a second copy of
    /// the order. A second copy is how the two arrays drift apart.
    /// </summary>
    public static string Characters => Alphabet;

    /// <summary>Nine elements per character, W for wide and N for narrow.</summary>
    private static readonly string[] Patterns =
    [
        // 0-9
        "NNNWWNWNN", "WNNWNNNNW", "NNWWNNNNW", "WNWNNNNNW", "NNNWWNNNW",
        "WNNWWNNNN", "NNWWWNNNN", "NNNWNNWNW", "WNNWNNWNN", "NNWWNNWNN",

        // -  .  space  $  /  +  %
        "NWNNNNWNW", "WWNNNNWNN", "NWWNNNWNN", "NWNWNWNNN", "NWNWNNNWN",
        "NWNNNWNWN", "NNNWNWNWN",

        // A-I
        "WNNNNWNNW", "NNWNNWNNW", "WNWNNWNNN", "NNNNWWNNW", "WNNNWWNNN",
        "NNWNWWNNN", "NNNNNWWNW", "WNNNNWWNN", "NNWNNWWNN",

        // J-R
        "NNNNWWWNN", "WNNNNNNWW", "NNWNNNNWW", "WNWNNNNWN", "NNNNWNNWW",
        "WNNNWNNWN", "NNWNWNNWN", "NNNNNNWWW", "WNNNNNWWN",

        // S-Z
        "NNWNNNWWN", "NNNNWNWWN", "WWNNNNNNW", "NWWNNNNNW", "WWWNNNNNN",
        "NWNNWNNNW", "WWNNWNNNN", "NWWNWNNNN",

        // the asterisk: the start and stop marker, not a data character
        "NWNNWNWNN",
    ];

    /// <summary>
    /// Turns text into the bars to draw.
    /// </summary>
    /// <param name="value">
    /// The text, which must already carry the leading and trailing asterisk. Text
    /// without them scans as one long run with no end, which is a barcode that
    /// reads back as the wrong device.
    /// </param>
    /// <exception cref="ArgumentException">A character Code39 cannot carry.</exception>
    public static IEnumerable<string> Encode(string value)
    {
        foreach (char character in value)
        {
            int index = Alphabet.IndexOf(character);
            if (index < 0)
                throw new ArgumentException(
                    $"Code39 cannot carry {character}. It encodes digits, capitals and - . $ / + % only.",
                    nameof(value));

            yield return Patterns[index];
        }
    }

    /// <summary>Whether every character can be encoded.</summary>
    public static bool CanEncode(string value) =>
        value.All(character => Alphabet.IndexOf(character) >= 0);

    /// <summary>
    /// The bars and spaces of a value, each with the width it is drawn at.
    /// </summary>
    /// <remarks>
    /// The width matters as much as whether it is a bar. Code39 has three wide
    /// elements in every nine, and which three is what tells one character from
    /// another: draw every bar wide and the code is no longer Code39, it is a row
    /// of stripes a scanner reads as noise. Nine characters of a serial coming back
    /// as nine wrong ones is worse than no barcode, because the operator files the
    /// phone under the wrong record.
    ///
    /// A wide element is three units and a narrow one is a single unit, which is the
    /// ratio the standard is read at. A narrow space sits between one character and
    /// the next; without it two characters run together and the whole value comes
    /// back as one long run.
    /// </remarks>
    public static IEnumerable<BarcodeElement> Elements(string value)
    {
        bool first = true;

        foreach (string pattern in Encode(value))
        {
            if (!first) yield return new(false, NarrowUnit);

            for (int i = 0; i < pattern.Length; i++)
            {
                bool isBar = i % 2 == 0;
                yield return new(isBar, pattern[i] == 'W' ? WideUnit : NarrowUnit);
            }

            first = false;
        }
    }

    /// <summary>A narrow element is one unit wide.</summary>
    public const int NarrowUnit = 1;

    /// <summary>A wide element is three, which is the ratio the standard is read at.</summary>
    public const int WideUnit = 3;
}