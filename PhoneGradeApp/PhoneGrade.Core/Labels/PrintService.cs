using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhoneGrade.Core;

/// <summary>
/// Hands a finished file to whatever the operating system uses for printing it.
///
/// .NET has no cross-platform printing API. System.Drawing.Printing stopped being
/// supported off Windows in .NET 6 and the escape hatch for it was removed in
/// .NET 7, and Avalonia ships no printing of its own, so this is three short
/// branches and a fallback. Each one ends in the platform's own print dialog
/// rather than sending paper without asking, because a label printed silently to
/// the wrong queue is not something the operator can undo.
/// </summary>
public static class PrintService
{
    /// <summary>What happened when a file was handed over to the system.</summary>
    public enum Outcome
    {
        /// <summary>The platform took the file and its print dialog is up.</summary>
        Opened,

        /// <summary>Sent straight to a named print queue, no dialog.</summary>
        Sent,

        /// <summary>Nothing could take it. The message says what was tried.</summary>
        Failed,
    }

    /// <summary>
 /// What came of handing a file to the system, and what to tell the operator
    /// about it.
 /// </summary>
    /// <param name="Outcome">Whether anything took it.</param>
    /// <param name="Message">
    /// One sentence, in the wording the caller handed in. This is shown under the
    /// panel's buttons, so it comes from the app's dictionaries rather than from
    /// this file's author.
    /// </param>
    public sealed record Attempt(Outcome Outcome, string Message);

    /// <summary>
    /// Opens the print dialog for a file. On Windows that is the print verb when
    /// a PDF handler is registered and a plain open when one is not, because
    /// without a handler the verb throws rather than degrading. Everywhere else
    /// the file goes to its default application, which is where the print button
    /// in that application lives.
    /// </summary>
    public static Attempt PrintDialog(string path, ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;

        if (!File.Exists(path))
            return new Attempt(Outcome.Failed, words.Say(words.NoFileAt, path));

        try
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using Process? _ = Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = true,
                        Verb = "print",
                    });
                    return new Attempt(Outcome.Opened, words.PrintDialogOpened);
                }
                catch (Win32Exception)
                {
                    // No application has registered a print command for this file
                    // type. Opening it instead lands the operator in a viewer with
                    // a print button, which is the same job one step later.
                    using Process? _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return new Attempt(Outcome.Opened, words.PrintNoHandler);
                }
            }

            if (OperatingSystem.IsMacOS())
            {
                Start("open", path);
                return new Attempt(Outcome.Opened, words.PrintPreviewOpened);
            }

            Start("xdg-open", path);
            return new Attempt(Outcome.Opened, words.PrintDefaultApp);
        }
        catch (Exception ex)
        {
            return new Attempt(Outcome.Failed, words.Say(words.PrintNotStarted, ex.Message));
        }
    }

    /// <summary>
    /// Sends a file straight to a named queue, with no dialog. Only CUPS offers
    /// this, so it is a no-op elsewhere and the caller is told so rather than
    /// being left waiting for paper that will never arrive.
    /// </summary>
    /// <param name="queue">The queue name as it appears in lpstat or in the print settings.</param>
    /// <param name="options">
    /// Printer specific options, the ones a label printer needs: the media, the
    /// quality, the density. Empty is a plain print.
    /// </param>
    public static Attempt PrintToQueue(string path, string queue, IReadOnlyList<string>? options = null,
        ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;

        // Checked before the platform, not after: naming no queue at all is the
        // same fault everywhere, and reporting it as a Windows-only one sends an
        // operator on a Mac to a print dialog that was never going to help.
        if (string.IsNullOrWhiteSpace(queue))
            return new Attempt(Outcome.Failed, words.NoPrinterChosen);

        if (OperatingSystem.IsWindows())
            return new Attempt(Outcome.Failed, words.PrintQueueNeedsDialog);

        if (!File.Exists(path))
            return new Attempt(Outcome.Failed, words.Say(words.NoFileAt, path));

        try
        {
            var arguments = new List<string> { "-d", queue };
            if (options is { Count: > 0 })
            {
                arguments.Add("-o");
                arguments.Add(string.Join(",", options));
            }

            arguments.Add(path);
            Start("lp", arguments.ToArray());
            return new Attempt(Outcome.Sent, words.Say(words.PrintSentToQueue, queue));
        }
        catch (Exception ex)
        {
            return new Attempt(Outcome.Failed, words.Say(words.PrintToQueueFailed, queue, ex.Message));
        }
    }

    /// <summary>The CUPS queues this machine knows about, with the default first.</summary>
    public static IReadOnlyList<PrintQueue> ListQueues()
    {
        if (OperatingSystem.IsWindows()) return [];

        var queues = new List<PrintQueue>();
        foreach (string line in Run("lpstat", ["-p", "-d"]).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "printer DYMO_LabelWriter is idle.  enabled since ..." or
            // "system default destination: DYMO_LabelWriter"
            int label = line.IndexOf("printer ", StringComparison.Ordinal);
            if (label < 0) continue;

            string rest = line[(label + "printer ".Length)..].Trim();
            int end = rest.IndexOf(' ');
            string name = end < 0 ? rest : rest[..end];
            if (name.Length > 0)
                queues.Add(new PrintQueue(name, line.Contains("idle", StringComparison.Ordinal)));
        }

        return queues;
    }

    /// <summary>Opens a folder in the file manager, which is where exports go.</summary>
    public static Attempt OpenFolder(string path, ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;

        // Asked for a folder, and it has to be that folder. Falling back to the
        // parent would land the operator one level up, which looks like it worked
        // while the exports they wanted are still nowhere to be seen, and that is
        // the one outcome this is here to rule out.
        if (!Directory.Exists(path))
            return new Attempt(Outcome.Failed, words.Say(words.NoFolderAt, path));

        string folder = path;

        try
        {
            if (OperatingSystem.IsMacOS()) Start("open", folder);
            else if (OperatingSystem.IsLinux()) Start("xdg-open", folder);
            else
            {
                using Process? _ = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }

            return new Attempt(Outcome.Opened, words.FolderOpened);
        }
        catch (Exception ex)
        {
            return new Attempt(Outcome.Failed, words.Say(words.FolderNotOpened, ex.Message));
        }
    }

    /// <summary>Opens one file in whatever handles it.</summary>
    public static Attempt Open(string path, ExportWording? wording = null)
    {
        ExportWording words = wording ?? ExportWording.English;

        if (!File.Exists(path))
            return new Attempt(Outcome.Failed, words.Say(words.NoFileAt, path));

        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                using Process? _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else
            {
                Start("xdg-open", path);
            }

            return new Attempt(Outcome.Opened, words.FileOpened);
        }
        catch (Exception ex)
        {
            return new Attempt(Outcome.Failed, words.Say(words.FileNotOpened, ex.Message));
        }
    }

    private static void Start(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        using Process? process = Process.Start(info);
    }

    private static string Run(string fileName, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);

            using Process? process = Process.Start(info);
            if (process is null) return "";
            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception)
        {
            // No CUPS, or no lp on the path. A machine without a print system is a
            // normal machine, not a fault to report.
            return "";
        }
    }
}

/// <param name="Name">The queue name to pass to lp.</param>
/// <param name="IsIdle">Whether it is ready. A queue that is busy is still a choice.</param>
public sealed record PrintQueue(string Name, bool IsIdle);