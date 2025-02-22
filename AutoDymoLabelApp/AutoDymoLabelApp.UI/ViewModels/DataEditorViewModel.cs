using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using System.Reactive.Threading.Tasks;

namespace AutoDymoLabelApp.UI.ViewModels
{
    public class DataEditorViewModel : ReactiveObject
    {
        private DeviceData _deviceData;
        public DeviceData DeviceData
        {
            get => _deviceData;
            set => this.RaiseAndSetIfChanged(ref _deviceData, value);
        }

        public ReactiveCommand<Unit, Unit> SaveAndOpenLabelCommand { get; }
        public Interaction<Unit, Unit> CloseWindowInteraction { get; } = new Interaction<Unit, Unit>();

        public DataEditorViewModel(DeviceData deviceData)
        {
            _deviceData = deviceData;
            SaveAndOpenLabelCommand = ReactiveCommand.CreateFromTask(HandleSaveAndOpenLabelAsync);
        }

        private async Task HandleSaveAndOpenLabelAsync()
        {
            LabelService.GenerateLabel(DeviceData);
            string result = await OpenLabel.OpenLabelFileAsync();

            MainWindowViewModel.Instance.Progress = 0;
            MainWindowViewModel.Instance.UpdateNotificationSafe(result);

            // Signal the view to close; convert IObservable<Unit> to Task using ToTask()
            await CloseWindowInteraction.Handle(Unit.Default).ToTask();
        }
    }

    public interface ICloseable
    {
        void Close();
    }
}