using System.Runtime.InteropServices;

namespace AutoHDRManager.Windows;

// Layout verified against Microsoft's win32metadata wingdi.h; see docs/HDR-COMPATIBILITY.md.
internal static class DisplayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id, ModeIndex, Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Target
    {
        public Luid Adapter; public uint Id, ModeIndex; public int Technology; public uint Rotation, Scaling;
        public Rational RefreshRate; public uint ScanLine; public int Available; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        // Union payload starts at 16 and is 48 bytes (VIDEO_SIGNAL_INFO). Only used as an output buffer.
        [FieldOffset(16)] public ulong PixelRate;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo { public Header Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo2 { public Header Header; public uint Flags, Encoding, Bits, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct SetColor { public Header Header; public uint Enabled; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetName
    {
        public Header Header; public uint Flags; public int Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] Mode[] modeArray, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetInfo(ref ColorInfo info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetInfo(ref ColorInfo2 info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetInfo(ref TargetName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] internal static extern int SetInfo(ref SetColor info);

    internal static Header MakeHeader<T>(uint type, Target target) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = target.Adapter, Id = target.Id };
    public static IReadOnlyDictionary<string, int> Layouts() => new Dictionary<string, int>
    {
        [nameof(Luid)] = Marshal.SizeOf<Luid>(), [nameof(Header)] = Marshal.SizeOf<Header>(), [nameof(Source)] = Marshal.SizeOf<Source>(),
        [nameof(Target)] = Marshal.SizeOf<Target>(), [nameof(DisplayPath)] = Marshal.SizeOf<DisplayPath>(), [nameof(Mode)] = Marshal.SizeOf<Mode>(),
        [nameof(ColorInfo)] = Marshal.SizeOf<ColorInfo>(), [nameof(ColorInfo2)] = Marshal.SizeOf<ColorInfo2>(), [nameof(SetColor)] = Marshal.SizeOf<SetColor>(), [nameof(TargetName)] = Marshal.SizeOf<TargetName>()
    };
    internal static void ValidateLayouts()
    {
        int[] expected = [8, 20, 20, 48, 72, 64, 32, 36, 24, 420];
        if (!Layouts().Values.SequenceEqual(expected)) throw new PlatformNotSupportedException("Несовместимая разметка структур DisplayConfig.");
        if (Marshal.OffsetOf<DisplayPath>(nameof(DisplayPath.Target)).ToInt32() != 20 || Marshal.OffsetOf<ColorInfo2>(nameof(ColorInfo2.ActiveMode)).ToInt32() != 32)
            throw new PlatformNotSupportedException("Неверное выравнивание DisplayConfig.");
    }
}
