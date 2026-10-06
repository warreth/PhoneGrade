<div align="center">
  <img src="PhoneGradeApp/PhoneGrade.UI/Assets/logo.svg" width="96" height="96" alt="PhoneGrade"/>
  <h1>PhoneGrade</h1>
  <p><strong>Inspection, grading and documentation for second hand phones.</strong></p>
  <p>Desktop app for phone shops and refurbishers. Windows, macOS and Linux.</p>
</div>
<div align="center">
  <img src="PhoneGradeApp/PhoneGrade.UI/Assets/logo.svg" width="96" height="96" alt="PhoneGrade"/>
  <h1>PhoneGrade</h1>
  <p><strong>Inspection, grading and documentation for second hand phones.</strong></p>
  <p>Desktop app for phone shops and refurbishers. Windows, macOS and Linux.</p>
</div>

---
---

## What it does
## What it does

Plug a phone in over USB. PhoneGrade reads what the handset reports about itself,
runs eleven interactive hardware tests in the phone's own browser through a QR code,
gives the device a grade from A to C, then prints a label and a report you can hand
a customer.
Plug a phone in over USB. PhoneGrade reads what the handset reports about itself,
runs eleven interactive hardware tests in the phone's own browser through a QR code,
gives the device a grade from A to C, then prints a label and a report you can hand
a customer.

**Read automatically:** model, serial, storage, memory, battery cycles and health,
IMEI, activation lock and Find My, carrier and SIM status.
**Read automatically:** model, serial, storage, memory, battery cycles and health,
IMEI, activation lock and Find My, carrier and SIM status.

**Original parts on iOS:** nine components compared against the factory values the
phone publishes about itself. **Integrity on Android:** bootloader, vbmeta, system
image, warranty bit and factory reset protection.
**Original parts on iOS:** nine components compared against the factory values the
phone publishes about itself. **Integrity on Android:** bootloader, vbmeta, system
image, warranty bit and factory reset protection.

**Eleven interactive tests:** touchscreen coverage across 112 cells, force touch,
display and dead pixels, rotation, earpiece and loudspeaker, microphone, SIM and
calling, every camera lens, motion sensors, GPS, vibration.
**Eleven interactive tests:** touchscreen coverage across 112 cells, force touch,
display and dead pixels, rotation, earpiece and loudspeaker, microphone, SIM and
calling, every camera lens, motion sensors, GPS, vibration.

**Output:** DYMO labels, label PDF, full inspection report as PDF, CSV and JSON
exports, and a local audit record per inspection.
**Output:** DYMO labels, label PDF, full inspection report as PDF, CSV and JSON
exports, and a local audit record per inspection.

## It reports what it could not read
## It reports what it could not read

A grading tool that guesses is how a shop ends up selling a bad phone. A refused
read is reported as refused, never as an empty field and never as a pass. A denied
permission is a choice, not a defect. Grade A is automatically lowered to B when a
phone is missing a hardware API it advertises.
A grading tool that guesses is how a shop ends up selling a bad phone. A refused
read is reported as refused, never as an empty field and never as a pass. A denied
permission is a choice, not a defect. Grade A is automatically lowered to B when a
phone is missing a hardware API it advertises.

## Pricing
## Pricing

| Plan | Price | What you get |
| --- | --- | --- |
| Free | Free, forever | 10 scans, no account needed |
| Pro | EUR 400 per year | Unlimited scans on every device, every station |
| Custom | On request | Volume terms and invoicing |
| Plan | Price | What you get |
| --- | --- | --- |
| Free | Free, forever | 10 scans, no account needed |
| Pro | EUR 400 per year | Unlimited scans on every device, every station |
| Custom | On request | Volume terms and invoicing |

