using PhoneGrade.Core;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The colour as the operator reads it, in the language they are working in.
///
/// The model carries a <see cref="ColorKeys"/> key rather than a word, because the
/// word used to be Dutch and was written onto the device as it was read. An English
/// window then showed "Wit", the English label printed it, and the CSV a shop filed
/// carried it too. The key is the join: <see cref="Mappers"/> produces it, this
/// spells it.
/// </summary>
public static class ColorWording
{
    /// <summary>The colour, or the not-reported wording when the phone sent none.</summary>
    public static string Word(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == DevicePlaceholders.Color)
            return LocalizationManager.GetString("Value_NotSet");

        return ColorKeys.Word(key, Lookup);
    }

    /// <summary>
    /// The colour as it belongs on paper.
    ///
    /// A label keeps the placeholder rather than the sentence the screen shows: the
    /// file that carries it says "NOCOLOR", and an operator reading a printed tag
    /// needs to see that the platform gave nothing rather than being told so in a
    /// sentence that belongs on a screen.
    /// </summary>
    public static string OnLabel(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == DevicePlaceholders.Color)
            return DevicePlaceholders.Color;

        return ColorKeys.Word(key, Lookup);
    }

    private static string? Lookup(string key)
    {
        string resourceKey = ColorKeys.ResourceKey(key);
        string word = LocalizationManager.GetString(resourceKey);

        // GetString hands back the key itself when the dictionary has no entry, which
        // on screen would read as a variable rather than as a colour.
        return word == resourceKey ? null : word;
    }
}
