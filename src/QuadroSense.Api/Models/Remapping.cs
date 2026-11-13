namespace QuadroSense.Api.Models;

/// <summary>A single button binding entry.</summary>
public sealed record ButtonBinding
{
    public string PhysicalButton { get; init; } = string.Empty; // e.g. "Cross", "L2", "TouchPadClick"
    public string MappedTo       { get; init; } = string.Empty; // virtual button name or macro id
    public bool   TurboEnabled   { get; init; }
    public int    TurboRateHz    { get; init; } = 10;
    public bool   HoldToggle     { get; init; }
}

/// <summary>A named macro: sequence of button/axis events.</summary>
public sealed record MacroStep
{
    public string Button    { get; init; } = string.Empty;
    public bool   Pressed   { get; init; }
    /// <summary>Delay after this step in milliseconds.</summary>
    public int    DelayMs   { get; init; }
}

public sealed record Macro
{
    public string        Id    { get; init; } = Guid.NewGuid().ToString();
    public string        Name  { get; init; } = string.Empty;
    public List<MacroStep> Steps { get; init; } = [];
}
