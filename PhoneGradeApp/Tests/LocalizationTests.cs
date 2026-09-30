using System;
using System.Linq;
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
}
