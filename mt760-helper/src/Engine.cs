using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MT760Helper
{
    // Turns side-wheel clicks into a held Alt+Tab. Alt stays down while the user is choosing
    // and is released once the wheel and the mouse have been idle for Settings.DelayMs.
    //
    // Hooks run on the UI thread and must return quickly, so they only decide whether to
    // swallow an event and queue the actual work through Post().
    sealed class Engine : NativeWindow, IDisposable
    {
        const int WM_RUN_QUEUE = Native.WM_APP + 1;
        const int WheelNotch = 120;
        const int WheelResetMs = 600;
        // Time for Windows to react to a click inside the switcher (switch, or close via X) before we look.
        const int ClickSettleMs = 400;
        const int RestoreSettleMs = 150;
        const int MoveThresholdPx = 12;
        const int MaxHoldMs = 60000;
        const int RawFreshMs = 250;
        const int RawBufferSize = 256;

        // "MT76": marks input we inject ourselves.
        static readonly IntPtr Marker = new IntPtr(0x4D543736);
        // Alt+Tab window: Windows 11, Windows 10, classic.
        static readonly string[] SwitcherClasses = { "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "TaskSwitcherWnd" };
        // USB: VID_24AE&PID_1871, Bluetooth: VID&000224ae_PID&4191, BLE: VID&0224ae_PID&4191.
        static readonly Regex RapooId = new Regex(@"VID[_&](?:0002|02)?24AE[_&]PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase);

        public static readonly int ShowMessage = Native.RegisterWindowMessage("MT760Helper.Show");

        readonly Settings settings;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Queue<Action> pending = new Queue<Action>();
        readonly Dictionary<IntPtr, string> rapooPids = new Dictionary<IntPtr, string>();
        readonly HashSet<string> rapooMousePids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<uint> swallowedKeys = new HashSet<uint>();
        readonly Timer timer = new Timer();
        readonly Native.HookProc mouseProc;
        readonly Native.HookProc keyProc;
        readonly IntPtr rawBuffer = Marshal.AllocHGlobal(RawBufferSize);
        readonly uint rawHeaderSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
        IntPtr mouseHook, keyHook;

        IntPtr rawWheelDevice;
        long rawWheelAt = -1;

        bool active;
        int switchId;
        long startedAt, lastActivityAt;
        long lastSideWheelAt, lastMainWheelAt;
        int sideAccum, mainAccum;
        Native.POINT anchor;
        bool swallowLeftUp, swallowMiddleUp, swallowRightUp;
        // Net wheel steps since the switcher opened: the index of the selected window, negative from the end.
        int position;
        // After a click inside the switcher Windows forgets the selection; it is rebuilt lazily
        // (on the next wheel step or when opening) so the highlight doesn't run across the list.
        bool selectionLost;
        List<IntPtr> windowsBeforeClick = new List<IntPtr>();

        public event EventHandler ShowRequested;
        public event Action<int, int> Countdown;
        public event Action Ended;
        public event Action<string> WheelSeen;
        public event Action MacroDetected;

        public bool MacroSeen { get; private set; }
        public bool IsSwitching { get { return active; } }

        public Engine(Settings settings)
        {
            this.settings = settings;
            mouseProc = MouseHookProc;
            keyProc = KeyHookProc;
            CreateHandle(new CreateParams { Caption = "MT760Helper.Engine" });
            RegisterRawInput();
            InstallMouseHook();
            timer.Interval = 50;
            timer.Tick += delegate { Tick(); };
        }

        long Now { get { return clock.ElapsedMilliseconds; } }

        // Windows silently drops low-level hooks after sleep or a long stall; call this to renew them.
        public void Rehook()
        {
            rapooPids.Clear();
            rapooMousePids.Clear();
            RegisterRawInput();
            if (!active) InstallMouseHook();
        }

        public void ReleaseKeys()
        {
            if (active) Finish(false, "release");
        }

        public void Dispose()
        {
            ReleaseKeys();
            timer.Dispose();
            RemoveKeyHook();
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
            DestroyHandle();
            Marshal.FreeHGlobal(rawBuffer);
        }

        void InstallMouseHook()
        {
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, Native.GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero) Program.LogError(new Win32Exception(), "mouse hook");
        }

        void InstallKeyHook()
        {
            if (keyHook != IntPtr.Zero) return;
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, Native.GetModuleHandle(null), 0);
            if (keyHook == IntPtr.Zero) Program.LogError(new Win32Exception(), "keyboard hook");
        }

        void RemoveKeyHook()
        {
            if (keyHook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(keyHook);
            keyHook = IntPtr.Zero;
            swallowedKeys.Clear();
        }

        void RemoveKeyHookIfIdle()
        {
            if (!active && swallowedKeys.Count == 0) RemoveKeyHook();
        }

        // Raw input tells which device produced a wheel event; it arrives before the hook call.
        void RegisterRawInput()
        {
            var devices = new[]
            {
                new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = Native.RIDEV_INPUTSINK, Target = Handle },
                new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = Native.RIDEV_INPUTSINK, Target = Handle },
            };
            if (!Native.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE))))
                Program.LogError(new Win32Exception(), "raw input");
        }

        // Safe to call from any thread; the action runs on the UI thread.
        void Post(Action action)
        {
            lock (pending) pending.Enqueue(action);
            Native.PostMessage(Handle, WM_RUN_QUEUE, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_RUN_QUEUE)
            {
                while (true)
                {
                    Action action;
                    lock (pending)
                    {
                        if (pending.Count == 0) break;
                        action = pending.Dequeue();
                    }
                    try { action(); }
                    catch (Exception ex) { Program.LogError(ex, "queued action"); }
                }
                return;
            }
            if (m.Msg == Native.WM_INPUT)
            {
                ProcessRawInput(m.LParam);
            }
            else if (m.Msg == ShowMessage && ShowMessage != 0)
            {
                if (ShowRequested != null) ShowRequested(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        void ProcessRawInput(IntPtr hRawInput)
        {
            uint size = RawBufferSize;
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, rawBuffer, ref size, rawHeaderSize) <= 0) return;
            uint type = (uint)Marshal.ReadInt32(rawBuffer, 0);
            IntPtr device = Marshal.ReadIntPtr(rawBuffer, 8);
            int body = (int)rawHeaderSize;
            if (type == Native.RIM_TYPEMOUSE)
            {
                ushort buttonFlags = (ushort)Marshal.ReadInt16(rawBuffer, body + 4);
                if ((buttonFlags & Native.RI_MOUSE_HWHEEL) != 0)
                {
                    rawWheelDevice = device;
                    rawWheelAt = Now;
                }
            }
            else if (type == Native.RIM_TYPEKEYBOARD && !MacroSeen)
            {
                // A HUB macros are typed by the mouse itself through its keyboard interface.
                ushort vkey = (ushort)Marshal.ReadInt16(rawBuffer, body + 6);
                if (vkey == Native.VK_TAB && IsKeyboardOfRapooMouse(device))
                {
                    MacroSeen = true;
                    Post(delegate { if (MacroDetected != null) MacroDetected(); });
                }
            }
        }

        void DrainRawInput()
        {
            Native.MSG msg;
            while (Native.PeekMessage(out msg, Handle, Native.WM_INPUT, Native.WM_INPUT, Native.PM_REMOVE | Native.PM_QS_INPUT))
            {
                ProcessRawInput(msg.lParam);
                Native.DefWindowProc(msg.hwnd, msg.message, msg.wParam, msg.lParam);
            }
        }

        IntPtr TakeRawWheelDevice()
        {
            IntPtr device = rawWheelAt >= 0 && Now - rawWheelAt <= RawFreshMs ? rawWheelDevice : IntPtr.Zero;
            rawWheelAt = -1;
            return device;
        }

        // Returns the Rapoo product id of a device, or null for other vendors.
        string RapooPid(IntPtr device)
        {
            string pid;
            if (rapooPids.TryGetValue(device, out pid)) return pid;
            Match match = RapooId.Match(DeviceName(device) ?? "");
            pid = match.Success ? match.Groups[1].Value : null;
            rapooPids[device] = pid;
            return pid;
        }

        bool IsRapoo(IntPtr device)
        {
            return device != IntPtr.Zero && RapooPid(device) != null;
        }

        bool IsKeyboardOfRapooMouse(IntPtr device)
        {
            string pid = RapooPid(device);
            if (pid == null) return false;
            if (rapooMousePids.Count == 0) FindRapooMice();
            return rapooMousePids.Contains(pid);
        }

        void FindRapooMice()
        {
            uint count = 0;
            uint itemSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            if (Native.GetRawInputDeviceList(null, ref count, itemSize) != 0 || count == 0) return;
            var list = new Native.RAWINPUTDEVICELIST[count];
            int got = Native.GetRawInputDeviceList(list, ref count, itemSize);
            for (int i = 0; i < got; i++)
            {
                if (list[i].dwType != Native.RIM_TYPEMOUSE) continue;
                string pid = RapooPid(list[i].hDevice);
                if (pid != null) rapooMousePids.Add(pid);
            }
        }

        static string DeviceName(IntPtr device)
        {
            if (device == IntPtr.Zero) return null;
            uint size = 0;
            Native.GetRawInputDeviceInfo(device, Native.RIDI_DEVICENAME, null, ref size);
            if (size == 0) return null;
            var name = new StringBuilder((int)size + 1);
            if (Native.GetRawInputDeviceInfo(device, Native.RIDI_DEVICENAME, name, ref size) <= 0) return null;
            return name.ToString();
        }

        IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    if (OnMouse((int)wParam, lParam)) return (IntPtr)1;
                }
                catch (Exception ex) { Program.LogError(ex, "mouse hook"); }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        // Returns true to swallow the event.
        bool OnMouse(int message, IntPtr lParam)
        {
            if (message == Native.WM_MOUSEMOVE && !active) return false;
            var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
            if (info.dwExtraInfo == Marker) return false;
            int delta = (short)(info.mouseData >> 16);
            switch (message)
            {
                case Native.WM_MOUSEHWHEEL:
                    return OnSideWheel(delta);

                case Native.WM_MOUSEWHEEL:
                    if (!active) return false;
                    Post(delegate { OnMainWheel(delta); });
                    return true;

                case Native.WM_MBUTTONDOWN:
                    if (!active) return false;
                    swallowMiddleUp = true;
                    Post(delegate { RunClickAction(settings.MiddleClickAction); });
                    return true;

                case Native.WM_MBUTTONUP:
                    if (!swallowMiddleUp) return false;
                    swallowMiddleUp = false;
                    return true;

                case Native.WM_RBUTTONDOWN:
                    if (!active) return false;
                    swallowRightUp = true;
                    Post(delegate { RunClickAction(settings.RightClickAction); });
                    return true;

                case Native.WM_RBUTTONUP:
                    if (!swallowRightUp) return false;
                    swallowRightUp = false;
                    return true;

                case Native.WM_LBUTTONDOWN:
                {
                    if (!active) return false;
                    if (IsSwitcher(Native.GetAncestor(Native.WindowFromPoint(info.pt), Native.GA_ROOT)))
                    {
                        // Windows handles it: a thumbnail opens that window, its X closes it.
                        Post(delegate
                        {
                            Touch();
                            windowsBeforeClick = VisibleWindows();
                        });
                        return false;
                    }
                    // Taskbar, another monitor, or the list not on screen yet: keep the click out of other windows.
                    swallowLeftUp = true;
                    Post(delegate { RunClickAction(settings.LeftClickAction); });
                    return true;
                }

                case Native.WM_LBUTTONUP:
                    if (swallowLeftUp)
                    {
                        swallowLeftUp = false;
                        return true;
                    }
                    if (active)
                    {
                        Post(delegate
                        {
                            int id = switchId;
                            After(ClickSettleMs, delegate { if (active && id == switchId) AfterSwitcherClick(); });
                        });
                    }
                    return false;

                case Native.WM_MOUSEMOVE:
                    if (settings.KeepOpenWhileMoving &&
                        (Math.Abs(info.pt.X - anchor.X) > MoveThresholdPx || Math.Abs(info.pt.Y - anchor.Y) > MoveThresholdPx))
                    {
                        anchor = info.pt;
                        Post(Touch);
                    }
                    return false;
            }
            return false;
        }

        bool OnSideWheel(int delta)
        {
            DrainRawInput();
            IntPtr device = TakeRawWheelDevice();
            bool rapoo = IsRapoo(device);
            // Shift + side wheel keeps ordinary horizontal scrolling (timelines, wide tables).
            bool shift = !active && (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0;
            string result = !settings.Enabled ? Lang.T("утилита выключена, обычная прокрутка", "helper disabled, normal scrolling")
                : settings.RapooOnly && !rapoo ? Lang.T("не Rapoo, обычная прокрутка", "not Rapoo, normal scrolling")
                : shift ? Lang.T("зажат Shift, обычная прокрутка", "Shift held, normal scrolling")
                : null;
            string source = device == IntPtr.Zero ? Lang.T("устройство не определено", "unknown device")
                : rapoo ? Lang.T("мышь Rapoo", "Rapoo mouse")
                : Lang.T("другое устройство", "other device");
            Post(delegate { ReportWheel(delta, source, result ?? Lang.T("переключаю окна", "switching windows")); });
            if (result != null) return false;
            Post(delegate { OnSideWheelStep(delta); });
            return true;
        }

        void ReportWheel(int delta, string source, string result)
        {
            string direction = delta > 0 ? Lang.T("Колесо вправо", "Wheel right") : Lang.T("Колесо влево", "Wheel left");
            if (WheelSeen != null) WheelSeen(direction + " · " + source + " · " + result);
        }

        void OnSideWheelStep(int delta)
        {
            int steps = Accumulate(ref sideAccum, ref lastSideWheelAt, delta, WheelNotch * Math.Max(1, settings.NotchesPerStep));
            if (settings.Invert) steps = -steps;
            StepBy(steps);
        }

        void OnMainWheel(int delta)
        {
            // Wheel down moves forward through the list, like scrolling down a page.
            StepBy(-Accumulate(ref mainAccum, ref lastMainWheelAt, delta, WheelNotch));
        }

        int Accumulate(ref int accum, ref long lastAt, int delta, int threshold)
        {
            long now = Now;
            if (now - lastAt > WheelResetMs || Math.Sign(delta) != Math.Sign(accum)) accum = 0;
            lastAt = now;
            accum += delta;
            int steps = accum / threshold;
            accum -= steps * threshold;
            return steps;
        }

        void StepBy(int steps)
        {
            for (int i = 0; i < Math.Abs(steps); i++) Step(steps > 0);
            if (active) Touch();
        }

        void Step(bool forward)
        {
            if (!active) Begin();
            if (selectionLost)
            {
                selectionLost = false;
                RestoreSelection();
            }
            position += forward ? 1 : -1;
            if (forward)
                Send(Key(Native.VK_TAB, false), Key(Native.VK_TAB, true));
            else
                Send(Key(Native.VK_LSHIFT, false), Key(Native.VK_TAB, false), Key(Native.VK_TAB, true), Key(Native.VK_LSHIFT, true));
        }

        void Begin()
        {
            active = true;
            startedAt = Now;
            switchId++;
            position = 0;
            selectionLost = false;
            Program.Trace("begin switch " + switchId);
            Native.GetCursorPos(out anchor);
            InstallKeyHook();
            Send(Key(Native.VK_LMENU, false));
            timer.Start();
        }

        void Touch()
        {
            if (!active) return;
            lastActivityAt = Now;
            if (Countdown != null) Countdown(settings.DelayMs, settings.DelayMs);
        }

        void Tick()
        {
            if (!active) { timer.Stop(); return; }
            long now = Now;
            int remaining = settings.DelayMs - (int)(now - lastActivityAt);
            if (remaining <= 0 || now - startedAt > MaxHoldMs)
            {
                Finish(false, remaining <= 0 ? "timeout" : "max hold");
                return;
            }
            if (Countdown != null) Countdown(remaining, settings.DelayMs);
        }

        void RunClickAction(ClickAction action)
        {
            if (action == ClickAction.Open) Finish(false, "click: open");
            else if (action == ClickAction.Cancel) Finish(true, "click: cancel");
            else Touch();
        }

        // Windows has handled a left click inside the switcher:
        // - the list is gone: a thumbnail was clicked and Windows switched to it;
        // - the list is open and a window disappeared: its X was clicked; carry on choosing;
        // - the list is open and nothing closed: empty space; run the configured action.
        // After a click the Windows 11 switcher ignores releasing Alt and Enter until Tab is pressed,
        // and Tab then starts from the first window, so the selection has to be rebuilt (selectionLost).
        void AfterSwitcherClick()
        {
            if (!IsSwitcher(Native.GetForegroundWindow()))
            {
                Finish(false, "thumbnail click");
                return;
            }
            bool closed = false;
            foreach (IntPtr hwnd in windowsBeforeClick)
                if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) closed = true;
            Program.Trace("switcher click: window closed=" + closed + " position=" + position);
            selectionLost = true;
            if (closed) Touch();
            else RunClickAction(settings.LeftClickAction);
        }

        // Tab selects the first window (index 0), Shift+Tab the last one (index -1); then step to `position`.
        void RestoreSelection()
        {
            var keys = new List<Native.INPUT>();
            bool forward = position >= 0;
            for (int i = 0; i < (forward ? position + 1 : -position); i++)
            {
                if (!forward) keys.Add(Key(Native.VK_LSHIFT, false));
                keys.Add(Key(Native.VK_TAB, false));
                keys.Add(Key(Native.VK_TAB, true));
                if (!forward) keys.Add(Key(Native.VK_LSHIFT, true));
            }
            Send(keys.ToArray());
        }

        // Windows of the kind Alt+Tab lists: visible, titled, unowned, not tool windows, not cloaked.
        // Tooltips and the switcher's own helper windows come and go by themselves and must not count.
        static List<IntPtr> VisibleWindows()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (Native.IsWindowVisible(hwnd) &&
                    Native.GetWindow(hwnd, Native.GW_OWNER) == IntPtr.Zero &&
                    (Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) == 0 &&
                    Native.GetWindowTextLength(hwnd) > 0 &&
                    !IsCloaked(hwnd) && !IsSwitcher(hwnd))
                    list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            return Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        static bool IsSwitcher(IntPtr hwnd)
        {
            var name = new StringBuilder(64);
            Native.GetClassName(hwnd, name, name.Capacity);
            return Array.IndexOf(SwitcherClasses, name.ToString()) >= 0;
        }

        static void After(int ms, Action action)
        {
            var once = new Timer { Interval = ms };
            once.Tick += delegate
            {
                once.Dispose();
                action();
            };
            once.Start();
        }

        // Releases Alt, which makes Windows switch to the selected window (or, with cancel, stay put).
        void Finish(bool cancel, string why = "")
        {
            Program.Trace("finish cancel=" + cancel + " (" + why + ") active=" + active);
            if (!active) return;
            if (!cancel && selectionLost)
            {
                // Rebuild the selection, then give Windows a moment to take the Tabs before Alt goes up.
                selectionLost = false;
                timer.Stop();
                RestoreSelection();
                int id = switchId;
                After(RestoreSettleMs, delegate { if (active && id == switchId) Finish(false, why); });
                return;
            }
            active = false;
            timer.Stop();
            var keys = new List<Native.INPUT>();
            if (cancel)
            {
                keys.Add(Key(Native.VK_ESCAPE, false));
                keys.Add(Key(Native.VK_ESCAPE, true));
            }
            keys.Add(Key(Native.VK_MASK, false));
            keys.Add(Key(Native.VK_MASK, true));
            keys.Add(Key(Native.VK_LMENU, true));
            Send(keys.ToArray());
            RemoveKeyHookIfIdle();
            if (Ended != null) Ended();
        }

        IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    if (OnKey((int)wParam, lParam)) return (IntPtr)1;
                }
                catch (Exception ex) { Program.LogError(ex, "keyboard hook"); }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        // Installed only while the switcher is open, so typing never goes out with Alt held.
        bool OnKey(int message, IntPtr lParam)
        {
            var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            if ((info.flags & Native.LLKHF_INJECTED) != 0) return false;
            uint vk = info.vkCode;
            bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
            if (!down)
            {
                if (swallowedKeys.Remove(vk))
                {
                    Post(RemoveKeyHookIfIdle);
                    return true;
                }
                // Releasing the physical Alt already closed the switcher.
                if (active && (vk == Native.VK_LMENU || vk == Native.VK_RMENU || vk == Native.VK_MENU))
                    Post(delegate { Finish(false, "physical Alt up"); });
                return false;
            }
            if (!active) return false;
            switch (vk)
            {
                case Native.VK_ESCAPE:
                    // Windows closes the switcher on Esc; only Alt needs releasing.
                    Post(delegate { Finish(false, "Esc"); });
                    return false;

                case Native.VK_TAB:
                case Native.VK_LEFT:
                case Native.VK_RIGHT:
                case Native.VK_UP:
                case Native.VK_DOWN:
                    Post(Touch);
                    return false;

                case Native.VK_SHIFT:
                case Native.VK_LSHIFT:
                case Native.VK_RSHIFT:
                case Native.VK_CONTROL:
                case Native.VK_LCONTROL:
                case Native.VK_RCONTROL:
                case Native.VK_MENU:
                case Native.VK_LMENU:
                case Native.VK_RMENU:
                    return false;

                default:
                    // Enter, Space or any other key: switch now and drop the key instead of sending Alt+key.
                    swallowedKeys.Add(vk);
                    Post(delegate { Finish(false, "key 0x" + vk.ToString("X2")); });
                    return true;
            }
        }

        static Native.INPUT Key(ushort vk, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki = new Native.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)Native.MapVirtualKey(vk, 0),
                dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = Marker,
            };
            return input;
        }

        static void Send(params Native.INPUT[] inputs)
        {
            if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT))) != inputs.Length)
                Program.LogError(new Win32Exception(), "SendInput");
        }
    }
}
