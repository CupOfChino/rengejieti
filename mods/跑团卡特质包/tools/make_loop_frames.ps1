# 从抽好的帧序列里找"真正的循环周期"，再挑出一组能首尾接上的帧。
#
# 为什么需要：视频里可能循环了好几次，直接从头切一段就会在接缝处"跳一下"。
# 这里用自相关（把每一帧和它后面某一帧比相似度）找出周期，再按周期均匀取帧。
#
# 用法：
#   powershell -File tools\make_loop_frames.ps1 -Src temp\shine30 -OutDir temp\shine_loop -OutFrames 16

param(
  [Parameter(Mandatory = $true)][string]$Src,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [int]$OutFrames = 16,
  [double]$Fps = 30,
  [double]$MinPeriodSec = 1.0,
  [double]$MaxPeriodSec = 4.5,
  [double]$Gamma = 1.7,
  [double]$Cut = 0.30
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class LoopTool
{
    // 亮度当透明度（和导入脚本同一套算法），返回 float[] 便于比较
    public static float[] Alpha(string path, double gamma, int step)
    {
        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            int sw = (w + step - 1) / step, sh = (h + step - 1) / step;
            float[] a = new float[sw * sh];
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
                    int mx = Math.Max(buf[i + 2], Math.Max(buf[i + 1], buf[i]));
                    if (mx <= 8) { continue; }
                    a[(y / step) * sw + (x / step)] = (float)Math.Pow(mx / 255.0, gamma);
                }
            }
            return a;
        }
    }

    // 两张图的平均差异（0 = 一模一样）
    public static double Diff(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) { return 1.0; }
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d < 0 ? -d : d;
        }
        return sum / a.Length;
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$files = Get-ChildItem -LiteralPath $Src -File | Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|bmp)$' } | Sort-Object Name
if ($files.Count -lt 8) { throw "帧太少：$($files.Count)" }
Write-Output ("读入 {0} 帧，开始算缓存…" -f $files.Count)

$alphas = New-Object 'System.Collections.Generic.List[object]'
foreach ($f in $files) { $alphas.Add([LoopTool]::Alpha($f.FullName, $Gamma, 4)) }
Write-Output '缓存完成（对比用 1/4 采样，省内存）'

# ---- 找周期：拿每一帧和它后面第 p 帧比，最像的那个 p 就是循环长度 ----
$minP = [int][Math]::Max(2, $MinPeriodSec * $Fps)
$maxP = [int][Math]::Min($MaxPeriodSec * $Fps, $files.Count - 1)
$rows = New-Object System.Collections.Generic.List[object]
$bestP = -1; $bestScore = [double]::MaxValue
for ($p = $minP; $p -le $maxP; $p++) {
  $sum = 0.0; $n = 0
  for ($o = 0; $o -lt 5; $o++) {
    $i = [int][Math]::Round(($files.Count - $p - 1) * $o / 4.0)
    if ($i -lt 0) { continue }
    $sum += [LoopTool]::Diff($alphas[$i], $alphas[$i + $p])
    $n++
  }
  if ($n -eq 0) { continue }
  $score = $sum / $n
  $rows.Add([pscustomobject]@{ 周期帧数 = $p; 秒 = [Math]::Round($p / $Fps, 2); 差异 = [Math]::Round($score, 5) })
  if ($score -lt $bestScore) { $bestScore = $score; $bestP = $p }
}
Write-Output '=== 最像的几组周期 ==='
$rows | Sort-Object 差异 | Select-Object -First 8 | Format-Table -AutoSize | Out-String -Width 100 | Write-Output
if ($bestP -lt 0) { throw '没找到可行周期' }
Write-Output ("选定周期：{0} 帧（{1:N2} 秒）" -f $bestP, ($bestP / $Fps))

# ---- 按这个周期均匀取 OutFrames 帧（不取最后一个端点，避免和第一帧重复）----
Get-ChildItem -LiteralPath $OutDir -File -ErrorAction SilentlyContinue | Remove-Item -Force
for ($i = 0; $i -lt $OutFrames; $i++) {
  $idx = [int][Math]::Round($i * $bestP / [double]$OutFrames)
  if ($idx -ge $files.Count) { $idx = $files.Count - 1 }
  $dst = Join-Path $OutDir ('{0:D2}{1}' -f $i, $files[$idx].Extension)
  Copy-Item -LiteralPath $files[$idx].FullName -Destination $dst -Force
}
$perFrame = ($bestP / $Fps) / $OutFrames
Write-Output ("输出 {0} 帧到 {1}" -f $OutFrames, $OutDir)
Write-Output ("每帧间隔应该用 {0:N3} 秒（= {1:N2} 秒 ÷ {2} 帧）" -f $perFrame, ($bestP / $Fps), $OutFrames)
