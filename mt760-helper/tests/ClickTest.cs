using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// End-to-end test of the configurable mouse-button actions while the switcher is open.
// Every click first checks that the switcher really is the foreground window, so a failure never clicks into user apps.
class ClickHarness
{
    [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MI mi; [FieldOffset(0)] public KI ki; }
    [StructLayout(LayoutKind.Sequential)] struct IN { public uint type; public U u; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int s);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    static readonly string Exe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\MT760Helper.exe"));
    static readonly string Ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MT760Helper\settings.ini");
    static int fails, W, H;

    static void Send(IN i) { SendInput(1, new[] { i }, Marshal.SizeOf(typeof(IN))); }
    static void Mouse(uint flags, int data = 0, int dx = 0, int dy = 0) { var i = new IN { type = 0 }; i.u.mi = new MI { dx = dx, dy = dy, data = (uint)data, flags = flags }; Send(i); }
    static void Key(ushort vk, bool up) { var i = new IN { type = 1 }; i.u.ki = new KI { vk = vk, flags = up ? 2u : 0u }; Send(i); }
    static void HWheel(int d) { Mouse(0x1000, d); }
    static void MoveTo(int x, int y) { Mouse(0x8001, 0, x * 65535 / (W - 1), y * 65535 / (H - 1)); Thread.Sleep(40); }
    static bool AltDown() { return (GetAsyncKeyState(0x12) & 0x8000) != 0; }
    static IntPtr Fg() { return GetForegroundWindow(); }
    static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
    static bool SwitcherOpen() { var s = new StringBuilder(64); GetClassName(Fg(), s, 64); return s.ToString() == "XamlExplorerHostIslandWindow" && AltDown(); }
    static bool SwitcherOpenIgnoringAlt() { var s = new StringBuilder(64); GetClassName(Fg(), s, 64); return s.ToString() == "XamlExplorerHostIslandWindow"; }
    // Left click on the taskbar is only safe when the helper is in switching mode (it then swallows the click).
    static bool SwitchingNow() { return AltDown(); }
    // Waits until the switcher has closed and a real window is in front (or 1.5 s passed).
    static IntPtr Settled() { for (int i = 0; i < 30; i++) { var s = new StringBuilder(64); GetClassName(Fg(), s, 64); if (!AltDown() && s.ToString() != "XamlExplorerHostIslandWindow") break; Thread.Sleep(50); } return Fg(); }

    static bool Click(string button)
    {
        if (!SwitcherOpen()) { Console.WriteLine("   (switcher not open: click skipped)"); return false; }
        if (button == "left") { Mouse(0x0002); Thread.Sleep(30); Mouse(0x0004); }
        else if (button == "right") { Mouse(0x0008); Thread.Sleep(30); Mouse(0x0010); }
        else { Mouse(0x0020); Thread.Sleep(30); Mouse(0x0040); }
        return true;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        if (!ok) fails++;
    }

    static void Start(string extra)
    {
        foreach (var p in Process.GetProcessesByName("MT760Helper")) { p.Kill(); p.WaitForExit(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Ini));
        File.WriteAllText(Ini, "Enabled=1\nDelayMs=3000\nRapooOnly=0\nKeepOpenWhileMoving=1\nShowCountdown=1\n" + extra);
        Process.Start(Exe, "--tray");
        Thread.Sleep(1500);
    }

    // Brings the start window back: one Alt+Tab step, opened with the middle button (Open in every config used here).
    static void BackTo(IntPtr a, string how)
    {
        if (Fg() == a) return;
        HWheel(120); Thread.Sleep(700); Click(how); Settled();
    }

    static System.Windows.Rect FirstThumbnail()
    {
        var root = AutomationElement.FromHandle(Fg());
        var items = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        return items.Count > 0 ? items[0].Current.BoundingRectangle : System.Windows.Rect.Empty;
    }

