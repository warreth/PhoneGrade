using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PhoneGrade.Core.SecurityServices;

/// <summary>
/// IMEI.info API integration service (BYOK - Bring Your Own Key).
///
/// Talks to the official v5 gateway (dash.imei.info) described by the public
/// OpenAPI document at https://dash.imei.info/swagger/?format=openapi:
///
///   GET /api-sync/check/{service}/?API_KEY=...&amp;format=json&amp;imei=...
///   GET /api/search_history/{id}/?API_KEY=...
///   GET /api/service/services/?API_KEY=...
///   GET /api/account/account/?API_KEY=...
///
/// Every request carries the key twice, as a Bearer header and as the API_KEY
/// query parameter, which is how the official SDKs call it. Service identifiers
/// are numeric and account specific, so the id behind each check is resolved
/// from the service list once per key and then cached.
/// </summary>
public static class ImeiInfoApiService
{
    public const string BaseUrl = "https://dash.imei.info";

    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        BaseAddress = new Uri(BaseUrl)
    };

    // Resolved service ids per API key; the dashboard decides which id means
    // which check, so nothing here is hard-coded to an account's numbering.
    private static readonly Dictionary<string, Dictionary<ImeiCheckType, int>> ServiceIdCache = new();
    private static readonly object CacheLock = new();

    private const int PollAttempts = 5;
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Types of checks available via imei.info API.</summary>
    public enum ImeiCheckType
    {
        /// <summary>$0.360 - APPLE: Carrier &amp; Lock Status &amp; FMI</summary>
        AppleCarrierLockFmi,

        /// <summary>$0.200 - BLACKLIST: Simple Check</summary>
        BlacklistSimple,

        /// <summary>$0.420 - BLACKLIST: Premium Check (detailed info)</summary>
        BlacklistPremium,

        /// <summary>$0.600 - GENERIC: Samsung Info Check &amp; Knox Info</summary>
        SamsungInfoKnox
    }

    /// <summary>Static pricing information for each check type (in USD).</summary>
    public static readonly Dictionary<ImeiCheckType, decimal> CheckPrices = new()
    {
        { ImeiCheckType.AppleCarrierLockFmi, 0.360m },
        { ImeiCheckType.BlacklistSimple, 0.200m },
        { ImeiCheckType.BlacklistPremium, 0.420m },
        { ImeiCheckType.SamsungInfoKnox, 0.600m }
    };

    /// <summary>Human-readable names for each check type.</summary>
    public static readonly Dictionary<ImeiCheckType, string> CheckNames = new()
    {
        { ImeiCheckType.AppleCarrierLockFmi, "APPLE: Carrier & Lock Status & FMI" },
        { ImeiCheckType.BlacklistSimple, "BLACKLIST: Simple Check" },
        { ImeiCheckType.BlacklistPremium, "BLACKLIST: Premium Check" },
        { ImeiCheckType.SamsungInfoKnox, "GENERIC: Samsung Info Check & Knox Info" }
    };

    /// <summary>Detailed descriptions for each check type.</summary>
    public static readonly Dictionary<ImeiCheckType, string> CheckDescriptions = new()
    {
        { ImeiCheckType.AppleCarrierLockFmi, "Returns carrier name, SIM lock status, and Find My iPhone (FMI) status for Apple devices." },
        { ImeiCheckType.BlacklistSimple, "Returns basic blacklist status (clean/blacklisted) only." },
        { ImeiCheckType.BlacklistPremium, "Returns detailed blacklist info: IMEI, Model Name, Manufacturer, Model Number, Blacklisted By, Blacklisted On, Blacklist Reason, Blacklist Status, Blacklisted Country, General List Status." },
        { ImeiCheckType.SamsungInfoKnox, "Returns Samsung device info including model, serial, warranty, and Knox warranty bit status." }
    };

    /// <summary>
    /// Stable codes stored in settings for each check. They are also offered as
    /// a slug match when the dashboard lists slugs alongside service names.
    /// </summary>
    public static readonly Dictionary<ImeiCheckType, string> ServiceCodes = new()
    {
        { ImeiCheckType.AppleCarrierLockFmi, "apple_carrier_lock_fmi" },
        { ImeiCheckType.BlacklistSimple, "blacklist_simple" },
        { ImeiCheckType.BlacklistPremium, "blacklist_premium" },
        { ImeiCheckType.SamsungInfoKnox, "samsung_info_knox" }
    };

    /// <summary>Result of an IMEI check.</summary>
    public class ImeiCheckResult
    {
        public ImeiCheckType CheckType { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public JsonElement? RawData { get; set; }
        public decimal Price { get; set; }

        /// <summary>Numeric service id the check ran against, once resolved.</summary>
        public int? ServiceId { get; set; }

        /// <summary>Request id in search history, when the gateway queued it.</summary>
        public int? RequestId { get; set; }

        // Parsed common fields
        public string? Status { get; set; }
        public string? CarrierName { get; set; }
        public string? SimLockStatus { get; set; }
        public string? FmiStatus { get; set; }
        public bool? IsBlacklisted { get; set; }
        public string? BlacklistReason { get; set; }
        public string? BlacklistedBy { get; set; }
        public DateTime? BlacklistedOn { get; set; }
        public string? BlacklistedCountry { get; set; }
        public string? ModelName { get; set; }
        public string? Manufacturer { get; set; }
        public string? ModelNumber { get; set; }
        public string? KnoxStatus { get; set; }
        public string? WarrantyStatus { get; set; }
    }

    /// <summary>
    /// Runs a specific check on an IMEI. The optional client is how tests hook
    /// a stub transport in; production code uses the shared client.
    /// </summary>
    public static async Task<ImeiCheckResult> CheckAsync(
        string imei,
        ImeiCheckType checkType,
        string apiKey,
        HttpClient? httpClient = null)
    {
        var result = new ImeiCheckResult
        {
            CheckType = checkType,
            Price = CheckPrices[checkType]
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            result.Success = false;
            result.ErrorMessage = "API key not configured";
            return result;
        }

        if (string.IsNullOrWhiteSpace(imei) || imei.Length < 14)
        {
            result.Success = false;
            result.ErrorMessage = "Invalid IMEI";
            return result;
        }

        var client = httpClient ?? SharedClient;

        try
        {
            int serviceId = await ResolveServiceIdAsync(checkType, apiKey, client).ConfigureAwait(false);
            if (serviceId <= 0)
            {
                result.Success = false;
                result.ErrorMessage = $"Service not found: {CheckNames[checkType]}";
                return result;
            }

            result.ServiceId = serviceId;

            string url = $"/api-sync/check/{serviceId}/"
                + $"?API_KEY={Uri.EscapeDataString(apiKey)}"
                + "&format=json"
                + $"&imei={Uri.EscapeDataString(imei.Trim())}";

            var (statusCode, body) = await SendAsync(client, url, apiKey).ConfigureAwait(false);

            // The gateway answers a bad token with HTTP 200 and a detail envelope
            // on the api-sync path, so the body is checked before the status.
            if (IsInvalidTokenBody(body))
            {
                result.Success = false;
                result.ErrorMessage = ExtractErrorMessage(body, statusCode);
                return result;
            }

            if (statusCode == HttpStatusCode.OK || statusCode == HttpStatusCode.Accepted)
                return await ApplyEnvelopeAsync(result, body, statusCode, apiKey, client, mayPoll: true).ConfigureAwait(false);

            result.Success = false;
            result.ErrorMessage = ExtractErrorMessage(body, statusCode);
            return result;
        }
        catch (TaskCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Request timeout";
            return result;
        }
        catch (HttpRequestException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Network error: {ex.Message}";
            return result;
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ImeiInfoApiService", "CheckAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Runs multiple checks on an IMEI in sequence.
    /// </summary>
    public static async Task<List<ImeiCheckResult>> CheckMultipleAsync(
        string imei,
        IEnumerable<ImeiCheckType> checkTypes,
        string apiKey,
        HttpClient? httpClient = null)
    {
        var results = new List<ImeiCheckResult>();
        var checks = checkTypes.ToList();
        for (int i = 0; i < checks.Count; i++)
        {
            var result = await CheckAsync(imei, checks[i], apiKey, httpClient).ConfigureAwait(false);
            results.Add(result);

            // Small delay between requests to be respectful to the API
            if (i < checks.Count - 1)
                await Task.Delay(200).ConfigureAwait(false);
        }
        return results;
    }

    /// <summary>
    /// Calculates total cost for a set of checks.
    /// </summary>
    public static decimal CalculateCost(IEnumerable<ImeiCheckType> checkTypes)
    {
        return checkTypes.Sum(t => CheckPrices[t]);
    }

    /// <summary>
    /// Calculates estimated cost for processing N devices with selected checks.
    /// Apple checks only apply to Apple devices, Samsung checks only to Android
    /// devices, and blacklist checks apply to everything.
    /// </summary>
    public static decimal CalculateEstimatedCost(
        int appleDeviceCount,
        int androidDeviceCount,
        IEnumerable<ImeiCheckType> selectedChecks)
    {
        decimal total = 0;

        foreach (var check in selectedChecks)
        {
            switch (check)
            {
                case ImeiCheckType.AppleCarrierLockFmi:
                    total += CheckPrices[check] * appleDeviceCount;
                    break;
                case ImeiCheckType.BlacklistSimple:
                case ImeiCheckType.BlacklistPremium:
                    total += CheckPrices[check] * (appleDeviceCount + androidDeviceCount);
                    break;
                case ImeiCheckType.SamsungInfoKnox:
                    total += CheckPrices[check] * androidDeviceCount;
                    break;
            }
        }

        return total;
    }

    /// <summary>
    /// Checks the remaining account credits on imei.info.
    /// </summary>
    public static async Task<(bool Success, decimal Balance, string? ErrorMessage)> GetBalanceAsync(
        string apiKey,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, 0, "API key not configured");

        var client = httpClient ?? SharedClient;

        try
        {
            var (statusCode, body) = await SendAsync(
                client, $"/api/account/account/?API_KEY={Uri.EscapeDataString(apiKey)}", apiKey).ConfigureAwait(false);

            if (IsInvalidTokenBody(body))
                return (false, 0, ExtractErrorMessage(body, statusCode));

            if (statusCode != HttpStatusCode.OK)
                return (false, 0, ExtractErrorMessage(body, statusCode));

            if (TryParseBalance(body, out decimal balance))
                return (true, balance, null);

            return (false, 0, "Could not parse balance from response");
        }
        catch (TaskCanceledException)
        {
            return (false, 0, "Request timeout");
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ImeiInfoApiService", "GetBalanceAsync", 1, ex.Message, ex.StackTrace ?? "");
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the numeric service id behind a check by listing the services
    /// the account can buy. Returns 0 when the dashboard does not offer it.
    /// </summary>
    public static async Task<int> ResolveServiceIdAsync(
        ImeiCheckType checkType,
        string apiKey,
        HttpClient? httpClient = null)
    {
        lock (CacheLock)
        {
            if (ServiceIdCache.TryGetValue(apiKey, out var cached) && cached.TryGetValue(checkType, out int hit))
                return hit;
        }

        var client = httpClient ?? SharedClient;
        var services = await FetchServicesAsync(apiKey, client).ConfigureAwait(false);

        // One listing covers every check, so all of them are resolved from it
        // and cached together instead of refetched per check.
        var resolved = new Dictionary<ImeiCheckType, int>();
        foreach (ImeiCheckType type in Enum.GetValues<ImeiCheckType>())
        {
            int id = MatchService(services, type);
            if (id > 0)
                resolved[type] = id;
        }

        lock (CacheLock)
        {
            if (!ServiceIdCache.TryGetValue(apiKey, out var map))
            {
                map = new Dictionary<ImeiCheckType, int>();
                ServiceIdCache[apiKey] = map;
            }

            foreach (var pair in resolved)
                map[pair.Key] = pair.Value;

            return resolved.TryGetValue(checkType, out int serviceId) ? serviceId : 0;
        }
    }

    /// <summary>Throws away resolved service ids; used when the key changes.</summary>
    public static void ResetServiceCache()
    {
        lock (CacheLock)
            ServiceIdCache.Clear();
    }

    // ------------------------------------------------------------------
    // Transport
    // ------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient client, string url, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    // ------------------------------------------------------------------
    // Response handling
    // ------------------------------------------------------------------

    private static async Task<ImeiCheckResult> ApplyEnvelopeAsync(
        ImeiCheckResult result,
        string body,
        HttpStatusCode statusCode,
        string apiKey,
        HttpClient client,
        bool mayPoll)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            result.Success = false;
            result.ErrorMessage = "Unexpected response from imei.info";
            return result;
        }

        var root = doc.RootElement;

        string? status = GetString(root, "status");
        result.Status = status;

        // Whether this body carries an id of its own, which is what tells an
        // envelope apart from a payload that is only about the device.
        int queueId = 0;
        bool bodyCarriesId = false;
        if (TryGetInt(root, "id", out int id))
        {
            queueId = id;
            bodyCarriesId = true;
        }
        else if (TryGetInt(root, "history_id", out int historyId))
        {
            queueId = historyId;
            bodyCarriesId = true;
        }

        // An id only points at a queued search when the envelope says the check
        // is running, or when the 202 itself announces the queue. A payload that
        // carries an id and no state of its own is a finished answer, and reading
        // its id as a history id would send the caller polling for an entry the
        // gateway never queued.
        if (bodyCarriesId && (!string.IsNullOrEmpty(status) || statusCode == HttpStatusCode.Accepted))
            result.RequestId = queueId;

        bool hasResult = root.TryGetProperty("result", out var resultNode)
            && resultNode.ValueKind == JsonValueKind.Object;

        if (hasResult)
        {
            ParseResultNode(result, resultNode);
            result.RawData = resultNode.Clone();
        }
        else
        {
            result.RawData = root.Clone();
        }

        // Only an HTTP 200 answer is read as a result or as a refusal. The 202
        // means the check was queued, so a body that happens to look finished
        // must not be reported as a finished check.
        bool isOk = statusCode == HttpStatusCode.OK;
        bool envelopeless = isOk && !hasResult && string.IsNullOrEmpty(status) && !bodyCarriesId;

        // A body without an envelope is a finished result. The official imei.info
        // SDKs read the absence of both a status and a history id that way, and
        // that is the shape the published sandbox numbers answer in.
        if (envelopeless && LooksLikeResult(root))
        {
            ParseResultNode(result, root);
            result.Success = true;
            return result;
        }

        // A refusal arrives as a lone detail string, and on the api-sync path it
        // arrives with HTTP 200. Read as anything else it looks like a check that
        // has not finished yet, which would leave the caller waiting on an answer
        // the gateway already gave.
        if (envelopeless && TryGetDetail(root, out string detail))
        {
            result.Success = false;
            result.ErrorMessage = detail;
            return result;
        }

        var normalized = (status ?? "").Trim().ToLowerInvariant();

        bool queued = statusCode == HttpStatusCode.Accepted
            || normalized is "pending" or "processing" or "running" or "in_progress"
            || (string.IsNullOrEmpty(normalized) && !hasResult);

        bool finished = !queued
            && (normalized is "done" or "completed"
                || (string.IsNullOrEmpty(normalized) && hasResult));

        if (finished)
        {
            result.Success = true;
            return result;
        }

        if (!queued)
        {
            // Rejected, Refunded, error, failed, cancelled...
            result.Success = false;
            result.ErrorMessage = $"Check returned status: {status}";
            return result;
        }

        if (!mayPoll)
            return result; // still pending; the caller keeps polling

        if (result.RequestId is not int queuedId || queuedId <= 0)
        {
            result.Success = false;
            result.ErrorMessage = "Result is still pending";
            return result;
        }

        for (int attempt = 0; attempt < PollAttempts; attempt++)
        {
            await Task.Delay(PollDelay).ConfigureAwait(false);

            var (pollStatus, pollBody) = await SendAsync(
                client,
                $"/api/search_history/{queuedId}/?API_KEY={Uri.EscapeDataString(apiKey)}",
                apiKey).ConfigureAwait(false);

            if (pollStatus != HttpStatusCode.OK || IsInvalidTokenBody(pollBody))
            {
                result.Success = false;
                result.ErrorMessage = ExtractErrorMessage(pollBody, pollStatus);
                return result;
            }

            // Reset before applying so a body without a status cannot read as
            // the result of the previous round.
            result.Status = null;
            await ApplyEnvelopeAsync(result, pollBody, pollStatus, apiKey, client, mayPoll: false).ConfigureAwait(false);

            // The recursive call has the final word: a finished check sets
            // Success, and a refusal, a rejected status or an unreadable body
            // sets ErrorMessage. Only an envelope that is still pending leaves
            // both untouched, so the wording the gateway gave is never replaced
            // by a guess made here.
            if (result.Success || result.ErrorMessage is not null)
                return result;

            var polled = (result.Status ?? "").Trim().ToLowerInvariant();

            if (polled is "pending" or "processing" or "running" or "in_progress")
                continue;

            if (polled.Length == 0)
            {
                result.Success = false;
                result.ErrorMessage = "Unexpected response from imei.info";
                return result;
            }

            result.Success = false;
            result.ErrorMessage = $"Check returned status: {result.Status}";
            return result;
        }

        result.Success = false;
        result.ErrorMessage = "Result is still pending";
        return result;
    }

    /// <summary>
    /// Reads the service specific result block. Different services fill
    /// different fields, so every read is optional.
    /// </summary>
    private static void ParseResultNode(ImeiCheckResult result, JsonElement node)
    {
        if (node.TryGetProperty("result", out var nested) && nested.ValueKind == JsonValueKind.Object)
            node = nested;

        // Blacklist status: CLEAN / BLACKLISTED, or a plain yes/no flag.
        string? blacklistStatus = GetString(node, "blacklist_status") ?? GetString(node, "blacklist");
        if (!string.IsNullOrWhiteSpace(blacklistStatus))
        {
            result.IsBlacklisted = blacklistStatus.Trim().ToUpperInvariant() is "BLACKLISTED" or "BLACKLIST" or "STOLEN";
        }
        else if (node.TryGetProperty("blacklisted", out var blacklisted))
        {
            if (blacklisted.ValueKind == JsonValueKind.True)
                result.IsBlacklisted = true;
            else if (blacklisted.ValueKind == JsonValueKind.False)
                result.IsBlacklisted = false;
            else if (blacklisted.ValueKind == JsonValueKind.String)
            {
                string? flag = blacklisted.GetString()?.Trim().ToLowerInvariant();
                result.IsBlacklisted = flag is "yes" or "true" or "1" or "blacklisted" or "stolen";
            }
        }

        result.BlacklistReason = GetString(node, "blacklist_reason") ?? GetString(node, "reason");
        result.BlacklistedBy = GetString(node, "blacklisted_by") ?? GetString(node, "reported_by");
        result.BlacklistedCountry = GetString(node, "blacklisted_country") ?? GetString(node, "country");

        string? blacklistedOn = GetString(node, "blacklisted_on") ?? GetString(node, "reported_at");
        if (!string.IsNullOrWhiteSpace(blacklistedOn) && DateTime.TryParse(blacklistedOn, out var when))
            result.BlacklistedOn = when;

        // Carrier and SIM lock
        result.CarrierName = GetString(node, "original_carrier") ?? GetString(node, "carrier");

        if (node.TryGetProperty("carrier_lock", out var lockNode))
        {
            if (lockNode.ValueKind == JsonValueKind.True)
                result.SimLockStatus = "Locked";
            else if (lockNode.ValueKind == JsonValueKind.False)
                result.SimLockStatus = "Unlocked";
            else if (lockNode.ValueKind == JsonValueKind.String)
                result.SimLockStatus = lockNode.GetString();
        }
        else
        {
            result.SimLockStatus = GetString(node, "sim_lock") ?? GetString(node, "simlock_status");
        }

        // Find My iPhone / activation lock
        result.FmiStatus = GetString(node, "fmi_status")
            ?? GetString(node, "find_my_iphone")
            ?? GetString(node, "icloud_status");

        if (result.FmiStatus is null && node.TryGetProperty("activation_lock", out var fmiNode))
        {
            if (fmiNode.ValueKind == JsonValueKind.True)
                result.FmiStatus = "ON";
            else if (fmiNode.ValueKind == JsonValueKind.False)
                result.FmiStatus = "OFF";
            else if (fmiNode.ValueKind == JsonValueKind.String)
                result.FmiStatus = fmiNode.GetString();
        }

        // Device identity
        result.ModelName = GetString(node, "model_name") ?? GetString(node, "model");
        result.Manufacturer = GetString(node, "manufacturer") ?? GetString(node, "brand");
        result.ModelNumber = GetString(node, "model_number") ?? GetString(node, "model_code");
        result.KnoxStatus = GetString(node, "knox_status") ?? GetString(node, "knox");
        result.WarrantyStatus = GetString(node, "warranty_status") ?? GetString(node, "warranty");
    }

    // ------------------------------------------------------------------
    // Service discovery
    // ------------------------------------------------------------------

    private sealed record ServiceInfo(int Id, string Name, string Slug);

    private static async Task<List<ServiceInfo>> FetchServicesAsync(string apiKey, HttpClient client)
    {
        var services = new List<ServiceInfo>();
        string? url = $"/api/service/services/?API_KEY={Uri.EscapeDataString(apiKey)}";
        int page = 0;

        while (!string.IsNullOrEmpty(url) && page++ < 10)
        {
            var (statusCode, body) = await SendAsync(client, url, apiKey).ConfigureAwait(false);

            if (IsInvalidTokenBody(body))
                throw new InvalidOperationException(ExtractErrorMessage(body, statusCode));

            if (statusCode != HttpStatusCode.OK)
                throw new InvalidOperationException(ExtractErrorMessage(body, statusCode));

            AppendServices(body, services);
            url = NextPage(body);
        }

        return services;
    }

    private static void AppendServices(string body, List<ServiceInfo> services)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        JsonElement items;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                items = results;
            else if (root.TryGetProperty("services", out var list) && list.ValueKind == JsonValueKind.Array)
                items = list;
            else
                return;
        }
        else
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryGetInt(item, "id", out int id))
                continue;

            string name = GetString(item, "name") ?? GetString(item, "service") ?? "";
            if (name.Length == 0)
                continue;

            string slug = GetString(item, "slug") ?? "";
            services.Add(new ServiceInfo(id, name, slug));
        }
    }

    private static string? NextPage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String)
            {
                string? url = next.GetString();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }
        }
        catch (JsonException)
        {
            // No pagination envelope to follow.
        }
        return null;
    }

    private static int MatchService(List<ServiceInfo> services, ImeiCheckType checkType)
    {
        if (services.Count == 0)
            return 0;

        string target = CheckNames[checkType];
        string code = ServiceCodes[checkType];

        // 1. The exact name the dashboard shows.
        foreach (var service in services)
        {
            if (string.Equals(service.Name.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
                return service.Id;
        }

        // 2. The slug we store in settings.
        foreach (var service in services)
        {
            if (service.Slug.Length > 0 && Normalize(service.Slug) == Normalize(code))
                return service.Id;
        }

        // 3. Same name once punctuation and case are gone.
        string normalizedTarget = Normalize(target);
        foreach (var service in services)
        {
            if (Normalize(service.Name) == normalizedTarget)
                return service.Id;
        }

        // 4. Unique best word overlap, so a slightly reworded service name
        //    still matches as long as no other service scores higher.
        var targetWords = Words(target);
        int bestScore = 0, secondScore = 0, bestId = 0;

        foreach (var service in services)
        {
            var words = Words(service.Name);
            int score = targetWords.Count(word => words.Contains(word));

            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestId = service.Id;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }

        int threshold = Math.Max(2, (targetWords.Count + 1) / 2);
        return bestId > 0 && bestScore >= threshold && secondScore < bestScore ? bestId : 0;
    }

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static HashSet<string> Words(string value) =>
        new(value.ToLowerInvariant()
            .Split(new[] { ' ', ':', ',', '.', '-', '_', '/', '(', ')', '&', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1),
            StringComparer.Ordinal);

    // ------------------------------------------------------------------
    // Balance
    // ------------------------------------------------------------------

    private static bool TryParseBalance(string body, out decimal balance)
    {
        balance = 0;

        string[] names = { "balance", "credits", "credit_balance", "wallet_balance", "amount", "funds" };

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (ReadBalance(root, names, out balance))
            return true;

        foreach (string container in new[] { "account", "data", "result" })
        {
            if (root.TryGetProperty(container, out var nested)
                && nested.ValueKind == JsonValueKind.Object
                && ReadBalance(nested, names, out balance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadBalance(JsonElement node, string[] names, out decimal balance)
    {
        foreach (string name in names)
        {
            if (!node.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out balance))
                return true;

            // Invariant culture: the gateway writes "12.34", and a Dutch
            // operator would otherwise see that read as 1234.
            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out balance))
                return true;
        }

        balance = 0;
        return false;
    }

    // ------------------------------------------------------------------
    // JSON helpers
    // ------------------------------------------------------------------

    private static string? GetString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetInt(JsonElement node, string property, out int value)
    {
        value = 0;
        if (!node.TryGetProperty(property, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            return true;

        value = 0;
        return false;
    }

    /// <summary>Reads the plain detail string a refusal is wrapped in.</summary>
    private static bool TryGetDetail(JsonElement root, out string detail)
    {
        detail = "";
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("detail", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        detail = value.GetString() ?? "";
        return detail.Length > 0;
    }

    /// <summary>
    /// True when the object carries device fields, which is what a finished
    /// result looks like once the envelope around it is stripped away.
    /// </summary>
    private static bool LooksLikeResult(JsonElement root)
    {
        foreach (string property in ResultFieldNames)
        {
            if (root.TryGetProperty(property, out _))
                return true;
        }

        return false;
    }

    private static readonly HashSet<string> ResultFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "imei", "brand", "model", "model_name", "manufacturer", "model_number",
        "blacklist_status", "blacklisted", "carrier_lock", "original_carrier",
        "purchase_country", "sim_lock", "fmi_status", "knox_status",
        "warranty_status", "specifications"
    };

    /// <summary>
    /// The gateway answers a bad token with HTTP 200 and a detail envelope on
    /// some paths, so the body has to be inspected as well. Only a string
    /// detail mentioning the token counts; a result block that happens to talk
    /// about tokens does not.
    /// </summary>
    private static bool IsInvalidTokenBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!doc.RootElement.TryGetProperty("detail", out var detail))
                return false;
            if (detail.ValueKind != JsonValueKind.String)
                return false;

            return (detail.GetString() ?? "").Contains("token", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Pulls the human readable reason out of an error body. The gateway uses
    /// message, detail and error interchangeably depending on the path, so all
    /// three are tried before falling back to the status code.
    /// </summary>
    private static string ExtractErrorMessage(string body, HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string property in new[] { "message", "detail", "error" })
                {
                    if (!root.TryGetProperty(property, out var value))
                        continue;

                    if (value.ValueKind == JsonValueKind.String)
                    {
                        string text = value.GetString() ?? "";
                        if (text.Length > 0)
                            return text;
                    }
                    else if (value.ValueKind == JsonValueKind.Object
                        && value.TryGetProperty("message", out var nested)
                        && nested.ValueKind == JsonValueKind.String)
                    {
                        string text = nested.GetString() ?? "";
                        if (text.Length > 0)
                            return text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; the status code alone tells the story.
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "Invalid API key",
            HttpStatusCode.Forbidden => "Authentication credentials were not provided",
            HttpStatusCode.PaymentRequired => "Insufficient credits. Add funds at dash.imei.info.",
            HttpStatusCode.NotFound => "Service not found",
            HttpStatusCode.UnprocessableEntity => "Invalid IMEI",
            HttpStatusCode.TooManyRequests => "Rate limit exceeded, try again shortly",
            _ => $"API error: {(int)statusCode}"
        };
    }
}
