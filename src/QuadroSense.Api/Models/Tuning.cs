namespace QuadroSense.Api.Models;

public sealed record GyroConfig
{
    public bool  Enabled       { get; init; }
    /// <summary>Sensitivity multiplier (0.1–5.0).</summary>
    public float Sensitivity   { get; init; } = 1.0f;
    /// <summary>Dead-zone in degrees/sec below which gyro input is ignored.</summary>
    public float DeadzoneDps   { get; init; } = 0.5f;
    /// <summary>Map gyro to: "rightStick" | "mouse" | "none".</summary>
    public string OutputTarget { get; init; } = "rightStick";
}

public sealed record StickConfig
{
    /// <summary>Circular deadzone radius 0.0–1.0.</summary>
    public float Deadzone        { get; init; } = 0.08f;
    /// <summary>Outer edge anti-deadzone 0.0–1.0.</summary>
    public float AntiDeadzone    { get; init; }
    /// <summary>Exponent for the response curve (1.0 = linear).</summary>
    public float CurveExponent   { get; init; } = 1.0f;
}

public sealed record TouchpadConfig
{
    public bool  MouseEmulation  { get; init; }
    /// <summary>Mouse DPI when emulating (100–32000).</summary>
    public int   MouseDpi        { get; init; } = 1600;
    public bool  InvertX         { get; init; }
    public bool  InvertY         { get; init; }
}

public sealed record TuningConfig
{
    public StickConfig   LeftStick  { get; init; } = new();
    public StickConfig   RightStick { get; init; } = new();
    public TouchpadConfig Touchpad  { get; init; } = new();
    public GyroConfig     Gyro      { get; init; } = new();
}
