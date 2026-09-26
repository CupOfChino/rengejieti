# 生成私货特质用的自定义状态图标（放到 Project_Depersonal\Assets\Resources\Texture\BuffIcon\）。
#
# 为什么自己画：原版 buff 图标里没有"双剑/记忆"这一挂的，借"攻击准备"那类都不贴。
# 生成方式是纯 System.Drawing 画图，128×128，画完直接看一眼不满意就调参数重跑。
#
# 用法：powershell -File mods\可视化攻击目标\tools\make_secret_icons.ps1
#      （2026-09-22 随私货从「自定义心」搬过来；脚本按 $PSScriptRoot 推 mod 根目录，搬完不用改路径）

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$OutDir  = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\BuffIcon'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-RoundedRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($x, $y, $r * 2, $r * 2, 180, 90)
  $p.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
  $p.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90)
  $p.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
  $p.CloseFigure()
  return $p
}

# 画一把剑：$cx/$cy 是剑柄末端，$angleDeg 是剑身指向，$len 剑身长，$w 剑身宽
function Draw-Blade($g, $cx, $cy, $angleDeg, $len, $w) {
  $rad = $angleDeg * [Math]::PI / 180.0
  $dx = [Math]::Cos($rad); $dy = [Math]::Sin($rad)
  $nx = -$dy;              $ny = $dx

  # 剑柄（从柄尾往前 16px）
  $hx = $cx + $dx * 16; $hy = $cy + $dy * 16
  $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 74, 58, 42))), @(
    (New-Object System.Drawing.PointF(($cx + $nx * 2.6), ($cy + $ny * 2.6))),
    (New-Object System.Drawing.PointF(($hx + $nx * 2.6), ($hy + $ny * 2.6))),
    (New-Object System.Drawing.PointF(($hx - $nx * 2.6), ($hy - $ny * 2.6))),
    (New-Object System.Drawing.PointF(($cx - $nx * 2.6), ($cy - $ny * 2.6)))
  ))
  # 柄尾圆头（金色）
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 201, 162, 74))),
    ($cx + $dx * 2 - 4), ($cy + $dy * 2 - 4), 8, 8)

  # 护手（金色横条）
  $gx = $cx + $dx * 20; $gy = $cy + $dy * 20
  $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 176, 84))), @(
    (New-Object System.Drawing.PointF(($gx + $nx * 12 + $dx * 3.5), ($gy + $ny * 12 + $dy * 3.5))),
    (New-Object System.Drawing.PointF(($gx + $nx * 12 - $dx * 3.5), ($gy + $ny * 12 - $dy * 3.5))),
    (New-Object System.Drawing.PointF(($gx - $nx * 12 - $dx * 3.5), ($gy - $ny * 12 - $dy * 3.5))),
    (New-Object System.Drawing.PointF(($gx - $nx * 12 + $dx * 3.5), ($gy - $ny * 12 + $dy * 3.5)))
  ))

  # 剑身（钢色渐变）
  $bx = $gx + $dx * 3.5; $by = $gy + $dy * 3.5
  $ex = $bx + $dx * $len; $ey = $by + $dy * $len
  $blade = @(
    (New-Object System.Drawing.PointF(($bx + $nx * ($w / 2)), ($by + $ny * ($w / 2)))),
    (New-Object System.Drawing.PointF(($bx + $nx * ($w / 2 * 0.55) + $dx * $len), ($by + $ny * ($w / 2 * 0.55) + $dy * $len))),
    (New-Object System.Drawing.PointF(($bx - $nx * ($w / 2 * 0.55) + $dx * $len), ($by - $ny * ($w / 2 * 0.55) + $dy * $len))),
    (New-Object System.Drawing.PointF(($bx - $nx * ($w / 2)), ($by - $ny * ($w / 2))))
  )
  $bb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 222, 238))
  $g.FillPolygon($bb, $blade)
  # 剑脊高光
  $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))), @(
    (New-Object System.Drawing.PointF(($bx + $nx * 1.6), ($by + $ny * 1.6))),
    (New-Object System.Drawing.PointF(($bx + $nx * 1.6 + $dx * ($len - 6)), ($by + $ny * 1.6 + $dy * ($len - 6)))),
    (New-Object System.Drawing.PointF(($bx + $nx * 1.6 - $dx * 6), ($by + $ny * 1.6 - $dy * 6)))
  ))
  # 剑尖亮点
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 255, 255, 255))), ($ex - 3), ($ey - 3), 6, 6)
}

