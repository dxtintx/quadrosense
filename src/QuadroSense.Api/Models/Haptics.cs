namespace QuadroSense.Api.Models;

public sealed record HapticConfig
{
    /// <summary>Rumble motor intensity 0–255 for left (low frequency) motor.</summary>
    public byte RumbleLow  { get; init; }

    /// <summary>Rumble motor intensity 0–255 for right (high frequency) motor.</summary>
    public byte RumbleHigh { get; init; }

    /// <summary>
    /// When true, the service captures WASAPI loopback audio and
    /// converts it to HD haptic output reports in real time.
    /// </summary>
    public bool AudioToHapticEnabled { get; init; }

    /// <summary>
    /// Band that drives haptic output: "bass" | "mid" | "treble" | "all".
    /// Only relevant when AudioToHapticEnabled = true.
    /// </summary>
    public string AudioBand { get; init; } = "bass";

    /// <summary>Gain multiplier for audio-to-haptic conversion (0.0–4.0).</summary>
    public float HapticGain { get; init; } = 1.0f;
}

public sealed record HapticState
{
    public bool AudioPipelineRunning { get; init; }
    public string? AudioDevice       { get; init; }
    public float   CurrentRms        { get; init; }
    public byte    LastReportLeft    { get; init; }
    public byte    LastReportRight   { get; init; }
}
