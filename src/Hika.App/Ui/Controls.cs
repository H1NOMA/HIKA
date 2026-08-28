using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hika.Config;

namespace Hika.Ui;

/// <summary>
/// Набор элементов интерфейса, нарисованных вручную.
///
/// Стандартные элементы WinForms берут цвета у системной темы и в тёмном
/// окне выглядят чужеродно — особенно списки и ползунки, которые вообще
/// не поддаются перекраске. Проще нарисовать нужное, чем воевать с тем,
/// что рисоваться не хочет.
///
/// Свойства помечены как несериализуемые: конструктор форм здесь не
/// используется вовсе, интерфейс собирается кодом. Без пометки анализатор
/// WinForms справедливо требует объяснить, как их сохранять в .designer.cs,
/// которого у нас нет.
/// </summary>

// ---- Тёмное меню ----------------------------------------------------------

public sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Card;
    public override Color ImageMarginGradientBegin => Theme.Card;
    public override Color ImageMarginGradientMiddle => Theme.Card;
    public override Color ImageMarginGradientEnd => Theme.Card;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuItemSelected => Theme.CardHover;
    public override Color MenuItemSelectedGradientBegin => Theme.CardHover;
    public override Color MenuItemSelectedGradientEnd => Theme.CardHover;
    public override Color MenuItemPressedGradientBegin => Theme.CardHover;
    public override Color MenuItemPressedGradientEnd => Theme.CardHover;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}

public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == true ? Theme.Text : Theme.TextFaint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextDim;
        base.OnRenderArrow(e);
    }
}

// ---- Переключатель --------------------------------------------------------

public sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hover;

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        Size = new Size(46, 26);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, (Height - 24) / 2, 44, 24);
        var fill = _checked
            ? (_hover ? Theme.Blend(Theme.Accent, Color.White, 0.12) : Theme.Accent)
            : (_hover ? Theme.CardHover : Theme.Border);

        Theme.FillRounded(g, track, 12, fill);

        var knobSize = 18;
        var knobX = _checked ? track.Right - knobSize - 3 : track.X + 3;

        using var knob = new SolidBrush(_checked ? Color.White : Theme.Blend(Theme.TextDim, Color.White, 0.3));
        g.FillEllipse(knob, knobX, track.Y + 3, knobSize, knobSize);
    }
}

// ---- Ползунок -------------------------------------------------------------

