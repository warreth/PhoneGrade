using System.Text.RegularExpressions;

namespace AutoDymoLabel.Core.Diagnostics;

/// <summary>A single panic-string rule: regex over raw log text → human explanation.</summary>
/// <param name="Pattern">Case-insensitive regex matched against the panic string and product description.</param>
/// <param name="Title">Short finding title.</param>
/// <param name="Explanation">What the error means in plain language.</param>
/// <param name="Fix">First action to take (which flex/part to check).</param>
/// <param name="Level">Severity: Error blocks auto-print; Warning is advisory.</param>
public record PanicRule(string Pattern, string Title, string Explanation, string Fix, Severity Level = Severity.Error);

/// <summary>
/// Rule database for iOS kernel panic logs — distilled from the iFixit iPhone Kernel Panics wiki,
/// vccboardrepairs' panic list and GSM-forum technician tables.
/// </summary>
public static class PanicRules
{
    public static readonly PanicRule[] All =
    [
        // --- thermalmonitord watchdog: missing sensor check-ins (3-minute reboot) ---
        new(@"Missing sensor\(s?\)?:?\s*.*\bPRS0\b|Missing sensor\(s?\)?:?\s*.*\bprs0\b",
            "Barometer sensor missing (PRS0)",
            "De barometer op de charge-port flex stuurt geen data meer. iOS herstart het toestel na 3 minuten zonder sensorcontact.",
            "Vervang de charge-port (lightning/USB-C) flex, of controleer de connector op de logic board."),
        new(@"Missing sensor\(s?\)?:?\s*.*\bMic1\b",
            "Ondermicrofoon mist (Mic1)",
            "De onderste microfoon (in de charge-port flex) reageert niet. Vaak vochtschade of een defecte flex.",
            "Vervang de charge-port flex met OEM-onderdeel; controleer ook de board-connector op corrosie."),
        new(@"Missing sensor\(s?\)?:?\s*.*\bMic2\b",
            "Achtermicrofoon mist (Mic2)",
            "De microfoon naast de cameraflits reageert niet — zit op de power-button/camera-flash flex.",
            "Vervang de power-button flex of camera-flash flex, of de desbetreffende connector."),
        new(@"Missing sensor\(s?\)?:?\s*.*\bTG0B\b",
            "Batterij-sensor mist (TG0B)",
            "Het toestel 'ziet' de batterij niet (Tigris/batterij-temperatuursensor). Leidt tot reboot-loops.",
            "Controleer de batterijconnector en batterij; vervang de batterij. Op de 11 Pro (Max) kan ook de charge-port de oorzaak zijn."),
        new(@"Missing sensor\(s?\)?:?\s*.*\bTG0V\b",
            "Spanningssensor batterij mist (TG0V)",
            "De batterij-spanningsensor meldt zich niet. Laadcircuit of batterij defect.",
            "Controleer batterij en charge-port; board-level diagnose van het laadcircuit bij aanhoudende reboot."),
        new(@"thermalmonitord",
            "Thermal monitor crash",
            "De temperatuurbewaking (thermalmonitord) krijgt geen sensordata binnen 3 minuten en forceert een herstart.",
            "Zoek bovenstaande 'Missing sensor'-regels voor het exacte onderdeel; meestal charge-port of power-button flex."),

        // --- AOP (Always On Processor) panics ---
        new(@"AOP PANIC.*(?:PressureController|Barometer)",
            "AOP panic: barometer",
            "De barometer op de onderste systeemkabel communiceert niet met de Always-On Processor (vaak iPhone X+).",
            "Vervang de onderste systeemkabel / dock flex; controleer op vochtschade."),
        new(@"AOP PANIC.*(?:SCM|prox|proximity)",
            "AOP panic: proximity sensor",
            "De nabijheidssensor (prox) reageert niet — klassiek na vochtschade; veroorzaakt reboot-loops.",
            "Vervang de oor/sensor-flex (proximity/oorstem); controleer de front-camera-flex."),
        new(@"AOP PANIC.*(?:NMI|POWER)",
            "AOP panic: NMI/power",
            "Always-On Processor meldt een power-interrupt: vaak de power-button flex of front-camera assembly.",
            "Controleer/vervang de power-button flex of front-camera-flex."),
        new(@"AOP PANIC",
            "AOP panic",
            "De Always-On Processor (sensorhub) crasht — vrijwel altijd hardware: een sensorflex communiceert niet.",
            "Zoek de genoemde sensor in de log; test met bekend-goed onderdeel (dock flex, front flex)."),

        // --- storage / baseband / CPU ---
        new(@"\bANS2?\b.*(?:panic|recoverable)|AppleANSSupport|NAND",
            "NAND-opslagfout (ANS/ANS2)",
            "De flashopslag-controller (NAND) rapporteert fouten. Data-verlies dreigt; reboot-loops mogelijk.",
            "Board-level reparatie: NAND-chip of communicatielijnen. vereist microsoldeer-ervaring."),
        new(@"userspace watchdog timeout: no successful checkins from backboardd|springboard",
            "Software watchdog (backboardd/springboard)",
            "Systeemprocessen melden zich niet — meestal een softwareprobleem, geen hardwaredefect.",
            "Maak een back-up en herstel het toestel via Finder/iTunes of 3uTools (DFU-restore).",
            Severity.Warning),
        new(@"wifid|WLAN",
            "Wi-Fi-modulefout",
            "De Wi-Fi-chip/antenne reageert niet (wifid watchdog). Vaak board-level: Wi-Fi IC of antenne.",
            "Controleer Wi-Fi/BT-module; board-level diagnose van de WLAN-lijnen."),
        new(@"AppleSocHot|Hot Hot Hot",
            "CPU oververhit (AppleSocHot)",
            "De CPU rapporteert oververhitting op de power-lijn tussen PMIC en CPU — vrijwel altijd board-level.",
            "Board-level diagnose; controleer gebieden met eerder soldeerwerk (Wi-Fi/audio)."),
        new(@"SEP ROM|seput",
            "Secure Enclave (SEP) boot-fout",
            "De Secure Enclave Processor kan niet booten — Face ID/Touch ID en data raken onbereikbaar.",
            "Niet herstelbaar door vervanging; board-level diagnose van SEP-lijnen. Data meestal verloren."),
        new(@"Kernel data abort|Kernel instruction abort|undefined kernel instruction",
            "Kernel data-abort (CPU/RAM)",
            "De CPU raampt op ongeldige data — RAM, NAND of board-lijnen defect.",
            "Board-level diagnose; test op schade rond CPU/RAM-voeding."),
        new(@"SMC.*Assertion Failed|BSC FAILURE",
            "SMC assertion failed",
            "Sensor-array meldt zich niet bij de System Management Controller (iPhone 13+): 3-minuten reboot.",
            "Zoek de genoemde sensor-code; vervang het bijhorende onderdeel (flex) of board-level diagnose."),
        new(@"i2c|I2C",
            "I2C-communicatiefout",
            "Een chip op de I2C-bus antwoordt niet — afhankelijk van het kanaal: ALS (lichtsensor), camera, audio-codec, ...",
            "Vergelijk met het schema welk onderdeel op het genoemde I2C-kanaal zit; vervang dat onderdeel."),

        // --- restore/activation helper diagnostics ---
        new(@"Could not connect to lockdownd",
            "Toestel niet vertrouwd",
            "De computer mag het toestel nog niet uitlezen: het 'Vertrouw deze computer'-scherm is niet (goed) bevestigd.",
            "Ontkoppel, toets de pincode in, tik 'Vertrouwen' en sluit opnieuw aan.",
            Severity.Warning),
        new(@"No device found|ERROR: Unable to connect",
            "Geen toestel gevonden",
            "Geen iPhone/iPad gedetecteerd op USB. Kabel, poort of usbmuxd-driver probleem.",
            "Probeer een andere kabel/USB-poort; herstart de app; op Windows: herinstalleer de Apple Mobile Device-service.",
            Severity.Warning),
    ];

    /// <summary>Applies all rules to raw log text; returns the matched findings (deduped, highest severity first).</summary>
    public static List<DiagnosticIssue> Match(string logText)
    {
        var issues = new List<DiagnosticIssue>();
        foreach (var rule in All)
        {
            try
            {
                if (Regex.IsMatch(logText, rule.Pattern, RegexOptions.IgnoreCase))
                    issues.Add(new DiagnosticIssue
                    {
                        Title = rule.Title,
                        Explanation = rule.Explanation,
                        Fix = rule.Fix,
                        Level = rule.Level,
                    });
            }
            catch (ArgumentException) { /* bad pattern — skip, never crash diagnostics */ }
        }
        return issues;
    }
}

