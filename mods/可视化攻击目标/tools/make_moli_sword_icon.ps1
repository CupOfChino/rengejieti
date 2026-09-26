# 为茉莉做「专属武器」图标：把原版蔷薇黑剑的图标改造成白 / 绿配色，并在剑格上加一朵百合花。
#
# 用法：
#   powershell -File tools\make_moli_sword_icon.ps1 -SourceIcon <原版图标png> -OutputDir <输出目录>
#
# 为什么这么做：游戏要的是 128×128、32bppArgb 的像素图标（原版 item 图标就是这个规格），
# 直接照着原版图标改色，能保证描边粗细、明暗层次、构图和原版完全一致。
#
# 配色规则（按通道判断，不引入色相转换的浮点误差）：
#   · 蓝/紫占优（剑身）→ 冰青白：按亮度在 SilverDark → SilverLight 之间插值（暗部带青，亮部近纯白）
#   · 红/橙占优（护手、藤蔓、剑柄）→ 青绿：按亮度在 TealDark → TealLight 之间插值
#   · 其余（近中性灰）原样保留
#
# 青绿的默认端点是直接从茉莉模型图（ResourcesFramework\Input\all\1-1.png）里取的：
#   她那套是"白 + 青绿（teal）"，深色约 #1E6A6A，亮色约 #A8E4E0，最饱和的一档 #20B0B0。

param(
  [Parameter(Mandatory = $true)][string]$SourceIcon,
  [Parameter(Mandatory = $true)][string]$OutputDir,
  [string]$BaseName = 'icon_item_moli_jian',
  [string]$SilverDark  = '#5A7A82',
  [string]$SilverLight = '#FFFFFF',
  [string]$TealDark  = '#1E6A6A',
  [string]$TealLight = '#A8E4E0',
  [switch]$NoLily,
  [switch]$NoPreview
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $SourceIcon)) { throw "找不到源图标：$SourceIcon" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Convert-HexToRgb([string]$hex) {
  $h = $hex.TrimStart('#')
  return @(
    [Convert]::ToInt32($h.Substring(0, 2), 16),
    [Convert]::ToInt32($h.Substring(2, 2), 16),
    [Convert]::ToInt32($h.Substring(4, 2), 16)
  )
}
$tealLo = Convert-HexToRgb $TealDark
$tealHi = Convert-HexToRgb $TealLight
$silverLo = Convert-HexToRgb $SilverDark
$silverHi = Convert-HexToRgb $SilverLight

$src = [System.Drawing.Bitmap]::FromFile($SourceIcon)
$w = $src.Width
$h = $src.Height

# ---- 1. 先找"护手中心"：橙金色像素的重心（百合花就画在那里）----
$sumX = 0.0; $sumY = 0.0; $gold = 0
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $c = $src.GetPixel($x, $y)
    if ($c.A -lt 32) { continue }
    if ($c.R -gt ($c.B + 20) -and $c.R -gt 110 -and $c.G -gt 70) {
      $sumX += $x; $sumY += $y; $gold++
    }
  }
}
$goldCx = if ($gold -gt 0) { [int][Math]::Round($sumX / $gold) } else { [int]($w / 2) }
$goldCy = if ($gold -gt 0) { [int][Math]::Round($sumY / $gold) } else { [int]($h / 2) }

# ---- 2. 逐像素改色 ----
$out = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$changed = 0
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $c = $src.GetPixel($x, $y)
    if ($c.A -lt 8) {
      $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
      continue
    }
    $r = [int]$c.R; $g = [int]$c.G; $b = [int]$c.B
    if ($b -ge $r -and $b -ge $g -and ($b - $r) -ge 6) {
      # 剑身：紫蓝 → 冰青白（明暗层次原样保留，整体换成"冷银白"）
      $lum = (0.35 * $r + 0.5 * $g + 0.15 * $b) / 255.0
      if ($lum -gt 1.0) { $lum = 1.0 }
      $nr = [Math]::Min(255, [int]($silverLo[0] + ($silverHi[0] - $silverLo[0]) * $lum))
      $ng = [Math]::Min(255, [int]($silverLo[1] + ($silverHi[1] - $silverLo[1]) * $lum))
      $nb = [Math]::Min(255, [int]($silverLo[2] + ($silverHi[2] - $silverLo[2]) * $lum))
      $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A, $nr, $ng, $nb))
      $changed++
    }
    elseif ($r -gt ($b + 8)) {
      # 护手 / 藤蔓 / 剑柄：橙红 → 青绿（按原本的亮度在深青和淡青之间插值，明暗层次原样保留）
      $lum = (0.35 * $r + 0.5 * $g + 0.15 * $b) / 255.0
      if ($lum -gt 1.0) { $lum = 1.0 }
      $nr = [int]($tealLo[0] + ($tealHi[0] - $tealLo[0]) * $lum)
      $ng = [int]($tealLo[1] + ($tealHi[1] - $tealLo[1]) * $lum)
      $nb = [int]($tealLo[2] + ($tealHi[2] - $tealLo[2]) * $lum)
      $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A, $nr, $ng, $nb))
      $changed++
    }
    else {
      $out.SetPixel($x, $y, $c)
    }
  }
}
$src.Dispose()

