using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Threading;
using System;
using static DeviceService.DeviceService;


namespace AutoDymoLabel.UI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        // Private backing fields
        private DeviceData _deviceData = new();
        private Dictionary<string, string> _devices = new();
        private string _selectedDeviceKey = string.Empty;
 //       private string _selectedDeviceValue;
        private bool _autoActivate;
        private int _progress;
        private string _data = string.Empty;
        private bool _isEditorEnabled = true;
        private string _updateNotification = "Welcome to AutoDymoLabel!";
        private bool _isUpdateNotifierVisible = true;
        private bool _enable85PercentChecker;
        private bool _enableDataEditor;
        private string _labelOpeningOption = "Direct";
        private bool _useDymoAPI;

        // Public properties
        public DeviceData DeviceData
        {
            get => _deviceData;
            set => this.RaiseAndSetIfChanged(ref _deviceData, value);
        }

        public Dictionary<string, string> Devices
        {
            get => _devices;
            set => this.RaiseAndSetIfChanged(ref _devices, value);
        }

        public string SelectedDeviceKey
        {
            get => _selectedDeviceKey;
            set => this.RaiseAndSetIfChanged(ref _selectedDeviceKey, value);
        }

/*
        public string SelectedDeviceValue
        {
            get => Devices.TryGetValue(SelectedDeviceKey, out var value) ? value : string.Empty;
            set {}
        }
*/
        public bool AutoActivate
        {
            get => _autoActivate;
            set => this.RaiseAndSetIfChanged(ref _autoActivate, value);
        }

        public int Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        public string Data
        {
            get => _data;
            set => this.RaiseAndSetIfChanged(ref _data, value);
        }

        public bool IsEditorEnabled
        {
            get => _isEditorEnabled;
            set => this.RaiseAndSetIfChanged(ref _isEditorEnabled, value);
        }

        public string UpdateNotification
        {
            get => _updateNotification;
            set => this.RaiseAndSetIfChanged(ref _updateNotification, value);
        }

        public bool IsUpdateNotifierVisible
        {
            get => _isUpdateNotifierVisible;
            set => this.RaiseAndSetIfChanged(ref _isUpdateNotifierVisible, value);
        }

        public bool Enable85PercentChecker
        {
            get => _enable85PercentChecker;
            set => this.RaiseAndSetIfChanged(ref _enable85PercentChecker, value);
        }

        public bool EnableDataEditor
        {
            get => _enableDataEditor;
            set => this.RaiseAndSetIfChanged(ref _enableDataEditor, value);
        }

        public string LabelOpeningOption
        {
            get => _labelOpeningOption;
            set => this.RaiseAndSetIfChanged(ref _labelOpeningOption, value);
        }

        public bool UseDymoAPI
        {
            get => _useDymoAPI;
            set => this.RaiseAndSetIfChanged(ref _useDymoAPI, value);
        }

        // Commands
        public ReactiveCommand<Unit, Unit> StartCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<string, Unit> SetQualityCommand { get; }

        public MainWindowViewModel()
        {
            var mainThreadScheduler = RxApp.MainThreadScheduler;

            RefreshDevicesCommand = ReactiveCommand.CreateFromTask(
                RefreshDeviceList,
                outputScheduler: mainThreadScheduler
            );

            SetQualityCommand = ReactiveCommand.Create<string>(
                SetQuality,
                outputScheduler: mainThreadScheduler
            );

            StartCommand = ReactiveCommand.Create(
                StartProcess,
                outputScheduler: mainThreadScheduler
            );

            RefreshDevicesCommand.ThrownExceptions
                .ObserveOn(mainThreadScheduler)
                .Subscribe(ex =>
                {
                    UpdateNotification = $"Error: {ex.Message}";
                });

            RefreshDevicesCommand.Execute().Subscribe();
        }

        private async Task RefreshDeviceList()
        {
            //Console.WriteLine("RefreshDeviceList started...");
            try
            {
                var devices = await Task.Run(() => GetConnectedDevices());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Initialize Devices; if 'devices' is null, create a new empty dictionary
                    Devices = devices ?? new Dictionary<string, string>();

                    // Check the number of devices
                    int deviceCount = Devices.Count;

                    if (deviceCount == 1)
                    {
                        // Automatically select the only available device
                        SelectedDeviceKey = Devices.Keys.First();
                        //System.Console.WriteLine($"Selected Device: {SelectedDeviceValue}");
                        UpdateNotification = "One device found and selected automatically.";
                    }
                    else
                    {
                        // Clear selection if no devices or multiple devices are found
                        //SelectedDeviceValue = "Select a device";
                        UpdateNotification = deviceCount > 0
                            ? $"Found {deviceCount} connected devices."
                            : "No devices found.";
                    }

                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateNotification = $"Error refreshing devices: {ex.Message}";
                });
                //Console.WriteLine($"Error refreshing devices: {ex}");
            }
            //Console.WriteLine("RefreshDeviceList completed.");
        }

        private void SetQuality(string quality)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DeviceData.Quality = quality;
                UpdateNotification = $"Device Quality set to: {quality}";
            });
        }

        private void UpdateNotificationSafe(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateNotification = message;
                IsUpdateNotifierVisible = true;
            });
        }
        private void StartProcess()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                UpdateNotificationSafe("Process started...");
                CheckDevice();
                HandleActivation();
                DeviceData DeviceData = GetDeviceData(SelectedDeviceKey);

                System.Console.WriteLine(DeviceData.BatteryHealth);
                System.Console.WriteLine(DeviceData.Identifier);
                System.Console.WriteLine(DeviceData.Model);
                System.Console.WriteLine(DeviceData.Quality);
                System.Console.WriteLine(DeviceData.PayMethod);
                System.Console.WriteLine(DeviceData.Storage);
                System.Console.WriteLine(DeviceData.Color);
                // TODO: get data, make label, print label
            });
        }

        private void CheckDevice()
        {
            if (!IsDeviceConnected())
            {
                UpdateNotificationSafe("No device connected.");
                return;
            }
            if (!IsDeviceTrusted())
            {
                UpdateNotificationSafe("Device not trusted.");
                return;
            }
        }

        private void HandleActivation()
        {
            if (AutoActivate && !IsActivated())
            {
                UpdateNotificationSafe("Device not activated, activating ..."); // TODO: Fix Not showing up
                System.Console.WriteLine($"Key = {SelectedDeviceKey}");
                string activationResult = Activation.ActivationService.SkipActivation(SelectedDeviceKey);
                UpdateNotificationSafe(activationResult);
                return;
            }
        }   

    }
}
