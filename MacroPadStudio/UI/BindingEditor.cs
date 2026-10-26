using System.ComponentModel;
using OshiMakiKey.Model;

namespace OshiMakiKey.UI;

/// <summary>Правая панель: настройка действия выбранной кнопки или ручки.</summary>
internal sealed class BindingEditor : UserControl
{
    // Подписи берутся при создании редактора: смена языка пересоздаёт окно целиком.
    readonly (ActionKind Kind, string Text)[] _kindButtonTexts =
    {
        (ActionKind.None, L.T("kind.none", "Нет действия")),
        (ActionKind.Keys, L.T("kind.keys", "Клавиши")),
        (ActionKind.Media, L.T("kind.media", "Мультимедиа")),
        (ActionKind.Mouse, L.T("kind.mouse", "Мышь")),
        (ActionKind.System, L.T("kind.system", "Система")),
    };

    readonly Choice<MouseActionKind>[] _mouseChoices = Enum.GetValues<MouseActionKind>()
        .Select(k => new Choice<MouseActionKind>(k, BindingText.MouseName(k))).ToArray();

    readonly Choice<MouseModifier>[] _mouseModifierChoices =
    {
        new(MouseModifier.None, L.T("mouse.modifier.none", "— ничего")),
        new(MouseModifier.Ctrl, "Ctrl"),
        new(MouseModifier.Shift, "Shift"),
        new(MouseModifier.Alt, "Alt"),
    };

    readonly Choice<SystemActionKind>[] _systemChoices = Enum.GetValues<SystemActionKind>()
        .Select(k => new Choice<SystemActionKind>(k, BindingText.SystemName(k))).ToArray();

    PadAction? _action;
    bool _loading;

    readonly List<Label> _wrapLabels = new();
    readonly Label _title = new(), _subtitle = new(), _execution = new();
    readonly List<RadioButton> _kindButtons = new();
    readonly Panel _host = new() { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Surface };
    readonly Dictionary<ActionKind, Control> _pages = new();

    // Клавиши
    readonly FlowLayoutPanel _chordList = new();
    readonly Button _addChord = new();
    Label _multiHint = null!;

    // Мультимедиа
    readonly ListBox _mediaList = new();

    // Мышь
    readonly ThemedComboBox _mouseAction = new(), _mouseModifier = new();
    readonly NumericUpDown _wheelStep = new();
    Label _wheelLabel = null!;

    // Система
    readonly ThemedComboBox _systemKind = new();
    readonly TextBox _target = new(), _arguments = new(), _text = new();
    readonly Button _browseFile = new(), _browseFolder = new(), _test = new();
    readonly FlowLayoutPanel _browseButtons = new();
    Label _systemDescription = null!, _targetLabel = null!, _argumentsLabel = null!, _textLabel = null!;

    public event EventHandler? ActionChanged;
    public event EventHandler? TestRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PadAction? Action => _action;

    public BindingEditor()
    {
        BackColor = Theme.Surface;
        Font = Theme.Body;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), Theme.S(16)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _title.AutoSize = true;
        _title.Font = Theme.Title;
        _title.ForeColor = Theme.Text;
        _title.Margin = new Padding(0);

        _subtitle.AutoSize = true;
        _subtitle.ForeColor = Theme.Muted;
        _subtitle.Margin = new Padding(0, Theme.S(4), 0, Theme.S(16));
        _wrapLabels.Add(_subtitle);

