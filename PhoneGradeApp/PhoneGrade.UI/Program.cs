using Avalonia;
using Avalonia.ReactiveUI;
using PhoneGrade.UI.Models;

namespace PhoneGrade.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't
    // initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack bootstrap: handles installer hooks and pending updates.
        // Must run before anything else, or the installed app won't respond to
        // install/update arguments.
        Velopack.VelopackApp.Build().Run();
        // The settings are read here rather than handed in, because this runs before
        // Avalonia exists and there is no view model to ask yet. Load() returns the same
        // instance the rest of the app uses, so the updater and the settings screen are
        // reading the same answer about whether this shop wants betas.
        _ = AutoUpdater.CheckAndApplyAsync(AppSettings.Load()); // fire and forget: never block startup
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
