using System.Drawing;
using System.Windows.Forms;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Ui;

/// <summary>
/// Свои команды: что открывать, когда сказано вот это.
///
/// Существует потому, что встроенный каталог не может знать личных вещей.
/// Он знает браузеры, мессенджеры и полсотни сайтов; он не знает папку
/// с курсовой, ярлык рабочего VPN и ту программу, ради которой всё
/// и затевалось. До сих пор ответ на «а мою добавить?» звучал так: открой
/// %APPDATA%\HIKA\config.json, найди раздел custom, допиши туда объект
/// с полями phrases и target, не забудь запятую. Это не ответ. Человеку,
/// который просит компьютер открывать вещи голосом, предлагать в качестве
/// решения редактирование JSON — значит не понимать, зачем он пришёл.
///
/// Список хранится там же, где и раньше — в config.Custom, — и подхватывается
/// каталогом с повышенным весом: если человек описал команду руками,
/// он имел в виду именно её.
/// </summary>
public sealed class CustomCommands : Panel
{
    private const int RowHeight = 44;
    private const int HeaderHeight = 28;

    private readonly List<Row> _rows = new();
    private readonly FlatButton _add = new("Добавить команду");
    private readonly Label _empty = new();

    public CustomCommands()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        BackColor = Theme.Panel;
        Height = HeaderHeight + RowHeight + 52;

        _empty.AutoSize = false;
        _empty.ForeColor = Theme.TextFaint;
        _empty.Font = Theme.Small;
        _empty.BackColor = Theme.Panel;
        _empty.TextAlign = ContentAlignment.MiddleLeft;
        _empty.Text = "Пока ничего своего нет.";

        _add.Width = 190;
        _add.Height = 34;
        _add.Click += (_, _) => { AddRow(new CustomEntry()); Place(); };

