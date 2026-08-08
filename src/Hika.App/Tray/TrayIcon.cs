using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Startup;

namespace Hika.Tray;

/// <summary>
/// Значок в трее — единственная видимая часть HIKA.
///
/// Он здесь не для красоты: программа круглые сутки слушает микрофон, и у человека
/// должен быть способ увидеть, что она делает, и выключить её одним движением.
/// Ассистент с постоянно открытым микрофоном и без видимого выключателя — плохая
/// программа независимо от того, насколько хорошо она распознаёт речь.
///
/// Значок рисуется кодом, а не берётся из файла: так он меняет цвет вместе
/// с состоянием и не тянет за собой двоичных ресурсов.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _muteItem;
    private readonly ToolStripMenuItem _autostartItem;

    private readonly Dictionary<HostState, Icon> _icons = new();
    private HostState _state = HostState.Starting;

    public event Action? MuteToggleRequested;
    public event Action? ExitRequested;
    public event Action? DiagnosticsRequested;
    public event Action? LiveListenRequested;

    public TrayIcon()
    {
        _statusItem = new ToolStripMenuItem("Запускается…") { Enabled = false };
        _muteItem = new ToolStripMenuItem("Выключить микрофон", null, (_, _) => MuteToggleRequested?.Invoke());
        _autostartItem = new ToolStripMenuItem("Запускать вместе с Windows", null, OnAutostartClicked)
        {
            CheckOnClick = true,
            Checked = AutostartManager.IsEnabled(),
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_muteItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Настройки (config.json)", null, (_, _) => OpenConfig()));
        _menu.Items.Add(new ToolStripMenuItem("Журнал работы", null, (_, _) => OpenLogs()));
        _menu.Items.Add(new ToolStripMenuItem("Что я слышу (живая проверка)", null, (_, _) => LiveListenRequested?.Invoke()));
        _menu.Items.Add(new ToolStripMenuItem("Проверка системы", null, (_, _) => DiagnosticsRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = IconFor(HostState.Starting),
            Text = "HIKA — запускается",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        // Двойной щелчок — быстрый выключатель микрофона.
        _icon.DoubleClick += (_, _) => MuteToggleRequested?.Invoke();
    }

    public void UpdateState(HostState state, bool muted)
    {
        _state = state;

        var status = muted
            ? "Микрофон выключен"
            : state switch
            {
                HostState.Starting => "Запускается…",
                HostState.Idle => "Слушает",
                HostState.Sensing => "Слышу голос…",
                HostState.Armed => "Жду команду",
                HostState.Working => "Выполняю…",
                HostState.Failed => "Ошибка — смотрите журнал",
                _ => "—",
            };

        try
        {
            _statusItem.Text = status;
            _muteItem.Text = muted ? "Включить микрофон" : "Выключить микрофон";
            _icon.Icon = muted ? IconFor(HostState.Failed) : IconFor(state);

            // Подпись значка в Windows ограничена 63 символами.
            var tooltip = $"HIKA — {status}";
            _icon.Text = tooltip.Length > 60 ? tooltip[..60] : tooltip;
        }
        catch (Exception ex)
        {
            Log.Debug($"значок в трее не обновился: {ex.Message}", "tray");
        }
    }

    public void ShowMessage(string title, string text, ToolTipIcon kind = ToolTipIcon.Info)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = kind;
            _icon.ShowBalloonTip(8000);
        }
        catch (Exception ex)
        {
            Log.Debug($"уведомление не показалось: {ex.Message}", "tray");
        }
    }

    private void OnAutostartClicked(object? sender, EventArgs e)
    {
        var wanted = _autostartItem.Checked;

        if (!AutostartManager.Set(wanted))
        {
            _autostartItem.Checked = !wanted;
            ShowMessage("HIKA", "Не удалось изменить автозапуск. Подробности в журнале.", ToolTipIcon.Warning);
        }
    }

    private static void OpenConfig()
    {
        try
        {
            AppPaths.EnsureCreated();

            // Открываем папку, а не сам файл: у .json может не быть программы по умолчанию,
            // и человек получил бы диалог «чем открыть» вместо настроек.
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("не удалось открыть папку настроек", ex, "tray");
        }
    }

    private static void OpenLogs()
    {
        try
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("не удалось открыть журнал", ex, "tray");
        }
    }

    /// <summary>Рисует значок: кольцо, цвет которого говорит о состоянии.</summary>
    private Icon IconFor(HostState state)
    {
        if (_icons.TryGetValue(state, out var cached)) return cached;

        var (outer, inner) = state switch
        {
            HostState.Idle => (Color.FromArgb(90, 150, 210), Color.FromArgb(160, 205, 245)),
            HostState.Sensing => (Color.FromArgb(70, 190, 205), Color.FromArgb(150, 235, 240)),
            HostState.Armed => (Color.FromArgb(90, 175, 255), Color.FromArgb(190, 225, 255)),
            HostState.Working => (Color.FromArgb(140, 120, 245), Color.FromArgb(200, 190, 255)),
            HostState.Failed => (Color.FromArgb(150, 90, 90), Color.FromArgb(210, 140, 140)),
            _ => (Color.FromArgb(120, 120, 130), Color.FromArgb(180, 180, 190)),
        };

        // 32 пикселя — размер, из которого Windows корректно получает и мелкий вариант.
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var ringPen = new Pen(outer, 3.2f);
            g.DrawEllipse(ringPen, 4, 4, 24, 24);

            using var coreBrush = new SolidBrush(inner);
            g.FillEllipse(coreBrush, 12, 12, 8, 8);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Копируем: исходный дескриптор нужно освободить сразу, иначе
            // на каждой смене состояния будет утекать по значку.
            using var temporary = Icon.FromHandle(handle);
            var icon = (Icon)temporary.Clone();
            _icons[state] = icon;
            return icon;
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();

            foreach (var icon in _icons.Values) icon.Dispose();
            _icons.Clear();
        }
        catch { /* при выходе неважно */ }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
