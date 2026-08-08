using System.Text.Json.Serialization;

namespace Hika.Config;

/// <summary>
/// Все настройки HIKA. Лежат в %APPDATA%\HIKA\config.json и правятся руками —
/// файл перечитывается на лету, перезапуск не нужен.
/// </summary>
public sealed class HikaConfig
{
    /// <summary>
    /// Кто сейчас откликается: hika или avi. Задаёт главное имя и цвет,
    /// которым живёт всё остальное — значок, свечение, окно настроек.
    /// </summary>
    public string Persona { get; set; } = "hika";

    public AudioConfig Audio { get; set; } = new();
    public SpeechConfig Speech { get; set; } = new();
    public WakeConfig Wake { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public VoiceConfig Voice { get; set; } = new();
    public BrainConfig Brain { get; set; } = new();
    public LearningConfig Learning { get; set; } = new();

    /// <summary>Свои команды поверх встроенного каталога: фраза -> что открыть.</summary>
    public List<CustomEntry> Custom { get; set; } = new();
}

public sealed class AudioConfig
{
    /// <summary>
    /// Часть названия микрофона. Пусто — устройство по умолчанию.
    /// Список доступных имён: Hika.exe --list-audio
    /// </summary>
    public string Device { get; set; } = "";

    /// <summary>Программное усиление входа, 1.0 — без изменений. Тихий микрофон лечится значением 1.5–3.0.</summary>
    public float Gain { get; set; } = 1.0f;

    /// <summary>Порог энергии для запасного детектора речи (когда нет модели Silero).</summary>
    public float EnergyThreshold { get; set; } = 0.012f;

    /// <summary>Порог вероятности речи для Silero VAD, 0..1. Выше — меньше ложных срабатываний.</summary>
    public float VadThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Сколько тишины считать концом фразы, мс.
    ///
    /// Это чистая задержка перед каждой командой: раньше этого срока
    /// распознавание даже не начнётся. Полсекунды — компромисс между
    /// отзывчивостью и риском разрезать фразу на паузе между словами.
    /// </summary>
    public int SilenceMs { get; set; } = 500;

    /// <summary>Речь короче этого — шум, игнорируем.</summary>
    public int MinSpeechMs { get; set; } = 260;

    /// <summary>Жёсткий предел длины одной фразы, мс.</summary>
    public int MaxUtteranceMs { get; set; } = 15000;

    /// <summary>Сколько звука до начала речи подмешивать в фразу, мс. Спасает съеденное начало слова.</summary>
    public int PreRollMs { get; set; } = 320;
}

public sealed class SpeechConfig
{
    /// <summary>
    /// tiny | base | small | medium | largev3turbo
    ///
    /// По умолчанию «small»: на русском её хватает, а главное — она успевает.
    /// Большие модели точнее, но на процессоре без видеокарты считают дольше,
    /// чем длится сама речь, и ассистент, отвечающий через полминуты, не нужен
    /// никому независимо от того, насколько верно он расслышал.
    ///
    /// Брать largev3turbo стоит с ускорением на видеокарте — тогда она и точнее,
    /// и быстрее всего остального.
    /// </summary>
    public string Model { get; set; } = "small";

    /// <summary>
    /// Модель для ранней проверки имени. Пусто — та же, что основная.
    ///
    /// Раздельные модели здесь дают почти весь выигрыш по отзывчивости.
    /// Ранняя проверка отвечает на единственный вопрос — прозвучало ли имя, —
    /// и для него хватает самой маленькой модели. Пока она была общей
    /// с основной, каждая команда стоила трёх тяжёлых распознаваний вместо
    /// одного: две проверки плюс сама фраза.
    /// </summary>
    public string ProbeModel { get; set; } = "base";

    /// <summary>Квантование: none | q5_0 | q8_0. Квантованные модели меньше и быстрее, качество почти то же.</summary>
    public string Quantization { get; set; } = "q5_0";

    /// <summary>
    /// ru | en | auto.
    ///
    /// По умолчанию «ru», потому что русский заявлен главным, а определение языка
    /// на коротких фразах ненадёжно: «Ави, ютуб» длится секунду, и модель вполне
    /// может счесть это английским. В режиме «ru» английские названия приезжают
    /// кириллицей («опен ворд»), но распознаватель команд переводит их обратно
    /// через транслитерацию — так что английские команды продолжают работать.
    /// Ставьте «auto», только если говорите по-английски не реже, чем по-русски.
    /// </summary>
    public string Language { get; set; } = "ru";

