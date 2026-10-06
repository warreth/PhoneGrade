namespace PhoneGrade.Core;

/// <summary>
/// The Code128 barcode alphabet.
/// </summary>
/// <remarks>
/// Code128 is here because Code39 cannot put both the identifier and the
/// specification on one code at any width a scanner reads. Code39 spends fifteen
/// units on every character, and a 27 character value is 431 of them: 85.7
/// millimetres of an 81.5 millimetre address label at the narrowest bar the
/// symbology is specified at, before the quiet zones. Code128 spends eleven
/// modules on a character and only five and a half on a pair of digits, which is
/// what makes the same value come out at about 69 millimetres and fit.
///
/// Three properties of the table are load bearing, and each is asserted by the
/// tests, for the same reason the Code39 table asserts them: a pattern that is a
/// digit out reads back as a different value, and a value that reads back as a
/// different value files a device under another device's record.
///
/// every one of the 107 patterns is six elements wide except the stop, which is
/// seven, and each set of them comes to eleven modules except the stop at
/// thirteen, which is what makes it Code128 and not any other six element
/// symbology;
///
/// no two patterns are the same, which is what stops one value being read as
/// another;
///
/// the arrays are the same length as the table they index, because a table that
/// is a row short is an out of range read at print time.
///
/// The patterns are written as the widths of the six elements rather than as bars
/// and spaces, because that is how the standard publishes them. The first element
/// of every one of them is a bar, so a pattern read left to right alternates from
/// there on its own.
/// </remarks>
public static class Code128
{
    /// <summary>
    /// The widths of the elements of every value Code128 has, in the order the
    /// standard files them: the ASCII characters from space to underscore, then the
    /// characters above them, then the four function codes and the three switches,
    /// then the start codes, and the stop at the end.
    /// </summary>
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312",
        "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112",
        "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412",
        "211214", "211232", "2331112",
    ];

    /// <summary>
    /// Every value Code128 can be asked for, for a test to walk without keeping a
    /// second copy of the table.
    /// </summary>
    /// <remarks>
    /// Worked out from the table rather than written down beside it. A count kept as
    /// a second number is a number that stops agreeing with the table the moment a
    /// row is added, and it is the table that decides what a barcode scans as.
    /// </remarks>
    public static int LastValue => Patterns.Length - 1;

    /// <summary>The switch into the set that reads a character as ASCII.</summary>
    public const int CodeB = 104;

    /// <summary>The switch into the set that reads two digits as one value.</summary>
    public const int CodeC = 105;

    /// <summary>The last character, which ends the code.</summary>
    public const int Stop = 106;

    /// <summary>Every character can be encoded, so this is always true.</summary>
    public static bool CanEncode(string value) => value.All(character => character is >= ' ' and <= '~');

    /// <summary>
    /// The values a string encodes to, start character first and stop last.
    /// </summary>
    /// <remarks>
    /// The choice between the two character sets is what makes Code128 worth having
    /// here. Code B spends eleven modules on a character. Code C spends eleven on a
    /// pair of digits, which is half a module a digit less, and an identifier is
    /// nothing but digits. Starting in Code C is therefore worth it as soon as four
    /// digits come first, and worth leaving again the moment a character appears
    /// that is not one.
    ///
    /// The switch back to Code C is only taken on a run of digits long enough to
    /// pay for the switch. One symbol is eleven modules, so a run of six digits
    /// saved five and a half a digit plus one symbol and clears the cost, and a run
    /// of fewer does not. The run also has to be an even number of digits, because
    /// Code C cannot encode a single digit and switching into it one digit from the
    /// end would leave that digit with nowhere to go.
    /// </remarks>
    /// <exception cref="ArgumentException">A character Code128 cannot carry.</exception>
    public static List<int> Values(string value)
    {
        var codes = new List<int>();

        // Code C from the start when the value opens with a run of digits, which is
        // what an identifier always is.
        int index = 0;
        bool pairs = DigitsFrom(value, 0) >= 4;

        codes.Add(pairs ? CodeC : CodeB);

        while (index < value.Length)
        {
            if (pairs)
            {
                if (IsDigit(value[index]) && index + 1 < value.Length && IsDigit(value[index + 1]))
                {
                    codes.Add((value[index] - '0') * 10 + (value[index + 1] - '0'));
                    index += 2;
                    continue;
                }

                // A single digit left, or a character. Either way Code C cannot
                // carry what comes next, so back to Code B for it.
                pairs = false;
                codes.Add(CodeB);
                continue;
            }

            // A long enough even run of digits is worth a switch into Code C.
            int run = DigitsFrom(value, index);
            if (run >= 6 && run % 2 == 0)
            {
                pairs = true;
                codes.Add(CodeC);
                continue;
            }

            char character = value[index];
            if (!CanEncode(character.ToString()))
                throw new ArgumentException(
                    $"Code128 cannot carry {character}. It encodes printable ASCII, and this value holds something else.",
                    nameof(value));

            codes.Add(character - ' ');
            index++;
        }

        codes.Add(Stop);
        return codes;
    }

    /// <summary>
    /// The checksum Code128 ends its data with, so a scanner knows it read the
    /// whole value rather than part of it.
    /// </summary>
    /// <remarks>
    /// The weight of each value is its position from the start, so the start
    /// character counts once and neither the checksum character nor the stop counts
    /// at all. Missing the middle of a value changes the total, which is the entire
    /// point: a code read with a character dropped does not pass.
    /// </remarks>
    /// <param name="values">The values, without the checksum or the stop.</param>
    public static int Checksum(IReadOnlyList<int> values)
    {
        // The stop is not part of the sum. It is not data: it is the mark that the
        // data has ended, and counting it as if it were a character is a checksum
        // that no reader in the world agrees with.
        int data = values.Count > 0 && values[^1] == Stop ? values.Count - 1 : values.Count;

        int total = values[0];
        for (int i = 1; i < data; i++) total += (i * values[i]);

        return total % 103;
    }

    /// <summary>
    /// The bars and spaces of a value, each with the width it is drawn at.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="Code39.Elements"/>, because every renderer in
    /// this app draws a barcode from a list of widths and has no business knowing
    /// which symbology it was handed. A renderer that branches on the symbology is a
    /// renderer that can draw the wrong one.
    ///
    /// There is no gap between characters: Code128 keeps every symbol the same width
    /// as the stop that ends it, so the reader finds the end of the data from the
    /// stop pattern rather than from the spacing. That is why the total of every
    /// symbol is eleven modules and the total of the stop is thirteen.
    /// </remarks>
    /// <param name="value">
    /// The text. No start and stop markers are added: Code39 needs them because a
    /// reader cannot otherwise tell where the data stops, and Code128 does not.
    /// </param>
    /// <exception cref="ArgumentException">A character Code128 cannot carry.</exception>
    public static IEnumerable<BarcodeElement> Elements(string value)
    {
        List<int> codes = Values(value);
        int checksum = Checksum(codes);

        foreach (int code in codes) foreach (BarcodeElement element in Of(code)) yield return element;

        foreach (BarcodeElement element in Of(checksum)) yield return element;
    }

    /// <summary>The elements of one value, from its pattern.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value outside the table.</exception>
    public static IEnumerable<BarcodeElement> Of(int value)
    {
        if (value < 0 || value > LastValue)
            throw new ArgumentOutOfRangeException(
                nameof(value), value, $"Code128 has values 0 to {LastValue}.");

        string pattern = Patterns[value];

        for (int i = 0; i < pattern.Length; i++)
        {
            // The first element of every pattern is a bar, so the alternation
            // follows from the position.
            yield return new(i % 2 == 0, pattern[i] - '0');
        }
    }

    /// <summary>How many modules a value is drawn as, quiet zones not included.</summary>
    public static int ModulesOf(string value)
    {
        int modules = 0;
        foreach (BarcodeElement element in Elements(value)) modules += element.Units;

        return modules;
    }

    /// <summary>How many digits run from an offset, stopping at the first other character.</summary>
    private static int DigitsFrom(string value, int start)
    {
        int count = 0;
        while (start + count < value.Length && IsDigit(value[start + count])) count++;

        return count;
    }

    private static bool IsDigit(char character) => character is >= '0' and <= '9';

    /// <summary>A narrow element is one module wide, the X dimension of the code.</summary>
    public const int NarrowUnit = 1;
}

/// <summary>
/// One bar or space of a barcode, and how wide it is drawn.
/// </summary>
/// <param name="IsBar">Whether there is ink. A space is paper.</param>
/// <param name="Units">
/// How many narrow elements wide it is. A unit is a module for Code128 and a narrow
/// bar for Code39, which are the same size in both cases.
/// </param>
public readonly record struct BarcodeElement(bool IsBar, int Units);