using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("MT760 Helper")]
[assembly: System.Reflection.AssemblyProduct("MT760 Helper")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace MT760Helper
{
    static class Program
    {
        // Lets the crash handlers release a held Alt key.
        public static Engine CurrentEngine;

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\MT760Helper.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running: ask that copy to show its settings window.
                    Native.PostMessage(Native.HWND_BROADCAST, Engine.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    LogError(e.Exception, "UI thread");
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    LogError(e.ExceptionObject as Exception, "unhandled");
                    Engine engine = CurrentEngine;
                    if (engine != null) engine.ReleaseKeys();
                };

                bool fromAutostart = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
                Application.Run(new TrayApp(!fromAutostart));
            }
        }

        // Step-by-step log for diagnosing input handling; on only while %APPDATA%\MT760Helper\trace.on exists.
        static readonly bool tracing = File.Exists(Path.Combine(Settings.Folder, "trace.on"));
        static readonly Stopwatch traceClock = Stopwatch.StartNew();

        public static void Trace(string message)
        {
            if (!tracing) return;
            try { File.AppendAllText(Path.Combine(Settings.Folder, "trace.log"), traceClock.ElapsedMilliseconds + " " + message + Environment.NewLine); }
            catch { }
        }

        public static void LogError(Exception ex, string where)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                string path = Path.Combine(Settings.Folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + where + "] " + ex + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
