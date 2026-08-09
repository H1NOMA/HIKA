using System.Runtime.InteropServices;
using Hika.Diagnostics;
using Hika.Interop;
using NAudio.CoreAudioApi;

namespace Hika.Skills;

/// <summary>
/// Управление системой: громкость, воспроизведение, окна, блокировка.
///
/// Громкость меняется не нажатием клавиш, а напрямую через звуковую подсистему
/// Windows. Так точнее — шаг задаём мы, а не производитель клавиатуры, — и,
/// что важнее, это на одно синтетическое нажатие клавиш меньше: чем реже
/// программа изображает ввод пользователя, тем спокойнее к ней относятся антивирусы.
/// </summary>
public static class SystemActions
{
    private const float VolumeStep = 0.08f;

    public static SkillResult VolumeUp() => AdjustVolume(+VolumeStep, "громкость увеличена");
    public static SkillResult VolumeDown() => AdjustVolume(-VolumeStep, "громкость уменьшена");

    private static SkillResult AdjustVolume(float delta, string description)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var volume = device.AudioEndpointVolume;
            var current = volume.MasterVolumeLevelScalar;
            var target = Math.Clamp(current + delta, 0f, 1f);

            volume.MasterVolumeLevelScalar = target;

            // Прибавление громкости на выключенном звуке должно его включать —
            // иначе человек крутит регулятор и не понимает, почему тихо.
            if (delta > 0 && volume.Mute) volume.Mute = false;

