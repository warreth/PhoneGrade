using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhoneGrade.Core;

/// <summary>
/// Client-side telemetry: browser capabilities, screen info, touch support.
/// Sent by PWA immediately on WebSocket connect for correlation and debugging.
/// </summary>
public class ClientTelemetry
{
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = "";

    [JsonPropertyName("browser")]
    public string Browser { get; set; } = ""; // Parsed: Safari, Chrome, Firefox, etc

    [JsonPropertyName("browserVersion")]
    public string BrowserVersion { get; set; } = "";

    [JsonPropertyName("os")]
    public string Os { get; set; } = ""; // iOS, Android, Windows, macOS, etc

    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = "";

    [JsonPropertyName("screenWidth")]
    public int ScreenWidth { get; set; }

    [JsonPropertyName("screenHeight")]
    public int ScreenHeight { get; set; }

    [JsonPropertyName("pixelRatio")]
    public double PixelRatio { get; set; } = 1.0;

    [JsonPropertyName("touchSupport")]
    public bool TouchSupport { get; set; }

    [JsonPropertyName("accelerometerSupport")]
    public bool AccelerometerSupport { get; set; }

    [JsonPropertyName("gyroscopeSupport")]
    public bool GyroscopeSupport { get; set; }

    [JsonPropertyName("geolocationSupport")]
    public bool GeolocationSupport { get; set; }

    [JsonPropertyName("webAudioSupport")]
    public bool WebAudioSupport { get; set; }

    [JsonPropertyName("cameraSupport")]
    public bool CameraSupport { get; set; }

    [JsonPropertyName("microphone Support")]
    public bool MicrophoneSupport { get; set; }

    [JsonPropertyName("vibrationSupport")]
    public bool VibrationSupport { get; set; }

    [JsonPropertyName("languag")]
    public string Language { get; set; } = ""; // navigator.language

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = ""; // Intl.DateTimeFormat().resolvedOptions().timeZone
}

/// <summary>
/// Extended DeviceSessionMessage to include log_event type for streaming logs over WebSocket.
/// </summary>
public class LogEventMessage : DeviceSessionMessage
{
    [JsonPropertyName("logEvent")]
    public LogEvent? LogEvent { get; set; }

    [JsonPropertyName("clientTelemetry")]
    public ClientTelemetry? ClientTelemetry { get; set; }
}
