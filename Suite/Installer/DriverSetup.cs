using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Harmonia.Suite.Installer;

internal static class DriverSetup {
    private static readonly Guid media = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    private const string HardwareId = @"ROOT\HarmoniaVirtualAudio";
    public static bool Install(string inf) {
        AssertTestBench();
        var guid = media; IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try {
            var devices = Find(set);
            if (devices.Count > 1) throw new IOException("Multiple Harmonia devnodes; resolve them before installation.");
            if (devices.Count == 0) {
                var data = Info();
                Check(SetupDiCreateDeviceInfo(set, "HarmoniaVirtualAudio", ref guid, "Harmonia Virtual Audio", IntPtr.Zero, 1, ref data));
                byte[] id = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
                Check(SetupDiSetDeviceRegistryProperty(set, ref data, 1, id, id.Length));
                Check(SetupDiCallClassInstaller(0x19, set, ref data));
            }
            Check(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, HardwareId, Path.GetFullPath(inf), 1, out bool reboot));
            return reboot;
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static void Remove() {
        AssertTestBench();
        var guid = media; IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try { var list = Find(set); if (list.Count > 1) throw new IOException("Ambiguous Harmonia devices."); foreach (var entry in list) { var data = entry; Check(SetupDiCallClassInstaller(5, set, ref data)); } }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    private static List<DeviceInfo> Find(IntPtr set) {
        var result = new List<DeviceInfo>();
        for (uint i = 0; ; i++) {
            var data = Info();
            if (!SetupDiEnumDeviceInfo(set, i, ref data)) { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(); break; }
            var bytes = new byte[8192];
            if (SetupDiGetDeviceRegistryProperty(set, ref data, 1, out _, bytes, bytes.Length, out _) &&
                Encoding.Unicode.GetString(bytes).Split('\0').Contains(HardwareId, StringComparer.OrdinalIgnoreCase)) result.Add(data);
        }
        return result;
    }
    private static DeviceInfo Info() => new() { Size = Marshal.SizeOf<DeviceInfo>() };
    private static void AssertTestBench() {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters");
        if (!string.Equals(key?.GetValue("VirtualMachineName") as string, "Harmonia Test Bench", StringComparison.Ordinal))
            throw new InvalidOperationException("Kernel deployment is currently approved only in Harmonia Test Bench.");
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid Class; public uint Instance; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid guid, string description, IntPtr parent, uint flags, ref DeviceInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint property, byte[] buffer, int size);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfo data);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint property, out uint type, byte[] buffer, int size, out int needed);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent, string hardware, string inf, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);
}
