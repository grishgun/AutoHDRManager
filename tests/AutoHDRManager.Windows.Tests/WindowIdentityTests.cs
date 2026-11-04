using System.Runtime.InteropServices;
using AutoHDRManager.Windows;
using Xunit;

namespace AutoHDRManager.Windows.Tests;

public sealed class WindowIdentityTests
{
    [Fact] public void InvalidWindowIsUnavailableRatherThanAbsent()
    {
        Assert.Equal("Unavailable", WindowIdentityDiagnostics.ReadWindowProperty(0).Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowIdentityDiagnostics.Capture(0));
    }
    [Fact] public void OwnWindowDistinguishesEmptyFromExplicitIdWithoutAlteringIt() => OnStaThread(() =>
    {
        nint hwnd = CreateWindowEx(0, "STATIC", "Auto HDR identity test", 0x80000000, 0, 0, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            var empty = WindowIdentityDiagnostics.ReadWindowProperty(hwnd);
            Assert.Equal("Absent", empty.Status); Assert.Null(empty.Value); Assert.Equal((ushort)0, empty.VariantType);
            // Only this test-owned window is written. Production probe exposes no SetValue COM slot.
            Guid iid = typeof(IPropertyStore).GUID; Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
            var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni("AutoHDRManager.Tests.ExplicitWindow") };
            try
            {
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
                var first = WindowIdentityDiagnostics.ReadWindowProperty(hwnd);
                var second = WindowIdentityDiagnostics.ReadWindowProperty(hwnd);
                Assert.Equal("Present", first.Status); Assert.Equal("AutoHDRManager.Tests.ExplicitWindow", first.Value); Assert.Equal(first, second);
                var captured = Assert.Single(WindowIdentityDiagnostics.Capture(Environment.ProcessId), w => w.Hwnd == "0x" + ((nuint)hwnd).ToString("X"));
                Assert.Equal(Environment.ProcessId, captured.Pid); Assert.Equal(Environment.ProcessPath, captured.ExecutablePath);
                Assert.Equal(first, captured.WindowAppUserModelId); Assert.NotNull(captured.ProcessCreationTime); Assert.False(captured.Visible);
            }
            finally
            {
                PropVariantClear(ref value);
                var clear = new PropVariant(); store.SetValue(ref key, ref clear); Marshal.FinalReleaseComObject(store);
            }
        }
        finally { DestroyWindow(hwnd); }
        Assert.Equal("Unavailable", WindowIdentityDiagnostics.ReadWindowProperty(hwnd).Status);
    });
    private static void OnStaThread(Action test)
    {
        Exception? error = null; var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Identity test timed out");
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value); [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    }
}
