using System.ComponentModel;
using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Действие перетащили с одной кнопки на другую; Copy — с зажатым Ctrl.</summary>
internal sealed record ActionDrop(ControlId Source, ControlId Target, bool Copy);

/// <summary>
/// Рисует плату по раскладке её драйвера (клавиши сеткой, ручки справа) и список всех действий; по щелчку выбирает,
/// что настраивать. Действие можно перетащить на другую кнопку: просто — перенести, с Ctrl — скопировать.
/// </summary>
internal sealed class DevicePanel : Control
{
    const TextFormatFlags SingleLine = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    DriverDef _driver = DriverStore.Default;
    IReadOnlyList<ControlId> _order = DriverStore.Default.Controls;
    readonly Dictionary<ControlId, RectangleF> _keys = new();
    readonly Dictionary<ControlId, RectangleF> _rows = new();
    readonly List<RectangleF> _knobs = new();
    RectangleF _pad, _list;
    float _listTitleY, _scroll, _maxScroll;
    ControlId _selected = ControlId.Key1;
    ControlId? _hover;

    // Перетаскивание
    ControlId? _pressed;
    Point _pressPoint, _dragPoint;
    bool _dragging;
    ControlId? _dropTarget;

    public event EventHandler<ControlId>? SelectionChanged;
    public event EventHandler<ActionDrop>? ActionDropped;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<ControlId, PadAction>? ActionProvider { get; set; }

    /// <summary>True — действие выполняет фоновый агент (рисуется значок ⚙).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<PadAction, bool>? NeedsAgent { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DriverDef Driver
    {
        get => _driver;
        set
        {
            _driver = value;
            _order = value.Controls;
            if (!_order.Contains(_selected)) _selected = _order[0];
            _scroll = 0;
            Invalidate();
        }
    }

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

    /// <summary>Рисунок платы и список кнопок — для тура по окну.</summary>
    public Rectangle PadBounds { get { ComputeLayout(); return Rectangle.Round(_pad); } }
    public Rectangle ListBounds { get { ComputeLayout(); return Rectangle.Round(RectangleF.FromLTRB(_list.X, _listTitleY, _list.Right, _list.Bottom)); } }

