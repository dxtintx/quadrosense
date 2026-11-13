using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace QuadroSense.ProjectWingman;

public sealed record AdapterConfig(bool Enabled, string QuadSenseBaseUrl, int PollIntervalMs, MemoryLayout Memory);
public sealed record Chain(string Root, string[] Offsets, string Type = "float", float Scale = 1);
public sealed record MemoryLayout(string ProcessName, string ExecutableSha256, Dictionary<string, Chain> Fields);

// Only PROCESS_VM_READ + PROCESS_QUERY_INFORMATION; never writes or injects into the game.
public sealed class WingmanMemoryReader(MemoryLayout layout) : IDisposable
{
    private SafeProcessHandle? _handle;
    private Process? _process;
    private long _module;
    private long _nextAttachAt;
    public string Status { get; private set; } = "Waiting for game";

    public WingmanState Sample()
    {
        try
        {
            if (_process is null || _process.HasExited)
            {
                if (Environment.TickCount64 < _nextAttachAt) return new();
                Dispose();
                _nextAttachAt = Environment.TickCount64 + 1000;
                var processes = Process.GetProcessesByName(layout.ProcessName);
                _process = processes.FirstOrDefault();
                foreach (var extra in processes.Skip(1)) extra.Dispose();
                if (_process is null) { Status = "Waiting for game"; return new(); }
                var module = _process.MainModule ?? throw new InvalidDataException("No game module");
                using var file = File.OpenRead(module.FileName);
                var hash = Convert.ToHexString(SHA256.HashData(file));
                if (!hash.Equals(layout.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Executable fingerprint mismatch");
                _module = module.BaseAddress.ToInt64();
                _handle = OpenProcess(0x410, false, _process.Id);
                if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (_handle is null || _handle.IsInvalid) throw new InvalidDataException("No readable process");
            var mission = Value("inMission");
            if (mission is not (0 or 1)) throw new InvalidDataException("Invalid mission flag");
            if (mission == 0) { Status = "Menu"; return new(); }
            var pawn = BitConverter.ToInt64(Read(Address("inMission"), 8));
            if (!IsFlightPawn(pawn)) { Status = "Menu / non-flight pawn"; return new(); }
            var paused = Range("paused", 0, 1);
            if (paused == 1) { Status = "Paused"; return new(); }
            var hp = Range("health", 0, 1);
            var thrust = Range("thrust", -1, 1);
            var throttle = Math.Max(0, thrust);
            var brake = Math.Max(0, -thrust);
            var locked = Range("targetLocked", 0, 1);
            var warning = Range("missileWarning", 0, 1);
            if (locked is not (0 or 1) || warning is not (0 or 1)) throw new InvalidDataException("Invalid boolean telemetry");
            // Estimated centripetal load, not a measured physical G sensor.
            var speed = Range("speedCmPerSecond", 0, 200000);
            var pitchRate = Range("pitchRateDegPerSecond", -1000, 1000);
            var yawRate = Range("yawRateDegPerSecond", -1000, 1000);
            var turnAcceleration = speed * MathF.Sqrt(pitchRate * pitchRate + yawRate * yawRate) * MathF.PI / 180 / 981;
            var g = Math.Clamp(MathF.Sqrt(1 + turnAcceleration * turnAcceleration), 1, 30);
            if (BitConverter.ToInt64(Read(Address("inMission"), 8)) != pawn)
                throw new InvalidDataException("Player pawn changed while sampling");
            Status = "Valid flight telemetry";
            return new(true, hp, throttle, brake, locked == 1, warning == 1, g, speed * .036f);
        }
        catch (Exception e) when (e is Win32Exception or InvalidDataException or InvalidOperationException or IOException or OverflowException or FormatException)
        {
            Status = $"Telemetry unavailable: {e.Message}";
            if (_handle is null || _handle.IsInvalid) Dispose();
            return new();
        }
    }
    private float Range(string name, float min, float max)
    {
        var value = Value(name);
        if (!float.IsFinite(value) || value < min || value > max) throw new InvalidDataException($"Invalid {name}");
        return value;
    }
    private float Value(string name)
    {
        var address = Address(name);
        var chain = layout.Fields[name];
        var raw = chain.Type switch
        {
            "float" => BitConverter.ToSingle(Read(address, 4)),
            "byte" => Read(address, 1)[0],
            "int32" => BitConverter.ToInt32(Read(address, 4)),
            "pointerPresent" => BitConverter.ToInt64(Read(address, 8)) != 0 ? 1 : 0,
            "countPresent" => ReadCount(address),
            _ => throw new InvalidDataException($"Unknown field type: {chain.Type}")
        };
        return raw * chain.Scale;
    }
    private long Address(string name)
    {
        if (!layout.Fields.TryGetValue(name, out var chain) || Hex(chain.Root) == 0)
            throw new InvalidDataException($"Unverified memory chain: {name}");
        var address = checked(_module + Hex(chain.Root));
        foreach (var offset in chain.Offsets) address = checked(BitConverter.ToInt64(Read(address, 8)) + Hex(offset));
        return address;
    }
    private int ReadCount(long address)
    {
        var count = BitConverter.ToInt32(Read(address, 4));
        if (count < 0 || count > 4096) throw new InvalidDataException("Invalid target array count");
        return count > 0 ? 1 : 0;
    }
    private bool IsFlightPawn(long pawn)
    {
        var cls = BitConverter.ToInt64(Read(pawn + 16, 8));
        for (var depth = 0; cls != 0 && depth < 16; depth++)
        {
            var id = BitConverter.ToInt32(Read(cls + 24, 4));
            if (id < 0 || id >> 16 >= 8192) throw new InvalidDataException("Invalid Unreal class name");
            var block = BitConverter.ToInt64(Read(_module + 0x9444800 + 16 + (id >> 16) * 8, 8));
            var entry = block + (id & 65535) * 2;
            var flags = BitConverter.ToUInt16(Read(entry, 2));
            var length = flags >> 6;
            if (length is 0 or > 512) throw new InvalidDataException("Invalid Unreal name length");
            if ((flags & 1) == 0 && System.Text.Encoding.ASCII.GetString(Read(entry + 2, length)) == "FlyingPawn_C") return true;
            cls = BitConverter.ToInt64(Read(cls + 0x40, 8));
        }
        return false;
    }
    private byte[] Read(long address, int length)
    {
        if (address < 0x10000 || address > 0x7FFFFFFEFFFF) throw new InvalidDataException("Invalid pointer");
        var buffer = new byte[length];
        if (!ReadProcessMemory(_handle!, (nint)address, buffer, (nuint)length, out var read) || read != (nuint)length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return buffer;
    }
    private static long Hex(string value) => long.Parse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    public void Dispose() { _handle?.Dispose(); _handle = null; _process?.Dispose(); _process = null; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint length, out nuint read);
}
