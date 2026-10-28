// ============================================================================
//  GenericMode.cs — 通用模式: 非 IN9 鼠标走标准 HID 电量 (Battery Strength)
//
//  优先级: 先跑 IN9 专用协议; 只有在没发现 IN9 时, 才去扫描其它鼠标的
//  标准电量字段. 这样插着 IN9 时行为和原来完全一致.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;

namespace InphicBatteryTray9
{
    static partial class Program
    {
        // 面板/托盘上显示的设备名
        internal static string DeviceTitle(DeviceState st)
        {
            if (!string.IsNullOrEmpty(AppSettings.DisplayName)) return AppSettings.DisplayName;
            if (st == null) return "INPHIC IN9";
            if (st.Vid == 0x248A || st.Vid == 0x249A) return "INPHIC IN9";
            string p = st.Product;
            if (string.IsNullOrEmpty(p)) p = "无线鼠标";
            if (p.IndexOf("符合 HID", StringComparison.Ordinal) >= 0 ||
                p.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("USB 输入设备", StringComparison.OrdinalIgnoreCase))
                p = "无线鼠标";
            if (p.Length > 22) p = p.Substring(0, 22) + "…";
            return p;
        }

        static bool In9Active()
        {
            lock (sync)
            {
                foreach (KeyValuePair<string, DeviceState> kv in states)
                {
                    if (kv.Key.StartsWith("GEN:", StringComparison.Ordinal)) continue;
                    if ((DateTime.Now - kv.Value.LastSeen).TotalSeconds < 15) return true;
                }
            }
            return false;
        }

        // 通用模式总控: 没 IN9 时找一个能读标准电量的鼠标
        static void GenericSupervise()
        {
            int misses = 0;
            while (running)
            {
                try
                {
                    if (In9Active()) { Thread.Sleep(5000); continue; }

                    HidInterface dev;
                    HidBatteryProbe probe;
                    IntPtr h, pp;
                    if (TryProbeGeneric(out dev, out probe, out h, out pp))
                    {
                        misses = 0;
                        Log("generic: " + dev.Label + " " + probe.Describe());
                        RunGenericLoop(dev, probe, h, pp);
                    }
                    else
                    {
                        // 没找到就别一直扫, 逐步退避
                        misses++;
                        Thread.Sleep(misses <= 2 ? 15000 : 60000);
                    }
                }
                catch (Exception ex)
                {
                    Log("generic supervise: " + ex.Message);
                    Thread.Sleep(8000);
                }
            }
        }

