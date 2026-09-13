# 把「黑底的金色光效图」导入成游戏能播的帧图。
#
# 做三件事：
#   1) 黑底 → 透明（按亮度当透明度，颜色保留，所以金光还是金色）
#   2) 按"所有帧的公共范围"裁剪，保证播放时画面不抖（不会每帧各自裁紧）
#   3) 输出成 PNG 到 mod 的 ExtraAnim 贴图目录，并把该用的 AnimSize 算给你看
#
# 用法：
#   powershell -File tools\import_heart_frames.ps1 -Src "..\..\..\resource\shine" -Prefix xin_shine
#
# 说明：逐像素处理放在内联 C# 里做，PowerShell 直接 GetPixel/SetPixel 会慢到不可接受。

param(
  [Parameter(Mandatory = $true)][string]$Src,
  [string]$Prefix = 'xin_shine',
  [double]$Gamma = 1.0,
  [double]$Cut = 0.05,
  [double]$BoundsCut = 0.18,
  [double]$Margin = 0.06,
  [double]$TargetUnits = 0,     # 想要的高度（世界单位）；填 0 就只算不写
  [int]$BlendFrames = 0,        # 首尾之间插几帧过渡（做无缝循环用）
  [double]$OffsetX = 0,         # 画面往右挪几像素（用来微调"正好在身体中间"）
  [double]$OffsetY = 0,         # 画面往下挪几像素
  [switch]$NoCrop
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$OutDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (-not (Test-Path -LiteralPath $Src)) { throw "找不到素材目录：$Src" }

$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class FrameTool
{
    // 读进来，按亮度算出 alpha，返回 {alpha[], width, height}
    public static double[] Alpha(string path, double gamma, out int w, out int h)
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
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    int b = buf[i], g = buf[i + 1], r = buf[i + 2];
                    int max = Math.Max(r, Math.Max(g, b));
                    if (max <= 8) { continue; }
                    double v = max / 255.0;
                    a[y * w + x] = Math.Pow(v, gamma);
                }
            }
            return a;
        }
    }

    // 亮度加权重心：用它来把火焰对准画面正中心
    public static double[] Centroid(string path, double gamma, double cut)
    {
        int w, h;
        double[] a = Alpha(path, gamma, out w, out h);
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

    // 找这一帧里"亮"的范围，用来算所有帧的公共取景框
    public static int[] Bounds(string path, double gamma, double cut)
    {
        int w, h;
        double[] a = Alpha(path, gamma, out w, out h);
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

    // 两张图按权重线性混合，用来插首尾过渡帧
    public static void Blend(string pathA, string pathB, string outPath, double wA)
    {
        using (Bitmap a = new Bitmap(pathA))
        using (Bitmap b = new Bitmap(pathB))
        {
            int w = a.Width, h = a.Height;
            double wB = 1.0 - wA;
            using (Bitmap outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData ad = a.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData od = outBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                byte[] abuf = new byte[ad.Stride * h]; Marshal.Copy(ad.Scan0, abuf, 0, abuf.Length);
                byte[] bbuf = new byte[bd.Stride * h]; Marshal.Copy(bd.Scan0, bbuf, 0, bbuf.Length);
                byte[] obuf = new byte[od.Stride * h];
                for (int y = 0; y < h; y++)
                {
                    int ao = y * ad.Stride, bo = y * bd.Stride, oo = y * od.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        int ai = ao + x * 4, bi = bo + x * 4, oi = oo + x * 4;
                        for (int c = 0; c < 4; c++)
                        {
                            int v = (int)Math.Round(abuf[ai + c] * wA + bbuf[bi + c] * wB);
                            obuf[oi + c] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
                        }
                    }
                }
                Marshal.Copy(obuf, 0, od.Scan0, obuf.Length);
                a.UnlockBits(ad); b.UnlockBits(bd); outBmp.UnlockBits(od);
                outBmp.Save(outPath, ImageFormat.Png);
            }
        }
    }

    // 按算好的 alpha 导出 PNG（可裁）
    public static void Save(string path, string outPath, double gamma, double cut,
                            int cropX, int cropY, int cropW, int cropH)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = d.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(d);

            using (Bitmap outBmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb))
            {
                BitmapData od = outBmp.LockBits(new Rectangle(0, 0, cropW, cropH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int ostride = od.Stride;
                byte[] obuf = new byte[ostride * cropH];
                for (int y = 0; y < cropH; y++)
                {
                    int sy = y + cropY;
                    int orow = y * ostride;
                    if (sy < 0 || sy >= h) { continue; }
                    int row = sy * stride;
                    for (int x = 0; x < cropW; x++)
                    {
                        int sx = x + cropX;
                        if (sx < 0 || sx >= w) { continue; }
                        int i = row + sx * 4;
                        int b = buf[i], g = buf[i + 1], r = buf[i + 2];
                        int max = Math.Max(r, Math.Max(g, b));
                        double v = max / 255.0;
                        double al = Math.Pow(v, gamma);
                        if (al < cut) { continue; }
                        int o = orow + x * 4;
                        obuf[o] = (byte)b;
                        obuf[o + 1] = (byte)g;
                        obuf[o + 2] = (byte)r;
                        obuf[o + 3] = (byte)Math.Min(255, (int)(al * 255));
                    }
                }
                Marshal.Copy(obuf, 0, od.Scan0, obuf.Length);
                outBmp.UnlockBits(od);
                outBmp.Save(outPath, ImageFormat.Png);
            }
        }
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
Write-Output '处理模块已编译'

# ---- 收集素材：按文件名里的数字排序（不然 10 会排在 1 前面）----
function Get-FrameIndex([string]$name) {
  $m = [regex]::Match($name, '(\d+)')
  if ($m.Success) { return [int]$m.Groups[1].Value }
  return 9999
}
$files = Get-ChildItem -LiteralPath $Src -File |
  Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|bmp)$' } |
  Sort-Object { Get-FrameIndex $_.Name }, Name
