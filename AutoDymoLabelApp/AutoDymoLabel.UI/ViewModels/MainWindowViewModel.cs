using System.Collections.ObjectModel;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using static DeviceService.DeviceService;
using System;
using System.Reactive.Disposables;

namespace AutoDymoLabel.UI.ViewModels;
public class MainWindowViewModel : ReactiveObject
{
    private DeviceData _deviceData = new DeviceData();
    public DeviceData DeviceData
    {
        get => _deviceData;
        set => this.RaiseAndSetIfChanged(ref _deviceData, value);
    }

    private Dictionary<string, string> _devices = GetConnectedDevices();
    public Dictionary<string, string> Devices
    {
        get => _devices;
        set => this.RaiseAndSetIfChanged(ref _devices, value);
    }

    private string _selectedDevice = string.Empty;
    public string SelectedDevice
    {
        get => _selectedDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedDevice, value);
    }

    private bool _autoActivate;
    public bool AutoActivate
    {
        get => _autoActivate;
        set => this.RaiseAndSetIfChanged(ref _autoActivate, value);
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string _data = string.Empty;
    public string Data
    {
        get => _data;
        set => this.RaiseAndSetIfChanged(ref _data, value);
    }

    private bool _isEditorEnabled = true;
    public bool IsEditorEnabled
    {
        get => _isEditorEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEditorEnabled, value);
    }

    private string _updateNotification = "Welcome to AutoDymoLabel!";
    public string UpdateNotification
    {
        get => _updateNotification;
        set => this.RaiseAndSetIfChanged(ref _updateNotification, value);
    }

    private bool _isUpdateNotifierVisible = true;
    public bool IsUpdateNotifierVisible
    {
        get => _isUpdateNotifierVisible;
        set => this.RaiseAndSetIfChanged(ref _isUpdateNotifierVisible, value);
    }

    private bool _enable85PercentChecker;
    public bool Enable85PercentChecker
    {
        get => _enable85PercentChecker;
        set => this.RaiseAndSetIfChanged(ref _enable85PercentChecker, value);
    }

    private bool _enableDataEditor;
    public bool EnableDataEditor
    {
        get => _enableDataEditor;
        set => this.RaiseAndSetIfChanged(ref _enableDataEditor, value);
    }

    private string _labelOpeningOption = "Direct";
    public string LabelOpeningOption
    {
        get => _labelOpeningOption;
        set => this.RaiseAndSetIfChanged(ref _labelOpeningOption, value);
    }

    private bool _useDymoAPI;
    public bool UseDymoAPI
    {
        get => _useDymoAPI;
        set => this.RaiseAndSetIfChanged(ref _useDymoAPI, value);
    }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
    public ReactiveCommand<string, Unit> SetQualityCommand { get; }

    public MainWindowViewModel()
    {
        StartCommand = ReactiveCommand.Create(
            StartProcess,
            outputScheduler: RxApp.MainThreadScheduler);

        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(
            RefreshDeviceList,
            outputScheduler: RxApp.MainThreadScheduler);

        SetQualityCommand = ReactiveCommand.Create<string>(
            SetQuality,
            outputScheduler: RxApp.MainThreadScheduler);

        // Handle command exceptions
        RefreshDevicesCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ex => UpdateNotification = $"Error: {ex.Message}");

        // Execute initial refresh
        RefreshDevicesCommand.Execute().Subscribe();
    }

    private async Task RefreshDeviceList()
    {
        Console.WriteLine("RefreshDeviceList started...");
        try
        {
            var devices = await Task.Run(() => GetConnectedDevices());
            
            RxApp.MainThreadScheduler.Schedule(TimeSpan.Zero, (scheduler, time) =>
            {
                Devices = devices ?? new Dictionary<string, string>();
                SelectedDevice = Devices.FirstOrDefault().Key ?? string.Empty;
                UpdateNotification = Devices.Any()
                    ? $"Found {Devices.Count} connected devices."
                    : "No devices found.";
                return Disposable.Empty;
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(TimeSpan.Zero, (scheduler, time) =>
            {
                UpdateNotification = $"Error refreshing devices: {ex.Message}";
                return Disposable.Empty;
            });
            Console.WriteLine($"Error refreshing devices: {ex}");
        }
        Console.WriteLine("RefreshDeviceList completed.");
    }

    private void SetQuality(string quality)
    {
        RxApp.MainThreadScheduler.Schedule(TimeSpan.Zero, (scheduler, time) =>
        {
            DeviceData.Quality = quality;
            UpdateNotification = $"Device Quality set to: {quality}";
            return Disposable.Empty;
        });
    }

    private void StartProcess()
    {
        RxApp.MainThreadScheduler.Schedule(TimeSpan.Zero, (scheduler, time) =>
        {
            UpdateNotification = "Process started...";
            return Disposable.Empty;
        });
    }
}