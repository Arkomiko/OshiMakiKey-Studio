using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OshiMakiKey.Setup;

/// <summary>Окно установки: шапка в цветах программы, папка, ярлык на рабочем столе, ход установки и запуск после неё.</summary>
internal sealed class SetupForm : Form
{
    static readonly Color Accent = Color.FromArgb(0x25, 0x63, 0xEB);
    static readonly Color AccentDark = Color.FromArgb(0x1D, 0x4E, 0xD8);

    readonly TextBox _folder = new();
    readonly CheckBox _desktop = new();
    readonly CheckBox _launch = new();
    readonly ProgressBar _progress = new();
    readonly Label _status = new();
    readonly Button _install = new();
    readonly Button _cancel = new();
    readonly Button _browse = new();
    bool _done;

    public SetupForm(string folder)
    {
        Text = $"{Installer.AppName} {Installer.Version} ({Installer.Arch}) — " + Installer.T("установка", "setup");
        Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 370);

        var header = new Header { Dock = DockStyle.Top, Height = 110 };

        var intro = new Label
        {
            Text = Installer.T("Программа установится для текущего пользователя, права администратора не нужны. Ваши настройки и пресеты при обновлении сохранятся.",
                "The program installs for the current user, no administrator rights needed. Your settings and presets are kept when updating."),
            Location = new Point(24, 126), Size = new Size(512, 40), ForeColor = Color.FromArgb(0x4B, 0x55, 0x63),
        };
        var folderLabel = new Label { Text = Installer.T("Папка установки:", "Install folder:"), Location = new Point(24, 172), AutoSize = true };
        _folder.Text = folder;
        _folder.Location = new Point(24, 194);
        _folder.Width = 420;
        _browse.Text = Installer.T("Обзор…", "Browse…");
        _browse.Location = new Point(452, 192);
        _browse.Size = new Size(84, 28);
        _browse.Click += (_, _) => Browse();

        _desktop.Text = Installer.T("Ярлык на рабочем столе", "Desktop shortcut");
        _desktop.Checked = true;
        _desktop.Location = new Point(24, 232);
        _desktop.AutoSize = true;
        _launch.Text = Installer.T("Запустить после установки", "Launch after installing");
        _launch.Checked = true;
        _launch.Location = new Point(24, 258);
        _launch.AutoSize = true;

        _progress.Location = new Point(24, 292);
        _progress.Size = new Size(512, 10);
        _progress.Visible = false;
        _status.Location = new Point(24, 306);
        _status.AutoSize = true;
        _status.ForeColor = Color.FromArgb(0x4B, 0x55, 0x63);

        _install.Text = Installer.T("Установить", "Install");
        _install.Size = new Size(120, 32);
        _install.Location = new Point(286, 326);
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.BackColor = Accent;
        _install.ForeColor = Color.White;
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Text = Installer.T("Отмена", "Cancel");
        _cancel.Size = new Size(120, 32);
        _cancel.Location = new Point(416, 326);
        _cancel.Click += (_, _) => Close();
        AcceptButton = _install;
        CancelButton = _cancel;

        Controls.AddRange(new Control[] { header, intro, folderLabel, _folder, _browse, _desktop, _launch, _progress, _status, _install, _cancel });
    }

    void Browse()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : "", ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folder.Text = Path.GetFileName(dialog.SelectedPath).Equals(Installer.AppName, StringComparison.OrdinalIgnoreCase)
                ? dialog.SelectedPath
                : Path.Combine(dialog.SelectedPath, Installer.AppName);
    }

    async Task InstallAsync()
    {
        if (_done)
        {
            if (_launch.Checked)
            {
                string exe = Path.Combine(_folder.Text, Installer.ExeName);
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _folder.Text, UseShellExecute = true })?.Dispose();
            }
            Close();
            return;
        }
        foreach (Control control in new Control[] { _folder, _browse, _desktop, _install, _cancel }) control.Enabled = false;
        _progress.Visible = true;
        _status.Text = Installer.T("Установка…", "Installing…");
        string folder = _folder.Text.Trim();
        bool desktop = _desktop.Checked;
        try
        {
            var progress = new Progress<double>(p => _progress.Value = (int)(p * 100));
            await Task.Run(() => Installer.Install(folder, desktop, p => ((IProgress<double>)progress).Report(p)));
            _done = true;
            _status.Text = Installer.T("Готово! Программа есть в меню «Пуск».", "Done! The program is in the Start menu.");
            _install.Text = Installer.T("Готово", "Finish");
            _install.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            foreach (Control control in new Control[] { _folder, _browse, _desktop, _install, _cancel }) control.Enabled = true;
            _progress.Visible = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Пока идёт установка, окно не закрываем: иначе останется половина файлов.
        if (!_install.Enabled && !_done) e.Cancel = true;
        base.OnFormClosing(e);
    }

    /// <summary>Шапка: градиент акцента, иконка, название и версия.</summary>
    sealed class Header : Control
    {
        public Header() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(ClientRectangle, Accent, AccentDark, 25f)) g.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(40, Color.White), 10))
            {
                g.DrawEllipse(pen, Width - 150, -60, 220, 220);
                g.DrawEllipse(pen, Width - 90, 40, 120, 120);
            }
            using (var icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                if (icon != null) g.DrawIcon(new Icon(icon, 64, 64), new Rectangle(24, (Height - 64) / 2, 64, 64));
            using var title = new Font("Segoe UI Semibold", 17f);
            using var body = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, Installer.AppName, title, new Point(104, Height / 2 - 30), Color.White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Installer.T($"Версия {Installer.Version} · {Installer.Arch}", $"Version {Installer.Version} · {Installer.Arch}"), body,
                new Point(106, Height / 2 + 8), Color.FromArgb(225, Color.White), TextFormatFlags.NoPrefix);
        }
    }
}
