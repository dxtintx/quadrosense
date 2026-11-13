using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QuadroSense.ProjectWingman;

// Diagnostic only. Discovers candidates; does not automatically turn them into trusted offsets.
internal static class MemoryProbe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(SafeProcessHandle handle, nint address, byte[] buffer, nuint size, out nuint read);

    public static void Run()
    {
        using var process = Process.GetProcessesByName("ProjectWingman-Win64-Shipping").FirstOrDefault()
            ?? throw new InvalidOperationException("Game is not running");
        using var handle = OpenProcess(0x410, false, process.Id);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var module = process.MainModule!;
        var baseAddress = module.BaseAddress.ToInt64();
        byte[] Read(long address, int length)
        {
            if (address < 0x10000 || address > 0x7FFFFFFEFFFF) return [];
            var bytes = new byte[length];
            return ReadProcessMemory(handle, (nint)address, bytes, (nuint)length, out var count) && count == (nuint)length ? bytes : [];
        }
        long Ptr(long address) { var bytes = Read(address, 8); return bytes.Length == 8 ? BitConverter.ToInt64(bytes) : 0; }
        int Int(long address) { var bytes = Read(address, 4); return bytes.Length == 4 ? BitConverter.ToInt32(bytes) : -1; }
        var header = Read(baseAddress, 4096);
        var pe = BitConverter.ToInt32(header, 0x3c);
        var count = BitConverter.ToUInt16(header, pe + 6);
        var sectionStart = pe + 24 + BitConverter.ToUInt16(header, pe + 20);
        long pool = 0;
        var data = new List<(long Address, byte[] Bytes)>();
        for (var section = 0; section < count; section++)
        {
            var at = sectionStart + section * 40;
            var name = Encoding.ASCII.GetString(header, at, 8).TrimEnd('\0');
            if (name != ".data") continue;
            var size = BitConverter.ToInt32(header, at + 8);
            var rva = BitConverter.ToInt32(header, at + 12);
            Console.WriteLine($"Scanning {name}: module+0x{rva:X}, {size} bytes");
            for (var offset = 0; offset < size; offset += 65536)
            {
                var bytes = Read(baseAddress + rva + offset, Math.Min(65536, size - offset));
                if (bytes.Length > 0) data.Add((baseAddress + rva + offset, bytes));
            }
        }
        var visited = new HashSet<long>();
        foreach (var chunk in data)
        {
            for (var i = 0; i + 8 <= chunk.Bytes.Length; i += 8)
            {
                var ptr = BitConverter.ToInt64(chunk.Bytes, i);
                if (ptr < 0x10000 || ptr > 0x7FFFFFFEFFFF || !visited.Add(ptr)) continue;
                var bytes = Read(ptr, 32);
                if (bytes.Length < 32 || Encoding.ASCII.GetString(bytes, 2, 4) != "None") continue;
                if (BitConverter.ToUInt16(bytes, 0) >> 6 != 4) continue;
                pool = chunk.Address + i - 16;
                Console.WriteLine($"FNamePool candidate: module+0x{pool - baseAddress:X}, first block 0x{ptr:X}");
                break;
            }
            if (pool != 0) break;
        }
        if (pool == 0) { Console.WriteLine("No FNamePool candidate found. No offsets configured."); return; }
        string Name(int id)
        {
            if (id < 0 || id >> 16 > 8191) return "";
            var block = Ptr(pool + 16 + (id >> 16) * 8);
            var entry = block + (id & 65535) * 2;
            var bytes = Read(entry, 2);
            if (bytes.Length != 2) return "";
            var flags = BitConverter.ToUInt16(bytes);
            var length = flags >> 6;
            if (length is 0 or > 512) return "";
            var text = Read(entry + 2, length * ((flags & 1) == 1 ? 2 : 1));
            return (flags & 1) == 1 ? Encoding.Unicode.GetString(text) : Encoding.ASCII.GetString(text);
        }
        string ObjectName(long obj) => Name(Int(obj + 24));
        void Dump(long obj, string label)
        {
            Console.WriteLine($"{label}: 0x{obj:X} {ObjectName(obj)}, class {ObjectName(Ptr(obj + 16))}");
            var seen = new HashSet<long>();
            for (var cls = Ptr(obj + 16); cls != 0 && seen.Add(cls) && seen.Count < 16; cls = Ptr(cls + 0x40))
            {
                Console.WriteLine($"  Class {ObjectName(cls)}");
                var fields = new HashSet<long>();
                for (var field = Ptr(cls + 0x50); field != 0 && fields.Add(field) && fields.Count < 512; field = Ptr(field + 0x20))
                {
                    var fieldName = Name(Int(field + 0x28));
                    if (!System.Text.RegularExpressions.Regex.IsMatch(fieldName, "health|hp|thrust|throttle|brake|lock|missile|warn|gforce|g_force|accel|speed|pause|target|flight|combat|player|control|damage", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    if (fieldName.StartsWith("CallFunc") || fieldName.StartsWith("K2Node")) continue;
                    var offset = Int(field + 0x4c);
                    var raw = offset >= 0 && offset < 65536 ? Read(obj + offset, 8) : [];
                    Console.WriteLine($"    +0x{offset:X}: {fieldName} [{Convert.ToHexString(raw)}] float={(raw.Length >= 4 ? BitConverter.ToSingle(raw) : 0)}");
                }
            }
        }
        visited.Clear();
        foreach (var chunk in data)
            for (var i = 0; i + 8 <= chunk.Bytes.Length; i += 8)
            {
                var obj = BitConverter.ToInt64(chunk.Bytes, i);
                if (obj < 0x10000 || obj > 0x7FFFFFFEFFFF || !visited.Add(obj)) continue;
                if (ObjectName(Ptr(obj + 16)) != "World") continue;
                Console.WriteLine($"World candidate: module+0x{chunk.Address + i - baseAddress:X}");
                Dump(obj, "World");
                var instance = Ptr(obj + 0x180);
                Dump(instance, "GameInstance candidate");
                var localPlayer = Ptr(Ptr(instance + 0x38));
                Dump(localPlayer, "LocalPlayer candidate");
                var controller = Ptr(localPlayer + 0x30);
                Dump(controller, "Controller candidate");
                Dump(Ptr(controller + 0x2a0), "Pawn candidate");
            }
    }
}
