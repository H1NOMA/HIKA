using System.Runtime.InteropServices;
using Hika.Diagnostics;
using Hika.Interop;

namespace Hika.Skills;

/// <summary>
/// Яркость экрана.
///
/// Единственная команда во всём наборе, у которой нет одного правильного
/// способа исполнения, и об этом стоит сказать прямо.
///
/// Встроенный экран ноутбука управляется через WMI: драйвер сам сообщает
/// системе, какие уровни яркости поддерживает. Внешний монитор через WMI
/// не виден вовсе — с ним разговаривают по DDC/CI, то есть по тому же
/// кабелю, что и картинку, и отвечает он далеко не всегда: у части мониторов
/// это не реализовано, у части выключено в меню.
///
/// Поэтому здесь два пути подряд и честный отказ в конце. Сказать «яркость
/// недоступна» лучше, чем сделать вид, что команда сработала, и оставить
/// человека гадать, почему ничего не изменилось.
/// </summary>
public static class Brightness
{
    public static SkillResult Step(int delta)
    {
        var current = Read();
        if (current is null) return Unavailable();

        return Set(Math.Clamp(current.Value + delta, 0, 100));
    }

    public static SkillResult Set(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        if (SetViaWmi(percent) || SetViaMonitor(percent))
        {
            Log.Info($"яркость {percent}%", "system");
            return SkillResult.Ok($"яркость {percent} процентов");
        }

        return Unavailable();
    }

    private static SkillResult Unavailable()
    {
        Log.Info("яркостью этого экрана управлять нельзя", "system");
        return SkillResult.Fail("яркость этого монитора мне не подчиняется");
    }

    // ---- Встроенный экран: WMI ---------------------------------------------

    private static int? Read()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");

            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    return Convert.ToInt32(item["CurrentBrightness"]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"яркость через WMI не читается: {ex.Message}", "system");
        }

        return ReadViaMonitor();
    }

    private static bool SetViaWmi(int percent)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");

            var applied = false;

            foreach (var item in searcher.Get())
            {
                using var method = (System.Management.ManagementObject)item;

                // Первый параметр — сколько секунд длится переход.
                method.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                applied = true;
            }

            return applied;
        }
        catch (Exception ex)
        {
            Log.Debug($"яркость через WMI не ставится: {ex.Message}", "system");
            return false;
        }
    }

    // ---- Внешний монитор: DDC/CI --------------------------------------------

    private static bool SetViaMonitor(int percent)
    {
        return WithPhysicalMonitors(handle =>
        {
            if (!GetMonitorBrightness(handle, out _, out _, out var max)) return false;

            var value = (uint)Math.Round(max * percent / 100.0);
            return SetMonitorBrightness(handle, value);
        });
    }

    private static int? ReadViaMonitor()
    {
        int? result = null;

        WithPhysicalMonitors(handle =>
        {
            if (!GetMonitorBrightness(handle, out _, out var current, out var max)) return false;
            if (max == 0) return false;

            result = (int)Math.Round(current * 100.0 / max);
            return true;
        });

        return result;
    }

    /// <summary>Обходит физические мониторы главного экрана, пока действие не удастся.</summary>
    private static bool WithPhysicalMonitors(Func<IntPtr, bool> action)
    {
        var monitor = MonitorFromWindow(Win32.GetForegroundWindow(), Win32.MONITOR_DEFAULTTOPRIMARY);
        if (monitor == IntPtr.Zero) return false;

        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return false;

        var monitors = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return false;

        try
        {
            var done = false;
            foreach (var physical in monitors)
            {
                try { if (action(physical.Handle)) done = true; }
                catch (Exception ex) { Log.Debug($"монитор не ответил: {ex.Message}", "system"); }
            }
            return done;
        }
        finally
        {
            try { DestroyPhysicalMonitors(count, monitors); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr handle, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr handle, uint brightness);
}
