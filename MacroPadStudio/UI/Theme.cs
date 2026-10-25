using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OshiMakiKey.UI;

/// <summary>Цвета, шрифты, масштаб под DPI и иконка приложения.</summary>
internal static class Theme
{
    public static float Scale { get; private set; } = 1f;

    public static readonly Color Window = Color.FromArgb(243, 244, 246);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(222, 225, 230);
    public static readonly Color Text = Color.FromArgb(17, 24, 39);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Faint = Color.FromArgb(156, 163, 175);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color AccentSoft = Color.FromArgb(232, 240, 254);
    public static readonly Color Hover = Color.FromArgb(243, 244, 246);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Tiny = new("Segoe UI", 7.5f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Subtitle = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font KeyNumber = new("Segoe UI Semibold", 15f);
    public static readonly Font Symbol = new("Segoe UI Symbol", 12f);
    public static readonly Font SymbolSmall = new("Segoe UI Symbol", 10f);

    /// <summary>Вызывается после включения DPI-режима, чтобы размеры в пикселях масштабировались под экран.</summary>
    public static void Init()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Scale = Math.Max(1f, g.DpiX / 96f);
    }

    public static int S(int px) => (int)Math.Round(px * Scale);

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

    static Bitmap DrawIcon(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float u = size / 32f;

        var body = new RectangleF(1 * u, 1 * u, 30 * u, 30 * u);
        using (var path = Rounded(body, 7 * u))
        using (var brush = new LinearGradientBrush(body, Color.FromArgb(59, 130, 246), AccentPressed, 90f))
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
        using var accent = new SolidBrush(AccentDark);
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
