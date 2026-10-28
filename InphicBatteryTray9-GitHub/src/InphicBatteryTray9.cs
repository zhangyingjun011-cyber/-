// ============================================================================
// InphicBatteryTray9 — 英菲克 IN9 系列鼠标任务栏电量显示
//
// 为什么需要这个:
//   Tan-TanZi/InphicBatteryTray 只支持 IN10: 电量是设备在 Usage Page 0x000A
//   接口上主动推送的 5 字节简报文 (03 type 40 dev level), 每 2 秒一次。
//   IN9 协议完全不同: 电量不会主动推送, 必须由主机主动查询。所以 IN10 的
//   工具读不到 IN09 鼠标 (VID/PID 不匹配 + 协议不兼容, 双重原因)。
//
// IN9 真实协议 (本机逆向官方 "Inphic IN9 GM.exe" 得到, 已实测验证):
//   通道:   Feature Report, 报告ID = 0x0C, 长度 = 33 字节 (不是 WriteFile!)
//   握手:   0C 01 F0 00 <seq> 01 01 FF   (HidD_SetFeature, 只发不收)
//   查询:   0C 01 20 00 <seq> 00 00      (HidD_SetFeature -> Sleep 10ms
//                                         -> HidD_GetFeature 读回 33 字节)
//   响应:   buf[2]==0x20 命令回显, buf[6]!=0 为有效
//           buf[8..0x0B]="M909" 型号魔数, buf[0x0F]=IC 型号
//           buf[0x12]=电量百分比
//           buf[0x13]=1 放电 / 2 充电中(官方此时强制显示 100%)
//           buf[0x15]=在线标志
//   设备定位: VID=0x248A (USB 0x5B2E / 2.4G 接收器 0x5B2F), 取
//             FeatureReportByteLength==33 的那个接口 (官方软件的判定条件)
//
// 隐私与安全:
//   - 只发送官方软件同款的读命令 (0x20 / 0xF0), 不修改鼠标任何设置;
//   - 以共享方式打开设备, 可与原厂软件 (INPHIC HUB / Inphic Mouse) 同时运行;
//   - 主动跳过 Usage Page 0x0001 (键鼠) 接口, 不采集任何输入数据;
//   - 不联网, 无遥测。
//
// 编译 (无需安装任何 SDK):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 ^
//     /out:InphicBatteryTray9.exe /codepage:65001 ^
//     /reference:System.dll /reference:System.Windows.Forms.dll ^
//     /reference:System.Drawing.dll /reference:System.Core.dll ^
//     src\InphicBatteryTray9.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InphicBatteryTray9
{
    // ------------------------------------------------------------------------
    // Native API
    // ------------------------------------------------------------------------
    static class Native
    {
        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const int FILE_SHARE_READ = 1;
        public const int FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public const int ERROR_IO_PENDING = 997;
        public const uint WAIT_OBJECT_0 = 0;
        public const uint WAIT_TIMEOUT = 0x102;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;
        public const int ATTACH_PARENT_PROCESS = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct GUID { public uint Data1; public ushort Data2; public ushort Data3; public byte b0, b1, b2, b3, b4, b5, b6, b7; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public GUID InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(ref GUID ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref GUID interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out GUID guid);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(IntPtr device, IntPtr buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsed);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        // IN9 真实协议走 Feature Report (官方 Inphic IN9 GM.exe 逆向得到)
        [DllImport("hid.dll")]
        public static extern bool HidD_SetFeature(IntPtr device, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetFeature(IntPtr device, byte[] reportBuffer, int reportBufferLength);

        // ---- 通用 HID 电量 (标准 Battery Strength) ----
        public const int HIDP_INPUT = 0;
        public const int HIDP_FEATURE = 2;

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetInputReport(IntPtr device, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsageValue(int reportType, byte reportId, int linkCollection,
            ushort usagePage, ushort usage, IntPtr preparsedData, byte[] report, int reportLength, out uint value);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, int shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, byte[] buffer, int numberOfBytesToRead, out int numberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, byte[] buffer, int numberOfBytesToWrite, out int numberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll")]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        // 给进程设置 AppUserModelID: 这样系统通知会带上应用名,
        // 并且在 Win10/11 上会呈现为系统通知横幅(可进操作中心), 而不是裸气泡
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    }

    // ------------------------------------------------------------------------
    // 数据模型
    // ------------------------------------------------------------------------
    class HidInterface
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public string Product;
        public string Transport;
        public int Mi = -1;        // 复合设备接口号 (路径中的 &mi_XX), -1=非复合设备
        public int InputLen;       // Input Report 长度 (HidP_GetCaps)
        public int OutputLen;      // Output Report 长度
        public int FeatureLen;     // Feature Report 长度 (IN9 厂商通道 = 33)
        public ushort Usage;       // 顶层 collection 的 usage (鼠标 = 0x0002)
        public ushort UsagePage;

        public string Label
        {
            get
            {
                string id = string.Format("{0:X4}:{1:X4}", Vid, Pid);
                if (!string.IsNullOrEmpty(Product)) return Product + " [" + id + "]";
                return "INPHIC [" + id + "]";
            }
        }
    }

    class DeviceState
    {
        public string Path;
        public string Label;
        public int Battery = -1;
        public bool Charging;
        public DateTime LastSeen = DateTime.MinValue;
        public double LastQueryMs;
        public string Product;
        public ushort Vid;
        public ushort Pid;
        public string Transport;
        public int Mi = -1;
        // ---- 低电量提醒状态 ----
        public bool LowNotified;                            // 本次低电量已提醒过
        public DateTime LastNotify = DateTime.MinValue;     // 上次提醒时间 (用于冷却)
    }

    // ------------------------------------------------------------------------
    // 主程序
    // ------------------------------------------------------------------------
    static partial class Program
    {
        // ---- 可配置项 ----
        static List<ushort> Vids = new List<ushort> { 0x248A, 0x249A };  // IN9: USB / 2.4G
        static List<ushort> Pids = new List<ushort>();                   // 空 = 不限 PID
        static int QueryIntervalMs = AppSettings.DefaultIntervalMs;   // 默认 5 分钟查一次
        static int OfflineAfterMs = 0;                                // 0 = 按间隔自动算
        static readonly System.Threading.AutoResetEvent refreshEvent = new System.Threading.AutoResetEvent(false);
        static int AckRetries = 12;
        static int AckTimeoutMs = 120;
        static int ForceMi = -1;

        // ---- 低电量提醒 ----
        static bool LowNotifyEnabled = true;   // 总开关 (托盘菜单可切)
        static int LowBatteryPercent = 20;     // 电量 <= 此值 触发静默提醒
        static int LowReleasePercent = 25;     // 电量回升 > 此值 才解除提醒状态 (滞回, 防抖)
        static int LowNotifyCooldownMin = 30; // 持续低电量时, 两次提醒的最小间隔(分钟)
        const int BalloonTimeoutMs = 10000;   // 通知停留时长

        // ---- IN9 协议常量 ----
        // 真实协议 (逆向官方 "Inphic IN9 GM.exe" 得到, 实测验证通过):
        //   通道 = Feature Report, 报告ID 0x0C, 长度 33 字节
        //   握手 0xF0 (只发不收) / 查询 0x20 (SetFeature -> Sleep 10ms -> GetFeature)
        //   响应: buf[2]=0x20 回显, buf[6]!=0 有效, buf[8..0x0B]="M909",
        //         buf[0x0F]=IC型号, buf[0x12]=电量%, buf[0x13]=1放电/2充电, buf[0x15]=在线
        const int FrameLen = 33;
        const byte FeatureReportId = 0x0C;
        const byte CmdHandshake = 0xF0;      // 握手/保活 (仅 SetFeature)
        const byte CmdGetDeviceInfo = 0x20;   // 读设备信息 + 电量
        const byte NotifyMagic = 0xC0;
        static readonly object seqLock = new object();
        static byte seqCounter = 0;

        // ---- 运行状态 ----
        static string logPath = null;
        static readonly object logSync = new object();
        static readonly object sync = new object();
        static readonly Dictionary<string, DeviceState> states = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Thread> readerThreads = new List<Thread>();
        static readonly Dictionary<string, DateTime> writeFailCooldown = new Dictionary<string, DateTime>();
        static readonly ManualResetEventSlim rescanEvent = new ManualResetEventSlim(false);
        static volatile bool running = true;

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RUN_NAME = "InphicBatteryTray9";

        // ====================================================================
        // 日志
        // ====================================================================
        static void Log(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath)) return;
                lock (logSync)
                {
                    File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n");
                }
            }
            catch { }
        }

        // ====================================================================
        // 入口
        // ====================================================================
        [STAThread]
        static void Main(string[] args)
        {
            // 必须在创建任何窗口/托盘图标之前调用, 否则通知不带应用名
            try { Native.SetCurrentProcessExplicitAppUserModelID("Inphic.IN9.BatteryTray9"); }
            catch { }

            Dictionary<string, string> opt = ParseArgs(args);

            // 高 DPI 必须在创建任何窗口/图标之前设置
            NativeUi.EnableDpiAwareness();
            AnimationsEnabled = !opt.ContainsKey("no-anim");

            // 美术资源导出 / 界面预览 (不连接鼠标也能跑)
            if (opt.ContainsKey("icons-out"))
            {
                EnsureConsole();
                ExportArtwork(opt["icons-out"]);
                return;
            }
            if (opt.ContainsKey("rename"))
            {
                RunRename();
                return;
            }
            if (opt.ContainsKey("preview"))
            {
                int secs = 9;
                if (opt["preview"] != "1") int.TryParse(opt["preview"], out secs);
                RunPreview(secs);
                return;
            }

            if (opt.ContainsKey("help") || opt.ContainsKey("h") || opt.ContainsKey("?"))
            {
                EnsureConsole();
                ShowHelp();
                return;
            }

            if (opt.ContainsKey("log")) logPath = opt["log"];
            else logPath = Environment.GetEnvironmentVariable("INPHIC_TRAY_LOG");

            if (opt.ContainsKey("vids")) Vids = ParseHexList(opt["vids"]);
            if (opt.ContainsKey("pids")) Pids = ParseHexList(opt["pids"]);
            // 用户设置 (名称/间隔) 先读, 命令行参数优先
            AppSettings.Load();
            QueryIntervalMs = AppSettings.IntervalMs;
            if (opt.ContainsKey("interval")) int.TryParse(opt["interval"], out QueryIntervalMs);
            if (opt.ContainsKey("offline")) int.TryParse(opt["offline"], out OfflineAfterMs);
            if (opt.ContainsKey("icon") && File.Exists(opt["icon"]))
            {
                try
                {
                    Directory.CreateDirectory(TrayArt.CustomIconDir);
                    File.Copy(opt["icon"], Path.Combine(TrayArt.CustomIconDir, "icon.png"), true);
                    TrayArt.ReloadCustomIcon();
                }
                catch { }
            }
            if (opt.ContainsKey("name"))
            {
                AppSettings.DisplayName = (opt["name"] ?? "").Trim();
                AppSettings.Save();
            }
            if (opt.ContainsKey("mi"))
            {
                int mi;
                if (int.TryParse(opt["mi"], System.Globalization.NumberStyles.HexNumber, null, out mi)) ForceMi = mi;
            }
            if (QueryIntervalMs < 500) QueryIntervalMs = 500;
            if (OfflineAfterMs <= 0) OfflineAfterMs = QueryIntervalMs * 2 + 15000;
            if (OfflineAfterMs < QueryIntervalMs * 2) OfflineAfterMs = QueryIntervalMs * 2;

            // 低电量提醒参数
            int tmp;
            if (opt.ContainsKey("low") && int.TryParse(opt["low"], out tmp)) LowBatteryPercent = tmp;
            if (opt.ContainsKey("low-release") && int.TryParse(opt["low-release"], out tmp)) LowReleasePercent = tmp;
            if (opt.ContainsKey("low-mins") && int.TryParse(opt["low-mins"], out tmp)) LowNotifyCooldownMin = tmp;
            if (opt.ContainsKey("no-low-notify")) LowNotifyEnabled = false;
            if (LowBatteryPercent < 1) LowBatteryPercent = 1;
            if (LowBatteryPercent > 99) LowBatteryPercent = 99;
            if (LowReleasePercent <= LowBatteryPercent) LowReleasePercent = LowBatteryPercent + 4;
            if (LowReleasePercent > 100) LowReleasePercent = 100;
            if (LowNotifyCooldownMin < 1) LowNotifyCooldownMin = 1;

            bool listMode = opt.ContainsKey("list");
            bool testMode = opt.ContainsKey("test");
            bool probeMode = opt.ContainsKey("probe");
            bool probeAll = opt.ContainsKey("probe-all");
            bool notifyTest = opt.ContainsKey("notify-test");
            bool findBattery = opt.ContainsKey("find-battery");

            if (listMode || testMode || probeMode || probeAll || notifyTest || findBattery) EnsureConsole();

            Log("=== InphicBatteryTray9 start: " + string.Join(" ", args));
            Log("config vids=[" + string.Join(",", Vids.ConvertAll(v => v.ToString("X4"))) +
                "] pids=[" + string.Join(",", Pids.ConvertAll(p => p.ToString("X4"))) +
                "] interval=" + QueryIntervalMs + "ms offline=" + OfflineAfterMs + "ms mi=" + (ForceMi < 0 ? "auto" : ForceMi.ToString("X2")) +
                "] low=" + LowBatteryPercent + "% release=" + LowReleasePercent + "% cooldown=" + LowNotifyCooldownMin + "min enabled=" + LowNotifyEnabled);

            if (listMode) { DoList(); return; }
            if (testMode) { DoTest(); return; }
            if (probeMode || probeAll) { DoProbe(probeAll); return; }
            if (findBattery) { DoFindBattery(); return; }
            if (notifyTest) { DoNotifyTest(); return; }

            if (!AcquireSingleInstance()) return;
            try { RunTray(); }
            catch (Exception ex) { CrashLog(ex); }
        }

        // ====================================================================
        // 低电量系统通知
        //   电量 <= LowBatteryPercent 且未在充电 -> 发系统通知;
        //   电量回升到 > LowReleasePercent 才解除 (滞回, 避免在阈值附近反复弹);
        //   持续低电量时每 LowNotifyCooldownMin 分钟最多提醒一次。
        // ====================================================================
        static void CheckLowBattery(DeviceState st)
        {
            if (!LowNotifyEnabled) return;

            bool fire = false;
            int level = st.Battery;

            lock (sync)
            {
                if (level >= 0 && level <= LowBatteryPercent && !st.Charging)
                {
                    bool cooled = (DateTime.Now - st.LastNotify).TotalMinutes >= LowNotifyCooldownMin;
                    if (!st.LowNotified || cooled)
                    {
                        st.LowNotified = true;
                        st.LastNotify = DateTime.Now;
                        fire = true;
                    }
                }
                else if (level > LowReleasePercent || st.Charging)
                {
                    if (st.LowNotified)
                        Log("low-battery state cleared (level=" + level + "% charging=" + st.Charging + ")");
                    st.LowNotified = false;
                }
            }

            if (fire)
            {
                Log("low battery notify: " + level + "% label=" + st.Label);
                ShowLowBatteryNotification(DeviceTitle(st), level);
            }
        }

        // ====================================================================
        // 参数解析
        // ====================================================================
        static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in args)
            {
                if (string.IsNullOrEmpty(a) || !a.StartsWith("--")) continue;
                int eq = a.IndexOf('=');
                string k, v;
                if (eq > 2) { k = a.Substring(2, eq - 2); v = a.Substring(eq + 1); }
                else { k = a.Substring(2); v = "1"; }
                d[k] = v;
            }
            return d;
        }

        static List<ushort> ParseHexList(string s)
        {
            List<ushort> r = new List<ushort>();
            foreach (string p in s.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                ushort v;
                if (ushort.TryParse(p.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v))
                    r.Add(v);
            }
            return r;
        }

        static void EnsureConsole()
        {
            try
            {
                if (!Native.AttachConsole(Native.ATTACH_PARENT_PROCESS)) Native.AllocConsole();
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch { }
        }

        static void ShowHelp()
        {
            Console.WriteLine("InphicBatteryTray9 猫耳版 — 英菲克 IN9 系列鼠标任务栏电量");
            Console.WriteLine();
            Console.WriteLine("用法: InphicBatteryTray9.exe [选项]");
            Console.WriteLine("  (无参数)           托盘常驻 (任务栏小图标 + 左键电量面板)");
            Console.WriteLine("  --list             列出所有 HID 接口 (VID/PID/接口号/报告长度/UsagePage)");
            Console.WriteLine("  --test             对匹配设备发 Feature 0x20 查询, 打印原始响应与解析结果");
            Console.WriteLine("  --probe            对匹配 VID 的接口做一次 Feature 0x20 探测");
            Console.WriteLine("  --probe-all        对全部非键鼠接口做 Feature 探测 (跳过 UsagePage 0x0001)");
            Console.WriteLine("  --find-battery     扫描所有鼠标接口, 尝试读取标准 HID 电量 (通用模式诊断)");
            Console.WriteLine("  --vids=248A,249A   覆盖默认 VID 列表 (十六进制, 逗号分隔)");
            Console.WriteLine("  --pids=XXXX        限定 PID (默认不限)");
            Console.WriteLine("  --mi=02            强制只使用指定 MI 接口号 (十六进制)");
            Console.WriteLine("  --interval=300000  查询间隔 ms (默认 300000 = 5 分钟; 想看实时可设 5000)");
            Console.WriteLine("  --name=名字        自定义显示名称 (任务栏悬停/面板标题, 会保存)");
            Console.WriteLine("  --rename           打开重命名对话框 (也可从右键菜单进入)");
            Console.WriteLine("  --icon=图片路径    设置自定义图标 (复制到图标目录, 建议 192x192 透明 PNG)");
            Console.WriteLine("  --offline=8000     离线判定超时 ms (默认 8000)");
            Console.WriteLine("  --low=20           静默提醒阈值 % (默认 20, 电量<=该值时弹无声音小卡片)");
            Console.WriteLine("  --low-release=25   解除阈值 % (默认 25, 回升到该值以上才重新武装提醒)");
            Console.WriteLine("  --low-mins=30      持续低电量时两次提醒的最小间隔分钟 (默认 30)");
            Console.WriteLine("  --no-low-notify    关闭低电量提醒");
            Console.WriteLine("  --no-anim          关闭托盘图标的呼吸动画");
            Console.WriteLine("  --notify-test      立即弹一次静默提醒 (验证效果, 不播放声音)");
            Console.WriteLine("  --icons-out=目录   导出图标 PNG/.ico 与面板、提醒预览图");
            Console.WriteLine("  --preview=秒       直接预览面板与提醒 (默认 9 秒)");
            Console.WriteLine("  --log=路径         日志文件 (也可用环境变量 INPHIC_TRAY_LOG)");
            Console.WriteLine();
            Console.WriteLine("推荐排查顺序: --list  →  --test  →  无参数常驻");
        }
        // ====================================================================
        // HID 枚举
        // ====================================================================
        static List<HidInterface> Enumerate(bool withCaps)
        {
            List<HidInterface> list = new List<HidInterface>();
            Native.GUID guid;
            Native.HidD_GetHidGuid(out guid);
            IntPtr set = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == Native.INVALID_HANDLE_VALUE) return list;

            try
            {
                int idx = 0;
                while (true)
                {
                    Native.SP_DEVICE_INTERFACE_DATA did = new Native.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, idx, ref did)) break;
                    idx++;

                    int need;
                    Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need <= 0 || need > 65536) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, need, out need, IntPtr.Zero)) continue;
                        string path = ReadDetailPath(buf);
                        if (string.IsNullOrEmpty(path)) continue;

                        HidInterface dev = new HidInterface();
                        dev.Path = path;
                        IntPtr h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (h != Native.INVALID_HANDLE_VALUE)
                        {
                            try
                            {
                                Native.HIDD_ATTRIBUTES attr = new Native.HIDD_ATTRIBUTES();
                                attr.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                                if (Native.HidD_GetAttributes(h, ref attr))
                                {
                                    dev.Vid = attr.VendorID;
                                    dev.Pid = attr.ProductID;
                                }
                                IntPtr sbuf = Marshal.AllocHGlobal(512);
                                try
                                {
                                    if (Native.HidD_GetProductString(h, sbuf, 512))
                                        dev.Product = Marshal.PtrToStringUni(sbuf);
                                }
                                finally { Marshal.FreeHGlobal(sbuf); }
                                if (withCaps) ReadCaps(h, dev);
                            }
                            finally { Native.CloseHandle(h); }
                        }
                        dev.Mi = ParseMi(path);
                        dev.Transport = DetectTransport(path);
                        if (dev.Vid != 0 || dev.Pid != 0) list.Add(dev);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        // SetupDiGetDeviceInterfaceDetailW 的缓冲区: cbSize 之后紧跟设备路径字符串。
        // x86 从偏移 4 开始, x64 从偏移 8 开始; 这里做兼容探测, 以 "\\?\" 开头判定。
        static string ReadDetailPath(IntPtr buf)
        {
            for (int off = 4; off <= 8; off += 4)
            {
                string p = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + off));
                if (!string.IsNullOrEmpty(p) && p.StartsWith("\\\\?\\")) return p;
            }
            return null;
        }

        static void ReadCaps(IntPtr h, HidInterface dev)
        {
            IntPtr pp;
            if (!Native.HidD_GetPreparsedData(h, out pp) || pp == IntPtr.Zero) return;
            try
            {
                Native.HIDP_CAPS caps = new Native.HIDP_CAPS();
                caps.Reserved = new ushort[17];
                if (Native.HidP_GetCaps(pp, out caps) == Native.HIDP_STATUS_SUCCESS)
                {
                    dev.InputLen = caps.InputReportByteLength;
                    dev.OutputLen = caps.OutputReportByteLength;
                    dev.FeatureLen = caps.FeatureReportByteLength;
                    dev.UsagePage = caps.UsagePage;
                    dev.Usage = caps.Usage;
                }
            }
            finally { Native.HidD_FreePreparsedData(pp); }
        }

        static int ParseMi(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            string p = path.ToLowerInvariant();
            int i = p.IndexOf("&mi_");
            if (i < 0) return -1;
            i += 4;
            int j = i;
            while (j < p.Length && IsHex(p[j])) j++;
            if (j <= i) return -1;
            int v;
            if (!int.TryParse(p.Substring(i, j - i), System.Globalization.NumberStyles.HexNumber, null, out v)) return -1;
            return v;
        }

        static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
        }

        static string DetectTransport(string path)
        {
            if (string.IsNullOrEmpty(path)) return "未知";
            string p = path.ToLowerInvariant();
            if (p.Contains("bth") || p.Contains("bluetooth")) return "蓝牙";
            return "USB";
        }

        static string Describe(HidInterface d)
        {
            return string.Format("{0:X4}:{1:X4} mi={2} in={3} out={4} feat={5} up=0x{6:X4} {7} [{8}] {9}",
                d.Vid, d.Pid,
                d.Mi < 0 ? "--" : d.Mi.ToString("X2"),
                d.InputLen, d.OutputLen, d.FeatureLen, d.UsagePage,
                d.Transport,
                string.IsNullOrEmpty(d.Product) ? "?" : d.Product,
                d.Path);
        }

        // 从所有匹配 VID/PID 的接口中选出厂商通道:
        // 1) --mi 强制指定; 2) FeatureLen==33 (官方判定); 3) InputLen==33;
        // 4) MI_02; 5) 全部
        static List<HidInterface> SelectTargets(List<HidInterface> all)
        {
            List<HidInterface> matches = new List<HidInterface>();
            foreach (HidInterface d in all)
            {
                bool vidOk = false;
                foreach (ushort v in Vids) if (d.Vid == v) { vidOk = true; break; }
                if (!vidOk) continue;
                if (Pids.Count > 0)
                {
                    bool pidOk = false;
                    foreach (ushort p in Pids) if (d.Pid == p) { pidOk = true; break; }
                    if (!pidOk) continue;
                }
                matches.Add(d);
            }
            if (matches.Count == 0) return matches;

            if (ForceMi >= 0)
            {
                List<HidInterface> forced = new List<HidInterface>();
                foreach (HidInterface d in matches) if (d.Mi == ForceMi) forced.Add(d);
                Log("select: 强制 MI_" + ForceMi.ToString("X2") + ", " + forced.Count + " 个接口");
                return forced.Count > 0 ? forced : matches;
            }

            // 官方软件的判定顺序 (逆向 0x2CE4E 处):
            //   VID=248A/249A 且 (InputReportByteLength==33 或 FeatureReportByteLength==33)
            // 实测本机接收器命中的是 FeatureReportByteLength==33 的那个接口。
            List<HidInterface> feat33 = new List<HidInterface>();
            List<HidInterface> in33 = new List<HidInterface>();
            List<HidInterface> mi02 = new List<HidInterface>();
            foreach (HidInterface d in matches)
            {
                if (d.FeatureLen == 33) feat33.Add(d);
                if (d.InputLen == 33) in33.Add(d);
                if (d.Mi == 0x02) mi02.Add(d);
            }
            if (feat33.Count > 0)
            {
                Log("select: FeatureLen==33 (厂商通道), " + feat33.Count + " 个接口");
                return feat33;
            }
            if (in33.Count > 0)
            {
                Log("select: 无 FeatureLen==33, 改用 InputLen==33, " + in33.Count + " 个接口");
                return in33;
            }
            if (mi02.Count > 0)
            {
                Log("select: 退回 MI_02, " + mi02.Count + " 个接口");
                return mi02;
            }
            Log("select: 未命中任何 33 字节通道, 用全部 " + matches.Count + " 个匹配接口");
            return matches;
        }

        // ====================================================================
        // IN9 协议: 33 字节帧
        // ====================================================================
        // 构造 Feature Report 帧 (33 字节, 报告ID 0x0C)
        //   [0]=0x0C 报告ID
        //   [1]=选择字 (官方: 接收器句柄时 1; 同时存在两个句柄时置 0)
        //   [2]=命令码 (0x20 查设备信息 / 0xF0 握手)
        //   [3]=0x00
        //   [4]=序号 (每帧自增, 官方用同一个自增计数器)
        //   [5],[6]=参数 (0x20 时填 0,0; 0xF0 时填 1,1)
        //   [7]=0xFF (仅 0xF0 握手帧)
        static byte[] BuildFeatureFrame(byte cmd, byte sel, byte p1, byte p2, bool tail)
        {
            byte[] b = new byte[FrameLen];
            b[0] = FeatureReportId;
            b[1] = sel;
            b[2] = cmd;
            b[3] = 0x00;
            lock (seqLock) { b[4] = seqCounter; seqCounter = (byte)((seqCounter + 1) & 0xFF); }
            b[5] = p1;
            b[6] = p2;
            if (tail) b[7] = 0xFF;
            return b;
        }

        static bool SetFeature(IntPtr h, byte[] frame)
        {
            return Native.HidD_SetFeature(h, frame, frame.Length);
        }

        static bool GetFeature(IntPtr h, byte[] buf)
        {
            Array.Clear(buf, 0, buf.Length);
            buf[0] = FeatureReportId;
            return Native.HidD_GetFeature(h, buf, FrameLen);
        }

        // 一次完整的电量查询: 握手(可选) -> 0x20 Set -> Sleep -> Get
        // 返回是否拿到有效响应; raw 为原始 33 字节
        static bool QueryBattery(IntPtr h, bool handshake, out byte[] raw, out string err)
        {
            raw = null;
            err = null;
            if (handshake)
            {
                SetFeature(h, BuildFeatureFrame(CmdHandshake, 1, 1, 1, true));
                Thread.Sleep(10);
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                byte[] req = BuildFeatureFrame(CmdGetDeviceInfo, 1, 0, 0, false);
                if (!SetFeature(h, req))
                {
                    err = "SetFeature 失败 (ERROR " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                Thread.Sleep(10);
                byte[] resp = new byte[FrameLen];
                if (!GetFeature(h, resp))
                {
                    err = "GetFeature 失败 (ERROR " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                raw = resp;
                if (resp[6] != 0) return true;    // 官方判定: buf[6]!=0 即有效
            }
            err = "4 次尝试后 buf[6] 仍为 0 (鼠标可能休眠/离线)";
            return false;
        }

        static bool WriteReport(IntPtr h, byte[] data, IntPtr ev, ref Native.OVERLAPPED ov, IntPtr ovPtr)
        {
            Marshal.StructureToPtr(ov, ovPtr, false);
            Native.ResetEvent(ev);
            int written;
            bool ok = Native.WriteFile(h, data, data.Length, out written, ovPtr);
            if (ok) return written == data.Length;   // 同步完成
            int err = Marshal.GetLastWin32Error();
            if (err != Native.ERROR_IO_PENDING) return false;
            uint rc = Native.WaitForSingleObject(ev, 500);
            if (rc != Native.WAIT_OBJECT_0) { Native.CancelIoEx(h, ovPtr); return false; }
            int got;
            if (!Native.GetOverlappedResult(h, ovPtr, out got, false)) return false;
            return got == data.Length;
        }

        // 返回: >0 字节数; 0 超时; -1 出错(设备可能已拔出)
        static int ReadReport(IntPtr h, byte[] buf, IntPtr ev, ref Native.OVERLAPPED ov, IntPtr ovPtr, int timeoutMs)
        {
            Marshal.StructureToPtr(ov, ovPtr, false);
            Native.ResetEvent(ev);
            int read;
            bool ok = Native.ReadFile(h, buf, buf.Length, out read, ovPtr);
            if (ok) return read;   // 同步完成
            int err = Marshal.GetLastWin32Error();
            if (err != Native.ERROR_IO_PENDING) return -1;
            uint rc = Native.WaitForSingleObject(ev, (uint)timeoutMs);
            if (rc != Native.WAIT_OBJECT_0) { Native.CancelIoEx(h, ovPtr); return 0; }
            int got;
            if (!Native.GetOverlappedResult(h, ovPtr, out got, false)) return -1;
            return got;
        }

        // 解析 Feature Report 查询响应 (33 字节, 报告ID 0x0C, 命令回显 0x20)
        //   buf[2]==0x20 命令回显, buf[3]==0, buf[6]!=0 有效
        //   buf[0x12]=电量%  buf[0x13]: 1=放电 2=充电中(官方强制显示100%)
        //   buf[0x15]=在线  buf[8..0x0B]="M909" 型号魔数  buf[0x0F]=IC型号
        static bool TryParseBattery(byte[] buf, int n, out int level, out bool charging, out bool online)
        {
            level = -1; charging = false; online = false;
            if (buf == null || n < 0x16) return false;
            if (buf[0] != FeatureReportId) return false;
            if (buf[2] != CmdGetDeviceInfo) return false;
            if (buf[3] != 0x00) return false;
            if (buf[6] == 0x00) return false;

            int lv = buf[0x12];
            byte st = buf[0x13];
            if (st == 2) { level = 100; charging = true; }   // 官方: 充电中显示 100%
            else if (lv <= 100) { level = lv; charging = false; }
            else return false;

            online = buf[0x15] != 0;
            // 实测接收器模式下 buf[0x15] 恒为 0, 但能读到有效电量即视为在线
            if (!online && level >= 0) online = true;
            return true;
        }

        static string DescribeResponse(byte[] b, int n)
        {
            if (b == null || n < 0x16) return "(帧太短)";
            StringBuilder sb = new StringBuilder();
            sb.Append("型号=");
            for (int i = 8; i <= 0x0B; i++)
            {
                char c = (char)b[i];
                sb.Append((c >= 32 && c < 127) ? c : '.');
            }
            sb.Append(" IC=0x" + b[0x0F].ToString("X2"));
            sb.Append(" 电量=" + b[0x12] + "%");
            sb.Append(" 状态=" + b[0x13]);
            sb.Append(" 在线=" + b[0x15]);
            return sb.ToString();
        }

        static string Hex(byte[] b, int n)
        {
            StringBuilder sb = new StringBuilder(n * 3);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(b[i].ToString("X2"));
            }
            return sb.ToString();
        }

        static IntPtr OpenComm(string path)
        {
            // Feature Report (HidD_Set/GetFeature) 是同步 IOCTL, 不需要 OVERLAPPED
            IntPtr h = Native.CreateFileW(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE)
            {
                h = Native.CreateFileW(path, Native.GENERIC_READ,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                    0, IntPtr.Zero);
            }
            if (h == Native.INVALID_HANDLE_VALUE) return IntPtr.Zero;
            return h;
        }

        // ====================================================================
        // 常驻轮询: 每 QueryIntervalMs 用 Feature Report 0x20 查一次电量
        // ====================================================================
        static void QueryLoop(HidInterface dev)
        {
            string label = dev.Label;
            IntPtr h = OpenComm(dev.Path);
            if (h == IntPtr.Zero)
            {
                Log("open failed: " + label);
                return;
            }
            Log("opened: " + Describe(dev));

            int failCount = 0;
            bool first = true;

            try
            {
                while (running)
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        byte[] raw;
                        string err;
                        bool ok = QueryBattery(h, first, out raw, out err);
                        first = false;

                        if (ok && raw != null)
                        {
                            int level; bool charging, online;
                            if (TryParseBattery(raw, raw.Length, out level, out charging, out online))
                            {
                                double ms = sw.Elapsed.TotalMilliseconds;
                                DeviceState st;
                                lock (sync)
                                {
                                    if (!states.TryGetValue(dev.Path, out st))
                                    {
                                        st = new DeviceState();
                                        st.Path = dev.Path;
                                        states[dev.Path] = st;
                                    }
                                    st.Label = label;
                                    st.Battery = level;
                                    st.Charging = charging;
                                    st.LastSeen = DateTime.Now;
                                    st.LastQueryMs = ms;
                                    st.Product = dev.Product;
                                    st.Vid = dev.Vid;
                                    st.Pid = dev.Pid;
                                    st.Transport = dev.Transport;
                                    st.Mi = dev.Mi;
                                }
                                failCount = 0;
                                Log(string.Format("battery {0}% charging={1} {2:F0}ms [{3}]", level, charging, ms, label));
                                CheckLowBattery(st);   // 低电量 -> 系统通知
                            }
                            else
                            {
                                Log("unparsed: " + Hex(raw, raw.Length) + " | " + DescribeResponse(raw, raw.Length) + " [" + label + "]");
                            }
                        }
                        else
                        {
                            failCount++;
                            Log("query failed: " + err + " [" + label + "]");
                            if (failCount >= 5)
                            {
                                Log("连续 5 次查询失败, 暂时放弃该通道: " + label);
                                lock (sync) writeFailCooldown[dev.Path] = DateTime.Now.AddSeconds(30);
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("query error: " + ex.Message + " [" + label + "]");
                    }

                    int sleep = QueryIntervalMs - (int)sw.Elapsed.TotalMilliseconds;
                    if (sleep < 300) sleep = 300;
                    refreshEvent.WaitOne(sleep);
                }
            }
            catch (ThreadInterruptedException) { }
            finally
            {
                try { Native.CloseHandle(h); } catch { }
                lock (sync) activePaths.Remove(dev.Path);
                Log("reader exit: " + label);
            }
        }

        static void Supervise()
        {
            while (running)
            {
                try
                {
                    List<HidInterface> all = Enumerate(true);
                    List<HidInterface> targets = SelectTargets(all);

                    int vidMatch = 0;
                    foreach (HidInterface d in all)
                    {
                        foreach (ushort v in Vids) if (d.Vid == v) { vidMatch++; break; }
                    }
                    Log(string.Format("enumerate: {0} interfaces, {1} vid-matched, {2} selected", all.Count, vidMatch, targets.Count));

                    foreach (HidInterface d in targets)
                    {
                        bool skip = false;
                        lock (sync)
                        {
                            if (activePaths.Contains(d.Path)) skip = true;
                            else if (writeFailCooldown.ContainsKey(d.Path) && writeFailCooldown[d.Path] > DateTime.Now) skip = true;
                            else activePaths.Add(d.Path);
                        }
                        if (skip) continue;

                        HidInterface dd = d;
                        Thread t = new Thread(delegate()
                        {
                            try { QueryLoop(dd); }
                            finally { lock (sync) activePaths.Remove(dd.Path); }
                        });
                        t.IsBackground = true;
                        t.Start();
                        lock (readerThreads) readerThreads.Add(t);
                    }

                    lock (readerThreads)
                    {
                        List<Thread> alive = new List<Thread>();
                        foreach (Thread t in readerThreads) if (t.IsAlive) alive.Add(t);
                        readerThreads.Clear();
                        readerThreads.AddRange(alive);
                    }
                }
                catch (Exception ex)
                {
                    Log("supervise error: " + ex.Message);
                }

                rescanEvent.Wait(10000);
                rescanEvent.Reset();
            }
        }

        // ====================================================================
        // 诊断模式
        // ====================================================================
        static void DoList()
        {
            List<HidInterface> all = Enumerate(true);
            Console.WriteLine("共发现 " + all.Count + " 个 HID 接口:");
            int i = 0;
            foreach (HidInterface d in all)
            {
                Console.WriteLine("[" + i + "] " + Describe(d));
                Log("list[" + i + "]: " + Describe(d));
                i++;
            }
            List<HidInterface> targets = SelectTargets(all);
            Console.WriteLine();
            Console.WriteLine("按当前 VID 列表 (" + string.Join(",", Vids.ConvertAll(v => v.ToString("X4"))) + ") 匹配到 " + targets.Count + " 个接口:");
            foreach (HidInterface d in targets) Console.WriteLine("  * " + Describe(d));
            if (targets.Count == 0)
            {
                Console.WriteLine("  (无) — 请从上面列表中找到你的鼠标 (产品名通常含 INPHIC/IN9),");
                Console.WriteLine("        记下其 VID, 用 --vids=XXXX 重新指定, 例如 --vids=248A --test");
            }
        }

        static void DoTest()
        {
            List<HidInterface> all = Enumerate(true);
            List<HidInterface> targets = SelectTargets(all);
            if (targets.Count == 0)
            {
                Console.WriteLine("未找到匹配设备 (当前 VID 列表: " + string.Join(",", Vids.ConvertAll(v => v.ToString("X4"))) + ")");
                Console.WriteLine("请先运行 --list 查看实际 VID/PID, 再用 --vids=XXXX 指定");
                Log("test: no target device");
                return;
            }
            foreach (HidInterface dev in targets)
            {
                Console.WriteLine("==== 测试设备: " + Describe(dev));
                Log("test device: " + Describe(dev));
                TestDevice(dev, 3);
            }
        }

        static void TestDevice(HidInterface dev, int rounds)
        {
            IntPtr h = OpenComm(dev.Path);
            if (h == IntPtr.Zero)
            {
                Console.WriteLine("  打开失败 (ERROR " + Marshal.GetLastWin32Error() + ") — 设备可能被独占");
                return;
            }
            try
            {
                bool hs = SetFeature(h, BuildFeatureFrame(CmdHandshake, 1, 1, 1, true));
                Console.WriteLine("  握手 0xF0: " + (hs ? "已发送" : "失败 (ERROR " + Marshal.GetLastWin32Error() + ") — 该接口可能不是厂商通道"));
                Thread.Sleep(20);

                for (int r = 1; r <= rounds; r++)
                {
                    Console.WriteLine("  -- 查询 0x20 第 " + r + " 次 --");
                    byte[] raw;
                    string err;
                    bool ok = QueryBattery(h, false, out raw, out err);
                    if (!ok)
                    {
                        Console.WriteLine("     失败: " + err);
                        Thread.Sleep(700);
                        continue;
                    }
                    Console.WriteLine("     原始: " + Hex(raw, raw.Length));
                    Console.WriteLine("     " + DescribeResponse(raw, raw.Length));
                    Log("test raw: " + Hex(raw, raw.Length));
                    int level; bool ch, on;
                    if (TryParseBattery(raw, raw.Length, out level, out ch, out on))
                        Console.WriteLine("     => 电量 " + level + "%  充电=" + (ch ? "是" : "否") + "  在线=" + (on ? "是" : "否"));
                    else
                        Console.WriteLine("     => 无法解析");
                    Thread.Sleep(700);
                }
            }
            finally
            {
                try { Native.CloseHandle(h); } catch { }
            }
        }

        static void DoProbe(bool all)
        {
            List<HidInterface> list = Enumerate(true);
            Console.WriteLine("共发现 " + list.Count + " 个 HID 接口 (明细见日志)");
            foreach (HidInterface d in list) Log("probe list: " + Describe(d));

            List<HidInterface> targets;
            if (all)
            {
                targets = new List<HidInterface>();
                foreach (HidInterface d in list)
                {
                    if (d.UsagePage == 0x0001)
                    {
                        Console.WriteLine("[跳过] 键鼠输入接口 (隐私保护): " + Describe(d));
                        continue;
                    }
                    targets.Add(d);
                }
                Console.WriteLine("探测全部 " + targets.Count + " 个非键鼠接口 (每个转储 2 秒)...");
            }
            else
            {
                targets = SelectTargets(list);
                Console.WriteLine("探测匹配 VID 的 " + targets.Count + " 个接口 (每个转储 2 秒)...");
            }

            foreach (HidInterface dev in targets) ProbeDevice(dev);
            Console.WriteLine("探测完成。正确的 IN9 厂商通道会回 33 字节、buf[2]=0x20、buf[6]!=0 的帧。");
        }

        static void ProbeDevice(HidInterface dev)
        {
            Console.WriteLine("==== 探测: " + Describe(dev));
            IntPtr h = OpenComm(dev.Path);
            if (h == IntPtr.Zero)
            {
                Console.WriteLine("  打开失败 (ERROR " + Marshal.GetLastWin32Error() + ")");
                return;
            }
            try
            {
                // 握手 0xF0 (只发不收)
                bool hs = SetFeature(h, BuildFeatureFrame(CmdHandshake, 1, 1, 1, true));
                Console.WriteLine("  握手 0xF0: " + (hs ? "成功" : "失败 (ERROR " + Marshal.GetLastWin32Error() + ")"));
                Thread.Sleep(20);

                byte[] raw;
                string err;
                bool ok = QueryBattery(h, false, out raw, out err);
                if (!ok)
                {
                    Console.WriteLine("  0x20 查询失败: " + err);
                    return;
                }
                Console.WriteLine("  原始响应 (33B): " + Hex(raw, raw.Length));
                Console.WriteLine("  " + DescribeResponse(raw, raw.Length));
                Log("probe raw: " + Hex(raw, raw.Length));
                int level; bool ch, on;
                if (TryParseBattery(raw, raw.Length, out level, out ch, out on))
                    Console.WriteLine("  => 电量 " + level + "%  充电=" + (ch ? "是" : "否") + "  在线=" + (on ? "是" : "否"));
                else
                    Console.WriteLine("  => 无法解析 (不是 IN9 Feature 通道)");
            }
            finally
            {
                try { Native.CloseHandle(h); } catch { }
            }
        }

    }
}









