using System;
using PhoneGrade.Tests;
using Xunit;

namespace Tests;

/// <summary>
/// The two settings that decide which address the phone is given.
///
/// They sat at the very bottom of the drawer, past the advanced list, with no
/// heading of their own, so they read as two more advanced toggles rather than
/// as the choice between a cable and the internet. Everything a phone needs to
/// reach this pc is in one place, under a title the operator can find.
/// </summary>
public class ConnectionSettingsTests
{
    private static string View => RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");

    private static int IndexOf(string haystack, string needle, int from = 0) =>
        haystack.IndexOf(needle, from, StringComparison.Ordinal);

    [Fact]
    public void TheTwoConnectionOptionsHaveAGroupOfTheirOwn()
    {
        string view = View;

        int heading = IndexOf(view, "Text=\"{DynamicResource Settings_Connection}\"");
        Assert.True(heading > 0, "the group needs a heading the operator can read");

        int licensing = IndexOf(view, "Settings_LicensingCardTitle", heading);
        Assert.True(licensing > heading, "the group has to end before the licensing card");

        string group = view.Substring(heading, licensing - heading);

        Assert.Contains("UseSecureOrigin", group);
        Assert.Contains("UsePublicTunnel", group);

        // Both descriptions travel with the choice, or the heading is a title
        // over two words with nothing to say what they do.
        Assert.Contains("Settings_SecureOriginDesc", group);
        Assert.Contains("Settings_PublicTunnelDesc", group);
    }

    [Fact]
    public void TheTwoConnectionOptionsAreNotInTheAdvancedListAnyMore()
    {
        string view = View;

        int advanced = IndexOf(view, "Text=\"{DynamicResource Settings_Advanced}\"");
        Assert.True(advanced > 0, "the advanced section is the one they were moved out of");

        // From that heading to the end of the drawer. Bounding it at the advanced
        // list's own closing tag would pass either way, because the two boxes
        // were never inside that list; they were loose at the end of the drawer,
        // which is the placement this rules out.
        int end = IndexOf(view, "</ScrollViewer>", advanced);
        string afterAdvanced = view.Substring(advanced, end - advanced);

        Assert.DoesNotContain("UseSecureOrigin", afterAdvanced);
        Assert.DoesNotContain("UsePublicTunnel", afterAdvanced);
    }

    /// <summary>
    /// The connection heading has to be written out in every language.
    ///
    /// A missing key draws the key itself rather than nothing at all, so a
    /// language that lacks it shows a shopper "Settings_Connection" where the
    /// section name belongs.
    /// </summary>
    [Fact]
    public void TheHeadingIsWrittenOutInEveryLanguage()
    {
        foreach (var language in PhoneGrade.UI.Services.SupportedLanguages.All)
        {
            string file = $"Strings.{language.Code}.axaml";
            string strings = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Resources", file);

            Assert.True(strings.Contains("x:Key=\"Settings_Connection\""),
                $"{file} has to carry the heading, or the drawer shows the raw key");
        }
    }

    /// <summary>The value between the opening tag and the closing tag of a key.</summary>
    private static string ValueOf(string strings, string key)
    {
        int at = strings.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(at > 0, $"{key} is missing");

        int open = strings.IndexOf('>', at);
        int close = strings.IndexOf("</x:String>", open, StringComparison.Ordinal);

        Assert.True(open > 0 && close > open, $"{key} is not a plain string entry");
        return strings.Substring(open + 1, close - open - 1);
    }

    [Fact]
    public void TheExplanationAnswersWhetherBothMayBeOnAndWhatAGoodAddressGives()
    {
        // Two checkboxes next to each other read as a choice, and the operator
        // has to guess whether turning both on is allowed. It is, and which one
        // runs first is the other half of the answer. The list of what a secure
        // address buys also has to carry location, because that is the step that
        // skips without one.
        (string file, string both, string triedFirst, string location)[] languages =
        {
            ("Strings.nl.axaml", "Beide mogen aan staan", "als eerste geprobeerd", "locatie"),
            ("Strings.en.axaml", "Both may be on at once", "tried first", "location")
        };

        foreach (var language in languages)
        {
            string strings = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Resources", language.file);
            string together = ValueOf(strings, "Settings_SecureOriginDesc") + " "
                            + ValueOf(strings, "Settings_PublicTunnelDesc");

            Assert.Contains(language.both, together);
            Assert.Contains(language.triedFirst, together);
            Assert.Contains(language.location, together);
        }
    }
}