        Controls.Add(_empty);
        Controls.Add(_add);
    }

    /// <summary>
    /// Заполняет список из настроек.
    ///
    /// Недописанные строки при этом переживают перезагрузку. Сохранить их
    /// нельзя — команда без цели никуда не ведёт, — но и стереть нельзя:
    /// человек вписал фразу, нажал «Применить» и увидел бы, как набранное
    /// исчезает. Ровно то, из-за чего перестают доверять окну целиком.
    /// </summary>
    public void Load(IEnumerable<CustomEntry>? entries)
    {
        var unfinished = _rows
            .Where(r => !r.Blank && r.Entry() is null)
            .Select(r => r.Draft())
            .ToList();

        foreach (var row in _rows) row.Detach(this);
        _rows.Clear();

        foreach (var entry in entries ?? Enumerable.Empty<CustomEntry>()) AddRow(entry);
        foreach (var draft in unfinished) AddRow(draft);

        Place();
    }

    /// <summary>
    /// Собирает то, что получилось. Пустые строки выбрасываются молча:
    /// человек нажал «Добавить» и передумал — это не ошибка и повода
    /// ругаться на него не даёт.
    /// </summary>
    public List<CustomEntry> Collect()
    {
        var result = new List<CustomEntry>(_rows.Count);

        foreach (var row in _rows)
        {
            var entry = row.Entry();
            if (entry is not null) result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Заводит новую строку с уже вписанной фразой и ставит на неё курсор.
    ///
    /// Зовётся из списка услышанного: там видно фразу, на которую ничего
    /// не открылось, и научить программу правильнее всего прямо оттуда,
    /// не заставляя человека вспоминать и перепечатывать сказанное.
    /// </summary>
    public void StartWith(string phrase)
    {
        var text = phrase.Trim();

        // Второй щелчок по той же фразе не должен заводить вторую такую же
        // строку: человек нажал ещё раз потому, что не заметил результата,
        // а не потому, что хочет две одинаковые команды.
        var existing = _rows.FirstOrDefault(r => r.Mentions(text));

        if (existing is not null)
        {
            try { existing.FocusTarget(); } catch { }
            return;
        }

        var row = AddRow(new CustomEntry { Phrases = new List<string> { text } });
        Place();

        try { row.FocusTarget(); } catch { }
    }

    private Row AddRow(CustomEntry entry)
    {
        var row = new Row(entry, remove =>
        {
            remove.Detach(this);
            _rows.Remove(remove);
            Place();
        });

        row.Attach(this);
        _rows.Add(row);

        return row;
    }

    private bool _placing;

    private void Place()
    {
        // Place задаёт свою же высоту, а смена высоты вызывает OnResize,
        // который зовёт Place. Без замка это круг.
        if (_placing) return;
        _placing = true;

        try { PlaceRows(); }
        finally { _placing = false; }
    }

    private void PlaceRows()
    {
        _empty.Visible = _rows.Count == 0;

        var y = HeaderHeight;

        foreach (var row in _rows)
        {
            row.Place(y, Width);
            y += RowHeight;
        }

        _empty.SetBounds(0, HeaderHeight, Math.Max(100, Width), _rows.Count == 0 ? 30 : 0);
        if (_rows.Count == 0) y += 30;

        _add.Location = new Point(0, y + 8);

        Height = y + 8 + _add.Height + 6;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Place();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_rows.Count == 0) return;

        // Подписи к столбцам. Без них два одинаковых поля рядом — загадка:
        // в какое из них писать «открой мою папку», а в какое путь.
        TextRenderer.DrawText(e.Graphics, "Что сказать", Theme.Small,
            new Rectangle(0, 6, PhrasesWidth, 18), Theme.TextFaint, TextFormatFlags.Left);

        TextRenderer.DrawText(e.Graphics, "Что открыть", Theme.Small,
            new Rectangle(PhrasesWidth + 8, 6, TargetWidth, 18), Theme.TextFaint, TextFormatFlags.Left);
    }

    private int PhrasesWidth => Math.Max(120, (Width - 96) * 2 / 5);
    private int TargetWidth => Math.Max(140, Width - PhrasesWidth - 96);

    /// <summary>Одна команда: фразы, цель и две кнопки.</summary>
    private sealed class Row
    {
        private readonly TextField _phrases = new() { Placeholder = "открой мою папку, мои файлы" };
        private readonly TextField _target = new() { Placeholder = "путь, ссылка или имя программы" };
        private readonly FlatButton _browse = new("…") { Width = 34, Height = 34 };
        private readonly FlatButton _remove = new("×") { Width = 34, Height = 34 };

        /// <summary>
        /// Запись, с которой строка начиналась.
        ///
        /// Хранится ради полей, которых в окне нет: аргументы командной строки
        /// правятся только руками в config.json, и собрать строку заново
        /// без них значило бы стереть их первым же «Применить» — молча
        /// и у того самого человека, который не поленился их вписать.
        /// </summary>
        private readonly CustomEntry _original;

        public Row(CustomEntry entry, Action<Row> onRemove)
        {
            _original = entry;

            _phrases.Text = string.Join(", ", entry.Phrases ?? new List<string>());
            _target.Text = entry.Target ?? "";

            _browse.Click += (_, _) => Browse();
            _remove.Click += (_, _) => onRemove(this);
        }

        public void Attach(Control parent)
        {
            parent.Controls.Add(_phrases);
            parent.Controls.Add(_target);
            parent.Controls.Add(_browse);
            parent.Controls.Add(_remove);
        }

        public void Detach(Control parent)
        {
            foreach (var control in new Control[] { _phrases, _target, _browse, _remove })
            {
                parent.Controls.Remove(control);
                control.Dispose();
            }
        }

        public void Place(int y, int width)
        {
            var phrases = Math.Max(120, (width - 96) * 2 / 5);
            var target = Math.Max(140, width - phrases - 96);

            _phrases.SetBounds(0, y, phrases, 34);
            _target.SetBounds(phrases + 8, y, target, 34);
            _browse.Location = new Point(phrases + target + 16, y);
            _remove.Location = new Point(phrases + target + 54, y);
        }

        public void FocusTarget()
        {
            _target.Focus();
        }

        /// <summary>Строка, в которой ничего не набрано.</summary>
        public bool Blank => _phrases.Text.Trim().Length == 0 && _target.Text.Trim().Length == 0;

        /// <summary>Набранное как есть, даже если этого мало для команды.</summary>
        public CustomEntry Draft() => new()
        {
            Phrases = _phrases.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            Target = _target.Text.Trim(),
            Arguments = _original.Arguments ?? "",
        };

        /// <summary>Есть ли среди фраз ровно такая.</summary>
        public bool Mentions(string phrase) => _phrases.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => p.Equals(phrase, StringComparison.OrdinalIgnoreCase));

        /// <summary>Строка как запись настроек. Null — заполнено не до конца.</summary>
        public CustomEntry? Entry()
        {
            var phrases = _phrases.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var target = _target.Text.Trim();

            if (phrases.Count == 0 || target.Length == 0) return null;

            return new CustomEntry
            {
                Phrases = phrases,
                Target = target,
                Arguments = _original.Arguments ?? "",
                Unknown = _original.Unknown,
            };
        }

        private void Browse()
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Что открывать по этой команде",
                    Filter = "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd;*.url|Все файлы|*.*",
                    CheckFileExists = true,
                };

                if (dialog.ShowDialog() != DialogResult.OK) return;

                _target.Text = dialog.FileName;

                // Фразу подсказываем по имени файла, но только если её ещё нет:
                // переписать то, что человек уже вписал, было бы наглостью.
                if (_phrases.Text.Trim().Length == 0)
                {
                    var name = Path.GetFileNameWithoutExtension(dialog.FileName);
                    if (name.Length > 0) _phrases.Text = "открой " + name.ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Log.Error("выбор файла не открылся", ex, "ui");
            }
        }
    }
}
