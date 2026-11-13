using HidSharp;
using QuadroSense.Api.Models;

namespace QuadroSense.Api.HID;

public sealed record ControllerMotion(double PitchDegrees = 0, double RollDegrees = 0, DateTimeOffset? UpdatedAt = null,
    double LeftX = 0, double LeftY = 0, double RightX = 0, double RightY = 0,
    float Qx = 0, float Qy = 0, float Qz = 0, float Qw = 1, string[]? Buttons = null);

/// <summary>
/// Discovers and opens physical DualSense controllers via HidSharp.
/// Prioritizes USB over Bluetooth when both are connected, and recovers gracefully from broken streams.
/// DS5 USB: VID 0x054C PID 0x0CE6
/// DS5 BT:  VID 0x054C PID 0x0CE6
/// </summary>
public sealed class ControllerDeviceManager : IDisposable
{
    private const int SonyVid = 0x054C;
    private const int Ds5Pid  = 0x0CE6;

    // Bluetooth HID interface contains standard BT GUID
    private const string BluetoothGuidSubstring = "{00001124";

    private HidDevice? _device;
    private HidStream? _stream;
    private long       _reportsSent;
    private CancellationTokenSource? _inputCts;
    private Task? _inputTask;
    private volatile ControllerMotion _motion = new();
    private readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();

    public string? DevicePath  => _device?.DevicePath;
    public bool    IsOpen      => _stream is not null;
    public long    ReportsSent => _reportsSent;
    public ControllerMotion Motion => _motion;

