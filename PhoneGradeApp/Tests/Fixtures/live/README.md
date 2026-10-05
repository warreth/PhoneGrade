# Device captures

Recorded output from two handsets, used to prove the readers still understand
what phones actually print. Re-recorded with:

    PHONEGRADE_CAPTURE=1 dotnet test PhoneGradeApp/Tests/Tests.csproj \
        --filter FullyQualifiedName~DeviceCaptureTests

Each capture is written the way `ToolRunner.RunAsync` hands a command's output
to the parser, so the parsers are exercised on the same string they see on the
bench. A refused libimobiledevice query returns its plist with the warning
appended, and the captures keep both halves. An adb capture keeps stdout alone
and records the exit code and stderr beside it, because "the handset has no such
file" and "the handset would not tell us" are different answers.

## The identifiers are substituted

Serial numbers, IMEIs, MAC addresses and the device name were replaced before
the captures were committed. Each replacement keeps the shape the parser reads:
fifteen digits for an IMEI, seventeen characters for a board serial, six colon
separated pairs for a MAC. A substituted value is still a value the parser has to
get right, which is what makes the capture worth keeping.

Capturing from a handset writes the real values. Scrub them before committing.

## Handsets

`iphone8-` is an iPhone 8 (`iPhone10,1`) on iOS 16.7.10. It is on iOS 15 or
later, which is the point of it: the three component domains it answers with an
empty property list and a warning, so the captures show a phone refusing a
question rather than a phone with nothing to report.

`honor-x8b-` is a Honor X8b (`LLY-LX1EEA`) on Android 14. It refuses the battery
counters under `/sys`, refuses the Wi-Fi MAC file, and refuses the secure settings
provider, so its captures carry several refusals rather than several values.