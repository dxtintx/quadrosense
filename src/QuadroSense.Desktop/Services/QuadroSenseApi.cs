using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using Profile = QuadroSense.Api.Models.Profile;
using QuadroSense.Api.Models;

namespace QuadroSense.Desktop.Services;

public sealed class QuadroSenseApi : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public const string DefaultBaseUrl = "http://localhost:5176";
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuadroSense", "desktop.json");
    private string _baseUrl = LoadBaseUrl();
    public string BaseUrl
    {
        get => _baseUrl;
        set
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Enter a valid HTTP or HTTPS service address.");
            var address = value.TrimEnd('/');
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { BaseUrl = address }));
            _baseUrl = address;
        }
    }
    private static string LoadBaseUrl()
    {
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var address = settings.RootElement.GetProperty("BaseUrl").GetString();
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")) return address!.TrimEnd('/');
        }
        catch { /* Missing or damaged settings use the local service. */ }
        return DefaultBaseUrl;
    }

    private static async Task CheckAsync(HttpResponseMessage response)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            string? detail = null;
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.String) detail = json.RootElement.GetString();
                else if (json.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString();
                else if (json.RootElement.TryGetProperty("title", out var t)) detail = t.GetString();
            }
            catch (JsonException) { if (!body.TrimStart().StartsWith('<')) detail = body.Trim('"'); }
            throw new ApiException(string.IsNullOrWhiteSpace(detail) ? $"Service returned {(int)response.StatusCode} ({response.ReasonPhrase})." : detail);
        }
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(Url(path), ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        if (!response.IsSuccessStatusCode) await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    public Task<ControllerConfiguration?> GetConfigurationAsync() => GetOptionalAsync<ControllerConfiguration>("/api/controller/configuration");
    public Task<Profile?> GetActiveProfileAsync() => GetOptionalAsync<Profile>("/api/profiles/active");
    public async Task SaveProfileAsync(Profile profile) => await CheckAsync(await _http.PutAsJsonAsync(Url($"/api/profiles/{Uri.EscapeDataString(profile.Id)}"), profile, JsonOptions));
    public async Task DeleteProfileAsync(string id) => await CheckAsync(await _http.DeleteAsync(Url($"/api/profiles/{Uri.EscapeDataString(id)}")));
    public async Task PlayToneAsync() => await CheckAsync(await _http.PostAsJsonAsync(Url("/api/controller/sound/tone"), new { FrequencyHz = 880, DurationMs = 1000, Volume = 0.2 }, JsonOptions));
    private string Url(string path) => $"{BaseUrl.TrimEnd('/')}{path}";

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<string>>(Url("/api/controller/devices"), JsonOptions, ct) ?? [];

    public async Task<Diagnostics?> GetDiagnosticsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<Diagnostics>(Url("/api/controller/diagnostics"), JsonOptions, ct);

    public async Task<ControllerMotion?> GetMotionAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ControllerMotion>(Url("/api/controller/motion"), JsonOptions, ct);

    public async Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<ProfileSummary>>(Url("/api/profiles"), JsonOptions, ct) ?? [];

    public async Task<IReadOnlyList<ButtonBinding>> GetBindingsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<ButtonBinding>>(Url("/api/remapping/bindings"), JsonOptions, ct) ?? [];

    public async Task SaveBindingsAsync(IReadOnlyList<ButtonBinding> bindings, CancellationToken ct = default) =>
        await CheckAsync(await _http.PutAsJsonAsync(Url("/api/remapping/bindings"), bindings, JsonOptions, ct));

    public async Task<IReadOnlyList<MacroSummary>> GetMacrosAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<MacroSummary>>(Url("/api/remapping/macros"), JsonOptions, ct) ?? [];

    public async Task TriggerMacroAsync(string id, CancellationToken ct = default) =>
        await CheckAsync(await _http.PostAsync(Url($"/api/remapping/macros/{Uri.EscapeDataString(id)}/trigger"), null, ct));

    public async Task<TuningConfig?> GetTuningAsync(CancellationToken ct = default) =>
        await GetOptionalAsync<TuningConfig>("/api/tuning", ct);

    public async Task SaveTuningAsync(TuningConfig config, CancellationToken ct = default) =>
        await CheckAsync(await _http.PutAsJsonAsync(Url("/api/tuning"), config, JsonOptions, ct));

    public async Task<IReadOnlyList<ModRegistration>> GetModsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<ModRegistration>>(Url("/api/mods"), JsonOptions, ct) ?? [];

    public async Task RegisterModAsync(string name, string version, CancellationToken ct = default)
    {
        var body = new { Name = name, Version = version, ApiContract = "1.0" };
        await CheckAsync(await _http.PostAsJsonAsync(Url("/api/mods/register"), body, JsonOptions, ct));
    }

    public async Task RemoveModAsync(string id, CancellationToken ct = default) =>
        await CheckAsync(await _http.DeleteAsync(Url($"/api/mods/{Uri.EscapeDataString(id)}"), ct));

    public async Task<Profile?> GetProfileAsync(string id, CancellationToken ct = default) =>
        await GetOptionalAsync<Profile>($"/api/profiles/{Uri.EscapeDataString(id)}", ct);

    public async Task CreateProfileAsync(string id, string name, CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync() ?? throw new ApiException("Controller settings are unavailable.");
        var tuning = await GetOptionalAsync<QuadroSense.Api.Models.TuningConfig>("/api/tuning", ct);
        var bindings = await GetOptionalAsync<List<QuadroSense.Api.Models.ButtonBinding>>("/api/remapping/bindings", ct);
        await SaveProfileAsync(new Profile
        {
            Id = id, Name = name, ColorTag = "#808080",
            LeftTrigger = config.LeftTrigger, RightTrigger = config.RightTrigger,
            Haptics = config.Haptics, Lighting = config.Lighting,
            Tuning = tuning ?? new(), Bindings = bindings ?? []
        });
    }

    public async Task ConnectAsync(CancellationToken ct = default) =>
        await CheckAsync(await _http.PostAsync(Url("/api/controller/connect"), null, ct));

    public async Task ActivateProfileAsync(string id, CancellationToken ct = default) =>
        await CheckAsync(await _http.PostAsync(Url($"/api/profiles/{Uri.EscapeDataString(id)}/activate"), null, ct));

    public async Task ApplyTriggerAsync(string side, string mode, int start, int end, int strength, int frequency, int snapForce, CancellationToken ct = default)
    {
        var body = new { Mode = mode, StartPosition = start, EndPosition = end, Strength = strength, Frequency = frequency, SnapForce = snapForce };
        await CheckAsync(await _http.PutAsJsonAsync(Url($"/api/triggers/{side}"), body, JsonOptions, ct));
    }

    public async Task ResetTriggersAsync(CancellationToken ct = default) =>
        await CheckAsync(await _http.DeleteAsync(Url("/api/triggers"), ct));

    public async Task SetHapticsAsync(bool audioEnabled, string band, double gain, int low = 0, int high = 0, CancellationToken ct = default)
    {
        var body = new { RumbleLow = low, RumbleHigh = high, AudioToHapticEnabled = audioEnabled, AudioBand = band, HapticGain = gain };
        await CheckAsync(await _http.PutAsJsonAsync(Url("/api/haptics"), body, JsonOptions, ct));
    }

    public async Task<HapticState?> GetHapticsStateAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<HapticState>(Url("/api/haptics/state"), JsonOptions, ct);

    public async Task ResetLightingAsync(CancellationToken ct = default) =>
        await CheckAsync(await _http.PostAsync(Url("/api/lighting/reset"), null, ct));

    public async Task SetLightingAsync(int red, int green, int blue, int playerLeds, bool micLed, string effect, int speed, CancellationToken ct = default)
    {
        var body = new { LightbarColor = new { R = red, G = green, B = blue }, PlayerLeds = playerLeds, MicLedEnabled = micLed, DynamicEffect = effect, EffectSpeed = speed };
        await CheckAsync(await _http.PutAsJsonAsync(Url("/api/lighting"), body, JsonOptions, ct));
    }

    public async Task SetPlayerAsync(int player, CancellationToken ct = default) =>
        await CheckAsync(await _http.PostAsync(Url($"/api/lighting/player/{player}"), null, ct));

    public void Dispose() => _http.Dispose();
}

