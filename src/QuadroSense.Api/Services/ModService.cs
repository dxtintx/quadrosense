using System.Collections.Concurrent;
using QuadroSense.Api.Models;

namespace QuadroSense.Api.Services;

/// <summary>
/// Manages third-party mod registrations and routes ModEvents to the
/// controller output pipeline with priority and TTL enforcement.
///
/// Design:
///   - Mods are registered (GET a token back), then POST events by mod ID.
///   - Events override controller state for DurationMs, then revert.
///   - Multiple mods can be active; last-write wins per subsystem.
///   - A watchdog background task expires stale overrides.
///
/// This is the hardest service to get right because:
///   1. Real-time override must beat the 16ms frame budget.
///   2. TTL cleanup must not race with active writes (uses channel + timestamp).
/// </summary>
public sealed class ModService : BackgroundService
{
    private readonly ILogger<ModService>      _log;
    private readonly ControllerOutputService  _output;

    private readonly ConcurrentDictionary<string, ModRegistration> _registrations = new();

    private record ActiveOverride(ModEvent Event, DateTime ExpiresAt);
    private readonly ConcurrentDictionary<string, ActiveOverride> _activeOverrides = new();

    public ModService(ILogger<ModService> log, ControllerOutputService output)
    {
        _log    = log;
        _output = output;
    }

    public ModRegistration Register(ModRegistration reg)
    {
        _registrations[reg.Id] = reg;
        _log.LogInformation("Mod registered: {Name} v{Version}", reg.Name, reg.Version);
        return reg;
    }

    public bool Unregister(string modId)
    {
        _activeOverrides.TryRemove(modId, out _);
        _output.RemoveModOverride(modId);
        return _registrations.TryRemove(modId, out _);
    }

    public IReadOnlyList<ModRegistration> ListRegistrations() =>
        _registrations.Values.ToList();

    /// <summary>
    /// Applies a mod event immediately. Returns false if mod is not registered.
    /// </summary>
    public async Task<bool> PushEventAsync(ModEvent ev)
    {
        if (!_registrations.ContainsKey(ev.ModId))
        {
            _log.LogWarning("Unknown mod id: {ModId}", ev.ModId);
            return false;
        }

        var expiry = ev.DurationMs switch
        {
            0  => DateTime.UtcNow,                         // one-shot
            -1 => DateTime.MaxValue,                       // until next event
            _  => DateTime.UtcNow.AddMilliseconds(ev.DurationMs)
        };

        _activeOverrides[ev.ModId] = new ActiveOverride(ev, expiry);

        await _output.ApplyModEventAsync(ev).ConfigureAwait(false);
        return true;
    }

    // ── TTL watchdog ──────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(50, stoppingToken).ConfigureAwait(false); // 50ms sweep

            var now = DateTime.UtcNow;
            foreach (var (modId, ov) in _activeOverrides)
            {
                if (now >= ov.ExpiresAt)
                {
                    ((ICollection<KeyValuePair<string, ActiveOverride>>)_activeOverrides)
                        .Remove(new(modId, ov));
                    _log.LogDebug("Mod override expired for {ModId}", modId);
                    // Output service independently expires its leases and merges remaining mods.
                }
            }
        }
    }
}
