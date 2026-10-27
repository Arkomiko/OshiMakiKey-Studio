using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OshiMakiKey.UI;

/// <summary>
/// Плавная смена содержимого окна или страницы. Поверх кладётся снимок «до», содержимое перестраивается под ним
/// (промежуточная раскладка и поэтапная перерисовка не видны), затем снимок за доли секунды растворяется в новом виде.
/// </summary>
internal static class Transition
{
    const int DurationMs = 170;

    /// <summary>Выполняет <paramref name="change"/> под плавным переходом; окно ещё не показано — просто выполняет.</summary>
    public static void Run(Control host, Action change)
    {
        if (!host.IsHandleCreated || !host.Visible || host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
        {
            change();
            return;
        }
        // Переход уже идёт (например, закрыли страницу и сразу сменили тему) — растворяем тот же снимок «до» в новом виде.
        var cover = host.Controls.OfType<Cover>().FirstOrDefault();
        if (cover == null)
        {
            cover = new Cover(Capture(host, null)) { Bounds = host.ClientRectangle };
            host.Controls.Add(cover);
            cover.BringToFront();
            cover.Update();
        }
        try
        {
            change();
        }
        finally
        {
            if (!cover.IsDisposed)
            {
                cover.Bounds = host.ClientRectangle;
                cover.BringToFront();
                cover.Start(Capture(host, cover));
            }
        }
    }

    /// <summary>Служебный слой перехода — его не трогают перестройка окна и скрытие содержимого.</summary>
    public static bool IsCover(Control control) => control is Cover;

    /// <summary>Снимок клиентской части: фон и видимые дочерние элементы (кроме <paramref name="skip"/>) снизу вверх.</summary>
    public static Bitmap Capture(Control host, Control? skip)
    {
        var size = host.ClientSize;
        var shot = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(shot)) g.Clear(host.BackColor);
        foreach (var child in host.Controls.Cast<Control>().Reverse())
        {
            if (child == skip || !child.Visible || child.Width <= 0 || child.Height <= 0) continue;
            using var part = new Bitmap(child.Width, child.Height, PixelFormat.Format32bppPArgb);
            child.DrawToBitmap(part, new Rectangle(Point.Empty, child.Size));
            using var g = Graphics.FromImage(shot);
            g.DrawImageUnscaled(part, child.Left, child.Top);
        }
        return shot;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    /// <summary>Слой со снимком: сначала показывает «до», затем растворяет его в «после» и убирает себя.</summary>
    sealed class Cover : Control
    {
        readonly Bitmap _before;
        Bitmap? _after;
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        readonly Stopwatch _clock = new();
        float _t;

        public Cover(Bitmap before)
        {
            _before = before;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _timer.Tick += (_, _) => Step();
        }

        public void Start(Bitmap after)
        {
            _after?.Dispose();
            _after = after;
            _t = 0;
            _clock.Restart();
            _timer.Start();
            Invalidate();
        }

        void Step()
        {
            float p = Math.Min(1f, _clock.ElapsedMilliseconds / (float)DurationMs);
            _t = 1 - (1 - p) * (1 - p);
            if (p < 1)
            {
                Invalidate();
                return;
            }
            _timer.Stop();
            Finish();
        }

        // Убираем слой одним кадром: перерисовка окна под ним выключена, затем всё рисуется сразу и целиком.
        void Finish()
        {
            var host = Parent;
            if (host == null || !host.IsHandleCreated)
            {
                Dispose();
                return;
            }
            SendMessage(host.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            host.Controls.Remove(this);
            SendMessage(host.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
            // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME
            RedrawWindow(host.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
            Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            if (_after == null || _t <= 0)
            {
                g.DrawImageUnscaled(_before, 0, 0);
                return;
            }
            g.DrawImageUnscaled(_after, 0, 0);
            using var fade = new ImageAttributes();
            fade.SetColorMatrix(new ColorMatrix { Matrix33 = 1 - _t });
            g.DrawImage(_before, new Rectangle(Point.Empty, _before.Size), 0, 0, _before.Width, _before.Height, GraphicsUnit.Pixel, fade);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _before.Dispose();
                _after?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
