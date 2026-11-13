namespace QuadroSense.Api.Models;

/// <summary>
/// Virtual controller emulation mode.
/// </summary>
public enum EmulationMode
{
    /// <summary>Emulate DS4 (DualShock 4) over HID — broad legacy compatibility.</summary>
    DualShock4,

    /// <summary>
    /// Emulate DS5 (DualSense) natively over HID with full adaptive-trigger,
    /// haptic, and lighting output reports forwarded to the real hardware.
    /// </summary>
    DualSenseNative,

    /// <summary>XInput / Xbox 360 — maximum game compatibility.</summary>
    XInput,
}

public sealed record VirtualControllerConfig
{
    public EmulationMode Mode { get; init; } = EmulationMode.DualSenseNative;

    /// <summary>
    /// Pass-through: forward host output reports (adaptive triggers, haptics,
    /// LEDs) written by the game back to the physical controller.
    /// </summary>
    public bool NativePassthrough { get; init; } = true;

    /// <summary>
    /// Source physical controller device path (empty = auto-detect first DS5).
    /// </summary>
    public string? PhysicalDevicePath { get; init; }
}

public sealed record VirtualControllerStatus
{
    public bool    Active              { get; init; }
    public string? VirtualDevicePath  { get; init; }
    public string? PhysicalDevicePath { get; init; }
    public EmulationMode Mode         { get; init; }
    public string  ConnectionType     { get; init; } = "USB"; // USB | Bluetooth
}
