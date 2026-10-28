// ============================================================================
//  Art.Mascot.cs — 看板娘「小九」: 猫耳少女 (100x100 设计空间)
//    表情随电量变化: 元气 / 充电星星眼 / 委屈 / 快没电 / 睡着 / 未知
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace InphicBatteryTray9
{
    internal static class Mascot
    {
        public static void Draw(Graphics g, RectangleF box, Mood mood, float t)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(box.Width / 100f, box.Height / 100f);

            bool sad = (mood == Mood.Low || mood == Mood.Critical);

            // 背景柔光
            D2.Glow(g, new RectangleF(2, 8, 96, 92), sad ? Theme.Rose : Theme.Pink, 42);

            Color hairA = Color.FromArgb(0x70, 0x52, 0xAE);
            Color hairB = Color.FromArgb(0x31, 0x24, 0x58);
            Color hairLine = Color.FromArgb(0x1E, 0x14, 0x38);
            Color skinA = Color.FromArgb(0xFF, 0xF2, 0xEA);
            Color skinB = Color.FromArgb(0xFF, 0xD6, 0xC9);

            // 后发
            using (GraphicsPath back = new GraphicsPath())
            {
                back.AddEllipse(10, 12, 80, 78);
                using (LinearGradientBrush b = D2.Lin(new RectangleF(10, 12, 80, 78), hairA, hairB, 110f))
                    g.FillPath(b, back);
                using (Pen edge = new Pen(Color.FromArgb(130, 18, 12, 32), 1.6f))
                    g.DrawPath(edge, back);
            }

            // 猫耳
            DrawEar(g, new PointF(19, 31), new PointF(23, 3), new PointF(44, 21), hairA, hairB, hairLine);
            DrawEar(g, new PointF(81, 31), new PointF(77, 3), new PointF(56, 21), hairA, hairB, hairLine);

            // 脸
            using (GraphicsPath face = new GraphicsPath())
            {
                face.AddBezier(27, 44, 27, 27, 38, 23, 50, 23);
                face.AddBezier(50, 23, 62, 23, 73, 27, 73, 44);
                face.AddBezier(73, 44, 73, 61, 63, 75, 50, 75);
                face.AddBezier(50, 75, 37, 75, 27, 61, 27, 44);
                face.CloseFigure();
                using (LinearGradientBrush b = D2.Lin(new RectangleF(27, 23, 46, 52), skinA, skinB, 90f))
                    g.FillPath(b, face);
                using (Pen edge = new Pen(Color.FromArgb(80, 120, 70, 90), 0.9f))
                    g.DrawPath(edge, face);
            }

            // 侧发
            DrawSideLock(g, 29, 38, 22, 48, 26, 78, 34, 50, hairA, hairB, true);
            DrawSideLock(g, 71, 38, 78, 48, 74, 78, 66, 50, hairA, hairB, false);

            // 刘海
            using (GraphicsPath bang = new GraphicsPath())
            {
                bang.AddEllipse(20, 5, 60, 46);
                using (LinearGradientBrush b = D2.Lin(new RectangleF(20, 5, 60, 46),
                        D2.Mix(hairA, Color.White, 0.10f), hairB, 105f))
                    g.FillPath(b, bang);
            }
            DrawBangLocks(g, hairA, hairB);

            // 发丝高光
            using (Pen hl = new Pen(Color.FromArgb(80, 255, 255, 255), 2.2f))
            {
                hl.StartCap = LineCap.Round; hl.EndCap = LineCap.Round;
                g.DrawArc(hl, 28, 9, 44, 40, 200, 55);
            }
            using (Pen rim = new Pen(Color.FromArgb(100, 255, 168, 214), 2.0f))
            {
                rim.StartCap = LineCap.Round; rim.EndCap = LineCap.Round;
                g.DrawArc(rim, 13, 15, 74, 72, 192, 62);
            }

            // 呆毛
            using (Pen ah = new Pen(D2.Mix(hairA, Color.White, 0.15f), 2.3f))
            {
                ah.StartCap = LineCap.Round; ah.EndCap = LineCap.Round;
                g.DrawBezier(ah, 53, 10, 50, 0, 62, 1, 60, 10);
            }

            // 樱花发饰
            DrawSakura(g, 25.5f, 35.5f, 6.2f);

            // 眉毛
            Color browColor = Color.FromArgb(0x4A, 0x33, 0x6E);
            using (Pen bp = new Pen(browColor, 1.7f))
            {
                bp.StartCap = LineCap.Round; bp.EndCap = LineCap.Round;
                float tilt = sad ? 2.2f : 0f;
                g.DrawArc(bp, 33, 45 - tilt, 12, 5, 200, 120);
                g.DrawArc(bp, 55, 45 + tilt, 12, 5, 220, 120);
            }

            DrawEyes(g, mood, t);

            // 腮红
            using (SolidBrush blush = new SolidBrush(Color.FromArgb(78, 255, 116, 160)))
            {
                g.FillEllipse(blush, 29, 59, 14, 7);
                g.FillEllipse(blush, 57, 59, 14, 7);
            }
            using (Pen bl = new Pen(Color.FromArgb(95, 255, 140, 175), 0.8f))
            {
                g.DrawLine(bl, 32, 61.5f, 34.5f, 63.5f);
                g.DrawLine(bl, 35, 61, 37.5f, 63);
                g.DrawLine(bl, 62.5f, 63.5f, 65, 61.5f);
                g.DrawLine(bl, 65.5f, 63, 68, 61);
            }

            DrawMouth(g, mood);

            // 情绪点缀
            if (mood == Mood.Charging)
            {
                D2.Star(g, 87, 30, 5.5f, Theme.Mint, 225);
                D2.Star(g, 11, 44, 3.4f, Theme.Cyan, 180);
                D2.Star(g, 91, 60, 3.0f, Theme.Pink, 170);
            }
            else if (sad)
            {
                using (GraphicsPath sweat = new GraphicsPath())
                {
                    sweat.AddBezier(84, 38, 89, 45, 90, 49, 87, 51);
                    sweat.AddBezier(87, 51, 82.5f, 51, 82.5f, 46, 84, 38);
                    sweat.CloseFigure();
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 146, 222, 255)))
                        g.FillPath(b, sweat);
                    using (Pen sp = new Pen(Color.FromArgb(120, 255, 255, 255), 0.8f))
                        g.DrawPath(sp, sweat);
                }
            }
            else if (mood == Mood.Offline)
            {
                Font zf = F.Num(13f, FontStyle.Bold);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 205, 196, 238)))
                {
                    g.DrawString("z", zf, b, 82, 24);
                    g.DrawString("Z", zf, b, 90, 11);
                }
            }
            else if (mood == Mood.Unknown)
            {
                Font qf = F.Num(22f, FontStyle.Bold);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(225, 214, 205, 242)))
                    g.DrawString("?", qf, b, 76, 12);
            }

            g.Restore(st);
        }

        static void DrawSideLock(Graphics g, float x1, float y1, float x2, float y2,
                                 float x3, float y3, float x4, float y4,
                                 Color hairA, Color hairB, bool left)
        {
            using (GraphicsPath side = new GraphicsPath())
            {
                side.AddBezier(x1, y1, x2, y2, x2, 62, x3, 76);
                side.AddBezier(x3, 76, x3 + (left ? 7 : -7), 80, x4 + (left ? 2 : -2), 66, x4, y4);
                side.AddBezier(x4, y4, x4, 42, x4 + (left ? -1 : 1), y1, x1, y1);
                side.CloseFigure();
                using (LinearGradientBrush b = D2.Lin(new RectangleF(Math.Min(x1, x3), y1, 14, 42), hairA, hairB, left ? 100f : 80f))
                    g.FillPath(b, side);
                using (Pen edge = new Pen(Color.FromArgb(90, 22, 15, 40), 1.0f))
                    g.DrawPath(edge, side);
            }
        }

        static void DrawEar(Graphics g, PointF b1, PointF tip, PointF b2, Color hairA, Color hairB, Color line)
        {
            using (GraphicsPath ear = new GraphicsPath())
            {
                ear.AddBezier(b1, new PointF(b1.X - 1.5f, tip.Y + 6), tip, tip);
                ear.AddBezier(tip, new PointF(tip.X + 4, tip.Y + 3), b2, b2);
                ear.CloseFigure();
                using (LinearGradientBrush b = D2.Lin(new RectangleF(10, 0, 80, 34), hairA, hairB, 90f))
                    g.FillPath(b, ear);
                using (Pen p = new Pen(line, 1.2f))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawPath(p, ear);
                }
                using (GraphicsPath inner = new GraphicsPath())
                {
                    PointF c = new PointF((b1.X + b2.X + tip.X) / 3f, (b1.Y + b2.Y + tip.Y) / 3f);
                    inner.AddBezier(
                        new PointF(c.X - 5, c.Y + 3),
                        new PointF(c.X - 3, c.Y - 7),
                        new PointF(c.X + 1, c.Y - 7),
                        new PointF(c.X + 4, c.Y + 2));
                    inner.AddBezier(
                        new PointF(c.X + 4, c.Y + 2),
                        new PointF(c.X + 1, c.Y + 6),
                        new PointF(c.X - 3, c.Y + 7),
                        new PointF(c.X - 5, c.Y + 3));
                    inner.CloseFigure();
                    using (LinearGradientBrush b = D2.Lin(new RectangleF(c.X - 6, c.Y - 8, 12, 16),
                            Color.FromArgb(255, 255, 158, 198), Color.FromArgb(230, 240, 96, 156), 90f))
                        g.FillPath(b, inner);
                }
            }
        }

        static void DrawBangLocks(Graphics g, Color hairA, Color hairB)
        {
            using (LinearGradientBrush b = D2.Lin(new RectangleF(24, 22, 52, 30), hairA, hairB, 90f))
            {
                using (GraphicsPath l1 = new GraphicsPath())
                {
                    l1.AddBezier(27, 26, 26, 38, 30, 44, 36, 47);
                    l1.AddBezier(36, 47, 39, 40, 38, 33, 40, 27);
                    l1.CloseFigure();
                    g.FillPath(b, l1);
                }
                using (GraphicsPath l2 = new GraphicsPath())
                {
                    l2.AddBezier(73, 26, 74, 38, 70, 44, 64, 47);
                    l2.AddBezier(64, 47, 61, 40, 62, 33, 60, 27);
                    l2.CloseFigure();
                    g.FillPath(b, l2);
                }
                using (GraphicsPath l3 = new GraphicsPath())
                {
                    l3.AddBezier(46, 26, 46, 36, 44, 42, 41, 45);
                    l3.AddBezier(41, 45, 50, 46, 56, 44, 56, 44);
                    l3.AddBezier(56, 44, 54, 38, 54, 31, 54, 26);
                    l3.CloseFigure();
                    g.FillPath(b, l3);
                }
            }
        }

        static void DrawSakura(Graphics g, float cx, float cy, float r)
        {
            for (int i = 0; i < 5; i++)
            {
                GraphicsState s = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(i * 72f);
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(-r * 0.36f, -r * 1.02f, r * 0.72f, r * 0.95f);
                    using (LinearGradientBrush b = D2.Lin(new RectangleF(-r, -r, 2 * r, 2 * r),
                            Color.FromArgb(255, 255, 186, 219), Color.FromArgb(255, 255, 112, 168), 90f))
                        g.FillPath(b, p);
                    using (Pen pp = new Pen(Color.FromArgb(90, 190, 40, 100), 0.7f))
                        g.DrawPath(pp, p);
                }
                g.Restore(s);
            }
            using (SolidBrush c = new SolidBrush(Color.FromArgb(255, 255, 238, 168)))
                g.FillEllipse(c, cx - r * 0.24f, cy - r * 0.24f, r * 0.48f, r * 0.48f);
            using (SolidBrush c2 = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                g.FillEllipse(c2, cx - r * 0.10f, cy - r * 0.10f, r * 0.20f, r * 0.20f);
        }

        static void DrawEyes(Graphics g, Mood mood, float t)
        {
            float cy = 55f;
            float lx = 39.5f, rx = 60.5f;

            if (mood == Mood.Offline)
            {
                using (Pen p = new Pen(Color.FromArgb(235, 74, 50, 100), 2.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, lx - 6.5f, cy - 3.5f, 13, 8, 15, 150);
                    g.DrawArc(p, rx - 6.5f, cy - 3.5f, 13, 8, 15, 150);
                }
                return;
            }

            if (mood == Mood.Charging)
            {
                using (Pen p = new Pen(Color.FromArgb(245, 74, 50, 100), 2.4f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, lx - 7, cy - 3.5f, 14, 8, 200, 140);
                    g.DrawArc(p, rx - 7, cy - 3.5f, 14, 8, 200, 140);
                }
                D2.Star(g, lx - 11, cy - 11, 2.6f, Theme.Mint, 210);
                D2.Star(g, rx + 11, cy - 12, 2.2f, Theme.Pink, 210);
                return;
            }

            float scale = (mood == Mood.Critical) ? 0.86f : 1f;
            foreach (float x in new float[] { lx, rx })
            {
                float w = 13.5f * scale, h = 15.5f * scale;
                RectangleF eye = new RectangleF(x - w / 2, cy - h / 2, w, h);

                using (GraphicsPath ep = new GraphicsPath())
                {
                    ep.AddEllipse(eye);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(252, 255, 250, 253)))
                        g.FillPath(b, ep);
                }

                RectangleF iris = new RectangleF(eye.X + 1.2f, eye.Y + 1.4f, eye.Width - 2.4f, eye.Height - 2.6f);
                using (LinearGradientBrush ib = D2.Lin(iris, Color.FromArgb(0x6F, 0xE6, 0xFF), Color.FromArgb(0x9B, 0x7B, 0xEC), 90f))
                {
                    using (GraphicsPath ip = new GraphicsPath())
                    {
                        ip.AddEllipse(iris);
                        g.FillPath(ib, ip);
                    }
                }

                float pw = iris.Width * 0.42f;
                float phh = iris.Height * 0.52f;
                using (SolidBrush pb = new SolidBrush(Color.FromArgb(255, 32, 22, 52)))
                    g.FillEllipse(pb, x - pw / 2, iris.Y + iris.Height * 0.32f, pw, phh);

                using (SolidBrush hb = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                    g.FillEllipse(hb, x - iris.Width * 0.30f, iris.Y + iris.Height * 0.16f, iris.Width * 0.32f, iris.Width * 0.32f);
                using (SolidBrush hb2 = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
                    g.FillEllipse(hb2, x + iris.Width * 0.10f, iris.Y + iris.Height * 0.58f, iris.Width * 0.16f, iris.Width * 0.16f);

                using (Pen lid = new Pen(Color.FromArgb(245, 58, 38, 82), 2.6f))
                {
                    lid.StartCap = LineCap.Round; lid.EndCap = LineCap.Round;
                    g.DrawArc(lid, eye.X - 0.6f, eye.Y - 0.6f, eye.Width + 1.2f, eye.Height + 1.2f, 195, 150);
                }
                using (Pen low = new Pen(Color.FromArgb(90, 90, 62, 120), 1.0f))
                    g.DrawArc(low, eye.X + 1, eye.Y + 1, eye.Width - 2, eye.Height - 2, 20, 120);
                // 外眼角睫毛
                using (Pen lash = new Pen(Color.FromArgb(220, 58, 38, 82), 1.4f))
                {
                    lash.StartCap = LineCap.Round; lash.EndCap = LineCap.Round;
                    float dir = x < 50 ? -1 : 1;
                    g.DrawLine(lash, x + dir * (w / 2 - 1), eye.Y + 2.5f, x + dir * (w / 2 + 2.6f), eye.Y - 0.6f);
                }
            }

            if (mood == Mood.Critical)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 150, 220, 255)))
                {
                    g.FillEllipse(b, lx + 5.5f, cy + 5.5f, 2.2f, 3.2f);
                    g.FillEllipse(b, rx + 5.5f, cy + 5.5f, 2.2f, 3.2f);
                }
            }
        }

        static void DrawMouth(Graphics g, Mood mood)
        {
            using (Pen p = new Pen(Color.FromArgb(225, 92, 52, 76), 1.5f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                if (mood == Mood.Charging)
                {
                    using (GraphicsPath m = new GraphicsPath())
                    {
                        m.AddBezier(46, 64, 48, 70, 52, 70, 54, 64);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(225, 120, 58, 86)))
                            g.FillPath(b, m);
                    }
                }
                else if (mood == Mood.Critical)
                {
                    g.DrawArc(p, 44, 64, 12, 5, 200, 140);
                }
                else if (mood == Mood.Low)
                {
                    g.DrawArc(p, 45, 64, 10, 3.5f, 200, 140);
                }
                else if (mood == Mood.Offline)
                {
                    g.DrawArc(p, 46.5f, 64, 7, 3, 200, 140);
                }
                else
                {
                    g.DrawArc(p, 45.4f, 64, 4.6f, 3.4f, 10, 160);
                    g.DrawArc(p, 50.0f, 64, 4.6f, 3.4f, 10, 160);
                }
            }
        }
    }
}
