// ============================================================================
//  HidBattery.cs — 通用 HID 电量读取 (标准 Battery Strength)
//
//  很多无线鼠标会把电量放在标准 HID 用法里:
//    Usage Page 0x0006 (Generic Device Controls) / Usage 0x0020 (Battery Strength)
//    Usage Page 0x0085 (Battery System)          / Usage 0x0065 (Relative State of Charge)
//  这些值可能在 Feature Report, 也可能在 Input Report 里。
//
//  这里不解析 report descriptor 的复杂结构, 而是对每个报告 ID 直接尝试
//  HidP_GetUsageValue: 命中就记住 (报告类型 + ID), 之后只读那一条.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InphicBatteryTray9
{
    internal sealed class HidBatteryProbe
    {
        public int ReportType;          // Native.HIDP_FEATURE / HIDP_INPUT
        public byte ReportId;
        public int ReportLength;
        public ushort UsagePage;
        public ushort Usage;
        public IntPtr Preparsed;
        public int LogicalMax = 100;

        public string Describe()
        {
            return string.Format("{0} report id=0x{1:X2} usage=0x{2:X4}/0x{3:X4}",
                ReportType == Native.HIDP_FEATURE ? "Feature" : "Input",
                ReportId, UsagePage, Usage);
        }
    }

    internal static class HidBattery
    {
        // 常见电量用法, 按优先级尝试
        static readonly ushort[,] Usages = new ushort[,] {
            { 0x0006, 0x0020 },   // Battery Strength
            { 0x0085, 0x0065 },   // Relative State of Charge
            { 0x0085, 0x0066 }    // Absolute State of Charge
        };

        /// <summary>在已打开的接口上找电量用法; 找不到返回 null。</summary>
        public static HidBatteryProbe Find(IntPtr h, Native.HIDP_CAPS caps, IntPtr preparsed)
        {
            HidBatteryProbe p = TryReportType(h, caps, preparsed, Native.HIDP_FEATURE, caps.FeatureReportByteLength);
            if (p != null) return p;
            return TryReportType(h, caps, preparsed, Native.HIDP_INPUT, caps.InputReportByteLength);
        }

        static HidBatteryProbe TryReportType(IntPtr h, Native.HIDP_CAPS caps, IntPtr preparsed, int type, int len)
        {
            if (len <= 0 || len > 4096) return null;
            for (int u = 0; u < Usages.GetLength(0); u++)
            {
                ushort page = Usages[u, 0];
                ushort usage = Usages[u, 1];
                for (int id = 0; id <= 16; id++)
                {
                    byte[] buf = new byte[len];
                    if (!GetReport(h, type, (byte)id, buf, len)) continue;
                    uint value;
                    int st;
                    try
                    {
                        st = Native.HidP_GetUsageValue(type, (byte)id, 0, page, usage, preparsed, buf, len, out value);
                    }
                    catch { continue; }
                    if (st != Native.HIDP_STATUS_SUCCESS) continue;
                    if (value > 1000) continue;   // 明显不是电量
                    HidBatteryProbe p = new HidBatteryProbe();
                    p.ReportType = type;
                    p.ReportId = (byte)id;
                    p.ReportLength = len;
                    p.UsagePage = page;
                    p.Usage = usage;
                    p.Preparsed = preparsed;
                    p.LogicalMax = value > 100 ? 255 : 100;
                    return p;
                }
            }
            return null;
        }

        static bool GetReport(IntPtr h, int type, byte id, byte[] buf, int len)
        {
            try
            {
                if (type == Native.HIDP_FEATURE)
                {
                    buf[0] = id;
                    return Native.HidD_GetFeature(h, buf, len);
                }
                Array.Clear(buf, 0, buf.Length);
                bool ok = Native.HidD_GetInputReport(h, buf, len);
                if (!ok) return false;
                // 有些设备返回的报告不带 ID, 这里不强求
                return true;
            }
            catch { return false; }
        }

        /// <summary>读取一次电量 (0-100)。</summary>
        public static bool Read(IntPtr h, HidBatteryProbe p, out int level, out uint raw)
        {
            level = -1;
            raw = 0;
            if (p == null || h == IntPtr.Zero) return false;
            byte[] buf = new byte[p.ReportLength];
            if (!GetReport(h, p.ReportType, p.ReportId, buf, p.ReportLength)) return false;

            uint value;
            int st;
            try
            {
                st = Native.HidP_GetUsageValue(p.ReportType, p.ReportId, 0, p.UsagePage, p.Usage,
                    p.Preparsed, buf, p.ReportLength, out value);
            }
            catch { return false; }
            if (st != Native.HIDP_STATUS_SUCCESS) return false;
            raw = value;

            int lv;
            if (value <= 100) lv = (int)value;
            else if (value <= 255) lv = (int)Math.Round(value * 100.0 / 255.0);
            else lv = -1;
            if (lv < 0 || lv > 100) return false;
            level = lv;
            return true;
        }
    }
}
