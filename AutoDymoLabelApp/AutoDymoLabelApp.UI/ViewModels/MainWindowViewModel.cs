using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoDymoLabel.Core;
using AutoDymoLabel.Core.Diagnostics;
using AutoDymoLabelApp.UI.Models;
using Avalonia.Threading;
using DynamicData;
using ReactiveUI;

namespace AutoDymoLabelApp.UI.ViewModels;

/// <summary>
/// Main window logic. One button for the user: the flow starts automatically when a
/// device is plugged in (when AutoDetectOnPlug is on), walks connect → activate →
/// read → diagnose → label, and asks only the questions settings can't answer
/// (quality, payment) unless defaults are configured.
/// </summary>
public class MainWindowViewModel : ReactiveObject
{
    private readonly AppSettings _settings;
    private CancellationTokenSource? _flowCts;
    private IDisposable? _watcher;

    public ObservableCollection<DiagnosticIssue> Issues { get; } = [];

    private ObservableCollection<KeyValuePair<string, string>> _devices = [];
    public ObservableCollection<KeyValuePair<string, string>> Devices
    {
        get => _devices;
        set => this.RaiseAndSetIfChanged(ref _devices, value);
    }

    private KeyValuePair<string, string> _selectedDevice;
    public KeyValuePair<string, string> SelectedDevice
    {
        get => _selectedDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedDevice, value);
    }

    private int _progress;
    public int Progress { get => _progress; set => this.RaiseAndSetIfChanged(ref _progress, value); }

    private string _status = "Plug een iPhone/iPad in om te starten…";
    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private string _theme = "Dark";
    public string Theme
    {
        get => _theme;
        set
        {
            _settings.Theme = value;
            _settings.Save();
            AutoDymoLabel.UI.App.ApplyTheme(value);
            this.RaiseAndSetIfChanged(ref _theme, value);
        }
    }

    private bool _autoActivate = true;
    public bool AutoActivate
    {
        get => _autoActivate;
        set { _settings.AutoActivate = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoActivate, value); }
    }

    private bool _autoDetectOnPlug = true;
    public bool AutoDetectOnPlug
    {
        get => _autoDetectOnPlug;
        set
        {
            _settings.AutoDetectOnPlug = value;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _autoDetectOnPlug, value);
            if (value) StartWatcher(); else StopWatcher();
        }
    }

    private bool _runDiagnostics = true;
    public bool RunDiagnostics
    {
        get => _runDiagnostics;
        set { _settings.RunDiagnostics = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _runDiagnostics, value); }
    }

    private bool _enable85PercentChecker = true;
    public bool Enable85PercentChecker
    {
        get => _enable85PercentChecker;
        set { _settings.Enable85PercentChecker = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _enable85PercentChecker, value); }
    }

    private bool _openEditorBeforePrint;
    public bool OpenEditorBeforePrint
    {
        get => _openEditorBeforePrint;
        set { _settings.OpenEditorBeforePrint = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _openEditorBeforePrint, value); }
    }

    private string _defaultQuality = "";
    public string DefaultQuality
    {
        get => _defaultQuality;
        set { _settings.DefaultQuality = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _defaultQuality, value); }
    }

    private string _defaultPaymentMethod = "";
    public string DefaultPaymentMethod
    {
        get => _defaultPaymentMethod;
        set { _settings.DefaultPaymentMethod = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _defaultPaymentMethod, value); }
    }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set => this.RaiseAndSetIfChanged(ref _busy, value);
    }

    private DeviceData _deviceData = new();
    public DeviceData DeviceData
    {
        get => _deviceData;
        set => this.RaiseAndSetIfChanged(ref _deviceData, value);
    }

    // Popups kept minimal: only quality & payment, and only when no default is set.
    private bool _isQualityPopupVisible;
    public bool IsQualityPopupVisible { get => _isQualityPopupVisible; set => this.RaiseAndSetIfChanged(ref _isQualityPopupVisible, value); }

    private bool _isPaymentPopupVisible;
    public bool IsPaymentPopupVisible { get => _isPaymentPopupVisible; set => this.RaiseAndSetIfChanged(ref _isPaymentPopupVisible, value); }

    private bool _hasIssues;
    public bool HasIssues { get => _hasIssues; set => this.RaiseAndSetIfChanged(ref _hasIssues, value); }

    public string[] ThemeOptions { get; } = ["Dark", "Light", "System"];
    public string[] QualityOptions { get; } = ["", "A", "B", "C"];
    public string[] PaymentOptions { get; } = ["", "Marge", "BTW"];

    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<string, Unit> SetQualityCommand { get; }
    public ReactiveCommand<string, Unit> SetPaymentMethodCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLabelCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenEditorCommand { get; }

    public event Action<DeviceData>? DataEditorRequested;

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        _theme = _settings.Theme;
        _autoActivate = _settings.AutoActivate;
        _autoDetectOnPlug = _settings.AutoDetectOnPlug;
        _runDiagnostics = _settings.RunDiagnostics;
        _enable85PercentChecker = _settings.Enable85PercentChecker;
        _openEditorBeforePrint = _settings.OpenEditorBeforePrint;
        _defaultQuality = _settings.DefaultQuality;
        _defaultPaymentMethod = _settings.DefaultPaymentMethod;
        LabelService.ConfiguredTemplatePath = _settings.TemplatePath;

        var canStart = this.WhenAnyValue(x => x.Busy).Select(b => !b);
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDeviceListAsync);
        StartCommand = ReactiveCommand.CreateFromTask(() => RunFlowAsync(), canStart);
        SetQualityCommand = ReactiveCommand.Create<string>(q => _ = ContinueAfterQualityAsync(q));
        SetPaymentMethodCommand = ReactiveCommand.Create<string>(p => ContinueAfterPaymentAsync(p));
        OpenLabelCommand = ReactiveCommand.Create(OpenLabel);
        OpenEditorCommand = ReactiveCommand.Create(() => DataEditorRequested?.Invoke(DeviceData));

        _ = RefreshDeviceListAsync();
        if (_autoDetectOnPlug) StartWatcher();
    }

    /// <summary>Polls for device changes every 2s; starts the auto flow on first sight of a device.</summary>
    private void StartWatcher()
    {
        StopWatcher();
        _watcher = Observable.Interval(TimeSpan.FromSeconds(2))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .SelectMany(_ => Observable.FromAsync(RefreshDeviceListSilentAsync))
            .Where(c => c > 0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (Busy) return;
                if (SelectedDevice.Key is { Length: > 0 })
                    RunFlowAsync(); // fire-and-forget; errors surface via Status
            });
    }

    private void StopWatcher() => _watcher?.Dispose();

    /// <summary>True when a device list refresh surfaced exactly one usable device.</summary>
    private async Task<int> RefreshDeviceListSilentAsync()
    {
        try
        {
            var devices = await DeviceService.GetConnectedDevicesAsync();
            if (devices.Count == 0) return 0;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Devices = new ObservableCollection<KeyValuePair<string, string>>(devices);
                // auto-select the single/first device
                if (devices.ContainsKey(SelectedDevice.Key) is false)
                    SelectedDevice = Devices[0];
            });
            return devices.Count;
        }
        catch { return 0; }
    }

    public async Task RefreshDeviceListAsync()
    {
        int count = await RefreshDeviceListSilentAsync();
        Status = count switch
        {
            0 => "Geen toestel gevonden. Kabel/poort proberen of toestel ontgrendelen en 'Vertrouwen' tikken.",
            1 => "Eén toestel gevonden en geselecteerd.",
            _ => $"{count} toestellen gevonden — kies er één.",
        };
    }

    /// <summary>The whole pipeline for one device.</summary>
    private async Task RunFlowAsync()
    {
        string? udid = SelectedDevice.Key;
        if (udid is not { Length: > 0 })
        {
            Status = "Geen toestel geselecteerd.";
            return;
        }

        Busy = true;
        _flowCts = new CancellationTokenSource();
        Issues.Clear();
        HasIssues = false;
        Progress = 5;
        try
        {
            // 1. Trust / connectivity
            Status = "Verbinding controleren…";
            var state = await DeviceService.GetConnectionStateAsync(udid);
            if (state == DeviceService.ConnectionState.NotTrusted)
            {
                Status = "Toestel niet vertrouwd: ontgrendel en tik 'Vertrouwen'.";
                return;
            }
            if (state != DeviceService.ConnectionState.Connected)
            {
                Status = "Toestel niet bereikbaar — andere kabel/poort proberen.";
                return;
            }
            Progress = 20;

            // 2. Activation bypass (optional)
            if (AutoActivate && state == DeviceService.ConnectionState.NotActivated ||
                AutoActivate && await NeedsActivationAsync(udid))
            {
                Status = "Toestel activeren (bypass)…";
                string result = await ActivationService.SkipActivationAsync(udid);
                Status = result;
            }
            Progress = 40;

            // 3. Read device data
            Status = "Toesteldata uitlezen…";
            DeviceData = await DeviceService.GetDeviceDataAsync(udid);
            Progress = 60;

            // 4. Battery checker
            if (Enable85PercentChecker && int.TryParse(DeviceData.BatteryHealth, out int health) && health < 85)
                DeviceData.BatteryHealth = "100%-X";

            // 5. Diagnostics (panic logs & sensors) — advisory, not blocking
            if (RunDiagnostics)
            {
                Status = "Diagnostiek draaien (panic-logs)…";
                foreach (var issue in await DiagnosticService.DiagnoseAsync(udid))
                    Issues.Add(issue);
                HasIssues = Issues.Count > 0;
            }
            Progress = 75;

            // 6. Quality + payment: defaults from settings, else popup
            if (DefaultQuality is { Length: > 0 })
                await ContinueAfterQualityAsync(DefaultQuality);
            else
            {
                IsQualityPopupVisible = true;
                Status = "Kies de kwaliteit…";
            }
        }
        catch (Exception ex)
        {
            Status = $"Fout: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<bool> NeedsActivationAsync(string udid) =>
        (await DeviceService.GetKeyAsync(udid, "ActivationState")).Contains("Unactivated");

    private async Task ContinueAfterQualityAsync(string quality)
    {
        DeviceData.Quality = quality;
        Progress = 85;
        if (DefaultPaymentMethod is { Length: > 0 })
            await ContinueAfterPaymentAsync(DefaultPaymentMethod);
        else
        {
            IsQualityPopupVisible = false;
            IsPaymentPopupVisible = true;
        }
    }

    private Task ContinueAfterPaymentAsync(string method)
    {
        DeviceData.PayMethod = method;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
        Progress = 95;

        if (OpenEditorBeforePrint)
        {
            DataEditorRequested?.Invoke(DeviceData);
            return Task.CompletedTask;
        }

        FinishLabel();
        return Task.CompletedTask;
    }

    /// <summary>Generate + open the label; single click path for the user.</summary>
    public void FinishLabel()
    {
        try
        {
            Status = "Label genereren…";
            string path = LabelService.GenerateLabel(DeviceData);
            Status = LabelService.OpenLabelFile(path);
            Progress = 100;
        }
        catch (Exception ex)
        {
            Status = $"Label mislukt: {ex.Message}";
        }
    }

    private void OpenLabel()
    {
        if (File.Exists(LabelService.OutputPath))
            Status = LabelService.OpenLabelFile();
        else
            Status = "Nog geen label gegenereerd.";
    }
}
