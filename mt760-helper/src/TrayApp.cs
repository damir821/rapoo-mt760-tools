using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MT760Helper
{
    sealed class TrayApp : ApplicationContext
    {
        readonly Settings settings;
        readonly Engine engine;
        readonly Overlay overlay;
        readonly Icon icon;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem enabledItem;
        SettingsForm settingsForm;
        bool macroTipShown;

        public TrayApp(bool showSettings)
        {
            settings = Settings.Load();
            icon = LoadIcon();
            engine = new Engine(settings);
            Program.CurrentEngine = engine;
            overlay = new Overlay();

            engine.Countdown += delegate(int remaining, int total)
            {
                if (settings.ShowCountdown) overlay.ShowCountdown(remaining, total);
            };
            engine.Ended += overlay.HideCountdown;
            engine.ShowRequested += delegate { ShowSettings(); };
            engine.MacroDetected += OnMacroDetected;

            enabledItem = new ToolStripMenuItem("Включено", null, delegate
            {
                settings.Enabled = !settings.Enabled;
                settings.Save();
                SyncState();
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.LoadValues();
            });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Настройки…", null, delegate { ShowSettings(); });
            menu.Items.Add(enabledItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitThread(); });

            tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowSettings();
            };
            SyncState();

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;

            if (showSettings) ShowSettings();
        }

        static Icon LoadIcon()
        {
            using (var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("MT760Helper.app.ico"))
                return stream == null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
        }

        void SyncState()
        {
            overlay.SetHint(Overlay.HintFor(settings));
            enabledItem.Checked = settings.Enabled;
            tray.Text = settings.Enabled ? "MT760 Helper — переключение окон колесом" : "MT760 Helper — выключено";
        }

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
                settingsForm = new SettingsForm(settings, engine, icon, SyncState);
            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
        }

        void OnMacroDetected()
        {
            if (macroTipShown) return;
            macroTipShown = true;
            tray.ShowBalloonTip(10000, "Колесо настроено в A HUB",
                "Мышь сама отправляет Alt+Tab (макрос A HUB), поэтому окна мелькают. " +
                "Верните боковому колесу стандартное действие в A HUB.", ToolTipIcon.Warning);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) engine.ReleaseKeys();
            if (e.Reason == SessionSwitchReason.SessionUnlock) engine.Rehook();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) engine.ReleaseKeys();
            if (e.Mode == PowerModes.Resume) engine.Rehook();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            engine.ReleaseKeys();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Dispose();
            tray.Visible = false;
            tray.Dispose();
            engine.Dispose();
            Program.CurrentEngine = null;
            overlay.Dispose();
            icon.Dispose();
            base.ExitThreadCore();
        }
    }
}
