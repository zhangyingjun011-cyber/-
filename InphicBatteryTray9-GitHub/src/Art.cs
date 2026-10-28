// ============================================================================
//  Art.cs — 二次元视觉层 (纯绘制, 不涉及业务逻辑)
//
//  设计语言:
//    深夜紫 (midnight violet) 打底 + 樱花粉 / 薄荷青 霓虹点缀,
//    托盘图标 = 猫耳鼠标 (IN9 轮廓 + 猫耳 + 表情 + 电量液面),
//    面板 = 圆角玻璃卡片 + 猫耳娘看板娘 + 电量条,
//    低电量提醒 = 无声音的静默小卡片.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace InphicBatteryTray9
{
    // ------------------------------------------------------------------------
    // 配色 / 字体
    // ------------------------------------------------------------------------
    internal static class Theme
    {
        public static readonly Color Ink        = Color.FromArgb(0x11, 0x0D, 0x20);
        public static readonly Color BgTop      = Color.FromArgb(0x18, 0x12, 0x2E);
        public static readonly Color BgBottom   = Color.FromArgb(0x2B, 0x1F, 0x4E);
        public static readonly Color CardLine   = Color.FromArgb(0x7A, 0x63, 0xB0);
        public static readonly Color Pink       = Color.FromArgb(0xFF, 0x7E, 0xB6);
        public static readonly Color PinkSoft   = Color.FromArgb(0xFF, 0xB3, 0xD4);
        public static readonly Color Mint       = Color.FromArgb(0x53, 0xF2, 0xD0);
        public static readonly Color Cyan       = Color.FromArgb(0x66, 0xDB, 0xFF);
        public static readonly Color Violet     = Color.FromArgb(0xA7, 0x8B, 0xFA);
        public static readonly Color Amber      = Color.FromArgb(0xFF, 0xC4, 0x4D);
        public static readonly Color Rose       = Color.FromArgb(0xFF, 0x5C, 0x8A);
        public static readonly Color Text       = Color.FromArgb(0xF8, 0xF3, 0xFF);
        public static readonly Color TextDim    = Color.FromArgb(0xB3, 0xA6, 0xD4);
        public static readonly Color TextFaint  = Color.FromArgb(0x7C, 0x6F, 0xA2);
        public static readonly Color Track      = Color.FromArgb(0x33, 0x28, 0x57);

        public static Color LevelColor(int level)
        {
            if (level < 0) return TextDim;
            if (level <= 20) return Rose;
            if (level <= 45) return Amber;
            return Mint;
        }

        public static Color LevelColor2(int level)
        {
            if (level < 0) return Violet;
            if (level <= 20) return Color.FromArgb(0xFF, 0x9A, 0x6B);
            if (level <= 45) return Color.FromArgb(0xFF, 0xE1, 0x8A);
            return Cyan;
        }
    }

    // 字体: 优先微软雅黑 UI (中文) / Segoe UI (数字)
    internal static class F
    {
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        public static readonly string UiFamily = Pick("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma");
        public static readonly string NumFamily = Pick("Segoe UI", "Microsoft YaHei UI", "Arial");

        static string Pick(params string[] names)
        {
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    foreach (string n in names)
                    {
                        foreach (FontFamily ff in col.Families)
                        {
                            if (string.Equals(ff.Name, n, StringComparison.OrdinalIgnoreCase)) return n;
                        }
                    }
                }
            }
            catch { }
            return "Segoe UI";
        }

        public static Font Ui(float px, FontStyle st)
        {
            return Get(UiFamily, px, st);
        }

        public static Font Num(float px, FontStyle st)
        {
            return Get(NumFamily, px, st);
        }

        static Font Get(string fam, float px, FontStyle st)
        {
            string key = fam + "|" + px.ToString("0.##") + "|" + (int)st;
            Font f;
            if (!cache.TryGetValue(key, out f))
            {
                try { f = new Font(fam, px, st, GraphicsUnit.Pixel); }
                catch { f = new Font("Segoe UI", px, st, GraphicsUnit.Pixel); }
                cache[key] = f;
            }
            return f;
        }
    }

    // ------------------------------------------------------------------------
    // 绘图小工具
    // ------------------------------------------------------------------------
    internal static class D2
    {
        public static Color A(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0; if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            return Round(r.X, r.Y, r.Width, r.Height, rad);
        }

        public static GraphicsPath Round(float x, float y, float w, float h, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(w, h));
            if (d < 0.4f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static LinearGradientBrush Lin(RectangleF r, Color a, Color b, float angle)
        {
            LinearGradientBrush br = new LinearGradientBrush(r, a, b, angle);
            br.WrapMode = WrapMode.TileFlipXY;
            return br;
        }

        // 径向柔光: 中心亮, 边缘透明
        public static void Glow(Graphics g, RectangleF r, Color c, int centerAlpha)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = A(c, centerAlpha);
                    b.SurroundColors = new Color[] { A(c, 0) };
                    b.CenterPoint = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                    g.FillEllipse(b, r);
                }
            }
        }

        public static void Star(Graphics g, float cx, float cy, float r, Color c, int alpha)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                for (int i = 0; i < 8; i++)
                {
                    double ang = -Math.PI / 2 + i * Math.PI / 4;
                    float rr = (i % 2 == 0) ? r : r * 0.28f;
                    PointF pt = new PointF(cx + (float)Math.Cos(ang) * rr, cy + (float)Math.Sin(ang) * rr);
                    if (i == 0) p.AddLine(pt, pt); else p.AddLine(p.GetLastPoint(), pt);
                }
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(A(c, alpha))) g.FillPath(b, p);
            }
        }

        public static void Heart(Graphics g, RectangleF r, Color c, int alpha)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                float w = r.Width, h = r.Height;
                p.AddBezier(r.X + w * 0.5f, r.Y + h * 0.22f,
                            r.X + w * 0.18f, r.Y - h * 0.12f,
                            r.X - w * 0.04f, r.Y + h * 0.42f,
                            r.X + w * 0.5f, r.Y + h * 0.95f);
                p.AddBezier(r.X + w * 0.5f, r.Y + h * 0.95f,
                            r.X + w * 1.04f, r.Y + h * 0.42f,
                            r.X + w * 0.82f, r.Y - h * 0.12f,
                            r.X + w * 0.5f, r.Y + h * 0.22f);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(A(c, alpha))) g.FillPath(b, p);
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF box,
                                StringAlignment h, StringAlignment v)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, box, sf);
            }
        }

        public static void TextShadow(Graphics g, string s, Font f, Color c, RectangleF box,
                                      StringAlignment h, StringAlignment v, int shadowAlpha)
        {
            RectangleF sh = box;
            sh.Offset(0, 1.2f);
            Text(g, s, f, Color.FromArgb(shadowAlpha, 0, 0, 0), sh, h, v);
            Text(g, s, f, c, box, h, v);
        }

        public static Bitmap Downscale(Bitmap src, int w, int h)
        {
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            dst.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
            }
            return dst;
        }

        public static Bitmap NewCanvas(int w, int h, float scale)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, (int)Math.Round(w * scale)), Math.Max(1, (int)Math.Round(h * scale)), PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);
            return bmp;
        }

        public static Graphics Prep(Bitmap bmp, float scale)
        {
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(scale, scale);
            return g;
        }
    }

    internal enum Mood { Normal, Charging, Low, Critical, Offline, Unknown }

    internal static class Moods
    {
        public static Mood Of(int level, bool charging, bool offline)
        {
            if (level < 0) return Mood.Unknown;
            if (offline) return Mood.Offline;
            if (charging) return Mood.Charging;
            if (level < 0) return Mood.Unknown;
            if (level <= 10) return Mood.Critical;
            if (level <= 20) return Mood.Low;
            return Mood.Normal;
        }

        public static string Name(Mood m)
        {
            switch (m)
            {
                case Mood.Charging: return "充电中";
                case Mood.Low: return "有点饿了";
                case Mood.Critical: return "快没电了喵";
                case Mood.Offline: return "睡着啦";
                case Mood.Unknown: return "寻找中";
                default: return "元气满满";
            }
        }

        public static string Line(Mood m, int level)
        {
            switch (m)
            {
                case Mood.Charging: return "正在补充元气…";
                case Mood.Low: return "只剩 " + level + "% 了，记得充电哦";
                case Mood.Critical: return "呜…真的要没电了喵";
                case Mood.Offline: return "鼠标睡着了 / 暂时断开";
                case Mood.Unknown: return "还没找到 IN9 鼠标";
                default: return level >= 60 ? "状态极佳，随便用" : "电量还算充足";
            }
        }
    }

    // ------------------------------------------------------------------------
    // 托盘图标: 猫耳鼠标
    //   32x32 设计空间, 渲染时超采样后缩小, 保证 16px 下依旧干净
    // ------------------------------------------------------------------------
    internal static class ToastArt
    {
        public const int W = 336;
        public const int H = 112;

        public static Bitmap Render(int level, string device, float scale)
        {
            string dev = string.IsNullOrEmpty(device) ? "IN9 鼠标" : device;
            string body = string.Format("{0} 只剩 {1}%，记得充电哦", dev, Math.Max(0, level));
            return RenderCard("电量告急喵～", body, "静默提醒 · 不播放提示音", level, Mood.Critical, scale);
        }

        public static Bitmap RenderCard(string title, string body, string footer, int level, Mood mood, float scale)
        {
            Bitmap bmp = D2.NewCanvas(W, H, scale);
            using (Graphics g = D2.Prep(bmp, scale))
                DrawCard(g, title, body, footer, level, mood, 0f);
            return bmp;
        }

        public static void DrawCard(Graphics g, string title, string body, string footer, int level, Mood mood, float t)
        {
            bool info = (mood == Mood.Normal || mood == Mood.Charging);
            Color accent = info ? Theme.Cyan : Theme.Rose;
            Color accent2 = info ? Theme.Mint : Color.FromArgb(0xFF, 0xA2, 0x6B);

            RectangleF card = new RectangleF(0.5f, 0.5f, W - 1, H - 1);
            using (GraphicsPath rr = D2.Round(card, 16))
            {
                using (LinearGradientBrush bg = D2.Lin(card, Color.FromArgb(0x1D, 0x15, 0x35), Color.FromArgb(0x33, 0x22, 0x50), 60f))
                    g.FillPath(bg, rr);

                Region old = g.Clip;
                g.SetClip(rr, CombineMode.Replace);
                D2.Glow(g, new RectangleF(-60, -70, 220, 220), accent, 60);
                D2.Glow(g, new RectangleF(200, 40, 180, 180), Theme.Pink, 40);
                g.Clip = old;

                using (Pen border = new Pen(Color.FromArgb(120, accent), 1.2f))
                    g.DrawPath(border, rr);
            }

            // 看板娘
            Mascot.Draw(g, new RectangleF(12, 20, 74, 74), mood, t);

            // 标题
            D2.TextShadow(g, title, F.Ui(14.5f, FontStyle.Bold), Color.FromArgb(255, 255, 226, 240),
                new RectangleF(94, 14, 214, 22), StringAlignment.Near, StringAlignment.Center, 120);

            // 正文 (最多两行)
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush b = new SolidBrush(D2.A(Theme.Text, 218)))
                    g.DrawString(body, F.Ui(10.5f, FontStyle.Regular), b, new RectangleF(94, 38, 228, 32), sf);
            }

            // 电量条
            RectangleF bar = new RectangleF(94, 72, 222, 6);
            using (GraphicsPath bp = D2.Round(bar, 3f))
            using (SolidBrush tb = new SolidBrush(Theme.Track))
                g.FillPath(tb, bp);
            float pct = info ? 100f : Math.Max(0, Math.Min(100, level));
            float fw = Math.Max(6, bar.Width * pct / 100f);
            using (GraphicsPath bp = D2.Round(bar, 3f))
            {
                Region old = g.Clip;
                g.SetClip(bp, CombineMode.Replace);
                using (LinearGradientBrush fb = D2.Lin(new RectangleF(bar.X, bar.Y, fw, bar.Height), accent, accent2, 0f))
                    g.FillRectangle(fb, bar.X, bar.Y, fw, bar.Height);
                g.Clip = old;
            }

            // 脚注
            D2.Text(g, footer, F.Ui(9f, FontStyle.Regular), Theme.TextFaint,
                new RectangleF(94, 86, 214, 16), StringAlignment.Near, StringAlignment.Center);
            D2.Heart(g, new RectangleF(304, 86, 10, 10), Theme.Pink, 190);

            // 关闭提示
            D2.Text(g, "×", F.Num(12f, FontStyle.Regular), Color.FromArgb(140, 230, 215, 245),
                new RectangleF(306, 6, 22, 20), StringAlignment.Center, StringAlignment.Center);
        }
    }

    // 多尺寸 ICO 生成
    // ------------------------------------------------------------------------
    internal static class IcoBuilder
    {
        public static Icon Build(int[] sizes, Func<int, Bitmap> render)
        {
            List<byte[]> frames = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (Bitmap bmp = render(s))
                    frames.Add(EncodeDib(bmp));
            }

            int count = frames.Count;
            int offset = 6 + 16 * count;
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((ushort)1); bw.Write((ushort)32);
                bw.Write(frames[i].Length);
                bw.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) bw.Write(f);
            bw.Flush();
            ms.Position = 0;
            return new Icon(ms, new Size(sizes[0], sizes[0]));
        }

        static byte[] EncodeDib(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int andStride = ((w + 31) / 32) * 4;
            int xorSize = w * h * 4;
            int andSize = andStride * h;
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write(0); bw.Write(xorSize + andSize);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = h - 1; y >= 0; y--)
                {
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, w * 4);
                    bw.Write(row);
                }
            }
            finally { bmp.UnlockBits(bd); }

            byte[] and = new byte[andSize];
            BitmapData bd2 = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(bd2.Scan0.ToInt64() + (long)(h - 1 - y) * bd2.Stride), row, 0, w * 4);
                    int line = (h - 1 - y) * andStride;
                    for (int x = 0; x < w; x++)
                    {
                        if (row[x * 4 + 3] == 0)
                            and[line + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }
            finally { bmp.UnlockBits(bd2); }
            bw.Write(and);
            bw.Flush();
            return ms.ToArray();
        }
    }
}









