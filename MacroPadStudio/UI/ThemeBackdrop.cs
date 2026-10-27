using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace OshiMakiKey.UI;

/// <summary>
/// Фон окна из темы: встроенная анимированная сцена (например, «minecraft»), кадры из папки animate или картинка.
/// Окно рисует фон под всем содержимым, а панели цвета окна (Theme.Window) становятся прозрачными, чтобы фон
/// был виден в промежутках между карточками, на страницах настроек и «О программе». Карточки остаются непрозрачными.
/// </summary>
internal sealed class ThemeBackdrop : IDisposable
{
    static readonly Dictionary<Control, ThemeBackdrop> Hosts = new();

    readonly Control _host;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly HashSet<Control> _hooked = new();
    Scene? _scene;

    /// <summary>У текущей темы есть свой фон.</summary>
    public static bool Active => Scene.Has(ThemeStore.Current);

    /// <summary>Подключает или убирает фон окна по текущей теме; вызывать после смены темы и перестройки окна.</summary>
    public static void Update(Control host)
    {
        Hosts.TryGetValue(host, out var backdrop);
        if (!Active)
        {
            backdrop?.Dispose();
            return;
        }
        if (backdrop == null) Hosts[host] = backdrop = new ThemeBackdrop(host);
        backdrop.Restart();
    }

    /// <summary>Фон темы в миниатюре (для карточки темы в списке).</summary>
    public static void DrawPreview(Graphics g, ThemeDef theme, Rectangle rect)
    {
        if (!Scene.Has(theme)) return;
        using var scene = new Scene(theme, texel: 1);
        var state = g.Save();
        g.TranslateTransform(rect.X, rect.Y);
        scene.Draw(g, rect.Size, 0);
        g.Restore(state);
    }

    ThemeBackdrop(Control host)
    {
        _host = host;
        SetDoubleBuffered(host);
        host.Paint += OnHostPaint;
        host.Disposed += OnHostDisposed;
        _timer.Tick += (_, _) => Tick();
    }

    void Restart()
    {
        _scene?.Dispose();
        _scene = new Scene(ThemeStore.Current, Math.Max(2, Theme.S(3)));
        Hook(_host);
        _timer.Enabled = _scene.Animated;
        _host.Invalidate(true);
    }

    // ───────────────────────── Прозрачные панели ─────────────────────────

    void Hook(Control control)
    {
        if (_hooked.Add(control))
        {
            control.ControlAdded += OnControlAdded;
            control.BackColorChanged += OnBackColorChanged;
            control.Disposed += OnControlDisposed;
        }
        Convert(control);
        foreach (Control child in control.Controls) Hook(child);
    }

