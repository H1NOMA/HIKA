using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Interop;
using Hika.Startup;

namespace Hika.Ui;

/// <summary>
/// Окно настроек — всё, чем можно управлять, в одном месте.
///
/// Существует потому, что файл config.json удобен мне и совершенно неудобен
/// человеку: чтобы поправить чувствительность, не должно требоваться знание
/// имени поля и того, в какой из пяти разделов оно вложено.
///
/// У каждой настройки есть пояснение обычными словами. Значение «порог 0.45»
/// само по себе не говорит ничего, а отправлять за смыслом в документацию —
/// надёжный способ гарантировать, что настройку не тронут никогда.
/// </summary>
public sealed class SettingsWindow : Form
{
    private readonly ConfigStore _store;
    private readonly Func<double> _levelSource;
    private readonly Action<HikaConfig> _onApply;
    private readonly Action _onLiveListen;
    private readonly Action _onDiagnostics;
    private readonly Action _onTestOverlay;
    private readonly Func<(string Status, string Microphone, string Recognizer, int CatalogSize)> _statusSource;

    /// <summary>
    /// Живой ведущий — нужен разделам, где показывается не настройка, а состояние:
    /// какой голос выбран на самом деле, что уже выучено, отвечает ли ключ.
    /// Может отсутствовать: окно открывается и до того, как всё поднялось.
    /// </summary>
    private readonly AppHost? _host;

    private HikaConfig _config;

    private readonly NavList _nav = new();
    private readonly Panel _content = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Label _notice = new();

    // ---- Элементы, значения которых нужно читать при сохранении ----
    private readonly List<PersonaCard> _personaCards = new();
    private string _personaId = "hika";

    private ToggleSwitch _respondToBoth = null!;
    private SliderField _tolerance = null!;
    private ToggleSwitch _allowAnywhere = null!;
    private TextField _extraVariants = null!;

    private DropDownField _device = null!;
    private SliderField _gain = null!;
    private SliderField _vadThreshold = null!;
    private SliderField _silenceMs = null!;
    private SliderField _minSpeechMs = null!;

    private DropDownField _model = null!;
    private DropDownField _probeModel = null!;
    private DropDownField _language = null!;
    private SliderField _threads = null!;
    private ToggleSwitch _earlyProbe = null!;
    private ToggleSwitch _adaptiveContext = null!;
    private ToggleSwitch _fastDecoding = null!;
    private SliderField _probeAfterMs = null!;

    private ToggleSwitch _overlayEnabled = null!;
    private DropDownField _monitors = null!;
    private SliderField _thickness = null!;
    private SliderField _maxOpacity = null!;
    private ToggleSwitch _showBeforeWake = null!;
    private SliderField _sensingOpacity = null!;
    private SliderField _reactivity = null!;
    private SliderField _fps = null!;
    private ToggleSwitch _personaColors = null!;
    private ToggleSwitch _excludeCapture = null!;

    private ToggleSwitch _voiceEnabled = null!;
    private ToggleSwitch _neuralOnly = null!;
    private DropDownField _voiceEngine = null!;
    private DropDownField _voiceName = null!;
    private SliderField _voiceRate = null!;
    private SliderField _voiceVolume = null!;
    private ToggleSwitch _speakFailures = null!;
    private ToggleSwitch _speakConfirmations = null!;
    private ToggleSwitch _suppressMic = null!;
    private Label _voiceStatus = null!;

    private ToggleSwitch _brainEnabled = null!;
    private TextField _apiKey = null!;
    private DropDownField _brainModel = null!;
    private SliderField _brainMaxTokens = null!;
    private SliderField _followUpSeconds = null!;
    private ToggleSwitch _answerUnknown = null!;
    private ToggleSwitch _shareProfile = null!;
    private TextField _brainStyle = null!;
    private Label _brainStatus = null!;

    private ToggleSwitch _learningEnabled = null!;
    private ToggleSwitch _keepJournal = null!;
    private SliderField _maxPromptTerms = null!;
    private ToggleSwitch _learnWakeVariants = null!;
    private SliderField _wakeVariantThreshold = null!;
    private ToggleSwitch _learnAliases = null!;
    private Label _learningStatus = null!;

    private SliderField _armedSeconds = null!;
    private ToggleSwitch _searchFallback = null!;
    private SliderField _matchThreshold = null!;
    private ToggleSwitch _indexApps = null!;
    private ToggleSwitch _logTranscripts = null!;
    private ToggleSwitch _autostart = null!;
    private ToggleSwitch _runAsAdmin = null!;
    private ToggleSwitch _startMuted = null!;
    private DropDownField _logLevel = null!;

    private string _initialModel = "";
    private string _initialDevice = "";
    private bool _initialRunAsAdmin;

    public SettingsWindow(
        ConfigStore store,
        Func<double> levelSource,
        Func<(string, string, string, int)> statusSource,
        Action<HikaConfig> onApply,
        Action onLiveListen,
        Action onDiagnostics,
        Action onTestOverlay,
        AppHost? host = null)
    {
        _store = store;
        _levelSource = levelSource;
        _statusSource = statusSource;
        _onApply = onApply;
        _onLiveListen = onLiveListen;
        _onDiagnostics = onDiagnostics;
        _onTestOverlay = onTestOverlay;
        _host = host;

        _config = store.Current;
        _personaId = Personas.ById(_config.Persona).Id;
        Theme.ApplyPersona(_personaId);

        BuildWindow();
        BuildPages();
        LoadFromConfig();
    }

    // ---- Каркас окна ------------------------------------------------------

