using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AutoHDRManager.Windows;

// Read-only corroboration of the monitor interface supplied by DisplayConfig. Never
// use the friendly name, adapter target number or a model-only EDID as a serial ID.
public static class MonitorIdentity
{
    public static (string? Fingerprint, string? HardwareId) FromEdid(byte[]? edid)
    {
        if (edid is not { Length: >= 128 } || !edid.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }) ||
            edid.Take(128).Sum(b => (int)b) % 256 != 0) return (null, null);
        string fingerprint = Convert.ToHexString(SHA256.HashData(edid.AsSpan(0, 128)));
        string? serial = null;
        for (int offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0 || edid[offset + 3] != 255 || edid[offset + 4] != 0) continue;
            var text = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ').ToUpperInvariant();
            if (text.Length >= 4 && text.All(c => c >= 32 && c <= 126) && text.Distinct().Count() > 1 &&
                text is not ("UNKNOWN" or "DEFAULT" or "SERIAL" or "123456789" or "1234567890")) serial = "TEXT:" + text;
        }
        uint numeric = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(edid.AsSpan(12, 4));
        // 0x01010101 is the placeholder actually reported by MO34WQC2 here.
        if (serial == null && numeric != 0 && numeric != uint.MaxValue && edid.AsSpan(12, 4).ToArray().Distinct().Count() > 1)
            serial = "NUM:" + numeric.ToString("X8");
        string? hardwareId = serial == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexString(edid.AsSpan(8, 4)) + ":" + serial)));
        return (fingerprint, hardwareId);
    }

    internal static (string? Fingerprint, string? HardwareId) Read(string devicePath)
    {
        try
        {
            var parts = devicePath.Split('#');
            if (parts.Length != 4 || !parts[0].Equals(@"\\?\DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                parts.Skip(1).Take(2).Any(p => p.Length == 0 || p.Contains('\\') || p.Contains('/'))) return (null, null);
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + "\\" + parts[2] + @"\Device Parameters", false);
            return FromEdid(key?.GetValue("EDID") as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return (null, null); }
    }
}
