# Auto Dymo Label

Steek een iPhone/iPad in de USB-poort en het verkoop-label komt er (zogoed als) automatisch uit.
De app leest het toestel uit via **libimobiledevice**, draait **diagnostiek** over de panic-logs
(zoals 3uTools), en opent het invulbare label in de DYMO Label software.

![platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue)

## Installeren (Windows & macOS)

Download de installer van de **[Releases](https://github.com/warreth/Auto-Dymo-Label/releases)** pagina:

- **Windows**: `AutoDymoLabel-win-Setup.exe` — Velopack-installer, geen admin nodig.
- **macOS**: `AutoDymoLabel-osx-*.pkg` — sleep naar Applications.

De installer bundelt automatisch de benodigde `libimobiledevice` CLI-tools (`ideviceinfo`,
`idevicediagnostics`, `idevicecrashreport`, …). Je hoeft **niets zelf te installeren**.

### Vereisten

- **DYMO Label** software (gratis van [dymo.com](https://www.dymo.com)) — om het label te openen/printen.
- .NET 8 Desktop Runtime (de installer downloadt dit zelf indien nodig).
- Op Windows: als een toestel niet gevonden wordt, installeer dan eens iTunes van de
  Microsoft Store (levert de Apple Mobile Device USB-driver).

## Hoe het werkt (streamlined proces)

1. **Insteken** → de app detecteert het toestel binnen ~2 seconden en start automatisch
   (instelbaar: *"Start automatisch bij insteken"*).
2. **Vertrouwen/activatie** → ontbrekende trust of activatie wordt gedetecteerd; de app
   kan activatie bypassen (`ideviceactivation activate -b`).
3. **Uitlezen** → model, kleur, opslag, IMEI/serienummer, batterijconditie.
4. **Diagnostiek** → panic-logs (`panic-full-*.ips`) worden van het toestel gehaald en
   vertaald naar leesbare Nederlands: wat is kapot en wat moet je vervangen.
5. **Label** → vraagt alleen wat nog niet ingesteld is (kwaliteit, betaalmethode — beide
   kun je als standaard vastleggen), en opent het label daarna in DYMO Label.

Alles met vaste standaardinstellingen = **nul klikken**; het label opent vanzelf.

## Diagnostiek (3uTools-achtig)

De app leest de CrashReporter van het toestel via `idevicecrashreport` en parseert
kernel-panics met een regeldatabase gebaseerd op de publieke kennis van
[iFixit's Kernel Panics wiki](https://www.ifixit.com/Wiki/iPhone_Kernel_Panics) en
[vccboardrepairs' panic-log lijst](https://vccboardrepairs.com/panic-log-list/):

| Panic-tekst | Betekenis | Reparatie |
|---|---|---|
| `Missing sensor(s): PRS0` | Barometer op charge-port flex | Charge-port vervangen |
| `Missing sensor(s): Mic1` | Ondermicrofoon | Charge-port flex (OEM) |
| `Missing sensor(s): Mic2` | Achtermicrofoon | Power-button / flash flex |
| `Missing sensor(s): TG0B` | Batterij-sensor (Tigris) | Batterij/connector controleren |
| `AOP PANIC … prox` | Proximity-sensor (vochtschade) | Oor/sensor-flex |
| `ANS2 … panic` | NAND-opslag | Board-level reparatie |
| `AppleSocHot` | CPU power-lijn | Board-level reparatie |

Ook zonder panic-log controleert de app op: niet-vertrouwde verbinding, ontbrekende
batterij-SFI-data, lage batterijconditie (<80%) en missende tools.

## Instellingen (settings.json)

Opgeslagen in `%LOCALAPPDATA%/AutoDymoLabel/settings.json`:

```json
{
  "Theme": "Dark",                  // Dark | Light | System
  "AutoActivate": true,
  "AutoDetectOnPlug": true,
  "RunDiagnostics": true,
  "Enable85PercentChecker": true,   // X + "100%" op label bij < 85%
  "OpenEditorBeforePrint": false,
  "DefaultQuality": "",             // "" | A | B | C
  "DefaultPaymentMethod": "",       // "" | Marge | BTW
  "TemplatePath": null              // eigen my.dymo template
}
```

Alle opties zijn ook in de app te wijzigen onder *Instellingen*.

## Ontwikkelen

```bash
dotnet build AutoDymoLabelApp/AutoDymoLabelApp.sln
dotnet run --project AutoDymoLabelApp/Tests      # self-check tests (23 asserts)
dotnet run --project AutoDymoLabelApp/AutoDymoLabelApp.UI
```

Zonder gebundelde tools zoekt de app ze op PATH (`brew install libimobiledevice` op macOS).

## Releases bouwen

GitHub Actions bouwt automatisch bij een `v*` tag:

- Windows: `vpk pack` → Velopack installer (`.exe`) + portable, incl. gebundelde
  libimobiledevice-tools uit [L1ghtmann/libimobiledevice releases](https://github.com/L1ghtmann/libimobiledevice/releases).
- macOS: publiceert + bundelt de brew-versie van libimobiledevice.

```bash
git tag v1.1.0 && git push --tags
```

## Structuur

```
AutoDymoLabelApp/
├── AutoDymoLabelApp.Core/       # device tools, parsers, diagnostiek, labels
│   ├── Diagnostics/             # PanicRules (kennisbank) + DiagnosticService
│   ├── DeviceService.cs         # libimobiledevice wrappers
│   ├── LabelService.cs          # .dymo template vullen
│   ├── Mappers.cs / Parsers.cs  # model/kleur/opslag mapping
│   └── ToolRunner.cs            # tool-resolutie + veilig processen draaien
├── AutoDymoLabelApp.UI/         # Avalonia UI (MVVM, ReactiveUI)
└── Tests/                       # assert-based self-checks
```

## Licentie

MIT — zie [LICENSE](LICENSE).
