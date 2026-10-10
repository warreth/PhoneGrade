using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The IMEI API section ============
//
// This is the one part of settings that spends money. It takes a key off the
// operator and then charges their imei.info account for every check, so the
// reason it wants the key and the price of what it does with it belong on
// screen before the field does, not after.
//
// It also had three buttons out to the same website, two of which led to pages
// the four steps beside them already walk through. One door is enough, and the
// count is measured on the rendered section because a source file cannot tell
// how many buttons the operator is looking at.

public class ImeiSettingsTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"imei-settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public ImeiSettingsTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    [AvaloniaFact]
    public void TheSection_ExplainsTheKeyBeforeItAsksForOne()
    {
        using var window = OpenSection();
        var vm = (MainWindowViewModel)window.DataContext!;

        var why = CardHolding(window, vm.OpenImeiDashboardCommand);
        var apiKey = CardHolding(window, vm.SaveImeiInfoApiKeyCommand);

        // Above the field, not below it. An operator who has already pasted a
        // key has been told nothing by a paragraph they scroll past afterwards.
        Assert.True(why.Bounds.Y < apiKey.Bounds.Y,
            $"the explanation sits at {why.Bounds.Y:F0} and the key field at {apiKey.Bounds.Y:F0}");

        // Both halves of it, asked for by key rather than by wording, so the
        // copy in either language decides nothing about whether this passes.
        string text = Flatten(why);
        Assert.Contains(LocalizationManager.GetString("Settings_ImeiInfoWhy"), text);
        Assert.Contains(LocalizationManager.GetString("Settings_ImeiInfoWhyCredits"), text);
    }

    [AvaloniaFact]
    public void TheSection_HasOneDoorToTheWebsite_NotThree()
    {
        using var window = OpenSection();
        var vm = (MainWindowViewModel)window.DataContext!;

        Panel section = (Panel)CardHolding(window, vm.OpenImeiDashboardCommand).GetVisualParent()!;

        // CheckBox derives from Button in Avalonia, so the four checks in the
        // list further down are buttons as well; only the three that carry a
        // command act on anything.
        var buttons = Descendants(section).OfType<Button>()
            .Where(button => button.IsEffectivelyVisible
                             && button is not CheckBox
                             && button.Command is not null)
            .ToList();

        // The save and the test belong to the key field, and the dashboard is
        // the only page worth opening: registration and credits are both inside
        // it, and both are already written into the four steps.
        Assert.True(buttons.Count == 3,
            $"expected the save, the test and one dashboard button, found {buttons.Count}: "
            + string.Join(", ", buttons.Select(button => button.Content)));
        Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.OpenImeiDashboardCommand));
        Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.SaveImeiInfoApiKeyCommand));
        Assert.Single(buttons, button => ReferenceEquals(button.Command, vm.TestImeiInfoApiKeyCommand));
        HeadlessRender.Drain();

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

    /// <summary>The window with the IMEI API topic open and laid out.</summary>
    private static MainWindow OpenSection()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        window.Show();
        window.Width = 1200;
        window.Height = 1400;
        vm.IsSettingsDrawerOpen = true;
        vm.SelectedSettingsSection = "ImeiApi";
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        return window;
    }

    /// <summary>The settings card a given command's button sits in.</summary>
    private static Border CardHolding(Visual root, ICommand command)
    {
        var button = Descendants(root).OfType<Button>()
            .FirstOrDefault(candidate => candidate.IsEffectivelyVisible
                                         && ReferenceEquals(candidate.Command, command));
        Assert.NotNull(button);

        for (Visual? node = button; node is not null; node = node.GetVisualParent())
        {
            if (node is Border border && border.Classes.Contains("settingCard")) return border;
        }

        throw new InvalidOperationException("the button is not inside a settings card");
    }

    /// <summary>A card's words, one TextBlock per line.</summary>
    private static string Flatten(Visual root) =>
        string.Join("\n", Descendants(root).OfType<TextBlock>().Select(block => block.Text ?? ""));

    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root is null) yield break;
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }
}
