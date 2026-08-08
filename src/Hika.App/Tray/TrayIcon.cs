using System.Drawing;
using System.Windows.Forms;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Startup;
using Hika.Ui;

namespace Hika.Tray;

/// <summary>
/// Значок в трее — единственная постоянно видимая часть HIKA.
///
/// Он здесь не для красоты: программа круглые сутки слушает микрофон,
/// и у человека должен быть способ увидеть, что она делает, и выключить
/// её одним движением. Ассистент с открытым микрофоном и без видимого
/// выключателя — плохая программа независимо от того, насколько хорошо
/// он распознаёт речь.
///
/// Значок рисуется кодом тем же способом, что и логотип в окне настроек,
/// поэтому они не могут разойтись: сменил личность — сменилось и то и другое.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _muteItem;
    private readonly ToolStripMenuItem _autostartItem;

    private readonly Dictionary<(string Persona, bool Muted), Icon> _icons = new();

    private string _personaId = "hika";
    private bool _muted;

    public event Action? MuteToggleRequested;
    public event Action? ExitRequested;
    public event Action? DiagnosticsRequested;
    public event Action? LiveListenRequested;
    public event Action? SettingsRequested;

    public TrayIcon(string personaId)
    {
        _personaId = Personas.ById(personaId).Id;

        _statusItem = new ToolStripMenuItem("Запускается…") { Enabled = false };

        var settingsItem = new ToolStripMenuItem("Настройки", null, (_, _) => SettingsRequested?.Invoke())
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        };

        _muteItem = new ToolStripMenuItem("Выключить микрофон", null, (_, _) => MuteToggleRequested?.Invoke());

        _autostartItem = new ToolStripMenuItem("Запускать вместе с Windows", null, OnAutostartClicked)
        {
            CheckOnClick = true,
            Checked = AutostartManager.IsEnabled(),
        };

        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
        };

        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(_muteItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Что я слышу", null, (_, _) => LiveListenRequested?.Invoke()));
        _menu.Items.Add(new ToolStripMenuItem("Диагностика", null, (_, _) => DiagnosticsRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = IconFor(_personaId, muted: false),
            Text = "HIKA",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        // Обычный щелчок открывает настройки — это самое частое, зачем к значку
        // вообще тянутся. Микрофон выключается двойным щелчком и через меню.
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) SettingsRequested?.Invoke();
            else if (e.Button == MouseButtons.Middle) MuteToggleRequested?.Invoke();
        };
    }

    public void SetPersona(string personaId)
    {
        var id = Personas.ById(personaId).Id;
        if (id == _personaId) return;

        _personaId = id;
        RefreshIcon();
    }

    public void UpdateState(HostState state, bool muted)
    {
        _muted = muted;

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
            RefreshIcon();

            // Подпись значка в Windows ограничена 63 символами.
            var tooltip = $"HIKA · {Personas.ById(_personaId).Name} — {status}";
            _icon.Text = tooltip.Length > 60 ? tooltip[..60] : tooltip;
        }
        catch (Exception ex)
        {
            Log.Debug($"значок в трее не обновился: {ex.Message}", "tray");
        }
    }

    private void RefreshIcon()
    {
        try { _icon.Icon = IconFor(_personaId, _muted); }
        catch (Exception ex) { Log.Debug($"значок не перерисовался: {ex.Message}", "tray"); }
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

    private Icon IconFor(string personaId, bool muted)
    {
        var key = (personaId, muted);
        if (_icons.TryGetValue(key, out var cached)) return cached;

        var accent = Theme.AccentOf(Personas.ById(personaId));
        var icon = RingLogo.CreateIcon(accent, muted);

        _icons[key] = icon;
        return icon;
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
}