    static void Main()
    {
        SetProcessDPIAware();
        W = GetSystemMetrics(0); H = GetSystemMetrics(1);
        string original = File.Exists(Ini) ? File.ReadAllText(Ini) : null;
        int neutralX = W / 2, neutralY = 150;   // above the thumbnail grid
        try
        {
            Start("LeftClickAction=0\nRightClickAction=1\nMiddleClickAction=0\n");
            IntPtr a = Fg();
            Console.WriteLine("start window: " + Title(a));
            // Learn the "previous window" with a plain wheel step and the timeout, then go back.
            HWheel(120); Thread.Sleep(3400);
            IntPtr b = Settled();
            Console.WriteLine("previous window: " + Title(b));
            Check("plain wheel + timeout switches", b != a && !SwitcherOpenIgnoringAlt(), Title(b));
            HWheel(120); Thread.Sleep(3400); Settled();
            Check("plain wheel + timeout switches back", Fg() == a, Title(Fg()));

            // 1. Neutral left click (on the switcher, but not on a thumbnail) opens the selected window.
            MoveTo(neutralX, neutralY);
            HWheel(120); Thread.Sleep(700);
            Click("left"); Settled();
            Check("neutral left click opens selected window", !AltDown() && Fg() == b, Title(Fg()));
            BackTo(a, "middle");
            Check("middle click opens (back to start)", Fg() == a, Title(Fg()));

            // 2. Neutral left click right after the wheel, before the switcher is fully on screen.
            MoveTo(neutralX, neutralY);
            HWheel(120); Thread.Sleep(120);
            Click("left"); Thread.Sleep(300); Settled();
            Check("quick neutral left click opens selected window", !AltDown() && Fg() == b, Title(Fg()));
            BackTo(a, "middle");

            // 3. Left click on a thumbnail opens THAT window (here: the start window, not the selected one).
            //    Thumbnail positions come from UI Automation, which Windows exposes only sometimes.
            HWheel(120); Thread.Sleep(700);
            System.Windows.Rect r = SwitcherOpen() ? FirstThumbnail() : System.Windows.Rect.Empty;
            if (!r.IsEmpty)
            {
                MoveTo((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
                Thread.Sleep(150);
                Click("left"); Settled();
                Check("left click on a thumbnail opens that window", !AltDown() && Fg() == a, Title(Fg()));
            }
            else
            {
                Console.WriteLine("SKIP left click on a thumbnail (Windows does not expose thumbnail positions right now)");
                Click("middle"); Settled();
            }
            BackTo(a, "middle");

            // 3b. Left click outside the switcher (empty spot on the taskbar) is kept from the taskbar and opens the selection.
            MoveTo(1600, H - 24);
            HWheel(120); Thread.Sleep(700);
            Click("left"); Settled();
            Check("left click on the taskbar opens selected window", !AltDown() && Fg() == b, Title(Fg()));
            BackTo(a, "middle");

            // 4. Right click cancels.
            MoveTo(neutralX, neutralY);
            HWheel(120); Thread.Sleep(700);
            Click("right"); Settled();
            Check("right click cancels", !AltDown() && Fg() == a, Title(Fg()));

            // 5. Other settings: left = nothing, right = open, middle = cancel.
            Start("LeftClickAction=2\nRightClickAction=0\nMiddleClickAction=1\n");
            MoveTo(neutralX, neutralY);
            HWheel(120); Thread.Sleep(700);
            Click("left"); Thread.Sleep(500);
            Check("left = nothing: switcher stays open", SwitcherOpen());
            Click("right"); Settled();
            Check("right = open", !AltDown() && Fg() == b, Title(Fg()));
            BackTo(a, "right");
            HWheel(120); Thread.Sleep(700);
            Click("middle"); Settled();
            Check("middle = cancel", !AltDown() && Fg() == a, Title(Fg()));

            // 6. Left = cancel.
            Start("LeftClickAction=1\nRightClickAction=1\nMiddleClickAction=0\n");
            MoveTo(neutralX, neutralY);
            HWheel(120); Thread.Sleep(700);
            Click("left"); Settled();
            Check("left = cancel", !AltDown() && Fg() == a, Title(Fg()));
        }
        catch (Exception ex)
        {
            Console.WriteLine("TEST CRASHED: " + ex.Message);
            fails++;
        }
        finally
        {
            Key(0x1B, false); Key(0x1B, true); Key(0xE8, false); Key(0xE8, true); Key(0xA4, true);
            foreach (var p in Process.GetProcessesByName("MT760Helper")) { p.Kill(); p.WaitForExit(); }
            if (original != null) File.WriteAllText(Ini, original);
            else if (File.Exists(Ini)) File.Delete(Ini);
            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        }
    }
}
