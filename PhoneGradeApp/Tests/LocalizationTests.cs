using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PhoneGrade.Tests;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Both language files have to carry the same keys.
///
/// A missing key does not throw anywhere: the binding simply draws nothing, so an
/// English operator ends up with blank rows in the settings drawer and nobody
/// notices until it ships. Two of them were in fact missing before this.
/// </summary>
public class LocalizationTests
{
    private static readonly string[] Files = { "Strings.nl.axaml", "Strings.en.axaml" };

    private static string[] Keys(string file) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Value)
            .ToArray();

    [Fact]
    public void BothLanguagesDefineTheSameKeys()
    {
        string[] dutch = Keys(Files[0]);
        string[] english = Keys(Files[1]);

        Assert.Equal(
            dutch.OrderBy(key => key, StringComparer.Ordinal),
            english.OrderBy(key => key, StringComparer.Ordinal));
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
            "these belong in Strings.nl.axaml / Strings.en.axaml:\n" + string.Join("\n", literals));
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
}
