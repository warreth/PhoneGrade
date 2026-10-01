using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.Views;

namespace PhoneGrade.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var settings = AppSettings.Load();
        
        // Initialize localization before first frame
        Services.LocalizationManager.Initialize(settings.Language);
        
        ApplyTheme(settings.Theme);

        // Boot logging to ensure logs are never empty
        PhoneGrade.Core.SystemEventLogger.Info(PhoneGrade.Core.LogSource.Desktop, $"PhoneGrade initialized on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        PhoneGrade.Core.SystemEventLogger.Info(PhoneGrade.Core.LogSource.Desktop, $"App directory: {AppContext.BaseDirectory}");
        PhoneGrade.Core.SystemEventLogger.Info(PhoneGrade.Core.LogSource.Desktop, $"Tools directory: {PhoneGrade.Core.ToolRunner.ToolsDir}");
        PhoneGrade.Core.SystemEventLogger.Info(PhoneGrade.Core.LogSource.Desktop, $"Log directory: {PhoneGrade.Core.SystemEventLogger.LogDir}");
        PhoneGrade.Core.SystemEventLogger.Info(PhoneGrade.Core.LogSource.Desktop, $"Language: {settings.Language}");

        // A connector left by a run that was killed rather than closed cannot tidy
        // up after itself, so the leftovers go before anything new is started.
        PhoneGrade.Core.ConnectorLedger.ReapOrphans();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            // MainWindow builds its own view model and wires DataEditorRequested
            // in its constructor. Assigning a second one here would orphan that
            // instance, so the editor button would never fire, and would start a
            // second watcher and web server alongside the first.
            var window = new MainWindow();
            window.Closed += (_, _) => (window.DataContext as ViewModels.MainWindowViewModel)?.Shutdown();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Applies "Dark", "Light" or "System" to the whole app instantly.</summary>
    public static void ApplyTheme(string theme)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "System" or null => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        foreach (var plugin in BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray())
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
