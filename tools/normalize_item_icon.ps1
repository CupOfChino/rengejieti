# 把一张（AI 生成的）图标图规整成游戏道具图标要的样子：128×128、透明底、内容居中带边距。
#
# 为什么需要：qwen-image 出的是 1024/2048 的大图，内容四周有透明留白、比例也不一定方正；
# 游戏图标是 128×128，直接把大图缩下去会让图案偏小、位置也不对。
#
# 用法：
#   powershell -File tools\normalize_item_icon.ps1 -InFile "原图.png" -OutFile "icon_item_xxx.png"
#   powershell -File tools\normalize_item_icon.ps1 -InFile "原图.png" -OutFile "out.png" -Size 128 -Padding 6
#
# 做法：先缩到 256 找"非透明内容的边界"（比在大图上逐像素扫描快得多），
#      再按边界裁剪、等比缩放到 (Size - 2*Padding) 以内，居中贴到 Size×Size 的透明画布上。

param(
  [Parameter(Mandatory = $true)][string]$InFile,
  [Parameter(Mandatory = $true)][string]$OutFile,
  [int]$Size = 128,
  [int]$Padding = 6,
  [int]$AlphaThreshold = 128
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $InFile)) { throw "找不到输入图：$InFile" }

# 注意：**别用 [Color]::Transparent** —— 它是 ARGB(0,255,255,255)，即"RGB 全白的透明"。
# 一旦有哪一步（编辑器预览 / 不正确的 alpha 合成）忽略 alpha，看到的就是**纯白底**。
# 统一用"全透明黑"：即使 alpha 被忽略，露出来的也是黑，不会是一层白。
$TransparentBlack = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)

$src = [System.Drawing.Image]::FromFile($InFile)

# ---- 1. 先缩到 256 做边界检测 ----
$probe = 256
$small = New-Object System.Drawing.Bitmap($probe, $probe, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g0 = [System.Drawing.Graphics]::FromImage($small)
$g0.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g0.Clear($TransparentBlack)
$scale = [Math]::Min($probe / $src.Width, $probe / $src.Height)
$w0 = [int]($src.Width * $scale); $h0 = [int]($src.Height * $scale)
$g0.DrawImage($src, (($probe - $w0) / 2), (($probe - $h0) / 2), $w0, $h0)
$g0.Dispose()

$minX = $probe; $minY = $probe; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $probe; $y++) {
  for ($x = 0; $x -lt $probe; $x++) {
    if ($small.GetPixel($x, $y).A -gt 16) {
      if ($x -lt $minX) { $minX = $x }
      if ($x -gt $maxX) { $maxX = $x }
      if ($y -lt $minY) { $minY = $y }
      if ($y -gt $maxY) { $maxY = $y }
    }
  }
}
$small.Dispose()

if ($maxX -lt 0) { throw "整张图都是透明的？检查一下输入图：$InFile" }

# 换算回原图坐标
$offX = ($probe - $w0) / 2.0; $offY = ($probe - $h0) / 2.0
$cropX = [int](($minX - $offX) / $scale); $cropY = [int](($minY - $offY) / $scale)
$cropW = [int](($maxX - $minX + 1) / $scale); $cropH = [int](($maxY - $minY + 1) / $scale)
if ($cropX -lt 0) { $cropX = 0 }
if ($cropY -lt 0) { $cropY = 0 }
if ($cropX + $cropW -gt $src.Width) { $cropW = $src.Width - $cropX }
if ($cropY + $cropH -gt $src.Height) { $cropH = $src.Height - $cropY }

# ---- 2. 裁剪 → 等比缩放 → 居中贴到透明画布 ----
$canvas = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear($TransparentBlack)

$box = $Size - 2 * $Padding
$k = [Math]::Min($box / $cropW, $box / $cropH)
$dw = [int][Math]::Round($cropW * $k); $dh = [int][Math]::Round($cropH * $k)
$dx = [int](($Size - $dw) / 2); $dy = [int](($Size - $dh) / 2)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle($dx, $dy, $dw, $dh)), (New-Object System.Drawing.Rectangle($cropX, $cropY, $cropW, $cropH)), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

# ---- 3. alpha 二值化：干掉缩放产生的"半透明白雾边" ----
# 大图缩小时，边缘的透明与不透明像素会按比例混出 alpha=200~250 的**白色**像素；
# 它们在游戏里看着就是一圈白底 / 白边。这里直接把 alpha 拉成 0 或 255（像素风本来也是硬边）。
if ($AlphaThreshold -gt 0) {
  $fixed = 0
  for ($y = 0; $y -lt $Size; $y++) {
    for ($x = 0; $x -lt $Size; $x++) {
      $c = $canvas.GetPixel($x, $y)
      if ($c.A -eq 0) {
        # 全透明的地方连 RGB 一起清掉（AI 出的图透明区常常是"白色的透明"）
        if ($c.R -ne 0 -or $c.G -ne 0 -or $c.B -ne 0) {
          $canvas.SetPixel($x, $y, $TransparentBlack)
          $fixed++
        }
        continue
      }
      if ($c.A -lt $AlphaThreshold) {
        $canvas.SetPixel($x, $y, $TransparentBlack)
        $fixed++
      } elseif ($c.A -lt 255) {
        $canvas.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B))
        $fixed++
      }
    }
  }
  Write-Output ("alpha 二值化：修正了 {0} 个半透明像素（阈值 {1}）" -f $fixed, $AlphaThreshold)
}

$outDir = Split-Path -Parent $OutFile
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$canvas.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()

Write-Output ("裁切区域：{0},{1} {2}×{3} → 画布 {4}×{4}（内容 {5}×{6}，边距 {7}px）" -f $cropX, $cropY, $cropW, $cropH, $Size, $dw, $dh, $Padding)
Write-Output ("已保存：{0}（{1} 字节）" -f $OutFile, (Get-Item -LiteralPath $OutFile).Length)