if ($files.Count -eq 0) { throw "目录里没有图片：$Src" }
Write-Output ("素材 {0} 张，顺序：{1}" -f $files.Count, (($files | ForEach-Object { $_.Name }) -join ', '))

# ---- 算所有帧的公共取景框 ----
$img0 = [System.Drawing.Image]::FromFile($files[0].FullName)
$W = $img0.Width; $H = $img0.Height; $img0.Dispose()
$x0 = $W; $y0 = $H; $x1 = -1; $y1 = -1
foreach ($f in $files) {
  $b = [FrameTool]::Bounds($f.FullName, $Gamma, $BoundsCut)
  if ($b -eq $null) { continue }
  if ($b[0] -lt $x0) { $x0 = $b[0] }
  if ($b[1] -lt $y0) { $y0 = $b[1] }
  if ($b[2] -gt $x1) { $x1 = $b[2] }
  if ($b[3] -gt $y1) { $y1 = $b[3] }
}
Write-Output ("公共范围：x {0}~{1}  y {2}~{3}（原图 {4}x{5}）" -f $x0, $x1, $y0, $y1, $W, $H)

if ($NoCrop) { $cx = 0; $cy = 0; $cw = $W; $ch = $H }
else {
  # 用"亮度重心"当中心：这样火焰本体正好落在画面正中（远处的光点不会把它带偏）
  $sumX = 0.0; $sumY = 0.0; $cnt = 0
  foreach ($f in $files) {
    $c = [FrameTool]::Centroid($f.FullName, $Gamma, $BoundsCut)
    if ($c -eq $null) { continue }
    $sumX += $c[0]; $sumY += $c[1]; $cnt++
  }
  if ($cnt -eq 0) { throw '算不出重心，检查素材是不是全黑' }
  $avgX = $sumX / $cnt; $avgY = $sumY / $cnt
  Write-Output ("所有帧的平均重心：x={0:N1}  y={1:N1}（原图中心 x={2} y={3}）" -f $avgX, $avgY, ($W / 2), ($H / 2))

  # 取正方形取景框：边长够放下所有内容，再留一点余量
  $side = [int][Math]::Ceiling([Math]::Max($x1 - $x0 + 1, $y1 - $y0 + 1) * (1 + $Margin))
  $cx = [int][Math]::Round($avgX - $side / 2.0 - $OffsetX)
  $cy = [int][Math]::Round($avgY - $side / 2.0 - $OffsetY)
  if ($cx -lt 0) { $cx = 0 }
  if ($cy -lt 0) { $cy = 0 }
  if ($cx + $side -gt $W) { $cx = [Math]::Max(0, $W - $side) }
  if ($cy + $side -gt $H) { $cy = [Math]::Max(0, $H - $side) }
  $cw = $side; $ch = $side
}
Write-Output ("导出尺寸：{0}x{1}" -f $cw, $ch)

# ---- 逐张导出 ----
$made = 0
for ($i = 0; $i -lt $files.Count; $i++) {
  $out = Join-Path $OutDir ('{0}_{1:D2}.png' -f $Prefix, ($i + 1))
  [FrameTool]::Save($files[$i].FullName, $out, $Gamma, $Cut, $cx, $cy, $cw, $ch)
  $made++
}
$bytes = (Get-ChildItem -LiteralPath $OutDir -File -Filter "$Prefix*.png" | Measure-Object Length -Sum).Sum
Write-Output ("导出 {0} 张 → {1}（合计 {2} KB）" -f $made, $OutDir, [Math]::Round($bytes / 1KB))

# ---- 首尾过渡帧：把最后一帧和第一帧之间补几帧，循环就不用"跳" ----
if ($BlendFrames -gt 0) {
  $last = Join-Path $OutDir ('{0}_{1:D2}.png' -f $Prefix, $made)
  $first = Join-Path $OutDir ('{0}_01.png' -f $Prefix)
  for ($k = 1; $k -le $BlendFrames; $k++) {
    # 从"几乎是最后一帧"平滑过渡到"几乎是第一帧"
    $w = 1.0 - ($k / [double]($BlendFrames + 1))
    $out = Join-Path $OutDir ('{0}_{1:D2}.png' -f $Prefix, ($made + $k))
    [FrameTool]::Blend($last, $first, $out, $w)
  }
  Write-Output ("已插入 {0} 张首尾过渡帧，共 {1} 帧" -f $BlendFrames, ($made + $BlendFrames))
}

# ---- 大小换算 ----
# 参考类型 1023 的消息里，图片按 100 像素 = 1 世界单位 生成 Sprite（见游戏代码 Sprite.Create 默认分支）
$ppu = 100.0
$unitsTall = $ch / $ppu
Write-Output ("当前画面高度 = {0:N2} 世界单位（{1}px ÷ {2} 像素每单位）" -f $unitsTall, $ch, $ppu)
if ($TargetUnits -gt 0) {
  Write-Output ("想让它高 {0} 单位 → ExtraAnim 里的 AnimSize 填 {1:N3}" -f $TargetUnits, ($TargetUnits / $unitsTall))
} else {
  Write-Output '想定大小就再加一个 -TargetUnits（比如角色 2 单位 × 1.2 = 2.4），我把 AnimSize 算出来'
}
