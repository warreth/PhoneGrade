using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.Tests;
using Xunit;
using static Tests.LicensingTestContext;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the free-seat count on the licensing panel.
///
/// The panel already reported how many machines hold a seat. This is the number that
/// answers the question an operator asks after buying a second bench, which is whether
/// there is room for it, and the two are the same pair of numbers with a subtraction
/// between them. So these are mostly about the edges of that subtraction: the vendor
/// reporting no limit, and the vendor reporting more usage than the limit allows.
///
/// The values come from the vendor's response and not from a tier table, which is
/// deliberate. A key bought as Starter allows two machines and one bought as Pro allows
/// five, and the app is not told which is which except through the limit the vendor
/// sends back. Hardcoding two and five here would put pricing in the binary and it would
/// be wrong the first time a price or a tier changed.
/// </summary>
[Collection(LanguageCollection.Name)]
public class FreeSeatCounterTests : IDisposable
{
    private readonly string _settingsDir =
        Path.Combine(Path.GetTempPath(), $"free-seats-{Guid.NewGuid():N}");
    private readonly string? _original = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public FreeSeatCounterTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _original);
        try { Directory.Delete(_settingsDir, recursive: true); }
        catch { /* a scratch directory that will not go is not a test failure */ }
    }

    [AvaloniaFact]
    public async Task ThePanelSaysHowManySeatsAreFree()
    {
        LocalizationManager.SetLanguage("en");
        LicensingViewModel vm = await Panel(used: 2, limit: 5);

        Assert.Equal(3, vm.SeatsFree);
        Assert.True(vm.HasSeatFree);
        Assert.Equal("3 seats left on this key", vm.FreeSeatsText);
    }

    [AvaloniaFact]
    public async Task OneSeatLeftIsNotWrittenAsSeats()
    {
        LocalizationManager.SetLanguage("en");
        LicensingViewModel vm = await Panel(used: 1, limit: 2);

        // The Starter plan is exactly this case, so it is the one a customer meets first
        // and the one where the plural is visibly wrong.
        Assert.Equal(1, vm.SeatsFree);
        Assert.Equal("1 seat left on this key", vm.FreeSeatsText);
    }

    [AvaloniaFact]
    public async Task AFullKeySaysNothingAboutSeatsBeingFree()
    {
        LocalizationManager.SetLanguage("en");
        LicensingViewModel vm = await Panel(used: 5, limit: 5);

        Assert.Equal(0, vm.SeatsFree);
        Assert.False(vm.HasSeatFree);

        // "0 seats left" under "5 of 5 in use" says it twice, and the second one is the
        // less useful of the two.
        Assert.Equal("", vm.FreeSeatsText);
    }

    [AvaloniaFact]
    public async Task AKeyWithNoReportedLimitSaysNothingRatherThanCountingFromZero()
    {
        LocalizationManager.SetLanguage("en");
        LicensingViewModel vm = await Panel(used: 0, limit: 0);

        Assert.False(vm.HasSeatNumbers);
        Assert.Equal(0, vm.SeatsFree);
        Assert.Equal("", vm.FreeSeatsText);
    }

    [AvaloniaFact]
    public async Task ADowngradedPlanNeverClaimsANegativeNumberOfFreeSeats()
    {
        LocalizationManager.SetLanguage("en");

        // A plan reduced from five places to two while three seats are still live. The
        // subtraction is minus one, and a panel that printed that would read as a fault
        // in the application rather than as a plan with fewer places than machines.
        LicensingViewModel vm = await Panel(used: 3, limit: 2);

        Assert.Equal(0, vm.SeatsFree);
        Assert.Equal("", vm.FreeSeatsText);
    }

    [AvaloniaFact]
    public async Task TheCountFollowsTheLanguage()
    {
        LicensingViewModel vm = await Panel(used: 2, limit: 5);

        foreach (string language in SupportedLanguages.Codes)
        {
            LocalizationManager.SetLanguage(language);
            vm.UpdateStatusText();

            string text = vm.FreeSeatsText;
            Assert.False(string.IsNullOrWhiteSpace(text), $"{language} shows nothing about the free seats");
            Assert.False(text.StartsWith("Licensing_"), $"{language} fell back to the key");
            Assert.Contains("3", text);
        }
    }

    [AvaloniaFact]
    public void EveryLanguageCarriesBothFormsOfTheSentence()
    {
        // One seat and several are separate strings, so a language that has only the
        // plural shows "1 seats" on a Starter plan.
        foreach (string language in SupportedLanguages.Codes)
        {
            string source = File.ReadAllText(Dictionary(language));

            foreach (string key in new[] { "Licensing_PanelSeatsFree", "Licensing_PanelSeatFreeOne" })
            {
                Assert.True(
                    source.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal),
                    $"{language} is missing {key}");
            }
        }
    }

    private static string Dictionary(string language) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "PhoneGradeApp", "PhoneGrade.UI", "Resources", $"Strings.{language}.axaml"));

    /// <summary>
    /// A view model whose gate has been told a seat count by the vendor.
    ///
    /// Built through the real validate path rather than by assigning the numbers, because
    /// the point is that the vendor's limit and usage reach the panel unchanged. The fake
    /// server answers with the two fields the panel reads.
    /// </summary>
    private static async Task<LicensingViewModel> Panel(int used, int limit)
    {
        using var server = new FakeLicenseServer
        {
            ActivateResponseJson = ActivatedWith(used, limit),
        };
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        vm.Licensing!.LicenseKeyInput = "KEY-SEATS-1";
        await vm.Licensing.ValidateCommand.Execute();

        return vm.Licensing;
    }

    /// <summary>
    /// A validate response shaped like the vendor's, with the two counters set.
    ///
    /// Same shape as the fixture's own recorded activation response, with the two counters
    /// set, because the counters are the only thing that varies between these tests.
    ///
    /// Set on the activate response rather than the validate one. The panel reads the
    /// seat fraction from what activation answered with, so a body on the validate
    /// response leaves the panel drawing the fixture's own numbers and every test here
    /// passes or fails on five and two regardless of what it asked for.
    /// </summary>
    private static string ActivatedWith(int used, int limit) =>
        $"{{\"activated\":true,\"error\":null,\"license_key\":{{\"id\":7,\"status\":\"active\",\"activation_limit\":{limit},\"activation_usage\":{used}}},"
        + "\"instance\":{\"id\":996,\"name\":\"pg-0123456789abcdef0123456789abcdef\",\"created_at\":\"2026-01-02T03:04:05.000000Z\"},"
        + "\"meta\":{\"product_id\":1422604,\"variant_id\":12}}";
}