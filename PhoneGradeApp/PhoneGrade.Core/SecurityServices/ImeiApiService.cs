using System.Text.Json;

namespace PhoneGrade.Core.SecurityServices;

/// <summary>
/// IMEI API integration service supporting multiple providers.
/// Configured via environment variables or appsettings.
/// </summary>
public static class ImeiApiService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>Check activation lock status via IMEI lookup API (e.g., SickW, IMEIPro, etc.).</summary>
    public static async Task<ActivationLockApiResult> CheckActivationLockAsync(string imei)
    {
        string? apiKey = Environment.GetEnvironmentVariable("IMEI_API_KEY");
        string? apiProvider = Environment.GetEnvironmentVariable("IMEI_API_PROVIDER") ?? "sickw";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ActivationLockApiResult
            {
                Status = "Unknown",
                Message = "IMEI_API_KEY niet geconfigureerd",
                Source = apiProvider
            };
        }

        try
        {
            return apiProvider.ToLowerInvariant() switch
            {
                "sickw" => await CheckSickWAsync(imei, apiKey),
                "imeipro" => await CheckImeiProAsync(imei, apiKey),
                _ => new ActivationLockApiResult
                {
                    Status = "Unknown",
                    Message = $"Onbekende provider: {apiProvider}",
                    Source = apiProvider
                }
            };
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ImeiApiService", "CheckActivationLockAsync", 1, ex.Message, ex.StackTrace ?? "");
            return new ActivationLockApiResult
            {
                Status = "Error",
                Message = ex.Message,
                Source = apiProvider
            };
        }
    }

    private static async Task<ActivationLockApiResult> CheckSickWAsync(string imei, string apiKey)
    {
        // SickW API endpoint (adjust based on actual API documentation)
        string url = $"https://api.sickw.com/imei/check?imei={imei}&apikey={apiKey}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return new ActivationLockApiResult
            {
                Status = "Error",
                Message = $"API request failed: {response.StatusCode}",
                Source = "sickw"
            };
        }

        string json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SickWResponse>(json);

        return new ActivationLockApiResult
        {
            Status = result?.FindMyIPhone ?? "Unknown",
            CarrierLock = result?.CarrierLock,
            Blacklisted = result?.Blacklisted,
            Message = result?.Message,
            Source = "sickw"
        };
    }

    private static async Task<ActivationLockApiResult> CheckImeiProAsync(string imei, string apiKey)
    {
        // IMEIPro API endpoint (adjust based on actual API documentation)
        string url = $"https://api.imeipro.info/check/{imei}";
        
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return new ActivationLockApiResult
            {
                Status = "Error",
                Message = $"API request failed: {response.StatusCode}",
                Source = "imeipro"
            };
        }

        string json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ImeiProResponse>(json);

        return new ActivationLockApiResult
        {
            Status = result?.ICloudStatus ?? "Unknown",
            CarrierLock = result?.SimLock,
            Blacklisted = result?.BlacklistStatus,
            Message = result?.Info,
            Source = "imeipro"
        };
    }

    // API response models
    private class SickWResponse
    {
        public string? FindMyIPhone { get; set; }
        public string? CarrierLock { get; set; }
        public string? Blacklisted { get; set; }
        public string? Message { get; set; }
    }

    private class ImeiProResponse
    {
        public string? ICloudStatus { get; set; }
        public string? SimLock { get; set; }
        public string? BlacklistStatus { get; set; }
        public string? Info { get; set; }
    }
}

public class ActivationLockApiResult
{
    public string Status { get; set; } = "Unknown";
    public string? CarrierLock { get; set; }
    public string? Blacklisted { get; set; }
    public string? Message { get; set; }
    public string Source { get; set; } = "Unknown";
}
