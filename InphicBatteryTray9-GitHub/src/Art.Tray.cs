// ============================================================================
//  Art.Tray.cs — 托盘图标: 猫耳能量瓶 (IN9 鼠标轮廓 + 猫耳 + 电量液面)
//
//  16px 下只保留轮廓 + 液面 + 状态符号, 24px 以上才加滚轮 / 眼睛 / 小表情,
//  用"贴纸描边"(深色外描边 + 浅色内描边)保证深/浅任务栏上都清晰.
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace InphicBatteryTray9
{
    internal static class TrayArt
    {
        public static Bitmap Render(int px, int level, bool charging, bool offline, int frame)
        {
            return Render(px, level, charging, offline, frame, 0);
        }

        public static Bitmap Render(int px, int level, bool charging, bool offline, int frame, int pulse)
        {
            return RenderEx(px, level, charging, offline, frame, px >= 24, pulse);
        }

        public static Bitmap RenderEx(int px, int level, bool charging, bool offline, int frame, bool face, int pulse = 0)
        {
            int ss;
            if (px <= 16) ss = 8;
            else if (px <= 24) ss = 6;
            else if (px <= 32) ss = 4;
            else ss = Math.Max(2, 128 / px);

            Bitmap big = new Bitmap(px * ss, px * ss, PixelFormat.Format32bppArgb);
            big.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(big))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.ScaleTransform(px * ss / 32f, px * ss / 32f);
                if (!DrawChibi(g, px, level, charging, offline, frame, face, pulse))
                    Draw(g, px, level, charging, offline, frame, face);
            }
            Bitmap small = D2.Downscale(big, px, px);
            big.Dispose();
            return small;
        }

        static GraphicsPath Ears()
        {
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(new PointF[] {
                new PointF(6.2f, 15.4f), new PointF(7.6f, 2.9f), new PointF(17.8f, 9.6f)
            });
            p.AddPolygon(new PointF[] {
                new PointF(25.8f, 15.4f), new PointF(24.4f, 2.9f), new PointF(14.2f, 9.6f)
            });
            return p;
        }

        static GraphicsPath InnerEars()
        {
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(new PointF[] {
                new PointF(8.5f, 12.9f), new PointF(9.3f, 6.3f), new PointF(15.3f, 10.2f)
            });
            p.AddPolygon(new PointF[] {
                new PointF(23.5f, 12.9f), new PointF(22.7f, 6.3f), new PointF(16.7f, 10.2f)
            });
            return p;
        }

        static GraphicsPath Body()
        {
            GraphicsPath p = new GraphicsPath();
            p.StartFigure();
            p.AddBezier(7.4f, 12.0f, 7.4f, 8.7f, 11.0f, 7.5f, 16f, 7.5f);
            p.AddBezier(16f, 7.5f, 21.0f, 7.5f, 24.6f, 8.7f, 24.6f, 12.0f);
            p.AddBezier(24.6f, 12.0f, 26.5f, 16.6f, 26.8f, 21.9f, 25.3f, 26.1f);
            p.AddBezier(25.3f, 26.1f, 23.9f, 30.1f, 20.2f, 30.7f, 16f, 30.7f);
            p.AddBezier(16f, 30.7f, 11.8f, 30.7f, 8.1f, 30.1f, 6.7f, 26.1f);
            p.AddBezier(6.7f, 26.1f, 5.2f, 21.9f, 5.5f, 16.6f, 7.4f, 12.0f);
            p.CloseFigure();
            return p;
        }

        static void Draw(Graphics g, int px, int level, bool charging, bool offline, int frame, bool face)
        {
            Mood mood = Moods.Of(level, charging, offline);
            bool has = level >= 0;
            int lv = Math.Max(0, Math.Min(100, level));
            float ph = (float)(frame * Math.PI / 3.0);
            bool small = px <= 20;

            Color accent = offline ? Color.FromArgb(0x9A, 0x92, 0xB2) : Theme.LevelColor(level);
            Color accent2 = offline ? Color.FromArgb(0x74, 0x6C, 0x8E) : Theme.LevelColor2(level);

            using (GraphicsPath ears = Ears())
            using (GraphicsPath inner = InnerEars())
            using (GraphicsPath body = Body())
            {
                // 猫耳(在身体后面)
                using (LinearGradientBrush eb = D2.Lin(new RectangleF(6, 2, 20, 14),
                        offline ? Color.FromArgb(0x50, 0x4A, 0x62) : Color.FromArgb(0x55, 0x3D, 0x8E),
                        offline ? Color.FromArgb(0x33, 0x2F, 0x42) : Color.FromArgb(0x2E, 0x22, 0x52), 90f))
                    g.FillPath(eb, ears);
                if (!small || !offline)
                {
                    using (SolidBrush ib = new SolidBrush(offline ? Color.FromArgb(0x74, 0x6C, 0x86) : Color.FromArgb(0xFF, 0x8F, 0xC2)))
                        g.FillPath(ib, inner);
                }

                // 瓶身
                using (LinearGradientBrush bb = D2.Lin(new RectangleF(5, 7, 22, 24),
                        offline ? Color.FromArgb(0x3C, 0x37, 0x4A) : Color.FromArgb(0x2E, 0x23, 0x54),
                        offline ? Color.FromArgb(0x22, 0x20, 0x2C) : Color.FromArgb(0x17, 0x11, 0x2D), 90f))
                    g.FillPath(bb, body);

                // 电量液面
                if (has && !offline && lv > 0)
                {
                    float h = 23.2f * lv / 100f;
                    float top = 30.7f - h;
                    Region old = g.Clip;
                    g.SetClip(body, CombineMode.Replace);
                    RectangleF liquid = new RectangleF(4.8f, top, 22.4f, h + 0.8f);
                    Color deep = D2.Mix(accent, Color.FromArgb(0x12, 0x0C, 0x22), 0.42f);
                    using (LinearGradientBrush lb = D2.Lin(liquid, accent, deep, 90f))
                        g.FillRectangle(lb, liquid);
                    if (!small)
                    {
                        using (SolidBrush hl = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                            g.FillRectangle(hl, 4.8f, top, 22.4f, 0.7f);
                        float bx = 9.8f + (float)Math.Sin(ph) * 1.4f;
                        using (SolidBrush bub = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillEllipse(bub, bx, 26.4f, 1.5f, 1.5f);
                    }
                    g.Clip = old;
                }

                // 滚轮
                if (!small)
                {
                    using (GraphicsPath wheel = D2.Round(14.6f, 10.3f, 2.8f, 4.7f, 1.4f))
                    {
                        using (SolidBrush wb = new SolidBrush(Color.FromArgb(offline ? 80 : 150, 12, 8, 24)))
                            g.FillPath(wb, wheel);
                        using (Pen wp = new Pen(Color.FromArgb(170, 240, 235, 255), 0.7f))
                            g.DrawPath(wp, wheel);
                    }
                }

                // 贴纸描边: 深色外圈 + 浅色内圈
                float outer = small ? 2.6f : 2.3f;
                float innerW = small ? 1.3f : 1.0f;
                using (Pen dark = new Pen(Color.FromArgb(215, 18, 12, 32), outer))
                using (Pen light = new Pen(Color.FromArgb(246, 247, 242, 255), innerW))
                {
                    dark.LineJoin = LineJoin.Round;
                    light.LineJoin = LineJoin.Round;
                    g.DrawPath(dark, ears);
                    g.DrawPath(dark, body);
                    g.DrawPath(light, ears);
                    g.DrawPath(light, body);
                }

                // 表情 / 状态符号
                if (mood == Mood.Low || mood == Mood.Critical)
                {
                    DrawAlert(g, ph);
                }
                else if (mood == Mood.Charging)
                {
                    DrawBolt(g, ph);
                }
                else if (mood == Mood.Offline)
                {
                    DrawSleep(g, small);
                }
                else if (mood == Mood.Unknown)
                {
                    DrawQuestion(g);
                }
                else if (face)
                {
                    DrawFace(g);
                }
            }
        }

        // ---- 头像: 优先用户自定义图片, 其次内嵌图, 都没有则回退到猫耳能量瓶 ----
        static Bitmap chibiAsset;
        static bool chibiTried;
        static Bitmap customIcon;
        static bool customTried;

        public static string CustomIconDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "InphicBatteryTray9");
            }
        }

        // exe 同目录的 icon.png 优先, 其次是 %LOCALAPPDATA%\InphicBatteryTray9\icon.png
        public static string[] CustomIconCandidates()
        {
            return new string[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png"),
                Path.Combine(CustomIconDir, "icon.png")
            };
        }

        public static void ReloadCustomIcon()
        {
            if (customIcon != null) { try { customIcon.Dispose(); } catch { } customIcon = null; }
            if (chibiAsset != null) { try { chibiAsset.Dispose(); } catch { } chibiAsset = null; }
            customTried = false;
            chibiTried = false;
        }

        static Bitmap Square(Bitmap src)
        {
            int side = Math.Min(src.Width, src.Height);
            int x = (src.Width - side) / 2;
            int y = (src.Height - side) / 2;
            Bitmap dst = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, side, side), new Rectangle(x, y, side, side), GraphicsUnit.Pixel);
            }
            return dst;
        }

        static Bitmap Custom()
        {
            if (customTried) return customIcon;
            customTried = true;
            foreach (string p in CustomIconCandidates())
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Bitmap tmp = new Bitmap(fs))
                        customIcon = Square(tmp);
                    break;
                }
                catch { }
            }
            return customIcon;
        }

        static Bitmap Chibi()
        {
            Bitmap c = Custom();
            if (c != null) return c;
            if (chibiTried) return chibiAsset;
            chibiTried = true;
            try
            {
                if (string.IsNullOrEmpty(ChibiAsset.Png)) return null;
                byte[] data = Convert.FromBase64String(ChibiAsset.Png);
                using (MemoryStream ms = new MemoryStream(data))
                using (Bitmap tmp = new Bitmap(ms))
                    chibiAsset = new Bitmap(tmp);
            }
            catch { chibiAsset = null; }
            return chibiAsset;
        }

        static bool DrawChibi(Graphics g, int px, int level, bool charging, bool offline, int frame, bool face, int pulse)
        {
            Bitmap img = Chibi();
            if (img == null) return false;

            Mood mood = Moods.Of(level, charging, offline);
            bool has = level >= 0;
            int lv = Math.Max(0, Math.Min(100, level));
            float ph = (float)(frame * Math.PI / 3.0);
            Color accent = offline ? Color.FromArgb(0x9A, 0x92, 0xB2) : Theme.LevelColor(level);

            RectangleF head = new RectangleF(2.4f, 0.0f, 27.2f, 27.2f);

            // 点击时的一圈扩散光晕
            if (pulse > 0)
            {
                float k = pulse / 7f;
                float grow = (1f - k) * 3.2f;
                using (Pen halo = new Pen(Color.FromArgb((int)(150 * k), accent), 1.6f))
                    g.DrawEllipse(halo, 1.4f - grow, -1.0f - grow, 29.2f + grow * 2, 29.2f + grow * 2);
                D2.Glow(g, new RectangleF(-2 - grow, -3 - grow, 36 + grow * 2, 36 + grow * 2), accent, (int)(55 * k));
            }

            // 柔和投影: 浅色任务栏上也有轮廓
            DrawTinted(g, img, new RectangleF(head.X + 0.25f, head.Y + 0.85f, head.Width, head.Height), Color.Black, 0.34f);

            if (offline)
            {
                float[][] m = {
                    new float[]{0.34f,0.34f,0.34f,0,0},
                    new float[]{0.44f,0.44f,0.44f,0,0},
                    new float[]{0.56f,0.56f,0.56f,0,0},
                    new float[]{0,0,0,0.80f,0},
                    new float[]{0.03f,0.03f,0.04f,0,1}
                };
                DrawMatrix(g, img, head, m);
            }
            else
            {
                g.DrawImage(img, head.X, head.Y, head.Width, head.Height);
            }

            // 底部电量条
            RectangleF bar = new RectangleF(4.4f, 28.2f, 23.2f, 2.6f);
            using (GraphicsPath bp = D2.Round(bar, 1.3f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(232, 32, 25, 56)))
                g.FillPath(tb, bp);

            if (has && lv > 0)
            {
                float w = Math.Max(bar.Height, bar.Width * lv / 100f);
                Region old = g.Clip;
                using (GraphicsPath bp = D2.Round(bar, 1.3f)) g.SetClip(bp, CombineMode.Replace);
                using (LinearGradientBrush fb = D2.Lin(new RectangleF(bar.X, bar.Y, w, bar.Height), accent, offline ? accent : Theme.LevelColor2(level), 0f))
                    g.FillRectangle(fb, bar.X, bar.Y, w, bar.Height);
                g.Clip = old;
            }

            using (GraphicsPath bp = D2.Round(bar, 1.3f))
            using (Pen edge = new Pen(Color.FromArgb(200, 14, 10, 26), 0.55f))
                g.DrawPath(edge, bp);

            // 状态角标
            if (mood == Mood.Charging)
                DrawBadge(g, 26.3f, 5.6f, 5.0f, Theme.Amber, 235, "bolt", ph);
            else if (mood == Mood.Low || mood == Mood.Critical)
                DrawBadge(g, 26.3f, 5.6f, 5.0f, Theme.Rose, 240, "!", ph);
            else if (mood == Mood.Offline)
                DrawBadge(g, 26.3f, 5.6f, 4.6f, Color.FromArgb(0x6E, 0x66, 0x88), 240, "z", ph);
            else if (mood == Mood.Unknown)
                DrawBadge(g, 26.3f, 5.6f, 4.6f, Color.FromArgb(0x86, 0x7C, 0xA4), 240, "?", ph);

            return true;
        }

        static void DrawBadge(Graphics g, float cx, float cy, float r, Color fill, int alpha, string kind, float ph)
        {
            using (SolidBrush ring = new SolidBrush(Color.FromArgb(215, 15, 10, 26)))
                g.FillEllipse(ring, cx - r - 0.55f, cy - r - 0.55f, (r + 0.55f) * 2, (r + 0.55f) * 2);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, fill)))
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);

            if (kind == "bolt")
            {
                PointF[] pts = {
                    new PointF(cx + r * 0.12f, cy - r * 0.68f), new PointF(cx - r * 0.42f, cy + r * 0.10f),
                    new PointF(cx - r * 0.05f, cy + r * 0.10f), new PointF(cx - r * 0.22f, cy + r * 0.72f),
                    new PointF(cx + r * 0.46f, cy - r * 0.14f), new PointF(cx + r * 0.08f, cy - r * 0.14f),
                    new PointF(cx + r * 0.34f, cy - r * 0.68f)
                };
                using (SolidBrush w = new SolidBrush(Color.White)) g.FillPolygon(w, pts);
            }
            else if (kind == "!")
            {
                using (SolidBrush w = new SolidBrush(Color.White))
                {
                    g.FillRectangle(w, cx - r * 0.15f, cy - r * 0.60f, r * 0.30f, r * 0.74f);
                    g.FillEllipse(w, cx - r * 0.16f, cy + r * 0.28f, r * 0.32f, r * 0.32f);
                }
            }
            else
            {
                Font f = F.Num(r * 1.55f, FontStyle.Bold);
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (SolidBrush w = new SolidBrush(Color.White))
                    g.DrawString(kind, f, w, new RectangleF(cx - r, cy - r, r * 2, r * 2), sf);
                sf.Dispose();
            }
        }

        static void DrawGlyphText(Graphics g, string s, float x, float y, Color c, float size)
        {
            Font f = F.Num(size, FontStyle.Bold);
            using (SolidBrush b = new SolidBrush(c))
                g.DrawString(s, f, b, x, y);
        }

        static void DrawTinted(Graphics g, Bitmap img, RectangleF r, Color tint, float alpha)
        {
            float[][] m = {
                new float[]{0,0,0,0,0},
                new float[]{0,0,0,0,0},
                new float[]{0,0,0,0,0},
                new float[]{0,0,0,alpha,0},
                new float[]{tint.R / 255f, tint.G / 255f, tint.B / 255f, 0, 1}
            };
            DrawMatrix(g, img, r, m);
        }

        static void DrawMatrix(Graphics g, Bitmap img, RectangleF r, float[][] matrix)
        {
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(new ColorMatrix(matrix));
                g.DrawImage(img, new PointF[] {
                    new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom)
                }, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
            }
        }
        static void DrawFace(Graphics g)
        {
            float ey = 17.9f;
            using (SolidBrush wb = new SolidBrush(Color.FromArgb(250, 255, 253, 255)))
            {
                g.FillEllipse(wb, 10.7f, ey - 1.9f, 2.9f, 3.7f);
                g.FillEllipse(wb, 18.4f, ey - 1.9f, 2.9f, 3.7f);
            }
            using (SolidBrush pb = new SolidBrush(Color.FromArgb(255, 42, 28, 60)))
            {
                g.FillEllipse(pb, 11.5f, ey - 1.15f, 1.4f, 2.2f);
                g.FillEllipse(pb, 19.2f, ey - 1.15f, 1.4f, 2.2f);
            }
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
            {
                g.FillEllipse(gb, 11.6f, ey - 1.5f, 0.6f, 0.7f);
                g.FillEllipse(gb, 19.3f, ey - 1.5f, 0.6f, 0.7f);
            }
            // 腮红
            using (SolidBrush blush = new SolidBrush(Color.FromArgb(70, 255, 120, 165)))
            {
                g.FillEllipse(blush, 8.4f, 21.4f, 2.4f, 1.4f);
                g.FillEllipse(blush, 21.2f, 21.4f, 2.4f, 1.4f);
            }
            // ω 嘴
            using (Pen mp = new Pen(Color.FromArgb(190, 52, 30, 52), 0.7f))
            {
                mp.StartCap = LineCap.Round; mp.EndCap = LineCap.Round;
                g.DrawArc(mp, 14.9f, 22.4f, 1.2f, 1.3f, 10, 160);
                g.DrawArc(mp, 15.9f, 22.4f, 1.2f, 1.3f, 10, 160);
            }
        }

        static void DrawAlert(Graphics g, float ph)
        {
            float pulse = 0.8f + 0.2f * (float)Math.Sin(ph * 2.0);
            D2.Glow(g, new RectangleF(10, 12, 12, 12), Theme.Rose, (int)(90 * pulse));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(253, 255, 248, 252)))
            using (Pen op = new Pen(Color.FromArgb(160, 70, 10, 40), 0.55f))
            {
                RectangleF bar = new RectangleF(15.1f, 12.9f, 1.8f, 6.1f);
                using (GraphicsPath bp = D2.Round(bar, 0.9f)) { g.FillPath(b, bp); g.DrawPath(op, bp); }
                g.FillEllipse(b, 15.1f, 20.2f, 1.8f, 1.8f);
                g.DrawEllipse(op, 15.1f, 20.2f, 1.8f, 1.8f);
            }
        }

        static void DrawBolt(Graphics g, float ph)
        {
            PointF[] bolt = new PointF[] {
                new PointF(18.0f, 12.0f), new PointF(12.6f, 20.4f), new PointF(15.6f, 20.4f),
                new PointF(13.6f, 27.2f), new PointF(20.2f, 18.0f), new PointF(16.9f, 18.0f),
                new PointF(19.4f, 12.0f)
            };
            D2.Glow(g, new RectangleF(9, 11, 14, 17), Theme.Amber, 100);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 230, 145)))
            using (Pen p = new Pen(Color.FromArgb(150, 90, 52, 0), 0.6f))
            {
                g.FillPolygon(b, bolt);
                g.DrawPolygon(p, bolt);
            }
        }

        static void DrawSleep(Graphics g, bool small)
        {
            Font zf = F.Num(small ? 7f : 6.5f, FontStyle.Bold);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 214, 208, 232)))
            {
                g.DrawString("z", zf, b, 22.6f, 2.4f);
                g.DrawString("z", zf, b, 26.4f, 6.2f);
            }
            using (Pen p = new Pen(Color.FromArgb(235, 220, 214, 240), small ? 1.1f : 0.9f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawArc(p, 10.4f, 17.2f, 3.4f, 2.4f, 20, 140);
                g.DrawArc(p, 18.2f, 17.2f, 3.4f, 2.4f, 20, 140);
            }
        }

        static void DrawQuestion(Graphics g)
        {
            Font qf = F.Num(15f, FontStyle.Bold);
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 240, 235, 255)))
                g.DrawString("?", qf, b, new RectangleF(6, 14, 20, 16), sf);
            sf.Dispose();
        }
    }
}






