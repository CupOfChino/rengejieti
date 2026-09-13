# 生成「心」的金色火焰光效帧图（游戏按帧播放，配置见 Game\ExtraAnim\xin_flame.txt）。
#
# 素材：边狱巴士的火焰形状贴图 Fx_T_Shape_FireMoth_Fire.png（黑底白火）。
# 做法：把它的亮度当透明度、染成金色并糊开边缘，再让几簇火苗交错着往上飘，
#       底下再垫两层柔光当"光晕"，组成一段能无缝循环的动画。
# 为什么不直接用巴士的特效：《人格解体》加载不了别人的预制体+粒子系统，
#       但自带"按帧播图"的特效系统（CustomSpriteConfigData），所以走这条路。
#
# 用法： powershell -File tools\build_heart_flame.ps1

param(
  [int]$Size = 512,
  [int]$Frames = 16,
  [int]$Puffs = 9,
  [double]$Blur = 2.0,
  [string]$Source = 'E:\lim\_save\_raw\Assets\FXv2\V2_Texture\Shape\Fx_T_Shape_FireMoth_Fire\Fx_T_Shape_FireMoth_Fire.png'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$OutDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (-not (Test-Path -LiteralPath $Source)) { throw "找不到火焰贴图：$Source" }

# ---- 1) 黑底白火 → 金色 + 透明 ----
$src = [System.Drawing.Bitmap]::FromFile($Source)
$w = $src.Width; $h = $src.Height
$alpha = New-Object 'double[,]' $w, $h
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $c = $src.GetPixel($x, $y)
    $lum = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)) / 255.0
    if ($lum -le 0.02) { continue }
    # 提一下对比，让火苗轮廓更清楚
    $alpha[$x, $y] = [Math]::Pow($lum, 1.25)
  }
}
$src.Dispose()

# ---- 2) 把透明度糊开（半径 2 的两次模糊，足够把 128 的硬边化开）----
$tmp = New-Object 'double[,]' $w, $h
$r = [Math]::Max(1, [int][Math]::Round($Blur))
for ($pass = 0; $pass -lt 2; $pass++) {
  for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
      $sum = 0.0; $n = 0
      for ($dx = -$r; $dx -le $r; $dx++) {
        $xx = $x + $dx
        if ($xx -lt 0 -or $xx -ge $w) { continue }
        $sum += $alpha[$xx, $y]; $n++
      }
      $tmp[$x, $y] = $sum / $n
    }
  }
  for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
      $sum = 0.0; $n = 0
      for ($dy = -$r; $dy -le $r; $dy++) {
        $yy = $y + $dy
        if ($yy -lt 0 -or $yy -ge $h) { continue }
        $sum += $tmp[$x, $yy]; $n++
      }
      $alpha[$x, $y] = $sum / $n
    }
  }
}

# ---- 3) 生成火苗位图（金色，亮度只影响透明度，不再发灰）----
$flame = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $a = $alpha[$x, $y]
    if ($a -le 0.005) { continue }
    $flame.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(
      [int][Math]::Min(255, $a * 255),
      255,
      [int](206 + 30 * $a),
      [int](96 + 90 * $a)))
  }
}
Write-Output ("火苗素材已备好：{0}x{1}（已模糊半径 {2}）" -f $w, $h, $r)

# 画一团径向柔光（当光晕用）
function Draw-Glow($g, [System.Drawing.PointF]$center, [double]$radius, [int]$alpha) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddEllipse([float]($center.X - $radius), [float]($center.Y - $radius), [float]($radius * 2), [float]($radius * 2))
  $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush -ArgumentList $path
  $brush.CenterPoint = $center
  $brush.CenterColor = [System.Drawing.Color]::FromArgb([Math]::Min(255, $alpha), 255, 226, 150)
  $surround = New-Object 'System.Drawing.Color[]' 1
  $surround[0] = [System.Drawing.Color]::FromArgb(0, 255, 170, 40)
  $brush.SurroundColors = $surround
  $g.FillEllipse($brush, [float]($center.X - $radius), [float]($center.Y - $radius), [float]($radius * 2), [float]($radius * 2))
  $brush.Dispose()
  $path.Dispose()
}

# ---- 4) 逐帧：柔光 + 几簇火苗交错上飘，首尾相接 ----
$step = 1.0 / $Puffs
for ($i = 0; $i -lt $Frames; $i++) {
  $t = $i / [double]$Frames
  $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)

  # 屏幕上的呼吸
  $breath = [Math]::Sin($t * 2 * [Math]::PI)
  $gc = New-Object System.Drawing.PointF([float]($Size * 0.5), [float]($Size * 0.60))
  Draw-Glow $g $gc ($Size * 0.44) ([int](52 + 14 * $breath))
  Draw-Glow $g $gc ($Size * 0.26) ([int](80 + 20 * $breath))

  # 火苗
  for ($k = 0; $k -lt $Puffs; $k++) {
    $ph = ($t + $k * $step) % 1.0
    $ang = $ph * 2 * [Math]::PI + $k * 1.7
    $x = $Size * 0.5 + [Math]::Sin($ang) * $Size * 0.10 + (($k % 3) - 1) * $Size * 0.045
    $y = $Size * (1.08 - $ph * 1.22)
    $scale = 0.50 + 0.34 * [Math]::Sin($ph * [Math]::PI)
    $a = [Math]::Pow([Math]::Sin($ph * [Math]::PI), 1.4)
    $dw = $Size * $scale
    $imgAttr = New-Object System.Drawing.Imaging.ImageAttributes
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix33 = [float]$a
    $imgAttr.SetColorMatrix($cm)
    $g.TranslateTransform([float]$x, [float]$y)
    $g.RotateTransform([float](($k * 47 + $t * 90) % 360))
    $rect = New-Object System.Drawing.Rectangle([int](-$dw / 2), [int](-$dw / 2), [int]$dw, [int]$dw)
    $g.DrawImage($flame, $rect, 0, 0, $w, $h, [System.Drawing.GraphicsUnit]::Pixel, $imgAttr)
    $g.ResetTransform()
    $imgAttr.Dispose()
  }

  $g.Dispose()
  $name = 'xin_flame_{0:D2}.png' -f ($i + 1)
  $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

$flame.Dispose()
Write-Output "完成：$Frames 帧，输出目录 $OutDir"
