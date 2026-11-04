using System.ComponentModel;
using AutoHDRManager.Core;
using static AutoHDRManager.Windows.DisplayNative;

namespace AutoHDRManager.Windows;

public sealed class WindowsHdrController : IHdrController
{
    private readonly IEventLog log;
    public WindowsHdrController(IEventLog log) { this.log = log; ValidateLayouts(); }
    public static IReadOnlyDictionary<string, int> StructureSizes => Layouts();
    public static object ProbePaths()
    {
        var results = new List<object>();
        foreach (uint flags in new uint[] { 2, 0x12, 0x52, 1, 0x11, 0x51 })
        {
            int sizeResult = GetDisplayConfigBufferSizes(flags, out uint paths, out uint modes);
            uint inputPaths = paths, inputModes = modes;
            if (sizeResult != 0 || paths > 1024 || modes > 8192) { results.Add(new { flags, sizeResult, paths, modes }); continue; }
            var pathArray = new DisplayPath[Math.Max(paths, 1)]; var modeArray = new Mode[Math.Max(modes, 1)];
            int queryResult = QueryDisplayConfig(flags, ref paths, pathArray, ref modes, modeArray, 0);
            var targets = new List<object>();
            if (queryResult == 0) foreach (var p in pathArray.Take((int)paths))
            {
                var name = new TargetName { Header = MakeHeader<TargetName>(2, p.Target), Name = "", DevicePath = "" };
                int nameResult = GetInfo(ref name);
                var color = new ColorInfo2 { Header = MakeHeader<ColorInfo2>(15, p.Target) }; int colorResult = GetInfo(ref color);
                targets.Add(new { p.Target.Adapter.Low, p.Target.Adapter.High, p.Target.Id, p.Target.Available, PathFlags = p.Flags, nameResult, name.Name, name.DevicePath, colorResult, color.Flags, color.ActiveMode });
            }
            results.Add(new { flags, sizeResult, inputPaths, inputModes, queryResult, paths, modes, targets });
        }
        return results;
    }
    private sealed record Entry(Display Display, Target Target);
    private List<Entry> Query(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        const uint flags = 2; // QDC_ONLY_ACTIVE_PATHS; no topology writes anywhere in the application.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(flags, out var paths, out var modes), "GetDisplayConfigBufferSizes");
            if (paths > 1024 || modes > 8192) throw new InvalidOperationException("Некорректный размер конфигурации дисплеев.");
            var pathArray = new DisplayPath[Math.Max(paths, 1)]; var modeArray = new Mode[Math.Max(modes, 1)];
            int error = QueryDisplayConfig(flags, ref paths, pathArray, ref modes, modeArray, 0);
            if (error is 122 or 87) { Thread.Sleep(100); continue; } // Topology may be temporarily unavailable during an HDR transition.
            Check(error, "QueryDisplayConfig");
            var result = new List<Entry>();
            foreach (var p in pathArray.Take((int)paths).Where(p => p.Target.Available != 0))
            {
                var target = p.Target;
                var name = new TargetName { Header = MakeHeader<TargetName>(2, target), Name = "", DevicePath = "" };
                Check(GetInfo(ref name), "GetTargetName");
                // Stable device path avoids restoring to a different target after adapter/target ID reuse.
                if (string.IsNullOrWhiteSpace(name.DevicePath)) continue;
                string id = name.DevicePath.ToUpperInvariant();
                if (result.Any(e => e.Display.Id == id)) continue;
                var modern = new ColorInfo2 { Header = MakeHeader<ColorInfo2>(15, target) };
                error = GetInfo(ref modern);
                Display display;
                if (error == 0)
                {
                    display = new(id, name.Name, (modern.Flags & 0x10) != 0 && (modern.Flags & 8) == 0,
                        (modern.Flags & 0x20) != 0, modern.ActiveMode == 2, true, modern.Flags);
                }
                else if (error is 50 or 87 or 120)
                {
                    var legacy = new ColorInfo { Header = MakeHeader<ColorInfo>(9, target) };
                    Check(GetInfo(ref legacy), "GetAdvancedColorInfo");
                    // On newer builds do not guess that legacy Advanced Color means HDR (it may be WCG).
                    bool safeLegacy = Environment.OSVersion.Version.Build < 26100;
                    display = new(id, name.Name, safeLegacy && (legacy.Flags & 1) != 0 && (legacy.Flags & 12) == 0,
                        (legacy.Flags & 2) != 0, (legacy.Flags & 2) != 0, false, legacy.Flags);
                }
                else { Check(error, "GetAdvancedColorInfo2"); throw new InvalidOperationException(); }
                result.Add(new(display, target));
            }
            return result;
        }
        throw new InvalidOperationException("Конфигурация дисплеев меняется. Повторите проверку позже.");
    }
    public Task<IReadOnlyList<Display>> GetDisplaysAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<Display>>(() => Query(token).Select(e => e.Display).ToArray(), token);
    public Task<Display> ReadAsync(string displayId, CancellationToken token = default) => Task.Run(() => Find(displayId, token).Display, token);
    private Entry Find(string id, CancellationToken token) => Query(token).FirstOrDefault(e => e.Display.Id == id) ?? throw new InvalidOperationException("Целевой монитор отключён или изменился.");
    public async Task SetAsync(string displayId, bool enabled, CancellationToken token = default)
    {
        var entry = await Task.Run(() => Find(displayId, token), token);
        if (!entry.Display.Supported) throw new InvalidOperationException("Переключение HDR на этом мониторе недоступно.");
        if (entry.Display.Enabled == enabled) return;
        var request = new SetColor { Header = MakeHeader<SetColor>(entry.Display.ModernApi ? 16u : 10u, entry.Target), Enabled = enabled ? 1u : 0u };
        int error = SetInfo(ref request);
        log.Write("DisplayConfigSetDeviceInfo", new { Type = request.Header.Type, Result = error, Enabled = enabled });
        Check(error, "DisplayConfigSetDeviceInfo");
        // Bounded confirmation window; never issue additional writes while Windows settles.
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(150, token);
            try
            {
                var actual = await ReadAsync(displayId, token);
                if (actual.Enabled == enabled && (!enabled || actual.Active)) return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { log.Write("HdrConfirmationTransient", new { ex.Message }); }
        }
        throw new InvalidOperationException("HDR не подтвердился после ограниченного ожидания стабилизации дисплея.");
    }
    private static void Check(int result, string api) { if (result != 0) throw new Win32Exception(result, $"{api}: {new Win32Exception(result).Message} (код {result})"); }
}
