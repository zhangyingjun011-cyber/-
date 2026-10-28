// ============================================================================
//  AnimeUI.cs — 二次元托盘界面层
//    · 托盘图标: 猫耳能量瓶 (随电量变化 + 充电/低电量时轻微动画)
//    · 左键: 玻璃质感悬浮面板 (看板娘 + 大号电量 + 液面电量条)
//    · 低电量: 无声音的静默提醒小卡片 (从任务栏上方滑出)
//    · 右键: 深色定制菜单
//    · --icons-out=目录 导出美术资源, --preview 直接预览面板
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InphicBatteryTray9
{
    internal static class NativeUi
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // 高 DPI: 优先 PerMonitorV2, 老系统退回系统级 DPI 感知
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }
    }
    static partial class Program
    {
        // ---- 界面状态 ----
        static NotifyIcon tray;
        static Icon liveIcon;
        static string liveIconKey;
        static readonly Queue<Icon> oldIcons = new Queue<Icon>();
        static readonly Queue<IntPtr> oldHandles = new Queue<IntPtr>();
        static System.Windows.Forms.Timer uiTimer;
        static System.Windows.Forms.Timer animTimer;
        static int animFrame;
        static bool AnimationsEnabled = true;
        static BatteryBubble flyout;
        static SilentToast toast;
        static ContextMenuStrip menu;
        static ToolStripMenuItem lowItem;
        static ToolStripMenuItem autoItem;
        static DateTime lastToggle = DateTime.MinValue;
        static int iconSize = 16;
        internal static DeviceState PreviewDev;


        // ====================================================================
        // 托盘主循环
        // ====================================================================
        static void RunTray()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { CrashLog(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { CrashLog(e.ExceptionObject as Exception); };

            try
            {
                int cx = Native.GetSystemMetrics(Native.SM_CXSMICON);
                if (cx >= 16 && cx <= 128) iconSize = cx;
            }
            catch { }

            uiPump = new Form();
            uiPump.ShowInTaskbar = false;
            uiPump.FormBorderStyle = FormBorderStyle.None;
            uiPump.StartPosition = FormStartPosition.Manual;
            uiPump.Location = new Point(-32000, -32000);
            uiPump.Size = new Size(1, 1);
            uiPump.Opacity = 0.01;
            uiPump.Show();

            tray = new NotifyIcon();
            tray.Text = "IN9 电量 · 正在连接…";
            SetTrayIcon(-1, false, true);   // 必须先有图标再 Visible, 否则 notifyicon 不会进通知区
            try { tray.Visible = true; Log("ui: notifyicon visible=" + tray.Visible); }
            catch (Exception ex) { Log("ui: notifyicon failed " + ex.Message); }

            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                // 双击会连续触发两次 MouseClick, 这里做个去抖
                if ((DateTime.Now - lastToggle).TotalMilliseconds < 320) return;
                lastToggle = DateTime.Now;
                ShowBubble();
            };

            Thread supervisor = new Thread(Supervise);
            supervisor.IsBackground = true;
            supervisor.Start();

            Thread pinWatch = new Thread(PinWatch);
            pinWatch.IsBackground = true;
            pinWatch.Start();

            Thread generic = new Thread(GenericSupervise);
            generic.IsBackground = true;
            generic.Start();

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { try { UpdateTrayUI(); } catch { } };
            uiTimer.Start();

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 200;
            animTimer.Tick += delegate { try { AnimTick(); } catch { } };
            animTimer.Start();

            Log("ui: menu built, iconSize=" + iconSize);
            UpdateTrayUI();
            Log("ui: loop enter, icon=" + (liveIcon == null ? "null" : liveIcon.Width + "x" + liveIcon.Height));
            Application.Run();
        }

        static void Quit()
        {
            running = false;
            try { rescanEvent.Set(); } catch { }
            try { if (flyout != null) { flyout.Close(); flyout = null; } } catch { }
            try { if (toast != null) { toast.Close(); toast = null; } } catch { }
            try
            {
                if (tray != null)
                {
                    tray.Visible = false;
                    tray.Dispose();
                    tray = null;
                }
            }
            catch { }
            if (liveIcon != null)
            {
                IntPtr h = IntPtr.Zero;
                try { h = liveIcon.Handle; } catch { }
                try { liveIcon.Dispose(); } catch { }
                if (h != IntPtr.Zero) try { NativeUi.DestroyIcon(h); } catch { }
                liveIcon = null;
            }
            Application.Exit();
        }

        // 供窗体类调用的薄封装 (private 成员只在 Program 内部可见)
        internal static DeviceState UiPrimary() { return PrimaryState(); }
        internal static bool UiOffline(DeviceState st)
        {
            return st == null || (DateTime.Now - st.LastSeen).TotalMilliseconds > OfflineAfterMs;
        }
        internal static int UiLowThreshold() { return LowBatteryPercent; }
        internal static void ClearBubble() { flyout = null; }
        internal static void ClearToast() { toast = null; }

        // HID 线程 -> UI 线程
        static Form uiPump;
        static void UiInvoke(MethodInvoker action)
        {
            try
            {
                if (uiPump != null && uiPump.InvokeRequired) uiPump.BeginInvoke(action);
                else action();
            }
            catch (Exception ex) { Log("ui invoke failed: " + ex.Message); }
        }

        static DeviceState PrimaryState()
        {
            lock (sync)
            {
                DeviceState best = null;
                foreach (KeyValuePair<string, DeviceState> kv in states)
                {
                    if (best == null || kv.Value.LastSeen > best.LastSeen) best = kv.Value;
                }
                return best;
            }
        }

        // ====================================================================
        // 托盘图标 / 悬停提示
        // ====================================================================
        static void UpdateTrayUI()
        {
            if (tray == null) return;

            DeviceState prim = Program.PreviewDev != null ? Program.PreviewDev : Program.UiPrimary();
            int level = -1;
            bool charging = false;
            bool offline = true;
            string tip;

            if (prim == null)
            {
                tip = "猫耳电量 · 未检测到鼠标，点此查看";
            }
            else
            {
                level = prim.Battery;
                charging = prim.Charging;
                offline = Program.UiOffline(prim);
                string who = DeviceTitle(prim);
                if (offline)
                    tip = level >= 0
                        ? string.Format("{0} · 已休眠（最后 {1}%）", who, level)
                        : who + " · 已休眠";
                else if (charging)
                    tip = string.Format("{0} · 充电中 {1}%", who, level);
                else
                    tip = string.Format("{0} · {1}% · {2}", who, level, Moods.Name(Moods.Of(level, false, false)));
            }

            SetTrayIcon(level, charging, offline);
            bool bubbleUp = false;
            try { bubbleUp = flyout != null && flyout.Visible; } catch { }
            if (!bubbleUp) { try { tray.Text = tip; } catch { } }

        }

        static void SetTrayIcon(int level, bool charging, bool offline)
        {
            string key = level + "|" + (charging ? "1" : "0") + "|" + (offline ? "1" : "0") + "|" + animFrame + "|" + clickPulse;
            if (key == liveIconKey && liveIcon != null) return;

            Icon old = liveIcon;
            IntPtr oldH = IntPtr.Zero;
            if (old != null) { try { oldH = old.Handle; } catch { } }

            using (Bitmap bmp = TrayArt.Render(iconSize, level, charging, offline, animFrame, clickPulse))
            {
                IntPtr h = bmp.GetHicon();
                liveIcon = Icon.FromHandle(h);
                try { tray.Icon = liveIcon; } catch { }
            }
            liveIconKey = key;

            // 旧图标先留一小会儿(资源管理器可能还在引用), 只保留最近 6 个, 避免句柄泄漏
            if (old != null)
            {
                oldIcons.Enqueue(old);
                oldHandles.Enqueue(oldH);
                while (oldIcons.Count > 6)
                {
                    Icon dead = oldIcons.Dequeue();
                    IntPtr dh = oldHandles.Dequeue();
                    try { dead.Dispose(); } catch { }
                    if (dh != IntPtr.Zero) try { NativeUi.DestroyIcon(dh); } catch { }
                }
            }
        }

        static void AnimTick()
        {
            if (tray == null) return;
            DeviceState prim = Program.PreviewDev != null ? Program.PreviewDev : Program.UiPrimary();
            bool offline = prim == null ||
                (DateTime.Now - prim.LastSeen).TotalMilliseconds > OfflineAfterMs;
            bool animate = AnimationsEnabled && prim != null && !offline &&
                (prim.Charging || prim.Battery <= LowBatteryPercent);

            if (!animate)
            {
                if (animFrame != 0) { animFrame = 0; UpdateTrayUI(); }
                return;
            }
            animFrame = (animFrame + 1) % 6;
            UpdateTrayUI();
        }

        // ====================================================================
        // 迷你电量气泡 (点图标 -> 2 秒动画 -> 自动消失)
        // ====================================================================
        static void ShowBubble()
        {
            try { if (flyout != null) flyout.Close(); } catch { }
            DeviceState prim = PreviewDev != null ? PreviewDev : UiPrimary();
            int level = -1;
            bool charging = false, offline = true;
            string name = "INPHIC IN9";
            if (prim != null)
            {
                level = prim.Battery;
                charging = prim.Charging;
                offline = Program.UiOffline(prim);
                name = DeviceTitle(prim);
            }
            try { tray.Text = " "; } catch { }
            flyout = new BatteryBubble(level, charging, offline, name);
            flyout.ShowNearCursor();
            StartClickPulse();
        }

        static System.Windows.Forms.Timer pulseTimer;
        static int clickPulse;

        static void StartClickPulse()
        {
            if (!AnimationsEnabled) return;
            clickPulse = 7;
            if (pulseTimer == null)
            {
                pulseTimer = new System.Windows.Forms.Timer();
                pulseTimer.Interval = 55;
                pulseTimer.Tick += delegate
                {
                    clickPulse--;
                    if (clickPulse <= 0) { clickPulse = 0; pulseTimer.Stop(); }
                    UpdateTrayUI();
                };
            }
            pulseTimer.Start();
        }

        // 窗体关闭后把静态引用清掉, 避免下次访问已释放对象
        internal static void NotifyBubbleClosed(BatteryBubble f)
        {
            if (flyout == f) flyout = null;
            try { if (tray != null) UpdateTrayUI(); } catch { }
        }
        internal static void NotifyToastClosed(SilentToast t) { if (toast == t) toast = null; }

        static void LowThresholdChanged()
        {
            if (lowItem != null) lowItem.Text = "低电量静默提醒（≤" + LowBatteryPercent + "%）";
        }
        static void RunRename()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppSettings.Load();
            string cur = string.IsNullOrEmpty(AppSettings.DisplayName) ? "INPHIC IN9" : AppSettings.DisplayName;
            using (RenameDialog dlg = new RenameDialog(cur))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    AppSettings.DisplayName = dlg.Value;
                    AppSettings.Save();
                }
            }
        }
        static void ChooseCustomIcon()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择图标图片（建议 192×192 以上、透明底 PNG）";
                dlg.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    Directory.CreateDirectory(TrayArt.CustomIconDir);
                    string target = Path.Combine(TrayArt.CustomIconDir, "icon.png");
                    using (Bitmap src = new Bitmap(dlg.FileName))
                        src.Save(target, ImageFormat.Png);
                    TrayArt.ReloadCustomIcon();
                    UpdateTrayUI();
                    ShowInfoToast("图标已更新", "图片已保存到 " + target + "，随时可以再换。", "自定义图标");
                }
                catch (Exception ex)
                {
                    ShowInfoToast("图标没换成", "换一张 PNG 试试：" + ex.Message, "自定义图标");
                }
            }
        }

        static void OpenIconFolder()
        {
            try
            {
                Directory.CreateDirectory(TrayArt.CustomIconDir);
                System.Diagnostics.Process.Start("explorer.exe", TrayArt.CustomIconDir);
            }
            catch { }
        }

        static void RenameMouse()
        {
            using (RenameDialog dlg = new RenameDialog(DeviceTitle(PrimaryState())))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                AppSettings.DisplayName = dlg.Value;
                AppSettings.Save();
                UpdateTrayUI();
                // 气泡是短生命周期的, 不需要刷新
                ShowInfoToast("名字已保存",
                    string.IsNullOrEmpty(AppSettings.DisplayName)
                        ? "已恢复自动识别的设备名。"
                        : "以后任务栏悬停和面板标题都会显示「" + AppSettings.DisplayName + "」。",
                    "设置 · 可随时再改");
            }
        }

        // ====================================================================
        // 静默提醒 (低电量: 没有任何声音, 只有一张小卡片滑出来)
        // ====================================================================
        static void ShowLowBatteryNotification(string deviceName, int level)
        {
            UiInvoke(delegate
            {
                try
                {
                    try { if (flyout != null && flyout.Visible) flyout.Close(); } catch { }
                    try { if (toast != null) toast.Close(); } catch { }
                    toast = new SilentToast();
                    toast.ShowToast(level, deviceName);
                    Log("silent toast: " + level + "% " + deviceName);
                }
                catch (Exception ex) { Log("toast failed: " + ex.Message); }
            });
        }

        static void ShowInfoToast(string title, string body, string footer)
        {
            UiInvoke(delegate
            {
                try
                {
                    try { if (flyout != null && flyout.Visible) flyout.Close(); } catch { }
                    try { if (toast != null) toast.Close(); } catch { }
                    toast = new SilentToast();
                    toast.ShowCard(title, body, footer, 100, Mood.Normal);
                }
                catch (Exception ex) { Log("info toast failed: " + ex.Message); }
            });
        }

        // 等通知区把图标登记好, 再把它从 ^ 溢出区提升到任务栏
        static void PinWatch()
        {
            string exe = Application.ExecutablePath;
            bool found = false;
            bool changed = false;
            for (int i = 0; i < 4; i++)
            {
                Thread.Sleep(2200);
                changed = TrayPin.TryPromote(exe, out found);
                Log("tray pin: found=" + found + " changed=" + changed);
                if (found) break;
            }
            if (found && changed)
            {
                UiInvoke(delegate
                {
                    try { tray.Visible = false; Thread.Sleep(120); tray.Visible = true; } catch { }
                });
            }

            // Win11 新图标会先落到 ^ 溢出区, 首次运行提示一次怎么拖出来
            if ((changed || !found) && !TrayPin.HintShown() && IsWin11OrLater())
            {
                TrayPin.MarkHintShown();
                ShowInfoToast("猫耳图标在任务栏右下角",
                    "如果没看到它，点任务栏的 ^ 打开溢出菜单，把 IN9 猫耳图标拖到任务栏上一次，之后系统会记住。",
                    "只需设置一次 · 左键点图标看电量");
            }
        }

        static bool IsWin11OrLater()
        {
            try { return Environment.OSVersion.Version.Build >= 22000; }
            catch { return false; }
        }

        static Mutex singleInstance;
        static bool AcquireSingleInstance()
        {
            try
            {
                bool created;
                singleInstance = new Mutex(true, "InphicBatteryTray9.SingleInstance", out created);
                if (!created) { try { singleInstance.Dispose(); } catch { } singleInstance = null; return false; }
                return true;
            }
            catch { return true; }
        }

        static void CrashLog(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InphicBatteryTray9");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + (ex == null ? "unknown" : ex.ToString()) + "\r\n");
            }
            catch { }
        }
        static void DoNotifyTest()
        {
            Application.EnableVisualStyles();
            Console.WriteLine("预览静默提醒（不会播放任何提示音）…");
            ShowLowBatteryNotification("INPHIC IN9", LowBatteryPercent);
            PumpMessages(7000);
        }

        static void PumpMessages(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                try { Application.DoEvents(); } catch { }
                Thread.Sleep(40);
            }
        }

        // ====================================================================
        // 右键菜单
        // ====================================================================
        static void BuildMenu()
        {
            menu = new ContextMenuStrip();
            menu.Renderer = new AnimeMenuRenderer();
            menu.BackColor = Color.FromArgb(0x1A, 0x14, 0x30);
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Padding = new Padding(0, 6, 0, 6);
            menu.Font = F.Ui(10.5f, FontStyle.Regular);
            menu.Opening += delegate
            {
                if (autoItem != null) autoItem.Checked = IsAutoStartEnabled();
                if (lowItem != null) lowItem.Checked = LowNotifyEnabled;
            };

            ToolStripMenuItem show = MakeItem("显示电量");
            show.Font = F.Ui(10.5f, FontStyle.Bold);
            show.Click += delegate { ShowBubble(); };
            menu.Items.Add(show);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem rescan = MakeItem("重新扫描设备");
            rescan.Click += delegate { Log("manual rescan"); rescanEvent.Set(); };
            menu.Items.Add(rescan);

            ToolStripMenuItem refresh = MakeItem("立即刷新电量");
            refresh.Click += delegate { Log("manual refresh"); refreshEvent.Set(); };
            menu.Items.Add(refresh);

            ToolStripMenuItem rename = MakeItem("重命名鼠标…");
            rename.Click += delegate { RenameMouse(); };
            menu.Items.Add(rename);

            ToolStripMenuItem customIcon = MakeItem("选择自定义图标…");
            customIcon.Click += delegate { ChooseCustomIcon(); };
            menu.Items.Add(customIcon);

            ToolStripMenuItem iconFolder = MakeItem("打开图标文件夹");
            iconFolder.Click += delegate { OpenIconFolder(); };
            menu.Items.Add(iconFolder);

            lowItem = MakeItem("低电量静默提醒（≤" + LowBatteryPercent + "%）");
            lowItem.Checked = LowNotifyEnabled;
            lowItem.Click += delegate
            {
                LowNotifyEnabled = !LowNotifyEnabled;
                lowItem.Checked = LowNotifyEnabled;
                Log("low notify toggled: " + LowNotifyEnabled);
            };
            menu.Items.Add(lowItem);

            ToolStripMenuItem preview = MakeItem("预览静默提醒");
            preview.Click += delegate
            {
                DeviceState p = PrimaryState();
                ShowLowBatteryNotification(DeviceTitle(p), LowBatteryPercent);
            };
            menu.Items.Add(preview);

            ToolStripMenuItem pin = MakeItem("固定到任务栏通知区");
            pin.Click += delegate
            {
                bool found;
                bool changed = TrayPin.TryPromote(Application.ExecutablePath, out found);
                Log("manual pin: found=" + found + " changed=" + changed);
                if (found && changed)
                {
                    try { tray.Visible = false; Application.DoEvents(); tray.Visible = true; } catch { }
                    ShowInfoToast("已经设为常驻啦", "如果图标还在 ^ 里，重启一次资源管理器就会出现在任务栏上。", "通知区设置");
                }
                else if (found)
                {
                    ShowInfoToast("已经是常驻状态", "IN9 猫耳图标会一直显示在任务栏通知区。", "通知区设置");
                }
                else
                {
                    ShowInfoToast("手动固定一下", "点任务栏右下角的 ^ ，把猫耳图标拖到任务栏上即可。", "只需设置一次");
                }
            };
            menu.Items.Add(pin);
            menu.Items.Add(new ToolStripSeparator());

            autoItem = MakeItem("开机自动启动");
            autoItem.Checked = IsAutoStartEnabled();
            autoItem.Click += delegate
            {
                ToggleAutoStart();
                autoItem.Checked = IsAutoStartEnabled();
            };
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = MakeItem("退出");
            quit.Click += delegate { Quit(); };
            menu.Items.Add(quit);
        }

        static ToolStripMenuItem MakeItem(string text)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Padding = new Padding(4, 5, 12, 5);
            it.ForeColor = Theme.Text;
            return it;
        }

        // ====================================================================
        // 开机自启
        // ====================================================================
        static bool IsAutoStartEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    return k != null && k.GetValue(RUN_NAME) != null;
                }
            }
            catch { return false; }
        }

        static void ToggleAutoStart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (k == null) return;
                    if (IsAutoStartEnabled()) k.DeleteValue(RUN_NAME, false);
                    else k.SetValue(RUN_NAME, "\"" + Application.ExecutablePath + "\"", Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch (Exception ex) { Log("autostart error: " + ex.Message); }
        }

        // ====================================================================
        // 预览 / 美术资源导出
        // ====================================================================
        static void RunPreview(int seconds)
        {
            if (seconds < 4) seconds = 4;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { CrashLog(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { CrashLog(e.ExceptionObject as Exception); };

            PreviewDev = new DeviceState();
            PreviewDev.Battery = 76;
            PreviewDev.Charging = false;
            PreviewDev.LastSeen = DateTime.Now.AddMilliseconds(-2400);
            PreviewDev.Product = "INPHIC IN9";
            PreviewDev.Transport = "2.4G";
            PreviewDev.LastQueryMs = 24;

            ShowBubble();

            System.Windows.Forms.Timer phase = new System.Windows.Forms.Timer();
            phase.Interval = 8000;
            phase.Tick += delegate
            {
                phase.Stop();
                try { if (flyout != null) { flyout.Close(); flyout = null; } } catch { }
                PreviewDev.Battery = LowBatteryPercent;
                SilentToast t = new SilentToast();
                t.ShowToast(LowBatteryPercent, "INPHIC IN9");
                toast = t;
            };
            phase.Start();

            System.Windows.Forms.Timer close = new System.Windows.Forms.Timer();
            close.Interval = Math.Max(seconds, 14) * 1000;
            close.Tick += delegate
            {
                close.Stop();
                try { if (flyout != null) flyout.Close(); } catch { }
                try { if (toast != null) toast.Close(); } catch { }
                Application.Exit();
            };
            close.Start();
            Application.Run();
        }
        static void ExportArtwork(string dir)
        {
            Directory.CreateDirectory(dir);
            Console.WriteLine("导出美术资源到: " + dir);

            string[] names = new string[] { "full", "high", "mid", "low20", "low8", "charging", "offline", "unknown" };
            int[,] spec = new int[,] {
                { 100, 0, 0 }, { 76, 0, 0 }, { 45, 0, 0 }, { 20, 0, 0 },
                { 8, 0, 0 }, { 94, 1, 0 }, { 64, 0, 1 }, { -1, 0, 1 }
            };
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
            for (int i = 0; i < names.Length; i++)
            {
                foreach (int s in sizes)
                {
                    using (Bitmap bmp = TrayArt.Render(s, spec[i, 0], spec[i, 1] != 0, spec[i, 2] != 0, 0))
                        bmp.Save(Path.Combine(dir, string.Format("icon_{0}_{1}.png", names[i], s)), System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            int[] icoSizes = new int[] { 16, 24, 32, 40, 48, 64, 128, 256 };
            using (Icon ico = IcoBuilder.Build(icoSizes, delegate(int s) { return TrayArt.Render(s, 100, false, false, 0); }))
            using (FileStream fs = new FileStream(Path.Combine(dir, "InphicBatteryTray9.ico"), FileMode.Create))
                ico.Save(fs);

            using (Bitmap bubble = BubbleArt.Render(76, false, false, "INPHIC IN9", 1f, 2f))
                bubble.Save(Path.Combine(dir, "bubble.png"), System.Drawing.Imaging.ImageFormat.Png);
            using (Bitmap toastBmp = ToastArt.Render(LowBatteryPercent, "INPHIC IN9", 2f))
                toastBmp.Save(Path.Combine(dir, "toast.png"), System.Drawing.Imaging.ImageFormat.Png);

            Console.WriteLine("完成: 图标 PNG / .ico / bubble.png / toast.png");
        }
    }

    // ------------------------------------------------------------------------
    // 迷你电量气泡: 点一下任务栏图标, 在旁边弹出 2 秒, 然后自动消失
    // ------------------------------------------------------------------------
    class BatteryBubble : Form
    {
        System.Windows.Forms.Timer anim;
        DateTime start;
        int level;
        bool charging, offline;
        string name;
        float uiScale = 1f;

        public BatteryBubble(int level, bool charging, bool offline, string name)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0x1A, 0x14, 0x31);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Opacity = 0;
            this.level = level;
            this.charging = charging;
            this.offline = offline;
            this.name = name;

            using (Graphics g = CreateGraphics()) uiScale = g.DpiX / 96f;
            if (uiScale < 1f) uiScale = 1f;
            ClientSize = new Size((int)Math.Round(BubbleArt.W * uiScale), (int)Math.Round(BubbleArt.H * uiScale));
            using (GraphicsPath p = D2.Round(new RectangleF(0, 0, Width, Height), 15f * uiScale))
                Region = new Region(p);

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 15;
            anim.Tick += delegate
            {
                double ms = (DateTime.Now - start).TotalMilliseconds;
                if (ms >= 2100) { anim.Stop(); Close(); return; }
                if (ms < 120) Opacity = ms / 120.0;
                else if (ms > 1750) Opacity = Math.Max(0, (2100 - ms) / 350.0);
                else Opacity = 1;
                Invalidate();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        public void ShowNearCursor()
        {
            Show();
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            int x = Cursor.Position.X - Width - (int)Math.Round(10 * uiScale);
            int y = Cursor.Position.Y - Height - (int)Math.Round(12 * uiScale);
            if (y < wa.Top) y = Cursor.Position.Y + (int)Math.Round(20 * uiScale);
            if (x < wa.Left + 8) x = wa.Left + 8;
            if (x + Width > wa.Right - 8) x = wa.Right - Width - 8;
            Location = new Point(x, y);
            start = DateTime.Now;
            anim.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            double ms = (DateTime.Now - start).TotalMilliseconds;
            float t = (float)Math.Min(1.0, ms / 620.0);
            t = 1f - (1f - t) * (1f - t);
            float sc = 1f;
            if (ms < 150) sc = 0.90f + 0.10f * (float)(ms / 150.0);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            e.Graphics.TranslateTransform((BubbleArt.W * (1 - sc)) / 2f * uiScale, (BubbleArt.H * (1 - sc)) / 2f * uiScale);
            e.Graphics.ScaleTransform(uiScale * sc, uiScale * sc);
            BubbleArt.Draw(e.Graphics, level, charging, offline, name, t);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { if (anim != null) { anim.Stop(); anim.Dispose(); } } catch { }
            Program.NotifyBubbleClosed(this);
        }
    }
    // ------------------------------------------------------------------------
    // 静默提醒卡片 (无声音)
    // ------------------------------------------------------------------------
    class SilentToast : Form
    {
        System.Windows.Forms.Timer life;
        System.Windows.Forms.Timer appear;
        int countdown;
        int appearStep;
        int startY;
        int level;
        string title = "电量告急喵～";
        string body = "";
        string footer = "静默提醒 · 不播放提示音";
        Mood mood = Mood.Critical;
        internal float UiScale { get { return uiScale; } }
        float uiScale = 1.5f;

        public SilentToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0x1D, 0x15, 0x35);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Opacity = 0;

            using (Graphics g = CreateGraphics()) uiScale = g.DpiX / 96f;
            if (uiScale < 1f) uiScale = 1f;
            ClientSize = new Size((int)Math.Round(ToastArt.W * uiScale), (int)Math.Round(ToastArt.H * uiScale));
            using (GraphicsPath p = D2.Round(new RectangleF(0, 0, Width, Height), 16f * uiScale))
                Region = new Region(p);

            life = new System.Windows.Forms.Timer();
            life.Interval = 1000;
            life.Tick += delegate
            {
                if (Bounds.Contains(Control.MousePosition)) { countdown = 8; return; }
                countdown--;
                if (countdown <= 0) Close();
            };

            appear = new System.Windows.Forms.Timer();
            appear.Interval = 16;
            appear.Tick += delegate
            {
                appearStep++;
                Opacity = Math.Min(1.0, appearStep / 10.0);
                int dy = (int)Math.Round(18 * uiScale * (1 - Opacity));
                Top = startY + dy;
                if (appearStep >= 10) { appear.Stop(); Opacity = 1; Top = startY; }
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                cp.ExStyle |= 0x08000000;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        public void ShowToast(int lv, string dev)
        {
            string who = string.IsNullOrEmpty(dev) ? "IN9 鼠标" : dev;
            ShowCard("电量告急喵～", string.Format("{0} 只剩 {1}%，记得充电哦", who, Math.Max(0, lv)),
                "静默提醒 · 不播放提示音", lv, Mood.Critical);
        }

        public void ShowCard(string t1, string b1, string f1, int lv, Mood md)
        {
            title = t1; body = b1; footer = f1; level = lv; mood = md;
            Show();
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            int x = wa.Right - Width - (int)Math.Round(16 * uiScale);
            startY = wa.Bottom - Height - (int)Math.Round(16 * uiScale);
            Location = new Point(x, startY + (int)Math.Round(18 * uiScale));
            countdown = 8;
            Invalidate();
            appear.Start();
            life.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            e.Graphics.ScaleTransform(uiScale, uiScale);
            ToastArt.DrawCard(e.Graphics, title, body, footer, level, mood, 0f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Close();
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { if (life != null) { life.Stop(); life.Dispose(); } } catch { }
            try { if (appear != null) { appear.Stop(); appear.Dispose(); } } catch { }
            Program.NotifyToastClosed(this);
        }
    }

    // ------------------------------------------------------------------------
    // 深色右键菜单渲染
    // ------------------------------------------------------------------------
    class AnimeMenuRenderer : ToolStripProfessionalRenderer
    {
        public AnimeMenuRenderer() : base(new AnimeColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            if (e.Item.Selected && e.Item.Enabled)
            {
                using (GraphicsPath p = D2.Round(r, 6))
                using (LinearGradientBrush b = D2.Lin(r, Color.FromArgb(70, 0xA7, 0x8B, 0xFA), Color.FromArgb(70, 0xFF, 0x7E, 0xB6), 0f))
                    e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(90, 200, 170, 255), 1f))
                using (GraphicsPath p2 = D2.Round(r, 6))
                    e.Graphics.DrawPath(pen, p2);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? (e.Item.Selected ? Color.FromArgb(255, 255, 244, 252) : Color.FromArgb(235, 238, 230, 252))
                : Color.FromArgb(120, 160, 150, 185);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Color.FromArgb(40, 200, 180, 240), 1f))
                e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            float cy = e.Item.Height / 2f;
            D2.Heart(e.Graphics, new RectangleF(9, cy - 4.5f, 9, 9), Theme.Pink, 240);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Color.FromArgb(92, 0x6A, 0x58, 0x9C), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    class AnimeColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(0x1A, 0x14, 0x30);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Color.FromArgb(0x5A, 0x49, 0x86); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(0, 0, 0, 0); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(0, 0, 0, 0); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(0, 0, 0, 0); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(0, 0, 0, 0); } }
        public override Color MenuItemPressedGradientBegin { get { return Bg; } }
        public override Color MenuItemPressedGradientEnd { get { return Bg; } }
        public override Color SeparatorDark { get { return Color.FromArgb(0, 0, 0, 0); } }
        public override Color SeparatorLight { get { return Color.FromArgb(0, 0, 0, 0); } }
    }

    // ------------------------------------------------------------------------
    // 重命名对话框 (深色, 跟随 DPI)
    // ------------------------------------------------------------------------
    class RenameDialog : Form
    {
        TextBox box;
        float s = 1f;

        public string Value { get { return box.Text.Trim(); } }

        public RenameDialog(string current)
        {
            Text = "重命名鼠标";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0x1B, 0x15, 0x33);
            DoubleBuffered = true;

            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            if (s < 1f) s = 1f;
            ClientSize = new Size(I(380), I(172));

            Label title = new Label();
            title.Text = "重命名鼠标";
            title.Font = F.Ui(14f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.BackColor = Color.Transparent;
            title.AutoSize = false;
            title.SetBounds(I(22), I(16), I(336), I(24));
            Controls.Add(title);

            Label hint = new Label();
            hint.Text = "显示在任务栏悬停提示和电量面板标题上（留空 = 自动识别）";
            hint.Font = F.Ui(9.5f, FontStyle.Regular);
            hint.ForeColor = Theme.TextDim;
            hint.BackColor = Color.Transparent;
            hint.AutoSize = false;
            hint.SetBounds(I(22), I(44), I(336), I(18));
            Controls.Add(hint);

            box = new TextBox();
            box.Text = current ?? "";
            box.MaxLength = 24;
            box.Font = F.Ui(12.5f, FontStyle.Regular);
            box.BackColor = Color.FromArgb(0x25, 0x1D, 0x40);
            box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.SetBounds(I(22), I(70), I(336), I(30));
            Controls.Add(box);

            Button cancel = MakeButton("取消", I(196), I(122));
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            Button ok = MakeButton("保存", I(282), I(122));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
            box.SelectAll();
        }

        int I(float v) { return (int)Math.Round(v * s); }

        Button MakeButton(string text, int x, int y)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = F.Ui(10.5f, FontStyle.Bold);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(0x33, 0x28, 0x5A);
            b.ForeColor = Theme.Text;
            b.FlatAppearance.BorderColor = Color.FromArgb(0x6A, 0x58, 0xA0);
            b.FlatAppearance.BorderSize = 1;
            b.SetBounds(x, y, I(76), I(32));
            return b;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (GraphicsPath p = D2.Round(new RectangleF(0, 0, Width, Height), 16f * s))
                Region = new Region(p);
        }
    }
}







































