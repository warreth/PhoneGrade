<div align="center">
  <img src="PhoneGradeApp/PhoneGrade.UI/Assets/logo.svg" width="120" height="120" alt="PhoneGrade Logo"/>
  <h1>PhoneGrade</h1>
  <p>Professional iPhone & Android hardware inspection and grading system</p>
</div>
<div align="center">
  <img src="PhoneGradeApp/PhoneGrade.UI/Assets/logo.svg" width="120" height="120" alt="PhoneGrade Logo"/>
  <h1>PhoneGrade</h1>
  <p>Professional iPhone & Android hardware inspection and grading system</p>
</div>

---
---

## Overview
## Overview

PhoneGrade is a professional desktop application for comprehensive hardware diagnostics and quality grading of mobile devices. The system combines automated USB detection, diagnostic analysis, and an interactive PWA test suite for thorough device evaluation.
PhoneGrade is a professional desktop application for comprehensive hardware diagnostics and quality grading of mobile devices. The system combines automated USB detection, diagnostic analysis, and an interactive PWA test suite for thorough device evaluation.

**Key Features:**
- Automated iOS and Android device detection via USB
- Hardware diagnostics (battery health, activation status, component verification)
- Interactive PWA test suite (touchscreen, cameras, sensors, GPS, audio)
- Automatic grading engine with capability-aware penalties
- Dymo label generation for inventory management
**Key Features:**
- Automated iOS and Android device detection via USB
- Hardware diagnostics (battery health, activation status, component verification)
- Interactive PWA test suite (touchscreen, cameras, sensors, GPS, audio)
- Automatic grading engine with capability-aware penalties
- Dymo label generation for inventory management

## Requirements
## Requirements

**Desktop Application:**
- Windows 10/11 or macOS 11+
- .NET 8 Runtime
- libimobiledevice tools (iOS)
- Android Platform Tools (Android)
**Desktop Application:**
- Windows 10/11 or macOS 11+
- .NET 8 Runtime
- libimobiledevice tools (iOS)
- Android Platform Tools (Android)

**Mobile Devices:**
- iOS 13+ or Android 8+
- USB cable connection
- Device unlocked with "Trust This Computer" confirmed
**Mobile Devices:**
- iOS 13+ or Android 8+
- USB cable connection
- Device unlocked with "Trust This Computer" confirmed

## Android Setup
## Android Setup

1. Enable Developer Options (tap Build Number 7 times)
2. Enable USB Debugging in Developer Options
3. Select File Transfer (MTP) mode when connecting
4. Authorize RSA fingerprint prompt on device screen
1. Enable Developer Options (tap Build Number 7 times)
2. Enable USB Debugging in Developer Options
3. Select File Transfer (MTP) mode when connecting
4. Authorize RSA fingerprint prompt on device screen

## PWA Test Suite
## PWA Test Suite

The interactive test suite runs in the device browser and verifies:
- Touchscreen coverage, including the dead zones along the screen edges
- Display quality and dead pixels
- Front and rear cameras with live WebRTC preview
- Motion sensors (accelerometer, gyroscope)
- GPS location accuracy
- Audio (speakers, microphone, earpiece)
- Vibration motor
The interactive test suite runs in the device browser and verifies:
- Touchscreen coverage, including the dead zones along the screen edges
- Display quality and dead pixels
- Front and rear cameras with live WebRTC preview
- Motion sensors (accelerometer, gyroscope)
- GPS location accuracy
- Audio (speakers, microphone, earpiece)
- Vibration motor

Results sync automatically to the desktop application with offline fallback.
Results sync automatically to the desktop application with offline fallback.

The suite speaks Dutch and English. The desktop passes its own language on the
address the QR code opens, so the phone and the desk it is being graded on read
the same words; opened by hand the phone follows its own language settings, and
falls back to Dutch when neither carries a language this app has.
The suite speaks Dutch and English. The desktop passes its own language on the
address the QR code opens, so the phone and the desk it is being graded on read
the same words; opened by hand the phone follows its own language settings, and
falls back to Dutch when neither carries a language this app has.

## Free Tier and Pro
## Free Tier and Pro

The desktop application includes ten free scans. The counter is kept encrypted in
the local settings directory, and a scan is refused once the ten are spent until a
Pro license is activated. Activation validates the key against Lemon Squeezy and
only accepts keys sold for this product; the pricing page behind the application
links is the single source for what the paid plan costs.
The desktop application includes ten free scans. The counter is kept encrypted in
the local settings directory, and a scan is refused once the ten are spent until a
Pro license is activated. Activation validates the key against Lemon Squeezy and
only accepts keys sold for this product; the pricing page behind the application
links is the single source for what the paid plan costs.

## License