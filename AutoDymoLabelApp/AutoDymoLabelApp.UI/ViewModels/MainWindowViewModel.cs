using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Threading;
using AutoDymoLabelApp.UI.Models;
using AutoDymoLabelApp.UI.Views;
using System;
using static DeviceService.DeviceService;


namespace AutoDymoLabelApp.UI.ViewModels
{
    public class MainWindowViewModel : ReactiveObject
    {
        // Singleton instance
        public static MainWindowViewModel Instance { get; private set; } = null!;

        #region Properties
        private readonly AppSettings _settings;

        // Private backing fields
        private bool _autoActivate;
        private string _selectedDeviceKey = string.Empty;
        private bool _enable85PercentChecker;
        private bool _useDymoAPI;
        private DeviceData _deviceData = new();
        private Dictionary<string, string> _devices = [];
        private int _progress;
        private string _data = string.Empty;
        private bool _isEditorEnabled = true;
        private bool _enableDataEditor = true;

        private string _updateNotification = "Welcome to AutoDymoLabel!";
        private bool _isUpdateNotifierVisible = true;
        private bool _isQualityPopupVisible;
        private bool _isPaymentPopupVisible;
        private bool _isFileDialogVisible;
        private bool _isEditDataPopupVisible;
        public bool IsEditDataPopupVisible
        {
            get => _isEditDataPopupVisible;
            set => this.RaiseAndSetIfChanged(ref _isEditDataPopupVisible, value);
        }

        public bool EnableDataEditor
        {
            get => _enableDataEditor;
            set
            {
                _settings.EnableDataEditor = value;
                _settings.Save();
                this.RaiseAndSetIfChanged(ref _enableDataEditor, value);
            }
        }
        public bool AutoActivate
        {
            get => _autoActivate;
            set
            {
                _settings.AutoActivate = value;
                _settings.Save();
                this.RaiseAndSetIfChanged(ref _autoActivate, value);
            }
        }

        public string SelectedDeviceKey
        {
            get => _selectedDeviceKey;
            set
            {
                _settings.SelectedDeviceKey = value;
                _settings.Save();
                this.RaiseAndSetIfChanged(ref _selectedDeviceKey, value);
            }
        }

        public bool Enable85PercentChecker
        {
            get => _enable85PercentChecker;
            set
            {
                _settings.Enable85PercentChecker = value;
                _settings.Save();
                this.RaiseAndSetIfChanged(ref _enable85PercentChecker, value);
            }
        }

        public bool UseDymoAPI
        {
            get => _useDymoAPI;
            set
            {
                _settings.UseDymoAPI = value;
                _settings.Save();
                this.RaiseAndSetIfChanged(ref _useDymoAPI, value);
            }
        }

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

        public bool IsQualityPopupVisible
        {
            get => _isQualityPopupVisible;
            set => this.RaiseAndSetIfChanged(ref _isQualityPopupVisible, value);
        }

        public bool IsPaymentPopupVisible
        {
            get => _isPaymentPopupVisible;
            set => this.RaiseAndSetIfChanged(ref _isPaymentPopupVisible, value);
        }

        public bool IsFileDialogVisible
        {
            get => _isFileDialogVisible;
            set => this.RaiseAndSetIfChanged(ref _isFileDialogVisible, value);
        }

        public ReactiveCommand<Unit, Unit> StartCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<string, Unit> SetQualityCommand { get; }
        public ReactiveCommand<string, Unit> SetPaymentMethodCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowLabelCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenDataEditorCommand { get; }

