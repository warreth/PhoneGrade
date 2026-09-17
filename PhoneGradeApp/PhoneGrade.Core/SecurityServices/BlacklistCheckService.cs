namespace PhoneGrade.Core.SecurityServices;

/// <summary>IMEI blacklist checking service with pluggable provider pattern.</summary>
public static class BlacklistCheckService
{
    public class BlacklistStatus
    {
        public bool IsBlacklisted { get; set; }
        public string? Reason { get; set; }
        public string Source { get; set; } = "Unknown";
    }

    public interface IBlacklistProvider
    {
        Task<BlacklistStatus> CheckAsync(string imei);
    }

    /// <summary>No-op provider that always returns unknown status.</summary>
    public class NoOpBlacklistProvider : IBlacklistProvider
    {
        public Task<BlacklistStatus> CheckAsync(string imei)
        {
            return Task.FromResult(new BlacklistStatus
            {
                IsBlacklisted = false,
                Source = "NoOp"
            });
        }
    }

    /// <summary>GSMA registry provider stub (requires API key and subscription).</summary>
    public class GsmaBlacklistProvider : IBlacklistProvider
    {
        private readonly string? _apiKey;
        private readonly HttpClient _httpClient;

        public GsmaBlacklistProvider(string? apiKey = null)
        {
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GSMA_API_KEY");
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<BlacklistStatus> CheckAsync(string imei)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                ToolRunner.Log("GsmaBlacklistProvider", "CheckAsync", 0, "API key missing", "");
                return new BlacklistStatus { Source = "GSMA (unconfigured)" };
            }

            try
            {
                // Stub: GSMA API endpoint (hypothetical, adjust to actual API)
                string url = $"https://api.gsma.com/imei-check/v1/{imei}";
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    ToolRunner.Log("GsmaBlacklistProvider", "CheckAsync", (int)response.StatusCode, 
                        "API request failed", await response.Content.ReadAsStringAsync());
                    return new BlacklistStatus { Source = "GSMA (error)" };
                }

                string json = await response.Content.ReadAsStringAsync();
                // Parse JSON response (stub implementation)
                bool isBlacklisted = json.Contains("\"status\":\"blacklisted\"", StringComparison.OrdinalIgnoreCase);
                
                return new BlacklistStatus
                {
                    IsBlacklisted = isBlacklisted,
                    Reason = isBlacklisted ? "Reported lost or stolen" : null,
                    Source = "GSMA"
                };
            }
            catch (Exception ex)
            {
                ToolRunner.Log("GsmaBlacklistProvider", "CheckAsync", 1, ex.Message, ex.StackTrace ?? "");
                return new BlacklistStatus { Source = "GSMA (exception)" };
            }
        }
    }
}