    private void BuildWindow()
    {
        Text = "HIKA — настройки";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(940, 660);
        MinimumSize = new Size(820, 560);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;

        ShowInTaskbar = true;
        Icon = RingLogo.CreateIcon(Theme.Accent, muted: false);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) HideWindow();
            if (e.Control && e.KeyCode == Keys.S) { Apply(); e.Handled = true; }
        };

        var header = new HeaderBar(this, _statusSource) { Dock = DockStyle.Top, Height = 74 };
        var footer = BuildFooter();

        _nav.Dock = DockStyle.Left;
        _nav.Width = 196;
        _nav.SetItems(new[]
        {
            ("persona", "Личность"),
            ("mic", "Микрофон"),
            ("speech", "Распознавание"),
            ("voice", "Голос"),
            ("brain", "Разговор"),
            ("learning", "Обучение"),
            ("glow", "Свечение"),
            ("behavior", "Поведение"),
            ("about", "О программе"),
        });

        _nav.SelectionChanged += (_, _) => ShowPage(_nav.SelectedKey);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Panel;

        // Порядок добавления задаёт, кто у кого отъедает место:
        // сначала нижняя и верхняя полосы, потом боковая, остаток — содержимому.
        Controls.Add(_content);
        Controls.Add(_nav);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Background };

        var listen = new FlatButton("Что я слышу") { Width = 150 };
        listen.Click += (_, _) => _onLiveListen();

        var diagnose = new FlatButton("Диагностика") { Width = 130 };
        diagnose.Click += (_, _) => _onDiagnostics();

        var apply = new FlatButton("Применить", primary: true) { Width = 130 };
        apply.Click += (_, _) => Apply();

        _notice.AutoSize = false;
        _notice.ForeColor = Theme.TextFaint;
        _notice.Font = Theme.Small;
        _notice.TextAlign = ContentAlignment.MiddleRight;
        _notice.BackColor = Color.Transparent;

        footer.Controls.AddRange(new Control[] { listen, diagnose, apply, _notice });

        footer.Resize += (_, _) =>
        {
            listen.Location = new Point(24, (footer.Height - listen.Height) / 2);
            diagnose.Location = new Point(listen.Right + 10, listen.Top);
            apply.Location = new Point(footer.Width - apply.Width - 24, listen.Top);
            _notice.SetBounds(diagnose.Right + 16, 0, Math.Max(60, apply.Left - diagnose.Right - 32), footer.Height);
        };

        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        return footer;
    }

    // ---- Страницы ---------------------------------------------------------

    private static Panel Stack(params Control[] children)
    {
        var panel = new Panel
        {
            AutoScroll = true,
            BackColor = Theme.Panel,
            Padding = new Padding(30, 4, 30, 28),
            Dock = DockStyle.Fill,
            Visible = false,
        };

        // Dock.Top укладывает сверху вниз в порядке, обратном добавлению.
        for (int i = children.Length - 1; i >= 0; i--)
        {
            children[i].Dock = DockStyle.Top;
            panel.Controls.Add(children[i]);
        }

        return panel;
    }

    private void BuildPages()
    {
        _pages["persona"] = BuildPersonaPage();
        _pages["mic"] = BuildMicrophonePage();
        _pages["speech"] = BuildSpeechPage();
        _pages["voice"] = BuildVoicePage();
        _pages["brain"] = BuildBrainPage();
        _pages["learning"] = BuildLearningPage();
        _pages["glow"] = BuildGlowPage();
        _pages["behavior"] = BuildBehaviorPage();
        _pages["about"] = BuildAboutPage();

        foreach (var page in _pages.Values) _content.Controls.Add(page);

        ShowPage("persona");
    }

    private void ShowPage(string key)
    {
        foreach (var (name, page) in _pages) page.Visible = name == key;
    }

    private Control BuildPersonaPage()
    {
        var chooser = new Panel { Height = 156, BackColor = Theme.Panel };

        foreach (var persona in Personas.All)
        {
            var card = new PersonaCard(persona) { Location = new Point(_personaCards.Count * 204, 8) };
            card.Picked += (_, _) => SelectPersona(persona.Id);

            _personaCards.Add(card);
            chooser.Controls.Add(card);
        }

        _respondToBoth = new ToggleSwitch();
        _tolerance = new SliderField { Minimum = 0.15, Maximum = 0.7, Step = 0.01, Format = v => v.ToString("0.00") };
        _allowAnywhere = new ToggleSwitch();
        _extraVariants = new TextField { Placeholder = "обои, а ви, хикко" };

        return Stack(
            new SectionTitle("Личность", "Имя, на которое ассистент отзывается, и цвет, которым он живёт. Цвет один и тот же в значке возле часов и в свечении по краям экрана."),
            chooser,
            new SettingRow("Отзываться и на второе имя",
                "Цвет и подписи останутся от выбранной личности — меняется только то, на что она откликается.",
                _respondToBoth, 46),
            new SectionTitle("Чувствительность к имени"),
            new SettingRow("Насколько прощать искажения",
                "Больше — откликается охотнее, но и на похожие слова тоже. Если распознаватель коверкает имя, надёжнее дописать вариант ниже, чем поднимать это значение.",
                _tolerance, 240),
            new SettingRow("Свои варианты произношения",
                "Через запятую. Впишите сюда то, как имя реально расслышала модель — это надёжнее любой подстройки порогов. Посмотреть можно кнопкой «Что я слышу».",
                _extraVariants),
            new SettingRow("Искать имя в любом месте фразы",
                "Обычно имя ждут в начале. Включённое заметно повышает число ложных срабатываний.",
                _allowAnywhere, 46));
    }

    private Control BuildMicrophonePage()
    {
        _device = new DropDownField();
        _gain = new SliderField { Minimum = 0.5, Maximum = 6, Step = 0.1, Format = v => v.ToString("0.0") + "x" };
        _vadThreshold = new SliderField { Minimum = 0.15, Maximum = 0.9, Step = 0.01, Format = v => v.ToString("0.00") };
        _silenceMs = new SliderField { Minimum = 200, Maximum = 2000, Step = 50, Format = v => $"{v:0} мс" };
        _minSpeechMs = new SliderField { Minimum = 100, Maximum = 1200, Step = 20, Format = v => $"{v:0} мс" };

        RefreshDevices();

        var refresh = new FlatButton("Обновить список") { Width = 150, Height = 32 };
        refresh.Click += (_, _) => RefreshDevices();

        return Stack(
            new SectionTitle("Микрофон", "Если ассистент молчит, начните отсюда: полоска ниже показывает, доходит ли до него звук вообще."),
            new SettingRow("Устройство", "Пусто — микрофон Windows по умолчанию.", _device),
            new SettingRow("", "", refresh, 150),
            new SettingRow("Уровень сигнала",
                "Говорите — полоска должна двигаться. Если стоит на месте, выбран не тот микрофон или он приглушён в микшере Windows.",
                new LevelBar(_levelSource)),
            new SettingRow("Усиление",
                "Для тихих микрофонов. Поднимайте, пока обычная речь не заполняет полоску примерно наполовину.",
                _gain, 240),
            new SectionTitle("Определение речи"),
            new SettingRow("Порог срабатывания",
                "Насколько уверенно звук должен быть похож на речь. Реагирует на клавиатуру и музыку — поднимите; не замечает тихую речь — опустите.",
                _vadThreshold, 240),
            new SettingRow("Пауза до конца фразы",
                "Сколько тишины считать концом сказанного. Меньше — быстрее реакция, но фразу может разрезать посреди паузы.",
                _silenceMs, 240),
            new SettingRow("Минимальная длина речи",
                "Короче этого считается шумом. Защита от кашля, щелчков и хлопнувшей двери.",
                _minSpeechMs, 240));
    }

    private void RefreshDevices()
    {
        var items = new List<(string, string)> { ("Устройство по умолчанию", "") };

        foreach (var device in MicrophoneCapture.ListDevices())
            items.Add((device.Name + (device.IsDefault ? "  (по умолчанию)" : ""), device.Name));

        _device.SetItems(items, _config.Audio.Device ?? "");
    }

    private Control BuildSpeechPage()
    {
        _model = new DropDownField();
        _model.SetItems(new[]
        {
            ("small — успевает везде, 181 МБ", "small"),
            ("base — быстрее, но грубее, 57 МБ", "base"),
            ("medium — точнее, нужна видеокарта, 539 МБ", "medium"),
            ("large-v3-turbo — лучший русский, нужна видеокарта, 547 МБ", "largev3turbo"),
            ("tiny — на самых слабых машинах, 31 МБ", "tiny"),
        });

        _probeModel = new DropDownField();
        _probeModel.SetItems(new[]
        {
            ("base — быстро и достаточно", "base"),
            ("tiny — самая быстрая", "tiny"),
            ("small — точнее, но медленнее", "small"),
            ("Та же, что основная", ""),
        });

        _language = new DropDownField();
        _language.SetItems(new[]
        {
            ("Русский", "ru"),
            ("Английский", "en"),
            ("Определять автоматически", "auto"),
        });

        _threads = new SliderField
        {
            Minimum = 0, Maximum = Math.Max(4, Environment.ProcessorCount), Step = 1,
            Format = v => v < 1 ? "авто" : $"{v:0}",
        };

        _earlyProbe = new ToggleSwitch();
        _adaptiveContext = new ToggleSwitch();
        _fastDecoding = new ToggleSwitch();
        _probeAfterMs = new SliderField { Minimum = 400, Maximum = 2000, Step = 50, Format = v => $"{v:0} мс" };

        return Stack(
            new SectionTitle("Распознавание речи", "Всё считается на этом компьютере. В сеть уходит только загрузка самой модели, один раз."),
            new SettingRow("Модель",
                "Чем крупнее, тем лучше русский — но тем дольше ответ. Без видеокарты крупные модели считают дольше, чем длится сама речь. Смена применится после перезапуска и потребует загрузки.",
                _model, 340),
            new SettingRow("Модель для проверки имени",
                "Отвечает на единственный вопрос — прозвучало ли имя, — и большой для этого не нужна. Именно раздельные модели дают почти весь выигрыш в скорости отклика.",
                _probeModel, 300),
            new SettingRow("Язык",
                "На коротких фразах вроде «Ави, ютуб» автоопределение ненадёжно. В русском режиме английские названия приезжают кириллицей, но команды всё равно доходят.",
                _language),
            new SettingRow("Потоков",
                "Ноль — половина ядер процессора. Больше не всегда быстрее: прирост обычно заканчивается на четырёх-шести.",
                _threads, 240),
            new SectionTitle("Ранняя реакция", "Из-за неё кайма загорается посреди фразы, а не после того, как вы договорили."),
            new SettingRow("Проверять имя, не дожидаясь конца фразы",
                "Стоит одного дополнительного короткого распознавания на каждое слово, произнесённое рядом. На слабом процессоре можно выключить, но реакция станет запаздывающей.",
                _earlyProbe, 46),
            new SettingRow("Через сколько проверять",
                "Меньше — загорается быстрее, но чаще мимо: имя может ещё не прозвучать.",
                _probeAfterMs, 240),
            new SectionTitle("Скорость", "Здесь лежит почти всё ожидание перед выполнением команды."),
            new SettingRow("Считать по длине фразы",
                "Модель всегда работает окном в тридцать секунд: двухсекундную команду она дополняет " +
                "тишиной и честно обрабатывает всё окно целиком. Почти вся работа уходит на эту тишину. " +
                "Окно по длине фразы ускоряет распознавание в разы. Выключайте, только если появились " +
                "странности на длинных фразах.",
                _adaptiveContext, 46),
            new SettingRow("Не переспрашивать себя",
                "Не сойдясь с порогами уверенности, модель перезапускает расшифровку с другой " +
                "температурой — до пяти раз подряд. Для расшифровки лекции это правильно, для команды " +
                "из трёх слов означает пятикратное ожидание.",
                _fastDecoding, 46));
    }

    private Control BuildGlowPage()
    {
        _overlayEnabled = new ToggleSwitch();

        _monitors = new DropDownField();
        _monitors.SetItems(new[]
        {
            ("Все мониторы", "all"),
            ("Только главный", "primary"),
            ("Тот, где сейчас мышь", "active"),
        });

        _thickness = new SliderField { Minimum = 0.03, Maximum = 0.2, Step = 0.005, Format = v => $"{v * 100:0.0} %" };
        _maxOpacity = new SliderField { Minimum = 0.1, Maximum = 1.0, Step = 0.05, Format = v => $"{v * 100:0} %" };
        _showBeforeWake = new ToggleSwitch();
        _sensingOpacity = new SliderField { Minimum = 0.0, Maximum = 0.6, Step = 0.02, Format = v => $"{v * 100:0} %" };
        _reactivity = new SliderField { Minimum = 0.0, Maximum = 1.0, Step = 0.05, Format = v => $"{v * 100:0} %" };
        _fps = new SliderField { Minimum = 20, Maximum = 120, Step = 5, Format = v => $"{v:0}" };
        _personaColors = new ToggleSwitch();
        _excludeCapture = new ToggleSwitch();

        var test = new FlatButton("Показать свечение сейчас", primary: true) { Width = 230 };
        test.Click += (_, _) => _onTestOverlay();

        return Stack(
            new SectionTitle("Свечение по краям", "Слабое, пока непонятно, к вам ли обращаются. Разгорается и живёт в такт голосу, когда прозвучало имя."),
            new SettingRow("Проверка",
                "Прогонит кайму по всем состояниям, не трогая микрофон. Если после этого по краям ничего не появилось — дело в отрисовке, а не в распознавании.",
                test, 230),
            new SettingRow("Показывать свечение", "", _overlayEnabled, 46),
            new SettingRow("Мониторы", "", _monitors),
            new SettingRow("Толщина каймы",
                "В долях меньшей стороны экрана, чтобы одинаково выглядеть на любом разрешении.",
                _thickness, 240),
            new SettingRow("Яркость",
                "Смысл настройки — обозначить себя и не засветить экран.",
                _maxOpacity, 240),
            new SettingRow("Светиться до того, как услышала имя",
                "Выключено — и правильно: кайма должна означать ровно одно, что прозвучало имя. " +
                "Стоит ей начать вспыхивать от любого звука в комнате — от разговора рядом, от видео, " +
                "от кашля, — и она перестаёт что-либо значить.",
                _showBeforeWake, 46),
            new SettingRow("Яркость этого свечения",
                "Действует, только если включено предыдущее. Должна быть заметно ниже обычной: " +
                "если обращались не к ассистенту, вас не должно отвлекать.",
                _sensingOpacity, 240),
            new SettingRow("Отзывчивость на голос",
                "Ноль — ровное дыхание без реакции на речь. Сто — только голос.",
                _reactivity, 240),
            new SectionTitle("Внешний вид"),
            new SettingRow("Цвета выбранной личности",
                "У Хики синие, у Ави оранжевые. Выключите, чтобы задать свои в config.json.",
                _personaColors, 46),
            new SettingRow("Кадров в секунду",
                "Тридцати обычно достаточно, и это экономит батарею ноутбука.",
                _fps, 240),
            new SettingRow("Прятать от записи экрана",
                "Свечение не попадёт в скриншоты и трансляции.",
                _excludeCapture, 46));
    }

    private Control BuildBehaviorPage()
    {
        _armedSeconds = new SliderField { Minimum = 0, Maximum = 20, Step = 1, Format = v => v < 1 ? "выкл." : $"{v:0} с" };
        _searchFallback = new ToggleSwitch();
        _matchThreshold = new SliderField { Minimum = 0.4, Maximum = 0.9, Step = 0.01, Format = v => v.ToString("0.00") };
        _indexApps = new ToggleSwitch();
        _logTranscripts = new ToggleSwitch();
        _autostart = new ToggleSwitch();
        _runAsAdmin = new ToggleSwitch();
        _startMuted = new ToggleSwitch();

        _logLevel = new DropDownField();
        _logLevel.SetItems(new[]
        {
            ("Обычный", "info"),
            ("Подробный", "debug"),
            ("Всё подряд", "trace"),
            ("Только предупреждения", "warn"),
        });

        return Stack(
            new SectionTitle("Поведение"),
            new SettingRow("Ждать команду после имени",
                "Позволяет сказать «Ави», сделать паузу и договорить. Ноль — имя и команда должны быть одной фразой.",
                _armedSeconds, 240),
            new SettingRow("Уверенность при поиске программы",
                "Запускает не то — поднимите. Не находит очевидное — опустите.",
                _matchThreshold, 240),
            new SettingRow("Искать в интернете, если команда не распознана",
                "Молча проглотить команду хуже, чем показать результаты поиска: так хотя бы видно, что вас услышали.",
                _searchFallback, 46),
            new SettingRow("Знать про установленные программы",
                "Обходит меню «Пуск» и список приложений Windows, чтобы открывать голосом всё, что у вас стоит.",
                _indexApps, 46),
            new SectionTitle("Запуск"),
            new SettingRow("Запускать вместе с Windows", "", _autostart, 46),
            new SettingRow("Стартовать с выключенным микрофоном", "", _startMuted, 46),
            new SettingRow("Права администратора",
                "Нужны ровно для одного: управлять окнами программ, запущенных от администратора. " +
                "В остальном только мешают — антивирусы строже, перетаскивание файлов из проводника " +
                "перестаёт работать, при каждом запуске появляется запрос UAC. На доступ к микрофону " +
                "не влияют никак. Применится после перезапуска.",
                _runAsAdmin, 46),
            new SectionTitle("Журнал"),
            new SettingRow("Записывать распознанный текст",
                "Незаменимо при настройке и совершенно не нужно потом. Это ваша речь, которая ложится на диск, — выключите, когда всё заработает.",
                _logTranscripts, 46),
            new SettingRow("Подробность журнала", "", _logLevel));
    }

    // ---- Голос ------------------------------------------------------------

    private Control BuildVoicePage()
    {
        _voiceEnabled = new ToggleSwitch();
        _neuralOnly = new ToggleSwitch();
        _speakFailures = new ToggleSwitch();
        _speakConfirmations = new ToggleSwitch();
        _suppressMic = new ToggleSwitch();

        _voiceEngine = new DropDownField();
        _voiceEngine.SetItems(new[]
        {
            ("Лучший из доступных", "auto"),
            ("Голос из Windows", "windows"),
            ("Нейроголоса Microsoft (через интернет)", "edge"),
            ("Не говорить", "off"),
        });

        _voiceName = new DropDownField();
        _voiceRate = new SliderField { Minimum = 0.7, Maximum = 1.6, Step = 0.05, Format = v => $"{v:0.00}×" };
        _voiceVolume = new SliderField { Minimum = 0.1, Maximum = 1.0, Step = 0.05, Format = v => $"{v * 100:0} %" };

        _voiceStatus = new Label
        {
            AutoSize = false,
            Height = 46,
            ForeColor = Theme.TextFaint,
            Font = Theme.Small,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var test = new FlatButton("Сказать что-нибудь", primary: true) { Width = 210 };
        test.Click += (_, _) =>
        {
            var name = Personas.ById(_personaId).Name;
            _host?.Voice.Say($"Проверка связи. Меня зовут {name}, и вот так я звучу.");
            RefreshVoiceStatus();
        };

        var install = new FlatButton("Как поставить нейроголоса") { Width = 250 };
        install.Click += (_, _) => OpenSpeechSettings();

        return Stack(
            new SectionTitle("Голос",
                "Ответы на вопросы произносятся вслух. Запуск программ — нет: действие видно и так, " +
                "а «открываю Steam» поверх открывающегося Steam только мешает."),
            _voiceStatus,
            new SettingRow("Отвечать голосом", "", _voiceEnabled, 46),
            new SettingRow("Откуда брать голос",
                "«Лучший из доступных» сначала ищет нейроголос, установленный в самой Windows: он звучит " +
                "так же хорошо и ничего никуда не отправляет. Нейроголоса Microsoft через интернет — " +
                "лучшее звучание, но произносимый текст уходит на серверы Microsoft.",
                _voiceEngine),
            new SettingRow("Только нейроголос",
                "Старые голоса Windows склеены из кусочков записи и звучат как компьютер из двухтысячных. " +
                "Включено — не нашлось нейроголоса, промолчу. Молчание честнее: оно хотя бы не раздражает.",
                _neuralOnly, 46),
            new SettingRow("Голос", "Список обновляется при открытии окна.", _voiceName),
            new SettingRow("Проверка", "", test, 210),
            new SettingRow("Нейроголоса в Windows",
                "Их ставят один раз: Параметры → Время и язык → Речь → Управление голосами → Добавить голоса. " +
                "Нужен тот, у кого в названии есть «Natural» — обычные голоса звучат механически.",
                install, 250),
            new SectionTitle("Как говорить"),
            new SettingRow("Скорость", "", _voiceRate, 240),
            new SettingRow("Громкость", "", _voiceVolume, 240),
            new SectionTitle("Когда говорить"),
            new SettingRow("Говорить, если не получилось",
                "Короткое «не нашла такого» вместо молчаливой красной вспышки.",
                _speakFailures, 46),
            new SettingRow("Подтверждать запуск голосом",
                "Обычно не нужно: программа открывается на глазах.",
                _speakConfirmations, 46),
            new SettingRow("Глушить микрофон, пока говорю",
                "Обязательно, если звук идёт из колонок: иначе я услышу собственный голос и отвечу сама себе. " +
                "В наушниках можно выключить.",
                _suppressMic, 46));
    }

    private void RefreshVoices()
    {
        var items = new List<(string, string)> { ("Выбрать лучший сам", "") };

        try
        {
            var voices = _host?.Voice.AvailableVoices ?? Array.Empty<Speech.VoiceInfo>();
            foreach (var voice in voices)
                items.Add((voice.Describe(), voice.Name));
        }
        catch (Exception ex)
        {
            Log.Debug($"список голосов не получен: {ex.Message}", "ui");
        }

        _voiceName.SetItems(items, _config.Voice.Voice ?? "");
    }

    private void RefreshVoiceStatus()
    {
        if (_host is null)
        {
            _voiceStatus.Text = "";
            return;
        }

        var voice = _host.Voice;

        if (!voice.IsReady)
        {
            _voiceStatus.Text = $"Сейчас: {voice.Description}";
            _voiceStatus.ForeColor = Theme.TextFaint;
            return;
        }

        if (voice.SoundsRobotic)
        {
            _voiceStatus.Text = $"Сейчас: {voice.Description}. Это старый механический голос — " +
                                "нейроголосов в системе не нашлось.";
            _voiceStatus.ForeColor = Theme.Warn;
            return;
        }

        _voiceStatus.Text = $"Сейчас: {voice.Description}";
        _voiceStatus.ForeColor = Theme.Good;
    }

    private static void OpenSpeechSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:speech")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("не удалось открыть параметры речи Windows", ex, "ui");
        }
    }

    // ---- Разговор ----------------------------------------------------------

    private Control BuildBrainPage()
    {
        _brainEnabled = new ToggleSwitch();
        _answerUnknown = new ToggleSwitch();
        _shareProfile = new ToggleSwitch();

        _apiKey = new TextField { Secret = true, Placeholder = "sk-ant-..." };
        _brainStyle = new TextField { Placeholder = "например: отвечай сухо и без вежливостей" };

        _brainModel = new DropDownField();
        _brainModel.SetItems(new[]
        {
            ("Claude Opus 5 — самый умный", "claude-opus-5"),
            ("Claude Sonnet 5 — быстрее и дешевле", "claude-sonnet-5"),
            ("Claude Haiku 4.5 — самый быстрый", "claude-haiku-4-5"),
        });

        _brainMaxTokens = new SliderField { Minimum = 150, Maximum = 2000, Step = 50, Format = v => $"{v:0}" };
        _followUpSeconds = new SliderField { Minimum = 0, Maximum = 40, Step = 1, Format = v => v < 1 ? "выкл." : $"{v:0} с" };

        _brainStatus = new Label
        {
            AutoSize = false,
            Height = 46,
            ForeColor = Theme.TextFaint,
            Font = Theme.Small,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var save = new FlatButton("Сохранить ключ", primary: true) { Width = 190 };
        save.Click += (_, _) =>
        {
            var typed = _apiKey.Text.Trim();

            // Точки — это показанный замаскированный ключ, а не новый.
            if (typed.Contains('…')) { SetNotice("Ключ не изменился."); return; }

            if (Brain.ApiKeyStore.Write(typed))
            {
                _apiKey.Text = Brain.ApiKeyStore.Masked();
                SetNotice("Ключ сохранён. Нажмите «Применить», чтобы включить разговор.");
            }
            else
            {
                SetNotice("Ключ сохранить не удалось — подробности в журнале.");
            }

            RefreshBrainStatus();
        };

        var test = new FlatButton("Проверить") { Width = 140 };
        test.Click += (_, _) => TestBrain();

        var forget = new FlatButton("Забыть разговор") { Width = 190 };
        forget.Click += (_, _) =>
        {
            _host?.Brain.Forget();
            SetNotice("Разговор начат заново.");
        };

        return Stack(
            new SectionTitle("Разговор",
                "Всё, что не оказалось командой, можно отдать Claude и услышать ответ вслух. " +
                "Работает через интернет и по вашему ключу — то есть за ваши деньги. " +
                "Запуск программ этого не касается и по-прежнему идёт мгновенно и без сети."),
            _brainStatus,
            new SettingRow("Отвечать на вопросы", "", _brainEnabled, 46),
            new SettingRow("Ключ доступа",
                "Берётся на console.anthropic.com → API Keys. Хранится отдельно от config.json " +
                "и зашифрован средствами Windows: скопированный на другой компьютер файл бесполезен.",
                _apiKey),
            new SettingRow("", "", save, 190),
            new SettingRow("", "", test, 140),
            new SectionTitle("Как отвечать"),
            new SettingRow("Модель", "", _brainModel),
            new SettingRow("Предел длины ответа",
                "В токенах — это примерно полтора знака каждый. Маленький предел здесь благо: " +
                "текст, который приятно читать, невыносимо слушать.",
                _brainMaxTokens, 240),
            new SettingRow("Свой характер",
                "Дописывается к описанию личности. Пусто — как есть.",
                _brainStyle),
            new SectionTitle("Как продолжать"),
            new SettingRow("Слушать продолжение без имени",
                "После ответа можно сразу спросить «а почему?», не называя имени снова. Ради этого разговор и затевался.",
                _followUpSeconds, 240),
            new SettingRow("Отвечать, когда команда не нашлась",
                "Вместо красной вспышки — попытка ответить словами. Явное «запусти» это не затрагивает: " +
                "оно по-прежнему обязано запустить или честно признать неудачу.",
                _answerUnknown, 46),
            new SettingRow("Рассказывать, чем вы пользуетесь",
                "Список часто запускаемых программ уходит вместе с вопросом. Помогает в ответах про ваш же компьютер.",
                _shareProfile, 46),
            new SettingRow("", "", forget, 190));
    }

    private void RefreshBrainStatus()
    {
        if (_host is null) { _brainStatus.Text = ""; return; }

        var brain = _host.Brain;

        if (brain.IsReady)
        {
            _brainStatus.Text = $"Разговор готов: {brain.Description}";
            _brainStatus.ForeColor = Theme.Good;
        }
        else if (!Brain.ApiKeyStore.HasKey)
        {
            _brainStatus.Text = "Ключ не задан — отвечать нечем.";
            _brainStatus.ForeColor = Theme.TextFaint;
        }
        else
        {
            _brainStatus.Text = $"Не подключено: {brain.Description}";
            _brainStatus.ForeColor = Theme.Warn;
        }
    }

    private void TestBrain()
    {
        if (_host is null) return;

        SetNotice("Проверяю ключ…");

        _ = Task.Run(async () =>
        {
            var result = await _host.Brain.TestAsync().ConfigureAwait(false);
            try { BeginInvoke(() => SetNotice($"Проверка: {result}")); } catch { }
        });
    }

    // ---- Обучение ----------------------------------------------------------

    private Control BuildLearningPage()
    {
        _learningEnabled = new ToggleSwitch();
        _keepJournal = new ToggleSwitch();
        _learnWakeVariants = new ToggleSwitch();
        _learnAliases = new ToggleSwitch();

        _maxPromptTerms = new SliderField { Minimum = 0, Maximum = 80, Step = 2, Format = v => v < 1 ? "выкл." : $"{v:0}" };
        _wakeVariantThreshold = new SliderField { Minimum = 2, Maximum = 10, Step = 1, Format = v => $"{v:0} раз" };

        _learningStatus = new Label
        {
            AutoSize = false,
            Height = 64,
            ForeColor = Theme.Text,
            Font = Theme.Small,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var openJournal = new FlatButton("Открыть дневник речи") { Width = 230 };
        openJournal.Click += (_, _) => OpenFolder(AppPaths.Root);

        var rebuild = new FlatButton("Пересобрать из дневника") { Width = 230 };
        rebuild.Click += (_, _) =>
        {
            _host?.Learning?.RebuildFromJournal();
            RefreshLearningStatus();
            SetNotice("Наблюдения пересобраны заново.");
        };

        var forget = new FlatButton("Забыть всё обо мне") { Width = 230 };
        forget.Click += (_, _) =>
        {
            var answer = MessageBox.Show(this,
                "Стереть словарь, выученные синонимы и написания имени?\n\n" +
                "Дневник речи при этом останется — из него всё можно собрать обратно.",
                "Забыть наблюдения", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            _host?.Learning?.Forget();
            RefreshLearningStatus();
            SetNotice("Наблюдения стёрты.");
        };

        return Stack(
            new SectionTitle("Обучение",
                "Скажу прямо, потому что вопрос обычно звучит иначе: дообучить саму модель распознавания " +
                "на домашнем компьютере нельзя — это недели счёта на чужих видеокартах. " +
                "А вот подсказывать ей ваши слова можно, и на слух разница выходит примерно та же."),
            _learningStatus,
            new SettingRow("Наблюдать за речью", "", _learningEnabled, 46),
            new SectionTitle("Что именно запоминается"),
            new SettingRow("Слов в подсказке распознаванию",
                "Ваши частые слова показываются модели перед каждой фразой — так «халдайверс» перестаёт " +
                "превращаться в «хал драйвер». Больше не всегда лучше: длинный список размывает подсказку.",
                _maxPromptTerms, 240),
            new SettingRow("Учить, как вы произносите имя",
                "Если я не откликнулась, а команда следом была осмысленной, я запомню услышанное написание. " +
                "После нескольких повторов начну откликаться на него сразу.",
                _learnWakeVariants, 46),
            new SettingRow("Сколько повторов, чтобы принять написание",
                "Меньше — подстроюсь быстрее, но рискую принять за имя случайное слово.",
                _wakeVariantThreshold, 240),
            new SettingRow("Учить синонимы из ваших исправлений",
                "Команда не вышла, вы сказали иначе и получилось — значит, первое и второе про одно и то же. " +
                "В следующий раз сработает сразу.",
                _learnAliases, 46),
            new SectionTitle("Дневник речи"),
            new SettingRow("Вести дневник",
                "Файл «речь.jsonl» рядом с настройками: по строке на фразу. Из него всегда можно собрать " +
                "наблюдения заново — и в него можно просто заглянуть и посмотреть, что я про вас знаю. " +
                "Это ваша речь, лежащая на диске: выключите, если это лишнее.",
                _keepJournal, 46),
            new SettingRow("", "", openJournal, 230),
            new SettingRow("", "", rebuild, 230),
            new SettingRow("", "", forget, 230));
    }

    private void RefreshLearningStatus()
    {
        _learningStatus.Text = _host?.Learning?.Describe() ?? "";
    }

    private Control BuildAboutPage()
    {
        var openConfig = new FlatButton("Открыть папку с настройками") { Width = 230 };
        openConfig.Click += (_, _) => OpenFolder(AppPaths.Root);

        var openLogs = new FlatButton("Открыть журнал") { Width = 230 };
        openLogs.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);

        var info = new InfoPanel(_statusSource) { Height = 190 };

        return Stack(
            new SectionTitle("О программе", BuildInfo.Describe() + " — голосовое управление Windows. Всё распознавание идёт на этом компьютере."),
            info,
            new SettingRow("", "", openConfig, 230),
            new SettingRow("", "", openLogs, 230));
    }

    private static void OpenFolder(string path)
    {
        try
        {
            AppPaths.EnsureCreated();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"не удалось открыть {path}", ex, "ui");
        }
    }

    // ---- Личность ---------------------------------------------------------

    private void SelectPersona(string id)
    {
        _personaId = id;
        Theme.ApplyPersona(id);

        foreach (var card in _personaCards) card.Selected = card.Persona.Id == id;

        try { Icon = RingLogo.CreateIcon(Theme.Accent, muted: false); } catch { }

        Invalidate(true);
        foreach (Control c in Controls) c.Invalidate(true);

        SetNotice($"Личность: {Personas.ById(id).Name}. Нажмите «Применить», чтобы сохранить.");
    }

    // ---- Загрузка и сохранение -------------------------------------------

    private void LoadFromConfig()
    {
        var c = _config;

        _initialModel = c.Speech.Model ?? "";
        _initialDevice = c.Audio.Device ?? "";
        _initialRunAsAdmin = c.Behavior.RunAsAdmin;

        SelectPersona(Personas.ById(c.Persona).Id);

        _respondToBoth.Checked = c.Wake.RespondToBoth;
        _tolerance.Value = c.Wake.Tolerance;
        _allowAnywhere.Checked = c.Wake.AllowAnywhere;
        _extraVariants.Text = string.Join(", ", c.Wake.ExtraVariants ?? new List<string>());

        _device.Value = c.Audio.Device ?? "";
        _gain.Value = c.Audio.Gain;
        _vadThreshold.Value = c.Audio.VadThreshold;
        _silenceMs.Value = c.Audio.SilenceMs;
        _minSpeechMs.Value = c.Audio.MinSpeechMs;

        _model.Value = c.Speech.Model ?? "small";
        _probeModel.Value = c.Speech.ProbeModel ?? "base";
        _language.Value = c.Speech.Language ?? "ru";
        _threads.Value = c.Speech.Threads;
        _earlyProbe.Checked = c.Speech.EarlyWakeProbe;
        _adaptiveContext.Checked = c.Speech.AdaptiveContext;
        _fastDecoding.Checked = c.Speech.FastDecoding;
        _probeAfterMs.Value = c.Speech.ProbeAfterMs;

        _overlayEnabled.Checked = c.Overlay.Enabled;
        _monitors.Value = c.Overlay.Monitors ?? "primary";
        _thickness.Value = c.Overlay.Thickness;
        _maxOpacity.Value = c.Overlay.MaxOpacity;
        _showBeforeWake.Checked = c.Overlay.ShowBeforeWakeWord;
        _sensingOpacity.Value = c.Overlay.SensingOpacity;
        _reactivity.Value = c.Overlay.VoiceReactivity;
        _fps.Value = c.Overlay.TargetFps;
        _personaColors.Checked = c.Overlay.UsePersonaColors;
        _excludeCapture.Checked = c.Overlay.ExcludeFromCapture;

        _voiceEnabled.Checked = c.Voice.Enabled;
        _voiceEngine.Value = c.Voice.Engine ?? "auto";
        _neuralOnly.Checked = c.Voice.NeuralOnly;
        _voiceRate.Value = c.Voice.Rate;
        _voiceVolume.Value = c.Voice.Volume;
        _speakFailures.Checked = c.Voice.SpeakFailures;
        _speakConfirmations.Checked = c.Voice.SpeakConfirmations;
        _suppressMic.Checked = c.Voice.SuppressMicWhileSpeaking;
        RefreshVoices();
        RefreshVoiceStatus();

        _brainEnabled.Checked = c.Brain.Enabled;
        _brainModel.Value = c.Brain.Model ?? "claude-opus-5";
        _brainMaxTokens.Value = c.Brain.MaxTokens;
        _followUpSeconds.Value = c.Brain.FollowUpSeconds;
        _answerUnknown.Checked = c.Brain.AnswerUnknownCommands;
        _shareProfile.Checked = c.Brain.ShareProfile;
        _brainStyle.Text = c.Brain.Style ?? "";
        _apiKey.Text = Brain.ApiKeyStore.HasKey ? Brain.ApiKeyStore.Masked() : "";
        RefreshBrainStatus();

        _learningEnabled.Checked = c.Learning.Enabled;
        _keepJournal.Checked = c.Learning.KeepJournal;
        _maxPromptTerms.Value = c.Learning.MaxPromptTerms;
        _learnWakeVariants.Checked = c.Learning.LearnWakeVariants;
        _wakeVariantThreshold.Value = c.Learning.WakeVariantThreshold;
        _learnAliases.Checked = c.Learning.LearnAliases;
        RefreshLearningStatus();

        _armedSeconds.Value = c.Behavior.ArmedSeconds;
        _searchFallback.Checked = c.Behavior.WebSearchFallback;
        _matchThreshold.Value = c.Behavior.MatchThreshold;
        _indexApps.Checked = c.Behavior.IndexInstalledApps;
        _logTranscripts.Checked = c.Behavior.LogTranscripts;
        _autostart.Checked = AutostartManager.IsAnyEnabled();
        _runAsAdmin.Checked = c.Behavior.RunAsAdmin;
        _startMuted.Checked = c.Behavior.StartMuted;
        _logLevel.Value = c.Behavior.LogLevel ?? "info";

        SetNotice("");
    }

    private void Apply()
    {
        var c = _store.Current;

        c.Persona = _personaId;

        c.Wake.RespondToBoth = _respondToBoth.Checked;
        c.Wake.Tolerance = _tolerance.Value;
        c.Wake.AllowAnywhere = _allowAnywhere.Checked;
        c.Wake.ExtraVariants = _extraVariants.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Список имён — производное от личности, поэтому переписываем его целиком.
        // Один источник правды: иначе выбор личности и содержимое config.json
        // начнут расходиться, и победит непонятно что.
        c.Wake.Words = Personas.WakeWordsFor(_personaId, _respondToBoth.Checked);

        c.Audio.Device = _device.Value;
        c.Audio.Gain = (float)_gain.Value;
        c.Audio.VadThreshold = (float)_vadThreshold.Value;
        c.Audio.SilenceMs = (int)_silenceMs.Value;
        c.Audio.MinSpeechMs = (int)_minSpeechMs.Value;

        c.Speech.Model = _model.Value;
        c.Speech.ProbeModel = _probeModel.Value;
        c.Speech.Language = _language.Value;
        c.Speech.Threads = (int)_threads.Value;
        c.Speech.EarlyWakeProbe = _earlyProbe.Checked;
        c.Speech.AdaptiveContext = _adaptiveContext.Checked;
        c.Speech.FastDecoding = _fastDecoding.Checked;
        c.Speech.ProbeAfterMs = (int)_probeAfterMs.Value;

        c.Overlay.Enabled = _overlayEnabled.Checked;
        c.Overlay.Monitors = _monitors.Value;
        c.Overlay.Thickness = _thickness.Value;
        c.Overlay.MaxOpacity = _maxOpacity.Value;
        c.Overlay.ShowBeforeWakeWord = _showBeforeWake.Checked;
        c.Overlay.SensingOpacity = _sensingOpacity.Value;
        c.Overlay.VoiceReactivity = _reactivity.Value;
        c.Overlay.TargetFps = (int)_fps.Value;
        c.Overlay.UsePersonaColors = _personaColors.Checked;
        c.Overlay.ExcludeFromCapture = _excludeCapture.Checked;

        c.Voice.Enabled = _voiceEnabled.Checked;
        c.Voice.Engine = _voiceEngine.Value;
        c.Voice.NeuralOnly = _neuralOnly.Checked;
        c.Voice.Voice = _voiceName.Value;
        c.Voice.Rate = _voiceRate.Value;
        c.Voice.Volume = _voiceVolume.Value;
        c.Voice.SpeakFailures = _speakFailures.Checked;
        c.Voice.SpeakConfirmations = _speakConfirmations.Checked;
        c.Voice.SuppressMicWhileSpeaking = _suppressMic.Checked;

        c.Brain.Enabled = _brainEnabled.Checked;
        c.Brain.Model = _brainModel.Value;
        c.Brain.MaxTokens = (int)_brainMaxTokens.Value;
        c.Brain.FollowUpSeconds = (int)_followUpSeconds.Value;
        c.Brain.AnswerUnknownCommands = _answerUnknown.Checked;
        c.Brain.ShareProfile = _shareProfile.Checked;
        c.Brain.Style = _brainStyle.Text.Trim();

        c.Learning.Enabled = _learningEnabled.Checked;
        c.Learning.KeepJournal = _keepJournal.Checked;
        c.Learning.MaxPromptTerms = (int)_maxPromptTerms.Value;
        c.Learning.LearnWakeVariants = _learnWakeVariants.Checked;
        c.Learning.WakeVariantThreshold = (int)_wakeVariantThreshold.Value;
        c.Learning.LearnAliases = _learnAliases.Checked;

        c.Behavior.ArmedSeconds = (int)_armedSeconds.Value;
        c.Behavior.WebSearchFallback = _searchFallback.Checked;
        c.Behavior.MatchThreshold = _matchThreshold.Value;
        c.Behavior.IndexInstalledApps = _indexApps.Checked;
        c.Behavior.LogTranscripts = _logTranscripts.Checked;
        c.Behavior.StartMuted = _startMuted.Checked;
        c.Behavior.LogLevel = _logLevel.Value;
        c.Behavior.Autostart = _autostart.Checked;
        c.Behavior.RunAsAdmin = _runAsAdmin.Checked;

        AutostartManager.Apply(_autostart.Checked, _runAsAdmin.Checked);

        _store.Save();
        _config = c;

        try { _onApply(c); }
        catch (Exception ex) { Log.Error("применение настроек сорвалось", ex, "ui"); }

        // Модель и звуковое устройство подхватываются только при старте:
        // менять их на живом пайплайне — значит рвать поток посреди фразы.
        var needsRestart = _model.Value != _initialModel
                           || _device.Value != _initialDevice
                           || _runAsAdmin.Checked != _initialRunAsAdmin;

        SetNotice(needsRestart
            ? "Сохранено. Модель, микрофон и права применятся после перезапуска HIKA."
            : "Сохранено — всё уже работает.");

        _initialModel = _model.Value;
        _initialDevice = _device.Value;
        _initialRunAsAdmin = _runAsAdmin.Checked;
    }

    private void SetNotice(string text)
    {
        _notice.Text = text;
        _notice.ForeColor = text.StartsWith("Сохранено") ? Theme.Good : Theme.TextFaint;
    }

    // ---- Показ и скрытие --------------------------------------------------

    public void ShowWindow()
    {
        _config = _store.Current;
        LoadFromConfig();
        RefreshDevices();

        Show();

        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

        BringToFront();
        Activate();
    }

    public void HideWindow() => Hide();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Закрытие крестиком прячет окно, а не выгружает программу: она
        // фоновая, и выход из неё живёт в меню значка.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ---- Заголовок --------------------------------------------------------

    /// <summary>Своя полоса заголовка: с логотипом, состоянием и перетаскиванием.</summary>
    private sealed class HeaderBar : Control
    {
        private readonly Form _owner;
        private readonly Func<(string Status, string Microphone, string Recognizer, int CatalogSize)> _status;
        private readonly System.Windows.Forms.Timer _timer;

        private Point _dragStart;
        private bool _dragging;
        private int _hoverButton = -1;

        public HeaderBar(Form owner, Func<(string, string, string, int)> status)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);

            _owner = owner;
            _status = status;
            BackColor = Theme.Background;

            _timer = new System.Windows.Forms.Timer { Interval = 700 };
            _timer.Tick += (_, _) => { if (Visible) Invalidate(); };
            _timer.Start();
        }

        private Rectangle CloseButton => new(Width - 46, 20, 32, 32);
        private Rectangle MinimizeButton => new(Width - 84, 20, 32, 32);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (CloseButton.Contains(e.Location)) { _owner.Hide(); return; }
            if (MinimizeButton.Contains(e.Location)) { _owner.WindowState = FormWindowState.Minimized; return; }

            _dragging = true;
            _dragStart = e.Location;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                _owner.Location = new Point(
                    _owner.Location.X + e.X - _dragStart.X,
                    _owner.Location.Y + e.Y - _dragStart.Y);
            }
            else
            {
                var hover = CloseButton.Contains(e.Location) ? 0
                          : MinimizeButton.Contains(e.Location) ? 1
                          : -1;

                if (hover != _hoverButton) { _hoverButton = hover; Invalidate(); }
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverButton = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            RingLogo.Draw(g, new RectangleF(24, 19, 36, 36), Theme.Accent, glow: 0.55);

            TextRenderer.DrawText(g, "HIKA", Theme.Title, new Rectangle(72, 14, 300, 26),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

            var (status, microphone, _, _) = SafeStatus();

            TextRenderer.DrawText(g, $"{Theme.Current.Name} · {status}", Theme.Small,
                new Rectangle(72, 40, 460, 20), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

            if (!string.IsNullOrEmpty(microphone))
            {
                TextRenderer.DrawText(g, microphone, Theme.Small,
                    new Rectangle(Width - 480, 40, 380, 20), Theme.TextFaint,
                    TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }

            DrawGlyph(g, MinimizeButton, _hoverButton == 1, minimize: true);
            DrawGlyph(g, CloseButton, _hoverButton == 0, minimize: false);

            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        private (string, string, string, int) SafeStatus()
        {
            try { return _status(); }
            catch { return ("—", "", "", 0); }
        }

        private static void DrawGlyph(Graphics g, Rectangle box, bool hover, bool minimize)
        {
            if (hover)
            {
                Theme.FillRounded(g, box, 6,
                    minimize ? Theme.CardHover : Theme.Blend(Theme.Card, Theme.Danger, 0.55));
            }

            using var pen = new Pen(hover ? Theme.Text : Theme.TextDim, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var cx = box.X + box.Width / 2;
            var cy = box.Y + box.Height / 2;

            if (minimize)
            {
                g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
            }
            else
            {
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Сводка состояния на странице «О программе».</summary>
    private sealed class InfoPanel : Panel
    {
        private readonly Func<(string Status, string Microphone, string Recognizer, int CatalogSize)> _status;
        private readonly System.Windows.Forms.Timer _timer;

        public InfoPanel(Func<(string, string, string, int)> status)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);

            _status = status;
            BackColor = Theme.Panel;

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => { if (Visible) Invalidate(); };
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var box = new Rectangle(0, 4, Width - 1, Height - 12);
            Theme.FillRounded(g, box, 12, Theme.Card);
            Theme.DrawRounded(g, box, 12, Theme.Border);

            (string, string)[] rows;

            try
            {
                var (status, microphone, recognizer, catalogSize) = _status();
                rows = new[]
                {
                    ("Состояние", status),
                    ("Микрофон", string.IsNullOrEmpty(microphone) ? "—" : microphone),
                    ("Распознавание", string.IsNullOrEmpty(recognizer) ? "—" : recognizer),
                    ("Знает команд", catalogSize.ToString()),
                    ("Настройки", AppPaths.Root),
                };
            }
            catch
            {
                rows = new[] { ("Состояние", "—") };
            }

            var y = box.Y + 18;
            foreach (var (label, value) in rows)
            {
                TextRenderer.DrawText(g, label, Theme.Small, new Rectangle(box.X + 20, y, 160, 22),
                    Theme.TextFaint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                TextRenderer.DrawText(g, value, Theme.Body, new Rectangle(box.X + 180, y, box.Width - 200, 22),
                    Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);

                y += 30;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
