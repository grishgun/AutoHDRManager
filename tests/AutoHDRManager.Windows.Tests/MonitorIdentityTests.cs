using AutoHDRManager.Windows;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class MonitorIdentityTests
{
    private static byte[] Edid(uint serial = 0x01010101)
    {
        var bytes = new byte[128]; new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }.CopyTo(bytes, 0);
        bytes[8] = 0x1c; bytes[9] = 0x54; bytes[10] = 8; bytes[11] = 0x34;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), serial);
        Checksum(bytes); return bytes;
    }
    private static void Checksum(byte[] bytes) => bytes[127] = (byte)((256 - bytes.Take(127).Sum(b => (int)b) % 256) % 256);
    [Theory] [InlineData(0u)] [InlineData(0x01010101u)] [InlineData(uint.MaxValue)]
    public void PlaceholderSerialIsNeverPhysicalIdentity(uint serial)
    {
        var result = MonitorIdentity.FromEdid(Edid(serial)); Assert.NotNull(result.Fingerprint); Assert.Null(result.HardwareId);
    }
    [Fact] public void ValidNumericSerialProducesStableHardwareKey()
    {
        var a = MonitorIdentity.FromEdid(Edid(123456)); var b = MonitorIdentity.FromEdid(Edid(123457));
        Assert.NotNull(a.HardwareId); Assert.NotEqual(a.HardwareId, b.HardwareId); Assert.Equal(a, MonitorIdentity.FromEdid(Edid(123456)));
    }
    [Fact] public void TextSerialCanIdentifyDisplayWithoutNumericSerial()
    {
        var bytes = Edid(); bytes[57] = 255; System.Text.Encoding.ASCII.GetBytes("SN-987654\n   ").CopyTo(bytes, 59); Checksum(bytes);
        Assert.NotNull(MonitorIdentity.FromEdid(bytes).HardwareId);
    }
    [Fact] public void InvalidOrMissingEdidHasNoIdentity()
    {
        Assert.Equal((null, null), MonitorIdentity.FromEdid(null)); Assert.Equal((null, null), MonitorIdentity.FromEdid(new byte[20]));
        var bytes = Edid(); bytes[30]++; Assert.Equal((null, null), MonitorIdentity.FromEdid(bytes));
        bytes = Edid(); bytes[0] = 1; Checksum(bytes); Assert.Equal((null, null), MonitorIdentity.FromEdid(bytes));
    }
    [Fact] public void SameSerialOnDifferentModelIsDifferentIdentity()
    {
        var bytes = Edid(123456); var first = MonitorIdentity.FromEdid(bytes); bytes[10]++; Checksum(bytes);
        Assert.NotEqual(first.HardwareId, MonitorIdentity.FromEdid(bytes).HardwareId);
    }
}
