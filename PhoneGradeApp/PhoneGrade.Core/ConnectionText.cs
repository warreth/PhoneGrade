using System;
using System.Collections.Generic;

namespace PhoneGrade.Core;

/// <summary>
/// The sentences the connection routes put in front of the technician.
///
/// This is the one place in Core that writes something a person reads, because
/// the address for the phone is worked out here rather than in the view models.
/// Avalonia's dictionaries live in the desktop project, which Core cannot
/// reference, so the desktop hands its language over through <see cref="Language"/>.
/// Both moments where the language is known, the start-up and a change of the
/// setting, go through LocalizationManager.SetLanguage, which is where that
/// handing over happens.
///
/// The two languages are two dictionaries rather than one dictionary behind a
/// language parameter, so a test can walk the keys of one against the other. That
/// is the failure that actually occurs: a sentence written for one language and
/// forgotten in the other, which only ever shows up on a machine set to it.
///
/// Sentences are quoted rather than built out of fragments wherever the routes
/// need a reason. What the connector says about itself stays in the connector's
/// own words and is dropped into the placeholder, because it is quoted material.
/// </summary>
public static class ConnectionText
{
    private const string Dutch = "nl";
    private const string English = "en";

    private static readonly Dictionary<string, string> DutchSentences = new()
    {
        ["openingUsb"] = "Beveiligde verbinding via USB opzetten...",
        ["openingInternet"] = "Beveiligde verbinding via internet opzetten...",
        ["routeUsb"] = "de USB-tunnel",
        ["routeInternet"] = "de veilige verbinding via internet",
        ["routeSeparator"] = " en ",
        ["secureOff"] = "De veilige verbinding staat uit",
        ["routeFailedOne"] = "Geen veilige verbinding: {0} lukte niet",
        ["routeFailedMany"] = "Geen veilige verbinding: {0} lukten niet",
        ["restrictedApis"] = "Camera, microfoon, bewegingssensoren en locatie werken daardoor niet.",
        ["internetStartFailed"] = "De veilige verbinding via internet kon niet worden gestart: {0}",
        ["noConnector"] = "De veilige verbinding via internet komt niet op: er is geen tunnelprogramma (cloudflared).",
        ["gaveUp"] = "De veilige verbinding via internet wordt even niet geprobeerd: de connector is te vaak gestopt.",
        ["noAddress"] = "De veilige verbinding via internet komt niet op. De connector zei: {0}"
    };

    private static readonly Dictionary<string, string> EnglishSentences = new()
    {
        ["openingUsb"] = "Setting up the secure connection over USB...",
        ["openingInternet"] = "Setting up the secure connection over the internet...",
        ["routeUsb"] = "the usb tunnel",
        ["routeInternet"] = "the secure connection over the internet",
        ["routeSeparator"] = " and ",
        ["secureOff"] = "The secure connection is switched off",
        ["routeFailedOne"] = "No secure connection: {0} could not be opened",
        ["routeFailedMany"] = "No secure connection: {0} could not be opened",
        ["restrictedApis"] = "Camera, microphone, motion sensors and location do not work because of it.",
        ["internetStartFailed"] = "The secure connection over the internet could not be started: {0}",
        ["noConnector"] = "The secure connection over the internet does not come up: there is no tunnel program (cloudflared).",
        ["gaveUp"] = "The secure connection over the internet is not attempted for now: the connector stopped too often.",
        ["noAddress"] = "The secure connection over the internet does not come up. The connector said: {0}"
    };

    private static string _language = Dutch;

    /// <summary>
    /// "nl" or "en". Anything else falls back to Dutch, which is what the
    /// dictionaries under Resources do as well.
    /// </summary>
    public static string Language
    {
        get => _language;
        set => _language = string.Equals(value, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Dutch;
    }

    /// <summary>The keys one language carries, so a test can compare the two.</summary>
    public static IReadOnlyCollection<string> Keys(string language) => Source(language).Keys;

    /// <summary>A sentence in the language currently in use.</summary>
    public static string Get(string key) => In(_language, key);

    /// <summary>A sentence in one named language, whichever is in use.</summary>
    public static string In(string language, string key)
    {
        if (!Source(language).TryGetValue(key, out string? sentence))
            throw new KeyNotFoundException($"ConnectionText carries no sentence '{key}' for {language}.");

        return sentence;
    }

    /// <summary>The sentence in use, with its placeholders filled.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(In(_language, key), args);

    private static Dictionary<string, string> Source(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? EnglishSentences
            : DutchSentences;
}
