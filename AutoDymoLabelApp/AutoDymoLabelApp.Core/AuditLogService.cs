using System.IO;
using System.Text.Json;

namespace AutoDymoLabel.Core;

/// <summary>Local JSON audit exporter for inspected devices and OEM component verification.</summary>
public static class AuditLogService
{
    public static string AuditDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoDymoLabel", "audits");

    /// <summary>Writes device data and component audit report to a local JSON file.</summary>
    public static string ExportAuditLog(DeviceData data, string? customDir = null)
    {
        string dir = customDir ?? AuditDir;
        Directory.CreateDirectory(dir);

        string fileName = $"audit_{SanitizeFileName(data.Identifier)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(dir, fileName);

        var auditRecord = new
        {
            TimestampUtc = DateTime.UtcNow,
            data.Identifier,
            data.DeviceId,
            data.Model,
            data.ProductType,
            data.Color,
            data.Storage,
            data.BatteryHealth,
            data.Quality,
            data.PayMethod,
            data.IosVersion,
            Battery = new
            {
                data.BatteryCycleCount,
                data.BatteryDesignCapacity,
                data.BatteryCurrentCapacity,
                data.BatterySerialNumber,
                data.OriginalBatterySerialNumber
            },
            Components = new
            {
                data.DisplaySerialNumber,
                data.CoverGlassSerialNumber,
                data.FrontCameraSerialNumber,
                data.RearCameraSerialNumber,
                data.MotherboardSerialNumber
            },
            Checks = data.ComponentChecks.Select(c => new
            {
                c.Name,
                c.SerialRead,
                c.SerialOriginal,
                Status = c.Status.ToString()
            }).ToList()
        };

        string json = JsonSerializer.Serialize(auditRecord, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
        return path;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "UNKNOWN";
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
