using QuadroSense.Api.HID;
using QuadroSense.Api.Models;

namespace QuadroSense.Api.Services;

/// <summary>
/// Central singleton that owns the HID stream and serializes all output reports.
///
/// Why a dedicated serialization lock?
///   DS5 output reports must not interleave — each write is an atomic 48/78-byte
///   transfer. Multiple concurrent callers (mod system, audio haptic, profile
///   activation) are merged here before the single write.
/// </summary>
public sealed class ControllerOutputService : IDisposable
{
    private readonly ILogger<ControllerOutputService> _log;
    private readonly ControllerDeviceManager          _device;
    private readonly SemaphoreSlim                    _writeLock = new(1, 1);
    private readonly CancellationTokenSource           _effectsCts = new();
    private Task                                       _effectsTask = Task.CompletedTask;

    // Current "base" state — overridden by mod events
    private AdaptiveTriggerConfig _leftTrigger  = new();
    private AdaptiveTriggerConfig _rightTrigger = new();
    private HapticConfig          _haptics      = new();
    private LightingConfig        _lighting     = new();
    private string                _connType     = "USB";
    private long                  _effectFrame;
    private volatile bool         _shuttingDown;
    private int                   _currentRumbleLow;
    private int                   _currentRumbleHigh;
    private sealed record Override(ModEvent Event, long ExpiresAt, long Order);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Override> _modOverrides = new();
    private long _overrideOrder;
    private int _restoreOneShot;

    public ControllerOutputService(
        ILogger<ControllerOutputService> log,
        ControllerDeviceManager device)
    {
        _log    = log;
        _device = device;
        _effectsTask = RunLightingEffectsAsync();
    }

    public bool TryConnect(string? path = null)
    {
        bool ok = _device.TryOpen(path);
        if (ok)
        {
            _connType = _device.GetDiagnostics().ConnectionType;
            _log.LogInformation("Connected to DS5 at {Path} ({Conn})", _device.DevicePath, _connType);

            // Immediately set active connected state: PlayStation Blue, Player 1 LED, mic off
            // This stops the controller from showing the orange breathing charging animation
            _ = SetDefaultConnectedStateAsync();
        }
        return ok;
    }

    /// <summary>
    /// Sets default connected state: PlayStation Blue lightbar, Player 1 indicator LED, mic LED off.
    /// Cancels any orange hardware charging animation.
    /// </summary>
    public Task SetDefaultConnectedStateAsync(int playerNumber = 1)
    {
        byte playerMask = playerNumber switch
        {
            1 => 0x04, // Center LED (Player 1)
            2 => 0x0A, // 2 LEDs (Player 2)
            3 => 0x15, // 3 LEDs (Player 3)
            4 => 0x1B, // 4 LEDs (Player 4)
            _ => 0x04
        };

        _lighting = new LightingConfig
        {
            LightbarColor = new RgbColor(0, 0, 255), // Solid PlayStation Blue
            PlayerLeds    = playerMask,
            MicLedEnabled = false,                   // Ensure orange mic LED is OFF
            DynamicEffect = "none"
        };
        _leftTrigger  = new AdaptiveTriggerConfig(); // Off
        _rightTrigger = new AdaptiveTriggerConfig(); // Off
        _haptics      = new HapticConfig();          // Off

        return FlushAsync();
    }

    public ControllerDiagnostics GetDiagnostics() => _device.GetDiagnostics();

    public ControllerConfiguration GetConfiguration() => new(_leftTrigger, _rightTrigger, _haptics, _lighting);

    public void ApplyProfile(Profile p)
    {
        _leftTrigger  = p.LeftTrigger;
        _rightTrigger = p.RightTrigger;
        _haptics      = p.Haptics;
        _lighting     = p.Lighting;
        _ = FlushAsync();
    }

    public void SetTrigger(TriggerSide side, AdaptiveTriggerConfig cfg)
    {
        if (side == TriggerSide.Left)  _leftTrigger  = cfg;
        else                           _rightTrigger = cfg;
        _ = FlushAsync();
    }

    public void SetLighting(LightingConfig cfg)
    {
        _lighting = cfg;
        Interlocked.Exchange(ref _effectFrame, 0);
        _ = FlushAsync();
    }

