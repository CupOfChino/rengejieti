# 把「灰底的金色光效图」导入成游戏能播的帧图（自定义心的【心】气焰）。
#
# 和旧的 import_heart_frames.ps1（黑底版）的区别：这批素材是**灰底**（R=G=B≈119），
# 不能直接拿亮度当透明度——那样整片灰都会留下半透明灰雾。
# 这版改成：先把底色减掉，再换算成透明度；颜色保持原样（只按最亮通道归一化），
# 这样火焰的暖黄色调不会被改黑、也不会带灰边。
#
# 做四件事：
#   1) 减底色 → 透明（底色自动从四角采样）
#   2) 按"所有帧的公共范围"裁剪，保证播放时不抖
#   3) 用亮度重心把火焰摆到画面正中，再统一缩放到目标画布
#   4) 底部补透明行（沿用旧版 122px，让特效相对角色上移），输出 PNG
#
# 用法：
#   powershell -File tools\import_xin_glow_frames.ps1
#   powershell -File tools\import_xin_glow_frames.ps1 -Src "..\..\..\resource\shin_v2"
#   powershell -File tools\import_xin_glow_frames.ps1 -OffsetY -20    # 画面往上挪，微调高度
#
# 逐像素处理放在内联 C# 里做（PowerShell 直接 GetPixel/SetPixel 慢到不可接受）。

param(
  [string]$Src = '',
  [string]$Prefix = 'xin_glow',
  [int]$OutWidth = 516,          # 画布宽（沿用旧版，编辑器里调好的 AnimSize 才不会失效）
  [int]$OutHeight = 638,         # 画布高
  [int]$PadBottom = 122,         # 底部留白：把画面往上顶，沿用旧版数值
  [double]$Bg = 0,               # 底色灰度 0~1；填 0 表示自动从四角采样
  [double]$Gamma = 1.0,
  [double]$Cut = 0.05,           # 低于这个透明度的直接扔（去 JPEG 噪点）
  [double]$BoundsCut = 0.12,     # 算公共取景框时，认为"有内容"的阈值
  [double]$Margin = 0.08,        # 取景框比内容多留的比例
  [double]$OffsetX = 0,
  [double]$OffsetY = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot  = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ModRoot)
if (-not $Src) { $Src = Join-Path $RepoRoot 'resource\shin_v2' }
$OutDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'

