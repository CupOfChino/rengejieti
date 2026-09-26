# 画「百合花」道具图标（128×128、像素风、透明底）。
#
# 为什么自己画：原版「鲜花」（icon_item_xianhua）是粉色的团状花，形态上和百合差得远，
# 光改色不像；参考它的构图（居中花朵 + 底部叶片 + 深色硬描边 + 少色）重新画一朵六瓣百合更贴。
#
# 用法：
#   powershell -File tools\make_baihehua_icon.ps1
#   powershell -File tools\make_baihehua_icon.ps1 -OutFile <路径>
#
# 画完把 PNG 丢给游戏的「编辑模组」导入（生成 .png/.png.rtmeta/.png.rtview 三件套），
# 再把它注册进 InternalConfigure.txt（1004）并给道具数据填 IconPathReference。
#
# 改版记录：v1 画成了八瓣的"星星"，v2 收敛成六瓣宽瓣 + 中脉 + 花蕊，颜色从紫粉调成白粉。

param(
  [string]$OutFile = '',
  [int]$Size = 128
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutFile) {
  $ModRoot = Split-Path -Parent $PSScriptRoot
  # 待导入的图统一放仓库根的 temp\icon_work（用户约定，2026-09-22）
  $RepoRoot = Split-Path -Parent (Split-Path -Parent $ModRoot)
  $OutFile = Join-Path $RepoRoot 'temp\icon_work\icon_item_baihehua.png'
}
$outDir = Split-Path -Parent $OutFile
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ---------------- 调色板（少色 = 像素味）----------------
$C = @{
  Outline    = [System.Drawing.Color]::FromArgb(255, 124, 86, 106)  # 灰紫描边
  PetalBack  = [System.Drawing.Color]::FromArgb(255, 240, 214, 230) # 后层花瓣（淡粉阴影）
  PetalFront = [System.Drawing.Color]::FromArgb(255, 255, 253, 255) # 前层花瓣（近纯白）
  PetalVein  = [System.Drawing.Color]::FromArgb(255, 230, 202, 218) # 花瓣中脉
  PetalRoot  = [System.Drawing.Color]::FromArgb(255, 246, 216, 168) # 花瓣根部暖色
  Stamen     = [System.Drawing.Color]::FromArgb(255, 232, 178, 62)  # 花丝（黄）
  StamenTip  = [System.Drawing.Color]::FromArgb(255, 172, 96, 48)   # 花药（橙）
  LeafDark   = [System.Drawing.Color]::FromArgb(255, 62, 92, 44)    # 叶子暗部
  Leaf       = [System.Drawing.Color]::FromArgb(255, 110, 150, 74)  # 叶子亮部
  LeafLine   = [System.Drawing.Color]::FromArgb(255, 42, 62, 30)    # 叶脉 / 叶描边
}

$bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None        # 硬边 → 像素风
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.Clear([System.Drawing.Color]::Transparent)

$outlinePen = New-Object System.Drawing.Pen($C.Outline, 1.5)
$leafPen = New-Object System.Drawing.Pen($C.LeafLine, 1.5)
$veinPen = New-Object System.Drawing.Pen($C.PetalVein, 1)
$stamenPen = New-Object System.Drawing.Pen($C.Stamen, 1.5)

# 极坐标 → 画布坐标（正上方 0°、顺时针）
function Get-Pt([double]$len, [double]$deg, [double]$cx, [double]$cy) {
  $rad = ($deg - 90) * [Math]::PI / 180
  return New-Object System.Drawing.PointF(($cx + $len * [Math]::Cos($rad)), ($cy + $len * [Math]::Sin($rad)))
}

# 一片花瓣：腰带形，最宽处在 60% 处，尖端收成小尖（百合的瓣形）
# $halfDeg = 半张角（度），决定花瓣有多宽
function New-Petal([double]$cx, [double]$cy, [double]$deg, [double]$len, [double]$halfDeg, [double]$rootDeg) {
  $pts = New-Object 'System.Drawing.PointF[]' 8
  $pts[0] = Get-Pt 2.0 $deg $cx $cy
  $pts[1] = Get-Pt ($len * 0.22) ($deg - $rootDeg) $cx $cy
  $pts[2] = Get-Pt ($len * 0.60) ($deg - $halfDeg) $cx $cy
  $pts[3] = Get-Pt ($len * 0.80) ($deg - ($halfDeg * 0.72)) $cx $cy
  $pts[4] = Get-Pt $len $deg $cx $cy
  $pts[5] = Get-Pt ($len * 0.80) ($deg + ($halfDeg * 0.72)) $cx $cy
  $pts[6] = Get-Pt ($len * 0.60) ($deg + $halfDeg) $cx $cy
  $pts[7] = Get-Pt ($len * 0.22) ($deg + $rootDeg) $cx $cy
  return ,$pts
}

