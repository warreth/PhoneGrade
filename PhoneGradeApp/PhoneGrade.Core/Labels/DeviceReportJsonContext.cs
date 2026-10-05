using System.Text.Json.Serialization;

namespace PhoneGrade.Core;

/// <summary>
/// The serialization contract for the JSON export, resolved at compile time.
///
/// A reflection based serializer would read the field names off the C# properties
/// at run time, which means renaming a property silently renames a field in a file
/// somebody archived last year. A generated context pins the contract into the
/// build instead: a property whose name or type changes stops compiling here.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeviceReportJson.Report))]
internal sealed partial class DeviceReportJsonContext : JsonSerializerContext;