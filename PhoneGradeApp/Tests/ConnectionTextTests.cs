using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the wording the connection routes hand to the window.
///
/// The sentences live in Core, which cannot see Avalonia's dictionaries, so the
/// desktop passes its language down instead. Two things can go wrong here and
/// both of them only ever show up on somebody's machine: a sentence written for
/// one language and forgotten in the other, and the routes carrying on with the
/// old language after the setting was changed. Both are pinned rather than
/// assumed, because a status line in the wrong language is the sort of thing
/// nobody reports and everybody sees.
/// </summary>
public class ConnectionTextTests
{
    [Fact]
    public void BothLanguagesCarryTheSameKeys()
    {
        string[] dutch = ConnectionText.Keys("nl")
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        string[] english = ConnectionText.Keys("en")
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(dutch);
        Assert.Equal(dutch, english);
    }

    [Fact]
    public void NoSentenceIsCarriedOverUntranslated()
    {
        foreach (string key in ConnectionText.Keys("nl"))
        {
            string dutch = ConnectionText.In("nl", key);
            string english = ConnectionText.In("en", key);

            Assert.False(string.IsNullOrWhiteSpace(dutch),
                $"The sentence '{key}' is empty in Dutch.");
            Assert.False(string.IsNullOrWhiteSpace(english),
                $"The sentence '{key}' is empty in English.");
            Assert.True(!string.Equals(dutch, english, StringComparison.Ordinal),
                $"The sentence '{key}' was never translated out of Dutch.");
        }
    }

    [Fact]
    public void TheLanguageAcceptsWhatTheSettingsHandOver()
    {
        string original = ConnectionText.Language;

        try
        {
            ConnectionText.Language = "EN";
            Assert.Equal("en", ConnectionText.Language);
            Assert.Equal(ConnectionText.In("en", "secureOff"), ConnectionText.Get("secureOff"));

            // The setting only ever holds "en" or "nl", but a value it does not
            // know has to leave the technician on a language they can read.
            ConnectionText.Language = "de";
            Assert.Equal("nl", ConnectionText.Language);
            Assert.Equal(ConnectionText.In("nl", "secureOff"), ConnectionText.Get("secureOff"));
        }
        finally
        {
            ConnectionText.Language = original;
        }
    }

    [Fact]
    public async Task TheWarningAndTheStatusLineFollowTheLanguage()
    {
        // The technician picks English once and every route after it has to speak
        // it, including the reason for the address that ended up in the QR code.
        string original = ConnectionText.Language;

        try
        {
            ConnectionText.Language = "en";

            var seen = new List<string>();
            var resolver = new WebRunnerOriginResolver(
                (_, _) => Task.FromResult(false),
                (_, _) => Task.FromResult<string?>(null));

            WebRunnerOrigin origin = await resolver.ResolveAsync(
                "00008030-001234567890ABCD", 5055,
                "http://192.168.0.216:5055", "http://localhost:5055",
                secureOriginEnabled: true, publicTunnelEnabled: true,
                onStatus: seen.Add);

            Assert.False(origin.IsSecure);
            Assert.NotNull(origin.Warning);
            Assert.Contains(
                "No secure connection: the secure connection over the internet could not be opened",
                origin.Warning);
            Assert.Contains(
                "Camera, microphone, motion sensors and location do not work",
                origin.Warning);
            Assert.DoesNotContain("verbinding", origin.Warning);

            Assert.Contains("Setting up the secure connection over the internet...", seen);
            Assert.DoesNotContain(seen, message => message.Contains("opzetten", StringComparison.Ordinal));
        }
        finally
        {
            ConnectionText.Language = original;
        }
    }

    [Fact]
    public async Task TheConnectorComplaintsAreFramedInTheChosenLanguage()
    {
        // Downloading the connector happens on demand, so "there is none" is an
        // everyday answer and it lands on the status line rather than the log.
        string original = ConnectionText.Language;

        try
        {
            ConnectionText.Language = "en";

            var seen = new List<string>();
            var tunnel = new QuickTunnel(
                (_, _, _) => Task.FromResult<QuickTunnel.IConnection>(null!),
                () => Task.FromResult<string?>(null));

            Assert.Null(await tunnel.StartAsync(5055, seen.Add));

            Assert.NotEmpty(seen);
            Assert.Contains(seen, line => line.Contains(
                "there is no tunnel program", StringComparison.Ordinal));
            Assert.DoesNotContain(seen, line => line.Contains(
                "tunnelprogramma", StringComparison.Ordinal));
            tunnel.Dispose();
        }
        finally
        {
            ConnectionText.Language = original;
        }
    }
}
