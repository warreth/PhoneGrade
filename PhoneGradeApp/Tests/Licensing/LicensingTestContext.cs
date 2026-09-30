using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhoneGrade.Core.Licensing;

namespace Tests;

/// <summary>
/// Shared fixture for the licensing tests: a settings.json and a
/// sys_cache.dat in a per-test temp folder plus a fake Lemon Squeezy transport.
/// The real profile is never touched, and the folder is deleted on dispose.
/// </summary>
public sealed class LicensingTestContext : IDisposable
{
    public LicensingTestContext()
    {
        Root = Path.Combine(Path.GetTempPath(), $"licensing-{Guid.NewGuid():N}");
        SettingsPath = Path.Combine(Root, "settings.json");
        BackupPath = Path.Combine(Root, "PhoneGrade", "sys_cache.dat");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string SettingsPath { get; }
    public string BackupPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        catch { /* temp cleanup never fails a test */ }
    }

    public TrialStateStore CreateStore() => TrialStateStore.CreateDefault(SettingsPath, BackupPath);

    /// <summary>Builds a gate over this context's files, seeded with <paramref name="initial"/>.</summary>
    public TrialGate CreateGate(FakeLicenseServer server, TrialState? initial = null, TimeProvider? clock = null)
    {
        TrialStateStore store = CreateStore();
        store.Save(initial ?? new TrialState());
        return new TrialGate(store, new LemonSqueezyClient(server), clock ?? TimeProvider.System);
    }

    public TrialState? ReadSettingsState() => ReadSettingsToken() is { } token
        ? TrialStateCipher.TryDecryptState(token)
        : null;

    public TrialState? ReadBackupState() => File.Exists(BackupPath)
        ? TrialStateCipher.TryDecryptState(File.ReadAllText(BackupPath))
        : null;

    public string? ReadSettingsToken()
    {
        if (!File.Exists(SettingsPath)) return null;
        return JsonNode.Parse(File.ReadAllText(SettingsPath))?["TrialToken"]?.GetValue<string>();
    }

    public void SeedSettingsToken(string rawToken)
    {
        var document = new JsonObject { ["Theme"] = "Dark", ["TrialToken"] = rawToken };
        File.WriteAllText(SettingsPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SeedBackupRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, content);
    }

    /// <summary>Stands in for api.lemonsqueezy.com. Records how many validations actually hit it.</summary>
    public sealed class FakeLicenseServer : HttpMessageHandler
    {
        public const string ActiveJson = """{"valid":true,"license_key":{"id":1,"status":"active"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
        public const string ExpiredJson = """{"valid":false,"license_key":{"id":1,"status":"expired"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
        public const string DeactivatedJson = """{"valid":false,"license_key":{"id":1,"status":"deactivated"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
        public const string InvalidKeyJson = """{"valid":false,"error":"This key is invalid."}""";
        public const string OtherProductJson = """{"valid":true,"license_key":{"id":2,"status":"active"},"meta":{"store_id":1,"product_id":9999999,"product_name":"Some Other Product"}}""";

        public string ResponseJson { get; set; } = ActiveJson;
        public bool FailWithNetworkError { get; set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (FailWithNetworkError)
                throw new HttpRequestException("network down");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Clock the gate reads instead of the real one, for cache expiry tests.</summary>
    public sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeClock(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
