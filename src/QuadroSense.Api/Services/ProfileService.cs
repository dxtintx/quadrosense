using System.Collections.Concurrent;
using System.Text.Json;
using QuadroSense.Api.Models;

namespace QuadroSense.Api.Services;

/// <summary>
/// Manages named profiles stored as JSON files in %AppData%\QuadroSense\profiles\.
///
/// Auto-switching:
///   A background loop polls running processes every 2s. When a process name
///   matches a profile's AutoSwitchProcesses list, that profile is activated.
///   This is simpler than WMI event subscriptions while being reliable enough
///   for gaming use (2s activation latency is acceptable at game launch).
/// </summary>
public sealed class ProfileService : BackgroundService
{
    private readonly ILogger<ProfileService>   _log;
    private readonly ControllerOutputService   _output;
    private readonly AudioHapticService _audio;
    private readonly RemappingService _remapping;
    private readonly string                    _profileDir;

    private readonly ConcurrentDictionary<string, Profile> _profiles = new();
    private string? _activeProfileId;
    private readonly QuadroSense.Api.HID.ControllerDeviceManager _device;
    private HashSet<string> _previousProcesses = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _heldShortcuts = [];


    public ProfileService(ILogger<ProfileService> log, ControllerOutputService output,
        AudioHapticService audio, RemappingService remapping, QuadroSense.Api.HID.ControllerDeviceManager device)
    {
        _device = device;
        _log        = log;
        _output     = output;
        _audio = audio;
        _remapping = remapping;
        _profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QuadroSense", "profiles");
        Directory.CreateDirectory(_profileDir);
        LoadAll();
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<ProfileSummary> ListProfiles() =>
        _profiles.Values
            .Select(p => new ProfileSummary(p.Id, p.Name, p.ColorTag, p.Id == _activeProfileId))
            .ToList();

    public Profile? GetProfile(string id) =>
        _profiles.TryGetValue(id, out var p) ? p : null;

    public Profile UpsertProfile(Profile profile)
    {
        _profiles[profile.Id] = profile;
        Save(profile);
        return profile;
    }

    public bool DeleteProfile(string id)
    {
        if (!_profiles.TryRemove(id, out _)) return false;
        if (_activeProfileId == id) _activeProfileId = null;
        var path = ProfilePath(id);
        if (File.Exists(path)) File.Delete(path);
        return true;
    }

    public bool ActivateProfile(string id)
    {
        if (!_profiles.TryGetValue(id, out var profile)) return false;
        _activeProfileId = id;
        _output.ApplyProfile(profile);
        _audio.ApplyConfig(profile.Haptics);
        _remapping.SetBindings(profile.Bindings);
        _log.LogInformation("Activated profile {Name} ({Id})", profile.Name, id);
        return true;
    }

    public Profile? GetActiveProfile() =>
        _activeProfileId is not null && _profiles.TryGetValue(_activeProfileId, out var p) ? p : null;

    // ── Auto-switch background loop ──────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextProcessCheck = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextProcessCheck)
                {
                    AutoSwitch();
                    nextProcessCheck = DateTimeOffset.UtcNow.AddSeconds(2);
                }
                var motion = _device.Motion;
                var pressed = motion.UpdatedAt is { } time && DateTimeOffset.UtcNow - time < TimeSpan.FromSeconds(1)
                    ? (motion.Buttons ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
                var held = new HashSet<string>();
                string? activate = null;
                foreach (var profile in _profiles.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
                {
                    if (profile.ActivationButtons.Count < 2 || !profile.ActivationButtons.All(pressed.Contains)) continue;
                    held.Add(profile.Id);
                    if (!_heldShortcuts.Contains(profile.Id) && activate is null) activate = profile.Id;
                }
                _heldShortcuts = held;
                if (activate is not null && activate != _activeProfileId) ActivateProfile(activate);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Auto-switch error"); }
            await Task.Delay(30, stoppingToken).ConfigureAwait(false);
        }
    }

    private void AutoSwitch()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try { running.Add(process.ProcessName); } catch (InvalidOperationException) { }
            }
        }
        var started = running.Except(_previousProcesses, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _previousProcesses = running;
        foreach (var profile in _profiles.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (profile.AutoSwitchProcesses.Any(proc => started.Contains(Path.GetFileNameWithoutExtension(proc.Trim()))))
            {
                if (profile.Id != _activeProfileId) ActivateProfile(profile.Id);
                return;
            }
        }
    }

    private void LoadAll()
    {
        foreach (var file in Directory.EnumerateFiles(_profileDir, "*.json"))
        {
            try
            {
                var json    = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<Profile>(json);
                if (profile is not null) _profiles[profile.Id] = profile;
            }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to load profile {File}", file); }
        }
    }

    private void Save(Profile profile)
    {
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProfilePath(profile.Id), json);
    }

    private string ProfilePath(string id) => Path.Combine(_profileDir, $"{id}.json");
}