    private async Task RunLightingEffectsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
        try
        {
            while (await timer.WaitForNextTickAsync(_effectsCts.Token).ConfigureAwait(false))
            {
                var cfg = _lighting;
                if (!_device.IsOpen) continue;
                // Keep leases alive through every output source and restore base state on expiry.
                var expired = false;
                foreach (var entry in _modOverrides)
                    if (Environment.TickCount64 >= entry.Value.ExpiresAt)
                        expired |= ((ICollection<KeyValuePair<string, Override>>)_modOverrides).Remove(entry);
                if (string.Equals(cfg.DynamicEffect, "none", StringComparison.OrdinalIgnoreCase))
                {
                    if (expired || Interlocked.Exchange(ref _restoreOneShot, 0) != 0 || !_modOverrides.IsEmpty)
                        await FlushAsync().ConfigureAwait(false);
                    continue;
                }

                long frame = Interlocked.Increment(ref _effectFrame);
                double phase = frame * Math.Clamp(cfg.EffectSpeed / 255.0, 0.02, 1.0) * 0.035;
                double level = cfg.DynamicEffect switch
                {
                    "breathing" or "battery" => 0.12 + 0.88 * (0.5 + 0.5 * Math.Sin(phase)),
                    "rainbow" => 1.0,
                    "audio_reactive" => Math.Clamp(0.18 + (Volatile.Read(ref _currentRumbleLow) + Volatile.Read(ref _currentRumbleHigh)) / 510.0, 0.12, 1.0),
                    _ => 1.0
                };

                RgbColor color = cfg.DynamicEffect switch
                {
                    "rainbow" => HueColor((phase * 28) % 360, level),
                    "audio_reactive" => ScaleColor(cfg.LightbarColor, level),
                    _ => ScaleColor(cfg.LightbarColor, level)
                };
                await FlushAsync(lighting: cfg with { LightbarColor = color }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_effectsCts.IsCancellationRequested) { }
    }

    private static RgbColor ScaleColor(RgbColor color, double scale) => new(
        (byte)Math.Clamp((int)(color.R * scale), 0, 255),
        (byte)Math.Clamp((int)(color.G * scale), 0, 255),
        (byte)Math.Clamp((int)(color.B * scale), 0, 255));

    private static RgbColor HueColor(double hue, double brightness)
    {
        double chroma = brightness;
        double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0.0), < 120 => (x, chroma, 0.0), < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma), < 300 => (x, 0.0, chroma), _ => (chroma, 0.0, x)
        };
        return new((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public void SetHaptics(HapticConfig cfg)
    {
        _haptics = cfg;
        Volatile.Write(ref _currentRumbleLow, cfg.RumbleLow);
        Volatile.Write(ref _currentRumbleHigh, cfg.RumbleHigh);
        _ = FlushAsync();
    }

    /// <summary>Direct low-latency rumble write (used by audio-haptic pipeline).</summary>
    public Task SetHapticsDirectAsync(byte rumbleLow, byte rumbleHigh)
    {
        var h = _haptics with { RumbleLow = rumbleLow, RumbleHigh = rumbleHigh };
        Volatile.Write(ref _currentRumbleLow, rumbleLow);
        Volatile.Write(ref _currentRumbleHigh, rumbleHigh);
        return FlushAsync(haptics: h);
    }

    /// <summary>Apply a mod event override.</summary>
    public Task ApplyModEventAsync(ModEvent ev)
    {
        if (ev.DurationMs == 0)
        {
            _modOverrides.TryRemove(ev.ModId, out _);
            Interlocked.Exchange(ref _restoreOneShot, 1);
            return FlushAsync(ev.LeftTrigger, ev.RightTrigger, ev.Haptics, ev.Lighting);
        }
        var expiry = ev.DurationMs == -1 ? long.MaxValue : Environment.TickCount64 + Math.Max(1, ev.DurationMs);
        _modOverrides[ev.ModId] = new(ev, expiry, Interlocked.Increment(ref _overrideOrder));
        return FlushAsync();
    }

    public void RemoveModOverride(string modId)
    {
        _modOverrides.TryRemove(modId, out _);
        _ = FlushAsync();
    }

    private async Task FlushAsync(
        AdaptiveTriggerConfig? leftTrigger  = null,
        AdaptiveTriggerConfig? rightTrigger = null,
        HapticConfig?          haptics      = null,
        LightingConfig?        lighting     = null,
        bool shutdownReset = false)
    {
        if (!_device.IsOpen) return;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_shuttingDown && !shutdownReset) return;
            var lt = leftTrigger  ?? _leftTrigger;
            var rt = rightTrigger ?? _rightTrigger;
            var h  = haptics      ?? _haptics;
            var l  = lighting     ?? _lighting;

            if (!shutdownReset)
                foreach (var entry in _modOverrides.Values.OrderBy(x => x.Order))
                {
                    if (Environment.TickCount64 >= entry.ExpiresAt) continue;
                    lt = entry.Event.LeftTrigger ?? lt;
                    rt = entry.Event.RightTrigger ?? rt;
                    h = entry.Event.Haptics ?? h;
                    l = entry.Event.Lighting ?? l;
                }

            byte[] report = _connType == "Bluetooth"
                ? DualSenseReportBuilder.BuildBluetooth(lt, rt, h, l)
                : DualSenseReportBuilder.Build(lt, rt, h, l);

            await _device.WriteReportAsync(report).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write HID report");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ResetForShutdownAsync()
    {
        _shuttingDown = true;
        _effectsCts.Cancel();
        await _effectsTask.ConfigureAwait(false);
        _leftTrigger = new();
        _rightTrigger = new();
        _haptics = new();
        _lighting = new LightingConfig { LightbarColor = new RgbColor(0, 0, 255), PlayerLeds = 0x04, MicLedEnabled = false, DynamicEffect = "none" };
        await FlushAsync(shutdownReset: true).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _effectsCts.Cancel();
        try { _effectsTask.GetAwaiter().GetResult(); } catch { }
        _effectsCts.Dispose();
        _writeLock.Dispose();
        _device.Dispose();
    }
}
