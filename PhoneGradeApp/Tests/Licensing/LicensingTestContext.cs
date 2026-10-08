using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// A fingerprint a test can hand to the gate, so activation can be exercised
    /// without reading the real machine id. <see cref="TestFingerprint"/> is what
    /// every test gets unless it is about an unidentifiable machine.
    /// </summary>
    public static MachineFingerprint TestFingerprint { get; } = new()
    {
        IsAvailable = true,
        Value = "pg-0123456789abcdef0123456789abcdef",
        Origin = MachineFingerprint.Source.WindowsMachineGuid
    };

    /// <summary>
    /// A machine the operating system would not identify. Activating must not
    /// happen here, and the free tier must keep working.
    /// </summary>
    public static MachineFingerprint UnidentifiableMachine { get; } = MachineFingerprint.Unavailable;

    /// <summary>Builds a gate over this context's files, seeded with <paramref name="initial"/>.</summary>
    public TrialGate CreateGate(
        FakeLicenseServer server,
        TrialState? initial = null,
        TimeProvider? clock = null,
        MachineFingerprint? fingerprint = null)
    {
        TrialStateStore store = CreateStore();
        store.Save(initial ?? new TrialState());
        return new TrialGate(
            store,
            new LemonSqueezyClient(server),
            clock ?? TimeProvider.System,
            fingerprint ?? TestFingerprint);
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

    /// <summary>
    /// Stands in for api.lemonsqueezy.com.
    ///
    /// One handler answers all three endpoints, because the point of several tests
    /// is what the gate does in a sequence of calls: which endpoint it reached,
    /// which instance it named, and whether it called activate at all. A single
    /// <c>ResponseJson</c> is kept for the validate tests that predate activate,
    /// and the activate and deactivate bodies have their own slots so a test can
    /// make the key validate and the seat list refuse, which is the case the UI has
    /// to be able to say something specific about.
    /// </summary>
    public sealed class FakeLicenseServer : HttpMessageHandler
    {
        public const string ActiveJson = """{"valid":true,"license_key":{"id":1,"status":"active"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
        public const string ExpiredJson = """{"valid":false,"license_key":{"id":1,"status":"expired"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
        public const string DeactivatedJson = """{"valid":false,"license_key":{"id":1,"status":"deactivated"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
        public const string InvalidKeyJson = """{"valid":false,"error":"This key is invalid."}""";
        public const string OtherProductJson = """{"valid":true,"license_key":{"id":2,"status":"active"},"meta":{"store_id":1,"product_id":9999999,"product_name":"Some Other Product"}}""";

        /// <summary>An active key with room left and one seat already taken.</summary>
        public const string SeatsLeftJson = """
            {"valid":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":2},"instance":{"id":991,"name":"pg-some-other-machine","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"store_id":1,"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>The same key with this computer holding a seat on it.</summary>
        public const string SeatHeldJson = """
            {"valid":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":3},"instance":{"id":992,"name":"pg-0123456789abcdef0123456789abcdef","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"store_id":1,"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>
        /// The key itself is healthy, but the seat this computer named is no longer
        /// activated on it because it was released elsewhere. The API says so with
        /// a 200, <c>valid: false</c> and the instance still echoed back, which is
        /// why the <c>valid</c> flag and not the key status is what the gate reads.
        /// </summary>
        public const string SeatNotHeldJson = """
            {"valid":false,"error":"The license instance is not activated.","license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":3},"instance":{"id":992,"name":"pg-0123456789abcdef0123456789abcdef","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"store_id":1,"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>Every seat taken. The API says this with a 200 and a sentence, not with a status code.</summary>
        public const string ActivationLimitJson = """
            {"activated":false,"error":"This license key has reached the activation limit.","license_key":{"id":7,"status":"active","activation_limit":2,"activation_usage":2},"meta":{"product_id":1422604,"variant_id":11}}
            """;

        /// <summary>A seat was created.</summary>
        public const string ActivatedJson = """
            {"activated":true,"error":null,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":1},"instance":{"id":994,"name":"pg-0123456789abcdef0123456789abcdef","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>
        /// A seat was created on a key whose plan reports no limit at all, which is
        /// what an old or partially configured product looks like. The panel must
        /// draw no seat fraction rather than "0 of 0".
        /// </summary>
        public const string ActivatedNoLimitJson = """
            {"activated":true,"error":null,"license_key":{"id":7,"status":"active"},"instance":{"id":995,"name":"pg-0123456789abcdef0123456789abcdef","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"product_id":1422604}}
            """;

        /// <summary>
        /// A seat was created on a plan of five with one already taken, which is
        /// what the panel draws as a seat fraction.
        /// </summary>
        public const string ActivatedWithSeatsJson = """
            {"activated":true,"error":null,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":3},"instance":{"id":996,"name":"pg-0123456789abcdef0123456789abcdef","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>The seat was handed back.</summary>
        public const string SeatReleasedJson = """
            {"deactivated":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":0},"meta":{"product_id":1422604,"variant_id":12}}
            """;

        /// <summary>What validate answers with.</summary>
        public string ResponseJson { get; set; } = ActiveJson;

        /// <summary>What activate answers with.</summary>
        public string ActivateResponseJson { get; set; } = ActivatedJson;

        /// <summary>What deactivate answers with.</summary>
        public string DeactivateResponseJson { get; set; } = SeatReleasedJson;

        public bool FailWithNetworkError { get; set; }
        public int CallCount { get; private set; }

        /// <summary>Every endpoint that was reached, in order.</summary>
        public List<string> Endpoints { get; } = [];

        /// <summary>Every form body that was sent, in order, parsed into fields.</summary>
        public List<Dictionary<string, string>> Forms { get; } = [];

        /// <summary>The instance ids the caller named on validate, in order.</summary>
        public List<string> NamedInstanceIds => Forms
            .Select(form => form.TryGetValue("instance_id", out string? id) ? id : "")
            .ToList();

        /// <summary>The instance names the caller claimed on activate, in order.</summary>
        public List<string> ClaimedInstanceNames => Forms
            .Where(form => form.ContainsKey("instance_name"))
            .Select(form => form["instance_name"])
            .ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            string endpoint = request.RequestUri!.AbsoluteUri;
            Endpoints.Add(endpoint);
            Forms.Add(await ReadForm(request, cancellationToken));

            if (FailWithNetworkError)
                throw new HttpRequestException("network down");

            string body = endpoint switch
            {
                LemonSqueezyClient.ActivateEndpoint => ActivateResponseJson,
                LemonSqueezyClient.DeactivateEndpoint => DeactivateResponseJson,
                _ => ResponseJson
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        private static async Task<Dictionary<string, string>> ReadForm(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            if (request.Content is null) return form;

            string body = await request.Content.ReadAsStringAsync(cancellationToken);
            foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = pair.IndexOf('=');
                if (separator < 0) continue;
                string name = Uri.UnescapeDataString(pair[..separator].Replace('+', ' '));
                string value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
                form[name] = value;
            }
            return form;
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
