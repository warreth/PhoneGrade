using System.Text;

namespace PhoneGrade.Core;

/// <summary>
/// The colour names a phone can report, reduced to one spelling each.
///
/// The phone reports a colour in whatever vocabulary the manufacturer uses: a hex
/// code, a one digit code, an Apple marketing name, or a three letter Android
/// property. All of those used to be translated into a Dutch word here and stored
/// on the device, which meant an English window showed "Wit", an English label
/// printed it, and the CSV a shop filed carried it too.
///
/// What lands on the model is now one of the keys below. A key is not a word and
/// not a code the phone sent, it is the join between the two: <see cref="Mappers"/>
/// turns whatever arrived into it, and the screen, the label and the report turn it
/// into a word in the language the operator is working in.
///
/// The keys are PascalCase because they are pasted straight into the dictionary key
/// "Color_&lt;key&gt;", so a key and its string are always spelled the same way.
/// </summary>
public static class ColorKeys
{
    public const string Black = "Black";
    public const string White = "White";
    public const string Red = "Red";
    public const string Orange = "Orange";
    public const string Gold = "Gold";
    public const string Green = "Green";
    public const string Blue = "Blue";
    public const string LightBlue = "LightBlue";
    public const string Purple = "Purple";
    public const string Pink = "Pink";
    public const string Grey = "Grey";
    public const string Silver = "Silver";
    public const string RoseGold = "RoseGold";
    public const string SpaceGrey = "SpaceGrey";
    public const string MidnightGreen = "MidnightGreen";
    public const string PacificBlue = "PacificBlue";
    public const string Graphite = "Graphite";
    public const string SierraBlue = "SierraBlue";
    public const string AlpineGreen = "AlpineGreen";
    public const string Midnight = "Midnight";
    public const string Starlight = "Starlight";
    public const string Coral = "Coral";
    public const string Obsidian = "Obsidian";
    public const string Porcelain = "Porcelain";
    public const string Hazel = "Hazel";
    public const string Rose = "Rose";
    public const string Charcoal = "Charcoal";
    public const string Mint = "Mint";
    public const string Navy = "Navy";
    public const string Sage = "Sage";
    public const string Olive = "Olive";
    public const string Cream = "Cream";
    public const string Beige = "Beige";
    public const string Brown = "Brown";
    public const string Teal = "Teal";
    public const string Lilac = "Lilac";
    public const string Lavender = "Lavender";
    public const string Violet = "Violet";
    public const string Titanium = "Titanium";
    public const string Sand = "Sand";
    public const string Clear = "Clear";

    /// <summary>What a colour that matched nothing is called.</summary>
    public const string Unknown = "Unknown";

    /// <summary>
    /// Every key the tables can produce.
    ///
    /// The dictionaries are checked against this, so a colour that gains a key and
    /// no string shows up as a failing test rather than as the raw key on a label.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Black, White, Red, Orange, Gold, Green, Blue, LightBlue, Purple, Pink,
        Grey, Silver, RoseGold, SpaceGrey, MidnightGreen, PacificBlue, Graphite,
        SierraBlue, AlpineGreen, Midnight, Starlight, Coral, Obsidian, Porcelain,
        Hazel, Rose, Charcoal, Mint, Navy, Sage, Olive, Cream, Beige, Brown, Teal,
        Lilac, Lavender, Violet, Titanium, Sand, Clear, Unknown,
    ];

    /// <summary>The dictionary key a colour is spelled under.</summary>
    public static string ResourceKey(string key) => "Color_" + key;

    /// <summary>
    /// The colour as a word, in whatever language the caller can look one up in.
    ///
    /// The fallback is the key with its capitals turned back into spaces, because a
    /// key that has lost its dictionary entry should read as a colour rather than as
    /// a variable name, and this has to hold in a language it does not know either.
    /// </summary>
    public static string Word(string? key, Func<string, string?> lookup)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";

        string word = lookup(key) ?? "";
        return word.Length > 0 ? word : Spaced(key);
    }

    private static string Spaced(string key)
    {
        var text = new StringBuilder(key.Length + 4);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c != '_' && i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]))
                text.Append(' ');
            text.Append(c == '_' ? ' ' : c);
        }
        return text.ToString();
    }
}
