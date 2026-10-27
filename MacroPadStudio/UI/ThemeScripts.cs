using System.Diagnostics;
using System.Drawing.Drawing2D;
using Jint;
using Jint.Native;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>
/// Скрипты темы (папка scripts пакета, *.js) в песочнице: встроенный интерпретатор JavaScript без доступа к файлам,
/// сети, процессам и .NET — скрипту видны только объект <c>theme</c> и холст для рисования. На каждый вызов действуют
/// ограничения по времени, памяти, глубине рекурсии и числу операций; упавший или зависший скрипт отключается
/// с уведомлением, программа продолжает работать.
///
/// API для скрипта:
///   theme.onFrame(function (g, t, w, h) { … })   — рисовать поверх фона темы каждый кадр (t — секунды);
///   theme.addWindow({ title, menu, width, height, onFrame(g, t, w, h), onClick(x, y) }) — своё окно (пункт в «Справке»);
///   theme.hour(), theme.minute()                 — часы компьютера; theme.log(текст) — строка в уведомлениях.
/// Холст g: rect(x, y, w, h, '#RRGGBB'), circle(x, y, r, цвет), line(x1, y1, x2, y2, цвет, толщина),
///   text(строка, x, y, цвет, размер), image('имя из img', x, y, w, h), width, height.
/// </summary>
internal static class ThemeScripts
{
    const int MaxScriptChars = 256 * 1024;

    static Engine? _engine;
    static ThemeDef? _theme;
    static readonly List<JsValue> FrameHandlers = new();
    static readonly List<WindowDef> WindowDefs = new();

    /// <summary>Окно, объявленное скриптом темы.</summary>
    internal sealed record WindowDef(string Title, string Menu, int Width, int Height, JsValue? OnFrame, JsValue? OnClick);

    /// <summary>Окна текущей темы — для пунктов меню.</summary>
    public static IReadOnlyList<WindowDef> Windows => WindowDefs;

    /// <summary>Скрипт рисует поверх фона — фону нужна анимация.</summary>
    public static bool HasFrame => FrameHandlers.Count > 0;

    /// <summary>Запускает скрипты темы (прежние останавливаются). Вызывается при каждом включении темы.</summary>
    public static void Start(ThemeDef theme)
    {
        Stop();
        if (theme.Scripts.Count == 0) return;
        _theme = theme;
        _engine = new Engine(options => options
            .Strict()
            .LimitRecursion(64)
            .LimitMemory(32 * 1024 * 1024)
            .MaxStatements(200_000)
            .TimeoutInterval(TimeSpan.FromMilliseconds(250)));
        _engine.SetValue("theme", new Api());
        foreach (var (name, source) in theme.Scripts.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (source.Length > MaxScriptChars) continue;
            if (!Safe(name, () => _engine.Execute(source, name))) break;
        }
    }

    public static void Stop()
    {
        FrameHandlers.Clear();
        WindowDefs.Clear();
        _engine?.Dispose();
        _engine = null;
        _theme = null;
    }

    /// <summary>Рисует обработчики onFrame поверх готового кадра фона.</summary>
    public static void DrawFrame(Graphics g, Size size, double time)
    {
        if (_engine == null || FrameHandlers.Count == 0) return;
        using var canvas = new Canvas(g, size, _theme);
        foreach (var handler in FrameHandlers.ToList())
            if (!Safe("onFrame", () => _engine.Invoke(handler, canvas, time, size.Width, size.Height)))
                FrameHandlers.Remove(handler);
    }

    /// <summary>Открывает окно, объявленное скриптом.</summary>
    public static void OpenWindow(WindowDef def, IWin32Window? owner)
    {
        var window = new ScriptWindow(def);
        if (owner != null) window.Show(owner);
        else window.Show();
    }

    // Любая ошибка скрипта (исключение, тайм-аут, лимит памяти) отключает его, но не роняет программу.
    static bool Safe(string what, Action call)
    {
        if (_engine == null) return false;
        try
        {
            _engine.Constraints.Reset();
            call();
            return true;
        }
        catch (Exception ex)
        {
            Notices.Add(NoticeLevel.Warning, L.F("notice.themeScript", "Скрипт темы «{0}» ({1}) остановлен: {2}", _theme?.Title ?? "", what, ex.Message));
            return false;
        }
    }

    static string Text(JsValue value, string key, string fallback)
    {
        var item = value.IsObject() ? value.AsObject().Get(key) : JsValue.Undefined;
        return item.IsString() ? item.AsString() : fallback;
    }

    static int Number(JsValue value, string key, int fallback, int min, int max)
    {
        var item = value.IsObject() ? value.AsObject().Get(key) : JsValue.Undefined;
        return item.IsNumber() ? Math.Clamp((int)item.AsNumber(), min, max) : fallback;
    }

