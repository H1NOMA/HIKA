using System.Runtime.InteropServices;

namespace Hika.Interop;

/// <summary>
/// Обращения к Windows API. Всё здесь — документированные функции user32/kernel32,
/// никаких перехватов, внедрения в чужие процессы и недокументированных приёмов:
/// ассистент с постоянно включённым микрофоном и без того выглядит для антивируса
/// подозрительно, добавлять к этому настоящие признаки вредоноса ни к чему.
/// </summary>
internal static class Win32
{
    // ---- Стили окна ----------------------------------------------------

    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_STYLE = -16;

    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_VISIBLE = 0x10000000;

    /// <summary>Окно не перехватывает мышь — клики уходят в то, что под ним.</summary>
    internal const uint WS_EX_TRANSPARENT = 0x00000020;

    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_TOPMOST = 0x00000008;

    /// <summary>Не показывать окно на панели задач и в Alt+Tab.</summary>
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Окно никогда не забирает фокус — иначе свечение крало бы ввод у активной программы.</summary>
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Ключевой стиль для прозрачного окна с композицией: у окна нет
    /// перенаправляющей поверхности, и содержимое с попиксельной прозрачностью
    /// показывает диспетчер рабочего стола напрямую.
    /// </summary>
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // ---- Показ и порядок окон -----------------------------------------

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_MINIMIZE = 6;

    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_SETTINGCHANGE = 0x001A;

    /// <summary>Исключить окно из захвата экрана — свечение не попадёт в записи и скриншоты.</summary>
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    internal const uint WDA_NONE = 0x00000000;

    // ---- Слоистые окна (запасной способ отрисовки) ---------------------

    internal const int ULW_ALPHA = 0x00000002;
    internal const byte AC_SRC_OVER = 0x00;
    internal const byte AC_SRC_ALPHA = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int Cx; public int Cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    // ---- Ввод -----------------------------------------------------------

    internal const int INPUT_KEYBOARD = 1;
    internal const int INPUT_MOUSE = 0;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Отправить символ, а не клавишу. Нужно для набора текста голосом.</summary>
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;

    internal const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    internal const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    internal const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    internal const ushort VK_MEDIA_STOP = 0xB2;
    internal const ushort VK_LWIN = 0x5B;
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_MENU = 0x12;   // Alt
    internal const ushort VK_LEFT = 0x25;
    internal const ushort VK_RIGHT = 0x27;
    internal const ushort VK_F5 = 0x74;
    internal const ushort VK_S = 0x53;
    internal const ushort VK_T = 0x54;
    internal const ushort VK_W = 0x57;
    internal const ushort VK_C = 0x43;
    internal const ushort VK_V = 0x56;
    internal const ushort VK_X = 0x58;
    internal const ushort VK_Z = 0x5A;
    internal const ushort VK_Y = 0x59;
    internal const ushort VK_A = 0x41;
    internal const ushort VK_D = 0x44;
    internal const ushort VK_E = 0x45;
    internal const ushort VK_F = 0x46;
    internal const ushort VK_I = 0x49;
    internal const ushort VK_N = 0x4E;
    internal const ushort VK_P = 0x50;
    internal const ushort VK_R = 0x52;
    internal const ushort VK_0 = 0x30;

    internal const ushort VK_TAB = 0x09;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_BACK = 0x08;
    internal const ushort VK_DELETE = 0x2E;
    internal const ushort VK_SPACE = 0x20;
    internal const ushort VK_UP = 0x26;
    internal const ushort VK_DOWN = 0x28;
    internal const ushort VK_HOME = 0x24;
    internal const ushort VK_END = 0x23;
    internal const ushort VK_PRIOR = 0x21;   // Page Up
    internal const ushort VK_NEXT = 0x22;    // Page Down
    internal const ushort VK_F4 = 0x73;
    internal const ushort VK_F11 = 0x7A;
    internal const ushort VK_OEM_PERIOD = 0xBE;
    internal const ushort VK_OEM_PLUS = 0xBB;
    internal const ushort VK_OEM_MINUS = 0xBD;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        // Место под самый крупный вариант объединения (мышь), иначе размер структуры
        // не совпадёт с тем, чего ждёт SendInput.
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public int Type;
        public INPUTUNION Union;
    }

    // ---- Мониторы -------------------------------------------------------

    internal const int MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    internal enum MonitorDpiType { EffectiveDpi = 0, AngularDpi = 1, RawDpi = 2 }

    // ---- Импорты --------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtr")]
    internal static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
    internal static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);

    internal static IntPtr GetWindowLongAuto(IntPtr hWnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong(hWnd, index));

    internal static void SetWindowLongAuto(IntPtr hWnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, index, value);
        else SetWindowLong(hWnd, index, value.ToInt32());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    // ---- Поиск и подъём окон ---------------------------------------------

    internal const int SW_RESTORE = 9;

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool force,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    /// <summary>Заголовок окна или пустая строка.</summary>
    internal static string WindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        if (length <= 0) return "";

        var buffer = new System.Text.StringBuilder(length + 1);
        var written = GetWindowTextW(hWnd, buffer, buffer.Capacity);

        return written > 0 ? buffer.ToString() : "";
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr monitor, MonitorDpiType type, out uint dpiX, out uint dpiY);

    // ---- Слоистые окна ---------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst, ref POINT dst, ref SIZE size,
        IntPtr hdcSrc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr obj);

    // ---- Вспомогательное -------------------------------------------------

    /// <summary>Нажать и отпустить клавишу.</summary>
    internal static void TapKey(ushort vk)
    {
        var inputs = new[]
        {
            new INPUT { Type = INPUT_KEYBOARD, Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Vk = vk } } },
            new INPUT { Type = INPUT_KEYBOARD, Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Vk = vk, Flags = KEYEVENTF_KEYUP } } },
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Кнопка мыши: нажатие и отпускание там, где курсор сейчас.</summary>
    internal static void TapMouse(uint down, uint up)
    {
        var inputs = new[]
        {
            new INPUT { Type = INPUT_MOUSE, Union = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = down } } },
            new INPUT { Type = INPUT_MOUSE, Union = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = up } } },
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Набирает текст посимвольно.
    ///
    /// Именно символами, а не клавишами: раскладка клавиатуры при этом
    /// не имеет значения, и русский текст наберётся даже при английской.
    /// </summary>
    internal static void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);

        foreach (var ch in text)
        {
            inputs.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Scan = ch, Flags = KEYEVENTF_UNICODE } },
            });
            inputs.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT { Scan = ch, Flags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP },
                },
            });
        }

        if (inputs.Count == 0) return;

        var array = inputs.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Нажать сочетание: модификаторы удерживаются, пока нажимается основная клавиша.</summary>
    internal static void TapCombo(params ushort[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];

        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Vk = keys[i] } },
            };
        }

        for (int i = 0; i < keys.Length; i++)
        {
            inputs[keys.Length + i] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT { Vk = keys[keys.Length - 1 - i], Flags = KEYEVENTF_KEYUP },
                },
            };
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
