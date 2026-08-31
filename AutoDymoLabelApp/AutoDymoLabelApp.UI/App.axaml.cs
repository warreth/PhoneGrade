using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AutoDymoLabelApp.UI.Models;
using AutoDymoLabelApp.UI.ViewModels;
using AutoDymoLabelApp.UI.Views;

namespace AutoDymoLabel.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(AppSettings.Load().Theme); // read settings before first frame
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
