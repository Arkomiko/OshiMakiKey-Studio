using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Шаг тура: область окна (в координатах окна), заголовок и пояснение.</summary>
internal sealed record TourStep(Rectangle Target, string Title, string Text);

/// <summary>
/// Тур по окну: поверх снимка окна затемняет всё, кроме нужной области, и показывает рядом карточку с
/// пояснением. Стрелки и Enter листают, Esc закрывает.
/// </summary>
internal sealed class TourOverlay : Control
{
    readonly Form _owner;
    readonly IReadOnlyList<TourStep> _steps;
    readonly Button _back, _next, _skip;
    Bitmap? _shot;
    RectangleF _card;
    int _index;
    bool _finished;

    public event EventHandler? Finished;

    public TourOverlay(Form owner, IReadOnlyList<TourStep> steps)
    {
        _owner = owner;
        _steps = steps;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        Font = Theme.Body;
        TabStop = true;

        _next = ThemedDialog.MakeButton(L.T("tour.next", "Далее"), primary: true);
        _back = ThemedDialog.MakeButton(L.T("tour.back", "Назад"), primary: false);
        _skip = ThemedDialog.MakeButton(L.T("tour.skip", "Пропустить"), primary: false);
        _skip.FlatAppearance.BorderSize = 0;
        _skip.ForeColor = Theme.Muted;
        _next.Click += (_, _) => Go(_index + 1);
        _back.Click += (_, _) => Go(_index - 1);
        _skip.Click += (_, _) => Finish();
        Controls.AddRange(new Control[] { _next, _back, _skip });
    }

    public void Start()
    {
        TakeSnapshot();
        Bounds = _owner.ClientRectangle;
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _owner.Controls.Add(this);
        BringToFront();
        _owner.Resize += OnOwnerResize;
        Go(0);
        Focus();
    }

    /// <summary>Снимок клиентской части окна — под затемнением видно настоящее окно.</summary>
    void TakeSnapshot()
    {
        using var full = new Bitmap(Math.Max(1, _owner.Width), Math.Max(1, _owner.Height));
        _owner.DrawToBitmap(full, new Rectangle(Point.Empty, _owner.Size));
        var origin = _owner.PointToScreen(Point.Empty);
        var offset = new Point(origin.X - _owner.Left, origin.Y - _owner.Top);
        _shot?.Dispose();
        _shot = full.Clone(new Rectangle(offset, new Size(Math.Max(1, Math.Min(_owner.ClientSize.Width, full.Width - offset.X)),
            Math.Max(1, Math.Min(_owner.ClientSize.Height, full.Height - offset.Y)))), full.PixelFormat);
    }

    // Области шагов посчитаны для прежнего размера окна — при изменении размера тур закрывается.
    void OnOwnerResize(object? sender, EventArgs e) => Finish();

    public void Go(int index)
    {
        if (index >= _steps.Count)
        {
            Finish();
            return;
        }
        _index = Math.Max(0, index);
        _next.Text = _index == _steps.Count - 1 ? L.T("tour.done", "Готово") : L.T("tour.next", "Далее");
        _back.Visible = _index > 0;
        LayoutCard();
        Invalidate();
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        _owner.Resize -= OnOwnerResize;
        _owner.Controls.Remove(this);
        Finished?.Invoke(this, EventArgs.Empty);
        // Не уничтожаем себя внутри обработчика щелчка своей же кнопки.
        _owner.BeginInvoke(new Action(Dispose));
    }

    Rectangle Hole => Rectangle.Inflate(Rectangle.Intersect(_steps[_index].Target, ClientRectangle), Theme.S(6), Theme.S(6));

    float CardWidth => Theme.S(390);

    Size TextSize(TourStep step) =>
        TextRenderer.MeasureText(step.Text, Theme.Body, new Size((int)CardWidth - Theme.S(40), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

    /// <summary>Карточка рядом с областью: снизу, сверху, справа или слева — где поместится.</summary>
    void LayoutCard()
    {
        float s = Theme.Scale;
        var step = _steps[_index];
        float height = 20 * s + 28 * s + TextSize(step).Height + 18 * s + 38 * s + 18 * s;
        float width = CardWidth;
        var hole = Hole;
        float gap = 16 * s;

        RectangleF Place(float x, float y) => new(Math.Clamp(x, 12 * s, Width - width - 12 * s), Math.Clamp(y, 12 * s, Height - height - 12 * s), width, height);
        float centerX = hole.X + hole.Width / 2f - width / 2;
        if (hole.Bottom + gap + height < Height) _card = Place(centerX, hole.Bottom + gap);
        else if (hole.Top - gap - height > 0) _card = Place(centerX, hole.Top - gap - height);
        else if (hole.Right + gap + width < Width) _card = Place(hole.Right + gap, hole.Y + hole.Height / 2f - height / 2);
        else if (hole.Left - gap - width > 0) _card = Place(hole.Left - gap - width, hole.Y + hole.Height / 2f - height / 2);
        else _card = Place(Width / 2f - width / 2, Height / 2f - height / 2);

        int bottom = (int)(_card.Bottom - 18 * s - _next.Height);
        _next.Location = new Point((int)(_card.Right - 20 * s - _next.Width), bottom);
        _back.Location = new Point(_next.Left - Theme.S(8) - _back.Width, bottom);
        _skip.Location = new Point((int)(_card.X + 12 * s), bottom);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_steps.Count > 0 && !_finished) LayoutCard();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Theme.Scale;
        if (_shot != null) g.DrawImageUnscaled(_shot, 0, 0);

        var hole = Hole;
        using (var holePath = Theme.Rounded(hole, 10 * s))
        using (var dim = new Region(ClientRectangle))
        {
            dim.Exclude(holePath);
            using var brush = new SolidBrush(Color.FromArgb(165, 0, 0, 0));
            g.FillRegion(brush, dim);
            using var pen = new Pen(Theme.Accent, 2.5f * s);
            g.DrawPath(pen, holePath);
        }

        var step = _steps[_index];
        using (var shadow = Theme.Rounded(new RectangleF(_card.X, _card.Y + 5 * s, _card.Width, _card.Height), 14 * s))
        using (var brush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillPath(brush, shadow);
        using (var path = Theme.Rounded(_card, 14 * s))
        using (var brush = new SolidBrush(Theme.Surface))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        using (var bar = Theme.Rounded(new RectangleF(_card.X, _card.Y, _card.Width, 6 * s), 3 * s))
        using (var brush = new SolidBrush(Theme.Accent))
            g.FillPath(brush, bar);

        float x = _card.X + 20 * s, y = _card.Y + 20 * s;
        TextRenderer.DrawText(g, L.F("tour.step", "{0} из {1}", _index + 1, _steps.Count), Theme.Small,
            Rectangle.Round(new RectangleF(x, y, _card.Width - 40 * s, 22 * s)), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, step.Title, Theme.Subtitle, Rectangle.Round(new RectangleF(x, y, _card.Width - 100 * s, 26 * s)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        var textSize = TextSize(step);
        TextRenderer.DrawText(g, step.Text, Theme.Body, Rectangle.Round(new RectangleF(x, y + 30 * s, _card.Width - 40 * s, textSize.Height + 4 * s)), Theme.Muted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    // Щелчок мимо карточки ничего не делает, чтобы случайно не закрыть тур.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Escape || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Finish();
        else if (e.KeyCode is Keys.Right or Keys.Enter) Go(_index + 1);
        else if (e.KeyCode == Keys.Left && _index > 0) Go(_index - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _shot?.Dispose();
        base.Dispose(disposing);
    }
}
