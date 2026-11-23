using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace MT760Helper
{
    // Small click-through countdown shown at the bottom of the screen while Alt+Tab is held.
    sealed class Overlay : Form
    {
        static readonly Color Background = Color.FromArgb(32, 33, 38);
        static readonly Color Muted = Color.FromArgb(170, 174, 184);
        static readonly Color Track = Color.FromArgb(58, 60, 68);
        static readonly Color Accent = Color.FromArgb(92, 150, 255);

        readonly Font titleFont = new Font("Segoe UI Semibold", 11F);
        readonly Font hintFont = new Font("Segoe UI", 8.5F);
        readonly float scale;
        int remaining, total = 1;
        string hint = "";

        public Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Background;
            DoubleBuffered = true;
            Opacity = 0.96;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
        }

        int S(float value) { return (int)Math.Round(value * scale); }

        // "колесо — выбор · ЛКМ — открыть · ПКМ — отмена", following the current button settings.
        public static string HintFor(Settings settings)
        {
            string text = Lang.T("колесо — выбор", "wheel — select");
            text += Part(Lang.T("ЛКМ", "left click"), settings.LeftClickAction);
            text += Part(Lang.T("ПКМ", "right click"), settings.RightClickAction);
            text += Part(Lang.T("нажатие колеса", "wheel click"), settings.MiddleClickAction);
            return text;
        }

        static string Part(string button, ClickAction action)
        {
            if (action == ClickAction.Open) return " · " + button + Lang.T(" — открыть", " — open");
            if (action == ClickAction.Cancel) return " · " + button + Lang.T(" — отмена", " — cancel");
            return "";
        }

        public void SetHint(string text)
        {
            hint = text;
            Size measured = TextRenderer.MeasureText(hint, hintFont);
            Size = new Size(Math.Max(S(300), measured.Width + S(36)), S(66));
            Invalidate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWCP_ROUND; ignored before Windows 11.
            Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST) { m.Result = (IntPtr)(-1); return; }
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; }
            base.WndProc(ref m);
        }

        public void ShowCountdown(int remainingMs, int totalMs)
        {
            remaining = Math.Max(0, remainingMs);
            total = Math.Max(1, totalMs);
            if (!Visible)
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - S(56));
                Show();
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            Invalidate();
        }

        public void HideCountdown()
        {
            if (Visible) Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string seconds = (remaining / 1000.0).ToString("0.0", Lang.English ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("ru-RU"));
            string title = Lang.T("Переход через " + seconds + " с", "Switching in " + seconds + " s");
            TextRenderer.DrawText(g, title, titleFont, new Point(S(16), S(10)), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, hint, hintFont, new Point(S(16), S(34)), Muted, TextFormatFlags.NoPadding);

            int barHeight = Math.Max(2, S(3));
            int y = Height - barHeight;
            using (var brush = new SolidBrush(Track)) g.FillRectangle(brush, 0, y, Width, barHeight);
            using (var brush = new SolidBrush(Accent)) g.FillRectangle(brush, 0, y, (int)((long)Width * remaining / total), barHeight);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                hintFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
