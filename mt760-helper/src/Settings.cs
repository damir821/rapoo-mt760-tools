using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MT760Helper
{
    // What a mouse button does while the switcher is open (for the left button: outside the thumbnails).
    enum ClickAction { Open = 0, Cancel = 1, Nothing = 2 }

    sealed class Settings
    {
        public bool Enabled = true;
        public ClickAction LeftClickAction = ClickAction.Open;
        public ClickAction RightClickAction = ClickAction.Cancel;
        public ClickAction MiddleClickAction = ClickAction.Open;
        public int DelayMs = 4500;
        public int NotchesPerStep = 1;
        public bool Invert;
        public bool KeepOpenWhileMoving = true;
        public bool ShowCountdown = true;
        public bool RapooOnly = true;
        // "auto", "ru" or "en"; see Lang.Apply.
        public string Language = "auto";

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MT760Helper"); }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.Enabled = Bool(values, "Enabled", s.Enabled);
                s.DelayMs = Int(values, "DelayMs", s.DelayMs, 500, 15000);
                s.NotchesPerStep = Int(values, "NotchesPerStep", s.NotchesPerStep, 1, 5);
                s.Invert = Bool(values, "Invert", s.Invert);
                s.KeepOpenWhileMoving = Bool(values, "KeepOpenWhileMoving", s.KeepOpenWhileMoving);
                s.ShowCountdown = Bool(values, "ShowCountdown", s.ShowCountdown);
                s.RapooOnly = Bool(values, "RapooOnly", s.RapooOnly);
                s.LeftClickAction = (ClickAction)Int(values, "LeftClickAction", (int)s.LeftClickAction, 0, 2);
                s.RightClickAction = (ClickAction)Int(values, "RightClickAction", (int)s.RightClickAction, 0, 2);
                s.MiddleClickAction = (ClickAction)Int(values, "MiddleClickAction", (int)s.MiddleClickAction, 0, 2);
                string language;
                if (values.TryGetValue("Language", out language) && (language == "ru" || language == "en")) s.Language = language;
            }
            catch (Exception ex) { Program.LogError(ex, "load settings"); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, new[]
                {
                    "Enabled=" + (Enabled ? 1 : 0),
                    "DelayMs=" + DelayMs,
                    "NotchesPerStep=" + NotchesPerStep,
                    "Invert=" + (Invert ? 1 : 0),
                    "KeepOpenWhileMoving=" + (KeepOpenWhileMoving ? 1 : 0),
                    "ShowCountdown=" + (ShowCountdown ? 1 : 0),
                    "RapooOnly=" + (RapooOnly ? 1 : 0),
                    "LeftClickAction=" + (int)LeftClickAction,
                    "RightClickAction=" + (int)RightClickAction,
                    "MiddleClickAction=" + (int)MiddleClickAction,
                    "Language=" + Language,
                }, Encoding.UTF8);
            }
            catch (Exception ex) { Program.LogError(ex, "save settings"); }
        }

        static bool Bool(Dictionary<string, string> values, string key, bool fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
        }

        static int Int(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            string v;
            int n;
            if (!values.TryGetValue(key, out v) || !int.TryParse(v, out n)) return fallback;
            return Math.Max(min, Math.Min(max, n));
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "MT760Helper";

        static string Command { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return value != null && value.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(ValueName, Command);
                    else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
                }
            }
            catch (Exception ex) { Program.LogError(ex, "autostart"); }
        }
    }
}
