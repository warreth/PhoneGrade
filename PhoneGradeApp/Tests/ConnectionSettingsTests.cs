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

    [Fact]
    public void TheHeadingIsWrittenOutOnBothLanguages()
    {
        foreach (var file in new[] { "Strings.nl.axaml", "Strings.en.axaml" })
        {
            string strings = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Resources", file);

            Assert.True(strings.Contains("x:Key=\"Settings_Connection\""),
                $"{file} has to carry the heading, or the drawer shows the raw key");
        }
    }
}