if (-not (Test-Path -LiteralPath $Src)) { throw "找不到素材目录：$Src" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$code = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class GlowTool
{
    // 底色（0~1）与"能提亮多少"的跨度
    static double Span(double bg) { double s = 1.0 - bg; return s < 0.02 ? 0.02 : s; }

    // 读图，算出每像素透明度：先把底色减掉，再归一化
    static double[] ReadAlpha(string path, double bg, double gamma, out int w, out int h)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            w = bmp.Width; h = bmp.Height;
            double[] a = new double[w * h];
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = d.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(d);
            double span = Span(bg);
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    int b = buf[i], g = buf[i + 1], r = buf[i + 2];
                    int mx = Math.Max(r, Math.Max(g, b));
                    double e = mx / 255.0 - bg;
                    if (e <= 0) { continue; }
                    double v = e / span; if (v > 1) { v = 1; }
                    a[y * w + x] = Math.Pow(v, gamma);
                }
            }
            return a;
        }
    }

    public static double[] Alpha(string path, double bg, double gamma, out int w, out int h)
    { return ReadAlpha(path, bg, gamma, out w, out h); }

    // 算"亮"的范围，用来取所有帧的公共取景框
    public static int[] Bounds(string path, double bg, double gamma, double cut)
    {
        int w, h;
        double[] a = ReadAlpha(path, bg, gamma, out w, out h);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (a[row + x] < cut) { continue; }
                if (x < x0) { x0 = x; }
                if (x > x1) { x1 = x; }
                if (y < y0) { y0 = y; }
                if (y > y1) { y1 = y; }
            }
        }
        if (x1 < 0) { return null; }
        return new int[] { x0, y0, x1, y1 };
    }

    // 亮度重心：用它把火焰对准画面正中（远处的小光点不会把重心带偏）
    public static double[] Centroid(string path, double bg, double gamma, double cut)
    {
        int w, h;
        double[] a = ReadAlpha(path, bg, gamma, out w, out h);
        double sw = 0, sx = 0, sy = 0;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                double v = a[row + x];
                if (v < cut) { continue; }
                sw += v; sx += v * x; sy += v * y;
            }
        }
        if (sw <= 0) { return null; }
        return new double[] { sx / sw, sy / sw };
    }

    // 四角采样估底色
    public static double BackgroundLevel(string path, int probe)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            int[] xs = { probe, w - 1 - probe };
            int[] ys = { probe, h - 1 - probe };
            double sum = 0; int n = 0;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    Color c = bmp.GetPixel(xs[i], ys[j]);
                    sum += (c.R + c.G + c.B) / 3.0 / 255.0; n++;
                }
            return sum / n;
        }
    }

    // 抠图 → 裁剪 → 缩放 → 底部留白，一次出来
    public static void Render(string path, string outPath, double bg, double gamma, double cut,
                              int cropX, int cropY, int cropW, int cropH,
                              int outW, int outH, int padBottom)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = d.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(d);

            using (Bitmap crop = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb))
            {
                BitmapData cd = crop.LockBits(new Rectangle(0, 0, cropW, cropH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int cs = cd.Stride;
                byte[] cb = new byte[cs * cropH];
                double span = Span(bg);

                for (int y = 0; y < cropH; y++)
                {
                    int sy = y + cropY;
                    if (sy < 0 || sy >= h) { continue; }
                    int row = sy * stride;
                    int crow = y * cs;
                    for (int x = 0; x < cropW; x++)
                    {
                        int sx = x + cropX;
                        if (sx < 0 || sx >= w) { continue; }
                        int i = row + sx * 4;
                        int b = buf[i], g = buf[i + 1], r = buf[i + 2];
                        int mx = Math.Max(r, Math.Max(g, b));
                        double e = mx / 255.0 - bg;
                        if (e <= 0) { continue; }
                        double v = e / span; if (v > 1) { v = 1; }
                        double al = Math.Pow(v, gamma);
                        if (al < cut) { continue; }
                        // 颜色：保持原色调，只按最亮通道归一化（去掉灰底带来的发灰）
                        double k = 255.0 / mx;
                        int o = crow + x * 4;
                        int rr = (int)Math.Round(r * k); if (rr > 255) { rr = 255; }
                        int gg = (int)Math.Round(g * k); if (gg > 255) { gg = 255; }
                        int bb = (int)Math.Round(b * k); if (bb > 255) { bb = 255; }
                        cb[o] = (byte)bb; cb[o + 1] = (byte)gg; cb[o + 2] = (byte)rr;
                        cb[o + 3] = (byte)Math.Min(255, (int)Math.Round(al * 255));
                    }
                }
                Marshal.Copy(cb, 0, cd.Scan0, cb.Length);
                crop.UnlockBits(cd);

                int square = outH - padBottom;   // 内容画成正方形，贴在画布上方
                if (square <= 0) { square = outH; }
                using (Bitmap outBmp = new Bitmap(outW, outH, PixelFormat.Format32bppArgb))
                using (Graphics gr = Graphics.FromImage(outBmp))
                {
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gr.CompositingMode = CompositingMode.SourceCopy;
                    gr.DrawImage(crop, new Rectangle(0, 0, outW, square));
                    outBmp.Save(outPath, ImageFormat.Png);
                }
            }
        }
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
Write-Output '处理模块已编译'

# ---- 收集素材（按文件名里的数字排序，不然 10 会排在 1 前面）----
function Get-FrameIndex([string]$name) {
  $m = [regex]::Match($name, '(\d+)')
  if ($m.Success) { return [int]$m.Groups[1].Value }
  return 9999
}
$files = Get-ChildItem -LiteralPath $Src -File |
  Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|bmp)$' } |
  Sort-Object { Get-FrameIndex $_.Name }, Name
if ($files.Count -eq 0) { throw "目录里没有图片：$Src" }
Write-Output ("素材 {0} 张：{1}" -f $files.Count, (($files | ForEach-Object { $_.Name }) -join ', '))

# ---- 底色 ----
if ($Bg -le 0) {
  $Bg = [GlowTool]::BackgroundLevel($files[0].FullName, 5)
  Write-Output ("底色自动采样：{0:N3}（灰阶约 {1}）" -f $Bg, [int][Math]::Round($Bg * 255))
} else {
  Write-Output ("底色指定：{0:N3}" -f $Bg)
}

