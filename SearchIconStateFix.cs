using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.Win32;
using System.Windows.Automation;
using System.Windows.Forms;

// Repairs a stale colored Search glyph, without changing the normal open/closed styling.
// Uses a small native XAML diagnostics bridge to restore only the icon animation.
// No Search toggling, input injection, focus change, Search registry write, or system-file patch.
internal static class SearchIconStateFix
{
    internal static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SearchIconStateFix");
    internal const string Caption = "Search Icon State Fix";
    internal static readonly string InstalledExe = Path.Combine(Dir, "SearchIconStateFix.exe");
    internal static bool Chinese { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; } }
    internal static string L(string en, string zh) { return Chinese ? zh : en; }
    [STAThread] static void Main(string[] args)
    {
        try {
            Directory.CreateDirectory(Dir);
            if (Array.IndexOf(args, "--stop") >= 0) { RequestStop(); return; }
            // Install a stable current-user copy so the downloaded EXE can be moved/deleted.
            string self = Assembly.GetExecutingAssembly().Location;
            if (!String.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase)) {
                RequestStop();
                for (int i = 0; i < 30 && Native.FindWindow(null, "SearchIconStateFix.Control.v3") != IntPtr.Zero; i++) Thread.Sleep(100);
                for (int attempt = 0; ; attempt++) {
                    try { File.Copy(self, InstalledExe, true); break; }
                    catch (IOException) { if (attempt >= 29) throw; Thread.Sleep(100); }
                }
                Process.Start(new ProcessStartInfo(InstalledExe, Array.IndexOf(args, "--background") >= 0 ? "--background" : "") { UseShellExecute = false });
                return;
            }
            bool owned;
            using (var mutex = new Mutex(true, "Local\\SearchIconStateFix-" + Process.GetCurrentProcess().SessionId, out owned)) {
                if (!owned) {
                    IntPtr h = Native.FindWindow(null, "SearchIconStateFix.Control.v3");
                    if (h != IntPtr.Zero && Array.IndexOf(args, "--background") < 0) Native.PostMessage(h, 0x8001, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Payload.Ensure();
                using (var context = new FixContext(Array.IndexOf(args, "--background") >= 0)) Application.Run(context);
            }
        } catch (Exception ex) { Log("startup failed: " + ex); MessageBox.Show(ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    static void RequestStop() {
        IntPtr h = Native.FindWindow(null, "SearchIconStateFix.Control.v3");
        if (h != IntPtr.Zero) Native.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
    }
    internal static class Payload {
        internal static string NativeDir;
        internal static void Ensure() {
            ushort processMachine, nativeMachine;
            if (!Native.IsWow64Process2(Process.GetCurrentProcess().Handle, out processMachine, out nativeMachine)) throw new InvalidOperationException("Cannot identify Windows architecture.");
            string arch = nativeMachine == 0xaa64 ? "arm64" : nativeMachine == 0x8664 ? "x64" : null;
            if (arch == null) throw new PlatformNotSupportedException("Windows 11 ARM64 or x64 is required.");
            foreach (string name in new[] { "AnimationBridgeHost.exe", "SearchAnimationBridge.dll" }) {
                byte[] bytes;
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Native." + arch + "." + name + ".gz")) {
                    if (resource == null) throw new InvalidDataException("Missing native payload: " + arch + "/" + name);
                    using (var gzip = new GZipStream(resource, CompressionMode.Decompress)) using (var memory = new MemoryStream()) { gzip.CopyTo(memory); bytes = memory.ToArray(); }
                }
                string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                string folder = Path.Combine(Dir, "native", arch + "-" + hash.Substring(0, 16));
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, name);
                if (File.Exists(target)) {
                    string existing; using (var sha = SHA256.Create()) existing = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(target))).Replace("-", "").ToLowerInvariant();
                    if (existing != hash) throw new InvalidDataException("Native payload checksum mismatch: " + name);
                } else File.WriteAllBytes(target, bytes);
                if (name.EndsWith(".dll")) NativeDir = folder;
                if (name.EndsWith(".exe")) HostPath = target;
                else DllPath = target;
            }
        }
        internal static string HostPath, DllPath;
    }
    internal static class Startup {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool Enabled {
            get { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && String.Equals(k.GetValue("SearchIconStateFix") as string, "\"" + InstalledExe + "\" --background", StringComparison.OrdinalIgnoreCase); }
        }
        internal static void Set(bool enabled) {
            // Migrate only this project's old SID-specific logon task, never other tasks.
            object service = null;
            try {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                if (t != null) {
                    service = Activator.CreateInstance(t);
                    t.InvokeMember("Connect", BindingFlags.InvokeMethod, null, service, new object[] { Type.Missing, Type.Missing, Type.Missing, Type.Missing });
                    object folder = t.InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, service, new object[] { "\\" });
                    try { folder.GetType().InvokeMember("DeleteTask", BindingFlags.InvokeMethod, null, folder, new object[] { "SearchIconStateFix-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value, 0 }); }
                    catch (Exception ex) { var cause = ex is TargetInvocationException ? ex.InnerException : ex; if (cause == null || (cause.HResult != unchecked((int)0x80070002) && cause.HResult != unchecked((int)0x8004130f))) throw; }
                    finally { Marshal.FinalReleaseComObject(folder); }
                }
            } catch (Exception ex) { Log("Legacy task migration skipped: " + ex); }
            finally { if (service != null && Marshal.IsComObject(service)) Marshal.FinalReleaseComObject(service); }
            using (var k = Registry.CurrentUser.CreateSubKey(Key)) {
                if (enabled) k.SetValue("SearchIconStateFix", "\"" + InstalledExe + "\" --background", RegistryValueKind.String);
                else k.DeleteValue("SearchIconStateFix", false);
            }
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
        DateTime lastAttach = DateTime.MinValue;
        string reason = "startup";
        volatile bool stopping;
        bool? previouslyVisible;
        SettingsForm form;
        internal FixContext(bool background) {
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
            Log("started pid=" + Process.GetCurrentProcess().Id + " version=0.2.1-preview.1; event-driven, no periodic polling");
            if (!background) ShowUI();
            Schedule("startup", 1500);
        }
        internal void ShowUI() {
            if (form == null || form.IsDisposed) form = new SettingsForm(this);
            form.RefreshStartup(); form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
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
                    if (repairStage != 0) Log("verified monochrome after animation frame restore");
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
                    if (!BridgePresent()) {
                        if (AttachBridge()) Schedule("wait-for-animation-bridge", 1500);
                        else { Log("animation bridge unavailable; no disruptive fallback"); staleSince = DateTime.MinValue; }
                        return;
                    }
                    lastRepair = DateTime.UtcNow;
                    Log("stale colored icon; toggle=" + toggle + " search-panel-hidden; restoring animation only; trigger=" + reason);
                    if (SearchVisible()) { staleSince = DateTime.MinValue; return; }
                    int applied = RestoreAnimationFrame();
                    Log("animation bridge applied=" + applied + "; no Search toggle or input/focus operation");
                    repairStage = 1; Schedule("verify-animation-restore", 700);
                } else {
                    Log("warning: icon remains colored after animation restore; no panel/settings fallback");
                    repairStage = 0; staleSince = DateTime.MinValue;
                }
            } catch (Exception ex) { Log("check skipped: " + ex.ToString()); staleSince = DateTime.MinValue; repairStage = 0; }
        }
        const string BridgeClass = "SearchIconStateFix.AnimationBridge.v3";
        bool BridgePresent() {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(new IntPtr(-3), h, BridgeClass, null)) != IntPtr.Zero) {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == explorerPid) return true;
            }
            return false;
        }
        bool AttachBridge() {
            if ((DateTime.UtcNow - lastAttach).TotalSeconds < 30) return false;
            lastAttach = DateTime.UtcNow;
            string host = Payload.HostPath, dll = Payload.DllPath;
            if (!File.Exists(host) || !File.Exists(dll)) { Log("animation bridge files missing; leaving Search untouched"); return false; }
            var start = new ProcessStartInfo(host, explorerPid + " \"" + dll + "\"") {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir
            };
            using (var process = Process.Start(start)) {
                if (process.WaitForExit(5000)) Log("animation bridge attach exit=" + process.ExitCode);
                else Log("animation bridge attachment still pending");
            }
            return true;
        }
        int RestoreAnimationFrame() {
            int applied = 0; IntPtr h = IntPtr.Zero;
            uint message = Native.RegisterWindowMessage("SearchIconStateFix.RestoreIdleAnimation.v3");
            while ((h = Native.FindWindowEx(new IntPtr(-3), h, BridgeClass, null)) != IntPtr.Zero) {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid != explorerPid) continue;
                IntPtr result;
                if (Native.SendMessageTimeout(h, message, IntPtr.Zero, IntPtr.Zero, 2, 1000, out result) != IntPtr.Zero && result.ToInt64() == 1) applied++;
            }
            return applied;
        }
        internal void Stop() { stopping = true; ExitThread(); }
        protected override void ExitThreadCore() {
            lock(scheduleGate) { stopping = true; timer.Change(Timeout.Infinite, Timeout.Infinite); }
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            if (visibilityHook != IntPtr.Zero) Native.UnhookWinEvent(visibilityHook);
            // Detach the event observer as well; no icon correction continues after Exit.
            IntPtr h = IntPtr.Zero; uint detach = Native.RegisterWindowMessage("SearchIconStateFix.DetachAnimationBridge.v3");
            while ((h = Native.FindWindowEx(new IntPtr(-3), h, BridgeClass, null)) != IntPtr.Zero) {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == explorerPid) { IntPtr result; Native.SendMessageTimeout(h, detach, IntPtr.Zero, IntPtr.Zero, 2, 1000, out result); }
            }
            if (form != null) { form.AllowClose = true; form.Dispose(); }
            window.Dispose(); timer.Dispose(); Log("stopped"); base.ExitThreadCore();
        }
    }
    internal sealed class SettingsForm : Form {
        readonly FixContext owner; readonly CheckBox startup; bool refreshing;
        internal bool AllowClose;
        internal SettingsForm(FixContext owner) {
            this.owner = owner; Text = Caption; Icon = Icon.ExtractAssociatedIcon(InstalledExe); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.None;
            float dpi; using (var graphics = CreateGraphics()) dpi = graphics.DpiX / 96f;
            Func<int, int> px = delegate(int value) { return (int)Math.Round(value * dpi); };
            ClientSize = new Size(px(480), px(320)); MinimumSize = Size; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedSingle;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(px(24)), ColumnCount = 1, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(40))); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(34)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(63))); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(42)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, px(38)));
            layout.Controls.Add(new Label { Text = Caption, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
            layout.Controls.Add(new Label { Text = L("Monitoring  ·  Experimental preview", "正在监测  ·  实验性预览"), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(38, 93, 63) }, 0, 1);
            layout.Controls.Add(new Label { Text = L("Restores the Search icon's idle image when it stays colored after Search closes. Long-term effectiveness is still being evaluated.", "搜索已关闭、图标却仍为彩色时，恢复图标的正常画面。\n长期有效性仍待验证。"), Dock = DockStyle.Fill }, 0, 2);
            startup = new CheckBox { Text = L("Start automatically when I sign in", "登录 Windows 时自动启动"), Dock = DockStyle.Fill, AutoSize = true, AccessibleName = "Start with Windows" };
            startup.CheckedChanged += delegate {
                if (refreshing) return;
                try { Startup.Set(startup.Checked); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L("Startup setting failed", "自启设置失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); RefreshStartup(); }
            };
            layout.Controls.Add(startup, 0, 3);
            var note = new Label { Text = L("Hide runs invisibly in the background. Open the desktop shortcut to show settings again. Closing this window exits.", "隐藏后在后台运行，不显示任务栏或托盘图标。\n再次双击桌面入口可打开；关闭窗口即退出。"), ForeColor = SystemColors.GrayText, Dock = DockStyle.Fill };
            layout.Controls.Add(note, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var exit = new Button { Text = L("Exit", "退出"), AutoSize = true, Height = px(32), AccessibleName = "Exit" }; exit.Click += delegate { owner.Stop(); };
            var hide = new Button { Text = L("Hide", "隐藏"), AutoSize = true, Height = px(32), AccessibleName = "Hide" }; hide.Click += delegate { Hide(); };
            var logs = new Button { Text = L("Logs", "日志"), AutoSize = true, Height = px(32), AccessibleName = "Logs" }; logs.Click += delegate { Process.Start(new ProcessStartInfo(Dir) { UseShellExecute = true }); };
            buttons.Controls.Add(exit); buttons.Controls.Add(hide); buttons.Controls.Add(logs); layout.Controls.Add(buttons, 0, 5);
            Controls.Add(layout);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!AllowClose) { e.Cancel = true; BeginInvoke(new Action(owner.Stop)); } };
            RefreshStartup();
        }
        internal void RefreshStartup() { refreshing = true; try { startup.Checked = Startup.Enabled; } finally { refreshing = false; } }
    }
    internal sealed class WatchWindow : NativeWindow, IDisposable
    {
        readonly FixContext owner;
        IntPtr power;
        readonly uint taskbarCreated;
        internal WatchWindow(FixContext owner) {
            this.owner = owner;
            CreateHandle(new CreateParams { Caption = "SearchIconStateFix.Control.v3", Style = 0 });
            Guid guid = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
            power = Native.RegisterPowerSettingNotification(Handle, ref guid, 0);
            Native.WTSRegisterSessionNotification(Handle, 0);
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x10) { owner.Stop(); return; }
            if (m.Msg == 0x8001) { owner.ShowUI(); return; }
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
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr RegisterPowerSettingNotification(IntPtr h,ref Guid guid,uint flags);
        [DllImport("user32.dll")] internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(IntPtr h,uint flags);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr h);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    }
}
