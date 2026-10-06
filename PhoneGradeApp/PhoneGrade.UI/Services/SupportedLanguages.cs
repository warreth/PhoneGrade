using System;
using System.Collections.Generic;
using System.Linq;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The languages the app ships in, and the one place that says which they are.
///
/// Everything else asks this table rather than repeating the list: the settings
/// dropdown, the switch that swaps the resource dictionary, and the check that
/// every language here actually has a file behind it. The old arrangement was a
/// chain of equality checks (<c>en</c> or <c>nl</c>) in three files, which is the
/// kind of list that grows to seven entries while three of the seven still fall
/// back to Dutch.
///
/// A language is named in its own script. A shop that reads Spanish sees
/// "Espanol" rather than "Spanish", because the person picking is looking for the
/// word they know rather than for a translation of it, and the row they are
/// standing in front of says which one it is.
/// </summary>
public static class SupportedLanguages
{
    /// <summary>One shipped language: the code the dictionaries are filed under, and the name to show.</summary>
    /// <param name="Code">Two letters, lower case: the file name and the value the phone suite is told.</param>
    /// <param name="Name">The language in its own script, which is what the dropdown shows.</param>
    /// <param name="NativeName">
    /// The same name again, for the places that need to read it as plain Latin
    /// letters. Chinese has none that a reader in a dropdown can recognise, so it
    /// carries the English name there instead.
    /// </param>
    public sealed record Language(string Code, string Name, string NativeName)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// The shipped languages, in the order the dropdown lists them: the two the
    /// app already spoke, then the rest by English name.
    /// </summary>
    public static readonly IReadOnlyList<Language> All = new[]
    {
        new Language("nl", "Nederlands", "Dutch"),
        new Language("en", "English", "English"),
        new Language("de", "Deutsch", "German"),
        new Language("es", "Espanol", "Spanish"),
        new Language("fr", "Francais", "French"),
        new Language("pt", "Portugues", "Portuguese (Brazil)"),
        new Language("zh", "中文", "Chinese (Simplified)")
    };

    /// <summary>
    /// The language the app starts in and the one an unusable answer falls back
    /// to. Dutch is the product's own language, which is why a missing answer
    /// reads Dutch rather than English.
    /// </summary>
    public const string DefaultCode = "nl";

    /// <summary>Every code, for the callers that iterate rather than look up.</summary>
    public static IEnumerable<string> Codes => All.Select(language => language.Code);

    /// <summary>The dropdown, as the strings a ComboBox binds to.</summary>
    public static string[] Names => All.Select(language => language.Name).ToArray();

    /// <summary>True when <paramref name="code"/> is a language this build carries.</summary>
    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        All.Any(language => string.Equals(language.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The code to use for whatever came in: a stored setting, a query string or
    /// the phone's own language. Anything this build does not carry becomes
    /// <see cref="DefaultCode"/>, so a saved language from a later build reads as
    /// Dutch rather than as a raw key on every screen.
    /// </summary>
    public static string Normalize(string? code)
    {
        string trimmed = (code ?? "").Trim().ToLowerInvariant();
        return IsSupported(trimmed) ? trimmed : DefaultCode;
    }

    /// <summary>The name to show for <paramref name="code"/>, in its own script.</summary>
    public static string NameOf(string? code)
    {
        string wanted = Normalize(code);
        return All.First(language => language.Code == wanted).Name;
    }

    /// <summary>
    /// The code behind a name from the dropdown. The two directions have to agree,
    /// because one is what the operator picks and the other is what gets saved:
    /// a name that resolves to nothing would save an empty language and leave the
    /// next launch on Dutch with the dropdown showing something else.
    /// </summary>
    public static string CodeOf(string? name)
    {
        string wanted = (name ?? "").Trim();
        return All.FirstOrDefault(language =>
                string.Equals(language.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(language.NativeName, wanted, StringComparison.OrdinalIgnoreCase))
            ?.Code ?? DefaultCode;
    }
}