# ---- 公共取景框 ----
$img0 = [System.Drawing.Image]::FromFile($files[0].FullName)
$W = $img0.Width; $H = $img0.Height; $img0.Dispose()
$x0 = $W; $y0 = $H; $x1 = -1; $y1 = -1
foreach ($f in $files) {
  $b = [GlowTool]::Bounds($f.FullName, $Bg, $Gamma, $BoundsCut)
  if ($b -eq $null) { continue }
  if ($b[0] -lt $x0) { $x0 = $b[0] }
  if ($b[1] -lt $y0) { $y0 = $b[1] }
  if ($b[2] -gt $x1) { $x1 = $b[2] }
  if ($b[3] -gt $y1) { $y1 = $b[3] }
}
if ($x1 -lt 0) { throw '所有帧都是空的，检查底色/阈值' }
Write-Output ("公共范围：x {0}~{1}  y {2}~{3}（原图 {4}x{5}）" -f $x0, $x1, $y0, $y1, $W, $H)

$sumX = 0.0; $sumY = 0.0; $cnt = 0
foreach ($f in $files) {
  $c = [GlowTool]::Centroid($f.FullName, $Bg, $Gamma, $BoundsCut)
  if ($c -eq $null) { continue }
  $sumX += $c[0]; $sumY += $c[1]; $cnt++
}
if ($cnt -eq 0) { throw '算不出重心' }
$avgX = $sumX / $cnt; $avgY = $sumY / $cnt
Write-Output ("平均重心：x={0:N0} y={1:N0}（原图中心 x={2} y={3}）" -f $avgX, $avgY, ($W / 2), ($H / 2))

$side = [int][Math]::Ceiling([Math]::Max($x1 - $x0 + 1, $y1 - $y0 + 1) * (1 + $Margin))
$cx = [int][Math]::Round($avgX - $side / 2.0 - $OffsetX)
$cy = [int][Math]::Round($avgY - $side / 2.0 - $OffsetY)
# 取景框边长：内容最长的一边 + 两侧留白
$span = [Math]::Max($x1 - $x0 + 1, $y1 - $y0 + 1)
$m = [int][Math]::Max(6, [Math]::Round($span * $Margin / 2.0))
$side = [int][Math]::Ceiling($span + 2 * $m)
$cx = [int][Math]::Round($avgX - $side / 2.0 - $OffsetX)
$cy = [int][Math]::Round($avgY - $side / 2.0 - $OffsetY)
# 取景框可以超出原图（超出的部分输出成透明），但必须罩住"内容 + 四周留白"：
#   上界 = 内容上边再往上留 $m；下界 = 内容下边加留白往回推一个边长
# 在满足这两条的前提下，取最接近"重心居中"的位置。
if ($cx -gt ($x0 - $m)) { $cx = $x0 - $m }
if ($cx -lt ($x1 + $m - $side + 1)) { $cx = $x1 + $m - $side + 1 }
if ($cy -gt ($y0 - $m)) { $cy = $y0 - $m }
if ($cy -lt ($y1 + $m - $side + 1)) { $cy = $y1 + $m - $side + 1 }
Write-Output ("取景框：{0}x{0} @ ({1},{2})  → 输出 {3}x{4}（底部留白 {5}）" -f $side, $cx, $cy, $OutWidth, $OutHeight, $PadBottom)

# ---- 逐张输出 ----
for ($i = 0; $i -lt $files.Count; $i++) {
  $out = Join-Path $OutDir ('{0}_{1:D2}.png' -f $Prefix, ($i + 1))
  [GlowTool]::Render($files[$i].FullName, $out, $Bg, $Gamma, $Cut, $cx, $cy, $side, $side, $OutWidth, $OutHeight, $PadBottom)
}
$made = Get-ChildItem -LiteralPath $OutDir -File -Filter "$Prefix`_*.png"
$bytes = ($made | Measure-Object Length -Sum).Sum
Write-Output ("导出 {0} 张 → {1}（合计 {2} KB）" -f $made.Count, $OutDir, [Math]::Round($bytes / 1KB))
Write-Output '记得：张数变了要同步改 InternalConfigure.txt（1023 登记）和 ExtraAnim 配置里的帧列表。'