    public DevicePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
        Font = Theme.Body;
        TabStop = true;
    }

    // ───────────────────────── Раскладка ─────────────────────────

    void ComputeLayout()
    {
        float s = Theme.Scale;
        float margin = 22 * s;
        float width = Math.Max(10, ClientSize.Width - 2 * margin);
        var layout = _driver.Layout;
        int rows = layout.Rows, columns = layout.Columns, knobs = layout.Knobs;

        // Клавиши сеткой слева, ручки столбиком справа.
        float inner = 18 * s, gap = 10 * s, knobMax = 92 * s;
        float knobColumn = knobs > 0 ? knobMax + 24 * s : 0;
        float key = (width - 2 * inner - knobColumn - (columns - 1) * gap) / columns;
        key = Math.Max(10, Math.Min(key, Math.Min(60 * s, (230 * s - (rows - 1) * gap) / rows)));
        float gridWidth = columns * key + (columns - 1) * gap;
        float gridHeight = rows * key + (rows - 1) * gap;
        float knob = knobs == 0 ? 0 : knobs == 1 ? knobMax : Math.Clamp((gridHeight - (knobs - 1) * gap) / knobs, 44 * s, knobMax);
        float knobsHeight = knobs * knob + Math.Max(0, knobs - 1) * gap;
        float padHeight = Math.Max(gridHeight, knobsHeight) + 2 * inner;
        _pad = new RectangleF(margin, margin + 30 * s, width, padHeight);

        float gridX = knobs == 0 ? _pad.X + (width - gridWidth) / 2 : _pad.X + inner;
        float gridY = _pad.Y + (padHeight - gridHeight) / 2;
        _keys.Clear();
        for (int i = 0; i < layout.Keys; i++)
        {
            int row = i / columns, column = i % columns;
            _keys[Model.Controls.Key(i)] = new RectangleF(gridX + column * (key + gap), gridY + row * (key + gap), key, key);
        }

        _knobs.Clear();
        float gridRight = gridX + gridWidth;
        float knobCenterX = Math.Max(gridRight + 12 * s + knob / 2, (gridRight + _pad.Right - inner) / 2);
        float knobY = _pad.Y + (padHeight - knobsHeight) / 2;
        for (int k = 0; k < knobs; k++)
            _knobs.Add(new RectangleF(knobCenterX - knob / 2, knobY + k * (knob + gap), knob, knob));

        _listTitleY = _pad.Bottom + 24 * s;
        float top = _listTitleY + 28 * s;
        _list = new RectangleF(margin, top, width, Math.Max(10, ClientSize.Height - top - 10 * s));
        // Мало места — строки ужимаются до однострочных, а не влезают — список прокручивается.
        float rowHeight = Math.Clamp(_list.Height / _order.Count, 26 * s, 46 * s);
        _rows.Clear();
        for (int i = 0; i < _order.Count; i++)
            _rows[_order[i]] = new RectangleF(margin, top + i * rowHeight, width - (rowHeight * _order.Count > _list.Height ? 8 * s : 0), rowHeight - 4 * s);
        _maxScroll = Math.Max(0, rowHeight * _order.Count - _list.Height);
        _scroll = Math.Clamp(_scroll, 0, _maxScroll);
    }

    RectangleF RowOnScreen(ControlId id) => RectangleF.Inflate(_rows[id], 0, 0) with { Y = _rows[id].Y - _scroll };

    ControlId? HitTest(Point point)
    {
        ComputeLayout();
        foreach (var (id, rect) in _keys)
            if (rect.Contains(point)) return id;
        if (_list.Contains(point))
            foreach (var id in _order)
                if (RowOnScreen(id).Contains(point)) return id;

        for (int k = 0; k < _knobs.Count; k++)
        {
            var knob = _knobs[k];
            float radius = knob.Width / 2;
            float dx = point.X - (knob.X + radius), dy = point.Y - (knob.Y + radius);
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= radius * 0.45) return Model.Controls.Knob(k, 1);
            if (distance <= radius + 8 * Theme.Scale) return Model.Controls.Knob(k, dx < 0 ? 0 : 2);
        }
        return null;
    }

    // ───────────────────────── Рисование ─────────────────────────

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

        foreach (var (id, rect) in _keys) DrawKey(g, id, rect);
        for (int k = 0; k < _knobs.Count; k++) DrawKnob(g, k, _knobs[k]);

        TextRenderer.DrawText(g, L.T("pad.list", "Все кнопки"), Theme.Subtitle, new Point((int)_pad.X, (int)_listTitleY), Theme.Text, TextFormatFlags.NoPrefix);
        var state = g.Save();
        g.SetClip(RectangleF.Inflate(_list, 2 * s, 0));
        foreach (var id in _order)
        {
            var row = RowOnScreen(id);
            if (row.Bottom >= _list.Top && row.Top <= _list.Bottom) DrawRow(g, id, row);
        }
        if (Focused && !_dragging) ControlPaint.DrawFocusRectangle(g, Rectangle.Round(RectangleF.Inflate(RowOnScreen(_selected), -1, -1)));
        g.Restore(state);
        if (_maxScroll > 0) DrawScrollBar(g);

        if (_dragging) DrawDrag(g);
    }

    void DrawKey(Graphics g, ControlId id, RectangleF rect)
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

        bool small = rect.Width < 48 * s;
        var numberRect = new RectangleF(rect.X, rect.Y + 2 * s, rect.Width, rect.Height * 0.58f);
        TextRenderer.DrawText(g, UiText.KeyNumber(id).ToString(), small ? Theme.Strong : Theme.KeyNumber, Rectangle.Round(numberRect),
            selected ? Theme.KeySelectedText : Theme.KeyText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);

        var action = ActionProvider?.Invoke(id);
        string label = action == null ? "" : BindingText.Compact(action);
        var labelRect = new RectangleF(rect.X + 3 * s, rect.Y + rect.Height * 0.6f, rect.Width - 6 * s, rect.Height * 0.34f);
        TextRenderer.DrawText(g, label, Theme.Tiny, Rectangle.Round(labelRect),
            selected ? Theme.KeySelectedText : Theme.KeyMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | SingleLine);

        if (action != null && NeedsAgent?.Invoke(action) == true)
        {
            var gearRect = new RectangleF(rect.Right - 17 * s, rect.Y + 2 * s, 15 * s, 15 * s);
            TextRenderer.DrawText(g, "⚙", Theme.SymbolSmall, Rectangle.Round(gearRect), Theme.Warning,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    void DrawKnob(Graphics g, int knob, RectangleF rect)
    {
        float s = Theme.Scale;
        if (rect.Width < 2) return;
        float radius = rect.Width / 2;
        var center = new PointF(rect.X + radius, rect.Y + radius);
        ControlId left = Model.Controls.Knob(knob, 0), press = Model.Controls.Knob(knob, 1), right = Model.Controls.Knob(knob, 2);

        using (var brush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            g.FillEllipse(brush, rect.X, rect.Y + 3 * s, rect.Width, rect.Height);
        using (var brush = new LinearGradientBrush(rect, Theme.KnobTop, Theme.KnobBottom, 90f))
            g.FillEllipse(brush, rect);

        // Насечки по краю ручки.
        using (var tick = new Pen(Color.FromArgb(55, Theme.KnobGlyph), 1.2f * s))
        {
            int ticks = radius > 30 * s ? 40 : 28;
            for (int i = 0; i < ticks; i++)
            {
                double angle = i * Math.PI * 2 / ticks;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                g.DrawLine(tick, center.X + cos * radius * 0.86f, center.Y + sin * radius * 0.86f,
                    center.X + cos * radius * 0.97f, center.Y + sin * radius * 0.97f);
            }
        }

        DrawKnobArc(g, rect, left, 112, 136);
        DrawKnobArc(g, rect, right, -68, 136);

        float glyphOffset = radius * 0.7f, glyphSize = radius * 0.6f;
        var font = radius > 30 * s ? Theme.Symbol : Theme.SymbolSmall;
        DrawGlyph(g, "↺", new RectangleF(center.X - glyphOffset - glyphSize / 2, center.Y - glyphSize / 2, glyphSize, glyphSize), left, font);
        DrawGlyph(g, "↻", new RectangleF(center.X + glyphOffset - glyphSize / 2, center.Y - glyphSize / 2, glyphSize, glyphSize), right, font);

        bool selected = _selected == press, hover = _hover == press;
        float capRadius = radius * 0.45f;
        var capRect = new RectangleF(center.X - capRadius, center.Y - capRadius, capRadius * 2, capRadius * 2);
        var top = selected ? Theme.KnobGlow : hover ? Theme.KnobCapHover : Theme.KnobCap;
        var bottom = selected ? Theme.Accent : Theme.KnobCapBottom;
        using (var brush = new LinearGradientBrush(capRect, top, bottom, 90f)) g.FillEllipse(brush, capRect);
        using (var pen = new Pen(selected ? Theme.OnAccent : Color.FromArgb(70, 0, 0, 0), (selected ? 2.5f : 1f) * s))
            g.DrawEllipse(pen, capRect);
        if (_driver.Layout.Knobs > 1)
            TextRenderer.DrawText(g, (knob + 1).ToString(), Theme.Tiny, Rectangle.Round(capRect), selected ? Theme.OnAccent : Color.FromArgb(200, Theme.KnobGlyph),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    void DrawKnobArc(Graphics g, RectangleF knob, ControlId id, float start, float sweep)
    {
        bool selected = _selected == id, hover = _hover == id;
        if (!selected && !hover) return;
        float s = Theme.Scale;
        float width = knob.Width > 60 * s ? 7 * s : 5 * s;
        var arcRect = RectangleF.Inflate(knob, -width / 2 - 0.5f * s, -width / 2 - 0.5f * s);
        using var pen = new Pen(selected ? Theme.KnobGlow : Color.FromArgb(140, Theme.KnobGlow), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(pen, arcRect, start, sweep);
    }

    void DrawGlyph(Graphics g, string glyph, RectangleF rect, ControlId id, Font font)
    {
        var color = _selected == id ? Theme.KnobGlyph : Color.FromArgb(_hover == id ? 230 : 170, Theme.KnobGlyph);
        TextRenderer.DrawText(g, glyph, font, Rectangle.Round(RectangleF.Inflate(rect, 6, 6)), color,
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
            float titleWidth = Math.Min(textWidth * 0.5f, 190 * s);
            TextRenderer.DrawText(g, UiText.Title(id), Theme.Strong,
                Rectangle.Round(new RectangleF(textX, rect.Y, titleWidth, rect.Height)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | SingleLine);
            TextRenderer.DrawText(g, summary, Theme.Small,
                Rectangle.Round(new RectangleF(textX + titleWidth + 8 * s, rect.Y, textWidth - titleWidth - 8 * s, rect.Height)),
                summaryColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | SingleLine);
        }
    }

    void DrawScrollBar(Graphics g)
    {
        float s = Theme.Scale;
        float content = _list.Height + _maxScroll;
        float height = Math.Max(24 * s, _list.Height * _list.Height / content);
        float y = _list.Y + (_list.Height - height) * (_scroll / _maxScroll);
        using var path = Theme.Rounded(new RectangleF(_list.Right - 4 * s, y, 4 * s, height), 2 * s);
        using var brush = new SolidBrush(Theme.Faint);
        g.FillPath(brush, path);
    }

    /// <summary>Подсветка кнопки под курсором и «призрак» переносимого действия.</summary>
    void DrawDrag(Graphics g)
    {
        float s = Theme.Scale;
        bool copy = (ModifierKeys & Keys.Control) != 0;

        if (_dropTarget is ControlId target && TargetRect(target) is RectangleF rect)
        {
            using var path = Theme.Rounded(RectangleF.Inflate(rect, 3 * s, 3 * s), 10 * s);
            using var pen = new Pen(Theme.Accent, 2.5f * s) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        if (_pressed is not ControlId source || ActionProvider?.Invoke(source) is not { } action) return;
        string text = BindingText.Compact(action);
        var size = TextRenderer.MeasureText(text, Theme.Small);
        float badge = copy ? 18 * s : 0;
        var ghost = new RectangleF(_dragPoint.X + 14 * s, _dragPoint.Y + 10 * s, Math.Min(220 * s, size.Width + badge + 20 * s), 28 * s);
        using (var shadow = Theme.Rounded(new RectangleF(ghost.X, ghost.Y + 3 * s, ghost.Width, ghost.Height), 8 * s))
        using (var brush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            g.FillPath(brush, shadow);
        using (var path = Theme.Rounded(ghost, 8 * s))
        using (var brush = new SolidBrush(Color.FromArgb(235, Theme.Surface)))
        using (var pen = new Pen(Theme.Accent, 1.5f * s))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (copy)
        {
            var plus = new RectangleF(ghost.X + 6 * s, ghost.Y + (ghost.Height - 16 * s) / 2, 16 * s, 16 * s);
            using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, plus);
            TextRenderer.DrawText(g, "+", Theme.Strong, Rectangle.Round(plus), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, text, Theme.Small, Rectangle.Round(new RectangleF(ghost.X + 10 * s + badge, ghost.Y, ghost.Width - 14 * s - badge, ghost.Height)),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | SingleLine);
    }

    RectangleF? TargetRect(ControlId id)
    {
        if (_keys.TryGetValue(id, out var key)) return key;
        var row = RowOnScreen(id);
        if (_list.Contains(_dragPoint) && row.Contains(_dragPoint)) return row;
        int knob = Model.Controls.KnobIndex(id);
        return !Model.Controls.IsKey(id) && knob < _knobs.Count ? _knobs[knob] : null;
    }

    // ───────────────────────── Мышь и клавиатура ─────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        _pressed = HitTest(e.Location);
        _pressPoint = e.Location;
        if (_pressed is ControlId id) Choose(id);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left && _pressed is ControlId source && !_dragging)
        {
            var drag = SystemInformation.DragSize;
            bool moved = Math.Abs(e.X - _pressPoint.X) > drag.Width || Math.Abs(e.Y - _pressPoint.Y) > drag.Height;
            if (moved && ActionProvider?.Invoke(source) is { Kind: not ActionKind.None })
            {
                _dragging = true;
                Capture = true;
            }
        }

        if (_dragging)
        {
            _dragPoint = e.Location;
            AutoScrollWhileDragging(e.Location);
            var hit = HitTest(e.Location);
            _dropTarget = hit == _pressed ? null : hit;
            Cursor = _dropTarget != null ? Cursors.Hand : Cursors.No;
            Invalidate();
            return;
        }

        var hover = HitTest(e.Location);
        if (hover == _hover) return;
        _hover = hover;
        Cursor = hover != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    void AutoScrollWhileDragging(Point point)
    {
        if (_maxScroll <= 0 || point.X < _list.X || point.X > _list.Right) return;
        float edge = 24 * Theme.Scale;
        if (point.Y < _list.Top + edge) _scroll -= 12 * Theme.Scale;
        else if (point.Y > _list.Bottom - edge) _scroll += 12 * Theme.Scale;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging && _pressed is ControlId source && _dropTarget is ControlId target)
        {
            bool copy = (ModifierKeys & Keys.Control) != 0;
            EndDrag();
            ActionDropped?.Invoke(this, new ActionDrop(source, target, copy));
        }
        else EndDrag();
        _pressed = null;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_dragging && !Capture) EndDrag();
    }

    void EndDrag()
    {
        bool wasDragging = _dragging;
        _dragging = false;
        _dropTarget = null;
        if (Capture) Capture = false;
        Cursor = Cursors.Default;
        if (wasDragging) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_maxScroll <= 0) return;
        _scroll = Math.Clamp(_scroll - e.Delta / 120f * 40 * Theme.Scale, 0, _maxScroll);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Escape || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_dragging)
        {
            if (e.KeyCode == Keys.Escape) EndDrag();
            else if (e.KeyCode == Keys.ControlKey) Invalidate();
            return;
        }
        int index = _order.ToList().IndexOf(_selected);
        if (e.KeyCode is Keys.Down or Keys.Right) Choose(_order[Math.Min(_order.Count - 1, index + 1)]);
        else if (e.KeyCode is Keys.Up or Keys.Left) Choose(_order[Math.Max(0, index - 1)]);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_dragging && e.KeyCode == Keys.ControlKey) Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    void Choose(ControlId id)
    {
        Selected = id;
        EnsureVisible(id);
        SelectionChanged?.Invoke(this, id);
    }

    void EnsureVisible(ControlId id)
    {
        ComputeLayout();
        if (!_rows.TryGetValue(id, out var row)) return;
        if (row.Y - _scroll < _list.Top) _scroll = row.Y - _list.Top;
        else if (row.Bottom - _scroll > _list.Bottom) _scroll = row.Bottom - _list.Bottom;
        _scroll = Math.Clamp(_scroll, 0, _maxScroll);
        Invalidate();
    }
}