    /// <summary>Потоков распознавания. 0 — половина логических ядер.</summary>
    public int Threads { get; set; } = 0;

    /// <summary>Куда класть модели. Пусто — %APPDATA%\HIKA\models</summary>
    public string ModelDirectory { get; set; } = "";

    /// <summary>
    /// Ранняя проверка слова пробуждения на первых миллисекундах речи.
    /// Именно она заставляет свечение вспыхивать сразу, а не в конце фразы.
    /// </summary>
    public bool EarlyWakeProbe { get; set; } = true;

    /// <summary>Сколько речи накопить перед ранней проверкой, мс.</summary>
    public int ProbeAfterMs { get; set; } = 900;
}

public sealed class WakeConfig
{
    /// <summary>
    /// Слова пробуждения.
    ///
    /// «Хико» стоит наравне с «Хика», а не считается её искажением: люди
    /// произносят имя так, как им удобнее, и подгонять человека под словарь —
    /// не та сторона, с которой стоит решать задачу.
    ///
    /// Внимание: список переписывается окном настроек при выборе личности,
    /// чтобы выбор и содержимое файла не разошлись. Свои имена и написания
    /// добавляйте в <see cref="ExtraVariants"/> — их не трогает никто.
    /// </summary>
    public List<string> Words { get; set; } = new() { "хика", "хико", "ави" };

    /// <summary>
    /// Дополнительные написания — сюда стоит дописывать то, как Whisper реально
    /// расслышал слово. Смотреть в журнале строки «услышано:».
    /// </summary>
    public List<string> ExtraVariants { get; set; } = new();

    /// <summary>
    /// Насколько прощать расхождение, 0..1. Больше — срабатывает чаще,
    /// но и ложно тоже.
    ///
    /// Значение подобрано в сторону «лучше лишний раз проснуться, чем
    /// не услышать»: не отреагировать на прямое обращение обиднее, чем
    /// разок откликнуться зря. От ложных запусков защищает не этот порог,
    /// а правило ниже — при слабом совпадении имени команда обязана
    /// однозначно найтись в каталоге.
    /// </summary>
    public double Tolerance { get; set; } = 0.45;

    /// <summary>
    /// Ниже этой уверенности в имени команда выполняется, только если
    /// точно нашлась в каталоге — без ухода в поисковик.
    ///
    /// Смысл: расплата за щедрый порог не должна выражаться в браузере,
    /// открывающемся с бессмыслицей посреди разговора.
    /// </summary>
    public double StrictBelowScore { get; set; } = 0.8;

    /// <summary>Реагировать на слово пробуждения где угодно во фразе, а не только в начале.</summary>
    public bool AllowAnywhere { get; set; } = false;

    /// <summary>
    /// Откликаться и на имя второй личности. Цвет и подписи при этом остаются
    /// от выбранной — меняется только то, на что она отзывается.
    /// </summary>
    public bool RespondToBoth { get; set; } = true;
}

public sealed class OverlayConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// all | primary | active — где показывать кайму.
    ///
    /// По умолчанию на всех: «не вижу свечения» слишком часто означает
    /// «оно на другом мониторе», и такой ответ не должен вообще возникать.
    /// «active» — только на том экране, где сейчас указатель мыши.
    /// </summary>
    public string Monitors { get; set; } = "all";

    /// <summary>Толщина каймы в долях меньшей стороны экрана. 0.09 ≈ 100 px на 1080p.</summary>
    public double Thickness { get; set; } = 0.09;

    /// <summary>Предел яркости, 0..1. Смысл настройки — не засветить экран.</summary>
    public double MaxOpacity { get; set; } = 0.55;

    /// <summary>Яркость едва заметного свечения, когда просто услышали голос, но слова пробуждения ещё нет.</summary>
    public double SensingOpacity { get; set; } = 0.18;

    /// <summary>Насколько сильно голос раскачивает свечение, 0..1.</summary>
    public double VoiceReactivity { get; set; } = 0.75;

    /// <summary>
    /// Брать цвета каймы у выбранной личности: у Хики синие, у Ави оранжевые.
    /// Выключите, если хотите задать свои в <see cref="Colors"/>.
    /// </summary>
    public bool UsePersonaColors { get; set; } = true;

