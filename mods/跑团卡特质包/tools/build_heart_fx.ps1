# 生成「心」的金光特效帧图（游戏用帧动画播它们，见 Game\ExtraAnim\xin_aura.txt）。
#
# 为什么自己做图：边狱巴士那边的金光是一整套预制体特效，格式和引用方式都不通用，
# 直接搬进《人格解体》加载不了；游戏自己支持"按帧播图片"的特效（CustomSpriteConfigData），
# 所以这里把金光画成一圈循环的帧，走那条路。
#
# 用法： powershell -File tools\build_heart_fx.ps1
#
# 输出： Project_Depersonal\Assets\Resources\Texture\ExtraAnim\xin_aura_01.png ... 12

param(
  [int]$Size = 512,
  [int]$Frames = 12,
  [string]$FrameSeconds = '0.08'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$OutDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 画一条从中心发散的锥形光束（根部亮、尖部淡）
function Draw-Ray($g, [System.Drawing.PointF]$center, [double]$angleDeg, [double]$len, [double]$halfWidthDeg, [int]$alpha) {
  $a0 = ($angleDeg - $halfWidthDeg) * [Math]::PI / 180.0
  $a1 = ($angleDeg + $halfWidthDeg) * [Math]::PI / 180.0
  $p0 = New-Object System.Drawing.PointF($center.X, $center.Y)
  $p1 = New-Object System.Drawing.PointF(($center.X + $len * [Math]::Cos($a0)), ($center.Y + $len * [Math]::Sin($a0)))
  $p2 = New-Object System.Drawing.PointF(($center.X + $len * [Math]::Cos($a1)), ($center.Y + $len * [Math]::Sin($a1)))
  $pts = New-Object 'System.Drawing.PointF[]' 3
  $pts[0] = $p0; $pts[1] = $p1; $pts[2] = $p2
  $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush -ArgumentList (, $pts)
  $brush.CenterPoint = $p0
  $brush.CenterColor = [System.Drawing.Color]::FromArgb([Math]::Min(255, $alpha), 255, 224, 130)
  $surround = New-Object 'System.Drawing.Color[]' 3
  $surround[0] = [System.Drawing.Color]::FromArgb(0, 255, 190, 60)
  $surround[1] = [System.Drawing.Color]::FromArgb(0, 255, 186, 55)
  $surround[2] = [System.Drawing.Color]::FromArgb(0, 255, 186, 55)
  $brush.SurroundColors = $surround
  $g.FillPolygon($brush, $pts)
  $brush.Dispose()
}

# 画中心那团柔和的金色光晕
function Draw-Glow($g, [System.Drawing.PointF]$center, [double]$radius, [int]$alpha) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddEllipse(($center.X - $radius), ($center.Y - $radius), ($radius * 2), ($radius * 2))
  $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush -ArgumentList $path
  $brush.CenterPoint = $center
  $brush.CenterColor = [System.Drawing.Color]::FromArgb([Math]::Min(255, $alpha), 255, 248, 205)
  $surround = New-Object 'System.Drawing.Color[]' 1
  $surround[0] = [System.Drawing.Color]::FromArgb(0, 255, 165, 30)
  $brush.SurroundColors = $surround
  $g.FillEllipse($brush, [float]($center.X - $radius), [float]($center.Y - $radius), [float]($radius * 2), [float]($radius * 2))
  $brush.Dispose()
  $path.Dispose()
}

# ---- 逐帧画 ----
$rnd = New-Object System.Random(20260913)
for ($i = 0; $i -lt $Frames; $i++) {
  $t = $i / [double]$Frames
  $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  $center = New-Object System.Drawing.PointF([float]($Size / 2.0), [float]($Size / 2.0))

  # 呼吸：光晕的大小和亮度来回起伏；周期正好一圈，所以首尾接得上
  $breath = [Math]::Sin($t * 2 * [Math]::PI)
  $radius = $Size * (0.30 + 0.035 * $breath)
  $alpha = [int](205 + 45 * $breath)

  # 外面那圈更大更淡，里面那圈是主体
  Draw-Glow $g $center ($radius * 1.5) ([int]($alpha * 0.45))
  Draw-Glow $g $center $radius $alpha

  # 光束：两层反向转，形成交错的星芒（每圈正好转满 360 度）
  $rayLen = $Size * 0.47
  for ($k = 0; $k -lt 14; $k++) {
    $ang = $t * 360.0 + ($k * (360.0 / 14.0))
    Draw-Ray $g $center $ang $rayLen 3.2 ([int](110 + 30 * $breath))
  }
  for ($k = 0; $k -lt 9; $k++) {
    $ang = -$t * 360.0 + ($k * (360.0 / 9.0)) + 12.0
    Draw-Ray $g $center $ang ($rayLen * 0.72) 2.0 ([int](75 + 20 * $breath))
  }

  # 几点飘动的亮斑，让它不那么规整
  for ($s = 0; $s -lt 18; $s++) {
    $sa = $rnd.NextDouble() * 2 * [Math]::PI
    $sr = $Size * (0.16 + 0.26 * $rnd.NextDouble())
    $sx = $center.X + $sr * [Math]::Cos($sa)
    $sy = $center.Y + $sr * [Math]::Sin($sa)
    $sz = 3.0 + 4.0 * $rnd.NextDouble()
    $br = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 248, 210))
    $g.FillEllipse($br, [float]($sx - $sz), [float]($sy - $sz), [float]($sz * 2), [float]($sz * 2))
    $br.Dispose()
  }

  $g.Dispose()
  $name = 'xin_aura_{0:D2}.png' -f ($i + 1)
  $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output ("已生成 $name （{0} 字节）" -f (Get-Item -LiteralPath (Join-Path $OutDir $name)).Length)
}

Write-Output "完成：$Frames 帧，目录 $OutDir"
