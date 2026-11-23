using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MT760Helper
{
    // Every change applies and saves immediately; there is no OK/Cancel.
    sealed class SettingsForm : Form
    {
        static readonly Color Muted = Color.FromArgb(96, 100, 110);
        // Index = position in the language list.
        static readonly string[] LanguageCodes = { "auto", "ru", "en" };

        readonly Settings settings;
        readonly Engine engine;
        readonly Action changed, languageChanged;
        readonly CheckBox enabled, invert, keepOpen, countdown, rapooOnly, autostart;
        readonly NumericUpDown delay, notches;
        readonly ComboBox language, leftAction, rightAction, middleAction;
        readonly Label diagnostics, macroWarning;
        bool loading;

        public SettingsForm(Settings settings, Engine engine, Icon icon, Action changed, Action languageChanged)
        {
            this.settings = settings;
            this.engine = engine;
            this.changed = changed;
            this.languageChanged = languageChanged;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "MT760 Helper";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 766);

            const int x = 20, width = 460;
            AddLabel(Lang.T("Переключение окон боковым колесом", "Switch windows with the side wheel"),
                     x, 16, width, new Font("Segoe UI", 12F, FontStyle.Bold), SystemColors.ControlText);
            AddLabel(Lang.T("Колесо открывает список окон (Alt+Tab) и держит его, пока вы выбираете. " +
                            "Окно откроется, когда вы перестанете крутить колесо.",
                            "The wheel opens the window list (Alt+Tab) and keeps it open while you choose. " +
                            "The window opens when you stop rolling the wheel."), x, 46, width, null, Muted);

            enabled = AddCheck(Lang.T("Включено", "Enabled"), x, 92);
            enabled.Font = new Font(Font, FontStyle.Bold);

            AddLabel("Язык / Language", 250, 93, 0, null, null);
            language = new ComboBox { Location = new Point(360, 89), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            language.Items.AddRange(new object[] { Lang.T("Как в Windows", "Windows language"), "Русский", "English" });
            Controls.Add(language);

            AddLabel(Lang.T("Держать список окон", "Keep the window list open"), x, 129, 0, null, null);
            delay = AddNumber(220, 126, 0.5m, 15m, 0.5m, 1);
            AddLabel(Lang.T("с после последнего действия", "s after the last action"), 290, 129, 0, null, null);

            AddLabel(Lang.T("Щелчков колеса на одно окно", "Wheel notches per window"), x, 163, 0, null, null);
            notches = AddNumber(220, 160, 1, 5, 1, 0);

            invert = AddCheck(Lang.T("Обратное направление бокового колеса", "Reverse side wheel direction"), x, 198);
            keepOpen = AddCheck(Lang.T("Не закрывать список, пока я двигаю мышью", "Keep the list open while I move the mouse"), x, 224);
            countdown = AddCheck(Lang.T("Показывать обратный отсчёт внизу экрана", "Show a countdown at the bottom of the screen"), x, 250);
            rapooOnly = AddCheck(Lang.T("Только мышь Rapoo (тачпад и другие мыши прокручивают как обычно)",
                                        "Rapoo mouse only (touchpad and other mice scroll as usual)"), x, 276);
            autostart = AddCheck(Lang.T("Запускать вместе с Windows", "Start with Windows"), x, 302);

            var buttons = new GroupBox
            {
                Text = Lang.T("Кнопки мыши, пока открыт список окон", "Mouse buttons while the window list is open"),
                Location = new Point(x, 340), Size = new Size(width, 160),
            };
            leftAction = AddAction(buttons, Lang.T("Левая кнопка (мимо окон)", "Left button (outside windows)"), 26);
            rightAction = AddAction(buttons, Lang.T("Правая кнопка", "Right button"), 56);
            middleAction = AddAction(buttons, Lang.T("Нажатие на колесо", "Wheel click"), 86);
            buttons.Controls.Add(new Label
            {
                Text = Lang.T("Щелчок прямо по окну в списке открывает это окно, если Windows сообщает, где окна.",
                              "Clicking a window in the list opens that window when Windows reports where the windows are."),
                Location = new Point(12, 116), AutoSize = true, MaximumSize = new Size(width - 24, 0), ForeColor = Muted,
            });
            Controls.Add(buttons);

            var check = new GroupBox { Text = Lang.T("Проверка колеса", "Wheel check"), Location = new Point(x, 514), Size = new Size(width, 112) };
            diagnostics = new Label
            {
                Text = Lang.T("Покрутите боковое колесо мыши — здесь будет видно, что оно отправляет.",
                              "Roll the side wheel — this shows what it sends."),
                Location = new Point(12, 24), AutoSize = true, MaximumSize = new Size(width - 24, 0),
            };
            macroWarning = new Label
            {
                Text = Lang.T("Мышь сама отправляет Alt+Tab — это макрос из A HUB, из-за него окна мелькают. " +
                              "В A HUB верните боковому колесу стандартное действие (колесо влево / вправо).",
                              "The mouse itself sends Alt+Tab — an A HUB macro, which makes windows flash past. " +
                              "In A HUB, set the side wheel back to its default action (roller left / right)."),
                Location = new Point(12, 60), AutoSize = true, MaximumSize = new Size(width - 24, 0),
                ForeColor = Color.FromArgb(180, 30, 30), Visible = false,
            };
            check.Controls.Add(diagnostics);
            check.Controls.Add(macroWarning);
            Controls.Add(check);

            AddLabel(Lang.T("Пока список открыт: колесо — выбрать окно, Enter — открыть, Esc — отмена. " +
                            "Shift + боковое колесо — обычная горизонтальная прокрутка.",
                            "While the list is open: wheel — choose a window, Enter — open, Esc — cancel. " +
                            "Shift + side wheel — normal horizontal scrolling."),
                     x, 640, width, null, Muted);

            var help = new LinkLabel { Text = Lang.T("Инструкция", "Help"), AutoSize = true, Location = new Point(x, 726) };
            help.LinkClicked += delegate { OpenReadme(); };
            Controls.Add(help);

            var close = new Button { Text = Lang.T("Закрыть", "Close"), Location = new Point(400, 720), Size = new Size(80, 28) };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            ResumeLayout(false);
            PerformLayout();

            LoadValues();
            foreach (CheckBox box in new[] { enabled, invert, keepOpen, countdown, rapooOnly })
                box.CheckedChanged += delegate { Apply(); };
            delay.ValueChanged += delegate { Apply(); };
            notches.ValueChanged += delegate { Apply(); };
            foreach (ComboBox box in new[] { leftAction, rightAction, middleAction })
                box.SelectedIndexChanged += delegate { Apply(); };
            autostart.CheckedChanged += delegate { if (!loading) Autostart.Set(autostart.Checked); };
            language.SelectedIndexChanged += delegate
            {
                if (loading) return;
                settings.Language = LanguageCodes[language.SelectedIndex];
                settings.Save();
                languageChanged();
            };

            engine.WheelSeen += OnWheelSeen;
            engine.MacroDetected += OnMacroDetected;
            macroWarning.Visible = engine.MacroSeen;
        }

        public void LoadValues()
        {
            loading = true;
            enabled.Checked = settings.Enabled;
            delay.Value = Math.Max(delay.Minimum, Math.Min(delay.Maximum, settings.DelayMs / 1000m));
            notches.Value = Math.Max(notches.Minimum, Math.Min(notches.Maximum, settings.NotchesPerStep));
            invert.Checked = settings.Invert;
            keepOpen.Checked = settings.KeepOpenWhileMoving;
            countdown.Checked = settings.ShowCountdown;
            rapooOnly.Checked = settings.RapooOnly;
            leftAction.SelectedIndex = (int)settings.LeftClickAction;
            rightAction.SelectedIndex = (int)settings.RightClickAction;
            middleAction.SelectedIndex = (int)settings.MiddleClickAction;
            language.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageCodes, settings.Language));
            autostart.Checked = Autostart.IsEnabled();
            loading = false;
        }

        void Apply()
        {
            if (loading) return;
            settings.Enabled = enabled.Checked;
            settings.DelayMs = (int)(delay.Value * 1000);
            settings.NotchesPerStep = (int)notches.Value;
            settings.Invert = invert.Checked;
            settings.KeepOpenWhileMoving = keepOpen.Checked;
            settings.ShowCountdown = countdown.Checked;
            settings.RapooOnly = rapooOnly.Checked;
            settings.LeftClickAction = (ClickAction)leftAction.SelectedIndex;
            settings.RightClickAction = (ClickAction)rightAction.SelectedIndex;
            settings.MiddleClickAction = (ClickAction)middleAction.SelectedIndex;
            settings.Save();
            changed();
        }

        void OnWheelSeen(string text)
        {
            diagnostics.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        void OnMacroDetected()
        {
            macroWarning.Visible = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.WheelSeen -= OnWheelSeen;
            engine.MacroDetected -= OnMacroDetected;
            base.OnFormClosed(e);
        }

        static void OpenReadme()
        {
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            string path = Path.Combine(folder, Lang.T("README.txt", "README-en.txt"));
            if (!File.Exists(path)) path = Path.Combine(folder, "README.txt");
            if (File.Exists(path)) Process.Start(path);
        }

        Label AddLabel(string text, int x, int y, int width, Font font, Color? color)
        {
            var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
            if (width > 0) label.MaximumSize = new Size(width, 0);
            if (font != null) label.Font = font;
            if (color.HasValue) label.ForeColor = color.Value;
            Controls.Add(label);
            return label;
        }

        ComboBox AddAction(GroupBox group, string text, int y)
        {
            group.Controls.Add(new Label { Text = text, Location = new Point(12, y + 3), AutoSize = true });
            var box = new ComboBox { Location = new Point(230, y), Width = 216, DropDownStyle = ComboBoxStyle.DropDownList };
            // Order = (int)ClickAction.
            box.Items.AddRange(new object[]
            {
                Lang.T("Открыть выбранное окно", "Open selected window"),
                Lang.T("Отменить переключение", "Cancel switching"),
                Lang.T("Ничего не делать", "Do nothing"),
            });
            group.Controls.Add(box);
            return box;
        }

        CheckBox AddCheck(string text, int x, int y)
        {
            var box = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true };
            Controls.Add(box);
            return box;
        }

        NumericUpDown AddNumber(int x, int y, decimal min, decimal max, decimal step, int decimals)
        {
            var number = new NumericUpDown
            {
                Location = new Point(x, y), Width = 62, Minimum = min, Maximum = max,
                Increment = step, DecimalPlaces = decimals, TextAlign = HorizontalAlignment.Right,
            };
            Controls.Add(number);
            return number;
        }
    }
}
