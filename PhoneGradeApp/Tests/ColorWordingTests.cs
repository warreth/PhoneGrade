using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using Xunit;
using PhoneGrade.Tests;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the colour name, in both languages and at every edge it reaches.
///
/// The colour used to be resolved to a Dutch word inside the mapper, and that word
/// was written onto the device as the phone was read. From there it reached
/// everything: an English window showed "Wit", the English label printed it, the
/// report PDF filed it, and the CSV a shop kept carried it too. Nothing caught it,
/// because the word was correct for the language the app happens to default to.
///
/// The value on the model is now a key. These pin the three things that have to
/// stay true: the key is what lands on the device, the word follows the window, and
/// a colour nobody has a word for stays visible rather than being guessed.
/// </summary>
[Collection(LanguageCollection.Name)]
public class ColorWordingTests
{
    [AvaloniaFact]
    public void TheDeviceCarriesAKeyRatherThanAWord()
    {
        // The whole point. A word on the model is what put Dutch on an English label.
        Assert.Equal(ColorKeys.White, Mappers.MapColor("WHT"));
        Assert.Equal(ColorKeys.White, Mappers.MapColor("#ffffff"));
        Assert.Equal(ColorKeys.White, Mappers.MapColor("white"));
        Assert.Equal(ColorKeys.SpaceGrey, Mappers.MapAndroidColor("space gray"));
        Assert.Equal(ColorKeys.RoseGold, Mappers.MapColor("rose gold"));
    }

    [AvaloniaFact]
    public void EveryKeyTheTablesCanProduceHasAWordInBothLanguages()
    {
        foreach (string language in new[] { "en", "nl" })
        {
            LocalizationManager.SetLanguage(language);
            foreach (string key in ColorKeys.All)
            {
                string word = ColorWording.Word(key);
                Assert.False(word == key || word.Length == 0,
                    $"{language} has no word for Color_{key}");
            }
        }
    }

    [AvaloniaFact]
    public void TheWordFollowsTheWindowRatherThanTheFirstLanguageLoaded()
    {
        LocalizationManager.SetLanguage("en");
        string english = ColorWording.Word(ColorKeys.White);
        string englishUnknown = ColorWording.Word(ColorKeys.Unknown);

        LocalizationManager.SetLanguage("nl");
        string dutch = ColorWording.Word(ColorKeys.White);
        string dutchUnknown = ColorWording.Word(ColorKeys.Unknown);

        // White is the word that differs most between the two. If these ever collapse
        // to one string the Dutch window quietly gains English, which is the whole
        // failure this file exists to catch.
        Assert.NotEqual(english, dutch);
        Assert.NotEqual(englishUnknown, dutchUnknown);
        Assert.Equal(english, "White");
        Assert.Equal(dutch, "Wit");
    }

    [AvaloniaFact]
    public void NoColourReportsAsNotSet()
    {
        foreach (string language in new[] { "en", "nl" })
        {
            LocalizationManager.SetLanguage(language);
            string notSet = LocalizationManager.GetString("Value_NotSet");

            Assert.Equal(notSet, ColorWording.Word(null));
            Assert.Equal(notSet, ColorWording.Word(""));
            Assert.Equal(notSet, ColorWording.Word(DevicePlaceholders.Color));
        }
    }

    [AvaloniaFact]
    public void TheLabelKeepsThePlaceholderSoTheFileStillSaysNothingWasReported()
    {
        LocalizationManager.SetLanguage("en");

        // The screen says "Not set"; the paper says NOCOLOR. A label is read at a
        // counter and a placeholder is the honest marker there, which is the same
        // rule the other label fields already follow.
        Assert.Equal(DevicePlaceholders.Color, ColorWording.OnLabel(DevicePlaceholders.Color));
        Assert.Equal("Not set", ColorWording.Word(DevicePlaceholders.Color));
        Assert.Equal("White", ColorWording.OnLabel(ColorKeys.White));
    }

    [AvaloniaFact]
    public void AColourWithNoWordReadsAsItselfRatherThanAsAVariableName()
    {
        LocalizationManager.SetLanguage("en");

        // Nothing guesses a colour. A key that has lost its dictionary entry is
        // spelled out rather than shown as a name, because a reader cannot grade a
        // phone from "LightBlue".
        Assert.Equal("Light Blue", ColorKeys.Word("LightBlue", _ => null));
        Assert.Equal("Not Akey", ColorKeys.Word("NotAkey", _ => null));
    }

    [AvaloniaFact]
    public void TheReportSpellsTheColourThroughTheWordingItWasGiven()
    {
        LocalizationManager.SetLanguage("en");
        ReportWording english = ReportWordingBuilder.Current();
        Assert.Equal("White", english.Colour(ColorKeys.White));

        LocalizationManager.SetLanguage("nl");
        ReportWording dutch = ReportWordingBuilder.Current();
        Assert.Equal("Wit", dutch.Colour(ColorKeys.White));
    }

    [AvaloniaFact]
    public void AReportWordingWithNoColourStillProducesAReadableColour()
    {
        // A caller that forgets to hand one in must still get a document rather than
        // an exception or a line reading "White".
        ReportWording bare = ReportWording.English;
        Assert.Equal("White", bare.Colour(ColorKeys.White));
        Assert.Equal("Light Blue", bare.Colour(ColorKeys.LightBlue));
    }

    [AvaloniaFact]
    public void TheLabelIsCompleteOnWhatWasReportedRatherThanOnWhatItSays()
    {
        DeviceData phone = new() { Color = ColorKeys.White };
        LabelFields spoken = LabelFields.From(phone, colour: ColorWording.OnLabel);
        Assert.Equal("White", spoken.Color);
        Assert.Equal(ColorKeys.White, spoken.ColorReported);

        DeviceData silent = new() { Color = DevicePlaceholders.Color };
        LabelFields unsaid = LabelFields.From(silent, colour: ColorWording.OnLabel);
        Assert.Equal(DevicePlaceholders.Color, unsaid.Color);
        Assert.Equal(DevicePlaceholders.Color, unsaid.ColorReported);
    }
}
