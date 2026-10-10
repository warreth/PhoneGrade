using System.Reactive;
using DynamicData;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

public partial class MainWindowViewModel
{

    /// <summary>True until the operator dismisses the first-run introduction screen.</summary>
    public bool IsIntroVisible
    {
        get => _isIntroVisible;
        set => this.RaiseAndSetIfChanged(ref _isIntroVisible, value);
    }

    /// <summary>
    /// The pages the introduction screen is made of, in the order they are read.
    ///
    /// It used to be one long card holding the plan, the Pro price, the IMEI key
    /// offer, three buttons and a license box, which put the whole of it on one
    /// screen at a font too small to read comfortably. It is now four pages, and the
    /// language and the look are asked for on the first one rather than left for
    /// the operator to find afterwards.
    /// </summary>
    public IReadOnlyList<string> IntroPages { get; } =
        ["Language", "Plan", "Workflow", "Licence", "Imei"];

    /// <summary>The page of the introduction currently on screen.</summary>
    public string IntroPage
    {
        get => _introPage;
        set
        {
            this.RaiseAndSetIfChanged(ref _introPage, value);
            this.RaisePropertyChanged(nameof(IsFirstIntroPage));
            this.RaisePropertyChanged(nameof(IsLastIntroPage));
            this.RaisePropertyChanged(nameof(IsOnLicencePage));
            this.RaisePropertyChanged(nameof(ShowIntroNext));
            this.RaisePropertyChanged(nameof(IntroStepNumber));
            this.RaisePropertyChanged(nameof(IntroStepDisplay));
        }
    }

    /// <summary>How many pages the introduction has, for the progress line.</summary>
    public int IntroPageCount => IntroPages.Count;

    /// <summary>Which page is on screen, counting from one.</summary>
    public int IntroStepNumber => IntroPages.IndexOf(IntroPage) + 1;

    /// <summary>
    /// The step counter beside the progress bar. Numbers only, so it needs no
    /// translation and cannot disagree with a language switch halfway through.
    /// </summary>
    public string IntroStepDisplay => $"{IntroStepNumber} / {IntroPageCount}";

    /// <summary>True on the first page, where the back button has nowhere to go.</summary>
    public bool IsFirstIntroPage => IntroPage == IntroPages[0];

    /// <summary>True on the last page, where the button finishes instead of advancing.</summary>
    public bool IsLastIntroPage => IntroPage == IntroPages[^1];

    /// <summary>The page after this one, wrapping to the first on the last.</summary>
    public string NextIntroPage =>
        IsLastIntroPage ? IntroPages[0] : IntroPages[IntroPages.IndexOf(IntroPage) + 1];

    /// <summary>The page before this one, stopping at the first.</summary>
    public string PreviousIntroPage =>
        IsFirstIntroPage ? IntroPages[0] : IntroPages[IntroPages.IndexOf(IntroPage) - 1];

    /// <summary>Goes to the next page, or closes the screen when it is the last one.</summary>
    public ReactiveCommand<Unit, Unit> IntroNextCommand { get; }

    /// <summary>Goes back a page. Does nothing on the first one.</summary>
    public ReactiveCommand<Unit, Unit> IntroBackCommand { get; }

    /// <summary>Jumps straight to a named page.</summary>
    public ReactiveCommand<string, Unit> IntroGoToPageCommand { get; }

    /// <summary>True once "I already have a license" was pressed on the introduction screen.</summary>
    public bool IsIntroActivationVisible
    {
        get => _isIntroActivationVisible;
        set => this.RaiseAndSetIfChanged(ref _isIntroActivationVisible, value);
    }

    /// <summary>Closes the introduction screen for good.</summary>
    public ReactiveCommand<Unit, Unit> DismissIntroCommand { get; }

    /// <summary>Reveals the license key box on the introduction screen.</summary>
    public ReactiveCommand<Unit, Unit> ShowIntroActivationCommand { get; }

    /// <summary>
    /// Accepts the terms and continues to the last page. Not the way out: the
    /// introduction ends on the last page, where Finish stands.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AcceptIntroTermsCommand { get; }

    /// <summary>Opens the introduction again from the settings page.</summary>
    public ReactiveCommand<Unit, Unit> ReplayIntroCommand { get; }

    /// <summary>True while the license panel under the title bar status pill is open.</summary>
    public bool IsLicensePanelOpen
    {
        get => _isLicensePanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isLicensePanelOpen, value);
    }

    /// <summary>
    /// Ends the introduction, and refuses to record it as seen until the terms have
    /// been accepted.
    ///
    /// Every route out of the introduction arrives here, so this is the one place the
    /// check has to be. Without it the last page's Finish would be a way around the
    /// agreement, and a gate that can be walked past is worse than no gate because it
    /// reads as one.
    /// </summary>
    private void DismissIntro()
    {
        if (!CanFinishIntro) return;

        _settings.IntroSeen = true;
        _settings.Save();
        IsIntroVisible = false;
        IsIntroActivationVisible = false;
    }

    /// <summary>The page carrying the agreement, named so the markup cannot drift.</summary>
    public const string LicencePage = "Licence";

    /// <summary>
    /// Whether the operator has accepted the terms that apply to them.
    ///
    /// Deliberately not written to the settings file. An operator who ticked this on
    /// one machine has not agreed on another, and a gate that remembered across
    /// machines could be walked past with a single tick on the first one. It is asked
    /// once per machine, which is also the only point at which the answer is worth
    /// anything.
    /// </summary>
    public bool IsLicenceAccepted
    {
        get => _isLicenceAccepted;
        set
        {
            // The field is asked, not the return value: ReactiveUI 20 returns the
            // new value from RaiseAndSetIfChanged, so `if (RaiseAndSetIfChanged(...))`
            // ran the dependent raise only when the box was ticked and skipped it
            // when it was cleared.
            if (_isLicenceAccepted == value) return;
            this.RaiseAndSetIfChanged(ref _isLicenceAccepted, value);
            this.RaisePropertyChanged(nameof(CanFinishIntro));
        }
    }

    /// <summary>
    /// Whether the introduction may be closed.
    ///
    /// Checked in <see cref="DismissIntro"/> rather than only on the buttons,
    /// because finishing and the terms page's continue are both ways out and
    /// neither may skip the one page that is not optional.
    /// </summary>
    public bool CanFinishIntro => IsLicenceAccepted;

    public bool IsOnLicencePage => IntroPage == LicencePage;

    /// <summary>
    /// Next is offered everywhere except the agreement page and the last page, where
    /// the button says Finish instead.
    /// </summary>
    public bool ShowIntroNext => !IsOnLicencePage && !IsLastIntroPage;

    /// <summary>
    /// Which document applies. The free plan is covered by the licence the source is
    /// published under. A paying customer is not, which is why the commercial end user
    /// licence agreement is the document that has to be named to them.
    /// </summary>
    public string LicenceDocumentUrl =>
        Services.LicenceTerms.DocumentFor(Licensing.IsPro);

    /// <summary>
    /// What the checkbox says. It names the document rather than only saying agree,
    /// so an operator who did not read it can still say afterwards which document it
    /// was.
    /// </summary>
    public string LicenceAgreementLabel => LocalizationManager.GetString(
        Licensing.IsPro
            ? "Intro_LicenceAcceptCommercial"
            : "Intro_LicenceAcceptFree");

    public ReactiveCommand<Unit, Unit> OpenLicenceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTrademarkCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToImeiSettingsCommand { get; }

}