public sealed class SliderField : Control
{
    private double _value;
    private bool _dragging;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Minimum { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Maximum { get; set; } = 1;

    /// <summary>Шаг округления. 0 — без округления.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Step { get; set; }

    /// <summary>Как показать число рядом с ползунком.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<double, string> Format { get; set; } = v => v.ToString("0.00");

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            // Значение из настроек, не попавшее в диапазон ползунка, означает,
            // что неверен диапазон, а не значение. Подтянуть его к границе
            // молча — худшее из возможного: человек открыл окно посмотреть,
            // нажал «Применить», и настройка изменилась сама, без его участия
            // и без единого следа. Такое ищется потом сутками.
            //
            // Значения приходят уже проверенными на разумность (ConfigStore),
            // поэтому раздвинуть границы здесь безопаснее, чем соврать.
            if (value < Minimum) Minimum = value;
            if (value > Maximum) Maximum = value;

            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (Step > 0) clamped = Math.Round(clamped / Step) * Step;
            if (Math.Abs(_value - clamped) < 1e-9) return;

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SliderField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        Size = new Size(240, 30);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    private const int LabelWidth = 62;
    private Rectangle TrackArea => new(0, Height / 2 - 3, Math.Max(10, Width - LabelWidth), 6);

    protected override void OnMouseDown(MouseEventArgs e) { _dragging = true; SetFromMouse(e.X); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromMouse(e.X); base.OnMouseMove(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var step = Step > 0 ? Step : (Maximum - Minimum) / 40;
        Value += Math.Sign(e.Delta) * step;
        base.OnMouseWheel(e);
    }

    private void SetFromMouse(int x)
    {
        var track = TrackArea;
        var fraction = Math.Clamp((x - track.X) / (double)Math.Max(1, track.Width), 0, 1);
        Value = Minimum + fraction * (Maximum - Minimum);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = TrackArea;
        Theme.FillRounded(g, track, 3, Theme.Border);

        var fraction = (Maximum - Minimum) < 1e-9 ? 0 : (_value - Minimum) / (Maximum - Minimum);
        var filledWidth = (int)(track.Width * fraction);

        if (filledWidth > 0)
            Theme.FillRounded(g, new Rectangle(track.X, track.Y, filledWidth, track.Height), 3, Theme.Accent);

        var knobX = track.X + filledWidth;
        using (var brush = new SolidBrush(Color.White))
            g.FillEllipse(brush, knobX - 7, track.Y + track.Height / 2 - 7, 14, 14);

        using (var pen = new Pen(Theme.Accent, 2))
            g.DrawEllipse(pen, knobX - 7, track.Y + track.Height / 2 - 7, 14, 14);

        TextRenderer.DrawText(g, Format(_value), Theme.Body,
            new Rectangle(Width - LabelWidth + 8, 0, LabelWidth - 8, Height),
            Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

// ---- Выпадающий список ----------------------------------------------------

public sealed class DropDownField : Control
{
    private readonly List<(string Text, string Value)> _items = new();
    private string _value = "";
    private bool _hover;

    public event EventHandler? SelectionChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value ?? "";
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string DisplayText =>
        _items.FirstOrDefault(i => i.Value == _value).Text ?? (_items.Count == 0 ? "—" : _value);

    public DropDownField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        Size = new Size(260, 34);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    public void SetItems(IEnumerable<(string Text, string Value)> items, string? select = null)
    {
        _items.Clear();
        _items.AddRange(items);

        if (select is not null) _value = select;
        else if (_items.Count > 0 && !_items.Any(i => i.Value == _value)) _value = _items[0].Value;

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_items.Count == 0) return;

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowImageMargin = false,
        };

        foreach (var (text, value) in _items)
        {
            var item = new ToolStripMenuItem(text) { Checked = value == _value, CheckOnClick = false };
            var captured = value;
            item.Click += (_, _) => Value = captured;
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, box, 8, _hover ? Theme.CardHover : Theme.Card);
        Theme.DrawRounded(g, box, 8, _hover ? Theme.Accent : Theme.Border);

        TextRenderer.DrawText(g, DisplayText, Theme.Body,
            new Rectangle(12, 0, Width - 40, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        // Стрелка вниз
        var cx = Width - 18;
        var cy = Height / 2;
        using var pen = new Pen(Theme.TextDim, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
    }
}

// ---- Поле ввода -----------------------------------------------------------

public sealed class TextField : Control
{
    private readonly TextBox _box;
    private bool _focused;

    public event EventHandler? TextEdited;

    [AllowNull]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string Text
    {
        get => _box.Text;
        set { var text = value ?? ""; if (_box.Text != text) _box.Text = text; }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get => _box.PlaceholderText; set => _box.PlaceholderText = value; }

    /// <summary>Скрывать содержимое точками — для ключей и паролей.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Secret { get => _box.UseSystemPasswordChar; set => _box.UseSystemPasswordChar = value; }

    public TextField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        Size = new Size(260, 34);
        BackColor = Theme.Card;

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Body,
        };

        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _box.TextChanged += (_, _) => TextEdited?.Invoke(this, EventArgs.Empty);

        Controls.Add(_box);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _box.SetBounds(12, (Height - _box.PreferredHeight) / 2, Math.Max(20, Width - 24), _box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);

        var box = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, box, 8, Theme.Card);
        Theme.DrawRounded(g, box, 8, _focused ? Theme.Accent : Theme.Border);
    }
}

// ---- Поле для сочетания клавиш --------------------------------------------

/// <summary>
/// Сочетание клавиш задаётся нажатием, а не набором текста.
///
/// Написать «Ctrl+Alt+Пробел» руками человек, конечно, может — и ошибётся
/// в названии клавиши, и не узнает об этом до того момента, когда нажатие
/// не сработает. Нажать то, что хочешь назначить, короче и не оставляет
/// места для ошибки.
/// </summary>
public sealed class HotkeyField : Control
{
    private bool _capturing;
    private bool _hover;
    private string _combination = "";

    public event EventHandler? CombinationChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Combination
    {
        get => _combination;
        set
        {
            var text = value?.Trim() ?? "";
            if (_combination == text) return;

            _combination = text;
            Invalidate();
            CombinationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public HotkeyField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                 | ControlStyles.Selectable, true);

        TabStop = true;
        Size = new Size(260, 34);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        _capturing = true;
        Invalidate();
        base.OnClick(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _capturing = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    /// <summary>
    /// Клавиши перехватываются здесь, а не в KeyDown, потому что часть из них
    /// до KeyDown не доходит вовсе: Alt, Tab и Escape окно разбирает само,
    /// а назначить их человек вправе.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;

        // Один модификатор — ещё не сочетание, ждём основную клавишу.
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
                or Keys.LWin or Keys.RWin or Keys.None) return true;

        if (key == Keys.Escape)
        {
            _capturing = false;
            Invalidate();
            return true;
        }

        if (key is Keys.Back or Keys.Delete)
        {
            _capturing = false;
            Combination = "";
            Invalidate();
            return true;
        }

        var text = Describe(keyData);
        if (text is null)
        {
            // Голая буква в глобальные не годится: назначить её значит отнять
            // у всей системы. Показываем, что не приняли, и ждём дальше.
            System.Media.SystemSounds.Beep.Play();
            return true;
        }

        _capturing = false;
        Combination = text;
        Invalidate();
        return true;
    }

    /// <summary>Собирает запись сочетания. Null — такое назначать нельзя.</summary>
    private static string? Describe(Keys keyData)
    {
        var parts = new List<string>(4);

        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");

        parts.Add(KeyName(keyData & Keys.KeyCode));

        var text = string.Join("+", parts);

        // Проверяем тем же разбором, каким её потом будет назначать система:
        // если он этого не понимает, показывать человеку такое сочетание
        // нельзя — оно всё равно не сработает.
        return Hotkey.IsValid(text) ? text : null;
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Prior => "PageUp",
        Keys.Next => "PageDown",
        Keys.Oemtilde => "Tilde",

        // Знаки основного ряда. Раньше «+» и «−» записывались как Plus
        // и Minus, а этими словами система называет клавиши цифрового блока.
        // Человек нажимал знак в верхнем ряду, видел в поле «Ctrl+Alt+Plus»
        // и потом безуспешно жал то же самое: назначено-то было другое.
        Keys.Oemplus => "Плюс",
        Keys.OemMinus => "Минус",
        Keys.Oemcomma => "Запятая",
        Keys.OemPeriod => "Точка",
        Keys.OemQuestion => "Слэш",
        Keys.OemSemicolon => "ТочкаЗапятая",
        Keys.OemOpenBrackets => "СкобкаЛевая",
        Keys.OemCloseBrackets => "СкобкаПравая",
        Keys.OemPipe => "ОбратныйСлэш",
        Keys.OemQuotes => "Кавычка",

        _ => key.ToString(),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, box, 8, _hover || _capturing ? Theme.CardHover : Theme.Card);
        Theme.DrawRounded(g, box, 8, _capturing || _hover ? Theme.Accent : Theme.Border);

        var (text, color) = _capturing
            ? ("Нажмите сочетание…", Theme.Accent)
            : _combination.Length > 0
                ? (_combination, Theme.Text)
                : ("не назначено", Theme.TextFaint);

        TextRenderer.DrawText(g, text, Theme.Body,
            new Rectangle(12, 0, Width - 24, Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

// ---- Кнопка ---------------------------------------------------------------

public sealed class FlatButton : Control
{
    private bool _hover;
    private bool _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    public FlatButton(string text, bool primary = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        Text = text;
        Primary = primary;
        Size = new Size(140, 36);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Primary)
        {
            var fill = _pressed ? Theme.Blend(Theme.Accent, Color.Black, 0.18)
                     : _hover ? Theme.Blend(Theme.Accent, Color.White, 0.12)
                     : Theme.Accent;

            Theme.FillRounded(g, box, 8, fill);
            TextRenderer.DrawText(g, Text, Theme.BodyBold, box, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            if (_hover) Theme.FillRounded(g, box, 8, Theme.CardHover);
            Theme.DrawRounded(g, box, 8, _hover ? Theme.Accent : Theme.Border);
            TextRenderer.DrawText(g, Text, Theme.Body, box, _hover ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

// ---- Карточка личности ----------------------------------------------------

public sealed class PersonaCard : Control
{
    private bool _hover;

    public Persona Persona { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public event EventHandler? Picked;

    public PersonaCard(Persona persona)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        Persona = persona;
        Size = new Size(190, 132);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Picked?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var accent = Theme.AccentOf(Persona);
        var box = new Rectangle(0, 0, Width - 1, Height - 1);

        var fill = Selected ? Theme.Blend(Theme.Card, accent, 0.10)
                 : _hover ? Theme.CardHover
                 : Theme.Card;

        Theme.FillRounded(g, box, 12, fill);
        Theme.DrawRounded(g, box, 12, Selected ? accent : Theme.Border, Selected ? 2f : 1f);

        RingLogo.Draw(g, new RectangleF(Width / 2f - 26, 16, 52, 52), accent,
            glow: Selected ? 0.85 : (_hover ? 0.35 : 0));

        TextRenderer.DrawText(g, Persona.Name, Theme.Section,
            new Rectangle(0, 74, Width, 24), Selected ? Theme.Text : Theme.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var hint = "«" + string.Join("», «", Persona.Words.Select(Capitalize)) + "»";
        TextRenderer.DrawText(g, hint, Theme.Small,
            new Rectangle(6, 98, Width - 12, 20), Theme.TextFaint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

// ---- Строка настройки -----------------------------------------------------

/// <summary>
/// Название, пояснение и сам элемент управления справа.
///
/// Пояснение есть почти у каждой настройки намеренно: значение вроде
/// «порог 0.45» ничего не говорит человеку, который видит его впервые,
/// а отправлять за смыслом в документацию — способ гарантировать,
/// что настройку не тронут никогда.
/// </summary>
public sealed class SettingRow : Panel
{
    private readonly string _title;
    private readonly string _description;
    private readonly Control _editor;

    public SettingRow(string title, string description, Control editor, int editorWidth = 260)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        _title = title;
        _description = description;
        _editor = editor;

        BackColor = Theme.Panel;
        Height = string.IsNullOrEmpty(description) ? 54 : 66;
        Padding = new Padding(0);

        editor.Width = editorWidth;
        Controls.Add(editor);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _editor.Location = new Point(Math.Max(200, Width - _editor.Width - 4), (Height - _editor.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var textWidth = Math.Max(120, Width - _editor.Width - 28);
        var hasDescription = !string.IsNullOrEmpty(_description);

        TextRenderer.DrawText(g, _title, Theme.Body,
            new Rectangle(0, hasDescription ? 12 : 0, textWidth, hasDescription ? 20 : Height),
            Theme.Text, TextFormatFlags.Left | (hasDescription ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter));

        if (hasDescription)
        {
            TextRenderer.DrawText(g, _description, Theme.Small,
                new Rectangle(0, 33, textWidth, 34), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordEllipsis);
        }

        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Заголовок раздела внутри страницы.</summary>
public sealed class SectionTitle : Panel
{
    private readonly string _text;
    private readonly string _hint;

    public SectionTitle(string text, string hint = "")
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        _text = text;
        _hint = hint;
        BackColor = Theme.Panel;
        Height = string.IsNullOrEmpty(hint) ? 46 : 64;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        TextRenderer.DrawText(g, _text, Theme.Section, new Rectangle(0, 12, Width, 24),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

        if (!string.IsNullOrEmpty(_hint))
        {
            TextRenderer.DrawText(g, _hint, Theme.Small, new Rectangle(0, 38, Width, 22),
                Theme.TextFaint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordEllipsis);
        }
    }
}

// ---- Боковая навигация ----------------------------------------------------

public sealed class NavList : Control
{
    private readonly List<(string Key, string Label)> _items = new();
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectionChanged;

    public string SelectedKey => _items.Count == 0 ? "" : _items[Math.Clamp(_selected, 0, _items.Count - 1)].Key;

    public NavList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        BackColor = Theme.Background;
        Cursor = Cursors.Hand;
    }

    private const int ItemHeight = 42;

    public void SetItems(IEnumerable<(string Key, string Label)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Invalidate();
    }

    /// <summary>Выбрать раздел не мышью, а из кода.</summary>
    public void Select(string key)
    {
        var index = _items.FindIndex(i => i.Key == key);
        if (index < 0 || index == _selected) return;

        _selected = index;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = e.Y / ItemHeight;
        var hover = index >= 0 && index < _items.Count ? index : -1;

        if (hover != _hover) { _hover = hover; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var index = e.Y / ItemHeight;
        if (index >= 0 && index < _items.Count && index != _selected)
        {
            _selected = index;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        for (int i = 0; i < _items.Count; i++)
        {
            var row = new Rectangle(8, i * ItemHeight + 3, Width - 16, ItemHeight - 6);
            var selected = i == _selected;

            if (selected) Theme.FillRounded(g, row, 8, Theme.Blend(Theme.Card, Theme.Accent, 0.14));
            else if (i == _hover) Theme.FillRounded(g, row, 8, Theme.Card);

            if (selected)
            {
                // Тонкая полоска у левого края — отметка текущего раздела.
                Theme.FillRounded(g, new Rectangle(row.X + 2, row.Y + 8, 3, row.Height - 16), 2, Theme.Accent);
            }

            TextRenderer.DrawText(g, _items[i].Label, selected ? Theme.BodyBold : Theme.Body,
                new Rectangle(row.X + 16, row.Y, row.Width - 20, row.Height),
                selected ? Theme.Text : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
