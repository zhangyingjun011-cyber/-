// ============================================================================
//  AppSettings.cs — 用户设置 (显示名称 / 查询间隔)
//  存在 %LOCALAPPDATA%\InphicBatteryTray9\settings.ini, 纯文本可直接编辑
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InphicBatteryTray9
{
    internal static class AppSettings
    {
        public const int DefaultIntervalMs = 300000;   // 默认 5 分钟查一次

        public static string DisplayName = "";         // 空 = 自动取设备名
        public static int IntervalMs = DefaultIntervalMs;

        static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "InphicBatteryTray9");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "name")
                    {
                        DisplayName = v;
                    }
                    else if (k == "interval")
                    {
                        int n;
                        if (int.TryParse(v, out n) && n >= 500) IntervalMs = n;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string>();
                lines.Add("# InphicBatteryTray9 settings");
                lines.Add("# name = 任务栏/面板上显示的名字 (留空自动识别)");
                lines.Add("# interval = 查询间隔毫秒 (默认 300000 = 5 分钟)");
                lines.Add("name=" + DisplayName);
                lines.Add("interval=" + IntervalMs);
                File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
