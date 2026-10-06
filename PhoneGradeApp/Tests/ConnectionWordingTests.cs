using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Services;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the wording the connection routes hand to the window.
///
/// Core names the step and carries what a failing route said about itself; the
/// sentence around those facts comes from the dictionaries the rest of the
/// window reads. Two things can go wrong here and both only ever show up on
/// somebody's machine: a step with no sentence of its own, which falls through
/// to the enum member's own name and reaches the status line as "NoAddress", and
/// a window set to English reading Dutch under the QR code. Both are pinned
/// rather than assumed, because a status line in the wrong language is the sort
/// of thing nobody reports and everybody sees.
/// </summary>
[Collection(LanguageCollection.Name)]
public class ConnectionWordingTests
{
    /// <summary>Stands in for the connector's own output, which is quoted.</summary>
    private const string Quote = "ERRTunnel instance limited";

    private const string IphoneUdid = "00008030-001234567890ABCD";
    private const string Lan = "http://192.168.0.216:5055";
    private const string Loopback = "http://localhost:5055";

    /// <summary>
    /// Every step the routes can report has to reach the operator as a sentence.
    ///
    /// The switch in <see cref="ConnectionWording.Say"/> has a fallback, because
    /// an unhandled step has to read as something rather than as nothing at all,
    /// and a fallback is exactly what makes a forgotten step look fine in review.
    /// Walking the enum is what makes a step added without a sentence fail here
    /// instead of on somebody's status line.
    /// </summary>
    [AvaloniaFact]
    public void EveryStepTheRoutesCanReport_ReachesTheOperatorAsASentence()
    {
        foreach (ConnectionStep step in Enum.GetValues<ConnectionStep>())
        {
            string sentence = ConnectionWording.Say(new ConnectionNotice(step, Quote));

            Assert.True(!string.IsNullOrWhiteSpace(sentence),
                $"{step} has no sentence in either dictionary");

            // The fallback hands back the detail, or the enum member's own name.
            Assert.True(sentence != step.ToString(),
                $"{step} fell through to its own name");
            Assert.True(sentence != Quote,
                $"{step} fell through to its detail");

            // A sentence that still holds a placeholder was never formatted.
            Assert.True(!sentence.Contains('{'),
                $"{step} still holds an unformatted placeholder: {sentence}");
        }
    }

    /// <summary>
    /// A warning has to name the routes that failed rather than the enum members.
    ///
    /// The dictionary of names below has no entry for a route added later, so the
    /// lookup throws: naming a route is a decision somebody has to make, and a
    /// test that enumerates the enum is what stops it being made by default.
    /// </summary>
    [AvaloniaFact]
    public void EveryRouteAWarningCanCarry_IsNamedFromTheDictionary()
    {
        var name = new Dictionary<ConnectionRoute, string>
        {
            [ConnectionRoute.Usb] = "Connect_RouteUsb",
            [ConnectionRoute.Internet] = "Connect_RouteInternet"
        };

        foreach (ConnectionRoute route in Enum.GetValues<ConnectionRoute>())
        {
            string named = LocalizationManager.GetString(name[route]);
            string sentence = ConnectionWording.Warn(
                new ConnectionWarning(new[] { route }, null));

            Assert.True(named != name[route],
                $"{route} asks for {name[route]}, which neither dictionary defines");
            Assert.Contains(named, sentence);
        }
    }