    void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) Hook(e.Control);
    }

    void OnBackColorChanged(object? sender, EventArgs e)
    {
        if (sender is Control control) Convert(control);
    }

    void OnControlDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control) Unhook(control);
    }

    void Unhook(Control control)
    {
        if (!_hooked.Remove(control)) return;
        control.ControlAdded -= OnControlAdded;
        control.BackColorChanged -= OnBackColorChanged;
        control.Disposed -= OnControlDisposed;
    }

    /// <summary>Панель цвета окна → прозрачная (видно фон); поля ввода и меню не трогаем — они не умеют прозрачность.</summary>
    void Convert(Control control)
    {
        if (control == _host) return;
        var color = control.BackColor;
        if (color.A == 255 && color.ToArgb() == Theme.Window.ToArgb() &&
            control is not (TextBoxBase or ListControl or NumericUpDown or ToolStrip or ScrollBar or WebBrowser))
        {
            try
            {
                control.BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                // Элемент не поддерживает прозрачный фон — остаётся цвета окна.
            }
        }
        if (control.BackColor.A < 255) SetDoubleBuffered(control);
    }

    static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

    // Прозрачные элементы перерисовываются с каждым кадром фона — без двойной буферизации они бы мерцали.
    static void SetDoubleBuffered(Control control) => DoubleBufferedProperty?.SetValue(control, true);

    // ───────────────────────── Кадры ─────────────────────────

    void Tick()
    {
        if (!_host.Visible || _host.FindForm() is { WindowState: FormWindowState.Minimized }) return;
        _host.Invalidate(false);
        InvalidateTransparent(_host);
    }

    // Перерисовываем только то, через что фон виден: прозрачные элементы, начиная от окна (непрозрачные — граница).
    static void InvalidateTransparent(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible || child.BackColor.A == 255) continue;
            child.Invalidate(false);
            InvalidateTransparent(child);
        }
    }

    void OnHostPaint(object? sender, PaintEventArgs e) => _scene?.Draw(e.Graphics, _host.ClientSize, _clock.Elapsed.TotalSeconds);

    void OnHostDisposed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        Hosts.Remove(_host);
        _timer.Dispose();
        _host.Paint -= OnHostPaint;
        _host.Disposed -= OnHostDisposed;
        foreach (var control in _hooked.ToList()) Unhook(control);
        _scene?.Dispose();
        _scene = null;
        if (!_host.IsDisposed) _host.Invalidate(true);
    }

    // ───────────────────────── Сцены ─────────────────────────

    /// <summary>
    /// Готовый к показу фон одной темы. Тяжёлые слои рисуются один раз под размер окна, а кадр собирается
    /// из них один раз за тик: все перерисовки прозрачных элементов в этом тике берут уже готовый кадр.
    /// </summary>
    sealed class Scene : IDisposable
    {
        readonly ThemeDef _theme;
        readonly int _texel;
        Size _size;
        Bitmap? _frame;
        double _frameTime = double.NaN;
        // Слои сцены «minecraft».
        Bitmap? _sky, _clouds, _hills, _terrain;
        (float X, float Y, float Phase)[] _stars = Array.Empty<(float, float, float)>();
        // Кадры animate и картинка, растянутые под окно.
        Bitmap?[] _scaledFrames = Array.Empty<Bitmap?>();
        Bitmap? _picture;

        public static bool Has(ThemeDef theme) => theme.Frames.Count > 0 || IsMinecraft(theme) || theme.Picture != null;

        static bool IsMinecraft(ThemeDef theme) => string.Equals(theme.Background, "minecraft", StringComparison.OrdinalIgnoreCase);

        public bool Animated => _theme.Frames.Count > 1 || IsMinecraft(_theme);

        public Scene(ThemeDef theme, int texel)
        {
            _theme = theme;
            _texel = texel;
        }

        public void Draw(Graphics g, Size size, double time)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            if (size != _size)
            {
                DisposeLayers();
                _size = size;
                _frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                _frameTime = double.NaN;
            }
            if (time != _frameTime)
            {
                using var fg = Graphics.FromImage(_frame!);
                Compose(fg, time);
                _frameTime = time;
            }
            g.DrawImageUnscaled(_frame!, 0, 0);
        }

        void Compose(Graphics g, double time)
        {
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (_theme.Frames.Count > 0)
            {
                if (_scaledFrames.Length != _theme.Frames.Count) _scaledFrames = new Bitmap?[_theme.Frames.Count];
                int index = (int)(time * Math.Max(1, _theme.AnimationFps)) % _theme.Frames.Count;
                g.DrawImageUnscaled(_scaledFrames[index] ??= Cover(_theme.Frames[index]), 0, 0);
            }
            else if (IsMinecraft(_theme)) ComposeMinecraft(g, time);
            else if (_theme.Picture != null) g.DrawImageUnscaled(_picture ??= Cover(_theme.Picture), 0, 0);
        }

        /// <summary>Картинка, заполняющая окно целиком с сохранением пропорций (лишнее обрезается).</summary>
        Bitmap Cover(Image image)
        {
            var result = new Bitmap(_size.Width, _size.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max(_size.Width / (float)image.Width, _size.Height / (float)image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            g.DrawImage(image, (_size.Width - w) / 2, (_size.Height - h) / 2, w, h);
            return result;
        }

        // ───────── Сцена «minecraft»: вечернее небо со звёздами, облака, дальние холмы и земля из блоков ─────────

        int Block => _texel * 16;

        void ComposeMinecraft(Graphics g, double time)
        {
            if (_sky == null) BuildMinecraft();
            int w = _size.Width, h = _size.Height;
            g.DrawImageUnscaled(_sky!, 0, 0);

            foreach (var (x, y, phase) in _stars)
            {
                int alpha = (int)(150 + 100 * Math.Sin(time * 1.6 + phase));
                using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 240));
                g.FillRectangle(brush, x * w, y * h, _texel, _texel);
            }

            Scroll(g, _clouds!, time * 9, (int)(h * 0.07));
            Scroll(g, _hills!, time * 5, h - Block - _hills!.Height);
            Scroll(g, _terrain!, time * 14, h - _terrain!.Height);
        }

        // Слой бесшовно повторяется по ширине и плывёт влево.
        static void Scroll(Graphics g, Bitmap layer, double offset, int y)
        {
            int x = -(int)(offset % layer.Width);
            g.DrawImageUnscaled(layer, x, y);
            g.DrawImageUnscaled(layer, x + layer.Width, y);
        }

        void BuildMinecraft()
        {
            int w = _size.Width, h = _size.Height, block = Block;
            var rnd = new Random(1189);

            _sky = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_sky))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), Color.FromArgb(0x10, 0x1A, 0x3E), Color.FromArgb(0x3D, 0x5C, 0x93), 90f))
                    g.FillRectangle(brush, 0, 0, w, h);
                // Квадратная луна с «кратерами».
                int moon = block * 5 / 4, mx = (int)(w * 0.8), my = (int)(h * 0.1);
                using (var brush = new SolidBrush(Color.FromArgb(0xF2, 0xEF, 0xD2))) g.FillRectangle(brush, mx, my, moon, moon);
                using (var brush = new SolidBrush(Color.FromArgb(0xC9, 0xC5, 0xA6)))
                {
                    g.FillRectangle(brush, mx + moon / 5, my + moon / 4, moon / 5, moon / 5);
                    g.FillRectangle(brush, mx + moon * 3 / 5, my + moon / 2, moon / 4, moon / 4);
                }
            }
            _stars = Enumerable.Range(0, 70).Select(_ => ((float)rnd.NextDouble(), (float)(rnd.NextDouble() * 0.45), (float)(rnd.NextDouble() * Math.PI * 2))).ToArray();

            // Облака: плоские белые «плиты» из клеток, раскиданные по ширине слоя.
            int cell = _texel * 4;
            int cloudWidth = Math.Max(w, block * 40) / cell * cell + cell;
            _clouds = new Bitmap(cloudWidth, (int)(h * 0.35) + cell * 4, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_clouds))
            using (var brush = new SolidBrush(Color.FromArgb(205, 255, 255, 255)))
            {
                int count = Math.Max(4, cloudWidth / (block * 6));
                for (int i = 0; i < count; i++)
                {
                    int cw = rnd.Next(6, 15), ch = rnd.Next(2, 4);
                    int cx = rnd.Next(cloudWidth / cell), cy = rnd.Next(0, Math.Max(1, (_clouds.Height - ch * cell) / cell));
                    for (int yy = 0; yy < ch; yy++)
                    {
                        int inset = yy == 0 || yy == ch - 1 ? rnd.Next(0, 3) : 0, width = cw - inset - rnd.Next(0, 3);
                        for (int xx = inset; xx < width; xx++)
                        {
                            int px = (cx + xx) % (cloudWidth / cell);
                            g.FillRectangle(brush, px * cell, (cy + yy) * cell, cell, cell);
                        }
                    }
                }
            }

            // Дальние холмы: ступенчатый силуэт, повторяющийся без шва.
            int step = block / 2;
            int columns = Math.Max(w, block * 48) / step + 1;
            _hills = new Bitmap(columns * step, block * 6, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_hills))
            using (var body = new SolidBrush(Color.FromArgb(0x1C, 0x33, 0x34)))
            using (var top = new SolidBrush(Color.FromArgb(0x27, 0x47, 0x40)))
            {
                for (int i = 0; i < columns; i++)
                {
                    double a = 2 * Math.PI * i / columns;
                    int height = (int)Math.Round(5 + 2 * Math.Sin(a * 3 + 0.7) + 1.2 * Math.Sin(a * 7 + 2.1)) * step + step;
                    int y = _hills.Height - height;
                    g.FillRectangle(body, i * step, y, step, height);
                    g.FillRectangle(top, i * step, y, step, _texel * 2);
                }
            }

            // Земля: столбцы блоков (трава, земля, камень) и редкие деревья.
            using var grass = Texture("grass_side", Color.FromArgb(0x5F, 0xA8, 0x3A), Color.FromArgb(0x86, 0x60, 0x43));
            using var dirt = Texture("dirt", Color.FromArgb(0x86, 0x60, 0x43));
            using var stone = Texture("stone", Color.FromArgb(0x7F, 0x7F, 0x7F));
            using var log = Texture("log", Color.FromArgb(0x6B, 0x51, 0x34));
            using var leaves = Texture("leaves", Color.FromArgb(0x3E, 0x7A, 0x2A));
            int blocks = Math.Max(w, block * 40) / block + 1;
            int rows = 5, treeRows = 5;
            _terrain = new Bitmap(blocks * block, (rows + treeRows) * block, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_terrain))
            using (var dim = new ImageAttributes())
            {
                // Вечер: земля чуть темнее текстур.
                dim.SetColorMatrix(new ColorMatrix(new[]
                {
                    new[] { 0.72f, 0, 0, 0, 0 }, new[] { 0, 0.72f, 0, 0, 0 }, new[] { 0, 0, 0.8f, 0, 0 }, new[] { 0, 0, 0, 1f, 0 }, new[] { 0, 0, 0, 0, 1f },
                }));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                void Put(Image texture, int column, int row) =>
                    g.DrawImage(texture, new Rectangle(column * block, row * block, block, block), 0, 0, texture.Width, texture.Height, GraphicsUnit.Pixel, dim);

                var heights = new int[blocks];
                for (int i = 0; i < blocks; i++)
                {
                    double a = 2 * Math.PI * i / blocks;
                    heights[i] = Math.Clamp((int)Math.Round(2.6 + 1.1 * Math.Sin(a * 2 + 0.4) + 0.7 * Math.Sin(a * 5 + 1.3)), 1, rows);
                }
                int total = rows + treeRows;
                for (int i = 0; i < blocks; i++)
                {
                    int topRow = total - heights[i];
                    Put(grass, i, topRow);
                    for (int r = topRow + 1; r < total; r++) Put(r - topRow <= 2 ? dirt : stone, i, r);
                }
                // Деревья: ствол в 3 блока и крона 3×2 сверху; не у самого края, чтобы крона не резалась швом.
                for (int i = 3; i < blocks - 3; i += rnd.Next(7, 13))
                {
                    int ground = total - heights[i];
                    for (int r = 1; r <= 3; r++) Put(log, i, ground - r);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int r = 4; r <= 5; r++) Put(leaves, i + dx, ground - r);
                    Put(leaves, i, ground - 6);
                }
            }
        }

        /// <summary>Текстура блока из img пакета темы; нет файла — простая заливка (сверху полоса травы, если задана).</summary>
        Bitmap Texture(string name, Color color, Color? below = null)
        {
            if (_theme.Images.TryGetValue(name, out var image)) return new Bitmap(image);
            var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);
            using (var brush = new SolidBrush(below ?? color)) g.FillRectangle(brush, 0, 0, 16, 16);
            if (below != null)
                using (var brush = new SolidBrush(color)) g.FillRectangle(brush, 0, 0, 16, 4);
            return bitmap;
        }

        void DisposeLayers()
        {
            foreach (var bitmap in new[] { _frame, _sky, _clouds, _hills, _terrain, _picture }) bitmap?.Dispose();
            foreach (var bitmap in _scaledFrames) bitmap?.Dispose();
            _frame = _sky = _clouds = _hills = _terrain = _picture = null;
            _scaledFrames = Array.Empty<Bitmap?>();
        }

        public void Dispose() => DisposeLayers();
    }
}
