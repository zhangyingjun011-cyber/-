// ============================================================================
//  TrayPin.cs — 把图标"放进任务栏"而不是缩在 ^ 溢出菜单里
//
//  Windows 11 把新图标默认丢进溢出区, 且没有公开 API 可以固定。
//  但每个图标在 HKCU\Control Panel\NotifyIconSettings 下有一条记录,
//  其中 IsPromoted=1 就表示"显示在任务栏上"。这里在图标注册后把那一条改掉,
//  并在资源管理器下次读取时生效 (通常重开资源管理器或下次开机)。
// ============================================================================

using System;
using Microsoft.Win32;

namespace InphicBatteryTray9
{
    internal static class TrayPin
    {
        const string SETTINGS = @"Control Panel\NotifyIconSettings";
        const string APP_KEY = @"Software\InphicBatteryTray9";

        /// <summary>把当前 exe 对应的托盘图标设为"显示在任务栏"。返回是否有改动。</summary>
        public static bool TryPromote(string exePath, out bool found)
        {
            found = false;
            bool changed = false;
            if (string.IsNullOrEmpty(exePath)) return false;
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(SETTINGS, true))
                {
                    if (root == null) return false;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey k = root.OpenSubKey(name, true))
                        {
                            if (k == null) continue;
                            string p = k.GetValue("ExecutablePath") as string;
                            if (p == null) continue;
                            if (!string.Equals(p, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                            found = true;
                            object v = k.GetValue("IsPromoted");
                            int cur = 0;
                            try { if (v != null) cur = Convert.ToInt32(v); } catch { }
                            if (cur != 1)
                            {
                                k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                                changed = true;
                            }
                        }
                    }
                }
            }
            catch { }
            return changed;
        }

        public static bool HintShown()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(APP_KEY, false))
                    return k != null && k.GetValue("PinHint") != null;
            }
            catch { return false; }
        }

        public static void MarkHintShown()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(APP_KEY))
                    if (k != null) k.SetValue("PinHint", 1, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
