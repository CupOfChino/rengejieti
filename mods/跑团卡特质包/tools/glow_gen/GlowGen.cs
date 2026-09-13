// 「心」金色光带效果的离线生成器（逐像素光栅化）。
//
// 为什么用 C# 而不是 PowerShell：光带需要"沿曲线变化宽度 + 横向边缘渐隐 + 局部暗口 + 亮芯"，
// 这些必须逐像素算，PowerShell 跑 48 帧×512² 会慢到不可用。
//
// 设计依据（外部建议）：主体是"有尖端、腰部、断口、亮边的弯曲薄光带"，不是柔边椭圆。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace GlowGen
{
    public static class Gen
    {
        const int W = 512;
        const int H = 512;
        // 第二轮：角色在画布上的高度改小到 245px，这样画布能装下"高过角色头顶"的气焰。
        // 对应 AnimSize 也从 0.302 改成约 0.53（0.302 × 429 ÷ 245）。
        const double H_PX = 245.0;
        const double FRAME_SEC = 0.2;

        class Band
        {
            public double Birth, Life;
            public double X0, Y0;
            public double WidthMax, Length;
            public double Alpha;
            public int Type;              // 0 主带 1 火舌
            public double BK, BN, BPhase, BAmpLow, BAmpHigh;
            public double WobAmp, WobK, WobN, WobPhase;
            public double[] WKeys = new double[7];
            public double N1Pos, N1Len, N1Depth, N1Speed, N1Side;
            public double N2Pos, N2Len, N2Depth, N2Speed, N2Side;
            public bool HasN2;
            public double CoreStart, CoreLen, CoreSide, CoreAlpha;
            public double CoreW, VUp;
            public double EdgeLeft, EdgeRight;      // 左右边缘的"硬度"（一侧清晰、一侧消散）
            public double[] LXS, LYS;               // 该层的生命周期亮度曲线
            public double WidthVar;                 // 宽度随时间的波动幅度（±比例）
            public bool HasCore;
            public double R0, G0, B0, R1, G1, B1;
        }

        class Spark
        {
            public double Birth, Life, X0, Y0, R, Alpha, Tail;
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }
        static double Lerp(double a, double b, double k) { return a + (b - a) * k; }
        static double Smooth(double t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        static double Curve(double[] xs, double[] ys, double t)
        {
            if (t <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (t <= xs[i])
                {
                    double k = (t - xs[i - 1]) / (xs[i] - xs[i - 1]);
                    return Lerp(ys[i - 1], ys[i], Smooth(k));
                }
            }
            return ys[ys.Length - 1];
        }

        static readonly double[] LifeXS = { 0.00, 0.18, 0.45, 0.72, 1.00 };
        static readonly double[] LifeYS = { 0.00, 0.80, 1.00, 0.65, 0.00 };
        // 宽度轮廓的 s 位置（窄根 → 展开 → 收腰 → 再展开 → 收尖）
        static readonly double[] WidthS = { 0.00, 0.12, 0.30, 0.52, 0.70, 0.86, 1.00 };

        static Random _rnd;
        static double Rand(double a, double b) { return a + _rnd.NextDouble() * (b - a); }

        static Band MakeBand(int type, double birth, int region)
        {
            Band b = new Band();
            b.Type = type;
            b.Birth = birth;
            if (type == 0)                 // 宽火焰面
            {
                b.Life = 3.2;
                b.WidthMax = Rand(0.09, 0.16) * H_PX;
                b.Length = Rand(0.75, 1.05) * H_PX;
                b.Alpha = Rand(0.70, 0.95);
                b.WidthVar = 0.20;
                b.R0 = 255; b.G0 = 216; b.B0 = 90;    // #FFD85A
                b.R1 = 255; b.G1 = 240; b.B1 = 166;   // #FFF0A6
                b.LXS = new double[] { 0.00, 0.15, 0.35, 0.70, 0.90, 1.00 };
                b.LYS = new double[] { 0.00, 0.80, 1.00, 1.00, 0.55, 0.00 };
            }
            else if (type == 1)            // 中等光带
            {
                b.Life = 2.4;
                b.WidthMax = Rand(0.025, 0.050) * H_PX;
                b.Length = Rand(0.40, 0.70) * H_PX;
                b.Alpha = Rand(0.65, 0.85);
                b.WidthVar = 0.12;
                b.R0 = 255; b.G0 = 223; b.B0 = 101;   // #FFDF65
                b.R1 = 255; b.G1 = 242; b.B1 = 179;   // #FFF2B3
                b.LXS = new double[] { 0.00, 0.18, 0.45, 0.72, 1.00 };
                b.LYS = new double[] { 0.00, 0.80, 1.00, 0.65, 0.00 };
            }
            else                           // 短火舌
            {
                b.Life = 1.6;
                b.WidthMax = Rand(0.030, 0.060) * H_PX;
                b.Length = Rand(0.16, 0.30) * H_PX;
                b.Alpha = Rand(0.45, 0.70);
                b.WidthVar = 0.10;
                b.R0 = 255; b.G0 = 209; b.B0 = 90;
                b.R1 = 255; b.G1 = 230; b.B1 = 160;
                b.LXS = new double[] { 0.00, 0.18, 0.45, 0.72, 1.00 };
                b.LYS = new double[] { 0.00, 0.80, 1.00, 0.65, 0.00 };
            }

            double feet = 256.0 + 0.5 * H_PX;          // 脚底
            if (type == 0)
            {
                int r = region % 3;
                if (r == 0)      { b.X0 = 256.0 - Rand(0.10, 0.20) * H_PX; b.Y0 = feet - Rand(0.02, 0.12) * H_PX; }
                else if (r == 1) { b.X0 = 256.0 + Rand(0.10, 0.20) * H_PX; b.Y0 = feet - Rand(0.25, 0.40) * H_PX; }
                else             { b.X0 = 256.0 + Rand(-0.06, 0.06) * H_PX; b.Y0 = feet - Rand(0.45, 0.60) * H_PX; }
                b.VUp = Rand(0.02, 0.05) * H_PX;        // 几乎不上升，靠拉长和内部结构动
                b.BAmpLow = Rand(0.025, 0.035) * H_PX;
                b.BAmpHigh = Rand(0.035, 0.050) * H_PX;
                b.BK = Rand(1.0, 1.4);
                b.BN = 2 + _rnd.Next(3);                // 0.2~0.4Hz
            }
            else if (type == 1)
            {
                double zone = _rnd.Next(3) - 1;
                b.X0 = 256.0 + zone * Rand(0.10, 0.18) * H_PX;
                b.Y0 = feet - Rand(0.05, 0.45) * H_PX;
                b.VUp = Rand(0.06, 0.10) * H_PX;
                b.BAmpLow = Rand(0.015, 0.022) * H_PX;
                b.BAmpHigh = Rand(0.022, 0.030) * H_PX;
                b.BK = Rand(1.0, 1.5);
                b.BN = 3 + _rnd.Next(4);                // 0.3~0.6Hz
            }
            else
            {
                double zone = _rnd.Next(3) - 1;
                b.X0 = 256.0 + zone * Rand(0.10, 0.22) * H_PX;
                b.Y0 = feet - Rand(0.02, 0.20) * H_PX;
                b.VUp = Rand(0.10, 0.16) * H_PX;
                b.BAmpLow = Rand(0.008, 0.015) * H_PX;
                b.BAmpHigh = Rand(0.015, 0.025) * H_PX;
                b.BK = Rand(1.0, 1.5);
                b.BN = 3 + _rnd.Next(4);
            }

            b.WobAmp = Rand(0.004, 0.010) * H_PX;
            b.WobK = Rand(2.5, 3.5);
            b.WobN = Rand(4.0, 6.0);
            b.WobPhase = _rnd.NextDouble() * Math.PI * 2;
            b.BPhase = _rnd.NextDouble() * Math.PI * 2;

            double j1 = Rand(0.85, 1.15), j2 = Rand(0.85, 1.15), j3 = Rand(0.70, 0.95);
            b.WKeys[0] = 0.00;
            b.WKeys[1] = Clamp(0.55 * j1, 0, 1);
            b.WKeys[2] = Clamp(1.00 * j2, 0, 1);
            b.WKeys[3] = Clamp(0.45 * j1, 0, 1);
            b.WKeys[4] = Clamp(0.70 * j2, 0, 1);
            b.WKeys[5] = Clamp(0.30 * j3, 0, 1);
            b.WKeys[6] = 0.00;
            if (type == 0)
            {
                b.WKeys[1] = Clamp(0.45 * j1, 0, 1);
                b.WKeys[3] = Clamp(0.35 * j1, 0, 1);
                b.WKeys[4] = Clamp(0.80 * j2, 0, 1);
                b.WKeys[5] = Clamp(0.22 * j3, 0, 1);
            }

            b.N1Pos = _rnd.NextDouble();
            b.N1Len = type == 0 ? Rand(0.10, 0.20) : Rand(0.08, 0.18);
            b.N1Depth = type == 0 ? Rand(0.55, 0.78) : Rand(0.50, 0.75);
            b.N1Speed = Rand(0.6, 1.4) * (_rnd.Next(2) == 0 ? 1 : -1);
            b.N1Side = Rand(-0.6, 0.6);
            b.HasN2 = (type != 0) && _rnd.Next(100) < 40;
            b.N2Pos = _rnd.NextDouble();
            b.N2Len = Rand(0.06, 0.12);
            b.N2Depth = Rand(0.40, 0.65);
            b.N2Speed = Rand(0.4, 1.0) * (_rnd.Next(2) == 0 ? 1 : -1);
            b.N2Side = Rand(-0.6, 0.6);

            b.HasCore = (type == 0) || (type == 1 && _rnd.Next(100) < 60);
            b.CoreStart = Rand(0.15, 0.60);
            b.CoreLen = Rand(0.20, 0.45);
            b.CoreSide = Rand(-0.45, 0.45);
            b.CoreAlpha = Rand(0.90, 1.00);
            b.CoreW = Rand(0.010, 0.018) * H_PX;
            b.EdgeLeft = Rand(0.45, 0.95);
            b.EdgeRight = Rand(0.45, 0.95);
            return b;
        }

        static Spark MakeSpark(double birth)
        {
            Spark s = new Spark();
            s.Birth = birth;
            s.Life = 1.6;
            double zone = _rnd.Next(3) - 1;
            s.X0 = 256.0 + zone * Rand(0.10, 0.26) * H_PX + Rand(-0.04, 0.04) * H_PX;
            s.Y0 = 470.0 - Rand(0.02, 0.20) * H_PX;
            s.R = Rand(1.0, 2.5);                    // 亮点直径 2~5px
            s.Alpha = Rand(0.65, 0.95);
            s.Tail = Rand(5.0, 10.0);
            return s;
        }

        // 画一条光带（含亮芯）。rgb/alpha 是累加缓冲（尺寸 W*H）
        static void DrawBand(double[] rgb, double[] a, Band b, double t, double loop, bool withCore)
        {
            double age = ((t - b.Birth) % loop + loop) % loop;
            if (age >= b.Life) { return; }
            double u = age / b.Life;
            double lifeEnv = Curve(b.LXS, b.LYS, u);
            if (lifeEnv <= 0.001) { return; }

            // 长度随时间伸缩：出生时短、中段拉长、后段再伸长一点
            double[] lenXS = { 0.00, 0.20, 0.55, 0.80, 1.00 };
            double[] lenYS = { 0.35, 1.00, 1.00, 1.15, 1.05 };
            double length = b.Length * Curve(lenXS, lenYS, u);
            double yBottom = b.Y0 - b.VUp * age;
            // 末段"下段先淡掉"：靠近 s=0 的部分先消失
            double tailFade = Smooth((u - 0.80) / 0.20);

            int x0 = (int)Math.Max(0, b.X0 - 30);
            int x1 = (int)Math.Min(W - 1, b.X0 + 30);
            int y0 = (int)Math.Max(0, yBottom - length - 6);
            int y1 = (int)Math.Min(H - 1, yBottom + 6);

            for (int y = y0; y <= y1; y++)
            {
                double s = (yBottom - y) / length;
                if (s < 0 || s > 1) { continue; }
                double k1 = 2 * Math.PI * (b.BK * s - b.BN * (t / loop)) + b.BPhase;
                double k2 = 2 * Math.PI * (b.WobK * s - b.WobN * (t / loop)) + b.WobPhase;
                double bend = Lerp(b.BAmpLow, b.BAmpHigh, s) * Math.Sin(k1) + b.WobAmp * Math.Sin(k2);
                double xc = b.X0 + bend;
                // 宽度还会随时间去轻微涨落（±WidthVar），避免像剪出来的带子
                double wv = 1 + b.WidthVar * Math.Sin(2 * Math.PI * (0.7 * (t / loop) + b.WobPhase * 0.3));
                double hw = b.WidthMax * 0.5 * Curve(WidthS, b.WKeys, s) * wv;

                // 暗口（沿长度缓慢上移，局部压暗并收窄）
                double notch = 0;
                double side = 0;
                double n1 = NotchAt(s, b.N1Pos, b.N1Speed, b.N1Len, t, loop);
                if (n1 > 0) { notch = Math.Max(notch, n1 * b.N1Depth); side = b.N1Side; }
                if (b.HasN2)
                {
                    double n2 = NotchAt(s, b.N2Pos, b.N2Speed, b.N2Len, t, loop);
                    if (n2 > 0) { notch = Math.Max(notch, n2 * b.N2Depth); side = b.N2Side; }
                }
                double hwN = hw * (1 - 0.25 * notch);

                for (int x = x0; x <= x1; x++)
                {
                    double d = x - xc;
                    double ad = Math.Abs(d);
                    if (ad > hwN) { continue; }
                    double e = ad / hwN;
                    // 一侧清晰、一侧消散（宽面尤其明显）
                    double hard = d < 0 ? b.EdgeLeft : b.EdgeRight;
                    double edge = 1.0 - Smooth((e - hard) / Math.Max(0.05, 1.0 - hard));
                    double sideFactor = 1.0;
                    if (notch > 0 && side != 0)
                    {
                        double sgn = d < 0 ? -1 : 1;
                        if (sgn == Math.Sign(side)) { sideFactor = 1.0 - 0.5 * notch; }
                    }
                    double localFade = 1.0 - (1.0 - s) * tailFade;
                    double alpha = b.Alpha * lifeEnv * edge * sideFactor * localFade * (1.0 - notch);
                    if (alpha <= 0.002) { continue; }
                    double cr = Lerp(b.R0, b.R1, s);
                    double cg = Lerp(b.G0, b.G1, s);
                    double cb = Lerp(b.B0, b.B1, s);
                    Over(rgb, a, x, y, cr, cg, cb, alpha);

                    // 亮芯：断续、偏一侧、只占宿主一部分长度
                    if (withCore && b.HasCore && s >= b.CoreStart && s <= b.CoreStart + b.CoreLen)
                    {
                        double cu = (s - b.CoreStart) / b.CoreLen;
                        double taper = Math.Sin(Math.PI * cu);
                        double gate = 0.55 + 0.45 * Math.Sin(2 * Math.PI * (3.0 * s + 1.7 * (t / loop)));
                        if (gate < 0.25) { gate = 0; }
                        double cx = xc + b.CoreSide * hwN;
                        double cd = Math.Abs(x - cx);
                        if (cd <= b.CoreW)
                        {
                            double ce = cd / b.CoreW;
                            double ca = b.CoreAlpha * lifeEnv * taper * gate * (1 - Smooth((ce - 0.5) / 0.5));
                            if (ca > 0.002)
                            {
                                double kr = Lerp(255, 255, cu);
                                double kg = Lerp(244, 251, cu);
                                double kb = Lerp(188, 227, cu);
                                Over(rgb, a, x, y, kr, kg, kb, ca);
                            }
                        }
                    }
                }
            }
        }

        static double NotchAt(double s, double pos, double speed, double len, double t, double loop)
        {
            double c = pos + speed * (t / loop);
            c = c - Math.Floor(c);                     // 0..1 循环
            double d = Math.Abs(s - c);
            d = Math.Min(d, 1 - d);
            if (d > len * 0.5) { return 0; }
            return Smooth(1 - d / (len * 0.5));
        }

        // 普通 alpha 合成（over）
        static void Over(double[] rgb, double[] a, int x, int y, double r, double g, double bl, double al)
        {
            int i = y * W + x;
            double outA = al + a[i] * (1 - al);
            if (outA <= 0) { return; }
            rgb[i * 3 + 0] = (r * al + rgb[i * 3 + 0] * a[i] * (1 - al)) / outA;
            rgb[i * 3 + 1] = (g * al + rgb[i * 3 + 1] * a[i] * (1 - al)) / outA;
            rgb[i * 3 + 2] = (bl * al + rgb[i * 3 + 2] * a[i] * (1 - al)) / outA;
            a[i] = outA;
        }

        // 火花：小亮点 + 短尾（先快后慢）
        static void DrawSpark(double[] rgb, double[] a, Spark s, double t, double loop)
        {
            double age = ((t - s.Birth) % loop + loop) % loop;
            if (age >= s.Life) { return; }
            double u = age / s.Life;
            double env = Math.Sin(Math.PI * u);
            double v = Lerp(0.40, 0.16, u) * H_PX * 0.55;     // px/秒（近似积分）
            double y = s.Y0 - v * age;
            double x = s.X0;
            double al = s.Alpha * env;
            int x0 = (int)Math.Max(0, x - 6), x1 = (int)Math.Min(W - 1, x + 6);
            int y0 = (int)Math.Max(0, y - s.Tail - 4), y1 = (int)Math.Min(H - 1, y + 4);
            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    double dx = px - x, dy = py - y;
                    double rv = dy > 0 ? (s.R + s.Tail) : s.R;
                    double d2 = (dx * dx) / (s.R * s.R) + (dy * dy) / (rv * rv);
                    if (d2 > 1) { continue; }
                    double al2 = al * (1 - Math.Sqrt(d2));
                    if (al2 <= 0.002) { continue; }
                    Over(rgb, a, px, py, 255, 231, 154, al2);   // #FFE79A
                }
            }
        }

        // 可分离箱式模糊（给外晕用）
        static double[] Blur(double[] src, int r)
        {
            double[] tmp = new double[src.Length];
            double[] dst = new double[src.Length];
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        double sum = 0; int cnt = 0;
                        for (int d = -r; d <= r; d++)
                        {
                            int xx = x + d;
                            if (xx < 0 || xx >= W) { continue; }
                            sum += src[(y * W + xx) * 3 + c]; cnt++;
                        }
                        tmp[(y * W + x) * 3 + c] = sum / cnt;
                    }
                }
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        double sum = 0; int cnt = 0;
                        for (int d = -r; d <= r; d++)
                        {
                            int yy = y + d;
                            if (yy < 0 || yy >= H) { continue; }
                            sum += tmp[(yy * W + x) * 3 + c]; cnt++;
                        }
                        dst[(y * W + x) * 3 + c] = sum / cnt;
                    }
                }
            }
            return dst;
        }

        // 对 alpha 通道做同样的可分离箱式模糊
        static double[] BlurA(double[] src, int r)
        {
            double[] tmp = new double[src.Length];
            double[] dst = new double[src.Length];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    double sum = 0; int cnt = 0;
                    for (int d = -r; d <= r; d++)
                    {
                        int xx = x + d;
                        if (xx < 0 || xx >= W) { continue; }
                        sum += src[y * W + xx]; cnt++;
                    }
                    tmp[y * W + x] = sum / cnt;
                }
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    double sum = 0; int cnt = 0;
                    for (int d = -r; d <= r; d++)
                    {
                        int yy = y + d;
                        if (yy < 0 || yy >= H) { continue; }
                        sum += tmp[yy * W + x]; cnt++;
                    }
                    dst[y * W + x] = sum / cnt;
                }
            }
            return dst;
        }

        /// <summary>
        /// 生成帧。haloNear/haloFar 是两级外晕强度（0 表示不画）；onlyFrame >= 0 时只出那一帧（做 A/B/C 对比用）。
        /// </summary>
        public static void Run(string outDir, int frames, double haloNear, double haloFar,
                               bool withCore, int seed, string prefix, int onlyFrame)
        {
            _rnd = new Random(seed);
            double loop = frames * FRAME_SEC;      // 循环周期永远按整圈算，出单帧也一样
            Directory.CreateDirectory(outDir);

            List<Band> bands = new List<Band>();
            int nPanel = 9, nMid = 18, nTongue = 12;
            for (int i = 0; i < nPanel; i++)
                bands.Add(MakeBand(0, (i * (loop / nPanel) + Rand(-0.06, 0.06) + loop) % loop, i));
            for (int i = 0; i < nMid; i++)
                bands.Add(MakeBand(1, (i * (loop / nMid) + Rand(-0.06, 0.06) + loop) % loop, i));
            for (int i = 0; i < nTongue; i++)
                bands.Add(MakeBand(2, (i * (loop / nTongue) + Rand(-0.06, 0.06) + loop) % loop, i));
            List<Spark> sparks = new List<Spark>();
            for (int i = 0; i < 20; i++)
                sparks.Add(MakeSpark((i * (loop / 20) + Rand(-0.06, 0.06) + loop) % loop));

            int first = onlyFrame >= 0 ? onlyFrame : 0;
            int last = onlyFrame >= 0 ? onlyFrame : frames - 1;

            for (int f = first; f <= last; f++)
            {
                double t = f * FRAME_SEC;
                double[] rgb = new double[W * H * 3];
                double[] al = new double[W * H];

                foreach (Band b in bands) { DrawBand(rgb, al, b, t, loop, withCore); }
                foreach (Spark s in sparks) { DrawSpark(rgb, al, s, t, loop); }

                // 外晕：只从主体轮廓派生，分近/远两级；主体本身保持清晰
                if (haloFar > 0)
                {
                    double[] hrgb = Blur(rgb, 8);
                    double[] hal = BlurA(al, 8);
                    CompositeUnder(rgb, al, hrgb, hal, haloFar, 255, 208, 90);   // #FFD05A
                }
                if (haloNear > 0)
                {
                    double[] hrgb = Blur(rgb, 3);
                    double[] hal = BlurA(al, 3);
                    CompositeUnder(rgb, al, hrgb, hal, haloNear, 255, 228, 129); // #FFE481
                }

                using (Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                {
                    BitmapData d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    byte[] buf = new byte[d.Stride * H];
                    for (int y = 0; y < H; y++)
                    {
                        int row = y * d.Stride;
                        for (int x = 0; x < W; x++)
                        {
                            int i = y * W + x, o = row + x * 4;
                            double a8 = Clamp(al[i], 0, 1);
                            buf[o + 3] = (byte)Math.Round(a8 * 255);
                            if (a8 <= 0.0001) { continue; }
                            buf[o + 0] = (byte)Math.Round(Clamp(rgb[i * 3 + 2], 0, 255));
                            buf[o + 1] = (byte)Math.Round(Clamp(rgb[i * 3 + 1], 0, 255));
                            buf[o + 2] = (byte)Math.Round(Clamp(rgb[i * 3 + 0], 0, 255));
                        }
                    }
                    System.Runtime.InteropServices.Marshal.Copy(buf, 0, d.Scan0, buf.Length);
                    bmp.UnlockBits(d);
                    bmp.Save(Path.Combine(outDir, prefix + "_" + (f + 1).ToString("D2") + ".png"), ImageFormat.Png);
                }
            }
        }

        // 把"染色的模糊层"垫在主体下面
        static void CompositeUnder(double[] rgb, double[] al, double[] hrgb, double[] hal,
                                   double strength, double tr, double tg, double tb)
        {
            for (int i = 0; i < W * H; i++)
            {
                double ha = hal[i] * strength;
                if (ha <= 0.002) { continue; }
                double sa = al[i];
                double outA = sa + ha * (1 - sa);
                if (outA <= 0) { continue; }
                for (int c = 0; c < 3; c++)
                {
                    double hv = hrgb[i * 3 + c];
                    double tint = c == 0 ? tr : (c == 1 ? tg : tb);
                    // 用主体颜色做主，外晕只提供一层淡淡的光
                    double baseCol = rgb[i * 3 + c];
                    double hcol = hv * 0.55 + tint * 0.45;
                    rgb[i * 3 + c] = (hcol * ha * (1 - sa) + baseCol * sa) / outA;
                }
                al[i] = outA;
            }
        }
    }
}
