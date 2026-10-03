using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace PhoneGrade.Tests;

// ============ The logbook viewer: a long list that must stay cheap to show ============
//
// The viewer is opened from the settings drawer and holds up to the whole ring
// buffer. It used to be a ScrollViewer around an ItemsControl, which realizes
// every row it is given: a thousand entries were a thousand rows of elements,
// re-measured whenever a new entry arrived, inside a modal that stays in the
// visual tree. The tests here measure what actually reaches the visual tree,
// because that is what the fix is about - the rows that exist as elements, not
// the rows that exist as data.
//
// The log directory is pointed at a temporary folder per test: these tests emit
// hundreds of real entries through SystemEventLogger, and writing them into the
// operator's log file would both slow the run down and race with the file the
// rest of the suite asserts on.

public class LogbookViewerTests : IDisposable
{
    private readonly string _logDir = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"logbook-{Guid.NewGuid():N}");
    private readonly string? _originalLogDir = Environment.GetEnvironmentVariable("AUTODYMO_LOG_DIR");

    public LogbookViewerTests()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_LOG_DIR", _logDir);
    }

    public void Dispose()
    {
        SystemEventLogger.ClearLogs();
        Environment.SetEnvironmentVariable("AUTODYMO_LOG_DIR", _originalLogDir);
        try
        {
            if (System.IO.Directory.Exists(_logDir)) System.IO.Directory.Delete(_logDir, true);
        }
        catch
        {
            // A slower process is still flushing to it; the folder is in temp either way.
        }
    }

    /// <summary>The shape of a row's time column: HH:mm:ss.fff, one per realized row.</summary>
    private static readonly Regex RowTime = new(@"^\d{2}:\d{2}:\d{2}\.\d{3}$", RegexOptions.Compiled);

    /// <summary>
    /// Runs the dispatcher and waits for the pipeline to deliver, with a deadline.
    ///
    /// The DynamicData pipeline ends in ObserveOn(RxApp.MainThreadScheduler), so a
    /// projection can be one dispatcher hop away from the property that caused it.
    /// Waiting on the condition with the dispatcher pumped underneath it is what
    /// keeps these tests deterministic; a bare sleep would be a coin flip.
    /// </summary>
    private static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(10);
        }
        Assert.True(condition(), $"timed out waiting for: {what}");
    }

    /// <summary>Runs the dispatcher and the render timer, so layout has happened.</summary>
    private static void Render(Visual window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    /// <summary>The log list of the shown window, or null when the view does not have one.</summary>
    private static ListBox? LogList(Visual window) =>
        Descendants(window).OfType<ListBox>().FirstOrDefault(list => list.Classes.Contains("logList"));

    /// <summary>Everything under the root, in visual tree order.</summary>
    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root is null) yield break;
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }

    /// <summary>
    /// The claim the fix rests on: with over a thousand entries loaded, only the
    /// rows that fit on screen exist as elements.
    ///
    /// What makes this fail against the old markup is the thing being counted.
    /// The rows are located by their time TextBlock, which the row template has
    /// always had, so the count is meaningful for any list control: the ListBox
    /// realizes a viewportful of containers (16 time columns measured for the
    /// 580px modal), while the old ScrollViewer plus ItemsControl measured every
    /// item and put all 1203 time columns in the tree - the assertion below would
    /// see 1203 instead of 16 and fail. The old markup also has no logList
    /// ListBox to find, so it cannot get past that assert either way.
    /// </summary>
    [AvaloniaFact]
    public void TheLogbookRealizesOnlyTheRowsThatFitOnScreen()
    {
        const int emitted = 1200;
        SystemEventLogger.ClearLogs();
        for (int i = 0; i < emitted; i++)
        {
            SystemEventLogger.Log(LogLevel.Info, LogSource.Desktop, $"logbook filler entry {i:D4}");
        }

        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1050;
        window.Height = 740;

        vm.OpenLogsModalCommand.Execute().Subscribe();
        Assert.True(vm.IsLogsModalOpen, "the modal has to be open for its rows to be measured");

        PumpUntil(() => vm.LogsViewModel.Logs.Count >= emitted, "the log list replays the ring buffer");
        Render(window);

        var list = LogList(window);
        Assert.True(list is not null, "the log modal has no ListBox bound to the log entries");
        Assert.True(list!.ItemsPanelRoot is VirtualizingStackPanel,
            $"the log list is backed by {list.ItemsPanelRoot?.GetType().Name}, not the virtualizing panel");

        int total = vm.LogsViewModel.Logs.Count;
        int realized = Descendants(list).OfType<TextBlock>()
            .Count(block => RowTime.IsMatch(block.Text ?? string.Empty));
        int containers = Descendants(list).OfType<ListBoxItem>().Count();

        Assert.True(realized > 0, "no row reached the visual tree, so a count of them proves nothing");
        Assert.True(containers > 0, "the log list generated no item containers at all");
        Assert.True(realized < 150,
            $"{realized} realized time columns and {containers} item containers for {total} log rows in the visual tree; " +
            "only the rows that fit on screen should");
    }

    /// <summary>
    /// The four strings the row template binds are built once from the event.
    /// The reference checks are the point: a property that formats on every get
    /// hands back a fresh string each time, a stored one hands back the same one.
    /// </summary>
    [Fact]
    public void LogEventViewModelPrecomputesItsDisplayStringsFromTheLogEvent()
    {
        var stamp = new DateTime(2026, 3, 4, 15, 16, 17, 89, DateTimeKind.Utc);
        var logEvent = new LogEvent
        {
            Timestamp = stamp,
            Level = LogLevel.Warning,
            Source = LogSource.UsbDetector,
            Message = "probe timed out",
        };

        var vm = new LogEventViewModel(logEvent);

        Assert.Equal(stamp.ToLocalTime().ToString("HH:mm:ss.fff"), vm.FormattedTime);
        Assert.Equal("Warning", vm.LevelText);
        Assert.Equal("UsbDetector", vm.SourceText);
        Assert.Equal("#F5A623", vm.LevelColor);

        // The enums stay available for the copy and export commands, which print
        // the same words they always did - through the stored strings now.
        Assert.Equal(LogLevel.Warning, vm.Level);
        Assert.Equal(LogSource.UsbDetector, vm.Source);
        Assert.Equal(vm.Level.ToString(), vm.LevelText);
        Assert.Equal(vm.Source.ToString(), vm.SourceText);

        Assert.Same(vm.FormattedTime, vm.FormattedTime);
        Assert.Same(vm.LevelColor, vm.LevelColor);

        // Every level the list can show has its colour, spelled the way a brush
        // binding accepts it.
        Assert.Equal("#DC143C", ColorOf(LogLevel.Critical));
        Assert.Equal("#F0564A", ColorOf(LogLevel.Error));
        Assert.Equal("#F5A623", ColorOf(LogLevel.Warning));
        Assert.Equal("#9B9BA6", ColorOf(LogLevel.Debug));
        Assert.Equal("#4F8CFF", ColorOf(LogLevel.Info));

        static string ColorOf(LogLevel level) =>
            new LogEventViewModel(new LogEvent { Level = level }).LevelColor;
    }

    /// <summary>
    /// The filter runs over the whole list on every toggle and every keystroke,
    /// so it is exercised through the real view model and its real projection,
    /// not through the predicate in isolation.
    /// </summary>
    [AvaloniaFact]
    public void TheLogbookFiltersFollowTheLevelTogglesAndTheSearchQuery()
    {
        SystemEventLogger.ClearLogs();
        SystemEventLogger.Log(LogLevel.Debug, LogSource.Desktop, "logbook debug alpha entry");
        SystemEventLogger.Log(LogLevel.Info, LogSource.WebSocket, "logbook info bravo entry");
        SystemEventLogger.Log(LogLevel.Warning, LogSource.Diagnostic, "logbook warning charlie entry");
        SystemEventLogger.Log(LogLevel.Error, LogSource.System, "logbook error delta entry");

        using var vm = new UnifiedLogsViewModel();
        PumpUntil(() => vm.Logs.Any(log => log.Message == "logbook warning charlie entry"),
            "the log list replays the ring buffer");

        Assert.Contains(vm.Logs, log => log.Message == "logbook debug alpha entry");
        Assert.Contains(vm.Logs, log => log.Message == "logbook error delta entry");

        vm.ShowDebug = false;
        PumpUntil(() => vm.Logs.All(log => log.Message != "logbook debug alpha entry"),
            "the debug toggle reaches the projection");
        Assert.DoesNotContain(vm.Logs, log => log.Level == LogLevel.Debug);
        Assert.Contains(vm.Logs, log => log.Message == "logbook info bravo entry");

        vm.ShowWarning = false;
        PumpUntil(() => vm.Logs.All(log => log.Message != "logbook warning charlie entry"),
            "the warning toggle reaches the projection");
        Assert.Contains(vm.Logs, log => log.Message == "logbook error delta entry");

        vm.ShowDebug = true;
        vm.ShowWarning = true;
        PumpUntil(() => vm.Logs.Any(log => log.Message == "logbook debug alpha entry"),
            "the toggles turn back on");

        // A search on the message word.
        vm.SearchQuery = "charlie";
        PumpUntil(() => vm.Logs.All(log => log.Message.Contains("charlie", StringComparison.OrdinalIgnoreCase)),
            "the search query reaches the projection");
        Assert.Contains(vm.Logs, log => log.Message == "logbook warning charlie entry");
        Assert.DoesNotContain(vm.Logs, log => log.Message == "logbook info bravo entry");

        // A search on the source word, which used to be an Enum.ToString per
        // entry per evaluation of the predicate.
        vm.SearchQuery = "WebSocket";
        PumpUntil(() => vm.Logs.Any(log => log.Source == LogSource.WebSocket),
            "the source word matches the search query");
        Assert.Contains(vm.Logs, log => log.Message == "logbook info bravo entry");
        Assert.DoesNotContain(vm.Logs, log => log.Message == "logbook warning charlie entry");
        Assert.All(vm.Logs, log => Assert.Contains("WebSocket", log.SourceText, StringComparison.OrdinalIgnoreCase));

        vm.SearchQuery = "";
        PumpUntil(() => vm.Logs.Any(log => log.Message == "logbook warning charlie entry"),
            "clearing the search query shows everything again");
    }

    /// <summary>
    /// The view model subscribes to a static event in its constructor. Disposal
    /// is what unhooks it, and after it the logger must not be able to push
    /// another row into this instance.
    /// </summary>
    [AvaloniaFact]
    public void TheLogbookStopsReceivingEventsAfterDispose()
    {
        SystemEventLogger.ClearLogs();
        SystemEventLogger.Log(LogLevel.Info, LogSource.Desktop, "logbook seed entry");

        var vm = new UnifiedLogsViewModel();
        PumpUntil(() => vm.Logs.Any(log => log.Message == "logbook seed entry"),
            "the log list replays the ring buffer");

        vm.Dispose();
        // Anything the logger posted before the unsubscribe lands here first, so
        // the count below is the count at the moment the instance let go.
        Dispatcher.UIThread.RunJobs();
        int countAtDispose = vm.Logs.Count;

        SystemEventLogger.Log(LogLevel.Info, LogSource.Desktop, "logbook entry after dispose");
        SystemEventLogger.Log(LogLevel.Warning, LogSource.Desktop, "logbook second entry after dispose");

        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (vm.Logs.Count != countAtDispose) break;
            Thread.Sleep(10);
        }

        Assert.Equal(countAtDispose, vm.Logs.Count);
        Assert.DoesNotContain(vm.Logs, log => log.Message.Contains("after dispose", StringComparison.Ordinal));
    }

    /// <summary>
    /// TextBlock in Avalonia 11.2.3 carries MaxLines, so one logged payload
    /// cannot stretch its row over the whole modal: the row stops after six
    /// lines of message.
    /// </summary>
    [AvaloniaFact]
    public void TheLogbookMessageColumnIsCappedAtSixLines()
    {
        SystemEventLogger.ClearLogs();
        string payload = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"payload line {i}"));
        SystemEventLogger.Log(LogLevel.Warning, LogSource.Desktop, payload);

        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1050;
        window.Height = 740;

        vm.OpenLogsModalCommand.Execute().Subscribe();
        PumpUntil(() => vm.LogsViewModel.Logs.Any(log => log.Message == payload),
            "the long entry reaches the log list");
        Render(window);

        var message = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(block => block.Text == payload);
        Assert.True(message is not null, "the row with the long message did not reach the visual tree");
        Assert.Equal(6, message!.MaxLines);
        Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
    }

    /// <summary>
    /// The level badge binds a hex string to a brush background through the
    /// compiled row template. A binding that silently stopped converting would
    /// leave the badges colourless, so the rendered brushes are checked against
    /// the colour the event's level maps to.
    /// </summary>
    [AvaloniaFact]
    public void TheLevelBadgePaintsFromThePrecomputedHexColour()
    {
        SystemEventLogger.ClearLogs();
        SystemEventLogger.Log(LogLevel.Warning, LogSource.Diagnostic, "logbook badge probe entry");

        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        window.Width = 1050;
        window.Height = 740;

        vm.OpenLogsModalCommand.Execute().Subscribe();
        PumpUntil(() => vm.LogsViewModel.Logs.Any(log => log.Message == "logbook badge probe entry"),
            "the warning entry reaches the log list");
        Render(window);

        var list = LogList(window);
        Assert.NotNull(list);

        var badges = Descendants(list).OfType<Border>()
            .Where(border => border.Child is TextBlock text
                             && (text.Text == "Warning" || text.Text == "Info"
                                 || text.Text == "Error" || text.Text == "Debug"
                                 || text.Text == "Critical"))
            .ToList();
        Assert.True(badges.Count > 0, "no level badge reached the visual tree");

        foreach (var badge in badges)
        {
            Assert.True(badge.Background is ISolidColorBrush,
                $"the badge behind '{((TextBlock)badge.Child!).Text}' did not become a brush");
        }

        var warning = badges.First(badge => ((TextBlock)badge.Child!).Text == "Warning");
        Assert.Equal(Color.Parse("#F5A623"), ((ISolidColorBrush)warning.Background!).Color);
    }

    /// <summary>
    /// Clear is one of the four commands on the log drawer, and the one that
    /// empties the source list the new replay fills. It still has to empty both
    /// the projected list and the logger's buffer.
    /// </summary>
    [AvaloniaFact]
    public void ClearLogsCommandEmptiesTheListAndTheBuffer()
    {
        SystemEventLogger.ClearLogs();
        SystemEventLogger.Log(LogLevel.Info, LogSource.Desktop, "logbook entry to be cleared");

        using var vm = new UnifiedLogsViewModel();
        PumpUntil(() => vm.Logs.Any(log => log.Message == "logbook entry to be cleared"),
            "the log list replays the ring buffer");

        vm.ClearLogsCommand.Execute().Subscribe();
        PumpUntil(() => vm.Logs.Count == 0, "the log list empties");

        Assert.Empty(vm.Logs);
        Assert.Empty(SystemEventLogger.GetRecentLogs());
    }

    /// <summary>
    /// The rendering of all of the above is decided in two files, so the two
    /// files are checked directly: a ListBox (which virtualizes) rather than the
    /// ItemsControl that realized every row, a row template typed against the
    /// view model so its bindings compile, the six line cap on the message, and
    /// the shared stylesheet taking the container chrome back off the rows.
    ///
    /// Selection: Avalonia's SelectionMode is a flags enum whose zero value is
    /// Single, so SelectionMode="None" does not exist in 11.2.3 and cannot be
    /// written here. What stands in for it is the shared stylesheet, which paints
    /// the hover, pressed and selection states of the container transparent.
    /// </summary>
    [Fact]
    public void TheLogbookMarkupUsesTheVirtualizedRowTemplate()
    {
        string view = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");
        int start = view.IndexOf("SYSTEM LOGS MODAL OVERLAY", StringComparison.Ordinal);
        int end = view.IndexOf("TROUBLESHOOT MODAL OVERLAY", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the log modal section is where the tests expect it");
        string section = view.Substring(start, end - start);

        // The virtualized control, with no selection modes beyond the default
        // and no wrapper scrolling for the panel the ListBox provides itself.
        Assert.Contains("<ListBox", section);
        Assert.Contains("Classes=\"logList\"", section);
        Assert.Contains("ItemsSource=\"{Binding LogsViewModel.Logs}\"", section);
        Assert.DoesNotContain("<ItemsControl", section);
        Assert.DoesNotContain("<ScrollViewer", section);
        Assert.DoesNotContain("SelectionMode=\"Multiple\"", section);
        Assert.DoesNotContain("SelectionMode=\"Toggle\"", section);

        // The row template compiles against the event view model.
        Assert.Contains("<DataTemplate x:DataType=\"vm:LogEventViewModel\">", section);
        Assert.Contains("Text=\"{Binding FormattedTime}\"", section);
        Assert.Contains("Text=\"{Binding LevelText}\"", section);
        Assert.Contains("Text=\"{Binding SourceText}\"", section);
        Assert.Contains("Background=\"{Binding LevelColor}\"", section);
        Assert.DoesNotContain("Text=\"{Binding Level}\"", section);
        Assert.DoesNotContain("Text=\"{Binding Source}\"", section);

        // One row cannot grow without bound.
        Assert.Contains("MaxLines=\"6\"", section);

        // The container chrome is taken off in the shared stylesheet, not in the view.
        string styles = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "App.axaml");
        Assert.Contains("<Style Selector=\"ListBox.logList\">", styles);
        Assert.Contains("<Style Selector=\"ListBox.logList ListBoxItem\">", styles);
        Assert.Contains(
            "<Style Selector=\"ListBox.logList ListBoxItem:pointerover /template/ ContentPresenter#PART_ContentPresenter\">",
            styles);
        Assert.Contains(
            "<Style Selector=\"ListBox.logList ListBoxItem:pressed /template/ ContentPresenter#PART_ContentPresenter\">",
            styles);
        Assert.Contains(
            "<Style Selector=\"ListBox.logList ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter\">",
            styles);
    }
}
