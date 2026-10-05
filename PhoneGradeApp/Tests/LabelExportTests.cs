using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using Xunit;

namespace Tests;

// ============ Writing the files an inspection leaves behind ============
//
// The label is the one file an operator puts on a device and cannot take back, and
// it had three separate writers that each decided on their own what the battery
// percentage looked like and where the file went. Everything below is written
// against the one writer, and every test asserts on a file that was really
// written rather than on a return value.

public class LabelExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}");
    private readonly string _template;

    public LabelExportTests()
    {
        Directory.CreateDirectory(_folder);

        // A template of the shape the shipped one is: a tag, a text body with
        // sentinels in it, and a barcode body. The sentinels sit in element
        // content, never in an attribute, because that is where the real template
        // puts them.
        _template = Path.Combine(_folder, "test.dymo");
        File.WriteAllText(_template, """
            <?xml version="1.0" encoding="utf-8"?>
            <DesktopLabel Version="1">
              <DYMOLabel Version="3">
                <LabelObjects>
                  <TextObject>
                    <Name>TEKST_1</Name>
                    <Text>MODEL STORAGE PCOLOR QUALITY BATTERY PAYM</Text>
                  </TextObject>
                  <BarcodeObject>
                    <Name>STREEPJESCODE_1</Name>
                    <Data><DataString>IDENTIFIER</DataString></Data>
                  </BarcodeObject>
                </LabelObjects>
              </DYMOLabel>
            </DesktopLabel>
            """);
    }

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809",
        Model = "13 Pro",
        Color = "Wit",
        Storage = "256GB",
        Memory = "6GB",
        BatteryHealth = "90",
        Quality = "A",
        PayMethod = "Marge",
    };

    [Fact]
    public async Task OneClickWritesTheLabelAndTheNumbers_SoAnOperatorNeverNeedsTwoButtons()
    {
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            ExportService.DefaultSet, _folder, "one-click"));

        Assert.True(batch.Succeeded, batch.Failures.FirstOrDefault()?.Problem);
        Assert.Equal(3, batch.Files.Count);

        // Every file shares the stem, so the three of them are visibly one
        // inspection rather than three unrelated downloads.
        Assert.Equal(
            ["one-click.dymo", "one-click-label.pdf", "one-click.json"],
            batch.Files.Where(file => file.Succeeded).Select(file => Path.GetFileName(file.Path!)));

        foreach (LabelWriter.Outcome file in batch.Files)
            Assert.True(File.Exists(file.Path!), $"{file.Format} was reported written but is not there");
    }

    [Fact]
    public async Task AskingForNothingWritesNothing_AndSaysSoRatherThanPretending()
    {
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(),
            new LabelWriter.Request([], _folder, "empty"));

        Assert.Empty(batch.Files);
        Assert.True(batch.Succeeded);
        Assert.Empty(Directory.GetFiles(_folder, "empty*"));
    }

    [Fact]
    public async Task TheLabelAndTheReportDoNotOverwriteEachOther()
    {
        // Both are .pdf and both belong to the same device. One of them landing on
        // top of the other is how a receipt sized label ends up filed as the
        // inspection record.
        await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.LabelPdf, ExportFormat.ReportPdf], _folder, "device-1"));

        Assert.True(File.Exists(Path.Combine(_folder, "device-1-label.pdf")));
        Assert.True(File.Exists(Path.Combine(_folder, "device-1-report.pdf")));
        Assert.NotEqual(
            new FileInfo(Path.Combine(_folder, "device-1-label.pdf")).Length,
            new FileInfo(Path.Combine(_folder, "device-1-report.pdf")).Length);
    }

    [Fact]
    public async Task OneFormatFailingLeavesTheOthersWritten()
    {
        // A template that cannot be filled is a failure of the label alone. If it
        // took the JSON down with it the operator would lose the audit record of a
        // device they have already sold.
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel, ExportFormat.Json], _folder, "partial",
            TemplatePath: Path.Combine(_folder, "absent.dymo")));

        Assert.True(batch.Files.Single(f => f.Format == ExportFormat.Json).Succeeded);
        Assert.False(batch.Files.Single(f => f.Format == ExportFormat.DymoLabel).Succeeded);
        Assert.True(File.Exists(Path.Combine(_folder, "partial.json")));
    }

    [Fact]
    public async Task EveryFormatIsStillAttemptedAfterOneHasFailed()
    {
        // All five asked for, the label doomed by a template that is not there.
        // The count of what was reported has to be the count of what was asked
        // for, or the operator cannot tell which file is missing.
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            ExportService.All, _folder, "all-of-them",
            TemplatePath: Path.Combine(_folder, "absent.dymo")));

        Assert.Equal(ExportService.All.Count, batch.Files.Count);
        Assert.Equal(1, batch.Failures.Count());
    }

    [Fact]
    public async Task TheFolderIsCreatedRatherThanAssumedToExist()
    {
        string nested = Path.Combine(_folder, "today", "batch-1");
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(),
            LabelWriter.Request.One(ExportFormat.Json, nested, "made"));

        Assert.True(batch.Succeeded);
        Assert.True(Directory.Exists(nested));
    }

    // ---- the .dymo file ----

    [Fact]
    public async Task TheDymoFileCarriesEveryValue_AndReadsBackAsXml()
    {
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "dymo", TemplatePath: _template));

        string path = batch.Files.Single().Path!;
        string text = File.ReadAllText(path);

        Assert.Contains("13 Pro 256GB Wit A 90% Marge", text);
        Assert.Contains("Wit", text);
        Assert.Contains("QUALITY".Replace("QUALITY", "A"), text);

        // The reason this is a substitution on the file's bytes rather than a
        // document round trip: DYMO's deserializer rejects the empty element
        // shorthand an XML writer produces.
        Assert.DoesNotContain("/>", text);
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(text));
    }

    [Fact]
    public async Task AValueWithMarkupInItDoesNotBreakTheFile()
    {
        // A model called "iPhone 15 & 16" substituted raw into the XML produces a
        // file the service rejects, with a line number that points at nothing.
        DeviceData phone = Phone();
        phone.Model = "iPhone 15 & 16 <Pro>";

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone, new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "escaped", TemplatePath: _template));

        string path = batch.Files.Single().Path!;
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(File.ReadAllText(path)));
        Assert.Contains("iPhone 15 &amp; 16", File.ReadAllText(path));
    }

    [Fact]
    public async Task ATemplateWithNothingToFillIsRefused_NotSilentlyPrinted()
    {
        // A template saved out of DYMO with the values already merged in would
        // otherwise print the last device inspected on every device after it.
        string merged = Path.Combine(_folder, "merged.dymo");
        File.WriteAllText(merged, """
            <?xml version="1.0" encoding="utf-8"?>
            <DesktopLabel Version="1">
              <DYMOLabel Version="3">
                <TextObject><Name>T</Name><Text>356938035643809 13 Pro 256GB</Text></TextObject>
              </DYMOLabel>
            </DesktopLabel>
            """);

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "merged", TemplatePath: merged));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.False(label.Succeeded);
        Assert.Contains("none of the fields", label.Problem);
        Assert.Null(label.Path);
    }

    [Fact]
    public async Task ATemplateThatAsksForSomethingWeDoNotFillSaysSoWithoutRefusingToPrint()
    {
        // A shop's own template carrying its company name is normal. Refusing to
        // print it would be worse than reporting it.
        string own = Path.Combine(_folder, "own.dymo");
        File.WriteAllText(own, """
            <?xml version="1.0" encoding="utf-8"?>
            <DesktopLabel Version="1"><DYMOLabel Version="3">
              <TextObject><Name>T</Name><Text>COMPANY MODEL</Text></TextObject>
            </DYMOLabel></DesktopLabel>
            """);

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "own", TemplatePath: own));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.True(label.Succeeded, label.Problem);
        Assert.Contains("COMPANY", label.Note);
        Assert.True(File.Exists(label.Path!));
    }

    [Fact]
    public async Task AMissingTemplateNamesTheSettingRatherThanOnlyTheInstallFolder()
    {
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "missing",
            TemplatePath: Path.Combine(_folder, "gone.dymo")));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.False(label.Succeeded);
        Assert.Contains("Settings", label.Problem);
    }

    // ---- the JSON ----

    [Fact]
    public async Task TheJsonCarriesEverySection_AndSaysWhichVersionItIs()
    {
        DeviceData phone = Phone();
        phone.ComponentChecks = [new ComponentStatus
        {
            Name = "Scherm",
            SerialRead = "C3X9P2LM4K1Q",
            SerialOriginal = "C3X9P2LM4K8Z",
            Status = ComponentStatusType.Mismatch,
        }];
        phone.InteractiveTests = new InteractiveTestSuiteResult
        {
            SessionId = "SESSION",
            Tests =
            [
                new InteractiveTestResult { Id = "touch", Name = "Touchscreen", Status = TestStatus.Failed },
            ],
        };
        phone.WithheldReads.Add("battery temperature");

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone,
            LabelWriter.Request.One(ExportFormat.Json, _folder, "json"));

        using var document = JsonDocument.Parse(File.ReadAllText(batch.Files.Single().Path!));
        var root = document.RootElement;

        Assert.Equal(DeviceReportJson.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());

        var device = root.GetProperty("device");
        Assert.Equal("356938035643809", device.GetProperty("identifier").GetString());
        Assert.Equal("256GB", device.GetProperty("storage").GetString());
        Assert.Equal("Mismatch", device.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.Equal("Failed", device.GetProperty("tests")[0].GetProperty("status").GetString());

        // Absent rather than an empty list would read as a version of the app that
        // did not record it at all.
        Assert.Equal("battery temperature", device.GetProperty("notRead")[0].GetString());
    }

    [Fact]
    public async Task TheJsonFieldNamesDoNotFollowTheCodeNames()
    {
        // DeviceData says Imei2 and FmiVerificationSource. The export says imei2
        // and fmiVerificationSource, and that is the contract a reader is written
        // against. A C# rename must not reach it.
        DeviceData phone = Phone();
        phone.Imei2 = "356938035643810";
        phone.FmiVerificationSource = "Via Server";

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone,
            LabelWriter.Request.One(ExportFormat.Json, _folder, "names"));

        using var document = JsonDocument.Parse(File.ReadAllText(batch.Files.Single().Path!));
        var device = document.RootElement.GetProperty("device");
        Assert.Equal("356938035643810", device.GetProperty("network").GetProperty("imei2").GetString());
        Assert.Equal("Via Server", device.GetProperty("fmiVerificationSource").GetString());
    }

    [Fact]
    public async Task TwoExportsOfTheSameDeviceDifferOnlyInTheTimestamp()
    {
        // A report gets archived and diffed. Everything that is not the moment of
        // export has to be identical, or every diff is noise.
        LabelWriter.Batch first = await LabelWriter.WriteAsync(Phone(),
            LabelWriter.Request.One(ExportFormat.Json, _folder, "diff-1"));
        LabelWriter.Batch second = await LabelWriter.WriteAsync(Phone(),
            LabelWriter.Request.One(ExportFormat.Json, _folder, "diff-2"));

        Assert.Equal(
            Without(File.ReadAllText(first.Files.Single().Path!)),
            Without(File.ReadAllText(second.Files.Single().Path!)));

        static string Without(string json) =>
            string.Join('\n', json.Split('\n')
                .Where(line => !line.Contains("exportedAt", StringComparison.Ordinal)));
    }

    // ---- the CSV ----

    [Fact]
    public async Task TheCsvQuotesAValueCarryingAComma_SoTheRowKeepsThreeColumns()
    {
        DeviceData phone = Phone();
        phone.ComponentChecks =
        [
            new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch, Description = "Left, top corner" },
        ];

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone,
            LabelWriter.Request.One(ExportFormat.Csv, _folder, "csv"));

        foreach (string[] row in Parse(File.ReadAllText(batch.Files.Single().Path!)))
            Assert.True(row.Length == 3,
                $"a row has {row.Length} columns instead of 3, so the note was split across them: {string.Join(" | ", row)}");

        string[] check = Parse(File.ReadAllText(batch.Files.Single().Path!))
            .Single(row => row[0] == "Check");
        Assert.Contains("Left, top corner", check[2]);
    }

    [Fact]
    public async Task TheCsvEscapesAQuoteInsideAValue_SoItDoesNotEndTheField()
    {
        DeviceData phone = Phone();
        phone.ComponentChecks =
        [
            new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch, Description = "Said \"mint\"" },
        ];

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone,
            LabelWriter.Request.One(ExportFormat.Csv, _folder, "quote"));

        string[] check = Parse(File.ReadAllText(batch.Files.Single().Path!))
            .Single(row => row[0] == "Check");

        // Read back rather than matched against the raw bytes: a doubled quote is
        // what the bytes should hold, but what matters is that a spreadsheet reads
        // the note as one cell with the quote in it.
        Assert.Contains("Said \"mint\"", check[2]);
    }

    [Fact]
    public async Task EveryRowOfTheCsvHasTheSameNumberOfColumns()
    {
        DeviceData phone = Phone();
        phone.ComponentChecks =
        [
            new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch, Description = "Left, top corner" },
            new ComponentStatus { Name = "Batterij", SerialRead = "A,B", Status = ComponentStatusType.Match },
        ];
        phone.InteractiveTests = new InteractiveTestSuiteResult
        {
            Tests = [new InteractiveTestResult { Name = "Touchscreen", Status = TestStatus.Failed, Notes = "a,b" }],
        };

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(phone,
            LabelWriter.Request.One(ExportFormat.Csv, _folder, "columns"));

        List<string[]> rows = Parse(File.ReadAllText(batch.Files.Single().Path!)).ToList();
        Assert.Equal(3, rows[0].Length);
        foreach (string[] row in rows)
            Assert.Equal(3, row.Length);
    }

    /// <summary>
    /// Reads a CSV the way a spreadsheet does: quoted fields hold whatever they
    /// hold, doubled quotes are one quote, and a comma inside quotes is not a
    /// separator. Written out here because the thing under test is that the writer
    /// agrees with this reader.
    /// </summary>
    private static IEnumerable<string[]> Parse(string csv)
    {
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char character = csv[i];

            if (quoted)
            {
                if (character != '"') { field.Append(character); continue; }

                // A doubled quote inside a quoted field is one quote.
                if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; continue; }

                quoted = false;
                continue;
            }

            switch (character)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Any(cell => cell.Length > 0)) yield return [.. row];
                    row.Clear();
                    break;
                default: field.Append(character); break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return [.. row];
        }
    }

    // ---- the file names ----

    [Fact]
    public void TheFileNameCarriesTheIdentifierAndTheMoment_SoTwoDevicesNeverCollide()
    {
        string stem = LabelWriter.FileStem(Phone(), new DateTime(2026, 3, 4, 15, 6, 7));
        Assert.Equal("356938035643809-20260304-150607", stem);
    }

    [Fact]
    public void AFileNameWithASlashInItCannotBecomeADirectory()
    {
        DeviceData phone = Phone();
        phone.Identifier = "abc/def";

        // The stem ends up in a path and in a download name. A slash in it would
        // be a directory change in one and nothing at all in the other.
        Assert.DoesNotContain('/', LabelWriter.FileStem(phone));
        Assert.DoesNotContain('\\', LabelWriter.FileStem(phone));
    }

    [Fact]
    public void ADeviceWithNoIdentifierIsStillNamedSomething()
    {
        Assert.StartsWith("unknown-", LabelWriter.FileStem(new DeviceData()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        // The template is passed in per call now, so there is nothing to reset.
    }
}

// ============ What goes on the label ============
//
// One read of the device, decided once. The preview in the export panel, the .dymo
// file and the label PDF all call this, and the test below is what stops them
// drifting apart.

public class LabelFieldsTests
{
    [Fact]
    public void ABatteryUnderTheThresholdCarriesTheMarker()
        => Assert.Equal("68% [X]", LabelFields.From(new DeviceData { BatteryHealth = "68" }).Battery);

    [Fact]
    public void ABatteryAtTheThresholdDoesNot()
    {
        // 85 percent is the line. Rounding it either side is how a battery that
        // passes gets marked and a battery that fails does not.
        Assert.Equal("85%", LabelFields.From(new DeviceData { BatteryHealth = "85" }).Battery);
        Assert.Equal("84% [X]", LabelFields.From(new DeviceData { BatteryHealth = "84" }).Battery);
    }

    [Fact]
    public void APercentageAlreadyCarryingItsSignIsNotGivenASecondOne()
        => Assert.Equal("92%", LabelFields.From(new DeviceData { BatteryHealth = "92%" }).Battery);

    [Fact]
    public void AStatusWordGetsNoPercentSign()
    {
        // Android reports a word when the capacity counters are unreadable.
        // "Good%" on a label is nonsense.
        LabelFields fields = LabelFields.From(new DeviceData { BatteryHealth = "Good" });
        Assert.Equal("Good", fields.Battery);
        Assert.False(fields.BatteryIsLow);
    }

    [Fact]
    public void NoBatteryDataStaysAsItArrived()
        => Assert.Equal("NOBATT", LabelFields.From(new DeviceData()).Battery);

    [Fact]
    public void TheMarkerIsNotAppliedWhenTheShopTurnedItOff()
    {
        // A shop that grades every battery as it stands should not get a marker
        // it cannot turn off.
        LabelFields fields = LabelFields.From(new DeviceData { BatteryHealth = "60" }, flagLowBattery: false);
        Assert.Equal("60%", fields.Battery);

        // Still known to be low, because the report says so either way.
        Assert.True(fields.BatteryIsLow);
    }

    [Fact]
    public void AValueThePhoneNeverSentBecomesItsPlaceholder()
    {
        // An empty field on a label is a question the operator has to answer at
        // the till. The placeholder says what is missing.
        LabelFields fields = LabelFields.From(new DeviceData { Model = "  " });
        Assert.Equal(DevicePlaceholders.Model, fields.Model);
        Assert.False(fields.IsComplete);
    }

    [Fact]
    public void AnInspectionWithEverythingOnItIsComplete()
    {
        Assert.True(LabelFields.From(new DeviceData
        {
            Identifier = "1", Model = "13 Pro", Storage = "256GB", Color = "Wit",
            Quality = "A", PayMethod = "Marge", BatteryHealth = "90",
        }).IsComplete);
    }
}

// ============ Filling the template ============

public class DymoTemplateTests
{
    private const string Template =
        "<DesktopLabel Version=\"1\"><DYMOLabel Version=\"3\">"
        + "<TextObject><Name>T</Name><Text>MODEL STORAGE PCOLOR QUALITY BATTERY PAYM</Text></TextObject>"
        + "<BarcodeObject><Name>B</Name><Data><DataString>IDENTIFIER</DataString></Data></BarcodeObject>"
        + "</DYMOLabel></DesktopLabel>";

    private static LabelFields Full() => new()
    {
        Battery = "90%",
        Grade = "A",
        Identifier = "356938035643809",
        Color = "Wit",
        Memory = "6GB",
        Model = "13 Pro",
        PayMethod = "Marge",
        Storage = "256GB",
    };

    [Fact]
    public void EveryFieldIsFilled()
    {
        string filled = DymoTemplate.Fill(Template, Full()).Text;
        Assert.Contains("13 Pro 256GB Wit A 90% Marge", filled);
        Assert.Contains("356938035643809", filled);
        Assert.DoesNotContain("MODEL", filled);
    }

    [Fact]
    public void TheTagNamesAreNotMistakenForFields()
    {
        // <LabelName> and <TextPosition> are upper case and are not values. Filling
        // one of them would put a phone's colour where a tag name belongs.
        string filled = DymoTemplate.Fill(Template, Full()).Text;
        Assert.Contains("<DesktopLabel", filled);
        Assert.Contains("<DYMOLabel", filled);
    }

    [Fact]
    public void ASubstitutedValueIsNotFilledAgain()
    {
        // The substitution walks the template once and writes into a separate
        // buffer, so a model that happens to spell a field name comes out as
        // typed rather than as whatever belongs in that field.
        string filled = DymoTemplate.Fill(Template, Full() with { Model = "MODEL" }).Text;

        Assert.Contains("MODEL 256GB", filled);
        Assert.Equal(1, filled.Split("MODEL").Length - 1);
    }

    [Fact]
    public void AnEmptyValueIsEscapedToNothingRatherThanToTheWordNull()
        => Assert.DoesNotContain("null", DymoTemplate.Fill(Template, Full() with { Model = "" }).Text);

    [Fact]
    public void ATemplateWithNoFieldsIsRefused()
    {
        string plain = "<TextObject><Text>13 Pro 256GB</Text></TextObject>";
        Assert.Throws<InvalidDataException>(() => DymoTemplate.Fill(plain, Full()));
    }

    [Fact]
    public void TheUnfilledFieldsAreNamed()
    {
        string own = Template.Replace("MODEL STORAGE", "COMPANY MODEL");
        Assert.Equal(["COMPANY"], DymoTemplate.Fill(own, Full()).UnfilledFields);
    }

    [Fact]
    public void TheGeometryOfTheTemplateIsNotReportedAsUnfilledFields()
    {
        // A label is mostly geometry. Reading it as fields produces a note listing
        // coordinates and object names, always long, which is a note an operator
        // learns to skip, and the one time it mattered it is skipped too.
     string shipped = DymoTemplateFiles.Read(DymoTemplateFiles.Shipped);

        Assert.Empty(DymoTemplate.Fill(shipped, Full()).UnfilledFields);
    }

    [Fact]
    public void ATemplateThatIsNotXmlReportsNothingRatherThanGuessing()
    {
        // It cannot be filled either, and the fill is about to say so.
        Assert.Empty(DymoTemplate.UnknownFields("MODEL and COMPANY, no markup at all"));
    }
}

// ============ The barcode ============

public class LabelBarcodeTests
{
    [Fact]
    public void APlainSerialIsWrappedInTheStartAndStopMarkers()
    {
        // A barcode without them reads back as one long run with no end, which is
        // a serial number that scans as the wrong device.
        Assert.Equal("*356938035643809*", LabelBarcode.Encode("356938035643809", out _));
        Assert.True(LabelBarcode.IsEncodable("356938035643809"));
    }

    [Fact]
    public void ASerialCode39CannotCarryHasItsBarcodeChanged_AndSaysSo()
    {
        string? encoded = LabelBarcode.Encode("ABC_123", out string? reason);

        Assert.Equal("*ABC-123*", encoded);
        Assert.NotNull(reason);
        Assert.Contains("dash", reason);
    }

    [Fact]
    public void ASerialThatOnlyNeedsMarkersIsStillExplained()
    {
        LabelBarcode.Encode("356938035643809", out string? reason);
        Assert.Null(reason);
    }

    [Fact]
    public void AnIdentifierThatIsAlreadyWrappedIsNotWrappedTwice()
    {
        Assert.Equal("*356938035643809*", LabelBarcode.Encode("*356938035643809*", out _));
    }

    [Fact]
    public void NothingEncodableProducesNoBarcodeAtAll()
    {
        // A barcode drawn from a placeholder would scan as NOID on every device.
        Assert.Null(LabelBarcode.Encode(DevicePlaceholders.Identifier, out _));
        Assert.Null(LabelBarcode.Encode("", out _));
    }

    [Fact]
    public void EveryEncodableCharacterSurvivesTheAlphabet()
    {
     // One wrong row in the Code39 table means one character that scans as
        // another, and the table is the kind of thing edited by hand. The
        // alphabet is read out of the table rather than copied here.
        //
        // Two characters are left out, because neither is a device on its own and
        // neither means anything wrapped round it: a space is not a serial
        // number, and the asterisk is the marker the wrapping is made of, so an
        // identifier of one asterisk is already wrapped.
   foreach (char character in Code39.Characters.Where(c => c is not (' ' or '*')))
  Assert.Equal($"*{character}*", LabelBarcode.Encode(character.ToString(), out _));
    }

    [Fact]
    public void AnIdentifierOfNothingButSpacesIsNotABarcode()
    {
     // For the same reason as the placeholder: it is not a device.
        Assert.Null(LabelBarcode.Encode("   ", out _));
    }
}

// ============ Code39 itself ============

public class Code39Tests
{
    /// <summary>
    /// Every character the table claims to carry, read out of the table rather
    /// than out of a copy of the alphabet kept here. A second copy is how the
    /// table and its alphabet drift apart, and the symptom is a character that
 /// encodes as another one.
 /// </summary>
    private static string Every => Code39.Characters;

    [Fact]
    public void EveryCharacterHasAPatternOfNineElements()
    {
        foreach (char character in Every)
        {
            var pattern = Code39.Encode(character.ToString()).Single();
            Assert.Equal(9, pattern.Length);
        }
    }

    [Fact]
    public void EveryPatternHasExactlyThreeWideElements()
    {
        // Three wide out of nine, alternating bar and space, is what makes this
        // Code39 rather than any other nine element symbology, and it is the
        // property a decoder counts.
        foreach (char character in Every)
        {
            var pattern = Code39.Encode(character.ToString()).Single();
            Assert.Equal(3, pattern.Count(element => element == 'W'));
        }
    }

    [Fact]
    public void EveryPatternIsDistinctFromEveryOther()
    {
        // Two characters sharing a pattern are two characters a scanner cannot
        // tell apart, which is the whole failure mode of a hand written table.
        var patterns = Every.Select(character => Code39.Encode(character.ToString()).Single()).ToList();
        Assert.Equal(patterns.Count, patterns.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheStartAndStopMarkerIsTheSameAsteriskOnBothSides()
    {
        // A barcode whose opening and closing markers differ is one a scanner
        // either refuses or reads to the wrong length.
        Assert.Equal(
            Code39.Encode("*").Single(),
            Code39.Encode("*").Single());
    }

    [Fact]
    public void EveryElementIsEitherWideOrNarrow()
    {
        // A typo in the table, a stray character, anything but W and N.
        foreach (char character in Every)
        {
            var pattern = Code39.Encode(character.ToString()).Single();
            Assert.All(pattern, element => Assert.True(element is 'W' or 'N',
                $"the pattern for {character} holds {element}, which is neither wide nor narrow"));
        }
    }

    [Fact]
    public void ACharacterOutsideTheAlphabetIsRefusedByName()
    {
        var error = Assert.Throws<ArgumentException>(() => Code39.Encode("*abc*").ToList());
        Assert.Contains("a", error.Message);
    }

    [Fact]
    public void CanEncodeAgreesWithEncode()
    {
        Assert.True(Code39.CanEncode("*ABC-123*"));
        Assert.False(Code39.CanEncode("*abc*"));
    }
}

// ============ Telling the operator what happened ============

public class ExportReportingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}");

    public ExportReportingTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void AFileThatWentWhereItWasSaidIsNamedRatherThanPathed()
    {
        // An operator who cannot find the file cannot hand it over.
        string message = ExportService.Describe(
            new LabelWriter.Outcome(ExportFormat.Json, Path.Combine(_folder, "a.json"), true, null), _folder);

        Assert.Contains("a.json", message);
        Assert.DoesNotContain(_folder, message);
    }

    [Fact]
    public void AFileThatWentSomewhereElseIsNamedInFull()
    {
        string elsewhere = Path.Combine(_folder, "sub");
        string message = ExportService.Describe(
            new LabelWriter.Outcome(ExportFormat.Json, Path.Combine(elsewhere, "b.json"), true, null), _folder);

        Assert.Contains(elsewhere, message);
    }

    [Fact]
    public void AFailureIsNamedAsAFailure_WithTheReason()
    {
        string message = ExportService.Describe(
            new LabelWriter.Outcome(ExportFormat.DymoLabel, null, false, "The template is not there."), _folder);

        Assert.Contains("failed", message);
        Assert.Contains("not there", message);
    }

    [Fact]
    public void ANoteIsCarriedOnRatherThanDropped()
    {
        // The template asking for a field nothing fills is not a failure, but the
        // operator still has to be told before a label goes out with it on.
        string message = ExportService.Describe(new LabelWriter.Outcome(
            ExportFormat.DymoLabel, Path.Combine(_folder, "c.dymo"), true, null, "It also asks for COMPANY."), _folder);

        Assert.Contains("COMPANY", message);
    }

    [Fact]
    public void TheTwoPdfsHaveDifferentNamesAndTheRestMatchTheirFormat()
    {
        Assert.Equal("-label.pdf", ExportFormat.LabelPdf.FileSuffix());
        Assert.Equal("-report.pdf", ExportFormat.ReportPdf.FileSuffix());
        Assert.Equal(".dymo", ExportFormat.DymoLabel.FileSuffix());
        Assert.Equal(".json", ExportFormat.Json.FileSuffix());
        Assert.Equal(".csv", ExportFormat.Csv.FileSuffix());
    }

    [Fact]
    public void TheFilesAlreadyOnDiskAreFoundAgain_SoPrintingTwiceNeedsNoSecondExport()
    {
        File.WriteAllText(Path.Combine(_folder, "dev-label.pdf"), "x");
        File.WriteAllText(Path.Combine(_folder, "dev.json"), "{}");

        Assert.Equal(
            [ExportFormat.LabelPdf, ExportFormat.Json],
            ExportService.Existing("dev", _folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}

// ============ What a failure is written in ============
//
// The panel shows these sentences to the operator at the moment something went
// wrong. They used to be English here and English there, on a panel whose
// buttons were in the operator's own language, which left the one line that
// mattered in the language of the code. They are now format strings handed in
// from the dictionaries, and these tests hold that place.

public class ExportWordingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"wording-{Guid.NewGuid():N}");

    public ExportWordingTests() => Directory.CreateDirectory(_folder);

    /// <summary>
    /// A wording set that is obviously not English, standing in for the Dutch
    /// dictionaries. Every sentence carries a marker so a test can tell which one
    /// reached the operator.
    /// </summary>
    private static ExportWording Marked(string tag) => new()
    {
        TemplateMissing = $"{tag} template missing {{0}}",
        TemplateUnreadable = $"{tag} unreadable {{0}} {{1}}",
        NoTemplateFound = $"{tag} none found {{0}} {{1}}",
        TemplateHasNoFields = $"{tag} no fields",
        WriteFailed = $"{tag} write failed {{0}} {{1}}",
        UnfilledFields = $"{tag} unfilled {{0}}",
        BarcodeReplaced = $"{tag} barcode replaced",
        BarcodeWrapped = $"{tag} barcode wrapped",
        NoFileAt = $"{tag} no file {{0}}",
        NoFolderAt = $"{tag} no folder {{0}}",
        NoPrinterChosen = $"{tag} no printer",
        PrintDialogOpened = $"{tag} dialog",
        PrintNoHandler = $"{tag} no handler",
        PrintPreviewOpened = $"{tag} preview",
        PrintDefaultApp = $"{tag} default app",
        PrintNotStarted = $"{tag} not started {{0}}",
        PrintQueueNeedsDialog = $"{tag} needs dialog",
        PrintSentToQueue = $"{tag} sent {{0}}",
        PrintToQueueFailed = $"{tag} queue failed {{0}} {{1}}",
        FolderOpened = $"{tag} folder opened",
        FolderNotOpened = $"{tag} folder failed {{0}}",
        FileOpened = $"{tag} file opened",
        FileNotOpened = $"{tag} file failed {{0}}",
    };

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809", Model = "13 Pro", Color = "Wit",
        Storage = "256GB", BatteryHealth = "90", Quality = "A", PayMethod = "Marge",
    };

    [Fact]
    public async Task AMissingTemplateIsExplainedInTheWordsThatWereHandedIn()
    {
        // This is the line an operator sees when their own template has been moved.
        // In English it is a sentence they can act on; in the language of the code it
        // is not, which is the whole reason the wording is passed in.
        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "missing",
            TemplatePath: Path.Combine(_folder, "gone.dymo"),
            Messages: Marked("XX")));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.False(label.Succeeded);
        Assert.StartsWith("XX template missing", label.Problem);
        Assert.Contains("gone.dymo", label.Problem);
    }

    [Fact]
    public async Task ATemplateWithNothingToFillIsExplainedInTheWordsThatWereHandedIn()
    {
        string merged = Path.Combine(_folder, "merged.dymo");
        File.WriteAllText(merged, """
            <?xml version="1.0" encoding="utf-8"?>
            <DesktopLabel Version="1"><DYMOLabel Version="3">
              <TextObject><Name>T</Name><Text>356938035643809 13 Pro</Text></TextObject>
            </DYMOLabel></DesktopLabel>
            """);

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "merged",
            TemplatePath: merged, Messages: Marked("XX")));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.False(label.Succeeded);
        Assert.Equal("XX no fields", label.Problem);
    }

    [Fact]
    public async Task ANoteAboutUnfilledFieldsIsWrittenInTheWordsThatWereHandedIn()
    {
        string own = Path.Combine(_folder, "own.dymo");
        File.WriteAllText(own, """
            <?xml version="1.0" encoding="utf-8"?>
            <DesktopLabel Version="1"><DYMOLabel Version="3">
              <TextObject><Name>T</Name><Text>COMPANY MODEL IDENTIFIER</Text></TextObject>
            </DYMOLabel></DesktopLabel>
            """);

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(Phone(), new LabelWriter.Request(
            [ExportFormat.DymoLabel], _folder, "own",
            TemplatePath: own, Messages: Marked("XX")));

        LabelWriter.Outcome label = batch.Files.Single();
        Assert.True(label.Succeeded, label.Problem);
        Assert.StartsWith("XX unfilled COMPANY", label.Note);
    }

    [Fact]
    public void ARewrittenBarcodeSaysSoInTheWordsThatWereHandedIn()
    {
        // A serial with a character Code39 cannot carry comes out as a different
        // serial, and the operator has to know that before the label is on a device.
        LabelBarcode.Encode("356938&035643809", out string? reason, Marked("XX"));

        Assert.Equal("XX barcode replaced", reason);
    }

    [Fact]
    public void AFileThatIsNotThereIsReportedInTheWordsThatWereHandedIn()
    {
        PrintService.Attempt attempt = PrintService.PrintDialog(
            Path.Combine(_folder, "not-written.pdf"), Marked("XX"));

        Assert.Equal(PrintService.Outcome.Failed, attempt.Outcome);
        Assert.StartsWith("XX no file", attempt.Message);
    }

    [Fact]
    public void AFailedPrintIsReportedInTheWordsThatWereHandedIn()
    {
        PrintService.Attempt attempt = PrintService.PrintToQueue(
            Path.Combine(_folder, "not-written.pdf"), "", wording: Marked("XX"));

        Assert.Equal(PrintService.Outcome.Failed, attempt.Outcome);
        Assert.Equal("XX no printer", attempt.Message);
    }

    [Fact]
    public void AFolderThatIsNotThereIsReportedInTheWordsThatWereHandedIn()
    {
        PrintService.Attempt attempt = PrintService.OpenFolder(
            Path.Combine(_folder, "no-such-folder"), Marked("XX"));

        Assert.Equal(PrintService.Outcome.Failed, attempt.Outcome);
        Assert.StartsWith("XX no folder", attempt.Message);
    }

    [Fact]
    public void ACallerWithNoWordingStillGetsAReadableSentence()
    {
        // The Core project is usable without the UI, so nothing may depend on the
        // panel having handed wording in. The fallback is English rather than an
        // empty line, which would be the worst of both.
        PrintService.Attempt attempt = PrintService.PrintDialog(
            Path.Combine(_folder, "not-written.pdf"));

        Assert.Equal(PrintService.Outcome.Failed, attempt.Outcome);
        Assert.Contains("no file", attempt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFormatWithNoArgumentsIsLeftAlone()
    {
        // Several of these sentences carry no value at all, and passing them through
        // string.Format anyway is how "{0}" ends up on a Dutch screen.
        Assert.Equal("XX no printer", Marked("XX").Say(Marked("XX").NoPrinterChosen));
        Assert.Equal("XX folder opened", Marked("XX").Say(Marked("XX").FolderOpened));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}

// ============ Reading DYMO's own answers ============

public class DymoPrintServiceTests
{
    private const string TwoPrinters = """
        <?xml version="1.0" encoding="utf-8"?>
        <Printers>
          <LabelWriterPrinter>
            <Name>DYMO LabelWriter 450 Turbo</Name>
            <ModelName>LabelWriter 450</ModelName>
            <IsConnected>True</IsConnected>
            <IsLocal>True</IsLocal>
            <IsTwinTurbo>False</IsTwinTurbo>
          </LabelWriterPrinter>
          <TapePrinter>
            <Name>DYMO Mobile Labeler</Name>
            <ModelName>Mobile Labeler</ModelName>
            <IsConnected>False</IsConnected>
          </TapePrinter>
        </Printers>
        """;

    [Fact]
    public void BothPrinterKindsAreRead()
    {
        // The element name is the printer type, so a tape printer arrives under a
        // different tag than a LabelWriter and both of them are printers.
        var printers = DymoPrintService.ParsePrinters(TwoPrinters);

        Assert.Equal(2, printers.Count);
        Assert.Equal("DYMO LabelWriter 450 Turbo", printers[0].Name);
        Assert.True(printers[0].IsConnected);
        Assert.False(printers[0].IsTwinTurbo);
        Assert.Equal("DYMO Mobile Labeler", printers[1].Name);
        Assert.False(printers[1].IsConnected);
    }

    [Fact]
    public void TheNameIsReadAsTheThingToPrintTo()
    {
        // The model is what an operator recognises; the name is what the service
        // wants back. Sending the model is a refused job.
        var printer = DymoPrintService.ParsePrinters(TwoPrinters)[0];
        Assert.Equal("DYMO LabelWriter 450 Turbo", printer.Name);
        Assert.Equal("LabelWriter 450", printer.Model);
    }

    [Fact]
    public void ABodyThatIsNotXmlIsNotAPrinterList()
    {
        Assert.Empty(DymoPrintService.ParsePrinters("true"));
        Assert.Empty(DymoPrintService.ParsePrinters(""));
    }

    [Fact]
    public void APrinterWithNoNameIsSkipped()
    {
        // Printing to an empty name would be a request that fails for a reason
        // nobody can act on.
        Assert.Empty(DymoPrintService.ParsePrinters("<Printers><LabelWriterPrinter></LabelWriterPrinter></Printers>"));
    }

    [Fact]
    public void ThePortRangeIsTheOneDymoUses()
    {
        // Not 4195. The service takes the first free port in a range and a client
        // has to go looking for it.
        Assert.Equal(41951, DymoPrintService.FirstPort);
        Assert.Equal(41960, DymoPrintService.LastPort);
    }
}

// ============ The verdict the report prints ============

public class ReportVerdictTests
{
    private static ReportWording Wording => ReportWording.English;

    [Fact]
    public void ADeviceWithNothingWrongSaysSo()
        => Assert.Equal("No faults found", ReportPdfWriter.Verdict(new DeviceData(), Wording));

    [Fact]
    public void AReplacedPartShowsUp()
    {
        var data = new DeviceData();
        data.ComponentChecks.Add(new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch });
        Assert.Contains("not original", ReportPdfWriter.Verdict(data, Wording));
    }

    [Fact]
    public void AFailedTestShowsUp()
    {
        var data = new DeviceData
        {
            InteractiveTests = new InteractiveTestSuiteResult
            {
                Tests = [new InteractiveTestResult { Name = "Touchscreen", Status = TestStatus.Failed }],
            },
        };
        Assert.Contains("test failed", ReportPdfWriter.Verdict(data, Wording));
    }

    [Fact]
    public void SkippedTestsDoNotReadAsAFault_ButAreNotHiddenEither()
    {
        // "No faults found" over a run that never happened is the report
        // misinforming whoever reads it next.
        var data = new DeviceData
        {
            InteractiveTests = new InteractiveTestSuiteResult
            {
                Tests = [new InteractiveTestResult { Name = "Nabijheidssensor", Status = TestStatus.Skipped }],
            },
        };
        string verdict = ReportPdfWriter.Verdict(data, Wording);

        Assert.StartsWith("No faults found", verdict);
        Assert.Contains("1 skipped", verdict);
    }
}