            Log.Info($"{description}: {current:P0} -> {target:P0}", "system");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error("не удалось изменить громкость", ex, "system");
            return SkillResult.Fail("громкость недоступна");
        }
    }

    public static SkillResult ToggleMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var volume = device.AudioEndpointVolume;
            volume.Mute = !volume.Mute;

            var description = volume.Mute ? "звук выключен" : "звук включён";
            Log.Info(description, "system");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error("не удалось переключить звук", ex, "system");
            return SkillResult.Fail("звук недоступен");
        }
    }

    /// <summary>
    /// Громкость на заданное число процентов.
    ///
    /// Абсолютное значение, а не шаг: «сделай тридцать» — это тридцать,
    /// сколько бы ни стояло до того. Шагами это выражается только чередой
    /// команд, и человек их произносить не станет.
    /// </summary>
    public static SkillResult SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var volume = device.AudioEndpointVolume;
            volume.MasterVolumeLevelScalar = percent / 100f;

            // Ноль процентов — это и есть просьба о тишине; всё остальное
            // подразумевает, что звук должен быть слышен.
            volume.Mute = percent == 0;

            Log.Info($"громкость {percent}%", "system");
            return SkillResult.Ok($"громкость {percent} процентов");
        }
        catch (Exception ex)
        {
            Log.Error("не удалось выставить громкость", ex, "system");
            return SkillResult.Fail("громкость недоступна");
        }
    }

    // Управление воспроизведением живёт в MediaSessions: там видно, кто именно
    // играет, и пауза адресуется ему, а не уходит в никуда мультимедийной
    // клавишей. Здесь остаётся только та самая клавиша — как запасной путь.

    public static SkillResult TapMedia(ushort key, string description)
    {
        try
        {
            Win32.TapKey(key);
            Log.Info(description, "system");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error("медиаклавиша не отправилась", ex, "system");
            return SkillResult.Fail("не вышло");
        }
    }

    /// <summary>Сочетание клавиш активному окну — вкладки, обновление, шаг назад.</summary>
    public static SkillResult Combo(string description, params ushort[] keys)
    {
        try
        {
            Win32.TapCombo(keys);
            Log.Info(description, "system");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error($"сочетание клавиш не отправилось: {description}", ex, "system");
            return SkillResult.Fail("не вышло");
        }
    }

    public static SkillResult Time()
    {
        var now = DateTime.Now;
        var text = now.ToString("HH:mm");

        Log.Info($"время: {text}", "system");
        return SkillResult.Ok($"сейчас {text}");
    }

    /// <summary>
    /// Сон, а не выключение.
    ///
    /// Выключения и перезагрузки здесь нет намеренно. Ассистент, срабатывающий
    /// от похожего слова, не должен иметь права выключить компьютер: цена
    /// одной ошибки — несохранённая работа за весь день. Сон обратим одним
    /// движением мыши, и цена ошибки в нём — секунда.
    /// </summary>
    public static SkillResult Sleep()
    {
        try
        {
            if (Win32.SetSuspendState(hibernate: false, force: false, disableWakeEvent: false))
            {
                Log.Info("компьютер уходит в сон", "system");
                return SkillResult.Ok("ухожу в сон");
            }

            Log.Warn("сон не сработал", "system");
            return SkillResult.Fail("не вышло уйти в сон");
        }
        catch (Exception ex)
        {
            Log.Error("сон не сработал", ex, "system");
            return SkillResult.Fail("не вышло уйти в сон");
        }
    }

    public static SkillResult LockWorkstation()
    {
        try
        {
            if (Win32.LockWorkStation())
            {
                Log.Info("компьютер заблокирован", "system");
                return SkillResult.Ok("компьютер заблокирован");
            }

            var error = Marshal.GetLastWin32Error();
            Log.Warn($"блокировка не сработала, код {error}", "system");
            return SkillResult.Fail("не вышло заблокировать");
        }
        catch (Exception ex)
        {
            Log.Error("блокировка не сработала", ex, "system");
            return SkillResult.Fail("не вышло заблокировать");
        }
    }

    public static SkillResult ShowDesktop()
    {
        try
        {
            StaRunner.Run(() =>
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application")
                    ?? throw new InvalidOperationException("Shell.Application недоступен");

                var shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("не удалось создать Shell.Application");

                try
                {
                    dynamic dynamicShell = shell;
                    dynamicShell.MinimizeAll();
                }
                finally
                {
                    if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                }
            });

            Log.Info("все окна свёрнуты", "system");
            return SkillResult.Ok("все окна свёрнуты");
        }
        catch (Exception ex)
        {
            Log.Error("свернуть всё не удалось", ex, "system");
            return SkillResult.Fail("не вышло свернуть окна");
        }
    }

    public static SkillResult MinimizeActiveWindow()
    {
        try
        {
            var window = Win32.GetForegroundWindow();
            if (window == IntPtr.Zero) return SkillResult.Fail("активного окна нет");

            Win32.ShowWindow(window, Win32.SW_MINIMIZE);
            Log.Info("окно свёрнуто", "system");
            return SkillResult.Ok("окно свёрнуто");
        }
        catch (Exception ex)
        {
            Log.Error("свернуть окно не удалось", ex, "system");
            return SkillResult.Fail("не вышло свернуть окно");
        }
    }

    public static SkillResult CloseActiveWindow()
    {
        try
        {
            var window = Win32.GetForegroundWindow();
            if (window == IntPtr.Zero) return SkillResult.Fail("активного окна нет");

            // Именно WM_CLOSE, а не завершение процесса: программа получит шанс
            // спросить про несохранённое. Голосовая команда не должна ничего терять.
            Win32.PostMessage(window, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            Log.Info("окно закрыто", "system");
            return SkillResult.Ok("окно закрыто");
        }
        catch (Exception ex)
        {
            Log.Error("закрыть окно не удалось", ex, "system");
            return SkillResult.Fail("не вышло закрыть окно");
        }
    }

    public static SkillResult Screenshot()
    {
        try
        {
            // Win+Shift+S — штатные «Ножницы» Windows: снимок попадает в буфер обмена,
            // и человек сам выбирает область.
            Win32.TapCombo(Win32.VK_LWIN, Win32.VK_SHIFT, Win32.VK_S);
            Log.Info("вызван инструмент снимка экрана", "system");
            return SkillResult.Ok("снимок экрана");
        }
        catch (Exception ex)
        {
            Log.Error("снимок экрана не вызвался", ex, "system");
            return SkillResult.Fail("не вышло");
        }
    }
}
