using System.Drawing.Drawing2D;

namespace OshiMakiKey.UI;

/// <summary>
/// Выпадающий список (только выбор), нарисованный в цветах темы: системный ComboBox остаётся светлым
/// в тёмных темах и серым в режиме Flat.
/// </summary>
internal sealed class ThemedComboBox : ComboBox
{
    const TextFormatFlags TextFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    bool _hover;

    public ThemedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = Theme.S(20);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Input;
        ForeColor = Theme.InputText;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var back = new SolidBrush(Enabled ? Theme.Input : Theme.Hover)) g.FillRectangle(back, ClientRectangle);

        var border = Focused || DroppedDown ? Theme.Accent : _hover ? Theme.Faint : Theme.Border;
        using (var pen = new Pen(border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        int arrowWidth = Theme.S(22);
        string text = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) ?? "" : "";
        TextRenderer.DrawText(g, text, Font, new Rectangle(Theme.S(7), 0, Width - arrowWidth - Theme.S(7), Height),
            Enabled ? Theme.InputText : Theme.Faint, TextFlags);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = Width - arrowWidth / 2f - 1, cy = Height / 2f, a = 3.5f * Theme.Scale;
        using var arrow = new Pen(Enabled ? Theme.Muted : Theme.Faint, 1.6f * Theme.Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(arrow, new[] { new PointF(cx - a, cy - a / 2), new PointF(cx, cy + a / 2), new PointF(cx + a, cy - a / 2) });
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(selected ? Theme.AccentSoft : Theme.Input)) e.Graphics.FillRectangle(brush, e.Bounds);
        var textRect = new Rectangle(e.Bounds.X + Theme.S(7), e.Bounds.Y, e.Bounds.Width - Theme.S(9), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textRect, selected ? Theme.AccentText : Theme.InputText, TextFlags);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}
