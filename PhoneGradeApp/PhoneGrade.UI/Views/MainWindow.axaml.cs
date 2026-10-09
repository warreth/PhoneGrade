using System;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ShopProfiles;
using PhoneGrade.UI.ViewModels;

namespace PhoneGrade.UI.Views;

public partial class MainWindow : Window, IDisposable
{
    /// <summary>
    /// The only file type a profile can be, offered to the pickers. The Apple
    /// identifier is named because the macOS picker filters by type rather than
    /// by extension, and a file that cannot be picked is a file that cannot be
    /// imported on one of the three systems.
    /// </summary>
    private static readonly FilePickerFileType JsonFileType = new("JSON")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        // Data editor requests arrive from the view model; keep a single editor instance.
        vm.DataEditorRequested += data =>
        {
            var form = new DataEditorViewModel(data);
            var editor = new DataEditorWindow { DataContext = form };

            // A correction made on the label form is followed by the export panel.
            // The form edits the same DeviceData the panel reads, so opening the
            // panel here is enough: the operator lands on a preview showing what
            // they just typed rather than having to ask for the label again.
            form.SavedInteraction.RegisterHandler(interaction =>
            {
                vm.ExportViewModel?.Open();
                interaction.SetOutput(Unit.Default);
            });

            editor.Show();
        };

        // The shop profile pickers live here because a file dialog needs a
        // window. The view model keeps the file work, the preview and the apply,
        // which is also what the tests drive without a picker.
        vm.ShopProfileExportRequested += async () =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = LocalizationManager.GetString("Settings_ShopProfileExport"),
                    SuggestedFileName = ShopProfile.SuggestedFileName,
                    DefaultExtension = "json",
                    FileTypeChoices = [JsonFileType],
                });

                if (file?.TryGetLocalPath() is { Length: > 0 } path)
                    vm.ExportShopProfileTo(path);
            }
            catch (Exception ex)
            {
                vm.ReportShopProfilePickerFailed(ex.Message);
            }
        };

        vm.ShopProfileImportRequested += async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = LocalizationManager.GetString("Settings_ShopProfileImport"),
                    AllowMultiple = false,
                    FileTypeFilter = [JsonFileType],
                });

                if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
                    vm.PreviewShopProfileImport(path);
            }
            catch (Exception ex)
            {
                vm.ReportShopProfilePickerFailed(ex.Message);
            }
        };
    }

    /// <summary>
    /// Takes the picked device from the dropdown. The binding only ever runs from
    /// the view model into the control, because every refresh rebuilds the item list
    /// and the dropdown answers that by handing over null, and null does not fit in
    /// the struct the view model stores. A write that does not fit is not dropped
    /// quietly: Avalonia records it on the control and prints it underneath, which is
    /// how the text of the conversion failure ended up permanently below the list of
    /// detected devices. Nothing here reacts to an empty choice, and the refresh is
    /// what puts the selection back once it knows which devices are there.
    /// </summary>
    private void DeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && e.AddedItems.Count > 0
            && e.AddedItems[0] is KeyValuePair<string, string> device)
        {
            viewModel.SelectedDevice = device;
        }
    }

    /// <summary>
    /// Releases the view model behind the window, which closes the public address
    /// its tunnel opened. Closing the window already does this through the Closed
    /// event in App; this is for a window that is only built, because one of those
    /// is never closed and would otherwise leave the connector running.
    /// </summary>
    void IDisposable.Dispose() => (DataContext as MainWindowViewModel)?.Shutdown();
}
