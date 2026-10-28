// ============================================================================
//  Art.Bubble.cs — 点击任务栏图标时弹出的迷你电量气泡 (2 秒后自动消失)
//  设计: 小圆角卡片 + 大号百分比 + 竖电池; 数字和电池液面有 0->当前值 的动画
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace InphicBatteryTray9
{
    internal static class BubbleArt
    {
        public const int W = 172;
        public const int H = 64;

        public static Bitmap Render(int level, bool charging, bool offline, string name, float t, float scale)
        {
            Bitmap bmp = D2.NewCanvas(W, H, scale);
            using (Graphics g = D2.Prep(bmp, scale))
                Draw(g, level, charging, offline, name, t);
            return bmp;
        }

        public static void Draw(Graphics g, int level, bool charging, bool offline, string name, float t)
        {
            bool has = level >= 0;
            int lv = Math.Max(0, Math.Min(100, level));
            float shown = has ? lv * Math.Max(0f, Math.Min(1f, t)) : 0f;
            Mood mood = Moods.Of(level, charging, offline);
            Color accent = offline ? Color.FromArgb(0x9A, 0x92, 0xB2) : Theme.LevelColor(level);
            Color accent2 = offline ? Color.FromArgb(0x7E, 0x76, 0x96) : Theme.LevelColor2(level);

            RectangleF card = new RectangleF(0.5f, 0.5f, W - 1, H - 1);
            using (GraphicsPath rr = D2.Round(card, 15))
            {
                using (LinearGradientBrush bg = D2.Lin(card, Color.FromArgb(0x1A, 0x14, 0x31), Color.FromArgb(0x2C, 0x21, 0x4E), 30f))
                    g.FillPath(bg, rr);

                Region old = g.Clip;
                g.SetClip(rr, CombineMode.Replace);
                D2.Glow(g, new RectangleF(-30, -30, 130, 130), accent, 55);
                g.Clip = old;

                using (Pen border = new Pen(Color.FromArgb(110, accent), 1f))
                    g.DrawPath(border, rr);
            }

            // 大号百分比
            string num = has ? ((int)Math.Round(shown)).ToString() : "--";
            Font nf = F.Num(30f, FontStyle.Bold);
            using (LinearGradientBrush gb = D2.Lin(new RectangleF(14, 8, 96, 36), accent, accent2, 0f))
            {
                StringFormat sf = new StringFormat();
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(num, nf, gb, new RectangleF(14, 6, 96, 38), sf);
                sf.Dispose();
            }
            float nw = g.MeasureString(num, nf, 200, StringFormat.GenericTypographic).Width;
            Font uf = F.Num(12f, FontStyle.Bold);
            using (SolidBrush ub = new SolidBrush(D2.A(accent, 225)))
                g.DrawString("%", uf, ub, 16 + nw, 30);

            // 名字 / 状态
            string sub = string.IsNullOrEmpty(name) ? "IN9" : name;
            if (mood == Mood.Offline) sub += " · 已休眠";
            else if (mood == Mood.Unknown) sub = "未检测到鼠标";
            else if (charging) sub += " · 充电中";
            if (sub.Length > 12) sub = sub.Substring(0, 12) + "…";
            D2.Text(g, sub, F.Ui(9.5f, FontStyle.Regular), Color.FromArgb(215, Theme.TextDim),
                new RectangleF(15, 44, 100, 14), StringAlignment.Near, StringAlignment.Center);

            // 竖电池
            RectangleF body = new RectangleF(128, 16, 28, 38);
            using (GraphicsPath bp = D2.Round(body, 6))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, 30, 24, 52)))
                g.FillPath(tb, bp);
            if (shown > 0.5f)
            {
                float fh = Math.Max(4f, (body.Height - 5f) * (float)shown / 100f);
                RectangleF fill = new RectangleF(body.X + 2.5f, body.Bottom - 2.5f - fh, body.Width - 5f, fh);
                Region old = g.Clip;
                using (GraphicsPath bp = D2.Round(new RectangleF(body.X + 2.5f, body.Y + 2.5f, body.Width - 5f, body.Height - 5f), 4))
                    g.SetClip(bp, CombineMode.Replace);
                using (LinearGradientBrush fb = D2.Lin(new RectangleF(fill.X, body.Y, fill.Width, body.Height), accent, accent2, 90f))
                    g.FillRectangle(fb, fill);
                g.Clip = old;
            }
            using (GraphicsPath bp = D2.Round(body, 6))
            using (Pen p = new Pen(Color.FromArgb(225, 236, 230, 255), 1.1f))
                g.DrawPath(p, bp);
            using (GraphicsPath cap = D2.Round(new RectangleF(135, 10.5f, 14, 6), 2.5f))
            using (SolidBrush cb = new SolidBrush(Color.FromArgb(235, 236, 230, 255)))
                g.FillPath(cb, cap);

            // 充电闪电
            if (charging)
            {
                PointF[] bolt = {
                    new PointF(145.5f, 24f), new PointF(135f, 38f), new PointF(141f, 38f),
                    new PointF(137.5f, 49f), new PointF(149f, 34f), new PointF(143f, 34f),
                    new PointF(147.5f, 24f)
                };
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 224, 130)))
                    g.FillPolygon(b, bolt);
                using (Pen p = new Pen(Color.FromArgb(150, 90, 52, 0), 0.7f))
                    g.DrawPolygon(p, bolt);
            }
        }
    }
}

