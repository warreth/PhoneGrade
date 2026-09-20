using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.ViewModels;
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
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
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