        var kindRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0, Theme.S(6), 0, Theme.S(14)),
        };
        foreach (var (kind, text) in _kindButtonTexts)
        {
            var button = MakeKindButton(kind, text);
            _kindButtons.Add(button);
            kindRow.Controls.Add(button);
        }

        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_subtitle, 0, 1);
        root.Controls.Add(Caption(L.T("editor.what", "Что делать:")), 0, 2);
        root.Controls.Add(kindRow, 0, 3);

        _execution.AutoSize = true;
        _execution.Padding = new Padding(Theme.S(10), Theme.S(7), Theme.S(10), Theme.S(7));
        _execution.Margin = new Padding(0, 0, 0, Theme.S(14));
        _wrapLabels.Add(_execution);
        root.Controls.Add(_execution, 0, 4);
        root.Controls.Add(_host, 0, 5);
        Controls.Add(root);

        _pages[ActionKind.None] = BuildNonePage();
        _pages[ActionKind.Keys] = BuildKeysPage();
        _pages[ActionKind.Media] = BuildMediaPage();
        _pages[ActionKind.Mouse] = BuildMousePage();
        _pages[ActionKind.System] = BuildSystemPage();
        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _host.Controls.Add(page);
        }

        _host.Resize += (_, _) => UpdateWrapWidth();
        Theme.Style(this);
    }

    public void LoadAction(ControlId id, PadAction action)
    {
        _loading = true;
        try
        {
            _action = action;
            _title.Text = UiText.Title(id);
            _subtitle.Text = UiText.Subtitle(id);
            if (action.Kind == ActionKind.Keys && action.Chords.Count == 0) action.Chords.Add(new KeyChord());
            foreach (var button in _kindButtons) button.Checked = (ActionKind)button.Tag! == action.Kind;
            LoadKeys();
            LoadMedia();
            LoadMouse();
            LoadSystem();
            ShowPage(action.Kind);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Показывает, кто выполняет действие: само устройство или фоновый агент программы.</summary>
    public void SetExecution(bool viaAgent)
    {
        if (_action == null || _action.Kind == ActionKind.None)
        {
            _execution.Visible = false;
            return;
        }
        _execution.Visible = true;
        if (viaAgent)
        {
            string trigger = _action.TriggerSlot is int slot
                ? $"«{TriggerKeys.Describe(slot)}»"
                : L.T("editor.agent.anyTrigger", "служебную клавишу");
            _execution.Text = L.F("editor.agent",
                "⚙  Выполняет {0} в фоне: устройство отправит {1}, программа сделает остальное. Программа должна быть запущена (значок в трее).",
                Branding.AppName, trigger);
            _execution.BackColor = Theme.WarningSoft;
            _execution.ForeColor = Theme.WarningText;
        }
        else
        {
            _execution.Text = L.T("editor.native", "✓  Записывается в память устройства — работает даже без программы, на любом компьютере.");
            _execution.BackColor = Theme.SuccessSoft;
            _execution.ForeColor = Theme.SuccessText;
        }
    }

    void Raise()
    {
        if (!_loading) ActionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ───────────────────────── Общие элементы ─────────────────────────

    RadioButton MakeKindButton(ActionKind kind, string text)
    {
        var button = new RadioButton
        {
            Appearance = Appearance.Button,
            Text = text,
            Tag = kind,
            AutoSize = true,
            MinimumSize = new Size(0, Theme.S(36)),
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, Theme.S(8), Theme.S(8)),
            Cursor = Cursors.Hand,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.CheckedBackColor = Theme.Accent;
        button.FlatAppearance.MouseOverBackColor = Theme.Hover;
        button.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
        button.CheckedChanged += (_, _) =>
        {
            button.ForeColor = button.Checked ? Theme.OnAccent : Theme.Text;
            button.FlatAppearance.BorderColor = button.Checked ? Theme.Accent : Theme.Border;
            button.FlatAppearance.MouseOverBackColor = button.Checked ? Theme.AccentDark : Theme.Hover;
            if (button.Checked) OnKindSelected(kind);
        };
        return button;
    }

    void OnKindSelected(ActionKind kind)
    {
        if (_action == null || _loading) return;
        _action.Kind = kind;
        switch (kind)
        {
            case ActionKind.Keys:
                if (_action.Chords.Count == 0) _action.Chords.Add(new KeyChord());
                LoadKeys();
                break;
            case ActionKind.Media:
                if (MediaCatalog.Find(_action.MediaCode) == null) _action.MediaCode = MediaCatalog.All[0].Code;
                LoadMedia();
                break;
        }
        ShowPage(kind);
        Raise();
        if (kind == ActionKind.Keys && _chordList.Controls.Count > 0 && _chordList.Controls[0] is ChordRow row && _action.Chords[0].IsEmpty)
            row.FocusCapture();
    }

    void ShowPage(ActionKind kind)
    {
        foreach (var (pageKind, page) in _pages) page.Visible = pageKind == kind;
    }

    static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Strong,
        ForeColor = Theme.Text,
        Margin = new Padding(0, 0, 0, Theme.S(2)),
    };

    static Label FieldCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, Theme.S(6), Theme.S(14), Theme.S(6)),
    };

    Label Hint(string text, int top = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, Theme.S(top), 0, Theme.S(10)),
        };
        _wrapLabels.Add(label);
        return label;
    }

    Label InfoBox(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            BackColor = Theme.AccentSoft,
            ForeColor = Theme.AccentText,
            Padding = new Padding(Theme.S(12), Theme.S(10), Theme.S(12), Theme.S(10)),
            Margin = new Padding(0, 0, 0, Theme.S(14)),
        };
        _wrapLabels.Add(label);
        return label;
    }

    static void StyleSmallButton(Button button, Color foreColor, Color hoverColor)
    {
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.Surface;
        button.ForeColor = foreColor;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(Theme.S(6), Theme.S(2), Theme.S(6), Theme.S(2));
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = hoverColor;
    }

    static TableLayoutPanel Grid(int columns, int rows)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = columns,
            RowCount = rows,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoScroll = true,
        };
        for (int i = 0; i < rows - 1; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return grid;
    }

    static void SelectValue<T>(ComboBox box, T value)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    void UpdateWrapWidth()
    {
        int width = Math.Max(Theme.S(220), _host.ClientSize.Width - Theme.S(24));
        foreach (var label in _wrapLabels) label.MaximumSize = new Size(width, 0);
    }

    // ───────────────────────── Нет действия ─────────────────────────

    Control BuildNonePage()
    {
        var page = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        page.Controls.Add(Hint(L.T("editor.none.hint", "Эта кнопка ничего не будет делать. Выберите тип действия выше, чтобы назначить её.")));
        return page;
    }

    // ───────────────────────── Клавиши ─────────────────────────

    Control BuildKeysPage()
    {
        var page = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Surface,
        };
        page.Controls.Add(Hint(L.T("editor.keys.hint", "Сочетание клавиш или последовательность до 5 нажатий подряд, например Ctrl + C, затем Ctrl + V.")));

        _chordList.FlowDirection = FlowDirection.TopDown;
        _chordList.WrapContents = false;
        _chordList.AutoSize = true;
        _chordList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _chordList.Margin = new Padding(0, Theme.S(6), 0, Theme.S(2));
        page.Controls.Add(_chordList);

        _addChord.Text = L.T("editor.keys.add", "+  Добавить нажатие");
        StyleSmallButton(_addChord, Theme.Accent, Theme.AccentSoft);
        _addChord.Margin = new Padding(0, 0, 0, Theme.S(12));
        _addChord.Click += (_, _) => AddChord();
        page.Controls.Add(_addChord);

        _multiHint = Hint(L.T("editor.keys.multi", "На некоторых моделях модификаторы (Ctrl, Shift, Alt, Win) срабатывают только в первом нажатии последовательности."));
        page.Controls.Add(_multiHint);
        page.Controls.Add(Hint(L.T("editor.keys.how", "Как записать: щёлкните по полю с комбинацией и нажмите нужные клавиши. Сочетания, которые перехватывает сама Windows (например Win + L), отметьте флажками и выберите клавишу из списка."), top: 4));
        return page;
    }

    void LoadKeys()
    {
        _chordList.SuspendLayout();
        foreach (var control in _chordList.Controls.Cast<Control>().ToList()) control.Dispose();
        _chordList.Controls.Clear();

        if (_action != null)
        {
            for (int i = 0; i < _action.Chords.Count; i++)
            {
                int index = i;
                var row = new ChordRow(_action.Chords[i]);
                row.Changed += (_, _) => Raise();
                row.RemoveRequested += (_, _) => BeginInvoke(() => RemoveChord(index));
                _chordList.Controls.Add(row);
            }
            _addChord.Enabled = _action.Chords.Count < PadAction.MaxChords;
            _multiHint.Visible = _action.Chords.Count > 1;
        }
        _chordList.ResumeLayout();
    }

    void AddChord()
    {
        if (_action == null || _action.Chords.Count >= PadAction.MaxChords) return;
        _action.Chords.Add(new KeyChord());
        LoadKeys();
        Raise();
        if (_chordList.Controls[_chordList.Controls.Count - 1] is ChordRow row) row.FocusCapture();
    }

    void RemoveChord(int index)
    {
        if (_action == null || index >= _action.Chords.Count) return;
        if (_action.Chords.Count > 1) _action.Chords.RemoveAt(index);
        else _action.Chords[0] = new KeyChord();
        LoadKeys();
        Raise();
    }

    // ───────────────────────── Мультимедиа ─────────────────────────

    Control BuildMediaPage()
    {
        var page = Grid(1, 2);
        page.AutoScroll = false;
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(Hint(L.T("editor.media.hint", "Музыка, громкость, яркость экрана и быстрый запуск — как на мультимедийной клавиатуре.")), 0, 0);

        _mediaList.Dock = DockStyle.Fill;
        _mediaList.BorderStyle = BorderStyle.FixedSingle;
        _mediaList.IntegralHeight = false;
        _mediaList.DrawMode = DrawMode.OwnerDrawFixed;
        _mediaList.ItemHeight = Theme.S(30);
        _mediaList.Margin = new Padding(0, Theme.S(2), 0, 0);
        foreach (var media in MediaCatalog.All) _mediaList.Items.Add(media);
        _mediaList.DrawItem += DrawMediaItem;
        _mediaList.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _action == null || _mediaList.SelectedItem is not MediaInfo media) return;
            _action.MediaCode = media.Code;
            Raise();
        };
        page.Controls.Add(_mediaList, 0, 1);
        return page;
    }

    void DrawMediaItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var media = (MediaInfo)_mediaList.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(selected ? Theme.AccentSoft : Theme.Input)) e.Graphics.FillRectangle(brush, e.Bounds);
        if (selected)
            using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
        var textRect = new Rectangle(e.Bounds.X + Theme.S(14), e.Bounds.Y, e.Bounds.Width - Theme.S(20), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, media.Name, selected ? Theme.Strong : Theme.Body, textRect,
            selected ? Theme.AccentText : Theme.InputText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void LoadMedia()
    {
        int index = -1;
        for (int i = 0; i < _mediaList.Items.Count; i++)
            if (_action != null && ((MediaInfo)_mediaList.Items[i]).Code == _action.MediaCode) index = i;
        _mediaList.SelectedIndex = index;
    }

    // ───────────────────────── Мышь ─────────────────────────

    Control BuildMousePage()
    {
        var page = Grid(2, 6);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var hint = Hint(L.T("editor.mouse.hint", "Щелчок кнопкой мыши или прокрутка колеса."));
        page.Controls.Add(hint, 0, 0);
        page.SetColumnSpan(hint, 2);

        _mouseAction.DropDownStyle = ComboBoxStyle.DropDownList;
        _mouseAction.Width = Theme.S(280);
        _mouseAction.Anchor = AnchorStyles.Left;
        _mouseAction.Items.AddRange(_mouseChoices);
        _mouseAction.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _action == null || _mouseAction.SelectedItem is not Choice<MouseActionKind> choice) return;
            _action.Mouse = choice.Value;
            UpdateMouseFields();
            Raise();
        };
        page.Controls.Add(FieldCaption(L.T("editor.action", "Действие:")), 0, 1);
        page.Controls.Add(_mouseAction, 1, 1);

        _wheelLabel = FieldCaption(L.T("editor.mouse.step", "Шаг колеса:"));
        _wheelStep.Minimum = 1;
        _wheelStep.Maximum = 20;
        _wheelStep.Width = Theme.S(90);
        _wheelStep.Anchor = AnchorStyles.Left;
        _wheelStep.ValueChanged += (_, _) =>
        {
            if (_loading || _action == null) return;
            _action.WheelStep = (int)_wheelStep.Value;
            Raise();
        };
        page.Controls.Add(_wheelLabel, 0, 2);
        page.Controls.Add(_wheelStep, 1, 2);

        _mouseModifier.DropDownStyle = ComboBoxStyle.DropDownList;
        _mouseModifier.Width = Theme.S(160);
        _mouseModifier.Anchor = AnchorStyles.Left;
        _mouseModifier.Items.AddRange(_mouseModifierChoices);
        _mouseModifier.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _action == null || _mouseModifier.SelectedItem is not Choice<MouseModifier> choice) return;
            _action.MouseModifier = choice.Value;
            Raise();
        };
        page.Controls.Add(FieldCaption(L.T("editor.mouse.holding", "Удерживая:")), 0, 3);
        page.Controls.Add(_mouseModifier, 1, 3);

        var tip = Hint(L.T("editor.mouse.tip", "Совет: «Колесо вверх / вниз» на повороты ручки — удобная прокрутка, а вместе с Ctrl — плавный масштаб в браузере и графических редакторах."), top: 12);
        page.Controls.Add(tip, 0, 4);
        page.SetColumnSpan(tip, 2);
        return page;
    }

    void LoadMouse()
    {
        if (_action == null) return;
        SelectValue(_mouseAction, _action.Mouse);
        SelectValue(_mouseModifier, _action.MouseModifier);
        _wheelStep.Value = Math.Clamp(_action.WheelStep, 1, 20);
        UpdateMouseFields();
    }

    void UpdateMouseFields()
    {
        bool wheel = _action?.Mouse is MouseActionKind.WheelUp or MouseActionKind.WheelDown;
        _wheelLabel.Visible = _wheelStep.Visible = wheel;
    }

    // ───────────────────────── Система ─────────────────────────

    Control BuildSystemPage()
    {
        var page = Grid(3, 9);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var info = InfoBox(L.T("editor.system.info", "Чтобы системные действия работали всегда, включите «Настройки → Запускать вместе с Windows» — программа будет стартовать свёрнутой в трей."));
        page.Controls.Add(info, 0, 0);
        page.SetColumnSpan(info, 3);

        _systemKind.DropDownStyle = ComboBoxStyle.DropDownList;
        _systemKind.Width = Theme.S(300);
        _systemKind.Anchor = AnchorStyles.Left;
        _systemKind.MaxDropDownItems = 12;
        _systemKind.Items.AddRange(_systemChoices);
        _systemKind.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _action == null || _systemKind.SelectedItem is not Choice<SystemActionKind> choice) return;
            _action.SystemAction = choice.Value;
            UpdateSystemFields();
            Raise();
        };
        page.Controls.Add(FieldCaption(L.T("editor.action", "Действие:")), 0, 1);
        page.Controls.Add(_systemKind, 1, 1);
        page.SetColumnSpan(_systemKind, 2);

        _systemDescription = Hint("");
        _systemDescription.Margin = new Padding(0, Theme.S(2), 0, Theme.S(12));
        page.Controls.Add(_systemDescription, 1, 2);
        page.SetColumnSpan(_systemDescription, 2);

        _targetLabel = FieldCaption(L.T("editor.target.program", "Программа:"));
        _target.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _target.Margin = new Padding(0, Theme.S(4), Theme.S(8), Theme.S(4));
        _target.TextChanged += (_, _) =>
        {
            if (_loading || _action == null) return;
            _action.Target = _target.Text;
            Raise();
        };
        _browseFile.Text = L.T("editor.browse", "Обзор…");
        _browseFolder.Text = L.T("editor.browseFolder", "Папка…");
        foreach (var button in new[] { _browseFile, _browseFolder })
        {
            StyleSmallButton(button, Theme.Text, Theme.Hover);
            button.Margin = new Padding(0, Theme.S(2), Theme.S(6), 0);
        }
        _browseFile.Click += (_, _) => BrowseFile();
        _browseFolder.Click += (_, _) => BrowseFolder();
        _browseButtons.AutoSize = true;
        _browseButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _browseButtons.WrapContents = false;
        _browseButtons.Margin = new Padding(0);
        _browseButtons.Anchor = AnchorStyles.Left;
        _browseButtons.Controls.Add(_browseFile);
        _browseButtons.Controls.Add(_browseFolder);
        page.Controls.Add(_targetLabel, 0, 3);
        page.Controls.Add(_target, 1, 3);
        page.Controls.Add(_browseButtons, 2, 3);

        _argumentsLabel = FieldCaption(L.T("editor.arguments", "Аргументы:"));
        _arguments.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _arguments.Margin = new Padding(0, Theme.S(4), Theme.S(8), Theme.S(4));
        _arguments.PlaceholderText = L.T("editor.optional", "необязательно");
        _arguments.TextChanged += (_, _) =>
        {
            if (_loading || _action == null) return;
            _action.Arguments = _arguments.Text;
            Raise();
        };
        page.Controls.Add(_argumentsLabel, 0, 4);
        page.Controls.Add(_arguments, 1, 4);

        _textLabel = FieldCaption(L.T("editor.text", "Текст:"));
        _textLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _text.Multiline = true;
        _text.AcceptsReturn = true;
        _text.ScrollBars = ScrollBars.Vertical;
        _text.Height = Theme.S(120);
        _text.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _text.Margin = new Padding(0, Theme.S(4), Theme.S(8), Theme.S(4));
        _text.TextChanged += (_, _) =>
        {
            if (_loading || _action == null) return;
            _action.Text = _text.Text;
            Raise();
        };
        page.Controls.Add(_textLabel, 0, 5);
        page.Controls.Add(_text, 1, 5);
        page.SetColumnSpan(_text, 2);

        _test.Text = L.T("editor.test", "▶  Проверить сейчас");
        StyleSmallButton(_test, Theme.Accent, Theme.AccentSoft);
        _test.Anchor = AnchorStyles.Left;
        _test.Margin = new Padding(0, Theme.S(10), 0, Theme.S(8));
        _test.Click += (_, _) => TestRequested?.Invoke(this, EventArgs.Empty);
        page.Controls.Add(_test, 1, 6);
        return page;
    }

    void LoadSystem()
    {
        if (_action == null) return;
        SelectValue(_systemKind, _action.SystemAction);
        _target.Text = _action.Target;
        _arguments.Text = _action.Arguments;
        _text.Text = _action.Text;
        UpdateSystemFields();
    }

    void UpdateSystemFields()
    {
        var kind = _action?.SystemAction ?? SystemActionKind.RunProgram;
        bool hasTarget = kind is SystemActionKind.RunProgram or SystemActionKind.OpenUrl or SystemActionKind.OpenPath or SystemActionKind.RunCommand;

        _systemDescription.Text = UiText.SystemDescription(kind);
        _targetLabel.Text = kind switch
        {
            SystemActionKind.RunProgram => L.T("editor.target.program", "Программа:"),
            SystemActionKind.OpenUrl => L.T("editor.target.url", "Адрес сайта:"),
            SystemActionKind.OpenPath => L.T("editor.target.path", "Файл или папка:"),
            _ => L.T("editor.target.command", "Команда:"),
        };
        _target.PlaceholderText = kind switch
        {
            SystemActionKind.RunProgram => L.T("editor.example.program", "например: C:\\Windows\\notepad.exe"),
            SystemActionKind.OpenUrl => L.T("editor.example.url", "например: youtube.com"),
            SystemActionKind.OpenPath => L.T("editor.example.path", "например: C:\\Users\\Имя\\Documents"),
            _ => L.T("editor.example.command", "например: start ms-settings:"),
        };
        _targetLabel.Visible = _target.Visible = hasTarget;
        _browseButtons.Visible = kind is SystemActionKind.RunProgram or SystemActionKind.OpenPath;
        _browseFolder.Visible = kind == SystemActionKind.OpenPath;
        _argumentsLabel.Visible = _arguments.Visible = kind == SystemActionKind.RunProgram;
        _textLabel.Visible = _text.Visible = kind == SystemActionKind.TypeText;
        // Ввод текста проверять бессмысленно (он напечатается сюда же), а выключение и сон — опасно.
        _test.Visible = kind is not (SystemActionKind.TypeText or SystemActionKind.Sleep or SystemActionKind.Shutdown or SystemActionKind.Restart);
    }

    void BrowseFile()
    {
        if (_action == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = _action.SystemAction == SystemActionKind.RunProgram
                ? L.T("dialog.chooseProgram", "Выберите программу")
                : L.T("dialog.chooseFile", "Выберите файл"),
            Filter = _action.SystemAction == SystemActionKind.RunProgram
                ? L.T("filter.programs", "Программы и ярлыки (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk") + "|" + L.T("filter.all", "Все файлы (*.*)|*.*")
                : L.T("filter.all", "Все файлы (*.*)|*.*"),
            DereferenceLinks = false,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.FileName;
    }

    void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = L.T("dialog.chooseFolder", "Выберите папку"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.SelectedPath;
    }
}