$cx = 64.0
$cy = 58.0

$backBrush = New-Object System.Drawing.SolidBrush($C.PetalBack)
$frontBrush = New-Object System.Drawing.SolidBrush($C.PetalFront)
$rootBrush = New-Object System.Drawing.SolidBrush($C.PetalRoot)
$stamenBrush = New-Object System.Drawing.SolidBrush($C.StamenTip)

# ---- 六瓣：后三瓣偏粉衬底，前三瓣近白压在上面 ----
$backDegs = @(30.0, 150.0, 270.0)
$frontDegs = @(90.0, 210.0, 330.0)

foreach ($deg in $backDegs) {
  $p = New-Petal $cx $cy $deg 47.0 20.0 9.0
  $g.FillPolygon($backBrush, $p)
  $g.DrawPolygon($outlinePen, $p)
  $a = Get-Pt 9 $deg $cx $cy
  $b = Get-Pt 36 $deg $cx $cy
  $g.DrawLine($veinPen, $a, $b)
}
foreach ($deg in $frontDegs) {
  $p = New-Petal $cx $cy $deg 45.0 19.0 8.0
  $g.FillPolygon($frontBrush, $p)
  $g.DrawPolygon($outlinePen, $p)
  # 花瓣中脉（浅色细线，让白瓣不至于糊成一片）
  $a = Get-Pt 9 $deg $cx $cy
  $b = Get-Pt 38 $deg $cx $cy
  $g.DrawLine($veinPen, $a, $b)
}

# ---- 喉部暖色（花瓣根部的黄绿/暖色）----
$g.FillEllipse($rootBrush, ($cx - 8), ($cy - 8), 16, 16)
$g.DrawEllipse($outlinePen, ($cx - 8), ($cy - 8), 16, 16)

# ---- 花蕊：五条短黄丝 + 橙色花药 ----
foreach ($deg in 18.0, 55.0, 90.0, 125.0, 162.0) {
  $a = Get-Pt 3 $deg $cx $cy
  $b = Get-Pt 20 $deg $cx $cy
  $g.DrawLine($stamenPen, $a, $b)
  $g.FillEllipse($stamenBrush, ($b.X - 2.5), ($b.Y - 2.5), 5, 5)
}

# ---- 茎 + 两片叶子（比 v1 收小、放低，别压住花）----
$leafDarkBrush = New-Object System.Drawing.SolidBrush($C.LeafDark)
$leafBrush = New-Object System.Drawing.SolidBrush($C.Leaf)

$stemPts = New-Object 'System.Drawing.PointF[]' 4
$stemPts[0] = New-Object System.Drawing.PointF(61, 92)
$stemPts[1] = New-Object System.Drawing.PointF(67, 92)
$stemPts[2] = New-Object System.Drawing.PointF(68, 124)
$stemPts[3] = New-Object System.Drawing.PointF(60, 124)
$g.FillPolygon($leafBrush, $stemPts)
$g.DrawPolygon($leafPen, $stemPts)

$leafR = New-Object 'System.Drawing.PointF[]' 4
$leafR[0] = New-Object System.Drawing.PointF(66, 104)
$leafR[1] = New-Object System.Drawing.PointF(94, 112)
$leafR[2] = New-Object System.Drawing.PointF(72, 126)
$leafR[3] = New-Object System.Drawing.PointF(64, 118)
$g.FillPolygon($leafDarkBrush, $leafR)
$g.DrawPolygon($leafPen, $leafR)

$leafL = New-Object 'System.Drawing.PointF[]' 4
$leafL[0] = New-Object System.Drawing.PointF(62, 106)
$leafL[1] = New-Object System.Drawing.PointF(34, 114)
$leafL[2] = New-Object System.Drawing.PointF(56, 127)
$leafL[3] = New-Object System.Drawing.PointF(64, 119)
$g.FillPolygon($leafBrush, $leafL)
$g.DrawPolygon($leafPen, $leafL)

$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$img = [System.Drawing.Image]::FromFile($OutFile)
Write-Output ("已生成：{0}（{1}×{2}，{3} 字节）" -f $OutFile, $img.Width, $img.Height, (Get-Item -LiteralPath $OutFile).Length)
$img.Dispose()
Write-Output '下一步：把这张 PNG 用游戏的「编辑模组」导入，生成三件套后再来登记注册表。'
