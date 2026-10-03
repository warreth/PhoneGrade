using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Threading;
using PhoneGrade.Core.Usb;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// The content of the USB debugging how-to: which phone was found, and the four
/// steps that get it talking to this machine.
///
/// The card is never empty and never half-filled. It opens with the generic
/// instructions for an unnamed Android device and is narrowed down once a phone
/// is actually identified, because the two ways it opens - a USB connection
/// event, and adb reporting a phone it does not trust - do not always arrive
/// together. The phone that was already on the cable before this process
/// started never produces a connection event at all, which is why
/// <see cref="IdentifyIfUnknown"/> exists.
///
/// The visibility of the card is not decided here. That belongs to
/// ShowAdbWarning in the main window view model, so the guide and the adb probe
/// that found the problem share one flag instead of two.
/// </summary>
public class AdbTutorialViewModel : ReactiveObject, IDisposable
{
    private readonly AdbDeviceDirector _director;
    private readonly Dictionary<string, string[]> _brandSteps;
    private readonly string[] _genericSteps;

    private string _manufacturer = "";
    private string _deviceIdentity = "";
    private string _manufacturerPrefix = "";
    private StepItem[] _steps;
    private IDisposable? _poll;

    public AdbTutorialViewModel(AdbDeviceDirector director)
    {
        _director = director;
        _brandSteps = BuildBrandSteps();
        _genericSteps = new[]
        {
            "AdbTutorial_Generic_Step1",
            "AdbTutorial_Generic_Step2",
        };

        _steps = BuildSteps("");
        _deviceIdentity = LocalizationManager.GetString("AdbTutorial_AndroidDevice");
        _manufacturerPrefix = string.Format(
            LocalizationManager.GetString("AdbTutorial_ManufacturerPrefix"), _deviceIdentity);

        _director.AdbRequired += OnAdbRequired;
    }

    /// <summary>Raised when an Android device needs the how-to on screen.</summary>
    public event EventHandler? Requested;

    /// <summary>Raised when adb can talk to a phone again, so the how-to has done its job.</summary>
    public event EventHandler? Authorized;

    /// <summary>The brand behind the phone on the cable, empty when none is known.</summary>
    public string Manufacturer
    {
        get => _manufacturer;
        private set => this.RaiseAndSetIfChanged(ref _manufacturer, value);
    }

    /// <summary>
    /// What the heading calls the phone: the model when the OS reported one,
    /// otherwise "Samsung device", otherwise "Android device". Never empty, so
    /// the heading cannot come out as "For your :" with nothing in the middle.
    /// </summary>
    public string DeviceIdentity
    {
        get => _deviceIdentity;
        private set => this.RaiseAndSetIfChanged(ref _deviceIdentity, value);
    }

    /// <summary>The localized heading above the steps, built from <see cref="DeviceIdentity"/>.</summary>
    public string ManufacturerPrefix
    {
        get => _manufacturerPrefix;
        private set => this.RaiseAndSetIfChanged(ref _manufacturerPrefix, value);
    }

    /// <summary>The four steps to follow, keys resolved at render time.</summary>
    public StepItem[] Steps
    {
        get => _steps;
        private set => this.RaiseAndSetIfChanged(ref _steps, value);
    }

    /// <summary>One line of the how-to, by localization key.</summary>
    public sealed class StepItem
    {
        public int Index { get; set; }
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// Points the card at a specific phone. Safe to call before the card is
    /// shown, which is how the connection event uses it.
    /// </summary>
    public void SetDevice(string manufacturer, string modelName)
    {
        string brand = manufacturer?.Trim() ?? "";
        string model = modelName?.Trim() ?? "";

        Manufacturer = brand;
        DeviceIdentity = IdentityFor(brand, model);
        ManufacturerPrefix = string.Format(
            LocalizationManager.GetString("AdbTutorial_ManufacturerPrefix"), DeviceIdentity);
        Steps = BuildSteps(brand);
    }

    /// <summary>
    /// Names the phone when the guide opened without one. The phone was
    /// probably already plugged in when this process started, in which case no
    /// connection event ever fires for it and the OS is asked directly instead.
    /// </summary>
    public void IdentifyIfUnknown()
    {
        if (Manufacturer.Length > 0) return;

        (string manufacturer, string model) = _director.IdentifyConnectedDevice();
        if (manufacturer.Length > 0) SetDevice(manufacturer, model);
    }

    /// <summary>
    /// Watches for the phone to be trusted, so the card can close itself the
    /// moment it is. Started by a connection event: when the card was opened by
    /// the adb probe instead, that probe is already running and closes it.
    /// </summary>
    public void WatchForAuthorization()
    {
        _poll?.Dispose();
        _poll = Observable.Interval(TimeSpan.FromSeconds(2))
            .SelectMany(async _ => await _director.IsAuthorizedAsync())
            .Where(authorized => authorized)
            .Take(1)
            .Subscribe(_ => Authorized?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Re-reads the heading and rebuilds the steps after a language change.
    /// The steps carry keys rather than text, so only a new array makes the
    /// bindings run the converter again.
    /// </summary>
    public void RefreshLocalization()
    {
        ManufacturerPrefix = string.Format(
            LocalizationManager.GetString("AdbTutorial_ManufacturerPrefix"), DeviceIdentity);
        Steps = BuildSteps(Manufacturer);
    }

    private void OnAdbRequired(object? sender, AdbRequiredEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            SetDevice(e.Manufacturer, e.ModelName);
            WatchForAuthorization();
            Requested?.Invoke(this, EventArgs.Empty);
        });

    /// <summary>Where each brand asks for the build number, in step order.</summary>
    private StepItem[] BuildSteps(string manufacturer)
    {
        string[] brand = _brandSteps.TryGetValue(manufacturer, out string[]? found)
            ? found
            : _genericSteps;

        return new[]
        {
            new StepItem { Index = 1, Value = "AdbTutorial_Common_ScreenLock" },
            new StepItem { Index = 2, Value = brand[0] },
            new StepItem { Index = 3, Value = brand[1] },
            new StepItem { Index = 4, Value = "AdbTutorial_Common_Authorize" },
        };
    }

    private static string IdentityFor(string manufacturer, string model)
    {
        if (model.Length > 0) return model;
        if (manufacturer.Length > 0)
            return string.Format(LocalizationManager.GetString("AdbTutorial_DeviceWord"), manufacturer);

        return LocalizationManager.GetString("AdbTutorial_AndroidDevice");
    }

    private static Dictionary<string, string[]> BuildBrandSteps()
    {
        string[] For(string brand) => new[]
        {
            $"AdbTutorial_{brand}_Step1",
            $"AdbTutorial_{brand}_Step2",
        };

        // Anything not in here - Honor, ZTE, Poco and the rest - takes the
        // generic pair rather than pretending to know that vendor's menus.
        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Samsung"] = For("Samsung"),
            ["Xiaomi"] = For("Xiaomi"),
            ["Google"] = For("Google"),
            ["Motorola"] = For("Motorola"),
            ["Sony"] = For("Sony"),
            ["Huawei"] = For("Huawei"),
            ["OnePlus"] = For("OnePlus"),
        };
    }

    public void Dispose()
    {
        _director.AdbRequired -= OnAdbRequired;
        _poll?.Dispose();
        _poll = null;
    }
}