# ---- 3. 剑格上加一朵像素百合（白花瓣 + 绿花蕊 + 深色描边）----
$petalR = [int][Math]::Max(8, [Math]::Min(14, $w * 0.095))
$lilyPixels = 0
if (-not $NoLily) {
  $stroke = [System.Drawing.Color]::FromArgb(255, 78, 138, 132)
  $white = [System.Drawing.Color]::FromArgb(255, 252, 255, 250)
  $shade = [System.Drawing.Color]::FromArgb(255, 206, 236, 230)
  $stem = [System.Drawing.Color]::FromArgb(255, 46, 150, 144)
  $core = [System.Drawing.Color]::FromArgb(255, 232, 208, 106)

  $points = @{}
  function Add-Point([int]$px, [int]$py, $color, [int]$order) {
    if ($px -lt 0 -or $py -lt 0 -or $px -ge $w -or $py -ge $h) { return }
    $key = "$px,$py"
    if ($points.ContainsKey($key) -and $points[$key].Order -gt $order) { return }
    $points[$key] = [pscustomobject]@{ X = $px; Y = $py; Color = $color; Order = $order }
  }

  # 描边层（比花瓣大一圈）
  for ($i = 0; $i -lt 6; $i++) {
    $ang = [Math]::PI * 2.0 * $i / 6.0 - [Math]::PI / 2.0
    for ($t = 2; $t -le ($petalR + 1); $t++) {
      $halfW = [Math]::Max(1.0, ($petalR + 1) * 0.24 * [Math]::Sin([Math]::PI * ($t / ($petalR + 1.0))))
      for ($s = -$halfW; $s -le $halfW; $s += 0.5) {
        $px = [int][Math]::Round($goldCx + $t * [Math]::Cos($ang) - $s * [Math]::Sin($ang))
        $py = [int][Math]::Round($goldCy + $t * [Math]::Sin($ang) + $s * [Math]::Cos($ang))
        Add-Point $px $py $stroke 0
      }
    }
  }
  # 花瓣层
  for ($i = 0; $i -lt 6; $i++) {
    $ang = [Math]::PI * 2.0 * $i / 6.0 - [Math]::PI / 2.0
    for ($t = 2; $t -le $petalR; $t++) {
      $halfW = [Math]::Max(1.0, $petalR * 0.20 * [Math]::Sin([Math]::PI * ($t / [double]$petalR)))
      for ($s = -$halfW; $s -le $halfW; $s += 0.5) {
        $px = [int][Math]::Round($goldCx + $t * [Math]::Cos($ang) - $s * [Math]::Sin($ang))
        $py = [int][Math]::Round($goldCy + $t * [Math]::Sin($ang) + $s * [Math]::Cos($ang))
        # 花瓣根部（靠花心的一小段）带一点淡青，其余纯白 —— 更像百合
        $tone = if ($t -lt ($petalR * 0.45)) { $shade } elseif ($s -ge 0) { $white } else { $shade }
        Add-Point $px $py $tone 1
      }
    }
  }
  # 花心：青底 + 中间几粒黄色花药（百合的花蕊就长这样）
  for ($dy = -3; $dy -le 3; $dy++) {
    for ($dx = -3; $dx -le 3; $dx++) {
      $dist = $dx * $dx + $dy * $dy
      if ($dist -gt 11) { continue }
      Add-Point ($goldCx + $dx) ($goldCy + $dy) $stem 2
    }
  }
  foreach ($pt in @(@(0, 0), @(-2, -1), @(2, -1), @(0, 2))) {
    Add-Point ($goldCx + $pt[0]) ($goldCy + $pt[1]) $core 3
  }

  foreach ($k in $points.Keys) {
    $p = $points[$k]
    $out.SetPixel($p.X, $p.Y, $p.Color)
    $lilyPixels++
  }
}

# ---- 4. 保存成品 + 4 倍预览 ----
$outFile = Join-Path $OutputDir ($BaseName + '.png')
$out.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)

if (-not $NoPreview) {
  $pv = New-Object System.Drawing.Bitmap(($w * 4), ($h * 4))
  $g2 = [System.Drawing.Graphics]::FromImage($pv)
  $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g2.DrawImage($out, 0, 0, $w * 4, $h * 4)
  $g2.Dispose()
  $pv.Save((Join-Path $OutputDir ($BaseName + '_x4.png')), [System.Drawing.Imaging.ImageFormat]::Png)
  $pv.Dispose()
}

$out.Dispose()
Write-Output ("图标已生成：{0}（{1}×{2}，改色 {3} 像素，护手中心 {4},{5}，百合 {6} 像素）" -f $outFile, $w, $h, $changed, $goldCx, $goldCy, $lilyPixels)
