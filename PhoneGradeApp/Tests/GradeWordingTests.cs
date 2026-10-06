using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Xunit;
using PhoneGrade.Tests;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the wording of the grade and the invoice method, in both languages.
///
/// These two strings used to be built on the model, where no dictionary is
/// reachable, so they came out Dutch whatever the window was set to. An operator
/// running the app in English saw "KLASSE A" and "Niet opgegeven" beside rows
/// that were in English, which reads as a broken build rather than as a
/// translation gap. Nothing caught it, because the string was correct for the
/// language the app happened to default to.
///
/// The grade is asserted as the bare letter on purpose. The label, the report,
/// the CSV and the JSON all carry A, B or C, so the window agreeing with them is
/// the point; a second spelling on screen is a third thing to keep in step.
/// </summary>
[Collection(LanguageCollection.Name)]
public class GradeWordingTests
{
    [AvaloniaFact]
    public void ThePlaceholderGradeReadsAsNotGradedInEitherLanguage()
    {
        foreach (string language in new[] { "en", "nl" })
        {
            LocalizationManager.SetLanguage(language);
            string notGraded = LocalizationManager.GetString("Value_NotGraded");

            Assert.Equal(notGraded, GradeWording.Grade(DevicePlaceholders.Grade));
            Assert.Equal(notGraded, GradeWording.Grade(""));
            Assert.Equal(notGraded, GradeWording.Grade(null));
        }
    }

    [AvaloniaFact]
    public void ARealGradeIsTheLetterAndIsNotWovenIntoASentence()
    {
        LocalizationManager.SetLanguage("en");
        Assert.Equal("A", GradeWording.Grade("A"));

        // The word in front of the letter used to be Dutch. If a sentence ever comes
        // back it has to come from a dictionary, so these three stay letters.
        LocalizationManager.SetLanguage("nl");
        Assert.Equal("A", GradeWording.Grade("A"));
        Assert.Equal("B", GradeWording.Grade("B"));
        Assert.Equal("C", GradeWording.Grade("C"));
    }

    [AvaloniaFact]
    public void AnUnrecognisedValueIsPassedThroughSoItStaysVisible()
    {
        LocalizationManager.SetLanguage("en");

        // Dressing an unknown value up as a grade would hide the very thing worth
        // seeing, so neither field invents wording for one.
        Assert.Equal("BB", GradeWording.Grade("BB"));
        Assert.Equal("Rente", GradeWording.InvoiceMethod("Rente"));
    }

    [AvaloniaFact]
    public void TheInvoiceMethodIsSpelledInTheLanguageOfTheWindow()
    {
        LocalizationManager.SetLanguage("en");
        Assert.Equal(LocalizationManager.GetString("Payment_Marge"), GradeWording.InvoiceMethod("Marge"));
        Assert.Equal(LocalizationManager.GetString("Payment_BTW"), GradeWording.InvoiceMethod("BTW"));
        Assert.Equal(LocalizationManager.GetString("Value_NotSet"), GradeWording.InvoiceMethod(DevicePlaceholders.PayMethod));
        Assert.Equal(LocalizationManager.GetString("Value_NotSet"), GradeWording.InvoiceMethod(""));
        Assert.Equal(LocalizationManager.GetString("Value_NotSet"), GradeWording.InvoiceMethod(null));

        LocalizationManager.SetLanguage("nl");
        Assert.Equal(LocalizationManager.GetString("Payment_Marge"), GradeWording.InvoiceMethod("Marge"));
        Assert.Equal(LocalizationManager.GetString("Payment_BTW"), GradeWording.InvoiceMethod("BTW"));
    }

    [AvaloniaFact]
    public void TheTwoInvoiceMethodsAreNotTheSameSentenceInBothLanguages()
    {
        LocalizationManager.SetLanguage("en");
        string englishMargin = LocalizationManager.GetString("Payment_Marge");
        string englishVat = LocalizationManager.GetString("Payment_BTW");

        LocalizationManager.SetLanguage("nl");
        string dutchMargin = LocalizationManager.GetString("Payment_Marge");
        string dutchVat = LocalizationManager.GetString("Payment_BTW");

        // VAT is the word that differs most between the two. If this ever collapses to
        // one shared string the Dutch window quietly gains English, which is the
        // failure this whole file exists to catch.
        Assert.NotEqual(englishMargin, dutchMargin);
        Assert.NotEqual(englishVat, dutchVat);
    }

    [AvaloniaFact]
    public void TheSummaryRowsFollowTheWindowRatherThanTheFirstLanguageLoaded()
    {
        using var vm = new MainWindowViewModel();
        vm.SelectedDevice = new System.Collections.Generic.KeyValuePair<string, string>("MOCK_UDID", "iPhone 13");
        vm.SetQualityCommand.Execute("B").Subscribe();
        vm.SetPaymentMethodCommand.Execute("Marge").Subscribe();

        LocalizationManager.SetLanguage("en");
        Assert.Equal("B", vm.SelectedGradeDisplay);
        Assert.Equal(LocalizationManager.GetString("Payment_Marge"), vm.SelectedInvoiceMethodDisplay);

        LocalizationManager.SetLanguage("nl");
        Assert.Equal("B", vm.SelectedGradeDisplay);
        Assert.Equal(LocalizationManager.GetString("Payment_Marge"), vm.SelectedInvoiceMethodDisplay);
    }
}