    static JsValue? Function(JsValue value, string key)
    {
        var item = value.IsObject() ? value.AsObject().Get(key) : JsValue.Undefined;
        return item.IsUndefined() || item.IsNull() ? null : item;
    }

    /// <summary>Объект theme, видимый скрипту. Имена — в стиле JavaScript.</summary>
    public sealed class Api
    {
        public void onFrame(JsValue handler) => FrameHandlers.Add(handler);

        public void addWindow(JsValue def)
        {
            string title = Text(def, "title", _theme?.Title ?? "");
            WindowDefs.Add(new WindowDef(title, Text(def, "menu", title), Number(def, "width", 420, 200, 1200), Number(def, "height", 300, 150, 900),
                Function(def, "onFrame"), Function(def, "onClick")));
        }

        public int hour() => ThemeBackdrop.HourOverride ?? DateTime.Now.Hour;

        public int minute() => DateTime.Now.Minute;

        public void log(string text) => Notices.Add(NoticeLevel.Info, $"{_theme?.Title}: {text}");
    }

    /// <summary>Холст для скрипта: только рисование, без доступа к чему-либо ещё.</summary>
    public sealed class Canvas : IDisposable
    {
        readonly Graphics _g;
        readonly ThemeDef? _theme;
        readonly Dictionary<int, Font> _fonts = new();

        public Canvas(Graphics g, Size size, ThemeDef? theme)
        {
            _g = g;
            _theme = theme;
            width = size.Width;
            height = size.Height;
            _g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        public double width { get; }
        public double height { get; }

        static Color Parse(string color) => Theme.TryParseColor(color, out var parsed) ? parsed : Color.Transparent;

        public void rect(double x, double y, double w, double h, string color)
        {
            using var brush = new SolidBrush(Parse(color));
            _g.FillRectangle(brush, (float)x, (float)y, (float)w, (float)h);
        }

        public void circle(double x, double y, double r, string color)
        {
            using var brush = new SolidBrush(Parse(color));
            _g.FillEllipse(brush, (float)(x - r), (float)(y - r), (float)(r * 2), (float)(r * 2));
        }

        public void line(double x1, double y1, double x2, double y2, string color) => line(x1, y1, x2, y2, color, 1);

        public void line(double x1, double y1, double x2, double y2, string color, double thickness)
        {
            using var pen = new Pen(Parse(color), (float)Math.Clamp(thickness, 0.5, 50));
            _g.DrawLine(pen, (float)x1, (float)y1, (float)x2, (float)y2);
        }

        public void text(string value, double x, double y, string color) => text(value, x, y, color, 12);

        public void text(string value, double x, double y, string color, double size)
        {
            int key = (int)Math.Clamp(size, 6, 72);
            if (!_fonts.TryGetValue(key, out var font)) _fonts[key] = font = new Font(Theme.Body.FontFamily, key, GraphicsUnit.Pixel);
            TextRenderer.DrawText(_g, value.Length > 500 ? value[..500] : value, font, new Point((int)x, (int)y), Parse(color), TextFormatFlags.NoPrefix);
        }

        public void image(string name, double x, double y, double w, double h)
        {
            if (_theme == null || !_theme.Images.TryGetValue(name, out var picture)) return;
            var mode = _g.InterpolationMode;
            _g.InterpolationMode = InterpolationMode.NearestNeighbor;
            _g.PixelOffsetMode = PixelOffsetMode.Half;
            _g.DrawImage(picture, (float)x, (float)y, (float)w, (float)h);
            _g.InterpolationMode = mode;
        }

        public void Dispose()
        {
            foreach (var font in _fonts.Values) font.Dispose();
        }
    }

    /// <summary>Окно, объявленное скриптом: холст с анимацией и щелчками, в цветах темы.</summary>
    sealed class ScriptWindow : Form
    {
        readonly WindowDef _def;
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly ThemeDef? _owner = _theme;

        public ScriptWindow(WindowDef def)
        {
            _def = def;
            Text = def.Title;
            Icon = Theme.AppIcon;
            BackColor = Theme.Window;
            ClientSize = new Size(Theme.S(def.Width), Theme.S(def.Height));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            DoubleBuffered = true;
            Theme.ApplyTo(this);
            _timer.Tick += (_, _) => Invalidate();
            if (def.OnFrame != null) _timer.Start();
        }

        // Тему сменили — окно старой темы закрывается: её скрипты уже остановлены.
        bool Alive => _engine != null && _theme == _owner;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Alive)
            {
                BeginInvoke(Close);
                return;
            }
            if (_def.OnFrame is not { } frame) return;
            using var canvas = new Canvas(e.Graphics, ClientSize, _theme);
            double t = _clock.Elapsed.TotalSeconds;
            if (!Safe(_def.Title, () => _engine!.Invoke(frame, canvas, t, ClientSize.Width, ClientSize.Height))) _timer.Stop();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (Alive && _def.OnClick is { } click) Safe(_def.Title, () => _engine!.Invoke(click, e.X, e.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
