# 诊断用：把特效按"游戏里的实际尺寸"挂在替身小人身上，看亮带会不会被角色挡光。
#
# 尺寸关系（和生成器一致）：画布 512×512、AnimSize 0.302 → 约 1.55 世界单位；
# 角色高约 1.3 单位 = 画布上 429px，脚底在 y=470。
# 特效画在角色**下面**（游戏里就是这么设的）。
#
# 用法： powershell -File tools\make_fx_on_char.ps1

param(
  [string]$StandIn = 'E:\lim\_save\素材库\01_YiSang\SD\ErosionAppearance_2010221\idle.png',
  [string]$OutFile = '',
  [int[]]$Frames = @(6, 18, 30, 42)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$TexDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot 'preview\onchar.png' }
if (-not (Test-Path -LiteralPath $StandIn)) { throw "找不到替身小人：$StandIn" }

$H_PX = 429.0                 # 角色在画布上的高度
$FEET = 470.0                 # 脚底 y

$cell = 512
$cols = 2
$rows = [Math]::Ceiling($Frames.Count / [double]$cols)
$bmp = New-Object System.Drawing.Bitmap(($cell * $cols), ($cell * $rows), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 16, 16, 18))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$char = [System.Drawing.Image]::FromFile($StandIn)
$chH = [int]$H_PX
$chW = [int]($char.Width * ($H_PX / $char.Height))

$i = 0
foreach ($f in $Frames) {
  $fxPath = Join-Path $TexDir ('xin_glow_{0:D2}.png' -f $f)
  if (-not (Test-Path -LiteralPath $fxPath)) { continue }
  $cx = ($i % $cols) * $cell
  $cy = [Math]::Floor($i / $cols) * $cell
  $fx = [System.Drawing.Image]::FromFile($fxPath)
  $g.DrawImage($fx, $cx, $cy, $cell, $cell)                     # 1) 特效
  $g.DrawImage($char, ($cx + ($cell - $chW) / 2), ($cy + $FEET - $chH), $chW, $chH)  # 2) 角色压在上面
  $fx.Dispose()
  $i++
}
$char.Dispose()
$g.Dispose()
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "挂角色诊断图：$OutFile"
