using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using PhoneGrade.Tests;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Every language file has to carry the same keys.
///
/// A missing key does not throw anywhere: the binding simply draws nothing, so an
/// English operator ends up with blank rows in the settings drawer and nobody
/// notices until it ships. Two of them were in fact missing before this.
///
/// Dutch is the reference. It is the product's own language, so it is the one
/// every other file is read against, and the one an unusable answer falls back to.
/// </summary>
[Collection(LanguageCollection.Name)]
public class LocalizationTests
{
    private const string Reference = "Strings.nl.axaml";
    /// <summary>
    /// Every dictionary that has to exist, in the order the app lists them.
    ///
    /// Read from the shipped language table rather than written out here, so a new
    /// language is covered by these tests from the moment it is added and a
    /// dictionary that is not wired into the picker is a failure instead of a file
    /// nobody opens. <see cref="EveryShippedLanguageHasItsOwnDictionary"/> is what
    /// holds the two sides to each other.
    /// </summary>
    private static string[] Files => PhoneGrade.UI.Services.SupportedLanguages.All
        .Select(language => $"Strings.{language.Code}.axaml")
        .ToArray();

    private static string[] Keys(string file) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Value)
            .ToArray();

    [Fact]
    public void EveryLanguageDefinesTheSameKeysAsDutch()
    {
        string[] reference = Keys(Reference);

        foreach (string file in Files.Where(file => file != Reference))
        {
            Assert.Equal(
                reference.OrderBy(key => key, StringComparer.Ordinal),
                Keys(file).OrderBy(key => key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The shipped table and the dictionaries on disk are the same set, in both
    /// directions.
    ///
    /// A language in the picker with no file behind it draws raw keys on every
    /// screen the moment an operator picks it, and nothing else in this file would
    /// notice: the parity checks only look at files that exist. A dictionary on disk
    /// with no entry in the table is the other direction of the same fault, and it
    /// is one a build can carry without anyone being able to reach the language.
    /// </summary>
    [Fact]
    public void EveryShippedLanguageHasItsOwnDictionary()
    {
        var onDisk = Directory
            .GetFiles(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources"), "Strings.*.axaml")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var shipped = Files.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(shipped, onDisk);
    }

    /// <summary>
    /// Every shipped dictionary loads as a resource dictionary and answers for
    /// every key the Dutch one carries.
    ///
    /// Loading through Avalonia rather than parsing the file as XML is the point: a
    /// dictionary with a typo in its root element parses as XML perfectly well and
    /// only fails once the loader is handed it, which is the moment a window opens
    /// without any wording on it.
    /// </summary>
    [AvaloniaFact]
    public void EveryShippedDictionaryLoadsAndAnswersForEveryKey()
    {
        string[] keys = Keys(Reference);

        foreach (string language in PhoneGrade.UI.Services.SupportedLanguages.Codes)
        {
            var loaded = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri($"avares://PhoneGrade.UI/Resources/Strings.{language}.axaml"));

            Assert.Equal(keys.Length, loaded.Count);
            foreach (string key in keys)
            {
                Assert.True(loaded.TryGetResource(key, null, out object? value),
                    $"Strings.{language}.axaml does not load {key}");
                Assert.False(string.IsNullOrWhiteSpace(value as string),
                    $"{language} leaves {key} empty");
            }
        }
    }

    /// <summary>
    /// Switching the language settles on one the build carries, and the manager
    /// reports the same code that goes out to the phone suite.
    ///
    /// A language this build does not carry becomes Dutch rather than leaving the
    /// previous dictionary on screen, because a settings file written by a build
    /// with more languages is exactly what arrives otherwise.
    /// </summary>
    [AvaloniaFact]
    public void SwitchingToALanguageThisBuildDoesNotCarryFallsBackToDutch()
    {
        foreach (string language in PhoneGrade.UI.Services.SupportedLanguages.Codes)
        {
            PhoneGrade.UI.Services.LocalizationManager.SetLanguage(language);
            Assert.Equal(language, PhoneGrade.UI.Services.LocalizationManager.CurrentLanguage);
        }

        PhoneGrade.UI.Services.LocalizationManager.SetLanguage("sv");
        Assert.Equal(
            PhoneGrade.UI.Services.SupportedLanguages.DefaultCode,
            PhoneGrade.UI.Services.LocalizationManager.CurrentLanguage);

        PhoneGrade.UI.Services.LocalizationManager.SetLanguage("");
        Assert.Equal(
            PhoneGrade.UI.Services.SupportedLanguages.DefaultCode,
            PhoneGrade.UI.Services.LocalizationManager.CurrentLanguage);

        PhoneGrade.UI.Services.LocalizationManager.SetLanguage(
            PhoneGrade.UI.Services.SupportedLanguages.DefaultCode);
    }

    /// <summary>
    /// The name shown in the picker and the code that gets saved have to be two
    /// ends of the same thing.
    ///
    /// The dropdown speaks in names and the settings file in codes, and nothing
    /// checks that a name resolves: a name that resolved to nothing would save an
    /// empty language, and the next launch would come up in Dutch with the picker
    /// still showing the language that was picked.
    /// </summary>
    [Fact]
    public void EveryNameInThePickerResolvesToItsOwnCodeAndBack()
    {
        foreach (var language in PhoneGrade.UI.Services.SupportedLanguages.All)
        {
            Assert.Equal(language.Code,
                PhoneGrade.UI.Services.SupportedLanguages.CodeOf(language.Name));
            Assert.Equal(language.Name,
                PhoneGrade.UI.Services.SupportedLanguages.NameOf(language.Code));
        }

        // The two names a saved file could plausibly hold from an older build.
        Assert.Equal("en", PhoneGrade.UI.Services.SupportedLanguages.CodeOf("English"));
        Assert.Equal("nl", PhoneGrade.UI.Services.SupportedLanguages.CodeOf("Nederlands"));
        Assert.Equal(
            PhoneGrade.UI.Services.SupportedLanguages.DefaultCode,
            PhoneGrade.UI.Services.SupportedLanguages.CodeOf("Klingon"));
    }

    [Fact]
    public void EachLanguageDefinesEachKeyOnce()
    {
        foreach (string file in Files)
        {
            string[] duplicated = Keys(file)
                .GroupBy(key => key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.True(duplicated.Length == 0,
                $"{file} defines {string.Join(", ", duplicated)} more than once");
        }
    }

    /// <summary>
    /// No em dash, no en dash and no emoji in the wording an operator reads.
    ///
    /// The rule is written down for everything this project produces, and a
    /// character that arrives by paste rather than by decision is the kind of
    /// fault nobody reports: it draws, it just does not belong. Measured on
    /// both dictionaries instead of on one screen, because a character in
    /// either language is a character on screen.
    ///
    /// The tick and the cross are left out on purpose. They are drawn as button
    /// glyphs rather than written into a sentence, and the views already hold
    /// them to their own list.
    /// </summary>
    [Fact]
    public void NeitherLanguageCarriesADashOrAnEmoji()
    {
        foreach (string file in Files)
        {
            // Loaded through the document rather than read as bytes, because a
            // character written as an entity reaches the screen all the same.
            string wording = string.Concat(
                XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
                    .Descendants()
                    .Select(node => node.Value));
            var found = new List<string>();

            for (int i = 0; i < wording.Length; i++)
            {
                int code = wording[i];

                // Everything written above the first plane is a surrogate pair,
                // and the pictographs all live up there.
                if (char.IsHighSurrogate(wording[i]) && i + 1 < wording.Length && char.IsLowSurrogate(wording[i + 1]))
                {
                    code = char.ConvertToUtf32(wording[i], wording[i + 1]);
                    i++;
                }

                // En dash, em dash, the emoji variation selector, the warning
                // sign, the cross and the tick.
                if (code is 0x2013 or 0x2014 or 0xFE0F or 0x26A0 or 0x274C or 0x2705 or > 0xFFFF)
                {
                    found.Add($"U+{code:X4}");
                }
            }

            Assert.True(found.Count == 0,
                $"{file} writes {string.Join(", ", found.Distinct())} into wording an operator reads");
        }
    }

    private static string Value(string file, string key) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key" && attribute.Value == key)
            .Select(attribute => attribute.Parent!.Value)
            .First();

    /// <summary>
    /// The line that says why no phone is being found is drawn in a pill about
    /// 180 pixels wide, which at this font size is roughly thirty two Latin
    /// characters: everything past that is cut off with an ellipsis, so an
    /// instruction written after the first sentence never reaches the operator.
    /// It has to fit inside the pill and it has to name the button that opens
    /// the diagnostics, which used to be a tab that does not exist.
    ///
    /// Two budgets rather than one. Chinese carries one character per idea, so it
    /// reaches the same meaning in nine characters where Dutch needs thirty two;
    /// holding it to the Latin budget would either demand a sentence with padding
    /// in it or force a translation that says less. What matters is that the
    /// sentence fits and names the diagnostics, and a language that is not written
    /// in Latin script says the latter in its own word for it rather than in ours.
    /// </summary>
    [Fact]
    public void TheMissingToolsStatusFitsItsPillAndPointsAtTheDiagnostics()
    {
        foreach (var language in PhoneGrade.UI.Services.SupportedLanguages.All)
        {
            string file = $"Strings.{language.Code}.axaml";
            string status = Value(file, "Status_ToolsMissing");

            // A character in a CJK sentence is about as wide as a full Latin word,
            // which is why the pill fits fewer of them. Counted the same way for
            // every language that is not written in Latin script.
            int budget = language.Code == "zh" ? 16 : 32;

            Assert.True(status.Length <= budget,
                $"{file} writes {status.Length} characters where the pill shows about {budget}: {status}");
        }

        // The instruction has to point at the diagnostics. In the languages written
        // in Latin script that is checked on the letters of the word, because that
        // is what the operator has to recognise on screen. Chinese cannot contain
        // them, so for that one language the check is that the sentence was
        // translated at all: a Chinese reader sees 诊断, which is the same button.
        //
        // Compared without the accent, because Spanish and Portuguese spell it
        // "diagnóstico" and the accent sits between the g and the s. Checking the
        // bare letters would fail on the correct spelling and pass on a misspelling
        // that dropped the accent to satisfy this line, which is the wrong way
        // round.
        foreach (string file in Files.Where(file => !file.Contains(".zh.")))
        {
            string status = Value(file, "Status_ToolsMissing");
            Assert.Contains("diagnos", WithoutAccents(status).ToLowerInvariant());
        }

        Assert.Contains("诊断", Value("Strings.zh.axaml", "Status_ToolsMissing"));
    }

    /// <summary>
    /// The letters of a word with the accents taken off, for a check that is about
    /// which word was used rather than how it is spelled.
    /// </summary>
    private static string WithoutAccents(string value) =>
        new string(value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

    /// <summary>
    /// Wording belongs in the string dictionaries, not in the views.
    ///
    /// A literal in a Text attribute is invisible to the language switch: half the
    /// window follows along and half stays behind in whatever it was written in,
    /// which reads as a product nobody finished. This walks every view and names
    /// the literals it finds; the short list below is the deliberate exception
    /// (a brand name, a logo, a badge, a step number, a grade letter).
    /// </summary>
    [Fact]
    public void ViewsCarryNoWordingOfTheirOwn()
    {
        string[] allowed = { "PG", "PhoneGrade", "USB", "1", "2", "3", "4", "A", "B", "C", "✕", "\u2715" };
        string[] textAttributes = { "Text", "Content", "Watermark", "Header", "Tip", "ToolTip.Tip" };

        var literals = new List<string>();
        foreach (string view in Directory.GetFiles(
            RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Views"), "*.axaml"))
        {
            var document = XDocument.Load(view);
            foreach (XAttribute attribute in document.Descendants().Attributes())
            {
                if (!textAttributes.Contains(attribute.Name.LocalName)) continue;
                string value = attribute.Value.Trim();
                if (value.Length == 0) continue;
                if (value.StartsWith("{")) continue; // binding or resource reference
                if (allowed.Contains(value)) continue;
                literals.Add($"{Path.GetFileName(view)}: {attribute.Name}=\"{value}\"");
            }
        }

        Assert.True(literals.Count == 0,
            "these belong in the Strings dictionaries:\n" + string.Join("\n", literals));
    }

    [Fact]
    public void TheSecureOriginSettingsAreSpelledOutInBothLanguages()
    {
        // These are the rows that decide whether a phone gets camera and motion
        // access at all, so neither may end up blank.
        foreach (string file in Files)
        {
            string[] keys = Keys(file);
            Assert.Contains("Settings_SecureOrigin", keys);
            Assert.Contains("Settings_SecureOriginDesc", keys);
            Assert.Contains("Settings_PublicTunnel", keys);
            Assert.Contains("Settings_PublicTunnelDesc", keys);
        }
    }

    /// <summary>
    /// The code asks for its wording by key while it runs, and a key nobody
    /// defined comes back as itself, so a typo shows up on the status line as
    /// "Session_FailedSkipped". Every key the code asks for has to be in the
    /// dictionaries; the parity test above then carries it to the other language.
    /// </summary>
    [Fact]
    public void EveryKeyTheCodeAsksForIsDefined()
    {
        string[] defined = Keys(Files[0]);
        var asked = new SortedSet<string>(StringComparer.Ordinal);
        var keyOfCall = new Regex(@"GetString\(\s*""([^""]+)""");

        foreach (string file in Directory.GetFiles(
            RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            foreach (Match match in keyOfCall.Matches(File.ReadAllText(file)))
                asked.Add(match.Groups[1].Value);
        }

        Assert.NotEmpty(asked);
        string[] missing = asked.Where(key => !defined.Contains(key)).ToArray();
        Assert.True(missing.Length == 0,
            "the code asks for these and neither dictionary defines them: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// Every sentence that carries a value has to carry the same number of
    /// placeholders in every language.
    ///
    /// These are format strings filled in at runtime with the queue name, the file
    /// path or the platform's own reason. A translation that drops {0} is not a
    /// typo, it is a status line that reads "DYMO_LabelWriter" with no sentence
    /// around it, or a FormatException that blanks the line that said why the
    /// export failed. Dutch is the reference for how many there are.
    /// </summary>
    [Fact]
    public void EveryWordingWithAPlaceholderHasTheSameCountInEveryLanguage()
    {
        Dictionary<string, string> reference = Values(Reference);

        var problems = new List<string>();
        foreach (string file in Files.Where(file => file != Reference))
        {
            Dictionary<string, string> values = Values(file);

            foreach (KeyValuePair<string, string> entry in reference)
            {
                int wanted = Placeholders(entry.Value);
                if (wanted == 0) continue;

                if (!values.TryGetValue(entry.Key, out string? other)) continue;

                int got = Placeholders(other);
                if (got != wanted)
                    problems.Add($"{entry.Key} has {got} in {file} and {wanted} in Dutch");
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>
    /// A language cannot be a copy of Dutch with a different file name.
    ///
    /// The parity checks above pass on a copied file: the keys are the same and
    /// the placeholders are the same. What gives it away is the wording, and an
    /// operator picking Spanish and reading Dutch would have no way to tell the
    /// app had not heard them.
    /// </summary>
    [Fact]
    public void NoLanguageIsDutchWrittenOutAgain()
    {
        Dictionary<string, string> dutch = Values(Reference);
        int count = dutch.Count;

        foreach (string file in Files.Where(file => file != Reference))
        {
            Dictionary<string, string> values = Values(file);
            int identical = dutch.Count(entry => values.TryGetValue(entry.Key, out string? other)
                                                && other == entry.Value);

            Assert.True(identical < count / 2,
                $"{file} has {identical} of {count} sentences the same as Dutch");
        }
    }

    private static Dictionary<string, string> Values(string file) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Parent!)
            .Where(node => node.Name.LocalName == "String")
            .ToDictionary(
                node => node.Attributes().First(a => a.Name.LocalName == "Key").Value,
                node => node.Value,
                StringComparer.Ordinal);

    /// <summary>
    /// How many placeholders a sentence carries. Counts {0} and {1} but not the
    /// doubled brace, so a sentence that wants a literal brace is not mistaken for
    /// one that wants a value.
    /// </summary>
    private static int Placeholders(string value)
    {
        int count = 0;
        for (int i = 0; i + 1 < value.Length; i++)
        {
            if (value[i] != '{' || value[i + 1] == '{') continue;
            if (int.TryParse(value.AsSpan(i + 1), out int index) && index >= 0) count++;
        }

        return count;
    }
}
