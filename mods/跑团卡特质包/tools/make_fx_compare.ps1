# 拼一张"参考图 vs 我们的两个版本"的对比图，方便拿给别人看。
#
# 用法： powershell -File tools\make_fx_compare.ps1

param(
  [string]$Ref  = 'D:\HuaweiMoveData\Users\huawei\Desktop\钢丝\人格解体本地存档\resource\shine\0s.jpg',
  [string]$Out  = 'D:\HuaweiMoveData\Users\huawei\Desktop\钢丝\人格解体本地存档\mods\跑团卡特质包\tools\preview\compare.png',
  [int]$CellW = 380,
  [int]$CellH = 520
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$texDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'

$panels = @()
if (Test-Path -LiteralPath $Ref) { $panels += @{ Title = 'Limbus 参考图（原版截图）'; Path = $Ref } }
$g1 = Get-ChildItem -LiteralPath $texDir -File -Filter 'xin_glow_12.png' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($g1) { $panels += @{ Title = '当前程序化版（柔光柱+火花）'; Path = $g1.FullName } }
$g2 = Get-ChildItem -LiteralPath $texDir -File -Filter 'xin_shine_09.png' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($g2) { $panels += @{ Title = '视频抽帧版（上一版）'; Path = $g2.FullName } }
if ($panels.Count -eq 0) { throw '没有可用的对比图' }

$headH = 40
$w = $CellW * $panels.Count
$h = $CellH + $headH
$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 18, 18, 20))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$font = $null
try { $font = New-Object System.Drawing.Font('Microsoft YaHei', 13) } catch { $font = New-Object System.Drawing.Font('Arial', 12) }
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 230, 200))

$i = 0
foreach ($p in $panels) {
  $x = $i * $CellW
  $g.DrawString($p.Title, $font, $brush, [float]($x + 12), 10)
  $img = [System.Drawing.Image]::FromFile($p.Path)
  # 等比缩放塞进格子
  $k = [Math]::Min(($CellW - 20) / $img.Width, ($CellH - 20) / $img.Height)
  $dw = [int]($img.Width * $k); $dh = [int]($img.Height * $k)
  $dx = $x + [int](($CellW - $dw) / 2)
  $dy = $headH + [int](($CellH - $dh) / 2)
  $g.DrawImage($img, $dx, $dy, $dw, $dh)
  $img.Dispose()
  if ($i -gt 0) { $g.DrawLine((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 60, 60, 60))), $x, 0, $x, $h) }
  $i++
}
$g.Dispose()
$dir = Split-Path -Parent $Out
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "对比图：$Out"