        #endregion
        #region Methods
        public MainWindowViewModel()
        {
            Instance = this;

            _settings = AppSettings.Load();

            // Initialize backing fields from settings
            _autoActivate = _settings.AutoActivate;
            _selectedDeviceKey = _settings.SelectedDeviceKey;
            _enable85PercentChecker = _settings.Enable85PercentChecker;
            _useDymoAPI = _settings.UseDymoAPI;
            _enableDataEditor = _settings.EnableDataEditor;


            var mainThreadScheduler = RxApp.MainThreadScheduler;

            RefreshDevicesCommand = ReactiveCommand.CreateFromTask(
                RefreshDeviceList,
                outputScheduler: mainThreadScheduler
            );

            SetQualityCommand = ReactiveCommand.Create<string>(
                SetQuality,
                outputScheduler: mainThreadScheduler
            );

            StartCommand = ReactiveCommand.CreateFromTask(
                StartProcessAsync,
                outputScheduler: mainThreadScheduler
            );

            RefreshDevicesCommand.ThrownExceptions
                .ObserveOn(mainThreadScheduler)
                .Subscribe(ex =>
                {
                    UpdateNotification = $"Error: {ex.Message}";
                });

            RefreshDevicesCommand.Execute().Subscribe();

            SetPaymentMethodCommand = ReactiveCommand.Create<string>(
                SetPaymentMethod,
                outputScheduler: mainThreadScheduler
            );

            ShowLabelCommand = ReactiveCommand.Create(
                HandleLabelOpening,
                outputScheduler: mainThreadScheduler
            );

            OpenDataEditorCommand = ReactiveCommand.Create(OpenDataEditor);
        }

        private async Task RefreshDeviceList()
        {
            try
            {
                var devices = await Task.Run(() => GetConnectedDevicesAsync());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Devices = devices ?? new Dictionary<string, string>();

                    int deviceCount = Devices.Count;

                    if (deviceCount == 1)
                    {
                        SelectedDeviceKey = Devices.Keys.First();
                        UpdateNotification = "One device found and selected automatically.";
                    }
                    else
                    {
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
            }
        }

        private void SetQuality(string quality)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DeviceData.Quality = quality;
                UpdateProgressSafe(85, $"Device Quality set to: {quality}");
                IsQualityPopupVisible = false;
                IsPaymentPopupVisible = true; //show the payment popup
            });
        }

        private void SetPaymentMethod(string method)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DeviceData.PayMethod = method;
                UpdateProgressSafe(90, $"Payment method set to: {method}");
                IsPaymentPopupVisible = false;

                if (EnableDataEditor)
                {
                    // Open data editor window directly
                    OpenDataEditor();
                }
                else
                {
                    // Show edit data popup
                    IsEditDataPopupVisible = true;
                }
            });
        }

        private void OpenDataEditor()
        {
            IsEditDataPopupVisible = false; // Close the popup
            var editorWindow = new DataEditorWindow
            {
                DataContext = new DataEditorViewModel(DeviceData)
            };
            editorWindow.Show();
        }
        private void UpdateProgressSafe(int progress, string? message = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Progress = progress;
                if (!string.IsNullOrEmpty(message))
                {
                    UpdateNotificationSafe(message);
                }
            });
        }

        public void UpdateNotificationSafe(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateNotification = message;
                IsUpdateNotifierVisible = true;
            });
        }

        private async Task StartProcessAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateProgressSafe(0, "Process started...");
            });

            if (!await CheckDeviceAsync())
            {
                return;
            }

            await HandleActivation();

            DeviceData = await GetDeviceDataAsync(SelectedDeviceKey);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateProgressSafe(50, "Device data retrieved...");
                IsQualityPopupVisible = true; // Show the quality popup.
            });
        }

        private async Task<bool> CheckDeviceAsync()
        {
            if (!await IsDeviceConnectedAsync())
            {
                UpdateNotificationSafe("No device connected.");
                return false;
            }
            if (!await IsDeviceTrustedAsync())
            {
                UpdateNotificationSafe("Device not trusted.");
                return false;
            }
            return true;
        }

        private async Task HandleActivation()
        {
            if (AutoActivate && !await IsActivatedAsync())
            {
                UpdateNotificationSafe("Device not activated, activating ...");
                string activationResult = await Activation.ActivationService.SkipActivationAsync(SelectedDeviceKey);
                UpdateNotificationSafe(activationResult);
                return;
            }
        }

        private async void HandleLabelOpening()
        {
            IsEditDataPopupVisible = false;
            LabelService.GenerateLabel(DeviceData);
            string result = await OpenLabel.OpenLabelFileAsync();
            UpdateNotificationSafe(result);
            Progress = 0; // Reset progress bar to 0
        }
        #endregion
    }
}