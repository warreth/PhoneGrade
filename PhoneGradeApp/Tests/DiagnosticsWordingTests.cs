using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

// ============ What the diagnostics say ============
//
// The scan runs in Core, which has no language files, so it hands over keys
// and the sentences are put together when the report reaches the panel. Two
// things can go wrong there and neither of them throws: a key nobody wrote,
// which draws the key itself on screen, and a key written in only one of the
// two files, which leaves one language reading the other's words.

[Collection(LanguageCollection.Name)]
public class DiagnosticsWordingTests
{
    /// <summary>
    /// Every wording key the code asks for has to be in both files. The keys
    /// are handed over as plain text rather than through GetString, so the
    /// check that already guards the language files cannot see them. The
    /// screenshot fixture counts as code because it stands in for the service;
    /// the wording tests here invent keys on purpose to see what an undefined
    /// one does, and are the one source left out.
    /// </summary>
    /// Every language the app ships has to carry the wording the panel asks for.
    /// </summary>
    [Fact]
    public void EveryDiagnosticKeyIsDefinedInEveryLanguage()
    {
        string[] asked = WordingKeysTheCodeAsksFor();

        Assert.True(asked.Length > 10,
            $"Only {asked.Length} wording keys were found in the sources, so this scan is not reading them.");

        foreach (var language in PhoneGrade.UI.Services.SupportedLanguages.All)
        {
            string file = $"Strings.{language.Code}.axaml";
            string[] defined = KeysOf(file);
            string[] missing = asked.Where(key => !defined.Contains(key)).ToArray();

            Assert.True(missing.Length == 0,
                $"{file} does not define {string.Join(", ", missing)}");
        }
    }

    [AvaloniaFact]
    public void ThePanelSpeaksDutchWhenDutchIsOn()
    {
        LocalizationManager.SetLanguage("nl");
        var panel = Publish(KeyedReport());

        Assert.Contains("iOS niet beschikbaar", panel.OverallStatus);

        Assert.Equal("Tunnelconnector (cloudflared)", panel.Checks[0].Title);
        Assert.Contains("gewone netwerkadres", panel.Checks[0].Message);
        Assert.Contains("Installeer de tunnelconnector", panel.Checks[0].Resolution);
        Assert.Equal("Verbinding", panel.Checks[0].CategoryLabel);
        Assert.Equal("Waarschuwing", panel.Checks[0].SeverityLabel);

        Assert.Contains("Uitvoercode: 9009", panel.Checks[1].Message);
        Assert.Equal("Fout", panel.Checks[1].SeverityLabel);

        Assert.Equal("Fysieke USB-herkenning", panel.Checks[2].Title);
    }

    [AvaloniaFact]
    public void ThePanelSpeaksEnglishWhenEnglishIsOn()
    {
        // The language is put back before this test ends rather than in a
        // teardown: the dictionary lives on the application, and a teardown
        // may well be running after that application was put away.
        LocalizationManager.SetLanguage("en");
        try
        {
            var panel = Publish(KeyedReport());

            Assert.Contains("iOS detection unavailable", panel.OverallStatus);

            Assert.Equal("Tunnel Connector (cloudflared)", panel.Checks[0].Title);
            Assert.Contains("plain network address", panel.Checks[0].Message);
            Assert.Contains("Install the tunnel connector", panel.Checks[0].Resolution);
            Assert.Equal("Connection", panel.Checks[0].CategoryLabel);
            Assert.Equal("Warning", panel.Checks[0].SeverityLabel);

            Assert.Contains("Exit code: 9009", panel.Checks[1].Message);
            Assert.Equal("Fail", panel.Checks[1].SeverityLabel);

            Assert.Equal("Physical USB Detection", panel.Checks[2].Title);
        }
        finally
        {
            LocalizationManager.SetLanguage("nl");
        }
    }

    /// <summary>
    /// A key nobody wrote has to fall back to the sentence the item already
    /// carries. Drawing the key would put Diag_MsgAdbFailed in front of the
    /// operator, and carrying nothing at all would blank the row, so this pins
    /// the one remaining outcome.
    /// </summary>
    [AvaloniaFact]
    public void AKeyNobodyDefinedNeverReachesThePanel()
    {
        LocalizationManager.SetLanguage("nl");

        var report = new TroubleshootReport
        {
            OverallStatusKey = "Diag_StatusThatNobodyWrote",
            OverallStatus = "Critical tools missing.",
            Checks =
            {
                new DiagnosticCheckItem
                {
                    Category = "iOS",
                    TitleKey = "Diag_TitleThatNobodyWrote",
                    Title = "idevice_id Missing",
                    Severity = DiagnosticSeverity.Fail,
                    MessageKey = "Diag_MsgThatNobodyWrote",
                    Message = "Could not execute idevice_id."
                }
            }
        };

        var panel = Publish(report);

        Assert.Equal("Critical tools missing.", panel.OverallStatus);
        Assert.Equal("idevice_id Missing", panel.Checks[0].Title);
        Assert.Equal("Could not execute idevice_id.", panel.Checks[0].Message);
        Assert.DoesNotContain("Diag_", panel.Checks[0].Title + panel.Checks[0].Message);
    }

    private static TroubleshootViewModel Publish(TroubleshootReport report)
    {
        var panel = new TroubleshootViewModel();
        panel.PublishReport(report);
        return panel;
    }

    // Three rows, one per category the wording has to change for, and one of
    // each severity the pills are painted from.
    private static TroubleshootReport KeyedReport() => new()
    {
        OverallStatusKey = "Diag_StatusAndroidOnly",
        Checks =
        {
            new DiagnosticCheckItem
            {
                Category = "Connection",
                TitleKey = "Diag_TitleTunnel",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = "Diag_MsgNoTunnel",
                ResolutionKey = "Diag_ResolveTunnel"
            },
            new DiagnosticCheckItem
            {
                Category = "iOS",
                TitleKey = "Diag_TitleIdeviceIdMissing",
                Severity = DiagnosticSeverity.Fail,
                MessageKey = "Diag_MsgIdeviceIdExitCode",
                MessageArgs = new[] { "C:\\tools\\idevice_id.exe", "9009" }
            },
            new DiagnosticCheckItem
            {
                Category = "Hardware",
                TitleKey = "Diag_TitleUsbDetection",
                Severity = DiagnosticSeverity.Info,
                MessageKey = "Diag_MsgNoUsbDevice"
            }
        }
    };

    private static string[] WordingKeysTheCodeAsksFor()
    {
        var askedFor = new Regex("\"(Diag_[A-Za-z0-9_]+)\"");

        return CSharpSources()
            .SelectMany(path => askedFor.Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
    }

    private static string[] CSharpSources()
    {
        string[] roots =
        {
            RepoPath.Get("PhoneGradeApp", "PhoneGrade.Core"),
            RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI")
        };

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Append(RepoPath.Get("PhoneGradeApp", "Tests", "ScreenshotRunner.cs"))
            .Where(path => !IsBuildOutput(path))
            .ToArray();
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string[] KeysOf(string file) =>
        XDocument.Load(RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources", file))
            .Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Value)
            .ToArray();
}