    /// <summary>
    /// Returns paths of all connected DS5 controllers, with USB devices ordered before Bluetooth.
    /// </summary>
    public static IReadOnlyList<string> EnumerateDevices()
    {
        return DeviceList.Local
            .GetHidDevices(vendorID: SonyVid, productID: Ds5Pid)
            .OrderBy(d => d.DevicePath.Contains(BluetoothGuidSubstring, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .Select(d => d.DevicePath)
            .ToList();
    }

    /// <summary>
    /// Opens the first available DS5 (prioritizing USB), or the device at <paramref name="devicePath"/>.
    /// </summary>
    public bool TryOpen(string? devicePath = null)
    {
        Close();

        var devices = DeviceList.Local
            .GetHidDevices(vendorID: SonyVid, productID: Ds5Pid)
            .OrderBy(d => d.DevicePath.Contains(BluetoothGuidSubstring, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();

        _device = devicePath is null
            ? devices.FirstOrDefault()
            : devices.FirstOrDefault(d => d.DevicePath == devicePath);

        if (_device is null) return false;

        try
        {
            _stream = _device.Open();
            _stream.ReadTimeout  = Timeout.Infinite;
            _stream.WriteTimeout = 1000;
            _inputCts = new CancellationTokenSource();
            _inputTask = ReadInputAsync(_stream, _device.GetMaxInputReportLength(), _inputCts.Token);
            return true;
        }
        catch
        {
            _stream?.Dispose();
            _stream = null;
            _device = null;
            return false;
        }
    }

    public void Close()
    {
        _inputCts?.Cancel();
        try { _stream?.Dispose(); } catch { }
        _stream = null;
        _device = null;
        _inputCts?.Dispose();
        _inputCts = null;
        _inputTask = null;
        _motion = new();
    }

    private async Task ReadInputAsync(HidStream stream, int reportLength, CancellationToken ct)
    {
        if (reportLength <= 0) return;
        var report = new byte[reportLength];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double previous = 0;
        var orientation = System.Numerics.Quaternion.Identity;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int count = await stream.ReadAsync(report, 0, report.Length, ct).ConfigureAwait(false);
                int offset = report[0] == 0x31 ? 1 : 0;
                if (count < 28 + offset || (report[0] != 0x01 && report[0] != 0x31)) continue;
                double now = clock.Elapsed.TotalSeconds;
                double dt = previous == 0 ? 0 : Math.Clamp(now - previous, 0, 0.05);
                previous = now;
                var velocity = new System.Numerics.Vector3(
                    BitConverter.ToInt16(report, 16 + offset),
                    BitConverter.ToInt16(report, 18 + offset),
                    BitConverter.ToInt16(report, 20 + offset)) * (float)(Math.PI / (180 * 16.4));
                float speed = velocity.Length();
                if (speed > 0.01f && dt > 0)
                    orientation = System.Numerics.Quaternion.Normalize(orientation *
                        System.Numerics.Quaternion.CreateFromAxisAngle(velocity / speed, speed * (float)dt));
                short ax = BitConverter.ToInt16(report, 22 + offset);
                short ay = BitConverter.ToInt16(report, 24 + offset);
                short az = BitConverter.ToInt16(report, 26 + offset);
                double x = ax / 16384.0, y = ay / 16384.0, z = az / 16384.0;
                double roll = Math.Atan2(y, z) * 180 / Math.PI;
                double pitch = Math.Atan2(-x, Math.Sqrt(y * y + z * z)) * 180 / Math.PI;
                static double Stick(byte value) => Math.Clamp((value - 128) / 127.0, -1, 1);
                _motion = new(pitch, roll, DateTimeOffset.UtcNow,
                    Stick(report[1 + offset]), Stick(report[2 + offset]),
                    Stick(report[3 + offset]), Stick(report[4 + offset]),
                    orientation.X, orientation.Y, orientation.Z, orientation.W, DecodeButtons(report, offset));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception) when (ct.IsCancellationRequested) { break; }
            catch { await Task.Delay(10, ct).ConfigureAwait(false); }
        }
    }

    private static string[] DecodeButtons(byte[] report, int offset)
    {
        var buttons = new List<string>();
        int dpad = report[8 + offset] & 15;
        if (dpad is 0 or 1 or 7) buttons.Add("DPadUp");
        if (dpad is 1 or 2 or 3) buttons.Add("DPadRight");
        if (dpad is 3 or 4 or 5) buttons.Add("DPadDown");
        if (dpad is 5 or 6 or 7) buttons.Add("DPadLeft");
        string[] names = ["Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2", "Create", "Options", "L3", "R3", "PS", "TouchPadClick", "Mute"];
        for (int i = 0; i < names.Length; i++)
        {
            int bit = i + 4;
            if ((report[8 + offset + bit / 8] & (1 << (bit % 8))) != 0) buttons.Add(names[i]);
        }
        return buttons.ToArray();
    }

    public async Task WriteReportAsync(byte[] report, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Device not open.");

        int expectedLength = _device?.GetMaxOutputReportLength() ?? report.Length;
        byte[] payload = report;

        if (expectedLength > 0 && report.Length != expectedLength)
        {
            payload = new byte[expectedLength];
            Buffer.BlockCopy(report, 0, payload, 0, Math.Min(report.Length, expectedLength));
        }

        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _reportsSent);
    }

    public ControllerDiagnostics GetDiagnostics()
    {
        return new ControllerDiagnostics
        {
            DevicePath      = DevicePath ?? string.Empty,
            ConnectionType  = DetectConnectionType(),
            BatteryPercent  = -1,
            IsCharging      = false,
            BluetoothRssi   = null,
            FirmwareVersion = string.Empty,
            HardwareVersion = string.Empty,
            UptimeSeconds   = (long)_uptime.Elapsed.TotalSeconds,
            ReportsSent     = _reportsSent,
        };
    }

    private string DetectConnectionType()
    {
        if (_device is null) return "Unknown";
        if (_device.DevicePath.Contains(BluetoothGuidSubstring, StringComparison.OrdinalIgnoreCase))
            return "Bluetooth";

        try
        {
            return _device.GetMaxOutputReportLength() > 64 ? "Bluetooth" : "USB";
        }
        catch { return "USB"; }
    }

    public void Dispose() => Close();
}
