using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The USB debugging overlay ============
//
// This overlay appears only when an Android phone is on the cable and adb does
// not trust this computer. Nothing is wrong with the phone; what is missing is a
// tap on the phone, and that tap has to happen in the right order or nothing
// changes.
//
// So it is tested against the rendered overlay rather than against the source
// file. A source grep cannot tell whether the overlay is reachable, whether the
// binding fires, or whether a style selector ever matched anything, and those
// are exactly the ways this overlay has been useless: it used to be an inline
// card that overflowed the idle screen, it used to be open for an iPad, and it
// used to greet an operator with "Voor uw -toestel:" and no steps at all.

public class UsbDebuggingGuideTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"usb-guide-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public UsbDebuggingGuideTests()
    {
        // The overlay uses DynamicResource colours, so the theme has to be loaded
        // from a known state rather than left over from whichever test ran before.
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    // ---- when it is on screen at all ----

    [AvaloniaFact]
    public void TheOverlay_IsVisible_WhenThePhoneDoesNotTrustThisComputer()
    {
        var window = ShowOverlay();
        var overlay = FindOverlay(window);

        Assert.NotNull(overlay);
        Assert.True(overlay!.IsEffectivelyVisible,
            "the overlay is bound to ShowAdbWarning, so a false here means the operator " +
            "is never shown the steps that would fix it");
        Assert.NotNull(FindGuideCard(window));
    }

    [AvaloniaFact]
    public void TheOverlay_IsHidden_WhenThePhoneIsTrusted()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.ShowAdbWarning = false;

        window.Show();
        window.Width = 900;
        window.Height = 1000;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var overlay = FindOverlay(window);
        Assert.NotNull(overlay);
        Assert.False(overlay!.IsVisible,
            "a phone that is already trusted has nothing to be told");
    }

    [AvaloniaFact]
    public void TheOverlay_SitsBelowTheSettingsPage_WhenSettingsIsOpen()
    {
        // An operator who opens Connection settings to fix something should not
        // have the answer covered up while they read the settings.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 1000;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var overlay = FindOverlay(window);
        var settings = Descendants(window).OfType<Panel>()
            .FirstOrDefault(p => p.Name == "SettingsPanel");

        Assert.NotNull(overlay);
        Assert.NotNull(settings);
        Assert.True(overlay!.ZIndex < settings!.ZIndex,
            $"the how-to is at ZIndex {overlay.ZIndex} and settings at {settings.ZIndex}");
    }

    // ---- what it says ----

    [AvaloniaFact]
    public void TheOverlay_SaysThePhoneItselfIsFine()
    {
        // It reads as a defect report otherwise, and the operator goes off to
        // swap the cable instead of tapping the phone.
        Assert.Contains("telefoon zelf is prima", GuideText());
    }

    [AvaloniaFact]
    public void TheOverlay_IsInDutch_LikeTheRestOfTheApp()
    {
        // The overlay was the only English string left in an otherwise Dutch
        // window, which reads on screen as a half-finished product.
        var text = GuideText();
        foreach (var english in new[] { "Android device connected", "Retry ADB Detection", "USB Debugging is turned ON" })
        {
            Assert.DoesNotContain(english, text);
        }

        Assert.Contains("Opnieuw zoeken", text);
        Assert.Contains("Sluiten", text);
    }

    [AvaloniaFact]
    public void TheOverlay_DoesNotAskForMtpMode()
    {
        // Measured on the real Pixel: with USB debugging on, adb connects whatever
        // the USB mode is set to. Switching to File Transfer changes nothing about
        // the trust state and costs the operator a trip through the notification
        // shade, so the old instruction was pure cost.
        Assert.DoesNotContain("MTP", GuideText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bestandsoverdracht", GuideText(), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheOverlay_NamesTheScreenLock_WhichIsWhyNoPromptAppears()
    {
        // The most common reason for "nothing happens": the prompt is on a
        // screen nobody is looking at, because the phone went to sleep in the
        // seconds between the cable going in and the keypress.
        Assert.Contains("Ontgrendel het scherm", GuideText());
    }

    [AvaloniaFact]
    public void TheOverlay_TellsTheOperatorToTickAlwaysAllow()
    {
        // Without this tick the phone asks again on every new cable and the pc
        // lands back on unauthorized every time, which is the same overlay over
        // and over with no way out. It is the one line that decides whether it sticks.
        Assert.Contains("Altijd toestaan vanaf deze computer", GuideText());
    }

    [AvaloniaFact]
    public void TheOverlay_CoversTheCaseWhereNoPromptAppearsAtAll()
    {
        // If developer options was never switched on, or the prompt scrolled past,
        // there is nothing to tap. The path to the setting is the fallback that
        // makes the overlay work for a phone nobody has set up before.
        var text = GuideText();
        Assert.Contains("Build-nummer", text);
        Assert.Contains("zeven keer", text);
        Assert.Contains("USB-foutopsporing", text);
    }

    // ---- what it names the phone ----

    [AvaloniaFact]
    public void TheOverlay_NamesThePhone_BeforeItStartsInstructing()
    {
        // An operator holding three phones wants to be told which one is on the
        // cable, not left guessing from a generic "Android device". The model is
        // read off the native port, so it is there to show and it is shown first.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.AdbTutorialViewModel.SetDevice("Honor", "HONOR 600 Lite");
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var text = GuideText(window);
        var name = text.IndexOf("HONOR 600 Lite", StringComparison.Ordinal);
        Assert.True(name >= 0, $"the model never reached the screen:\n{text}");
        Assert.True(name < text.IndexOf("Ontgrendel het scherm", StringComparison.Ordinal),
            "the phone is named after the instructions rather than before them");
        Assert.Contains("Voor uw HONOR 600 Lite:", text);
    }

    [AvaloniaFact]
    public void TheOverlay_FallsBackToTheBrand_WhenTheModelIsUnknown()
    {
        // Windows often has no better answer than the hardware. "Voor uw -toestel"
        // was what came out then, which tells an operator nothing about which of
        // the phones on the bench this is.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.AdbTutorialViewModel.SetDevice("Samsung", "");
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var text = GuideText(window);
        Assert.Contains("Voor uw Samsung-toestel:", text);
        Assert.DoesNotContain("Voor uw -", text);
        Assert.DoesNotContain("Voor uw :", text);

        // And the steps that came with the brand are Samsung's, not the generic
        // pair: the extra hop through Software information is the part a generic
        // instruction would have made the operator hunt for.
        Assert.Contains("Software-informatie", text);
    }

    [AvaloniaFact]
    public void TheOverlay_FallsBackToAndroid_WhenNothingWasReported()
    {
        // The heading is built from the model, so an empty model has to have
        // something to put in it. It used to come out as "Voor uw -toestel:",
        // which tells an operator nothing at all.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.AdbTutorialViewModel.SetDevice("", "");
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var text = GuideText(window);
        Assert.Contains("Voor uw Android-toestel:", text);
        Assert.DoesNotContain("Voor uw -", text);
        Assert.DoesNotContain("Voor uw :", text);

        // And the steps under it are the generic pair, because nothing named a
        // manufacturer to look up.
        Assert.Contains("Build-nummer", text);
        Assert.DoesNotContain("Software-informatie", text);
    }

    [AvaloniaFact]
    public void TheOverlay_GivesGenericSteps_ForABrandNobodyWroteAPathFor()
    {
        // Honor, ZTE, Poco and the rest have no table of their own. The wrong
        // answer is a manufacturer's menu they do not have; the right answer is
        // the stock Android path, which every one of them has.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.AdbTutorialViewModel.SetDevice("Honor", "");
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var text = GuideText(window);
        Assert.Contains("Voor uw Honor-toestel:", text);
        Assert.Contains("Build-nummer", text);
        Assert.Contains("Over de telefoon", text);

        // A generic fallback that named a specific vendor's menu would be worse
        // than no fallback at all.
        Assert.DoesNotContain("Software-informatie", text);
        Assert.DoesNotContain("MIUI-versie", text);
    }

    // ---- how it is laid out ----

    [AvaloniaFact]
    public void TheOverlay_IsNumbered_AndTheBadgesActuallyResolve()
    {
        var window = ShowOverlay();
        var card = FindGuideCard(window);
        Assert.NotNull(card);

        // Four numbered steps, in order. An unordered list of four actions reads as
        // one long instruction, and people skip half of it.
        //
        // The numbers are checked as the badges themselves, not as "1." in the
        // text, because the badge is the part that can silently stop existing: a
        // style selector that matches nothing leaves a plain numbered line that
        // still reads correctly and looks nothing like a guide.
        var badges = Descendants(card).OfType<Border>()
            .Where(b => b.Classes.Contains("stepBadge"))
            .ToList();

        Assert.Equal(new[] { "1", "2", "3", "4" }, badges.Select(BadgeText).ToArray());

        foreach (var badge in badges)
        {
            Assert.Equal(22d, badge.Width);
            Assert.Equal(22d, badge.Height);

            // A square badge is a list marker, not a step. The roundness is what
            // makes it read as a position in a sequence at a glance.
            Assert.Equal(11d, (double)badge.CornerRadius.TopLeft, 3);
            Assert.NotNull(badge.Child);
        }

        // Each badge sits beside a line of its own, so the number is next to the
        // thing it numbers rather than in a column of its own further away.
        foreach (var badge in badges)
        {
            var row = badge.GetVisualParent();
            Assert.NotNull(row);
            var body = Descendants(row).OfType<TextBlock>()
                .FirstOrDefault(t => t.Classes.Contains("stepBody"));
            Assert.NotNull(body);
            Assert.False(string.IsNullOrWhiteSpace(body!.Text), "a numbered step with no text");
        }
    }

    [AvaloniaFact]
    public void TheOverlay_StepsRunDownwards_EachNumberBesideItsOwnLine()
    {
        var window = ShowOverlay();
        var card = FindGuideCard(window);
        Assert.NotNull(card);

        var rows = Descendants(card).OfType<Border>()
            .Where(b => b.Classes.Contains("stepBadge"))
            .Select(badge =>
            {
                var row = badge.GetVisualParent()!;
                var body = Descendants(row).OfType<TextBlock>()
                    .First(t => t.Classes.Contains("stepBody"));
                return (Y: badge.TranslatePoint(new Point(0, 0), card)!.Value.Y,
                        Right: badge.TranslatePoint(new Point(badge.Bounds.Width, 0), card)!.Value.X,
                        BodyX: body.TranslatePoint(new Point(0, 0), card)!.Value.X);
            })
            .ToList();

        // Downwards and not level. Four numbers in a row with the text below them
        // is a legend, not a sequence, and the order is the whole point.
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i].Y > rows[i - 1].Y,
                $"step {i + 1} is not below step {i}: {rows[i].Y} vs {rows[i - 1].Y}");
        }

        // And each number sits to the left of its own line, not in a column far
        // enough away that the pairing has to be guessed.
        foreach (var row in rows)
        {
            Assert.True(row.BodyX >= row.Right,
                $"a step's text starts at {row.BodyX} but its badge ends at {row.Right}");
        }
    }

    [AvaloniaFact]
    public void TheOverlay_HasNoScrollbar_OfItsOwn()
    {
        // The overlay replaced an inline card that pushed the rest of the screen
        // down. Scrolling was offered as the answer, which is not an answer: an
        // operator reading a how-to off a kiosk will not drag a panel to find the
        // last line. It either fits or the layout is wrong.
        var window = ShowOverlay();
        var overlay = FindOverlay(window);
        Assert.NotNull(overlay);

        Assert.Empty(Descendants(overlay).OfType<ScrollViewer>());
    }

    [AvaloniaFact]
    public void TheOverlay_FitsOnAWindowWithoutScrolling()
    {
        // A how-to that runs off the bottom is how-to that gets half read.
        // Two sizes rather than one: the full desktop, and the 850x620 window
        // the kiosk actually opens at, which is where the old inline card
        // overflowed.
        foreach (var (width, height) in new[] { (900, 1400), (850, 620) })
        {
            using var window = new MainWindow();
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.Theme = "Dark";
            vm.AdbTutorialViewModel.SetDevice("Honor", "HONOR 600 Lite");
            vm.ShowAdbWarning = true;

            window.Show();
            window.Width = width;
            window.Height = height;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var overlay = FindOverlay(window);
            var card = FindGuideCard(window);
            Assert.NotNull(overlay);
            Assert.NotNull(card);

            // Everything, not just the card: the top bar says what this is and
            // the buttons are how it gets dismissed, so an overlay that only
            // keeps the card on screen has still failed.
            var bottom = overlay!.TranslatePoint(new Point(0, overlay.Bounds.Height), window)!.Value.Y;
            Assert.True(bottom <= window.ClientSize.Height,
                $"the how-to ends at {bottom:F0} on a {width}x{height} window: the last line is off screen");

            // The card has to fit inside the overlay rather than be clipped by
            // it. A card whose last line runs past the overlay's own edge is the
            // scrolling failure in its other shape: the operator still cannot
            // read the end of the how-to, whatever is around it.
            var cardBottom = card!.TranslatePoint(new Point(0, card.Bounds.Height), overlay)!.Value.Y;
            Assert.True(cardBottom <= overlay.Bounds.Height + 1,
                $"the card ends at {cardBottom:F0} inside an overlay {overlay.Bounds.Height:F0} tall " +
                $"on a {width}x{height} window");

            foreach (var badge in Descendants(card).OfType<Border>().Where(b => b.Classes.Contains("stepBadge")))
            {
                var top = badge.TranslatePoint(new Point(0, 0), window)!.Value.Y;
                Assert.True(top > 0 && top < window.ClientSize.Height,
                    $"a step badge sits at {top:F0} on a {width}x{height} window");
            }
        }
    }

    // ---- colour ----

    [AvaloniaFact]
    public void TheOverlay_UsesThemeColours_NotAHardcodedLightCard()
    {
        // The old card painted itself #FFF3CD with #856404 text, which is a light
        // yellow card in an app that ships a dark theme and asks for one by
        // default. A source grep cannot see that; the rendered pixels can.
        var window = ShowOverlay();
        var card = FindGuideCard(window);
        Assert.NotNull(card);

        var background = card!.Background as ISolidColorBrush;
        Assert.NotNull(background);

        // The page behind the card is dark, so a card this bright would be a block
        // of light in the middle of it. The old card was #FFF3CD.
        var luminance = 0.2126 * background!.Color.R + 0.7152 * background.Color.G + 0.0722 * background.Color.B;
        Assert.True(luminance < 90,
            $"the card is much brighter than the dark page it sits on: {luminance:F0}");

        // And the border comes from the theme, so the light theme gets the light
        // theme's warning colour instead of a yellow that fits neither.
        var border = card.BorderBrush as ISolidColorBrush;
        Assert.NotNull(border);
        var page = window.Background as ISolidColorBrush;
        Assert.NotNull(page);
        Assert.NotEqual(page!.Color, border!.Color);
    }

    [AvaloniaFact]
    public void TheOverlay_FollowsTheLightThemeToo()
    {
        // The dark case only proves the card is not glaring white. The light case is
        // what proves the card is reading the theme at all: a card that hardcoded
        // #FFF3CD would still be light here, and a card that hardcoded the dark
        // card colour would be a black block on a white page. Neither shows up in
        // the dark case, so both are measured.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Light";
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        window.Height = 900;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var card = FindGuideCard(window);
        Assert.NotNull(card);
        Assert.True(Luminance(card!.Background as ISolidColorBrush) > 90,
            "the light theme gets a light card, so the card colour comes from the theme");

        // And it is not the same light yellow as before, which belonged to neither
        // theme and was chosen by copying a bootstrap warning.
        var background = card.Background as ISolidColorBrush;
        Assert.NotEqual(Color.Parse("#FFF3CD"), background!.Color);
    }

    // ---- what the button inside it does ----

    [AvaloniaFact]
    public async Task SearchingAgain_KeepsTheHowToOnScreen_WhenTheProbeHasNoAnswer()
    {
        // The bug this is here for: "Opnieuw zoeken" closed the how-to before
        // it asked, and a probe that came back with nothing to say about the
        // phone left it closed. An operator tapping the button that promises
        // another look was put back on the idle screen, mid-instructions, with
        // the phone still on the cable still showing the prompt.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.DeviceProbe = () => Task.FromResult((
            new Dictionary<string, string>(),
            DeviceService.ConnectionState.NotFound));
        vm.ShowAdbWarning = true;

        await vm.RetryAdbDetectionCommand.Execute();

        Assert.True(vm.ShowAdbWarning,
            "the how-to went away on an answer that said nothing about the phone");
    }

    [AvaloniaFact]
    public async Task SearchingAgain_ReopensAHowToThatWasDismissed()
    {
        // The other half of that button: a phone whose how-to was closed is
        // worth asking about once more, so the dismissal is what it gives up.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        // Named up front so the refresh does not go off and ask the OS for a
        // name; that lookup belongs to the guide opening, not to this button.
        vm.AdbTutorialViewModel.SetDevice("Samsung", "Galaxy A55");
        vm.DeviceProbe = () => Task.FromResult((
            new Dictionary<string, string>(),
            DeviceService.ConnectionState.Unauthorized));

        await vm.DismissAdbGuideCommand.Execute();
        Assert.False(vm.ShowAdbWarning);

        await vm.RetryAdbDetectionCommand.Execute();

        Assert.True(vm.ShowAdbWarning);
    }

    [AvaloniaFact]
    public async Task SearchingAgain_TakesTheHowToOff_WhenThePhoneIsTrusted()
    {
        // Looking again is also how the how-to ends when the operator tapped
        // Allow on the phone in the meantime: adb can talk to it, so there is
        // nothing left to tell them.
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.AdbTutorialViewModel.SetDevice("Samsung", "Galaxy A55");
        vm.DeviceProbe = () => Task.FromResult((
            new Dictionary<string, string>(),
            DeviceService.ConnectionState.Connected));
        vm.ShowAdbWarning = true;

        await vm.RetryAdbDetectionCommand.Execute();

        Assert.False(vm.ShowAdbWarning);
    }

    private static double Luminance(ISolidColorBrush? brush)
    {
        Assert.NotNull(brush);
        return 0.2126 * brush!.Color.R + 0.7152 * brush.Color.G + 0.0722 * brush.Color.B;
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }

    // ---- helpers ----

    /// <summary>The whole overlay, in the order it lays out.</summary>
    private static string GuideText() => GuideText(ShowOverlay());

    private static string GuideText(Window window)
    {
        var overlay = FindOverlay(window);
        Assert.NotNull(overlay);
        return Flatten(overlay!);
    }

    /// <summary>The number inside a step badge.</summary>
    private static string BadgeText(Border badge)
    {
        var text = Descendants(badge).OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        return text.Trim();
    }

    /// <summary>An overlay in its "phone needs attention" state, on a known theme.</summary>
    private static MainWindow ShowOverlay()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        // Tall enough that the whole overlay is inside the viewport, which is
        // where the other sizes come in below.
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    /// <summary>
    /// The overlay itself, by the name it carries in the markup rather than by a
    /// text match, so a copy edit cannot make the overlay stop being found.
    /// </summary>
    private static Panel? FindOverlay(Visual root) =>
        Descendants(root).OfType<Panel>().FirstOrDefault(p => p.Name == "AdbGuideOverlay");

    /// <summary>
    /// The card holding the guide, found by the badge style rather than by a text
    /// match. Locating it by content means the test cannot tell a reordered guide
    /// from a missing one, and would break for the wrong reason on every copy edit.
    /// </summary>
    private static Border? FindGuideCard(Visual root)
    {
        var badge = Descendants(root).OfType<Border>().FirstOrDefault(b => b.Classes.Contains("stepBadge"));
        if (badge is null) return null;

        for (Visual? node = badge; node is not null; node = node.GetVisualParent())
        {
            if (node is Border card && card.BorderThickness.Left >= 1 && card.Padding.Left > 0) return card;
        }

        return null;
    }

    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root is null) yield break;
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }

    /// <summary>The overlay's words, one TextBlock per line.</summary>
    private static string Flatten(Visual root)
    {
        var lines = Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? "");
        return string.Join("\n", lines);
    }
}
