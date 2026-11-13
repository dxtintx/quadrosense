namespace QuadroSense.Api.Models;

public sealed record ControllerDiagnostics
{
    public string DevicePath      { get; init; } = string.Empty;
    public string ConnectionType  { get; init; } = "USB"; // USB | Bluetooth
    /// <summary>Battery level 0–100 (-1 = unknown).</summary>
    public int    BatteryPercent  { get; init; }
    public bool   IsCharging      { get; init; }
    /// <summary>Bluetooth RSSI in dBm (null if USB).</summary>
    public int?   BluetoothRssi   { get; init; }
    public string FirmwareVersion { get; init; } = string.Empty;
    public string HardwareVersion { get; init; } = string.Empty;
    public long   UptimeSeconds   { get; init; }
    /// <summary>Total HID output reports sent in this session.</summary>
    public long   ReportsSent     { get; init; }
}
