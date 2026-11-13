namespace QuadroSense.Api.Models;

public sealed record RgbColor(byte R, byte G, byte B);

public sealed record LightingConfig
{
    public RgbColor LightbarColor  { get; init; } = new(0, 0, 255);
    /// <summary>Player indicator LEDs bitmask (bits 0–3 = P1–P4).</summary>
    public byte     PlayerLeds     { get; init; } = 0b0001;
    public bool     MicLedEnabled  { get; init; }

    /// <summary>
    /// Dynamic effect: "none" | "battery" | "audio_reactive" | "breathing" | "rainbow".
    /// "battery" and "audio_reactive" override LightbarColor at runtime.
    /// </summary>
    public string DynamicEffect    { get; init; } = "none";
    /// <summary>Effect speed 0–255.</summary>
    public byte   EffectSpeed      { get; init; } = 128;
}
