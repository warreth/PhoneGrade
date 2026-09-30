using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The USB debugging guide ============

// This card appears only when the phone is connected but has not accepted this
// computer. Nothing is wrong with the phone; what is missing is a tap on the
// phone, and that tap has to happen in the right order or nothing changes.
//
// So this is tested against the rendered card rather than against the source
// file. A source grep cannot tell whether the card is reachable, whether the
// binding fires, or whether a style selector ever matched anything, and those
// are exactly the three ways this card has been useless.

public class UsbDebuggingGuideTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"usb-guide-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public UsbDebuggingGuideTests()
    {
        // The guide uses DynamicResource colours, so the theme has to be loaded
        // from a known state rather than left over from whichever test ran before.
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void TheGuide_IsVisible_WhenThePhoneDoesNotTrustThisComputer()
    {
        var card = ShowGuideCard();

        Assert.True(card.IsEffectivelyVisible,
            "the card is bound to ShowAdbWarning, so a false here means the operator " +
            "is never shown the steps that would fix it");
    }

    [AvaloniaFact]
    public void TheGuide_IsHidden_WhenThePhoneIsTrusted()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.ShowAdbWarning = false;

        window.Show();
        window.Width = 900;
        window.Height = 1000;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var card = FindGuideCard(window);
        Assert.False(card.IsEffectivelyVisible, "a phone that is already trusted has nothing to be told");
    }

    [AvaloniaFact]
    public void TheGuide_SaysThePhoneItselfIsFine()
    {
        // The card appears when the computer is not trusted. Read as a defect
        // report it is alarming, and the operator goes off to swap the cable
        // instead of tapping the phone.
        Assert.Contains("telefoon zelf is prima", GuideText());
    }

    [AvaloniaFact]
    public void TheGuide_IsInDutch_LikeTheRestOfTheApp()
    {
        // The card was the only English string left in an otherwise Dutch window,
        // which reads on screen as a half-finished product.
        var text = GuideText();
        foreach (var english in new[] { "Android device connected", "Retry ADB Detection", "USB Debugging is turned ON" })
        {
            Assert.DoesNotContain(english, text);
        }

        Assert.Contains("Opnieuw zoeken", text);
    }

    [AvaloniaFact]
    public void TheGuide_DoesNotAskForMtpMode()
    {
        // Measured on the real Pixel: with USB debugging on, adb connects whatever
        // the USB mode is set to. Switching to File Transfer changes nothing about
        // the trust state and costs the operator a trip through the notification
        // shade, so the old instruction was pure cost.
        Assert.DoesNotContain("MTP", GuideText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bestandsoverdracht", GuideText(), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheGuide_NamesTheScreenLock_WhichIsWhyNoPromptAppears()
    {
        // This is the most common reason for "nothing happens": the prompt is on a
        // screen nobody is looking at, because the phone went to sleep in the
        // seconds between the cable going in and the keypress.
        Assert.Contains("Ontgrendel het scherm", GuideText());
    }

    [AvaloniaFact]
    public void TheGuide_TellsTheOperatorToTickAlwaysAllow()
    {
        // Without this tick the phone asks again on every new cable and the pc
        // lands back on unauthorized every time, which is the same card over and
        // over with no way out. It is the one line that decides whether it sticks.
        Assert.Contains("Altijd toestaan vanaf deze computer", GuideText());
    }

    [AvaloniaFact]
    public void TheGuide_CoversTheCaseWhereNoPromptAppearsAtAll()
    {
        // If developer options was never switched on, or the prompt scrolled past,
        // there is nothing to tap. The path to the setting is the fallback that
        // makes the guide work for a phone nobody has set up before.
        Assert.Contains("Build-nummer", GuideText());
        Assert.Contains("zeven keer", GuideText());
        Assert.Contains("USB-debugging", GuideText());
    }

    [AvaloniaFact]
    public void TheGuide_IsNumbered_AndTheBadgesActuallyResolve()
    {
        var window = ShowGuideCard();
        var card = FindGuideCard(window);

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
    public void TheGuide_StepsRunDownwards_EachNumberBesideItsOwnLine()
    {
        var window = ShowGuideCard();
        var card = FindGuideCard(window);

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
    public void TheGuide_FitsOnAWindowWithoutScrolling()
    {
        // A how-to that runs off the bottom is how-to that gets half read. Measured
        // rather than assumed, because the card only shows on a window size nobody
        // chose.
        var window = ShowGuideCard();
        var card = FindGuideCard(window);

        var bottom = card.TranslatePoint(new Point(0, card.Bounds.Height), window)!.Value.Y;
        Assert.True(bottom <= window.ClientSize.Height,
            $"the guide ends at {bottom:F0} on a {window.ClientSize.Height:F0} window: the last step is off screen");

        // Every step is inside it too, not just the padding.
        foreach (var badge in Descendants(card).OfType<Border>().Where(b => b.Classes.Contains("stepBadge")))
        {
            var top = badge.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.True(top > 0 && top < window.ClientSize.Height, $"a step badge sits at {top:F0}");
        }
    }

    [AvaloniaFact]
    public void TheGuide_UsesThemeColours_NotAHardcodedLightCard()
    {
        // The old card painted itself #FFF3CD with #856404 text, which is a light
        // yellow card in an app that ships a dark theme and asks for one by
        // default. A source grep cannot see that; the rendered pixels can.
        var window = ShowGuideCard();
        var card = FindGuideCard(window);

        var background = card.Background as ISolidColorBrush;
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
    public void TheGuide_FollowsTheLightThemeToo()
    {
        // The dark case only proves the card is not glaring white. The light case is
        // what proves the card is reading the theme at all: a card that hardcoded
        // #FFF3CD would still be light here, and a card that hardcoded the dark
        // card colour would be a black block on a white page. Neither shows up in
        // the dark case, so both are measured.
        var window = new MainWindow();
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

    /// <summary>Every TextBlock in the card, in the order they are laid out.</summary>
    private static string GuideText()
    {
        var window = ShowGuideCard();
        var card = FindGuideCard(window);
        Assert.NotNull(card);
        return Flatten(card!);
    }

    /// <summary>The number inside a step badge.</summary>
    private static string BadgeText(Border badge)
    {
        var text = Descendants(badge).OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        return text.Trim();
    }

    private static MainWindow ShowGuideCard()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        vm.ShowAdbWarning = true;

        window.Show();
        window.Width = 900;
        // Tall enough that the whole card is inside the viewport. A card that only
        // fits on a large monitor is a card most operators never finish reading.
        window.Height = 1400;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

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

    /// <summary>The card's visible words, one TextBlock per line.</summary>
    private static string Flatten(Visual root)
    {
        var lines = Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? "");
        return string.Join("\n", lines);
    }
}
