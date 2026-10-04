using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// The device dropdown is the one control that hands its selection back to the view
// model as a KeyValuePair, and a struct cannot express "nothing selected". While the
// dropdown cannot match the value it is holding it answers by handing over null, and
// writing null into that struct fails. A failing write is recorded on the element,
// and Avalonia prints what it recorded underneath the control. In the window that
// showed up as the text of the conversion failure, permanently, below the list of
// detected devices.
//
// The assertions read DataValidationErrors.HasErrors rather than looking for the
// printed message: that flag is exactly what the template reads to decide whether to
// print anything, and it is set even in a headless window that has not laid the
// message out. Both the cause and the symptom are covered that way.
public class DeviceDropdownTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"dropdown-settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public DeviceDropdownTests()
    {
        // Isolate settings so another test's Theme or defaults can't leak in.
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void RebuildingTheDeviceList_LeavesNoErrorOnTheDropdown()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1312;
        window.Height = 925;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        vm.Devices = new ObservableCollection<KeyValuePair<string, string>>(
            new[] { new KeyValuePair<string, string>("TEST-ONE", "Test phone one") });
        vm.SelectedDevice = vm.Devices[0];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        ComboBox dropdown = DeviceDropdown(window, vm);

        // What every refresh does: a freshly built dictionary, so the same devices
        // under objects the dropdown was never holding.
        vm.Devices = new ObservableCollection<KeyValuePair<string, string>>(
            new[] { new KeyValuePair<string, string>("TEST-ONE", "Test phone one") });

        // ... and the selection is only put back when its key has gone, which here it
        // has not, so nothing tells the view model a second time.
        string currentKey = vm.SelectedDevice.Key ?? "";
        if (!vm.Devices.Any(device => device.Key == currentKey)) vm.SelectedDevice = vm.Devices[0];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.False(HasError(dropdown), ErrorText(dropdown));
        Assert.Equal(vm.SelectedDevice, dropdown.SelectedItem);
        HeadlessRender.Drain();

    }

    [AvaloniaFact]
    public void EmptyingTheDeviceList_LeavesNoErrorOnTheDropdown()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1312;
        window.Height = 925;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        vm.Devices = new ObservableCollection<KeyValuePair<string, string>>(
            new[] { new KeyValuePair<string, string>("TEST-ONE", "Test phone one") });
        vm.SelectedDevice = vm.Devices[0];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        ComboBox dropdown = DeviceDropdown(window, vm);

        // The other half of the same refresh, where nothing was detected: the list is
        // emptied and the view model is told there is nothing to select.
        vm.Devices.Clear();
        vm.SelectedDevice = new KeyValuePair<string, string>("", "");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.False(HasError(dropdown), ErrorText(dropdown));
        HeadlessRender.Drain();

    }

    [AvaloniaFact]
    public void PickingADeviceInTheDropdown_ReachesTheViewModel()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1312;
        window.Height = 925;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        vm.Devices = new ObservableCollection<KeyValuePair<string, string>>(
            new[]
            {
                new KeyValuePair<string, string>("TEST-ONE", "Test phone one"),
                new KeyValuePair<string, string>("TEST-TWO", "Test phone two"),
            });
        vm.SelectedDevice = vm.Devices[0];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        ComboBox dropdown = DeviceDropdown(window, vm);

        // The direction the fix leaves open: choosing by hand still has to arrive.
        dropdown.SelectedItem = vm.Devices[1];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.Equal("TEST-TWO", vm.SelectedDevice.Key);
        Assert.False(HasError(dropdown), ErrorText(dropdown));
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

    private static ComboBox DeviceDropdown(Visual root, MainWindowViewModel vm) =>
        root.GetVisualDescendants().OfType<ComboBox>()
            .First(control => ReferenceEquals(control.ItemsSource, vm.Devices));

    private static bool HasError(ComboBox dropdown) =>
        dropdown.GetValue(FindProperty()) is true;

    private static string ErrorText(ComboBox dropdown)
    {
        if (!HasError(dropdown)) return "no error";
        object? errors = dropdown.GetValue(FindProperty());
        return $"the dropdown is holding a failed write: {errors}";
    }

    /// <summary>The flag Avalonia's error template reads before printing anything.</summary>
    private static AvaloniaProperty FindProperty()
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly =>
            {
                try { return assembly.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .FirstOrDefault(candidate => candidate.Name == "DataValidationErrors")
            ?? throw new InvalidOperationException(
                "Avalonia no longer exposes DataValidationErrors, so nothing can say whether the dropdown failed to write.");

        FieldInfo field = type.GetField("HasErrorsProperty", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("DataValidationErrors.HasErrorsProperty is gone.");

        return (AvaloniaProperty?)field.GetValue(null)
            ?? throw new InvalidOperationException("DataValidationErrors.HasErrorsProperty was never set.");
    }
}
