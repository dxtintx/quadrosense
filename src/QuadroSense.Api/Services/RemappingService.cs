using System.Collections.Concurrent;
using QuadroSense.Api.Models;

namespace QuadroSense.Api.Services;

/// <summary>
/// Manages button remapping and macro execution.
///
/// Why in-process instead of kernel driver?
///   For a pure API layer (no UI), keeping remapping in user-space avoids
///   KMDF signing requirements. Games receive input through the virtual
///   controller (ViGEm, or the DS5 passthrough), so we intercept at the
///   HID input report level and re-emit to the virtual device.
///
/// Macro execution:
///   Steps are played sequentially with their DelayMs gaps on a dedicated
///   thread to avoid blocking the HTTP request thread.
/// </summary>
public sealed class RemappingService
{
    private readonly ILogger<RemappingService>              _log;
    private readonly ConcurrentDictionary<string, ButtonBinding> _bindings = new();
    private readonly ConcurrentDictionary<string, Macro>    _macros   = new();

    public RemappingService(ILogger<RemappingService> log) => _log = log;

    // ── Bindings ──────────────────────────────────────────────────────────────

    public void SetBindings(IEnumerable<ButtonBinding> bindings)
    {
        _bindings.Clear();
        foreach (var b in bindings) _bindings[b.PhysicalButton] = b;
    }

    public IReadOnlyList<ButtonBinding> GetBindings() => _bindings.Values.ToList();

    // ── Macros ────────────────────────────────────────────────────────────────

    public Macro UpsertMacro(Macro macro)
    {
        _macros[macro.Id] = macro;
        return macro;
    }

    public bool DeleteMacro(string id) => _macros.TryRemove(id, out _);

    public IReadOnlyList<Macro> GetMacros() => _macros.Values.ToList();

    public Macro? GetMacro(string id) => _macros.TryGetValue(id, out var m) ? m : null;

    /// <summary>
    /// Triggers a macro playback asynchronously.
    /// Returns immediately — playback runs on a background task.
    /// </summary>
    public Task<bool> TriggerMacroAsync(string macroId)
    {
        if (!_macros.TryGetValue(macroId, out var macro))
        {
            _log.LogWarning("Macro {Id} not found", macroId);
            return Task.FromResult(false);
        }

        _ = Task.Run(async () =>
        {
            _log.LogInformation("Executing macro {Name}", macro.Name);
            foreach (var step in macro.Steps)
            {
                // In full implementation: inject virtual button press/release
                // into the virtual HID device via ViGEm bus or DS5 passthrough.
                _log.LogDebug("Step: {Button} {State}", step.Button, step.Pressed ? "DOWN" : "UP");
                if (step.DelayMs > 0)
                    await Task.Delay(step.DelayMs).ConfigureAwait(false);
            }
        });

        return Task.FromResult(true);
    }
}
