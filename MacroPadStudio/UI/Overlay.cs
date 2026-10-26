namespace OshiMakiKey.UI;

/// <summary>
/// Диалог «внутри окна»: форма без рамки ложится ровно на клиентскую часть главного окна, показывает его затемнённый
/// снимок и карточку с содержимым по центру. Для пользователя это выглядит как часть того же окна, а код остаётся
/// обычным модальным ShowDialog (результат возвращается сразу, без асинхронности).
/// </summary>
internal static class Overlay
{
    /// <summary>Настраивает <paramref name="dialog"/>: без владельца-окна остаётся обычным окном с карточкой.</summary>
    public static void Prepare(Form dialog, Control card, IWin32Window? owner)
    {
        card.BackColor = Theme.Surface;
        dialog.Controls.Add(card);
        dialog.ShowInTaskbar = false;
        dialog.KeyPreview = true;

        if (owner is Form host && host.Visible && host.WindowState != FormWindowState.Minimized && host.ClientSize.Width > 0)
        {
            dialog.FormBorderStyle = FormBorderStyle.None;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Bounds = host.RectangleToScreen(host.ClientRectangle);
            dialog.BackgroundImage = DimmedSnapshot(host);
            dialog.BackgroundImageLayout = ImageLayout.None;
            void Center(object? sender, EventArgs e) => card.Location = new Point(
                Math.Max(0, (dialog.ClientSize.Width - card.Width) / 2), Math.Max(0, (dialog.ClientSize.Height - card.Height) / 2));
            dialog.Load += Center;
            card.SizeChanged += Center;
            dialog.FormClosed += (_, _) => dialog.BackgroundImage?.Dispose();
        }
        else
        {
            // Снимки окон и фоновая служба: обычное небольшое окно по размеру карточки.
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.MaximizeBox = dialog.MinimizeBox = false;
            dialog.AutoSize = true;
            dialog.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
    }

    /// <summary>Снимок клиентской части окна, затемнённый как фон под диалогом.</summary>
    static Bitmap DimmedSnapshot(Form host)
    {
        var shot = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
        using var full = new Bitmap(Math.Max(1, host.Width), Math.Max(1, host.Height));
        host.DrawToBitmap(full, new Rectangle(Point.Empty, host.Size));
        var origin = host.PointToScreen(Point.Empty);
        using (var g = Graphics.FromImage(shot))
        {
            g.DrawImage(full, -(origin.X - host.Left), -(origin.Y - host.Top));
            using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, shot.Width, shot.Height);
        }
        return shot;
    }

    /// <summary>Карточка диалога: рамка цвета темы и отступы.</summary>
    public static Panel Card(Control content)
    {
        var card = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(1), BackColor = Theme.Border };
        var inner = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, BackColor = Theme.Surface };
        inner.Controls.Add(content);
        card.Controls.Add(inner);
        return card;
    }
}
