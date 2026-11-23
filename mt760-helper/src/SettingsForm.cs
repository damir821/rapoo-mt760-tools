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
        // Index = (int)ClickAction.
        static readonly string[] ActionNames = { "Открыть выбранное окно", "Отменить переключение", "Ничего не делать" };

        readonly Settings settings;
        readonly Engine engine;
        readonly Action changed;
        readonly CheckBox enabled, invert, keepOpen, countdown, rapooOnly, autostart;
        readonly NumericUpDown delay, notches;
        readonly ComboBox leftAction, rightAction, middleAction;
        readonly Label diagnostics, macroWarning;
        bool loading;

        public SettingsForm(Settings settings, Engine engine, Icon icon, Action changed)
        {
            this.settings = settings;
            this.engine = engine;
            this.changed = changed;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "MT760 Helper";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 746);

            const int x = 20, width = 460;
            AddLabel("Переключение окон боковым колесом", x, 16, width, new Font("Segoe UI", 12F, FontStyle.Bold), SystemColors.ControlText);
            AddLabel("Колесо открывает список окон (Alt+Tab) и держит его, пока вы выбираете. " +
                     "Окно откроется, когда вы перестанете крутить колесо.", x, 46, width, null, Muted);

            enabled = AddCheck("Включено", x, 92);
            enabled.Font = new Font(Font, FontStyle.Bold);

            AddLabel("Держать список окон", x, 129, 0, null, null);
            delay = AddNumber(220, 126, 0.5m, 15m, 0.5m, 1);
            AddLabel("с после последнего действия", 290, 129, 0, null, null);

            AddLabel("Щелчков колеса на одно окно", x, 163, 0, null, null);
            notches = AddNumber(220, 160, 1, 5, 1, 0);

            invert = AddCheck("Обратное направление бокового колеса", x, 198);
            keepOpen = AddCheck("Не закрывать список, пока я двигаю мышью", x, 224);
            countdown = AddCheck("Показывать обратный отсчёт внизу экрана", x, 250);
            rapooOnly = AddCheck("Только мышь Rapoo (тачпад и другие мыши прокручивают как обычно)", x, 276);
            autostart = AddCheck("Запускать вместе с Windows", x, 302);

            var buttons = new GroupBox { Text = "Кнопки мыши, пока открыт список окон", Location = new Point(x, 340), Size = new Size(width, 140) };
            leftAction = AddAction(buttons, "Левая кнопка (мимо окон)", 26);
            rightAction = AddAction(buttons, "Правая кнопка", 56);
            middleAction = AddAction(buttons, "Нажатие на колесо", 86);
            buttons.Controls.Add(new Label
            {
                Text = "Щелчок левой кнопкой прямо по окну в списке всегда открывает это окно.",
                Location = new Point(12, 116), AutoSize = true, MaximumSize = new Size(width - 24, 0), ForeColor = Muted,
            });
            Controls.Add(buttons);

            var check = new GroupBox { Text = "Проверка колеса", Location = new Point(x, 494), Size = new Size(width, 112) };
            diagnostics = new Label
            {
                Text = "Покрутите боковое колесо мыши — здесь будет видно, что оно отправляет.",
                Location = new Point(12, 24), AutoSize = true, MaximumSize = new Size(width - 24, 0),
            };
            macroWarning = new Label
            {
                Text = "Мышь сама отправляет Alt+Tab — это макрос из A HUB, из-за него окна мелькают. " +
                       "В A HUB верните боковому колесу стандартное действие (колесо влево / вправо).",
                Location = new Point(12, 60), AutoSize = true, MaximumSize = new Size(width - 24, 0),
                ForeColor = Color.FromArgb(180, 30, 30), Visible = false,
            };
            check.Controls.Add(diagnostics);
            check.Controls.Add(macroWarning);
            Controls.Add(check);

            AddLabel("Пока список открыт: колесо — выбрать окно, Enter — открыть, Esc — отмена. " +
                     "Shift + боковое колесо — обычная горизонтальная прокрутка.",
                     x, 620, width, null, Muted);

            var help = new LinkLabel { Text = "Инструкция", AutoSize = true, Location = new Point(x, 706) };
            help.LinkClicked += delegate { OpenReadme(); };
            Controls.Add(help);

            var close = new Button { Text = "Закрыть", Location = new Point(400, 700), Size = new Size(80, 28) };
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
            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "README.txt");
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
            box.Items.AddRange(ActionNames);
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