    /// <summary>
    /// Свои цвета каймы по кругу: верх, право, низ, лево. Оттенок плавно
    /// перетекает между ними. Действуют, только если выключено
    /// <see cref="UsePersonaColors"/>.
    /// </summary>
    public List<string> Colors { get; set; } = new() { "#3AA0FF", "#8A6CFF", "#FF5FA2", "#31D6BC" };

    public string SuccessColor { get; set; } = "#3BE07E";
    public string ErrorColor { get; set; } = "#FF5A4E";

    /// <summary>Кадров в секунду. 60 — плавно; 30 достаточно и экономит батарею ноутбука.</summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>composition | layered | auto — движок отрисовки. «auto» пробует лучший и откатывается.</summary>
    public string Renderer { get; set; } = "auto";

    /// <summary>Прятать свечение от записи экрана и скриншотов.</summary>
    public bool ExcludeFromCapture { get; set; } = false;
}

public sealed class BehaviorConfig
{
    /// <summary>
    /// Сколько секунд после «Ави» без команды ждать продолжения.
    /// Позволяет сказать «Ави» ... пауза ... «открой ютуб».
    /// </summary>
    public int ArmedSeconds { get; set; } = 6;

    /// <summary>Если ничего не нашли — искать фразу в поисковике вместо того, чтобы промолчать.</summary>
    public bool WebSearchFallback { get; set; } = true;

    /// <summary>Шаблон поиска, {q} подставляется.</summary>
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={q}";

    /// <summary>Насколько уверенным должно быть совпадение, чтобы что-то запустить, 0..1.</summary>
    public double MatchThreshold { get; set; } = 0.62;

    /// <summary>Индексировать меню «Пуск» и установленные приложения Windows.</summary>
    public bool IndexInstalledApps { get; set; } = true;

    /// <summary>Как часто переиндексировать установленные приложения, минут.</summary>
    public int ReindexMinutes { get; set; } = 30;

    /// <summary>Записывать распознанный текст в журнал. Полезно для отладки, но это ваша речь на диске.</summary>
    public bool LogTranscripts { get; set; } = true;

    /// <summary>Автозапуск вместе с Windows.</summary>
    public bool Autostart { get; set; } = false;

    /// <summary>
    /// Запускаться с правами администратора.
    ///
    /// Выключено по умолчанию и включать это стоит, только зная зачем.
    /// Единственная настоящая польза — управление окнами программ, которые
    /// сами запущены от администратора. Всё остальное режим только ухудшает:
    /// антивирусы относятся к такой программе строже, перетаскивание файлов
    /// из проводника перестаёт работать, а при каждом обычном запуске
    /// появляется запрос UAC.
    ///
    /// Наследование прав запускаемыми программами при этом не грозит:
    /// в повышенном режиме HIKA открывает их через проводник, и они
    /// достаются обычному пользователю.
    /// </summary>
    public bool RunAsAdmin { get; set; } = false;

    /// <summary>Стартовать с выключенным микрофоном.</summary>
    public bool StartMuted { get; set; } = false;

    /// <summary>Глобальная клавиша выключения микрофона. Пусто — не назначать.</summary>
    public string MuteHotkey { get; set; } = "Ctrl+Alt+H";

    /// <summary>Уровень журнала: trace | debug | info | warn | error</summary>
    public string LogLevel { get; set; } = "info";
}

public sealed class VoiceConfig
{
    /// <summary>
    /// Отвечать голосом.
    ///
    /// Изначально задумывалось наоборот: ассистент действует и молчит. Голос
    /// добавлен по прямой просьбе — но привычка остаётся правильной. Молчание
    /// при запуске программы информативнее, чем «открываю Steam», сказанное
    /// поверх уже открывающегося Steam. Поэтому вслух идут ответы на вопросы,
    /// а действия по-прежнему говорят сами за себя.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// auto | windows | edge | off
    ///
    /// «auto» выбирает лучшее из доступного, не выходя в интернет без нужды:
    /// сначала ищет нейроголоса, установленные в самой Windows, и только если
    /// их нет — берёт то, что есть. Голоса Microsoft из интернета («edge»)
    /// звучат лучше всего, но включать их надо осознанно: произносимый текст
    /// уходит на серверы Microsoft.
    /// </summary>
    public string Engine { get; set; } = "auto";