One price, not a bill per check. Nothing is metered: testing a device again because
you are unsure costs nothing. IMEI, blacklist, carrier lock and Knox lookups are not
included and are not resold; they come from [imei.info](https://imei.info) with a key
you hold yourself, and most refurbishers never turn them on.
One price, not a bill per check. Nothing is metered: testing a device again because
you are unsure costs nothing. IMEI, blacklist, carrier lock and Knox lookups are not
included and are not resold; they come from [imei.info](https://imei.info) with a key
you hold yourself, and most refurbishers never turn them on.

**Website and downloads:** <https://phonegrade.app>
**Store:** <https://store.phonegrade.app>
**Questions and feature requests:** hello@phonegrade.app
**Website and downloads:** <https://phonegrade.app>
**Store:** <https://store.phonegrade.app>
**Questions and feature requests:** hello@phonegrade.app

## Requirements
## Requirements

**Computer:** Windows 10 or 11, macOS 11 or newer, or Linux x64. Needs the .NET 8
runtime; the macOS and Linux builds are self contained. Android platform tools for
Android phones, libimobiledevice for iPhones and iPads, both installable from within
the app.
**Computer:** Windows 10 or 11, macOS 11 or newer, or Linux x64. Needs the .NET 8
runtime; the macOS and Linux builds are self contained. Android platform tools for
Android phones, libimobiledevice for iPhones and iPads, both installable from within
the app.

**Phone:** iOS 13 or newer or Android 8 or newer, a USB cable, and unlocked with
"Trust This Computer" confirmed. On Android also USB debugging on and File Transfer
selected.
**Phone:** iOS 13 or newer or Android 8 or newer, a USB cable, and unlocked with
"Trust This Computer" confirmed. On Android also USB debugging on and File Transfer
selected.

## Privacy
## Privacy

No cloud service and no telemetry upload. The only local server listens on your own
machine. Camera and microphone streams never leave the phone being tested, only
results do. Exports and audit files stay in your local app data folder. IMEI API keys
stay on the machine that entered them.
No cloud service and no telemetry upload. The only local server listens on your own
machine. Camera and microphone streams never leave the phone being tested, only
results do. Exports and audit files stay in your local app data folder. IMEI API keys
stay on the machine that entered them.

## Building from source
## Building from source

Requires the .NET 8 SDK and Node.
Requires the .NET 8 SDK and Node.

```
dotnet build PhoneGradeApp/PhoneGrade.sln -c Release
dotnet test PhoneGradeApp/Tests/Tests.csproj -c Release --no-build
node --test "PhoneGradeApp/PhoneGrade.UI/wwwroot/tests/*.test.js"
```
```
dotnet build PhoneGradeApp/PhoneGrade.sln -c Release
dotnet test PhoneGradeApp/Tests/Tests.csproj -c Release --no-build
node --test "PhoneGradeApp/PhoneGrade.UI/wwwroot/tests/*.test.js"
```

The desktop app is Avalonia on .NET 8. The interactive test suite is a PWA served by
a local Kestrel host on port 5055, reached from the phone over a USB loopback tunnel
where the platform allows it and a secure tunnel where it does not. The suite speaks
Dutch and English, and follows the language the desktop passes it, so the phone and
the desk read the same words.
The desktop app is Avalonia on .NET 8. The interactive test suite is a PWA served by
a local Kestrel host on port 5055, reached from the phone over a USB loopback tunnel
where the platform allows it and a secure tunnel where it does not. The suite speaks
Dutch and English, and follows the language the desktop passes it, so the phone and
the desk read the same words.

The browser suite has its own screenshot tool, run by hand rather than as part of the
test suite, which renders the real windows to PNG through Avalonia headless with Skia
and seeds demo view models so screens show content without hardware attached:
The browser suite has its own screenshot tool, run by hand rather than as part of the
test suite, which renders the real windows to PNG through Avalonia headless with Skia
and seeds demo view models so screens show content without hardware attached:

```
dotnet run --project PhoneGradeApp/Tests -c Release --no-build -- <outputDir> [section] [en|nl]
```
```
dotnet run --project PhoneGradeApp/Tests -c Release --no-build -- <outputDir> [section] [en|nl]
```

`section` is one of `main`, `lic`, `flow`, `settings`, or omitted for all. It writes
about forty shots in both themes, at kiosk and narrow sizes, with `en` or `nl` as the
interface language.
`section` is one of `main`, `lic`, `flow`, `settings`, or omitted for all. It writes
about forty shots in both themes, at kiosk and narrow sizes, with `en` or `nl` as the
interface language.

## Licence