    /// <summary>
    /// The route order decides which route is named first, and the joiner between
    /// them is a word of its own rather than a hard-coded "and".
    /// </summary>
    [AvaloniaFact]
    public void BothRoutesFailing_AreNamedInOrderAndJoinedByTheLanguage()
    {
        string original = LocalizationManager.CurrentLanguage;
        try
        {
            LocalizationManager.SetLanguage("en");
            string englishUsb = LocalizationManager.GetString("Connect_RouteUsb");
            string englishInternet = LocalizationManager.GetString("Connect_RouteInternet");
            string englishJoiner = LocalizationManager.GetString("Connect_Joiner");
            string english = ConnectionWording.Warn(
                new ConnectionWarning(
                    new[] { ConnectionRoute.Usb, ConnectionRoute.Internet }, null));

            LocalizationManager.SetLanguage("nl");
            string dutchUsb = LocalizationManager.GetString("Connect_RouteUsb");
            string dutchInternet = LocalizationManager.GetString("Connect_RouteInternet");
            string dutchJoiner = LocalizationManager.GetString("Connect_Joiner");
            string dutch = ConnectionWording.Warn(
                new ConnectionWarning(
                    new[] { ConnectionRoute.Usb, ConnectionRoute.Internet }, null));

            // Both routes turn up, named, and the first one named is the route that
            // failed first: the order is what tells the operator where to look.
            Assert.Contains(englishUsb, english);
            Assert.Contains(englishInternet, english);
            Assert.True(
                english.IndexOf(englishUsb, StringComparison.Ordinal) <
                english.IndexOf(englishInternet, StringComparison.Ordinal),
                "the usb route is not named before the route it fell back to");

            Assert.Contains(dutchUsb, dutch);
            Assert.Contains(dutchInternet, dutch);
            Assert.Contains($" {dutchJoiner} ", dutch);
            Assert.NotEqual(englishJoiner, dutchJoiner);
        }
        finally
        {
            LocalizationManager.SetLanguage(original);
        }
    }

    /// <summary>
    /// The same facts, worded twice.
    ///
    /// The route hands over which route failed; it says nothing about which
    /// language that is in. Both sentences therefore have to come out of the
    /// dictionaries, and neither may carry a word of the other language over.
    /// </summary>
    [AvaloniaFact]
    public async Task TheWarningAndTheStatusLineFollowTheLanguage()
    {
        string original = LocalizationManager.CurrentLanguage;
        try
        {
            List<ConnectionNotice> seen = new();
            WebRunnerOrigin origin = await InternetRouteFailedWithoutSayingWhy(seen);
            Assert.NotEmpty(seen);

            LocalizationManager.SetLanguage("en");
            string englishWarning = ConnectionWording.Warn(origin.Warning!);
            string englishStatus = ConnectionWording.Say(seen[^1]);

            LocalizationManager.SetLanguage("nl");
            string dutchWarning = ConnectionWording.Warn(origin.Warning!);
            string dutchStatus = ConnectionWording.Say(seen[^1]);

            Assert.Contains("No secure connection: the secure connection over the internet "
                + "could not be opened", englishWarning);
            Assert.Contains("Camera, microphone, motion sensors and location do not work "
                + "because of it.", englishWarning);
            Assert.DoesNotContain("verbinding", englishWarning);

            Assert.Contains("Geen veilige verbinding: de veilige verbinding via internet "
                + "lukte niet", dutchWarning);
            Assert.Contains("Camera, microfoon, bewegingssensoren en locatie werken "
                + "daardoor niet.", dutchWarning);
            Assert.DoesNotContain("No secure connection", dutchWarning);

            Assert.Contains("secure connection over the internet", englishStatus);
            Assert.DoesNotContain("opzetten", englishStatus);
            Assert.Contains("Beveiligde verbinding via internet opzetten", dutchStatus);
            Assert.DoesNotContain("Setting up", dutchStatus);
        }
        finally
        {
            LocalizationManager.SetLanguage(original);
        }
    }

    /// <summary>
    /// A warning with nothing tried at all is the secure origin having been
    /// switched off on purpose. It still owes the operator the reason the camera
    /// and motion steps are about to be unavailable, rather than reading as a
    /// failure of something that was never attempted.
    /// </summary>
    [AvaloniaFact]
    public void AWarningWithNoRouteTriedSaysTheSwitchRatherThanAFailure()
    {
        string original = LocalizationManager.CurrentLanguage;
        try
        {
            LocalizationManager.SetLanguage("en");
            string sentence = ConnectionWording.Warn(
                new ConnectionWarning(Array.Empty<ConnectionRoute>(), null));

            Assert.Contains("secure connection is switched off", sentence);
            Assert.Contains("Camera, microphone, motion sensors and location", sentence);
            Assert.DoesNotContain("could not be opened", sentence);
        }
        finally
        {
            LocalizationManager.SetLanguage(original);
        }
    }