    /// <summary>Часть названия голоса. Пусто — выбрать лучший доступный. Список: Hika.exe --list-voices</summary>
    public string Voice { get; set; } = "";

    /// <summary>Скорость речи, 0.5–2.0. Единица — обычная.</summary>
    public double Rate { get; set; } = 1.0;

    /// <summary>Громкость, 0–1.</summary>
    public double Volume { get; set; } = 0.85;

    /// <summary>Проговаривать вслух, что команда не вышла.</summary>
    public bool SpeakFailures { get; set; } = true;

    /// <summary>Проговаривать вслух подтверждение запуска. По умолчанию нет — действие видно и так.</summary>
    public bool SpeakConfirmations { get; set; } = false;

    /// <summary>
    /// Глушить микрофон, пока говорит сама.
    ///
    /// Обязательно, если звук идёт из колонок: иначе ассистент услышит
    /// собственный голос, распознает его как речь и в худшем случае ответит
    /// сам себе. В наушниках можно выключить.
    /// </summary>
    public bool SuppressMicWhileSpeaking { get; set; } = true;
}

public sealed class BrainConfig
{
    /// <summary>
    /// Отвечать на вопросы через Claude.
    ///
    /// Выключено, пока нет ключа: без него включать нечего. Ключ хранится
    /// не здесь — см. окно настроек, раздел «Разговор».
    /// </summary>
    public bool Enabled { get; set; } = false;

    public string Model { get; set; } = "claude-opus-5";

    /// <summary>
    /// Предел длины ответа.
    ///
    /// Маленький намеренно: ответ произносится вслух, а слушать вслух даже
    /// две минуты подряд невыносимо. Если нужно подробно — лучше спросить
    /// подробнее, чем ждать, пока договорит.
    /// </summary>
    public int MaxTokens { get; set; } = 700;

    /// <summary>Сколько последних реплик помнить в разговоре.</summary>
    public int HistoryTurns { get; set; } = 12;

    /// <summary>Если команда не нашлась — попробовать ответить словами вместо красной вспышки.</summary>
    public bool AnswerUnknownCommands { get; set; } = true;

    /// <summary>Сколько секунд после ответа слушать продолжение без имени.</summary>
    public int FollowUpSeconds { get; set; } = 15;

    /// <summary>Что дописать к описанию характера. Пусто — как есть.</summary>
    public string Style { get; set; } = "";

    /// <summary>Рассказывать Claude, чем человек обычно пользуется. Помогает в ответах про его же компьютер.</summary>
    public bool ShareProfile { get; set; } = true;
}

public sealed class LearningConfig
{
    /// <summary>Наблюдать за речью и подстраиваться.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Вести дневник речи — файл со всем услышанным.</summary>
    public bool KeepJournal { get; set; } = true;

    /// <summary>
    /// Сколько своих слов подкладывать распознаванию как подсказку.
    ///
    /// Больше — не всегда лучше: затравка соревнуется за внимание модели
    /// со всем остальным, и словарь из двухсот слов размывает подсказку
    /// вместо того, чтобы её усилить.
    /// </summary>
    public int MaxPromptTerms { get; set; } = 24;

    /// <summary>Учить новые написания имени.</summary>
    public bool LearnWakeVariants { get; set; } = true;

    /// <summary>Сколько раз услышать одно и то же написание, чтобы принять его.</summary>
    public int WakeVariantThreshold { get; set; } = 3;

    /// <summary>Учить синонимы из пары «не вышло — вышло».</summary>
    public bool LearnAliases { get; set; } = true;

    /// <summary>Предел прибавки за частый запуск, 0..1. Больше 0.15 ставить не стоит: перебьёт само сходство.</summary>
    public double MaxBoost { get; set; } = 0.10;
}

public sealed class CustomEntry
{
    /// <summary>Как это называть голосом. Можно несколько вариантов.</summary>
    public List<string> Phrases { get; set; } = new();

    /// <summary>Что запустить: путь к программе, ссылка или AppUserModelID.</summary>
    public string Target { get; set; } = "";

    /// <summary>Аргументы командной строки, если это программа.</summary>
    public string Arguments { get; set; } = "";

    [JsonIgnore]
    public bool IsValid => Phrases.Count > 0 && !string.IsNullOrWhiteSpace(Target);
}