function New-MemoryBladesIcon($path) {
  $bmp = New-Object System.Drawing.Bitmap 128, 128
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  # 底板
  $outer = New-RoundedRect 2 2 124 124 16
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 34, 48))), $outer)
  # 记忆感的紫色辉光（中心亮、边缘暗）
  $glowPath = New-RoundedRect 8 8 112 112 12
  $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
  $pgb.CenterColor = [System.Drawing.Color]::FromArgb(150, 120, 92, 190)
  $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 90, 70, 150))
  $g.FillPath($pgb, $glowPath)
  # 边框
  $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 96, 108, 140), 3)), $outer)

  # 两把交叉的剑：左下的剑指向右上、右下的剑指向左上，在中上部交叉成 X
  Draw-Blade -g $g -cx 38 -cy 104 -angleDeg (-55) -len 58 -w 11
  Draw-Blade -g $g -cx 90 -cy 104 -angleDeg (-125) -len 58 -w 11

  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

$out = Join-Path $OutDir 'secret_shuangjian_icon.png'
New-MemoryBladesIcon $out
Write-Output ("已生成: " + $out)

# ---------------------------------------------------------------
# 百合花用的"爱心"帧动画（ExtraAnim）：粉色心 + 三帧心跳缩放
# 输出到 Texture\ExtraAnim\secret_heart_0X.png（配置文件见 Game\ExtraAnim\secret_heart.txt）
# ---------------------------------------------------------------
function Draw-Heart($g, $cx, $cy, $s) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.StartFigure()
  $path.AddBezier(
    ($cx - 0.00 * $s), ($cy + 0.78 * $s),
    ($cx - 0.98 * $s), ($cy + 0.10 * $s),
    ($cx - 0.62 * $s), ($cy - 0.86 * $s),
    ($cx - 0.00 * $s), ($cy - 0.28 * $s))
  $path.AddBezier(
    ($cx + 0.00 * $s), ($cy - 0.28 * $s),
    ($cx + 0.62 * $s), ($cy - 0.86 * $s),
    ($cx + 0.98 * $s), ($cy + 0.10 * $s),
    ($cx + 0.00 * $s), ($cy + 0.78 * $s))
  $path.CloseFigure()

  $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
  $brush.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 170, 205)
  $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 246, 60, 130))
  $brush.CenterPoint = New-Object System.Drawing.PointF -ArgumentList ($cx - 0.18 * $s), ($cy - 0.30 * $s)
  $g.FillPath($brush, $path)

  $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(255, 255, 215, 232)), (2.2 * $s / 90)
  $g.DrawPath($pen, $path)

  # 左上高光
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))),
    ($cx - 0.52 * $s), ($cy - 0.46 * $s), (0.26 * $s), (0.20 * $s))
}

$AnimDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
New-Item -ItemType Directory -Force -Path $AnimDir | Out-Null

$scales = @(0.92, 1.0, 1.08)
for ($i = 0; $i -lt $scales.Count; $i++) {
  $bmp = New-Object System.Drawing.Bitmap 256, 256
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  Draw-Heart $g 128 126 (88.0 * $scales[$i])
  $file = Join-Path $AnimDir ("secret_heart_{0:00}.png" -f ($i + 1))
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ("已生成: " + $file)
}