        static bool TryProbeGeneric(out HidInterface best, out HidBatteryProbe bestProbe,
                                    out IntPtr bestHandle, out IntPtr bestPp)
        {
            best = null; bestProbe = null; bestHandle = IntPtr.Zero; bestPp = IntPtr.Zero;

            List<HidInterface> all = Enumerate(true);
            List<HidInterface> ordered = new List<HidInterface>();
            foreach (HidInterface d in all)
                if (d.UsagePage == 0x0001 && d.Usage == 0x0002) ordered.Add(d);
            foreach (HidInterface d in all)
                if (!(d.UsagePage == 0x0001 && d.Usage == 0x0002)) ordered.Add(d);

            foreach (HidInterface d in ordered)
            {
                if (d.FeatureLen <= 0 && d.InputLen <= 0) continue;
                string prod = d.Product == null ? "" : d.Product;
                if (prod.IndexOf("键盘", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prod.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                IntPtr h = OpenComm(d.Path);
                if (h == IntPtr.Zero) continue;

                IntPtr pp;
                if (!Native.HidD_GetPreparsedData(h, out pp) || pp == IntPtr.Zero)
                {
                    Native.CloseHandle(h);
                    continue;
                }

                Native.HIDP_CAPS caps;
                if (Native.HidP_GetCaps(pp, out caps) != Native.HIDP_STATUS_SUCCESS)
                {
                    Native.HidD_FreePreparsedData(pp);
                    Native.CloseHandle(h);
                    continue;
                }

                HidBatteryProbe probe = HidBattery.Find(h, caps, pp);
                if (probe == null)
                {
                    Native.HidD_FreePreparsedData(pp);
                    Native.CloseHandle(h);
                    continue;
                }

                int lv; uint raw;
                if (!HidBattery.Read(h, probe, out lv, out raw))
                {
                    Native.HidD_FreePreparsedData(pp);
                    Native.CloseHandle(h);
                    continue;
                }

                best = d; bestProbe = probe; bestHandle = h; bestPp = pp;
                Log("generic candidate: " + d.Label + " " + probe.Describe() + " raw=" + raw + " => " + lv + "%");
                return true;
            }
            return false;
        }

        static void RunGenericLoop(HidInterface dev, HidBatteryProbe probe, IntPtr h, IntPtr pp)
        {
            string key = "GEN:" + dev.Path;
            int fails = 0;
            try
            {
                while (running && fails < 5)
                {
                    if (In9Active()) break;   // IN9 上线了, 让位

                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    int level; uint raw;
                    if (HidBattery.Read(h, probe, out level, out raw))
                    {
                        fails = 0;
                        DeviceState st;
                        lock (sync)
                        {
                            if (!states.TryGetValue(key, out st))
                            {
                                st = new DeviceState();
                                st.Path = key;
                                states[key] = st;
                            }
                            st.Label = dev.Label;
                            st.Battery = level;
                            st.Charging = false;
                            st.LastSeen = DateTime.Now;
                            st.LastQueryMs = sw.Elapsed.TotalMilliseconds;
                            st.Product = dev.Product;
                            st.Vid = dev.Vid;
                            st.Pid = dev.Pid;
                            st.Transport = dev.Transport;
                            st.Mi = dev.Mi;
                        }
                        Log(string.Format("generic battery {0}% raw={1} [{2}]", level, raw, dev.Label));
                        CheckLowBattery(st);
                    }
                    else
                    {
                        fails++;
                    }
                    Thread.Sleep(5000);
                }
            }
            catch (ThreadInterruptedException) { }
            finally
            {
                try { Native.HidD_FreePreparsedData(pp); } catch { }
                try { Native.CloseHandle(h); } catch { }
                lock (sync) states.Remove(key);
                Log("generic loop exit: " + dev.Label);
            }
        }

        // 诊断: 列出所有接口, 逐个尝试标准电量
        static void DoFindBattery()
        {
            Console.WriteLine("扫描 HID 接口, 尝试读取标准电量 (Usage 0x0006/0x0020 等)...");
            List<HidInterface> all = Enumerate(true);
            Console.WriteLine("共 " + all.Count + " 个 HID 接口");
            int found = 0;

            foreach (HidInterface d in all)
            {
                if (d.FeatureLen <= 0 && d.InputLen <= 0) continue;
                Console.WriteLine();
                Console.WriteLine("---- " + d.Label);
                Console.WriteLine("     VID:PID = " + d.Vid.ToString("X4") + ":" + d.Pid.ToString("X4") +
                                  "  in=" + d.InputLen + " feat=" + d.FeatureLen +
                                  "  usagePage=0x" + d.UsagePage.ToString("X4") + " usage=0x" + d.Usage.ToString("X4"));

                IntPtr h = OpenComm(d.Path);
                if (h == IntPtr.Zero)
                {
                    Console.WriteLine("     打开失败 (ERROR " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                    continue;
                }

                IntPtr pp;
                if (!Native.HidD_GetPreparsedData(h, out pp) || pp == IntPtr.Zero)
                {
                    Console.WriteLine("     无法读取 HID 描述");
                    Native.CloseHandle(h);
                    continue;
                }

                try
                {
                    Native.HIDP_CAPS caps;
                    if (Native.HidP_GetCaps(pp, out caps) != Native.HIDP_STATUS_SUCCESS)
                    {
                        Console.WriteLine("     HidP_GetCaps 失败");
                        continue;
                    }
                    HidBatteryProbe probe = HidBattery.Find(h, caps, pp);
                    if (probe == null)
                    {
                        Console.WriteLine("     没有标准电量字段");
                        continue;
                    }
                    int level; uint raw;
                    bool ok = HidBattery.Read(h, probe, out level, out raw);
                    if (ok)
                    {
                        found++;
                        Console.WriteLine("     * 命中 " + probe.Describe() + "  raw=" + raw + "  电量=" + level + "%");
                    }
                    else
                    {
                        Console.WriteLine("     找到 " + probe.Describe() + " 但读取失败");
                    }
                }
                finally
                {
                    Native.HidD_FreePreparsedData(pp);
                    Native.CloseHandle(h);
                }
            }

            Console.WriteLine();
            Console.WriteLine("完成: " + found + " 个接口可以读到标准电量。");
            if (found == 0)
                Console.WriteLine("说明这些鼠标没有暴露标准 Battery Strength; 只能用厂商私有协议。");
        }
    }
}


