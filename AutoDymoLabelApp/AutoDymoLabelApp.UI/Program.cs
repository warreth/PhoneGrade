using Avalonia;
using Avalonia.ReactiveUI;

namespace AutoDymoLabel.UI;

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
        _ = AutoUpdater.CheckAndApplyAsync(); // fire and forget: never block startup
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
