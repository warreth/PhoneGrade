<div align="center">
  <img src="PhoneGradeApp/PhoneGrade.UI/Assets/logo.svg" width="120" height="120" alt="PhoneGrade Logo"/>
  <h1>PhoneGrade</h1>
  <p>Professional iPhone &amp; Android hardware inspection and grading system</p>
</div>

---

## Overview

PhoneGrade is a desktop application for hardware diagnostics and quality grading
of mobile devices. Plug a device into USB and it is detected within a couple of
seconds, read over `libimobiledevice` or `adb`, tested through an interactive
browser suite, and finished with a filled-in DYMO label.

**Key Features:**
- Automated iOS and Android device detection via USB
- Hardware diagnostics (battery health, activation status, component verification)
- Interactive PWA test suite (touchscreen, cameras, sensors, GPS, audio)
- Automatic grading engine with capability-aware penalties
- Dymo label generation for inventory management

## Installing

Grab the installer from the [Releases](https://github.com/warreth/PhoneGrade/releases)
page:

- **Windows**: `PhoneGrade-win-Setup.exe`. Velopack installer, no admin rights
  needed.
- **macOS**: `PhoneGrade-osx-arm64-*.pkg` (Apple Silicon) or
  `PhoneGrade-osx-x64-*.pkg` (Intel). Drag into Applications.
- **Linux**: `PhoneGrade-linux-x64.AppImage`. `chmod +x` and run.

The installers bundle the `libimobiledevice` and `adb` command line tools, so
there is nothing to set up yourself. Once installed the app updates itself:
on startup it checks the GitHub releases and installs a newer build silently.

## Requirements

- **DYMO Label** software, free from [dymo.com](https://www.dymo.com), to open
  and print the generated label. The app writes a `.dymo` file and hands it to
  the OS default handler, which is DYMO Label.
- .NET 8 Desktop Runtime. The installer downloads it if it is missing.
- On Windows, if a device never shows up, install iTunes from the Microsoft
  Store once. It ships the Apple Mobile Device USB driver.

**Mobile devices:** iOS 13+ or Android 8+, unlocked, with "Trust This
Computer" confirmed.

## Android Setup

1. Enable Developer Options (tap Build Number 7 times)
2. Enable USB Debugging in Developer Options
3. Select File Transfer (MTP) mode when connecting
4. Authorize the RSA fingerprint prompt on the device screen

If the prompt does not appear, reconnect the cable or press **Retry ADB
Detection** in the app.

## How it works

1. **Plug in**. The app notices the device within about two seconds and starts
   on its own (can be turned off in settings).
2. **Trust and activation**. Missing trust or activation gets detected and can
   be bypassed with `ideviceactivation activate -b`.
3. **Read**. Model, color, storage, IMEI or serial number, battery condition.
4. **Diagnose**. Panic logs (`panic-full-*.ips`) are pulled off the device and
   translated into plain language: what is broken and what to replace.
5. **Test**. Scan the QR code and run the interactive suite in the device
   browser.
6. **Label**. You only get asked for what is not set yet (quality, payment
   method, both can be defaulted), then the label opens in DYMO Label.

## PWA Test Suite

The interactive test suite runs in the device browser and verifies:
- Multi-touch digitizer response
- Display quality and dead pixels
- Front and rear cameras, with a live preview and photo review step
- Motion sensors (accelerometer, gyroscope)
- GPS location accuracy
- Audio (speakers, microphone, earpiece)
- Vibration motor

Results sync automatically to the desktop application with offline fallback.

### Grading penalty

A device that is missing a mandatory browser API cannot be graded A, because
grade A implies full function. A refused permission prompt is not the same
thing as a missing API, and does not cost the device a grade.

## Developing

```bash
dotnet build PhoneGradeApp/PhoneGrade.sln
dotnet test PhoneGradeApp/Tests/Tests.csproj
dotnet run --project PhoneGradeApp/PhoneGrade.UI
```

The PWA modules have their own suite:

```bash
node --test "PhoneGradeApp/PhoneGrade.UI/wwwroot/tests/*.test.js"
```

Without bundled tools the app looks for them on PATH (`brew install
libimobiledevice` on macOS).

## License

[AGPLv3](LICENSE)
