namespace QuadroSense.Api.Models;

public sealed record ControllerConfiguration(AdaptiveTriggerConfig LeftTrigger,
    AdaptiveTriggerConfig RightTrigger, HapticConfig Haptics, LightingConfig Lighting);
