namespace QuadroSense.Api.Models;

/// <summary>
/// A mod plugin registration entry. Mods register once and then push events.
/// </summary>
public sealed record ModRegistration
{
    public string Id          { get; init; } = Guid.NewGuid().ToString();
    public string Name        { get; init; } = string.Empty;
    public string Version     { get; init; } = "1.0.0";
    /// <summary>Semver API contract this mod targets (e.g. "1.0").</summary>
    public string ApiContract { get; init; } = "1.0";
}

/// <summary>
/// A mod event that directly overrides controller output for one frame.
/// All fields are optional — only specified fields are overridden.
/// </summary>
public sealed record ModEvent
{
    public string ModId { get; init; } = string.Empty;

    public AdaptiveTriggerConfig? LeftTrigger  { get; init; }
    public AdaptiveTriggerConfig? RightTrigger { get; init; }
    public HapticConfig?          Haptics      { get; init; }
    public LightingConfig?        Lighting     { get; init; }

    /// <summary>
    /// How long this override persists in milliseconds.
    /// 0 = one-shot (single report), -1 = until next ModEvent from same mod.
    /// </summary>
    public int DurationMs { get; init; }
}
