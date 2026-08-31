
namespace AutoDymoLabel.Core.Diagnostics;

/// <summary>
/// Reads syslogs / panic logs from a connected device the same way 3uTools-style tools do:
/// pulls CrashReporter via idevicecrashreport (needs a prior idevicepair), then scans the
/// panic-full-*.ips files with the PanicRules knowledge base.
/// </summary>
public static partial class DiagnosticService
{
    /// <summary>Local dir where crash reports are copied for parsing.</summary>
    public static string WorkDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoDymoLabel", "crashreports");

    /// <summary>
    /// Full diagnostic pass: connectivity, battery health, activation and panic-log analysis.
    /// Pulls crash reports when possible; falls back to ideviceinfo error strings.
    /// </summary>
    public static async Task<List<DiagnosticIssue>> DiagnoseAsync(string udid)
    {
        var issues = new List<DiagnosticIssue>();

        // 1. Connectivity / trust / activation via lockdown state
        var state = await DeviceService.GetConnectionStateAsync(udid);
        if (state != DeviceService.ConnectionState.Connected)
        {
            issues.Add(state switch
            {
                DeviceService.ConnectionState.NotTrusted => new()
                {
                    Title = "Toestel niet vertrouwd",
                    Explanation = "Ontgrendel het toestel en tik op 'Vertrouwen' om diagnostiek mogelijk te maken.",
                    Fix = "Koppel los, ontgrendel, tik Vertrouwen, koppel opnieuw aan.",
                    Level = Severity.Error,
                },
                DeviceService.ConnectionState.ToolsMissing => new()
                {
                    Title = "idevice-tools niet gevonden",
                    Explanation = "De libimobiledevice-tools zijn niet gebundeld en niet op PATH.",
                    Fix = "Herinstalleer de app of installeer libimobiledevice (brew install libimobiledevice / vzwijk.com).",
                    Level = Severity.Error,
                },
                _ => new()
                {
                    Title = "Toestel niet bereikbaar",
                    Explanation = "Geen geldige lockdown-verbinding: kabel, poort of trust-probleem.",
                    Fix = "Andere kabel/poort proberen; toestel herstarten.",
                    Level = Severity.Error,
                },
            });
            return issues;
        }

        // 2. Battery sensor sanity — a device that can't read battery health has a Tigris/battery issue
        var data = await DeviceService.GetDeviceDataAsync(udid);
        if (data.BatteryHealth == "NOBATT")
            issues.Add(new DiagnosticIssue
            {
                Title = "Batterijgegevens niet leesbaar",
                Explanation = "De batterij-SFI/sensor reageert niet op lockdown-vragen — signaal van een defecte batterij, Tigris-IC of flex.",
                Fix = "Controleer batterijconnector; test met bekend-goede batterij; Tigris-IC bij laadproblemen.",
                Level = Severity.Warning,
            });
        else if (int.TryParse(data.BatteryHealth.TrimEnd('%'), out int health) && health < 80)
            issues.Add(new DiagnosticIssue
            {
                Title = $"Batterijconditie laag ({health}%)",
                Explanation = "Maximale capaciteit is onder 80% — onder Apple's service-drempel.",
                Fix = "Batterij vervangen of als 'C-kwaliteit' labelen.",
                Level = Severity.Warning,
            });

        // 3. Panic logs — the main hardware-evidence source
        issues.AddRange(await AnalyzePanicLogsAsync(udid));
        return issues;
    }

    /// <summary>Copies CrashReporter off the device and runs PanicRules over the panic logs.</summary>
    public static async Task<List<DiagnosticIssue>> AnalyzePanicLogsAsync(string udid)
    {
        try
        {
            // Pairing record needed for the crashreport service on newer iOS versions.
            await ToolRunner.RunAsync("idevicepair", $"-u {udid} validate");

            string deviceDir = Path.Combine(WorkDir, udid);
            Directory.CreateDirectory(deviceDir);
            var (output, exit) = await ToolRunner.RunAsync("idevicecrashreport",
                $"-u {udid} -e {deviceDir}", 120_000);
            if (exit != 0 && !Directory.Exists(deviceDir))
                return FallbackInfo("Geen crash-logs opgehaald", output);

            var findings = new List<DiagnosticIssue>();
            foreach (string file in Directory.EnumerateFiles(deviceDir, "panic-full*", SearchOption.AllDirectories))
            {
                string log = await File.ReadAllTextAsync(file);
                findings.AddRange(PanicRules.Match(log));
                findings.Add(new DiagnosticIssue
                {
                    Title = $"Panic-log gevonden: {Path.GetFileName(file)}",
                    Explanation = "Het toestel heeft minstens één onverwachte herstart vastgelegd.",
                    Fix = "Zie de bijbehorende bevindingen; herhaalde panics van hetzelfde type = hardwarefout.",
                    Level = Severity.Warning,
                });
            }
            return findings.Dedupe();
        }
        catch (Exception ex)
        {
            return FallbackInfo("Diagnostiek kon niet gestart worden", ex.Message);
        }
    }

    private static List<DiagnosticIssue> FallbackInfo(string title, string detail) =>
        [new() { Title = title, Explanation = detail, Fix = "Herstart de diagnostiek of koppel het toestel opnieuw aan.", Level = Severity.Warning }];

    public static List<DiagnosticIssue> Dedupe(this List<DiagnosticIssue> issues) =>
        issues
            .GroupBy(i => i.Title)
            .Select(g => g.OrderBy(i => i.Level).First())
            .OrderBy(i => i.Level)
            .ToList();
}
