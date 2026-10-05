using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PhoneGrade.Core;

/// <summary>
/// The full inspection report as a PDF.
///
/// One page where it fits, several where it does not. The report is what a shop
/// files and what a customer disputes a grade with, so it states the verdict
/// first and shows every number that led to it, including the ones that were
/// never read: a report that silently drops what it could not collect reads as a
/// clean device.
/// </summary>
public static class ReportPdfWriter
{
    public static void Write(string path, DeviceData data, ReportWording wording)
    {
        // Same reason as the label writer: preparing the font manager, which also
        // sets the licence up, belongs to the thing that draws rather than to every
        // caller remembering to do it first.
        ReportFonts.Ensure();

        string? family = ReportFonts.Resolve();

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.6f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontFamily(family ?? "Helvetica").FontSize(9));

                page.Header().Element(container => Header(container, data, wording, family));
                page.Content().Element(container => Body(container, data, wording, family));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"{wording.Title} · ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(path);
    }

    private static void Header(IContainer container, DeviceData data, ReportWording wording, string? family)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(wording.Title).FontSize(17).Bold();
                    left.Item().Text($"{wording.Generated}: {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(150).Column(right =>
                {
                    right.Item().AlignRight().Text(Verdict(data, wording))
                        .FontSize(13).Bold().FontColor(VerdictColor(data));
                    right.Item().AlignRight().Text(wording.Confidential)
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten3);
        });
    }

    private static void Body(IContainer container, DeviceData data, ReportWording wording, string? family)
    {
        LabelFields fields = LabelFields.From(data);
        string na = wording.NotAvailable;

        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(c => Section(c, wording.SectionDevice, wording, family, [
                (wording.FieldName, null),
                ("IMEI / serial", data.Identifier),
                ("Model", data.Model),
                ("Colour", data.Color),
                ("Storage", data.Storage),
                ("Memory", data.Memory),
                ("OS", data.IosVersion ?? na),
            ], data, wording));

            column.Item().Element(c => Section(c, wording.SectionBattery, wording, family, [
                ("Condition", fields.Battery),
                ("Charge", data.BatteryLevel?.ToString(CultureInfo.InvariantCulture) + "%" ?? na),
                ("Cycles", data.BatteryCycleCount?.ToString(CultureInfo.InvariantCulture) ?? na),
                ("Design capacity", data.BatteryDesignCapacity > 0
                    ? data.BatteryDesignCapacity.ToString(CultureInfo.InvariantCulture) + " mAh"
                    : na),
                ("Current capacity", data.BatteryCurrentCapacity > 0
                    ? data.BatteryCurrentCapacity.ToString(CultureInfo.InvariantCulture) + " mAh"
                    : na),
                ("Battery serial", Blank(data.BatterySerialNumber, na)),
                ("Factory serial", Blank(data.OriginalBatterySerialNumber, na)),
            ], data, wording));

            column.Item().Element(c => Section(c, wording.SectionSecurity, wording, family, [
                ("Find My / activation lock", data.ActivationLock?.ToString() ?? na),
                ("Carrier lock iOS", Carrier(data.CarrierLockIOS?.IsCarrierLocked, wording)),
                ("Carrier", Blank(data.CarrierLockIOS?.CarrierName, na)),
                ("Carrier lock Android", Carrier(data.CarrierLockAndroid?.IsCarrierLocked, wording)),
                ("Carrier", Blank(data.CarrierLockAndroid?.CarrierName, na)),
                ("SIM state", Blank(data.CarrierLockAndroid?.SIMState, na)),
                ("Factory reset protection", data.FactoryResetProtection?.ToString() ?? na),
                ("Blacklisted", data.Blacklist is null
                    ? na
                    : data.Blacklist.IsBlacklisted ? wording.Yes : wording.No),
                ("Blacklist reason", Blank(data.Blacklist?.Reason, na)),
            ], data, wording));

            column.Item().Element(c => Section(c, wording.SectionNetwork, wording, family, [
                ("Wi-Fi", Blank(data.WifiMacAddress, na)),
                ("Bluetooth", Blank(data.BluetoothMacAddress, na)),
                ("Cellular", Blank(data.CellularAddress, na)),
                ("IMEI2", Blank(data.Imei2, na)),
            ], data, wording));

            if (data.ComponentChecks.Count > 0)
                column.Item().Element(c => Components(c, data, wording, family));

            if (data.InteractiveTests?.Tests.Count > 0)
                column.Item().Element(c => Tests(c, data, wording, family));

            // What could not be read is part of the record. A report that omits it
            // reads as a device with nothing wrong with it.
            if (data.WithheldReads.Count > 0)
            {
                column.Item().Element(c =>
                {
                    c.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(inner =>
                    {
                        inner.Item().Text("Not read").FontSize(11).Bold();
                        inner.Item().PaddingTop(4).Text(string.Join(", ", data.WithheldReads))
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }
        });
    }

    /// <summary>A two column table of fields, in the report's own wording.</summary>
    private static void Section(
        IContainer container, string title, ReportWording wording, string? family,
        (string Name, string? Value)[] rows, DeviceData data, ReportWording _)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).Text(title).FontSize(12).Bold().FontColor(Colors.Grey.Darken2);

            // Two field pairs per row: a report of thirty fields is four pages of
            // one column each, which is a document nobody reads to the end.
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(110);
                    columns.RelativeColumn();
                    columns.ConstantColumn(110);
                    columns.RelativeColumn();
                });

                for (int i = 0; i < rows.Length; i += 2)
                {
                    Field(table, rows[i].Name, family);
                    Field(table, rows[i].Value ?? wording.NotAvailable, family);
                    if (i + 1 < rows.Length)
                    {
                        Field(table, rows[i + 1].Name, family);
                        Field(table, rows[i + 1].Value ?? wording.NotAvailable, family);
                    }
                    else
                    {
                        table.Cell();
                        table.Cell();
                    }
                }
            });
        });
    }

    private static void Field(QuestPDF.Fluent.TableDescriptor table, string text, string? family) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3f)
            .PaddingRight(6f)
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(8.5f);

    private static void Components(IContainer container, DeviceData data, ReportWording wording, string? family)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).Text(wording.SectionComponents).FontSize(12).Bold()
                .FontColor(Colors.Grey.Darken2);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.ConstantColumn(70);
                    columns.RelativeColumn(1.4f);
                });

                table.Header(header =>
                {
                    for (int i = 0; i < wording.ComponentColumns.Length; i++)
                        header.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                            .Text(wording.ComponentColumns[i]).SemiBold().FontSize(8);
                });

                foreach (ComponentStatus check in data.ComponentChecks)
                {
                    table.Cell().Padding(4).Text(check.Name).FontSize(8.5f);
                    table.Cell().Padding(4).Text(Blank(check.SerialRead, wording.NotAvailable)).FontSize(8.5f);
                    table.Cell().Padding(4).Text(Blank(check.SerialOriginal, wording.NotAvailable)).FontSize(8.5f);
                    table.Cell().Padding(4).Text(check.Status.ToString()).FontSize(8)
                        .FontColor(StatusColor(check.Status));
                    table.Cell().Padding(4).Text(Blank(check.Description, "")).FontSize(8);
                }
            });
        });
    }

    private static void Tests(IContainer container, DeviceData data, ReportWording wording, string? family)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).Text(wording.SectionTests).FontSize(12).Bold()
                .FontColor(Colors.Grey.Darken2);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.2f);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(55);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    for (int i = 0; i < wording.TestColumns.Length; i++)
                        header.Cell().Background(Colors.Grey.Lighten4).Padding(4)
                            .Text(wording.TestColumns[i]).SemiBold().FontSize(8);
                });

                foreach (InteractiveTestResult test in data.InteractiveTests!.Tests)
                {
                    table.Cell().Padding(4).Text(test.Name).FontSize(8.5f);
                    table.Cell().Padding(4).Text(test.Status.ToString()).FontSize(8)
                        .FontColor(StatusColor(test.Status));
                    table.Cell().Padding(4)
                        .Text($"{test.DurationMs / 1000.0:0.#} s").FontSize(8);
                    table.Cell().Padding(4).Text(Blank(test.Notes, "")).FontSize(8);
                }
            });
        });
    }

    /// <summary>
    /// The one line the verdict is drawn from. Counted here rather than trusted from
    /// the data, because the report is what a dispute is settled on and a verdict
    /// that disagrees with the table under it is worse than no verdict.
    /// </summary>
    public static string Verdict(DeviceData data, ReportWording wording)
    {
        int mismatched = data.ComponentChecks.Count(check => check.Status == ComponentStatusType.Mismatch);
        int failed = data.InteractiveTests?.Tests.Count(test => test.Status == TestStatus.Failed) ?? 0;
        int untrusted = data.ComponentChecks.Count(check => check.Status == ComponentStatusType.Untrusted);
        int skipped = data.InteractiveTests?.Tests.Count(test => test.Status == TestStatus.Skipped) ?? 0;

        if (mismatched == 0 && failed == 0 && untrusted == 0)
            return skipped > 0 ? $"{wording.VerdictClean} ({skipped} skipped)" : wording.VerdictClean;

        var parts = new List<string>();
        if (mismatched > 0) parts.Add($"{mismatched} not original");
        if (untrusted > 0) parts.Add($"{untrusted} unverified");
        if (failed > 0) parts.Add($"{failed} test failed");

        return string.Join(", ", parts);
    }

    private static string VerdictColor(DeviceData data) =>
        data.ComponentChecks.Any(check => check.Status == ComponentStatusType.Mismatch)
        || data.InteractiveTests?.Tests.Any(test => test.Status == TestStatus.Failed) == true
            ? Colors.Red.Darken1
            : Colors.Green.Darken1;

    private static string StatusColor(ComponentStatusType status) => status switch
    {
        ComponentStatusType.Match => Colors.Green.Darken2,
        ComponentStatusType.Mismatch => Colors.Red.Darken1,
        ComponentStatusType.Untrusted => Colors.Orange.Darken1,
        ComponentStatusType.Failed => Colors.Red.Darken1,
        _ => Colors.Grey.Darken1,
    };

    private static string StatusColor(TestStatus status) => status switch
    {
        TestStatus.Passed => Colors.Green.Darken2,
        TestStatus.Failed => Colors.Red.Darken1,
        TestStatus.Skipped => Colors.Orange.Darken1,
        _ => Colors.Grey.Darken1,
    };

    private static string Carrier(bool? locked, ReportWording wording) => locked switch
    {
        true => wording.Locked,
        false => wording.Unlocked,
        null => wording.NotAvailable,
    };

    private static string Blank(string? value, string whenMissing) =>
        string.IsNullOrWhiteSpace(value) ? whenMissing : value;
}
