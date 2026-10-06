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

## Licence and terms
## Licence and terms

This project is published so a shop owner can read what the tool does with a serial
number rather than take a promise on trust. The licence is what makes that practical,
and it is set out in four documents.
This project is published so a shop owner can read what the tool does with a serial
number rather than take a promise on trust. The licence is what makes that practical,
and it is set out in four documents.

| Document | What it is |
| --- | --- |
| [LICENSE](LICENSE) | PolyForm Noncommercial 1.0.0. The licence the code is published under. Noncommercial use, change and distribution; commercial use is not covered by it. |
| [COMMERCIAL_EULA.md](COMMERCIAL_EULA.md) | What a paying business customer may and may not do, including the warranty, the liability limit and the end user licence agreement. |
| [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) | What you may and may not do with the PhoneGrade name and the logo. |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute, and the contributor licence agreement. |
| [NOTICE.md](NOTICE.md) | Who wrote it, and the attribution that has to stay with any copy. |
### Liability
### Liability

**PhoneGrade is provided as is, without warranty of any kind, to the fullest extent the
law allows.** The author makes no promise that it is free of defects, that it will be
available or uninterrupted, or that it is fit for any particular purpose.
**PhoneGrade is provided as is, without warranty of any kind, to the fullest extent the
law allows.** The author makes no promise that it is free of defects, that it will be
available or uninterrupted, or that it is fit for any particular purpose.

**To the fullest extent the law allows, the author is not liable for any damage arising
from the use of this software or from failing to use it.** That includes the device
tested and anything connected to it, lost profit, lost business, lost or corrupted
records, lost customers, regulatory penalties, and any claim by a customer or a third
party.
**To the fullest extent the law allows, the author is not liable for any damage arising
from the use of this software or from failing to use it.** That includes the device
tested and anything connected to it, lost profit, lost business, lost or corrupted
records, lost customers, regulatory penalties, and any claim by a customer or a third
party.

**For a paying customer, the author's total liability is limited to the amount that
customer actually paid for the subscription covering the twelve months in which the
claim arose.** That is the whole cap. Nothing excludes liability that cannot lawfully
be excluded, such as fraud or deliberate harm.
**For a paying customer, the author's total liability is limited to the amount that
customer actually paid for the subscription covering the twelve months in which the
claim arose.** That is the whole cap. Nothing excludes liability that cannot lawfully
be excluded, such as fraud or deliberate harm.

A grade produced by this tool is the result of automated checks and a published set of
rules. It is not a certification, not a valuation and not an inspection by a person. It
does not establish whether a device was stolen, opened or repaired, and it does not
establish what a customer will think of it. The decision to pay, accept or reject a
device on the basis of that output is yours alone.
A grade produced by this tool is the result of automated checks and a published set of
rules. It is not a certification, not a valuation and not an inspection by a person. It
does not establish whether a device was stolen, opened or repaired, and it does not
establish what a customer will think of it. The decision to pay, accept or reject a
device on the basis of that output is yours alone.

Nothing here is legal advice. Read the documents, and if any part of them is unclear or
you think is wrong, say so before you rely on it.
Nothing here is legal advice. Read the documents, and if any part of them is unclear or
you think is wrong, say so before you rely on it.