    /// <summary>
    /// What the connector said about itself is quoted material and arrives as it
    /// was written; only the sentence around it changes with the language. This is
    /// the one thing here that may not be translated, because it is somebody
    /// else's words reporting somebody else's failure.
    /// </summary>
    [AvaloniaFact]
    public void TheConnectorComplaintIsQuotedRatherThanTranslated()
    {
        var complaint = new ConnectionNotice(ConnectionStep.NoAddress, Quote);
        string original = LocalizationManager.CurrentLanguage;
        try
        {
            LocalizationManager.SetLanguage("en");
            string english = ConnectionWording.Say(complaint);

            LocalizationManager.SetLanguage("nl");
            string dutch = ConnectionWording.Say(complaint);

            Assert.Contains(Quote, english);
            Assert.Contains(Quote, dutch);
            Assert.NotEqual(english, dutch);
        }
        finally
        {
            LocalizationManager.SetLanguage(original);
        }
    }

    /// <summary>
    /// The connector is downloaded on demand, so the download failing is an
    /// everyday case. The route reports it as a step with no detail of its own,
    /// and the sentence it reaches the operator as comes from the dictionaries
    /// rather than from Core.
    /// </summary>
    [AvaloniaFact]
    public async Task AMissingConnectorIsWordingedByTheWindowNotByCore()
    {
        var tunnel = new QuickTunnel(
            (_, _, _) => Task.FromResult<QuickTunnel.IConnection>(null!),
            () => Task.FromResult<string?>(null));

        var seen = new List<ConnectionNotice>();
        Assert.Null(await tunnel.StartAsync(5055, seen.Add));
        tunnel.Dispose();

        ConnectionNotice notice = Assert.Single(
            seen, step => step.Step == ConnectionStep.NoConnector);

        string original = LocalizationManager.CurrentLanguage;
        try
        {
            LocalizationManager.SetLanguage("en");
            string english = ConnectionWording.Say(notice);
            Assert.Contains("there is no tunnel program (cloudflared)", english);
            Assert.DoesNotContain("tunnelprogramma", english);

            LocalizationManager.SetLanguage("nl");
            string dutch = ConnectionWording.Say(notice);
            Assert.Contains("geen tunnelprogramma (cloudflared)", dutch);
            Assert.DoesNotContain("there is no tunnel program", dutch);
        }
        finally
        {
            LocalizationManager.SetLanguage(original);
        }
    }

    /// <summary>
    /// A sentence written for one language and forgotten in the others only ever
    /// shows up on a machine set to it, so every language is read against Dutch
    /// rather than against a copy kept in the test. The placeholders are compared
    /// for the same reason: a translator dropping the {0} leaves the connector's
    /// complaint out of the sentence entirely.
    /// </summary>
    [Fact]
    public void NoConnectSentenceIsCarriedOverUntranslated()
    {
        Dictionary<string, string> dutch = Entries("Strings.nl.axaml");

        string[] keys = dutch.Keys
            .Where(key => key.StartsWith("Connect_", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(keys);

        foreach (var language in PhoneGrade.UI.Services.SupportedLanguages.All)
        {
            Dictionary<string, string> values = Entries($"Strings.{language.Code}.axaml");

            foreach (string key in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(values[key]), $"{key} is empty in {language.Name}");
                if (language.Code == "nl") continue;

                Assert.True(dutch[key] != values[key], $"{key} was never translated into {language.Name}");

                Assert.Equal(Placeholders(dutch[key]), Placeholders(values[key]));
            }
        }
    }

    /// <summary>An address where the public route fails without saying why.</summary>
    private static async Task<WebRunnerOrigin> InternetRouteFailedWithoutSayingWhy(
        List<ConnectionNotice> seen)
    {
        var resolver = new WebRunnerOriginResolver(
            (_, _) => Task.FromResult(false),
            (_, _) => Task.FromResult<string?>(null));

        return await resolver.ResolveAsync(
            IphoneUdid, 5055, Lan, Loopback,
            secureOriginEnabled: true, publicTunnelEnabled: true, seen.Add);
    }

    private static Dictionary<string, string> Entries(string file) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Where(element => element.Attributes().Any(a => a.Name.LocalName == "Key"))
            .ToDictionary(
                element => element.Attributes().First(a => a.Name.LocalName == "Key").Value,
                element => element.Value.Trim());

    /// <summary>The placeholders a sentence holds, so two can be compared.</summary>
    private static string[] Placeholders(string sentence) =>
        Regex.Matches(sentence, @"\{\d+\}")
            .Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}
