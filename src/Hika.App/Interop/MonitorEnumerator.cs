using Hika.Diagnostics;

namespace Hika.Interop;

/// <summary>Один физический экран в координатах рабочего стола.</summary>
public sealed record MonitorGeometry(
    string DeviceName,
    int Left, int Top, int Width, int Height,
    bool IsPrimary,
    uint Dpi)
{
    public double Scale => Dpi / 96.0;
    public override string ToString() => $"{DeviceName} {Width}x{Height} @ {Left},{Top} (DPI {Dpi}{(IsPrimary ? ", основной" : "")})";
}

/// <summary>
/// Перечисление мониторов. Свечение рисуется в физических пикселях, поэтому
/// нужны настоящие границы каждого экрана и его масштаб: на связке 4K + 1080p
/// без этого кайма на одном из мониторов уедет.
/// </summary>
public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorGeometry> Enumerate()
    {
        var result = new List<MonitorGeometry>();

        try
        {
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Win32.RECT clip, IntPtr data) =>
            {
                try
                {
                    var info = new Win32.MONITORINFOEX { Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>() };
                    if (!Win32.GetMonitorInfo(monitor, ref info)) return true;

                    uint dpi = 96;
                    try
                    {
                        if (Win32.GetDpiForMonitor(monitor, Win32.MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0)
                            dpi = dpiX;
                    }
                    catch
                    {
                        // shcore.dll есть начиная с Windows 8.1; на всякий случай остаёмся на 96.
                    }

                    result.Add(new MonitorGeometry(
                        DeviceName: info.DeviceName,
                        Left: info.Monitor.Left,
                        Top: info.Monitor.Top,
                        Width: info.Monitor.Width,
                        Height: info.Monitor.Height,
                        IsPrimary: (info.Flags & Win32.MONITORINFOF_PRIMARY) != 0,
                        Dpi: dpi));
                }
                catch (Exception ex)
                {
                    Log.Warn($"монитор не прочитался: {ex.Message}", "display");
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("перечисление мониторов не удалось", ex, "display");
        }

        if (result.Count == 0)
        {
            // Совсем без мониторов остаться нельзя — берём разумные значения по умолчанию.
            Log.Warn("мониторы не найдены, использую 1920x1080", "display");
            result.Add(new MonitorGeometry("\\\\.\\DISPLAY1", 0, 0, 1920, 1080, true, 96));
        }

        return result;
    }

    public static MonitorGeometry Primary()
    {
        var all = Enumerate();
        return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
    }
}
