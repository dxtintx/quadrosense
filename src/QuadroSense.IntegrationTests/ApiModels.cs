namespace QuadroSense.IntegrationTests;

// ── Shared response shapes that mirror the API models ────────────────────────
// (copied by value — no project reference needed)

public record ControllerDiagnostics(
    string DevicePath,
    string ConnectionType,
    int    BatteryPercent,
    bool   IsCharging,
    int?   BluetoothRssi,
    string FirmwareVersion,
    string HardwareVersion,
    long   UptimeSeconds,
    long   ReportsSent);

public record HapticState(
    bool    AudioPipelineRunning,
    string? AudioDevice,
    float   CurrentRms,
    byte    LastReportLeft,
    byte    LastReportRight);

public record ProfileSummary(string Id, string Name, string ColorTag, bool IsActive);

public record ModRegistration(string Id, string Name, string Version, string ApiContract);
