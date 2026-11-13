using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QuadroSense.IntegrationTests;

/// <summary>
/// Thin typed wrapper around HttpClient so test methods stay readable.
/// All methods throw TestFailException on unexpected HTTP status.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    }

    // ── Controller ───────────────────────────────────────────────────────────

    public Task<List<string>> GetDevicesAsync() =>
        GetAsync<List<string>>("api/controller/devices");

    public Task<ControllerDiagnostics> ConnectAsync(string? path = null)
    {
        var url = path is null ? "api/controller/connect" : $"api/controller/connect?devicePath={Uri.EscapeDataString(path)}";
        return PostEmptyAsync<ControllerDiagnostics>(url, expected: HttpStatusCode.OK);
    }

    public Task<ControllerDiagnostics> GetDiagnosticsAsync() =>
        GetAsync<ControllerDiagnostics>("api/controller/diagnostics");

    public Task PlayToneAsync(int frequencyHz = 880, int durationMs = 1500, float volume = 0.6f) =>
        PostAsync<object>("api/controller/sound/tone", new { FrequencyHz = frequencyHz, DurationMs = durationMs, Volume = volume }, expected: HttpStatusCode.OK);

    // ── Triggers ─────────────────────────────────────────────────────────────

    public Task SetTriggerAsync(string side, object config) =>
        PutAsync($"api/triggers/{side}", config, expected: HttpStatusCode.NoContent);

    public Task ResetTriggersAsync() =>
        DeleteAsync("api/triggers", expected: HttpStatusCode.NoContent);

    // ── Haptics ──────────────────────────────────────────────────────────────

    public Task SetHapticsAsync(object config) =>
        PutAsync("api/haptics", config, expected: HttpStatusCode.NoContent);

    public Task<HapticState> GetHapticStateAsync() =>
        GetAsync<HapticState>("api/haptics/state");

    // ── Lighting ─────────────────────────────────────────────────────────────

    public Task SetLightingAsync(object config) =>
        PutAsync("api/lighting", config, expected: HttpStatusCode.NoContent);

    public Task ResetLightingAsync() =>
        PostEmptyRawAsync("api/lighting/reset", expected: HttpStatusCode.NoContent);

    public Task SetPlayerAsync(int playerNumber) =>
        PostEmptyRawAsync($"api/lighting/player/{playerNumber}", expected: HttpStatusCode.NoContent);

    // ── Profiles ─────────────────────────────────────────────────────────────

    public Task<List<ProfileSummary>> ListProfilesAsync() =>
        GetAsync<List<ProfileSummary>>("api/profiles");

    public Task<JsonElement> UpsertProfileAsync(string id, object profile) =>
        PutAsync<JsonElement>($"api/profiles/{id}", profile);

    public Task ActivateProfileAsync(string id) =>
        PostEmptyRawAsync($"api/profiles/{id}/activate", expected: HttpStatusCode.NoContent);

    public Task<HttpStatusCode> GetActiveProfileStatusAsync() =>
        StatusAsync("api/profiles/active");

    public Task DeleteProfileAsync(string id) =>
        DeleteAsync($"api/profiles/{id}", expected: HttpStatusCode.NoContent);

    // ── Remapping ────────────────────────────────────────────────────────────

    public Task SetBindingsAsync(object bindings) =>
        PutAsync("api/remapping/bindings", bindings, expected: HttpStatusCode.NoContent);

    public Task<JsonElement> GetBindingsAsync() =>
        GetAsync<JsonElement>("api/remapping/bindings");

    public Task<JsonElement> UpsertMacroAsync(string id, object macro) =>
        PutAsync<JsonElement>($"api/remapping/macros/{id}", macro);

    public Task<HttpStatusCode> TriggerMacroAsync(string id) =>
        StatusAsync($"api/remapping/macros/{id}/trigger", HttpMethod.Post);

    public Task DeleteMacroAsync(string id) =>
        DeleteAsync($"api/remapping/macros/{id}", expected: HttpStatusCode.NoContent);

    // ── Tuning ───────────────────────────────────────────────────────────────

    public Task<JsonElement> SetTuningAsync(object config) =>
        PutAsync<JsonElement>("api/tuning", config);

    public Task<HttpStatusCode> GetTuningStatusAsync() =>
        StatusAsync("api/tuning");

    // ── Mods ─────────────────────────────────────────────────────────────────

    public Task<ModRegistration> RegisterModAsync(object reg) =>
        PostAsync<ModRegistration>("api/mods/register", reg, expected: HttpStatusCode.Created);

    public Task<HttpStatusCode> PushModEventAsync(string modId, object ev) =>
        StatusAsync($"api/mods/{modId}/event", HttpMethod.Post, body: ev);

    public Task UnregisterModAsync(string modId) =>
        DeleteAsync($"api/mods/{modId}", expected: HttpStatusCode.NoContent);

    // ── Health (generic ping) ────────────────────────────────────────────────

    public async Task<bool> IsAliveAsync()
    {
        try
        {
            var r = await _http.GetAsync("api/controller/devices");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Primitives ───────────────────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string url)
    {
        var r = await _http.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<T>(_json))!;
    }

    private async Task PutAsync(string url, object body, HttpStatusCode expected)
    {
        var r = await _http.PutAsJsonAsync(url, body, _json);
        AssertStatus(r, expected, url);
    }

    private async Task<T> PutAsync<T>(string url, object body)
    {
        var r = await _http.PutAsJsonAsync(url, body, _json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<T>(_json))!;
    }

    private async Task<T> PostAsync<T>(string url, object body, HttpStatusCode expected)
    {
        var r = await _http.PostAsJsonAsync(url, body, _json);
        AssertStatus(r, expected, url);
        return (await r.Content.ReadFromJsonAsync<T>(_json))!;
    }

    private async Task<T> PostEmptyAsync<T>(string url, HttpStatusCode expected)
    {
        var r = await _http.PostAsync(url, null);
        AssertStatus(r, expected, url);
        return (await r.Content.ReadFromJsonAsync<T>(_json))!;
    }

    private async Task PostEmptyRawAsync(string url, HttpStatusCode expected)
    {
        var r = await _http.PostAsync(url, null);
        AssertStatus(r, expected, url);
    }

    private async Task DeleteAsync(string url, HttpStatusCode expected)
    {
        var r = await _http.DeleteAsync(url);
        AssertStatus(r, expected, url);
    }

    private async Task<HttpStatusCode> StatusAsync(string url, HttpMethod? method = null, object? body = null)
    {
        method ??= HttpMethod.Get;
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: _json);
        var r = await _http.SendAsync(req);
        return r.StatusCode;
    }

    private static void AssertStatus(HttpResponseMessage r, HttpStatusCode expected, string url)
    {
        if (r.StatusCode != expected)
        {
            var body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new TestFailException(
                $"{url} → {(int)r.StatusCode} {r.StatusCode} (expected {(int)expected} {expected})" +
                (string.IsNullOrWhiteSpace(body) ? "" : $"\n  body: {body}"));
        }
    }

    public void Dispose() => _http.Dispose();
}
