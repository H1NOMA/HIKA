using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Interop;

/// <summary>
/// Глобальные горячие клавиши: слушать по нажатию и выключить микрофон.
///
/// Голосовой ассистент нужен не всегда голосом. Имя приходится произносить
/// вслух, а вслух — не везде: рядом спят, идёт запись, в наушниках созвон.
/// Одно нажатие решает это без единого слова.
///
/// Клавиша живёт у отдельного невидимого окна. Так надёжнее, чем вешать её
/// на окно настроек: то создаётся и закрывается, а сочетание должно работать
/// всё время, пока программа запущена.
///
/// Переназначение приходит из наблюдателя за файлом настроек, то есть из
/// чужого потока, а RegisterHotKey требует того самого потока, где живёт окно.
/// Поэтому смена сочетания не делается сразу, а отправляется окну сообщением
/// и выполняется там, где положено.
/// </summary>
internal sealed class HotkeyListener : NativeWindow, IDisposable
{
    /// <summary>Своё сообщение «перечитай настройки». Из диапазона, отведённого приложениям.</summary>
    private const int WM_REBIND = 0x0400 + 71;

    private const int IdListen = 0xB100;
    private const int IdMute = 0xB101;

    private readonly object _lock = new();
    private (string Listen, string Mute) _pending = ("", "");
    private (string Listen, string Mute) _applied = ("", "");

    private bool _listenBound;
    private bool _muteBound;

    /// <summary>Нажали клавишу «слушать».</summary>
    public event Action? ListenPressed;

    /// <summary>Нажали клавишу выключения микрофона.</summary>
    public event Action? MutePressed;

    /// <summary>Сочетание назначить не вышло — человеку об этом надо сказать.</summary>
    public event Action<string>? Problem;

    public HotkeyListener()
    {
        // Окно нужно только как адрес для сообщений: без WS_VISIBLE оно
        // не показывается, а WS_EX_TOOLWINDOW убирает его из Alt+Tab.
        CreateHandle(new CreateParams
        {
            Caption = "HIKA hotkeys",
            Style = unchecked((int)0x80000000),   // WS_POPUP
            ExStyle = 0x00000080,                 // WS_EX_TOOLWINDOW
            X = -3000,
            Y = -3000,
            Width = 1,
            Height = 1,
        });
    }

    /// <summary>
    /// Назначает сочетания. Можно звать из любого потока и сколько угодно раз:
    /// одинаковые сочетания переназначаться не будут.
    /// </summary>
    public void Rebind(string? listen, string? mute)
    {
        lock (_lock) _pending = (listen?.Trim() ?? "", mute?.Trim() ?? "");

        try { Win32.PostMessage(Handle, WM_REBIND, IntPtr.Zero, IntPtr.Zero); }
        catch (Exception ex) { Log.Error("не удалось попросить о переназначении клавиш", ex, "hotkey"); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_REBIND)
        {
            ApplyPending();
            return;
        }

        if (m.Msg == Win32.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();

            try
            {
                if (id == IdListen) ListenPressed?.Invoke();
                else if (id == IdMute) MutePressed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("обработчик горячей клавиши упал", ex, "hotkey");
            }

            return;
        }

        base.WndProc(ref m);
    }

    private void ApplyPending()
    {
        (string Listen, string Mute) wanted;
        lock (_lock) wanted = _pending;

        if (wanted == _applied) return;

        // Сначала снимаем обе, потом назначаем обе. Порознь ломается на самом
        // очевидном действии: человек меняет две клавиши местами, и первая
        // пытается занять сочетание, которое вторая ещё держит. Windows
        // отказывает, и клавиша остаётся неназначенной навсегда — повторное
        // «Применить» её уже не чинит, потому что запись в настройках
        // не изменилась.
        if (_listenBound) { Release(IdListen); _listenBound = false; }
        if (_muteBound) { Release(IdMute); _muteBound = false; }

        _listenBound = Bind(IdListen, wanted.Listen, "слушать по клавише");
        _muteBound = Bind(IdMute, wanted.Mute, "выключение микрофона");

        _applied = wanted;
    }

    private void Release(int id)
    {
        try { Win32.UnregisterHotKey(Handle, id); }
        catch (Exception ex) { Log.Warn($"снять клавишу не вышло: {ex.Message}", "hotkey"); }
    }

    private bool Bind(int id, string combination, string what)
    {
        if (string.IsNullOrWhiteSpace(combination)) return false;

        var hotkey = Hotkey.Parse(combination);
        if (hotkey is null)
        {
            Log.Warn($"«{combination}» — не сочетание клавиш, {what} остаётся без клавиши", "hotkey");
            Problem?.Invoke(
                $"«{combination}» не похоже на сочетание клавиш, поэтому {what} с клавиатуры не работает. " +
                "Задайте его в настройках, раздел «Поведение».");
            return false;
        }

        // NoRepeat обязателен: без него зажатая клавиша сыплет срабатываниями
        // десятками в секунду, и «нажал» превращается в «нажал сто раз».
        if (Win32.RegisterHotKey(Handle, id, hotkey.Modifiers | Hotkey.ModNoRepeat, hotkey.Key))
        {
            Log.Info($"{what}: {hotkey.Text}", "hotkey");
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        Log.Warn($"сочетание {hotkey.Text} не назначилось, код {error}", "hotkey");

        Problem?.Invoke(error == Win32.ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"Сочетание {hotkey.Text} занято другой программой, поэтому {what} с клавиатуры не работает. " +
              "Выберите другое в настройках, раздел «Поведение»."
            : $"Не вышло назначить {hotkey.Text} (ошибка {error}), поэтому {what} с клавиатуры не работает.");

        return false;
    }

    public void Dispose()
    {
        try
        {
            if (_listenBound) Win32.UnregisterHotKey(Handle, IdListen);
            if (_muteBound) Win32.UnregisterHotKey(Handle, IdMute);
        }
        catch (Exception ex)
        {
            Log.Warn($"снять горячие клавиши не вышло: {ex.Message}", "hotkey");
        }

        try { DestroyHandle(); } catch { }
    }
}
