namespace QuadroSense.ProjectWingman;

public sealed record WingmanState(bool InMission = false, float Health = 1, float Throttle = 0,
    float Brake = 0, bool TargetLocked = false, bool MissileWarning = false, float GForce = 1,
    float SpeedKmh = 0);
public sealed record Trigger(string Mode = "Off", byte StartPosition = 0, byte Strength = 0);
public sealed record Color(byte R, byte G, byte B);
public sealed record Lights(Color LightbarColor, byte PlayerLeds = 0, bool MicLedEnabled = false, string DynamicEffect = "none");
public sealed record Rumble(byte RumbleLow = 0, byte RumbleHigh = 0, bool AudioToHapticEnabled = false);
public sealed record Frame(Trigger LeftTrigger, Trigger RightTrigger, Rumble Haptics, Lights Lighting, int DurationMs = 250);

// A complete frame prevents a lighting update from losing a concurrent trigger effect.
public sealed class EffectEngine
{
    private WingmanState _previous = new();
    private long _hitUntil;
    private long _lastTime = -1;
    private float _gBrightness = 1;
    private float _brakingEnvelope;

    public Frame Compose(WingmanState state, long now)
    {
        if (state.InMission && _previous.InMission && state.Health < _previous.Health - .001f)
            _hitUntil = now + 600;
        var sampleDt = _lastTime < 0 ? 0 : (now - _lastTime) / 1000f;
        var dt = Math.Clamp(sampleDt, 0, .1f);
        // Derive deceleration only from consecutive flight samples. Loading, pauses,
        // and long gaps must not look like a sudden stop.
        var deceleration = state.InMission && _previous.InMission && sampleDt is > 0 and <= .25f
            ? Math.Max(0, (_previous.SpeedKmh - state.SpeedKmh) / sampleDt) : 0;
        var braking = state.Brake > .25f ? Math.Clamp((deceleration - 15) / 105, 0, 1) * state.Brake : 0;
        var brakeTime = braking > _brakingEnvelope ? .06f : .25f;
        _brakingEnvelope += (braking - _brakingEnvelope) * (1 - MathF.Exp(-dt / brakeTime));
        _lastTime = now;
        var target = 1 - Math.Clamp((Math.Abs(state.GForce) - 2) / 7, 0, .8f);
        _gBrightness += (target - _gBrightness) * (1 - MathF.Exp(-dt / .8f));
        _previous = state;
        if (!state.InMission)
        {
            _hitUntil = 0;
            _gBrightness = 1;
            _brakingEnvelope = 0;
            return new(new(), new(), new(), new(new(0, 0, 0)));
        }
        var hit = now < _hitUntil;
        var color = hit ? new Color(255, 0, 0) : state.MissileWarning ? new Color(255, 0, 0)
            : state.TargetLocked ? new Color(255, 128, 0) : new Color(0, 255, 0);
        var brightness = Math.Clamp(state.Health, 0, 1) * _gBrightness;
        if (hit && ((_hitUntil - now) / 100) % 2 == 0) brightness = 0;
        byte Scale(byte value) => (byte)Math.Clamp((int)MathF.Round(value * brightness), 0, 255);
        // Gas rumble is active only outside the cruise band: at low speed it
        // is a light takeoff texture, while above 700 km/h it grows gradually.
        // The throttle multiplier is intentional: releasing the gas must stop
        // this rumble, and 350..700 km/h must stay quiet regardless of throttle.
        // Live calibration: HUD SPD 500 equals about 926 km/h (500 knots).
        // Keep physical telemetry in km/h, but apply engine thresholds in HUD units.
        var hudSpeed = state.SpeedKmh / 1.852f;
        var lowSpeed = Math.Clamp((350 - hudSpeed) / 100, 0, 1);
        var highSpeed = Math.Clamp((hudSpeed - 700) / 300, 0, 1);
        var throttle = Math.Clamp(state.Throttle, 0, 1);
        var lowEngine = throttle * lowSpeed;
        var highEngine = throttle * highSpeed;
        var low = Math.Max(lowEngine * 18, highEngine * 30);
        var high = Math.Max(Math.Max(lowEngine * 5, highEngine * 8), Math.Clamp((Math.Abs(state.GForce) - 2) / 7, 0, 1) * 50);
        low = Math.Max(low, _brakingEnvelope * 180);
        high = Math.Max(high, _brakingEnvelope * 75);
        if (state.MissileWarning)
        {
            low = Math.Max(low, 30);
            high = Math.Max(high, now / 160 % 2 == 0 ? 65 : 0);
        }
        var rumble = hit ? new Rumble(150, 190) : new Rumble((byte)low, (byte)high);
        return new(SpeedResistance(state.SpeedKmh, brake: true), SpeedResistance(state.SpeedKmh, brake: false), rumble,
            new(new(Scale(color.R), Scale(color.G), Scale(color.B))));
    }

    private static Trigger SpeedResistance(float speedKmh, bool brake)
    {
        // HUD SPD is in knots. Map 0..1000 SPD to resistance levels 1..8.
        var speed = Math.Clamp(speedKmh / 1.852f / 1000, 0, 1);
        var force = (byte)Math.Clamp((int)MathF.Round(1 + (brake ? speed : 1 - speed) * 7), 1, 8);
        return new("Rigid", 45, force);
    }
}
