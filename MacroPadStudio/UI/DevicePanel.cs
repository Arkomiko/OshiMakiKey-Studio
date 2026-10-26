using System.ComponentModel;
using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Рисует устройство (6 клавиш + ручка) и список всех действий; по щелчку выбирает, что настраивать.</summary>
internal sealed class DevicePanel : Control
{
    static readonly ControlId[] Order = Enum.GetValues<ControlId>();

    const int KeyColumns = 3;
    const int KeyRows = 2;
    const int KeyCount = KeyColumns * KeyRows;

    const TextFormatFlags SingleLine = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    readonly Dictionary<ControlId, RectangleF> _keys = new();
    readonly Dictionary<ControlId, RectangleF> _rows = new();
    RectangleF _pad, _knob;
    float _listTitleY;
    ControlId _selected = ControlId.Key1;
    ControlId? _hover;

    public event EventHandler<ControlId>? SelectionChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<ControlId, PadAction>? ActionProvider { get; set; }

    /// <summary>True — действие выполняет фоновый агент (рисуется значок ⚙).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<PadAction, bool>? NeedsAgent { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ControlId Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    public DevicePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
        Font = Theme.Body;
        TabStop = true;
    }

    void ComputeLayout()
    {
        float s = Theme.Scale;
        float margin = 22 * s;
        float width = Math.Max(10, ClientSize.Width - 2 * margin);

        // Клавиши сеткой 3×2 слева, ручка — по центру оставшегося места справа.
        float inner = 18 * s, gap = 10 * s, knob = 92 * s;
        float keysWidth = width - 2 * inner - knob - 24 * s;
        float key = Math.Max(10, Math.Min((keysWidth - (KeyColumns - 1) * gap) / KeyColumns, 60 * s));
        float gridWidth = KeyColumns * key + (KeyColumns - 1) * gap;
        float gridHeight = KeyRows * key + (KeyRows - 1) * gap;
        float padHeight = Math.Max(knob, gridHeight) + 2 * inner;
        _pad = new RectangleF(margin, margin + 30 * s, width, padHeight);

        float gridY = _pad.Y + (padHeight - gridHeight) / 2;
        for (int i = 0; i < KeyCount; i++)
        {
            int row = i / KeyColumns, column = i % KeyColumns;
            _keys[ControlId.Key1 + i] = new RectangleF(_pad.X + inner + column * (key + gap), gridY + row * (key + gap), key, key);
        }
        float gridRight = _pad.X + inner + gridWidth;
        float knobCenterX = Math.Max(gridRight + 12 * s + knob / 2, (gridRight + _pad.Right - inner) / 2);
        _knob = new RectangleF(knobCenterX - knob / 2, _pad.Y + (padHeight - knob) / 2, knob, knob);

        _listTitleY = _pad.Bottom + 24 * s;
        float y = _listTitleY + 28 * s;
        // Мало места (низкое окно, уведомления сверху) — строки ужимаются до однострочных.
        float rowHeight = Math.Clamp((ClientSize.Height - y - 10 * s) / Order.Length, 24 * s, 46 * s);
        foreach (var id in Order)
        {
            _rows[id] = new RectangleF(margin, y, width, rowHeight - 4 * s);
            y += rowHeight;
        }
    }

    ControlId? HitTest(Point point)
    {
        ComputeLayout();
        foreach (var (id, rect) in _keys)
            if (rect.Contains(point)) return id;
        foreach (var (id, rect) in _rows)
            if (rect.Contains(point)) return id;

        float radius = _knob.Width / 2;
        float dx = point.X - (_knob.X + radius), dy = point.Y - (_knob.Y + radius);
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= radius * 0.45) return ControlId.KnobPress;
        if (distance <= radius + 8 * Theme.Scale) return dx < 0 ? ControlId.KnobLeft : ControlId.KnobRight;
        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ComputeLayout();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Theme.Scale;

        TextRenderer.DrawText(g, L.T("pad.title", "Ваше устройство"), Theme.Subtitle, new Point((int)_pad.X, (int)(20 * s)), Theme.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, L.T("pad.hint", "щёлкните по клавише или ручке"), Theme.Small,
            Rectangle.Round(new RectangleF(_pad.X, 20 * s, _pad.Width, 24 * s)), Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | SingleLine);

        if (_pad.Height > 1 && _pad.Width > 1)
        {
            using (var shadow = Theme.Rounded(new RectangleF(_pad.X, _pad.Y + 4 * s, _pad.Width, _pad.Height), 16 * s))
            using (var brush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillPath(brush, shadow);
            using (var body = Theme.Rounded(_pad, 16 * s))
            using (var brush = new LinearGradientBrush(_pad, Theme.PadTop, Theme.PadBottom, 90f))
                g.FillPath(brush, body);
        }

        for (int i = 0; i < KeyCount; i++)
        {
            var id = ControlId.Key1 + i;
            DrawKey(g, id, _keys[id], i + 1);
        }
        DrawKnob(g);

        TextRenderer.DrawText(g, L.T("pad.list", "Все кнопки и ручка"), Theme.Subtitle, new Point((int)_pad.X, (int)_listTitleY), Theme.Text, TextFormatFlags.NoPrefix);
        foreach (var id in Order) DrawRow(g, id, _rows[id]);

        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Round(RectangleF.Inflate(_rows[_selected], -1, -1)));
    }

    void DrawKey(Graphics g, ControlId id, RectangleF rect, int number)
    {
        float s = Theme.Scale;
        bool selected = id == _selected, hover = id == _hover;

        using (var shadow = Theme.Rounded(new RectangleF(rect.X, rect.Y + 3 * s, rect.Width, rect.Height), 9 * s))
        using (var brush = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillPath(brush, shadow);

        using var cap = Theme.Rounded(rect, 9 * s);
        var top = selected ? Theme.KeySelectedTop : hover ? Theme.KeyHover : Theme.KeyTop;
        var bottom = selected ? Theme.KeySelectedBottom : Theme.KeyBottom;
        using (var brush = new LinearGradientBrush(rect, top, bottom, 90f)) g.FillPath(brush, cap);
        if (selected)
            using (var pen = new Pen(Theme.Accent, 3 * s)) g.DrawPath(pen, cap);

        var numberRect = new RectangleF(rect.X, rect.Y + 2 * s, rect.Width, rect.Height * 0.58f);
        TextRenderer.DrawText(g, number.ToString(), Theme.KeyNumber, Rectangle.Round(numberRect),
            selected ? Theme.KeySelectedText : Theme.KeyText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);

        var action = ActionProvider?.Invoke(id);
        string label = action == null ? "" : BindingText.Compact(action);
        var labelRect = new RectangleF(rect.X + 4 * s, rect.Y + rect.Height * 0.6f, rect.Width - 8 * s, rect.Height * 0.34f);
        TextRenderer.DrawText(g, label, Theme.Tiny, Rectangle.Round(labelRect),
            selected ? Theme.KeySelectedText : Theme.KeyMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | SingleLine);

        if (action != null && NeedsAgent?.Invoke(action) == true)
        {
            var gearRect = new RectangleF(rect.Right - 17 * s, rect.Y + 2 * s, 15 * s, 15 * s);
            TextRenderer.DrawText(g, "⚙", Theme.SymbolSmall, Rectangle.Round(gearRect), Theme.Warning,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    void DrawKnob(Graphics g)
    {
        float s = Theme.Scale;
        var rect = _knob;
        if (rect.Width < 2) return;
        float radius = rect.Width / 2;
        var center = new PointF(rect.X + radius, rect.Y + radius);

        using (var brush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            g.FillEllipse(brush, rect.X, rect.Y + 3 * s, rect.Width, rect.Height);
        using (var brush = new LinearGradientBrush(rect, Theme.KnobTop, Theme.KnobBottom, 90f))
            g.FillEllipse(brush, rect);

        // Насечки по краю ручки.
        using (var tick = new Pen(Color.FromArgb(55, Theme.KnobGlyph), 1.2f * s))
        {
            for (int i = 0; i < 40; i++)
            {
                double angle = i * Math.PI * 2 / 40;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                g.DrawLine(tick, center.X + cos * radius * 0.86f, center.Y + sin * radius * 0.86f,
                    center.X + cos * radius * 0.97f, center.Y + sin * radius * 0.97f);
            }
        }

        DrawKnobArc(g, ControlId.KnobLeft, 112, 136);
        DrawKnobArc(g, ControlId.KnobRight, -68, 136);

        float glyphOffset = radius * 0.7f, glyphSize = radius * 0.6f;
        DrawGlyph(g, "↺", new RectangleF(center.X - glyphOffset - glyphSize / 2, center.Y - glyphSize / 2, glyphSize, glyphSize), ControlId.KnobLeft);
        DrawGlyph(g, "↻", new RectangleF(center.X + glyphOffset - glyphSize / 2, center.Y - glyphSize / 2, glyphSize, glyphSize), ControlId.KnobRight);

        bool selected = _selected == ControlId.KnobPress, hover = _hover == ControlId.KnobPress;
        float capRadius = radius * 0.45f;
        var capRect = new RectangleF(center.X - capRadius, center.Y - capRadius, capRadius * 2, capRadius * 2);
        var top = selected ? Theme.KnobGlow : hover ? Theme.KnobCapHover : Theme.KnobCap;
        var bottom = selected ? Theme.Accent : Theme.KnobCapBottom;
        using (var brush = new LinearGradientBrush(capRect, top, bottom, 90f)) g.FillEllipse(brush, capRect);
        using (var pen = new Pen(selected ? Theme.OnAccent : Color.FromArgb(70, 0, 0, 0), (selected ? 2.5f : 1f) * s))
            g.DrawEllipse(pen, capRect);
    }

    void DrawKnobArc(Graphics g, ControlId id, float start, float sweep)
    {
        bool selected = _selected == id, hover = _hover == id;
        if (!selected && !hover) return;
        float s = Theme.Scale;
        var arcRect = RectangleF.Inflate(_knob, -4 * s, -4 * s);
        using var pen = new Pen(selected ? Theme.KnobGlow : Color.FromArgb(140, Theme.KnobGlow), 7 * s)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(pen, arcRect, start, sweep);
    }

    void DrawGlyph(Graphics g, string glyph, RectangleF rect, ControlId id)
    {
        var color = _selected == id ? Theme.KnobGlyph : Color.FromArgb(_hover == id ? 230 : 170, Theme.KnobGlyph);
        TextRenderer.DrawText(g, glyph, Theme.Symbol, Rectangle.Round(RectangleF.Inflate(rect, 6, 6)), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    void DrawRow(Graphics g, ControlId id, RectangleF rect)
    {
        float s = Theme.Scale;
        bool selected = id == _selected, hover = id == _hover;

        if (selected || hover)
        {
            using var path = Theme.Rounded(rect, 8 * s);
            using var brush = new SolidBrush(selected ? Theme.AccentSoft : Theme.Hover);
            g.FillPath(brush, path);
        }
        if (selected)
        {
            using var bar = Theme.Rounded(new RectangleF(rect.X, rect.Y + 7 * s, 4 * s, rect.Height - 14 * s), 2 * s);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillPath(brush, bar);
        }

        float badgeSize = Math.Min(28 * s, rect.Height - 6 * s);
        var badge = new RectangleF(rect.X + 14 * s, rect.Y + (rect.Height - badgeSize) / 2, badgeSize, badgeSize);
        using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Badge))
            g.FillEllipse(brush, badge);
        TextRenderer.DrawText(g, UiText.Glyph(id), UiText.IsKey(id) ? Theme.Strong : Theme.SymbolSmall, Rectangle.Round(badge),
            selected ? Theme.OnAccent : Theme.BadgeText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        float textX = badge.Right + 12 * s;
        float textWidth = rect.Right - textX - 10 * s;
        var action = ActionProvider?.Invoke(id);
        string summary = action == null ? "" : BindingText.Summary(action);
        if (action != null && NeedsAgent?.Invoke(action) == true) summary = "⚙ " + summary;
        var summaryColor = action?.Kind == ActionKind.None ? Theme.Faint : Theme.Muted;

        if (rect.Height >= 38 * s)
        {
            // Просторно: название сверху, действие под ним.
            TextRenderer.DrawText(g, UiText.Title(id), Theme.Strong,
                Rectangle.Round(new RectangleF(textX, rect.Y, textWidth, rect.Height * 0.52f)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Bottom | SingleLine);
            TextRenderer.DrawText(g, summary, Theme.Small,
                Rectangle.Round(new RectangleF(textX, rect.Y + rect.Height * 0.52f, textWidth, rect.Height * 0.46f)),
                summaryColor, TextFormatFlags.Left | TextFormatFlags.Top | SingleLine);
        }
        else
        {
            // Тесно: название и действие в одну строку.
            float titleWidth = Math.Min(textWidth * 0.48f, 170 * s);
            TextRenderer.DrawText(g, UiText.Title(id), Theme.Strong,
                Rectangle.Round(new RectangleF(textX, rect.Y, titleWidth, rect.Height)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | SingleLine);
            TextRenderer.DrawText(g, summary, Theme.Small,
                Rectangle.Round(new RectangleF(textX + titleWidth + 8 * s, rect.Y, textWidth - titleWidth - 8 * s, rect.Height)),
                summaryColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (HitTest(e.Location) is ControlId id) Choose(id);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int index = Array.IndexOf(Order, _selected);
        if (e.KeyCode is Keys.Down or Keys.Right) Choose(Order[Math.Min(Order.Length - 1, index + 1)]);
        else if (e.KeyCode is Keys.Up or Keys.Left) Choose(Order[Math.Max(0, index - 1)]);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    void Choose(ControlId id)
    {
        Selected = id;
        SelectionChanged?.Invoke(this, id);
    }
}
