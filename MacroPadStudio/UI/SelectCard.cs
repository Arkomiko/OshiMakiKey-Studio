using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace OshiMakiKey.UI;

/// <summary>Карточка выбора: картинка, название, пояснение; выбранная обведена акцентом.</summary>
internal sealed class SelectCard : Control
{
    bool _selected, _hover;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Picture { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    /// <summary>Картинка сверху (темы) или слева (языки, драйверы).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PictureOnTop { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public SelectCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = Theme.Strong;
        Margin = new Padding(0, 0, Theme.S(12), Theme.S(12));
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Theme.Scale;
        var rect = new RectangleF(1.5f * s, 1.5f * s, Width - 3 * s, Height - 3 * s);
        using (var path = Theme.Rounded(rect, 10 * s))
        {
            using (var brush = new SolidBrush(_selected ? Theme.AccentSoft : _hover ? Theme.Hover : Theme.Surface)) g.FillPath(brush, path);
            using var pen = new Pen(_selected ? Theme.Accent : Focused ? Theme.Faint : Theme.Border, (_selected ? 2.5f : 1f) * s);
            g.DrawPath(pen, path);
        }

        var textColor = _selected ? Theme.AccentText : Theme.Text;
        var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (PictureOnTop)
        {
            if (Picture != null)
            {
                var picture = new RectangleF(10 * s, 10 * s, Width - 20 * s, Height - 46 * s);
                float scale = Math.Min(picture.Width / Picture.Width, picture.Height / Picture.Height);
                var size = new SizeF(Picture.Width * scale, Picture.Height * scale);
                g.DrawImage(Picture, picture.X + (picture.Width - size.Width) / 2, picture.Y + (picture.Height - size.Height) / 2, size.Width, size.Height);
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(new RectangleF(12 * s, Height - 34 * s, Width - 24 * s, 26 * s)), textColor,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            if (_selected) DrawCheck(g, new RectangleF(Width - 30 * s, 8 * s, 22 * s, 22 * s));
            return;
        }

        float x = 14 * s;
        if (Picture != null)
        {
            float h = Height - 20 * s, w = Math.Min(h * Picture.Width / Picture.Height, 90 * s);
            g.DrawImage(Picture, x, (Height - h) / 2, w, h);
            x += w + 14 * s;
        }
        float right = _selected ? 40 * s : 14 * s;
        if (Subtitle.Length == 0)
        {
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(new RectangleF(x, 0, Width - x - right, Height)), textColor,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(new RectangleF(x, 8 * s, Width - x - right, 22 * s)), textColor,
                flags | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Subtitle, Theme.Small, Rectangle.Round(new RectangleF(x, 30 * s, Width - x - right, Height - 36 * s)),
                _selected ? Theme.AccentText : Theme.Muted, flags | TextFormatFlags.WordBreak);
        }
        if (_selected) DrawCheck(g, new RectangleF(Width - 32 * s, (Height - 22 * s) / 2, 22 * s, 22 * s));
    }

    static void DrawCheck(Graphics g, RectangleF rect)
    {
        using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, rect);
        TextRenderer.DrawText(g, "✓", Theme.Strong, Rectangle.Round(rect), Theme.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }
}