public sealed record Diagnostics(string DevicePath = "", string ConnectionType = "", int BatteryPercent = -1,
    bool IsCharging = false, int? BluetoothRssi = null, string FirmwareVersion = "", string HardwareVersion = "",
    long UptimeSeconds = 0, long ReportsSent = 0);
public sealed record ControllerMotion(double PitchDegrees = 0, double RollDegrees = 0, DateTimeOffset? UpdatedAt = null,
    double LeftX = 0, double LeftY = 0, double RightX = 0, double RightY = 0,
    float Qx = 0, float Qy = 0, float Qz = 0, float Qw = 1);
public sealed record ProfileSummary(string Id, string Name, string ColorTag, bool IsActive);
public sealed record HapticState(bool AudioPipelineRunning = false, string? AudioDevice = null, double CurrentRms = 0,
    int LastReportLeft = 0, int LastReportRight = 0);
public sealed record ButtonBinding(string PhysicalButton, string MappedTo, bool TurboEnabled = false, int TurboRateHz = 10, bool HoldToggle = false);
public sealed record StickConfig(float Deadzone = 0.08f, float AntiDeadzone = 0f, float CurveExponent = 1f);
public sealed record TouchpadConfig(bool MouseEmulation = false, int MouseDpi = 1600, bool InvertX = false, bool InvertY = false);
public sealed record GyroConfig(bool Enabled = false, float Sensitivity = 1f, float DeadzoneDps = 0.5f, string OutputTarget = "rightStick");
public sealed record TuningConfig(StickConfig LeftStick, StickConfig RightStick, TouchpadConfig Touchpad, GyroConfig Gyro);
public sealed record ModRegistration(string Id, string Name, string Version, string ApiContract);
public sealed record MacroSummary(string Id, string Name, List<MacroStepSummary> Steps);
public sealed record MacroStepSummary(string Button, bool Pressed, int DelayMs);

public sealed class ApiException(string message) : Exception(message);
