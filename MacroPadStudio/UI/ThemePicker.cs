using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Выбор темы: сворачиваемая группа «Стандартные» (светлые и тёмные — кружками цвета акцента)
/// и группа «Пользовательские» (темы из theme/custom — карточками с миниатюрой).
/// </summary>
internal sealed class ThemePicker : FlowLayoutPanel
{
    // Свёрнута ли группа «Стандартные» — запоминается до закрытия программы.
    static bool _standardCollapsed;

    readonly List<(string Id, Action<bool> Mark)> _items = new();
    readonly List<Image> _images = new();
    readonly ToolTip _tips = new();
    string _selected;

    /// <summary>Пользователь выбрал тему (id).</summary>
    public event EventHandler<string>? Picked;

    public ThemePicker(string selected, int width)
    {
        _selected = selected;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        BackColor = Theme.Window;
        Margin = new Padding(0);

        var standard = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Theme.Window, Margin = new Padding(Theme.S(4), 0, 0, Theme.S(10)) };
        standard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        standard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (title, dark) in new[] { (L.T("theme.group.light", "Светлые"), false), (L.T("theme.group.dark", "Тёмные"), true) })
        {
            var themes = ThemeStore.Standard.Where(t => t.Dark == dark).ToList();
            if (themes.Count == 0) continue;
            standard.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.S(14), 0) });
            var dots = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Math.Max(Theme.S(200), width - Theme.S(110)), 0), BackColor = Theme.Window, Margin = new Padding(0, Theme.S(2), 0, Theme.S(2)) };
            foreach (var theme in themes)
            {
                var dot = new Dot(theme) { Selected = theme.Id == selected };
                _tips.SetToolTip(dot, theme.Title);
                dot.Click += (_, _) => Pick(theme.Id);
                _items.Add((theme.Id, on => dot.Selected = on));
                dots.Controls.Add(dot);
            }
            standard.Controls.Add(dots);
        }
        standard.Visible = !_standardCollapsed;

        var header = new GroupHeader(L.T("theme.group.standard", "Стандартные")) { Expanded = !_standardCollapsed };
        header.Click += (_, _) =>
        {
            // «Шторка»: группа сворачивается и разворачивается плавным переходом окна.
            void Toggle()
            {
                _standardCollapsed = !_standardCollapsed;
                header.Expanded = !_standardCollapsed;
                standard.Visible = !_standardCollapsed;
            }
            if (FindForm() is { } form) Transition.Run(form, Toggle);
            else Toggle();
        };
        Controls.Add(header);
        Controls.Add(standard);

        Controls.Add(new Label { Text = L.T("theme.group.custom", "Пользовательские"), AutoSize = true, Font = Theme.Subtitle, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(8), 0, Theme.S(8)) });
        var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(width + Theme.S(12), 0), BackColor = Theme.Window, Margin = new Padding(0) };
        foreach (var theme in ThemeStore.CustomThemes)
        {
            var preview = ThemeStore.Preview(theme, Theme.S(160), Theme.S(100));
            _images.Add(preview);
            var card = new SelectCard
            {
                Text = theme.Title,
                Picture = preview,
                PictureOnTop = true,
                Size = new Size(Theme.S(184), Theme.S(150)),
                Selected = theme.Id == selected,
            };
            card.Click += (_, _) => Pick(theme.Id);
            _items.Add((theme.Id, on => card.Selected = on));
            cards.Controls.Add(card);
        }
        if (cards.Controls.Count > 0) Controls.Add(cards);
        Controls.Add(new Label
        {
            Text = L.F("theme.custom.hint", "Свои темы кладите в папку {0}: файл .json или .ts, либо пакет .zip с theme.ts и папками img, animate и scripts.",
                Path.Combine(Path.GetFileName(AppPaths.Themes), "custom")),
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(width, 0),
            Margin = new Padding(0, Theme.S(2), 0, Theme.S(6)),
        });
    }

    /// <summary>Выбранная тема; при смене обновляются отметки.</summary>
    public string Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            foreach (var (id, mark) in _items) mark(id == value);
        }
    }

    void Pick(string id)
    {
        Selected = id;
        Picked?.Invoke(this, id);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            foreach (var image in _images) image.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Заголовок сворачиваемой группы: стрелка и название.</summary>
    sealed class GroupHeader : Control
    {
        bool _expanded, _hover;

        public bool Expanded
        {
            get => _expanded;
            set { _expanded = value; Invalidate(); }
        }

        public GroupHeader(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Text = text;
            Font = Theme.Subtitle;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, Theme.S(4), 0, Theme.S(8));
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + Theme.S(34), Theme.S(30));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.Scale, cx = 9 * s, cy = Height / 2f;
            // Стрелка: ▾ — развёрнуто, ▸ — свёрнуто.
            var points = _expanded
                ? new[] { new PointF(cx - 5 * s, cy - 3 * s), new PointF(cx + 5 * s, cy - 3 * s), new PointF(cx, cy + 3.5f * s) }
                : new[] { new PointF(cx - 3 * s, cy - 5 * s), new PointF(cx + 3.5f * s, cy), new PointF(cx - 3 * s, cy + 5 * s) };
            using (var brush = new SolidBrush(_hover ? Theme.Accent : Theme.Muted)) g.FillPolygon(brush, points);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(22 * s), 0, Width, Height), _hover ? Theme.Accent : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Кружок цвета акцента темы; выбранный обведён кольцом с галочкой.</summary>
    sealed class Dot : Control
    {
        readonly Color _accent, _window;
        bool _selected, _hover;

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public Dot(ThemeDef theme)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _accent = theme.AccentColor;
            _window = Theme.Parse(theme.Colors).TryGetValue("window", out var window) ? window : theme.Dark ? Color.FromArgb(0x1E, 0x1F, 0x22) : Color.White;
            Size = new Size(Theme.S(34), Theme.S(34));
            Margin = new Padding(0, 0, Theme.S(4), Theme.S(4));
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.Scale, size = Width;
            if (_selected || _hover)
                using (var pen = new Pen(_selected ? Theme.Text : Theme.Faint, 2 * s))
                    g.DrawEllipse(pen, 1.5f * s, 1.5f * s, size - 3 * s, size - 3 * s);
            // Кружок: фон темы по краю и её акцент внутри — светлая и тёмная с одним акцентом различимы.
            var outer = new RectangleF(5 * s, 5 * s, size - 10 * s, size - 10 * s);
            using (var brush = new SolidBrush(_window)) g.FillEllipse(brush, outer);
            using (var pen = new Pen(Color.FromArgb(70, 128, 128, 128), 1)) g.DrawEllipse(pen, outer);
            var inner = RectangleF.Inflate(outer, -3 * s, -3 * s);
            using (var brush = new SolidBrush(_accent)) g.FillEllipse(brush, inner);
            if (_selected)
            {
                using var pen = new Pen(Theme.Luma(_accent) > 0.6 ? Color.FromArgb(30, 30, 30) : Color.White, 2 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float c = size / 2;
                g.DrawLines(pen, new[] { new PointF(c - 4.5f * s, c), new PointF(c - 1.2f * s, c + 3.3f * s), new PointF(c + 5 * s, c - 3.5f * s) });
            }
        }
    }
}
