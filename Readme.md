<div align="center">
  <img src="./PhoneGradeApp/PhoneGrade.UI/Assets/app-icon.svg" width="100" height="100" alt="PhoneGrade Logo">
  <h1>PhoneGrade</h1>
  <p>Automated iOS and Android hardware testing and label generation.</p>
</div>

![platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue)

PhoneGrade reads device hardware data via USB, runs diagnostic checks, and automatically guides you through interactive PWA hardware tests (touch, cameras, sensors). Once complete, it outputs a ready-to-print DYMO label.

## Installing

Grab the installer from the [Releases](https://github.com/warreth/PhoneGrade/releases) page:

- **Windows**: `PhoneGrade-win-Setup.exe` (No admin rights needed)
- **macOS**: `PhoneGrade-osx-arm64-*.pkg` (Apple Silicon) or `PhoneGrade-osx-x64-*.pkg` (Intel)
- **Linux**: `PhoneGrade-linux-*.AppImage`

All required CLI tools (`libimobiledevice`, `adb`) are bundled automatically.

## Usage

1. Launch the PhoneGrade application.
2. Connect a device via USB (Unlock and "Trust" if prompted).
3. Follow the on-screen kiosk steps and scan the QR code to run interactive browser tests.
4. Set the Quality grade to print the label.
