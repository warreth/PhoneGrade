using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.Tests;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the licence agreement on the introduction screen.
///
/// The point of these is not that the screen says the right words. It is that the
/// agreement cannot be skipped.
///
/// The introduction has three ways out and all three have to be closed, or the page is
/// decoration. The Finish on the last page, the Skip button, and the command that
/// jumps straight to a page. A check on one button leaves the other two open, and a
/// gate that can be walked past is worse than no gate because it reads as one. So the
/// check lives in the single method every route ends up calling, and these tests go
/// after that method rather than after the markup.
///
/// Settings are pointed at a scratch directory for every test. Without that the view
/// model writes IntroSeen into the settings file of whoever is running the suite, which
/// is both a side effect on a real installation and the reason the first attempt at
/// these tests could not be made to pass: the file already said true.
/// </summary>
[Collection(LanguageCollection.Name)]
public sealed class LicenceAgreementTests : IDisposable
{
    private readonly string _settingsDir =
        Path.Combine(Path.GetTempPath(), $"licence-agreement-{Guid.NewGuid():N}");
    private readonly string? _original = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    /// <summary>IntroSeen false, so the introduction is up, which is the state being tested.</summary>
    public LicenceAgreementTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":false}""");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _original);
        try { Directory.Delete(_settingsDir, recursive: true); }
        catch { /* a scratch directory that will not go is not a test failure */ }
    }

    private static MainWindowViewModel Fresh() => new();

    /// <summary>Puts the view model on the agreement page, which is the state under test.</summary>
    private static MainWindowViewModel OnLicencePage()
    {
        MainWindowViewModel vm = Fresh();
        vm.IsIntroVisible = true;
        vm.IntroPage = MainWindowViewModel.LicencePage;
        return vm;
    }

    [AvaloniaFact]
    public void TheIntroductionOffersTheTermsAsAPageOfItsOwn()
    {
        using MainWindowViewModel vm = Fresh();

        // A page in its own right rather than a box on the plan page, because the plan
        // page is one an operator is allowed to decline and this one is not.
        Assert.Contains(MainWindowViewModel.LicencePage, vm.IntroPages);

        // Exactly one, since a second would put two agreements in the flow.
        Assert.Equal(1, vm.IntroPages.Count(page => page == MainWindowViewModel.LicencePage));

        // And it is not the first or the last. First would mean an operator meets the
        // terms before anything else and has no idea what they are agreeing to use.
        Assert.NotEqual(vm.IntroPages[0], MainWindowViewModel.LicencePage);
        Assert.NotEqual(vm.IntroPages[^1], MainWindowViewModel.LicencePage);
    }

    [AvaloniaFact]
    public void TheAgreementCannotBeSkipped()
    {
        using MainWindowViewModel vm = OnLicencePage();

        Assert.False(vm.IsLicenceAccepted);

        // The Skip route, on the page where it is not offered.
        vm.DismissIntroCommand.Execute().Subscribe();
        Assert.True(vm.IsIntroVisible, "Skip closed the introduction without the terms being accepted");

        // And it is not offered either, so the operator is not pressing something that
        // does nothing and concluding the application is broken.
        Assert.False(vm.CanSkipIntro);
        Assert.False(vm.ShowIntroNext);
    }

    [AvaloniaFact]
    public void JumpingStraightToTheLastPageIsNotAWayRoundIt()
    {
        // The case a check on the agreement page's own button would miss. A jump
        // straight to the last page, which the settings replay and the tests can
        // still do, has to hit the same gate: its Finish is refused as well.
        using MainWindowViewModel vm = Fresh();
        vm.IsIntroVisible = true;

        vm.IntroGoToPageCommand.Execute("Imei").Subscribe();
        Assert.Equal("Imei", vm.IntroPage);

        vm.IntroNextCommand.Execute().Subscribe();

        Assert.True(vm.IsIntroVisible, "jumping to the last page skipped the agreement");
    }

    [AvaloniaFact]
    public void TickingTheBoxIsWhatOpensTheGate()
    {
        using MainWindowViewModel vm = OnLicencePage();

        vm.IsLicenceAccepted = true;

        Assert.True(vm.CanFinishIntro);
        Assert.True(vm.CanSkipIntro);

        vm.DismissIntroCommand.Execute().Subscribe();

        Assert.False(vm.IsIntroVisible);
    }

    [AvaloniaFact]
    public void AcceptingTheTermsContinuesToTheLastPage()
    {
        // The foot's button says "Accept and continue", so it continues. It used
        // to close the whole screen, which left the optional-checks page with no
        // door to it once the row of topic tabs was gone.
        using MainWindowViewModel vm = OnLicencePage();
        vm.IsLicenceAccepted = true;

        vm.AcceptIntroTermsCommand.Execute().Subscribe();

        Assert.True(vm.IsIntroVisible, "accepting the terms closed the introduction");
        Assert.True(vm.IsLastIntroPage);
        Assert.Equal("Imei", vm.IntroPage);

        // And the last page's Finish is the way out.
        vm.IntroNextCommand.Execute().Subscribe();
        Assert.False(vm.IsIntroVisible);
    }

    [AvaloniaFact]
    public void TheAgreementSurvivesBeingAcceptedOnlyOncePerMachine()
    {
        // The answer is deliberately not written to the settings file. A view model
        // rebuilt on the same machine starts from not agreed, and the test after this
        // one on the same scratch directory has to reach the same conclusion.
        using MainWindowViewModel first = OnLicencePage();
        first.IsLicenceAccepted = true;
        first.DismissIntroCommand.Execute();

        using MainWindowViewModel second = Fresh();
        second.IsIntroVisible = true;
        second.IntroPage = MainWindowViewModel.LicencePage;

        Assert.False(second.IsLicenceAccepted, "agreement was remembered across view models");
        Assert.False(second.CanFinishIntro);
    }

    [AvaloniaFact]
    public void AnOperatorWhoCameBackIsNotBlocked()
    {
        // Backwards has to keep working. An operator who ticked the box, went on to the
        // IMEI question, changed their mind about an earlier page and went back must not
        // be trapped by a gate that only remembers whether they ever agreed.
        using MainWindowViewModel vm = Fresh();
        vm.IsIntroVisible = true;
        vm.IsLicenceAccepted = true;

        vm.IntroPage = "Workflow";

        Assert.True(vm.CanSkipIntro);
        Assert.True(vm.ShowIntroNext, "the pages before the agreement lost their Next button");
    }

    [AvaloniaFact]
    public void TheOrdinaryPagesAreStillSkippable()
    {
        using MainWindowViewModel vm = Fresh();

        // Everything but the agreement page is optional, because those pages are
        // offering things an operator is allowed to decline. A Skip that vanished
        // everywhere would be a different bug: a first run an operator cannot leave.
        foreach (string page in vm.IntroPages.Where(page => page != MainWindowViewModel.LicencePage))
        {
            vm.IntroPage = page;
            Assert.True(vm.CanSkipIntro, $"Skip was removed from {page}");
        }
    }

    [AvaloniaFact]
    public void TheNextButtonTurnsIntoFinishOnTheLastPage()
    {
        using MainWindowViewModel vm = Fresh();

        vm.IntroPage = "Workflow";
        Assert.True(vm.ShowIntroNext);

        vm.IntroPage = "Imei";
        Assert.False(vm.ShowIntroNext);
        Assert.True(vm.IsLastIntroPage);
    }

    [AvaloniaFact]
    public void AFreeOperatorIsSentToTheLicenceTheCodeIsPublishedUnder()
    {
        // PolyForm Noncommercial, which is what they downloaded and what the
        // attribution requirement hangs off.
        Assert.Equal(LicenceTerms.Repository, LicenceTerms.DocumentFor(isPro: false));
        Assert.DoesNotContain("COMMERCIAL", LicenceTerms.DocumentFor(isPro: false));
    }

    [AvaloniaFact]
    public void AFreeOperatorIsNamedTheRightDocument()
    {
        // Not just the URL. A box that says only "I agree" leaves someone who did not
        // read it unable to say afterwards what they agreed to.
        using MainWindowViewModel vm = Fresh();

        LocalizationManager.SetLanguage("en");

        Assert.Contains("licence terms", vm.LicenceAgreementLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commercial", vm.LicenceAgreementLabel, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void EveryDocumentCarriesTheAuthorAndAnAddress()
    {
        // The attribution requirement. A link to the terms is worth nothing if the terms
        // do not say who wrote them, and the notice line in the licence file is what
        // makes that binding for anyone running the code without paying.
        Assert.Equal("WarreTh", LicenceTerms.Author);
        Assert.Contains("@", LicenceTerms.AuthorEmail);

        foreach (string url in new[] { LicenceTerms.Repository, LicenceTerms.Commercial, LicenceTerms.Trademark })
        {
            Assert.StartsWith("https://", url);
            Assert.Contains("warreth/PhoneGrade", url);
        }
    }

    [AvaloniaFact]
    public void TheRequiredNoticeSurvivesInTheLicenceFile()
    {
        // PolyForm's Notices clause requires the plain text notice line to travel with
        // every copy. Its absence is a licence breach, so this is the line that makes
        // the attribution requirement enforceable for a free operator who never sees a
        // contract.
        string licence = File.ReadAllText(RepositoryFile("LICENSE"));

        Assert.Contains("Required Notice:", licence);
        Assert.Contains(LicenceTerms.Author, licence);
        Assert.Contains("PolyForm Noncommercial License 1.0.0", licence);

        // And the attribution rule is written down where somebody looking for it will
        // find it, rather than only in the licence file.
        string notice = File.ReadAllText(RepositoryFile("NOTICE.md"));
        Assert.Contains(LicenceTerms.Author, notice);
        Assert.Contains(LicenceTerms.AuthorEmail, notice);
    }

    [AvaloniaFact]
    public void TheTrademarkPolicyIsPresentAndSaysTheAuthorKeepsTheMarks()
    {
        // The document the commercial terms point at as well, so all four are read from
        // disk rather than trusted to exist because the link above them resolves.
        string policy = File.ReadAllText(RepositoryFile("TRADEMARK_POLICY.md"));

        Assert.Contains(LicenceTerms.Author, policy);
        Assert.Contains("PhoneGrade", policy);

        // The two prohibitions that matter, named by the headings the document actually
        // uses rather than by wording taken from another one. Reselling is the business
        // terms' business; what a trademark policy forbids is using the marks on
        // something you distribute, and stripping the credit.
        foreach (string heading in new[]
        {
            "What you may not do without written permission",
            "Attribution requirements",
        })
        {
            Assert.True(
                policy.Contains(heading, StringComparison.OrdinalIgnoreCase),
                $"the trademark policy is missing the section: {heading}");
        }
    }

    [AvaloniaFact]
    public void TheCommercialTermsArePresentAndCarryALiabilityCap()
    {
        // The document a paying customer is sent to. If the cap is missing from the
        // shipped file the whole liability position rests on nothing.
        string eula = File.ReadAllText(RepositoryFile("COMMERCIAL_EULA.md"));

        Assert.Contains(LicenceTerms.Author, eula);
        Assert.Contains("subscription", eula, StringComparison.OrdinalIgnoreCase);

        // The cap, in the words that make it one: the amount paid for the year the
        // claim arose in.
        Assert.Contains("twelve months", eula, StringComparison.OrdinalIgnoreCase);

        // And the carve-outs that keep it honest. A cap with no carve-out for fraud is
        // not stronger, it is just unenforceable.
        Assert.Contains("fraud", eula, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheLiabilityIsAlsoWrittenOutOnTheScreen()
    {
        // The sentence that matters has to be readable where the operator is standing,
        // not only in a document behind a link on a first run.
        foreach (string language in new[] { "en", "nl" })
        {
            LocalizationManager.SetLanguage(language);

            foreach (string key in new[]
            {
                "Intro_LiabilityNoWarranty", "Intro_LiabilityCap", "Intro_LiabilityGrade",
            })
            {
                string sentence = LocalizationManager.GetString(key);

                Assert.False(string.IsNullOrWhiteSpace(sentence), $"{language} has no text for {key}");
                Assert.False(sentence.StartsWith(key), $"{language} fell back to the key for {key}");
                Assert.Equal(sentence.Trim(), sentence);
            }
        }
    }

    [AvaloniaFact]
    public void EveryShippedLanguageHasTheWholeAgreement()
    {
        // A key that exists only in English leaves an operator reading the fallback in
        // a language they did not choose, on the one page that decides what they are
        // agreeing to.
        string[] keys =
        {
            "Intro_PageLicence", "Intro_LicenceLead", "Intro_LicenceWhat",
            "Intro_LicenceRead", "Intro_LicenceTrademark", "Intro_LicenceAcknowledge",
            "Intro_LicenceAcceptFree", "Intro_LicenceAcceptCommercial",
            "Intro_LicenceRequired", "Intro_LicenceFinish",
            "Intro_LiabilityTitle", "Intro_LiabilityNoWarranty",
            "Intro_LiabilityCap", "Intro_LiabilityGrade", "Intro_Finish",
        };

        foreach (string language in SupportedLanguages.Codes)
        {
            string source = File.ReadAllText(RepositoryFile($"PhoneGradeApp/PhoneGrade.UI/Resources/Strings.{language}.axaml"));

            foreach (string key in keys)
            {
                Assert.True(
                    source.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal),
                    $"{language} is missing {key}");
            }
        }
    }

    /// <summary>
    /// A path back to the repository root from the test assembly.
    ///
    /// Counted rather than guessed: the output sits at bin/Release/net8.0, so it is
    /// four directories up to the solution folder and one more to the root. Getting this
    /// wrong reads the wrong file and reports a licence that is missing when it is not.
    /// </summary>
    private static string RepositoryFile(string relative)
    {
        string root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        Assert.True(File.Exists(Path.Combine(root, "LICENSE")), $"repository root not found at {root}");

        return Path.Combine(root, relative);
    }
}