namespace QuadroSense.Api.Models;

/// <summary>
/// Which trigger side.
/// </summary>
public enum TriggerSide { Left, Right }

/// <summary>
/// DS5 adaptive trigger effect modes, matching USB output report byte values.
/// </summary>
public enum TriggerMode : byte
{
    Off              = 0x00,
    Rigid            = 0x01, // Firm stop at StartPosition
    Pulse            = 0x02, // Vibrate between StartPosition and EndPosition
    Rigid_A          = 0x21, // Rigid at StartPosition in section A
    Rigid_B          = 0x22, // Rigid at StartPosition in section B
    Rigid_AB         = 0x23,
    Pulse_A          = 0x25,
    Pulse_B          = 0x26,
    Pulse_AB         = 0x27,
    Calibration      = 0xFC,
}

public sealed record AdaptiveTriggerConfig
{
    /// <summary>Trigger effect mode.</summary>
    public TriggerMode Mode { get; init; } = TriggerMode.Off;

    /// <summary>Start position 0–255 (actuator unit).</summary>
    public byte StartPosition { get; init; }

    /// <summary>End position 0–255 (actuator unit). Used for ranged modes.</summary>
    public byte EndPosition { get; init; }

    /// <summary>Strength 0–8 (0 = none, 8 = maximum).</summary>
    public byte Strength { get; init; }

    /// <summary>Vibration frequency in Hz for pulse modes (1–255).</summary>
    public byte Frequency { get; init; }

    /// <summary>Snap-back force 0–8.</summary>
    public byte SnapForce { get; init; }
}
