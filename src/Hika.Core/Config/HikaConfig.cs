using System.Text.Json.Serialization;

namespace Hika.Config;

/// <summary>
/// Все настройки HIKA. Лежат в %APPDATA%\HIKA\config.json и правятся руками —
/// файл перечитывается на лету, перезапуск не нужен.
/// </summary>
public sealed class HikaConfig
{
    public AudioConfig Audio { get; set; } = new();
    public SpeechConfig Speech { get; set; } = new();
    public WakeConfig Wake { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();

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

    /// <summary>Сколько тишины считать концом фразы, мс.</summary>
    public int SilenceMs { get; set; } = 600;

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
    /// По умолчанию самая толковая из доступных: на русском она заметно лучше
    /// остальных, а короткие выдуманные слова вроде «Хико» и «Ави» узнаёт
    /// там, где small слышит что попало. Плата — 547 МБ загрузки и больше
    /// времени на процессоре без видеокарты.
    ///
    /// Если ответ приходит дольше секунды, смотрите в журнал время
    /// распознавания и опускайтесь до medium или small.
    /// </summary>
    public string Model { get; set; } = "largev3turbo";

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
    /// Основные слова пробуждения. Варианты произношения добавляются автоматически.
    ///
    /// «Хико» стоит наравне с «Хика», а не считается её искажением: люди
    /// произносят имя так, как им удобнее, и подгонять человека под словарь —
    /// не та сторона, с которой стоит решать задачу.
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
}

public sealed class OverlayConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>primary | all — светиться на главном мониторе или на всех.</summary>
    public string Monitors { get; set; } = "primary";

    /// <summary>Толщина каймы в долях меньшей стороны экрана. 0.09 ≈ 100 px на 1080p.</summary>
    public double Thickness { get; set; } = 0.09;

    /// <summary>Предел яркости, 0..1. Смысл настройки — не засветить экран.</summary>
    public double MaxOpacity { get; set; } = 0.55;

    /// <summary>Яркость едва заметного свечения, когда просто услышали голос, но слова пробуждения ещё нет.</summary>
    public double SensingOpacity { get; set; } = 0.18;

    /// <summary>Насколько сильно голос раскачивает свечение, 0..1.</summary>
    public double VoiceReactivity { get; set; } = 0.75;

    /// <summary>Цвета каймы по кругу: верх, право, низ, лево. Оттенок плавно перетекает между ними.</summary>
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

    /// <summary>Стартовать с выключенным микрофоном.</summary>
    public bool StartMuted { get; set; } = false;

    /// <summary>Глобальная клавиша выключения микрофона. Пусто — не назначать.</summary>
    public string MuteHotkey { get; set; } = "Ctrl+Alt+H";

    /// <summary>Уровень журнала: trace | debug | info | warn | error</summary>
    public string LogLevel { get; set; } = "info";
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
