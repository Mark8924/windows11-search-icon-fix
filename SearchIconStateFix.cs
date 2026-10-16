using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

// Repairs a stale colored Search glyph, without changing the normal open/closed styling.
// No clicks, cursor movement, DLL injection, process restart, or permanent search-policy changes.
internal static class SearchIconStateFix
{
    internal static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SearchIconStateFix");
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(Dir);
        if (args.Length > 0 && args[0] == "--stop") {
            IntPtr h = Native.FindWindow(null, "Search Icon State Fix");
            if (h != IntPtr.Zero) Native.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        bool owned;
        using (var mutex = new Mutex(true, "Local\\SearchIconStateFix-" + Process.GetCurrentProcess().SessionId, out owned)) {
            if (!owned) return;
            Native.SetProcessDPIAware();
            using (var context = new FixContext()) Application.Run(context);
        }
    }
    internal static void Log(string message) {
        try {
            string path = Path.Combine(Dir, "events.log");
            if (File.Exists(path) && new FileInfo(path).Length > 131072) File.Move(path, path + ".old." + DateTime.UtcNow.Ticks);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
        } catch { }
    }
    internal sealed class FixContext : ApplicationContext
    {
        readonly WatchWindow window;
        readonly System.Threading.Timer timer;
        readonly object scheduleGate = new object(), windowsGate = new object();
        bool pending;
        int checking;
        readonly Native.WinEvent callback;
        IntPtr foregroundHook, visibilityHook;
        uint explorerPid;
        readonly HashSet<IntPtr> searchWindows = new HashSet<IntPtr>();
        DateTime staleSince = DateTime.MinValue, lastRepair = DateTime.MinValue;
        int repairStage;
        string reason = "startup";
        volatile bool stopping;
        bool? previouslyVisible;
        internal FixContext() {
            // UI Automation belongs on an MTA worker, never the STA window-message thread.
            timer = new System.Threading.Timer(delegate {
                lock (scheduleGate) pending = false;
                if (Interlocked.Exchange(ref checking, 1) != 0) { Schedule("busy", 600); return; }
                try { Check(); } finally { Interlocked.Exchange(ref checking, 0); }
            }, null, Timeout.Infinite, Timeout.Infinite);
            window = new WatchWindow(this);
            callback = OnWinEvent;
            foregroundHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
            visibilityHook = Native.SetWinEventHook(0x8002, 0x8003, IntPtr.Zero, callback, 0, 0, 2);
            Log("started pid=" + Process.GetCurrentProcess().Id + " version=0.1.0-preview.1; event-driven, no periodic polling");
            Schedule("startup", 1500);
        }
        internal void Schedule(string why, int delay) {
            lock (scheduleGate) {
                if (stopping) return;
                reason = why;
                // Keep an existing deadline: busy UI event streams must not postpone a check indefinitely.
                if (!pending) { pending = true; timer.Change(delay, Timeout.Infinite); }
            }
        }
        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time) {
            if (ev == 3) { Schedule("foreground", 700); return; }
            if (obj != 0 || hwnd == IntPtr.Zero) return;
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            bool known; lock(windowsGate) known = searchWindows.Contains(hwnd);
            if (known || (pid != 0 && pid == explorerPid)) Schedule("shell-visibility", 700);
        }
        AutomationElement SearchElement(out IntPtr taskbar) {
            taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return null;
            Native.GetWindowThreadProcessId(taskbar, out explorerPid);
            return AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SearchButton"));
        }
        bool SearchVisible() {
            bool visible = false;
            lock(windowsGate) searchWindows.Clear();
            var pids = new HashSet<uint>();
            foreach (string name in new[] { "SearchHost", "SearchApp", "SearchUI" })
                foreach (Process p in Process.GetProcessesByName(name)) using (p) {
                    try { if (p.SessionId == Process.GetCurrentProcess().SessionId) pids.Add((uint)p.Id); } catch { }
                }
            uint foregroundPid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out foregroundPid);
            bool active = pids.Contains(foregroundPid); // Also protect Search before its first frame.
            Native.EnumWindows(delegate(IntPtr h, IntPtr param) {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;
                lock(windowsGate) searchWindows.Add(h);
                Native.RECT r; int cloaked = 0;
                Native.DwmGetWindowAttribute(h, 14, out cloaked, 4);
                if (Native.IsWindowVisible(h) && cloaked == 0 && Native.GetWindowRect(h, out r) && r.Right - r.Left > 100 && r.Bottom - r.Top > 100) visible = true;
                return true;
            }, IntPtr.Zero);
            return active || visible;
        }
        static bool Colored(System.Windows.Rect r) {
            // Capture only the glyph at the center of SearchButton, never app content or full-screen pixels.
            int side = Math.Max(16, Math.Min(64, (int)(Math.Min(r.Width, r.Height) * 0.60)));
            int x = (int)(r.X + r.Width / 2) - side / 2;
            int y = (int)(r.Y + r.Height / 2) - side / 2;
            int colored = 0;
            using (var bmp = new Bitmap(side, side)) {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
                for (int j = 0; j < side; j += 2) for (int i = 0; i < side; i += 2) {
                    Color c = bmp.GetPixel(i, j);
                    int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (max - min > 48 && max > 85 && (c.B > c.R + 22 || c.G > c.R + 22)) colored++;
                }
            }
            return colored >= 10;
        }
        void Check() {
            try {
                IntPtr tray; AutomationElement e = SearchElement(out tray);
                if (e == null || e.Current.IsOffscreen || !Native.IsWindowVisible(tray)) { staleSince = DateTime.MinValue; repairStage = 0; return; }
                bool visible = SearchVisible();
                if (previouslyVisible != visible) { Log(visible ? "search panel active; correction paused" : "search panel closed"); previouslyVisible = visible; }
                if (visible) { staleSince = DateTime.MinValue; repairStage = 0; return; }
                var rect = e.Current.BoundingRectangle;
                Native.RECT tr;
                if (rect.IsEmpty || !Native.GetWindowRect(tray, out tr) || rect.Left < tr.Left || rect.Top < tr.Top || rect.Right > tr.Right || rect.Bottom > tr.Bottom) return;
                if (!Colored(rect)) {
                    if (repairStage != 0) Log("verified monochrome after " + (repairStage == 1 ? "pointer refresh" : "button recreation"));
                    staleSince = DateTime.MinValue; repairStage = 0; return;
                }
                ToggleState toggle = ToggleState.Off; object pattern;
                if (e.TryGetCurrentPattern(TogglePattern.Pattern, out pattern)) toggle = ((TogglePattern)pattern).Current.ToggleState;
                if (staleSince == DateTime.MinValue) { staleSince = DateTime.UtcNow; Schedule("confirm-stale", toggle == ToggleState.On ? 5000 : 800); return; }
                // Give a slow first Search launch time to become visible. Opening/closing stays native.
                double minimum = toggle == ToggleState.On ? 5.0 : 0.7;
                if ((DateTime.UtcNow - staleSince).TotalSeconds < minimum) { Schedule("confirm-stale", 600); return; }
                if ((DateTime.UtcNow - lastRepair).TotalSeconds < 10 && repairStage == 0) return;
                if (repairStage == 0) {
                    lastRepair = DateTime.UtcNow;
                    Log("stale colored icon; toggle=" + toggle + " search-panel-hidden; trigger=" + reason);
                    RefreshPointer(e, rect); repairStage = 1; Schedule("verify-pointer-refresh", 500);
                } else if (repairStage == 1) {
                    // Recheck just before any UI setting change, in case the user opened Search meanwhile.
                    if (SearchVisible()) { repairStage = 0; staleSince = DateTime.MinValue; return; }
                    RecreateButton(tray); repairStage = 2; Schedule("verify-button-recreation", 700);
                } else {
                    Log("warning: icon remains colored after refresh; no further changes until next event");
                    repairStage = 0; staleSince = DateTime.MinValue;
                }
            } catch (Exception ex) { Log("check skipped: " + ex.GetType().Name + ": " + ex.Message); staleSince = DateTime.MinValue; repairStage = 0; }
        }
        static void RefreshPointer(AutomationElement e, System.Windows.Rect r) {
            AutomationElement parent = e;
            while (parent != null && parent.Current.NativeWindowHandle == 0) parent = TreeWalker.RawViewWalker.GetParent(parent);
            if (parent == null) return;
            IntPtr h = new IntPtr(parent.Current.NativeWindowHandle), result;
            var p = new Native.POINT { X = (int)(r.X + r.Width / 2), Y = (int)(r.Y + r.Height / 2) };
            Native.ScreenToClient(h, ref p);
            IntPtr xy = new IntPtr((p.Y << 16) | (p.X & 0xffff));
            Native.SendMessageTimeout(h, 0x200, IntPtr.Zero, xy, 2, 500, out result);
            Native.SendMessageTimeout(h, 0x2a3, IntPtr.Zero, IntPtr.Zero, 2, 500, out result);
            Native.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero, 0x181);
        }
        static void RecreateButton(IntPtr tray) {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true)) {
                if (key == null || Convert.ToInt32(key.GetValue("SearchboxTaskbarMode", -1)) != 1) return;
                IntPtr result;
                try {
                    key.SetValue("SearchboxTaskbarMode", 0, RegistryValueKind.DWord);
                    Native.SendSetting(tray, 0x1a, IntPtr.Zero, "TraySettings", 2, 500, out result);
                    Thread.Sleep(250);
                } finally {
                    // Do not overwrite a different mode explicitly selected by the user concurrently.
                    if (Convert.ToInt32(key.GetValue("SearchboxTaskbarMode", -1)) == 0) key.SetValue("SearchboxTaskbarMode", 1, RegistryValueKind.DWord);
                    Native.SendSetting(tray, 0x1a, IntPtr.Zero, "TraySettings", 2, 500, out result);
                }
                Log("recreated SearchButton; original icon-only mode restored");
            }
        }
        internal void Stop() { stopping = true; ExitThread(); }
        protected override void ExitThreadCore() {
            lock(scheduleGate) { stopping = true; timer.Change(Timeout.Infinite, Timeout.Infinite); }
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            if (visibilityHook != IntPtr.Zero) Native.UnhookWinEvent(visibilityHook);
            window.Dispose(); timer.Dispose(); Log("stopped"); base.ExitThreadCore();
        }
    }
    internal sealed class WatchWindow : NativeWindow, IDisposable
    {
        readonly FixContext owner;
        IntPtr power;
        readonly uint taskbarCreated;
        internal WatchWindow(FixContext owner) {
            this.owner = owner;
            CreateHandle(new CreateParams { Caption = "Search Icon State Fix", Style = 0 });
            Guid guid = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
            power = Native.RegisterPowerSettingNotification(Handle, ref guid, 0);
            Native.WTSRegisterSessionNotification(Handle, 0);
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x10) { owner.Stop(); return; }
            if ((uint)m.Msg == taskbarCreated) owner.Schedule("taskbar-created", 1800);
            if (m.Msg == 0x2b1 && m.WParam.ToInt32() == 8) owner.Schedule("session-unlock", 1200);
            if (m.Msg == 0x218) {
                int code = m.WParam.ToInt32();
                if (code == 7 || code == 18) owner.Schedule("resume", 1200);
                if (code == 0x8013 && m.LParam != IntPtr.Zero && Marshal.ReadInt32(m.LParam, 20) == 1) owner.Schedule("display-on", 1200);
            }
            base.WndProc(ref m);
        }
        public void Dispose() {
            if (power != IntPtr.Zero) Native.UnregisterPowerSettingNotification(power);
            Native.WTSUnRegisterSessionNotification(Handle); DestroyHandle();
        }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        internal delegate void WinEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW")] internal static extern IntPtr SendSetting(IntPtr h,uint msg,IntPtr w,string l,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] internal static extern bool RedrawWindow(IntPtr h,IntPtr rect,IntPtr region,uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr RegisterPowerSettingNotification(IntPtr h,ref Guid guid,uint flags);
        [DllImport("user32.dll")] internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(IntPtr h,uint flags);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr h);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    }
}
