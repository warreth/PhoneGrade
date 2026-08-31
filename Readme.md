# Auto Dymo Label

Plug an iPhone or iPad into USB and the sales label comes out by itself. The
app reads the device with **libimobiledevice**, runs **diagnostics** over the
panic logs (think 3uTools), and opens the filled-in label in DYMO Label
software.

![platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue)

## Installing (Windows and macOS)

Grab the installer from the [Releases](https://github.com/warreth/Auto-Dymo-Label/releases)
page:

- **Windows**: `AutoDymoLabel-win-Setup.exe`. Velopack installer, no admin
  rights needed.
- **macOS**: `AutoDymoLabel-osx-arm64-*.pkg` (Apple Silicon) or
  `AutoDymoLabel-osx-x64-*.pkg` (Intel). Drag into Applications.

The installers bundle the `libimobiledevice` command line tools
(`ideviceinfo`, `idevicediagnostics`, `idevicecrashreport`, and friends) so
there is nothing to set up yourself.

### Requirements

- **DYMO Label** software, free from [dymo.com](https://www.dymo.com), to open
  and print the label.
- .NET 8 Desktop Runtime. The installer downloads it if it is missing.
- On Windows, if a device never shows up, install iTunes from the Microsoft
  Store once. It ships the Apple Mobile Device USB driver.

## How it works

1. **Plug in**. The app notices the device within about two seconds and starts
   on its own (can be turned off in settings).
2. **Trust and activation**. Missing trust or activation gets detected. The
   app can bypass activation with `ideviceactivation activate -b`.
3. **Read**. Model, color, storage, IMEI or serial number, battery condition.
4. **Diagnose**. Panic logs (`panic-full-*.ips`) get pulled off the device and
   translated into plain language: what is broken and what to replace.
5. **Label**. You only get asked for what is not set yet (quality, payment
   method, both can be defaulted), then the label opens in DYMO Label.

With the defaults filled in this is zero clicks. The label just opens.

## Diagnostics

The app reads the device CrashReporter through `idevicecrashreport` and parses
kernel panics with a rule database built from public knowledge:
[iFixit's Kernel Panics wiki](https://www.ifixit.com/Wiki/iPhone_Kernel_Panics)
and the [vccboardrepairs panic log list](https://vccboardrepairs.com/panic-log-list/).

| Panic text | Meaning | Repair |
|---|---|---|
| `Missing sensor(s): PRS0` | Barometer on the charge port flex | Replace charge port |
| `Missing sensor(s): Mic1` | Bottom microphone | Charge port flex (OEM) |
| `Missing sensor(s): Mic2` | Rear microphone | Power button / flash flex |
| `Missing sensor(s): TG0B` | Battery sensor (Tigris) | Check battery and connector |
| `AOP PANIC ... prox` | Proximity sensor, often liquid damage | Ear speaker / sensor flex |
| `ANS2 ... panic` | NAND storage | Board level repair |
| `AppleSocHot` | CPU power line | Board level repair |

Even without a panic log the app checks for an untrusted connection, missing
battery SFI data, low battery condition (below 80%), and missing tools.

## Settings

Stored at `%LOCALAPPDATA%/AutoDymoLabel/settings.json`:

```json
{
  "Theme": "Dark",                  // Dark | Light | System
  "AutoActivate": true,
  "AutoDetectOnPlug": true,
  "RunDiagnostics": true,
  "Enable85PercentChecker": true,   // writes X + "100%" on the label below 85%
  "OpenEditorBeforePrint": false,
  "DefaultQuality": "",             // "" | A | B | C
  "DefaultPaymentMethod": "",       // "" | Marge | BTW
  "TemplatePath": null              // your own my.dymo template
}
```

Every option can also be changed in the app under Settings.

## Developing

```bash
dotnet build AutoDymoLabelApp/AutoDymoLabelApp.sln
dotnet test AutoDymoLabelApp/Tests/Tests.csproj     # 62 tests, headless Avalonia included
dotnet run --project AutoDymoLabelApp/AutoDymoLabelApp.UI
```

Without bundled tools the app looks for them on PATH (`brew install
libimobiledevice` on macOS).

To render screenshots of every window and state:

```bash
dotnet run --project AutoDymoLabelApp/Tests -- /tmp/shots
```

## Building releases

GitHub Actions builds on every `v*` tag:

- **Windows**: `vpk pack` turns the publish output into a Velopack installer
  (`.exe`) plus a portable zip, with the libimobiledevice tools from
  [L1ghtmann/libimobiledevice](https://github.com/L1ghtmann/libimobiledevice)
  releases bundled in.
- **macOS (Apple Silicon and Intel)**: publish, Homebrew tools copied in with
  their dylibs and load paths rewritten, then a `.pkg`.

You can also trigger a build by hand from the Actions tab with a version
number.

```bash
git tag v1.1.0 && git push --tags
```

## Layout

```
AutoDymoLabelApp/
├── AutoDymoLabelApp.Core/       # device tools, parsers, diagnostics, labels
│   ├── Diagnostics/             # PanicRules (knowledge base) + DiagnosticService
│   ├── DeviceService.cs         # libimobiledevice wrappers
│   ├── LabelService.cs          # fills the .dymo template
│   ├── Mappers.cs / Parsers.cs  # model/color/storage mapping
│   └── ToolRunner.cs            # tool resolution + safe process running
├── AutoDymoLabelApp.UI/         # Avalonia UI (MVVM, ReactiveUI)
└── Tests/                       # xUnit suite, headless UI + visual theme tests
```

## License

MIT, see [LICENSE](LICENSE).
