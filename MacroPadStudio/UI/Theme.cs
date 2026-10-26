using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OshiMakiKey.UI;

/// <summary>
/// Цвета текущей темы, шрифты, масштаб под DPI и иконка приложения. Цвета задаёт <see cref="ThemeStore"/> из файла
/// theme/*.json; чего в файле нет, то выводится из основных цветов, поэтому теме достаточно нескольких строк.
/// </summary>
internal static class Theme
{
    public static float Scale { get; private set; } = 1f;

    public static bool Dark { get; private set; }

    // Окно и текст
    public static Color Window { get; private set; }
    public static Color Surface { get; private set; }
    public static Color Border { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Faint { get; private set; }
    public static Color Hover { get; private set; }
    public static Color Input { get; private set; }
    public static Color InputText { get; private set; }

    // Акцент
    public static Color Accent { get; private set; }
    public static Color AccentDark { get; private set; }
    public static Color AccentPressed { get; private set; }
    public static Color AccentSoft { get; private set; }
    public static Color AccentText { get; private set; }
    public static Color OnAccent { get; private set; }

    // Состояния
    public static Color Success { get; private set; }
    public static Color Danger { get; private set; }
    public static Color Warning { get; private set; }
    public static Color SuccessSoft { get; private set; }
    public static Color SuccessText { get; private set; }
    public static Color WarningSoft { get; private set; }
    public static Color WarningText { get; private set; }

    // Рисунок устройства
    public static Color PadTop { get; private set; }
    public static Color PadBottom { get; private set; }
    public static Color KeyTop { get; private set; }
    public static Color KeyBottom { get; private set; }
    public static Color KeyHover { get; private set; }
    public static Color KeySelectedTop { get; private set; }
    public static Color KeySelectedBottom { get; private set; }
    public static Color KeyText { get; private set; }
    public static Color KeyMuted { get; private set; }
    public static Color KeySelectedText { get; private set; }
    public static Color KnobTop { get; private set; }
    public static Color KnobBottom { get; private set; }
    public static Color KnobCap { get; private set; }
    public static Color KnobCapHover { get; private set; }
    public static Color KnobCapBottom { get; private set; }
    public static Color KnobGlow { get; private set; }
    public static Color KnobGlyph { get; private set; }
    public static Color Badge { get; private set; }
    public static Color BadgeText { get; private set; }

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Tiny = new("Segoe UI", 7.5f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Subtitle = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font KeyNumber = new("Segoe UI Semibold", 15f);
    public static readonly Font Symbol = new("Segoe UI Symbol", 12f);
    public static readonly Font SymbolSmall = new("Segoe UI Symbol", 10f);

    static readonly bool HasIconFont = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");

    /// <summary>Шрифт значков Windows 10 (Segoe MDL2 Assets), без него — обычные символы.</summary>
    public static readonly Font IconFont = HasIconFont ? new Font("Segoe MDL2 Assets", 11f) : new Font("Segoe UI Symbol", 11f);

    public static string IconGlyph(string mdl2, string fallback) => HasIconFont ? mdl2 : fallback;

    static Theme() => Apply(new ThemeDef());

    /// <summary>Вызывается после включения DPI-режима, чтобы размеры в пикселях масштабировались под экран.</summary>
    public static void Init()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Scale = Math.Max(1f, g.DpiX / 96f);
    }

    public static int S(int px) => (int)Math.Round(px * Scale);

    // ───────────────────────── Палитра ─────────────────────────

    /// <summary>Включает тему. Неизвестные или неверные цвета молча заменяются выведенными.</summary>
    public static void Apply(ThemeDef theme)
    {
        var colors = Parse(theme.Colors);
        bool d = Dark = theme.Dark;
        Color Get(string key, Color fallback) => colors.TryGetValue(key, out var color) ? color : fallback;
        var white = Color.White;
        var black = Color.Black;

        Window = Get("window", d ? Rgb(0x1E1F22) : Rgb(0xF3F4F6));
        Surface = Get("surface", d ? Rgb(0x2B2D31) : white);
        Text = Get("text", d ? Rgb(0xE6E7EA) : Rgb(0x111827));
        Border = Get("border", Blend(Surface, Text, d ? 0.16f : 0.12f));
        Muted = Get("muted", Blend(Surface, Text, 0.6f));
        Faint = Get("faint", Blend(Surface, Text, 0.4f));
        Hover = Get("hover", Blend(Surface, Text, d ? 0.08f : 0.045f));
        Input = Get("input", d ? Blend(Surface, Text, 0.05f) : Surface);
        InputText = Get("inputText", Text);

        Accent = Get("accent", d ? Rgb(0x4C8DFF) : Rgb(0x2563EB));
        AccentDark = Get("accentDark", d ? Blend(Accent, white, 0.15f) : Blend(Accent, black, 0.2f));
        AccentPressed = Get("accentPressed", d ? Blend(Accent, white, 0.28f) : Blend(Accent, black, 0.32f));
        AccentSoft = Get("accentSoft", Blend(Surface, Accent, d ? 0.22f : 0.1f));
        AccentText = Get("accentText", d ? Blend(Accent, white, 0.45f) : Blend(Accent, black, 0.3f));
        OnAccent = Get("onAccent", Luma(Accent) > 0.6 ? Rgb(0x111111) : white);

        Success = Get("success", d ? Rgb(0x4ADE80) : Rgb(0x16A34A));
        Danger = Get("danger", d ? Rgb(0xF87171) : Rgb(0xDC2626));
        Warning = Get("warning", d ? Rgb(0xFBBF24) : Rgb(0xD97706));
        SuccessSoft = Get("successSoft", Blend(Surface, Success, d ? 0.18f : 0.14f));
        SuccessText = Get("successText", d ? Blend(Success, white, 0.35f) : Blend(Success, black, 0.4f));
        WarningSoft = Get("warningSoft", Blend(Surface, Warning, d ? 0.18f : 0.16f));
        WarningText = Get("warningText", d ? Blend(Warning, white, 0.35f) : Blend(Warning, black, 0.42f));

        PadTop = Get("padTop", Rgb(0x383D47));
        PadBottom = Get("padBottom", Rgb(0x1F2228));
        KeyTop = Get("keyTop", Rgb(0xF3F4F7));
        KeyBottom = Get("keyBottom", Rgb(0xD3D6DD));
        KeyHover = Get("keyHover", white);
        KeySelectedTop = Get("keySelectedTop", Blend(white, Accent, 0.07f));
        KeySelectedBottom = Get("keySelectedBottom", Blend(white, Accent, 0.22f));
        KeyText = Get("keyText", Rgb(0x111827));
        KeyMuted = Get("keyMuted", Rgb(0x6B7280));
        KeySelectedText = Get("keySelectedText", Blend(Accent, black, 0.2f));
        KnobTop = Get("knobTop", Rgb(0x707681));
        KnobBottom = Get("knobBottom", Rgb(0x343840));
        KnobCap = Get("knobCap", Rgb(0x505560));
        KnobCapHover = Get("knobCapHover", Rgb(0x626874));
        KnobCapBottom = Get("knobCapBottom", Rgb(0x2A2E35));
        KnobGlow = Get("knobGlow", Blend(Accent, white, 0.35f));
        KnobGlyph = Get("knobGlyph", white);
        Badge = Get("badge", Blend(Surface, Text, 0.1f));
        BadgeText = Get("badgeText", Text);
    }

    /// <summary>Цвета из файла темы: «#RRGGBB» или «#AARRGGBB».</summary>
    public static Dictionary<string, Color> Parse(Dictionary<string, string>? colors)
    {
        var result = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        if (colors == null) return result;
        foreach (var (key, value) in colors)
            if (TryParseColor(value, out var color)) result[key] = color;
        return result;
    }

    public static bool TryParseColor(string? value, out Color color)
    {
        color = Color.Empty;
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint number))
            return false;
        color = hex.Length == 6 ? Color.FromArgb((int)(0xFF000000 | number)) : Color.FromArgb((int)number);
        return true;
    }

    static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>Цвет между from (t = 0) и to (t = 1).</summary>
    public static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * t),
        (int)Math.Round(from.G + (to.G - from.G) * t),
        (int)Math.Round(from.B + (to.B - from.B) * t));

    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    // ───────────────────────── Оформление стандартных элементов ─────────────────────────

    /// <summary>Окно целиком: цвет текста, поля ввода, тёмный заголовок и полосы прокрутки в тёмной теме.</summary>
    public static void ApplyTo(Form form)
    {
        form.ForeColor = Text;
        Style(form);
        if (form.IsHandleCreated) DarkTitleBar(form);
        else form.HandleCreated += (_, _) => DarkTitleBar(form);
    }

    /// <summary>Поля ввода и списки внутри control (рекурсивно) — в цвета темы.</summary>
    public static void Style(Control control)
    {
        foreach (Control child in control.Controls) Style(child);
        switch (control)
        {
            case TextBox or ListBox or NumericUpDown:
                control.BackColor = Input;
                control.ForeColor = InputText;
                if (control is TextBox { Multiline: true } or ListBox) DarkScrollBars(control);
                break;
            case ComboBox combo:
                // ThemedComboBox рисует себя сам; у обычного остаются только цвета списка.
                combo.BackColor = Input;
                combo.ForeColor = InputText;
                break;
            case CheckBox { Appearance: Appearance.Normal } check:
                check.FlatStyle = FlatStyle.Flat;
                check.FlatAppearance.BorderColor = Faint;
                check.FlatAppearance.CheckedBackColor = Input;
                break;
            case ScrollableControl { AutoScroll: true }:
                DarkScrollBars(control);
                break;
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    static void DarkTitleBar(Form form)
    {
        if (!Dark) return;
        int on = 1;
        // 20 — Windows 10 20H1 и новее, 19 — более ранние сборки Windows 10.
        if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
    }

    static void DarkScrollBars(Control control)
    {
        if (!Dark) return;
        if (control.IsHandleCreated) SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        else control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    /// <summary>Отрисовка меню, контекстных меню и строки состояния в цветах темы.</summary>
    public static ToolStripRenderer CreateRenderer() => new ThemeRenderer();

    sealed class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // У надписей строки состояния свой цвет (например, красный при ошибке) — его не трогаем.
            if (e.Item is ToolStripMenuItem) e.TextColor = e.Item.Enabled ? Text : Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Faint : Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var rect = Rectangle.Inflate(e.ImageRectangle, S(1), S(1));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(rect, S(3)))
            using (var brush = new SolidBrush(Accent))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, "✓", Strong, rect, OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    sealed class ThemeColors : ProfessionalColorTable
    {
        public ThemeColors() => UseSystemColors = false;

        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => AccentSoft;
        public override Color CheckSelectedBackground => AccentSoft;
        public override Color CheckPressedBackground => AccentSoft;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
        public override Color StatusStripBorder => Border;
        public override Color ToolStripBorder => Border;
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedBorder => Border;
    }

    // ───────────────────────── Геометрия и иконка ─────────────────────────

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Icon? _appIcon, _smallIcon;

    public static Icon AppIcon => _appIcon ??= LoadIcon(new Size(32, 32));
    public static Icon SmallIcon => _smallIcon ??= LoadIcon(SystemInformation.SmallIconSize);

    static Icon LoadIcon(Size size)
    {
        try
        {
            return new Icon(new MemoryStream(BuildIconBytes()), size);
        }
        catch (Exception)
        {
            using var bitmap = DrawIcon(size.Width);
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }

    /// <summary>Многоразмерный .ico с PNG-кадрами.</summary>
    public static byte[] BuildIconBytes()
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = sizes.Select(size =>
        {
            using var bitmap = DrawIcon(size);
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToList();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);
        writer.Flush();
        return output.ToArray();
    }

    /// <summary>Иконка не зависит от темы — это знак программы.</summary>
    static Bitmap DrawIcon(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float u = size / 32f;

        var body = new RectangleF(1 * u, 1 * u, 30 * u, 30 * u);
        using (var path = Rounded(body, 7 * u))
        using (var brush = new LinearGradientBrush(body, Color.FromArgb(59, 130, 246), Color.FromArgb(30, 64, 175), 90f))
            g.FillPath(brush, path);

        using var white = new SolidBrush(Color.White);
        float key = 6.5f * u, gap = 2f * u, x0 = 4f * u, y0 = 8.5f * u;
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
                using (var keyPath = Rounded(new RectangleF(x0 + col * (key + gap), y0 + row * (key + gap), key, key), 1.6f * u))
                    g.FillPath(white, keyPath);

        float knob = 9.5f * u;
        g.FillEllipse(white, 24.5f * u - knob / 2, 16f * u - knob / 2, knob, knob);
        float dot = 3.2f * u;
        using var accent = new SolidBrush(Color.FromArgb(29, 78, 216));
        g.FillEllipse(accent, 24.5f * u - dot / 2, 16f * u - dot / 2, dot, dot);
        return bitmap;
    }
}

/// <summary>Цветная точка-индикатор.</summary>
internal sealed class StatusDot : Control
{
    Color _dotColor = Theme.Faint;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_dotColor);
        e.Graphics.FillEllipse(brush, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
    }
}
