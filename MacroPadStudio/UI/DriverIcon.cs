using System.Drawing.Drawing2D;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Значок платы: корпус, клавиши сеткой и ручки — по раскладке и цветам из файла драйвера.</summary>
internal static class DriverIcon
{
    public static Bitmap Draw(DriverDef driver, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height));
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var body = Parse(driver.Icon?.Body, Color.FromArgb(43, 47, 54));
        var keys = Parse(driver.Icon?.Keys, Color.FromArgb(243, 244, 246));
        var accent = Parse(driver.Icon?.Accent, Color.FromArgb(59, 130, 246));
        var layout = driver.Layout;
        int rows = Math.Max(1, layout.Rows), columns = Math.Max(1, layout.Columns), knobs = layout.Knobs;

        // Размеры в «клавишах»: клавиша 1, зазор 0.25, поля 0.45, ручки — столбик справа.
        const float gap = 0.25f, pad = 0.45f, knobGap = 0.2f;
        float gridW = columns + (columns - 1) * gap, gridH = rows + (rows - 1) * gap;
        float knob = knobs == 0 ? 0 : Math.Max(0.6f, Math.Min(1.15f, (Math.Max(gridH, 1.15f) - (knobs - 1) * knobGap) / knobs));
        float knobsH = knobs * knob + Math.Max(0, knobs - 1) * knobGap;
        float unitsW = gridW + 2 * pad + (knobs > 0 ? knob + 0.35f : 0);
        float unitsH = Math.Max(gridH, knobsH) + 2 * pad;
        float u = Math.Min((width - 2) / unitsW, (height - 4) / unitsH);
        var rect = new RectangleF((width - unitsW * u) / 2, (height - unitsH * u) / 2 - 1, unitsW * u, unitsH * u);

        using (var shadow = Theme.Rounded(new RectangleF(rect.X, rect.Y + 0.12f * u, rect.Width, rect.Height), 0.35f * u))
        using (var brush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            g.FillPath(brush, shadow);
        using (var path = Theme.Rounded(rect, 0.35f * u))
        using (var brush = new LinearGradientBrush(rect, Theme.Blend(body, Color.White, 0.12f), Theme.Blend(body, Color.Black, 0.18f), 90f))
            g.FillPath(brush, path);

        float gridX = rect.X + pad * u, gridY = rect.Y + (rect.Height - gridH * u) / 2;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                var key = new RectangleF(gridX + c * (1 + gap) * u, gridY + r * (1 + gap) * u, u, u);
                using var path = Theme.Rounded(key, 0.2f * u);
                using var brush = new LinearGradientBrush(key, keys, Theme.Blend(keys, Color.Black, 0.14f), 90f);
                g.FillPath(brush, path);
            }
        }

        float knobX = gridX + gridW * u + 0.35f * u, knobY = rect.Y + (rect.Height - knobsH * u) / 2;
        for (int k = 0; k < knobs; k++)
        {
            var circle = new RectangleF(knobX, knobY + k * (knob + knobGap) * u, knob * u, knob * u);
            using (var brush = new LinearGradientBrush(circle, Theme.Blend(body, Color.White, 0.35f), Theme.Blend(body, Color.Black, 0.3f), 90f))
                g.FillEllipse(brush, circle);
            using (var pen = new Pen(accent, Math.Max(1f, 0.09f * u)))
                g.DrawEllipse(pen, RectangleF.Inflate(circle, -0.08f * u, -0.08f * u));
            float dot = knob * u * 0.32f;
            using (var brush = new SolidBrush(accent))
                g.FillEllipse(brush, circle.X + (circle.Width - dot) / 2, circle.Y + (circle.Height - dot) / 2, dot, dot);
        }
        return bitmap;
    }

    static Color Parse(string? value, Color fallback) => Theme.TryParseColor(value, out var color) ? color : fallback;
}
