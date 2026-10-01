using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using PhoneGrade.Core;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Tests.TestAppBuilder))]

namespace Tests;

// ============ Real UI tests: headless Avalonia constructs the actual windows ============

public class UiTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"ui-settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public UiTests()
    {
        // Isolate from the real user settings file so tests are order-independent.
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void MainWindow_Constructs_WithNativeChrome()
    {
        using var window = new MainWindow();

        // The Wayland bug: ExtendClientAreaToDecorationsHint=true removes resize/close buttons.
        Assert.False(window.ExtendClientAreaToDecorationsHint);
        Assert.True(window.CanResize);
        Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        Assert.IsType<MainWindowViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public async Task QualityAndPaymentSelection_AdvancesStateMachineAndUpdatesStatus()
    {
        using var vm = new MainWindowViewModel();
        vm.SelectedDevice = new System.Collections.Generic.KeyValuePair<string, string>("MOCK_UDID", "iPhone 13");
        vm.DefaultQuality = "";
        vm.DefaultPaymentMethod = "";
        vm.RequirePwaTest = false;

        // Start inspection completion flow
        vm.FinishInspectionCommand.Execute().Subscribe();
        Assert.True(vm.IsQualityPopupVisible);
        Assert.Equal(LocalizationManager.GetString("Status_ChooseQuality"), vm.Status);
        Assert.NotEqual("Status_ChooseQuality", vm.Status);

        // User clicks Quality "B"
        vm.SetQualityCommand.Execute("B").Subscribe();
        Assert.False(vm.IsQualityPopupVisible);
        Assert.True(vm.IsPaymentPopupVisible);
        Assert.Equal(90, vm.Progress);
        Assert.Equal(LocalizationManager.GetString("Status_ChoosePayment"), vm.Status);
        Assert.NotEqual("Status_ChoosePayment", vm.Status);
        Assert.Equal("KLASSE B", vm.SelectedGradeDisplay);

        // User clicks Payment "Marge"
        vm.SetPaymentMethodCommand.Execute("Marge").Subscribe();
        Assert.False(vm.IsPaymentPopupVisible);
        Assert.Equal(100, vm.Progress);
        Assert.Equal(AppWorkflowState.Summary, vm.WorkflowState);
        Assert.Equal("Marge (0% BTW)", vm.SelectedInvoiceMethodDisplay);
        Assert.Equal(LocalizationManager.GetString("Status_TestsComplete"), vm.Status);
        Assert.NotEqual("Status_TestsComplete", vm.Status);

        // Verify persisted to DeviceSessionManager
        Assert.True(DeviceSessionManager.IsDeviceCompleted("MOCK_UDID"));
    }

    [AvaloniaFact]
    public void MainWindow_ThemeSwitch_TakesEffectImmediatelyAndPersists()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        Assert.Equal("Dark", vm.Theme);

        vm.Theme = "Light";
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        Assert.Equal("Light", AppSettings.Load().Theme);

        vm.Theme = "Dark";
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal("Dark", AppSettings.Load().Theme);
    }

    [AvaloniaFact]
    public void MainWindow_PopupsStartHidden_WithAskingDefaults()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        Assert.False(vm.IsQualityPopupVisible);
        Assert.False(vm.IsPaymentPopupVisible);
        Assert.Equal(string.Empty, vm.DefaultQuality);       // "" = ask
        Assert.Equal(string.Empty, vm.DefaultPaymentMethod); // "" = ask
    }

    [AvaloniaFact]
    public void MainWindow_OfferedOptions_AreComplete()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        Assert.Equal(new[] { "Dark", "Light", "System" }, vm.ThemeOptions);
        Assert.Equal(new[] { "", "A", "B", "C" }, vm.QualityOptions);
        Assert.Equal(new[] { "", "Marge", "BTW" }, vm.PaymentOptions);
    }

    [AvaloniaFact]
    public void MainWindow_HasDevice_FalseUntilRealData()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        Assert.False(vm.HasDevice); // fresh VM holds placeholder data

        vm.DeviceData = new DeviceData { Model = "13Pro", Identifier = "356938035643809", Storage = "256GB" };
        Assert.True(vm.HasDevice);
    }

    [AvaloniaFact]
    public void DataEditorWindow_Constructs_BoundToDeviceData()
    {
        var data = new DeviceData { Model = "13Pro", Storage = "256GB" };
        var editor = new DataEditorWindow { DataContext = new DataEditorViewModel(data) };

        Assert.True(editor.CanResize);
        Assert.False(editor.ExtendClientAreaToDecorationsHint);
        Assert.Same(data, ((DataEditorViewModel)editor.DataContext!).DeviceData);
    }

    [AvaloniaFact]
    public void SeverityConverter_MapsAllSeveritiesToBrushes()
    {
        var c = SeverityToBrushConverter.Instance;
        Assert.NotNull(c.Convert(Severity.Ok, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(Severity.Warning, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(Severity.Error, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(null, typeof(IBrush), null, null)); // unknown → fallback
    }

    [AvaloniaFact]
    public void ComponentStatusConverter_MapsAllStatusesToBrushes()
    {
        var c = ComponentStatusToBrushConverter.Instance;
        Assert.NotNull(c.Convert(ComponentStatusType.Match, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(ComponentStatusType.Mismatch, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(ComponentStatusType.Untrusted, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(ComponentStatusType.Unknown, typeof(IBrush), null, null));
        Assert.NotNull(c.Convert(null, typeof(IBrush), null, null));
    }

    [AvaloniaFact]
    public void ModelDisplayConverter_DoesNotCallAnAndroidPhoneAnIPhone()
    {
        var c = new PhoneGrade.UI.Converters.ModelDisplayConverter();
        var pixel = new DeviceData
        {
            Model = "Google Pixel 8 Pro",
            ProductType = "Android (Google Pixel 8 Pro)",
        };

        Assert.Equal("Google Pixel 8 Pro", c.Convert(pixel, typeof(string), null, null));
    }

    [AvaloniaFact]
    public void ModelDisplayConverter_StillPrefixesAppleModels()
    {
        var c = new PhoneGrade.UI.Converters.ModelDisplayConverter();
        var iphone = new DeviceData { Model = "8", ProductType = "iPhone10,1" };
        var ipad = new DeviceData { Model = "Air 11", ProductType = "iPad14,3" };

        Assert.Equal("iPhone 8", c.Convert(iphone, typeof(string), null, null));
        Assert.Equal("iPad Air 11", c.Convert(ipad, typeof(string), null, null));
    }

    [AvaloniaFact]
    public void ModelDisplayConverter_FallsBackForNonDeviceData()
    {
        var c = new PhoneGrade.UI.Converters.ModelDisplayConverter();
        Assert.Equal(PhoneGrade.UI.Services.LocalizationManager.GetString("Value_UnknownDevice"),
            c.Convert(null, typeof(string), null, null));
        Assert.Equal("iPhone 8", c.Convert("8", typeof(string), null, null));
    }

    [AvaloniaFact]
    public void BatteryLevelConverter_ShowsTheChargeOnlyWhenKnown()
    {
        var c = new PhoneGrade.UI.Converters.BatteryLevelConverter();
        Assert.Equal(
            string.Format(PhoneGrade.UI.Services.LocalizationManager.GetString("Value_BatteryLevel"), 20),
            c.Convert(20, typeof(string), null, null));
        Assert.Equal("", c.Convert(null, typeof(string), null, null));
        Assert.Equal("", c.Convert(0, typeof(string), null, null));
    }

    [AvaloniaFact]
    public void MemoryConverter_PassesTheSizeThroughAndSoftensThePlaceholder()
    {
        var c = new PhoneGrade.UI.Converters.MemoryConverter();
        Assert.Equal("12GB", c.Convert("12GB", typeof(string), null, null));

        // iOS never reports the installed memory, and a raw placeholder on a
        // graded device's label would read as a fault rather than as a gap.
        var notSet = PhoneGrade.UI.Services.LocalizationManager.GetString("Value_NotSet");
        Assert.Equal(notSet, c.Convert("NOMEMORY", typeof(string), null, null));
        Assert.Equal(notSet, c.Convert("", typeof(string), null, null));
        Assert.Equal(notSet, c.Convert(null, typeof(string), null, null));
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<PhoneGrade.UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
