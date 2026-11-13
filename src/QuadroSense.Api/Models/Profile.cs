namespace QuadroSense.Api.Models;

/// <summary>A complete controller configuration profile.</summary>
public sealed record Profile
{
    public string         Id          { get; init; } = Guid.NewGuid().ToString();
    public string         Name        { get; init; } = "Default";
    /// <summary>CSS hex color tag for UI grouping (e.g. "#FF5733").</summary>
    public string         ColorTag    { get; init; } = "#0099FF";
    public bool           IsActive    { get; init; }

    public AdaptiveTriggerConfig LeftTrigger  { get; init; } = new();
    public AdaptiveTriggerConfig RightTrigger { get; init; } = new();
    public HapticConfig          Haptics      { get; init; } = new();
    public LightingConfig        Lighting     { get; init; } = new();
    public TuningConfig          Tuning       { get; init; } = new();
    public List<ButtonBinding>   Bindings     { get; init; } = [];

    /// <summary>
    /// Process executable names that trigger auto-switch to this profile.
    /// E.g. ["eldenring.exe", "ds.exe"].
    /// </summary>
    public List<string> AutoSwitchProcesses { get; init; } = [];
    public List<string> ActivationButtons { get; init; } = [];
}

public sealed record ProfileSummary(string Id, string Name, string ColorTag, bool IsActive);
