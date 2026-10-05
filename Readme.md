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

## Reading a phone
## Reading a phone

The desktop reads what the handset will tell it and nothing more. Where a phone
refuses a question, the report says the value was withheld instead of showing an
empty field, because a phone that withholds a value and a phone that has none
should not look the same on a grading sheet.
The desktop reads what the handset will tell it and nothing more. Where a phone
refuses a question, the report says the value was withheld instead of showing an
empty field, because a phone that withholds a value and a phone that has none
should not look the same on a grading sheet.

On iOS 15 and later the domains that carry component serials answer with an empty
property list, so the per-part audit on a recent iPhone reports what it can and
leaves the rest unknown. The battery figures still come through, as do the model,
the storage, the IMEI and the board serial.
On iOS 15 and later the domains that carry component serials answer with an empty
property list, so the per-part audit on a recent iPhone reports what it can and
leaves the rest unknown. The battery figures still come through, as do the model,
the storage, the IMEI and the board serial.

Android exposes no per-part serials at all, so its audit checks what a handset
always publishes: whether the bootloader is locked, whether the vbmeta partition
is signed, whether the build carries the manufacturer's signing tag, and the
warranty fuse where the manufacturer has one. A phone whose bootloader is open
is reported as not in factory state.
Android exposes no per-part serials at all, so its audit checks what a handset
always publishes: whether the bootloader is locked, whether the vbmeta partition
is signed, whether the build carries the manufacturer's signing tag, and the
warranty fuse where the manufacturer has one. A phone whose bootloader is open
is reported as not in factory state.

## Running the tests
## Running the tests

    dotnet test PhoneGradeApp/Tests/Tests.csproj
    dotnet test PhoneGradeApp/Tests/Tests.csproj

The parsers are tested against recorded output from real handsets, kept in
`PhoneGradeApp/Tests/Fixtures/live`, because a handset answers in ways sample
output written by hand does not: an iPhone that refuses a domain and returns a
warning on stderr, an Android build that refuses its own battery counters. The
identifiers in those captures are substituted, and the shape each one keeps is
what the parsers are checked against. Re-record them with:
The parsers are tested against recorded output from real handsets, kept in
`PhoneGradeApp/Tests/Fixtures/live`, because a handset answers in ways sample
output written by hand does not: an iPhone that refuses a domain and returns a
warning on stderr, an Android build that refuses its own battery counters. The
identifiers in those captures are substituted, and the shape each one keeps is
what the parsers are checked against. Re-record them with:

    PHONEGRADE_CAPTURE=1 dotnet test PhoneGradeApp/Tests/Tests.csproj \
        --filter FullyQualifiedName~DeviceCaptureTests
    PHONEGRADE_CAPTURE=1 dotnet test PhoneGradeApp/Tests/Tests.csproj \
        --filter FullyQualifiedName~DeviceCaptureTests

The browser suite has its own runner:
The browser suite has its own runner:

    node --test "PhoneGradeApp/PhoneGrade.UI/wwwroot/tests/*.test.js"
    node --test "PhoneGradeApp/PhoneGrade.UI/wwwroot/tests/*.test.js"

Most of the suite runs offline. A few tests talk to the real imei.info gateway
and need a key of their own: CI reads `IMEI_INFO_API_KEY` from the repository
secrets, a local run reads the same variable or, failing that,
`PhoneGradeApp/Tests/.imei-info-key`, which git ignores. Without one, those
tests report themselves as skipped.
Most of the suite runs offline. A few tests talk to the real imei.info gateway
and need a key of their own: CI reads `IMEI_INFO_API_KEY` from the repository
secrets, a local run reads the same variable or, failing that,
`PhoneGradeApp/Tests/.imei-info-key`, which git ignores. Without one, those
tests report themselves as skipped.

An account without credit has every check refused with `Request is too
expensive.`, so the live tests expect that wording rather than a paid answer,
and the one test that needs credit to say anything useful skips with the
reason until the account carries some.
An account without credit has every check refused with `Request is too
expensive.`, so the live tests expect that wording rather than a paid answer,
and the one test that needs credit to say anything useful skips with the
reason until the account carries some.

The sandbox IMEI numbers imei.info publishes for integration testing get their
own tests as well. They run against a small server inside the test project that
answers the way imei.info answers: fixed device data for the three published
numbers, HTTP 402 for every other one, the gateway's own words when the key is
not the one it knows, and the account balance behind the account route. That
keeps the contract covered on a machine with an empty account balance.
The sandbox IMEI numbers imei.info publishes for integration testing get their
own tests as well. They run against a small server inside the test project that
answers the way imei.info answers: fixed device data for the three published
numbers, HTTP 402 for every other one, the gateway's own words when the key is
not the one it knows, and the account balance behind the account route. That
keeps the contract covered on a machine with an empty account balance.

## License