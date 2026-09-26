# 校验整套帧资源：完整性 / 透明边缘与顶部有没有被裁 / 循环接缝正不正常。
#
# 为什么单独一个脚本：出整套帧之后，"张数对不对、四边有没有切到、接缝是不是比别的相邻帧突跳"
# 这三件事每次都有人肉看错的风险（尤其顶部光晕 σ8 已经接近画布上边界）。这里用字节级检查，
# 不靠肉眼。**必须用 Windows PowerShell 5.1 跑**（System.Drawing 在 .NET Framework 下才顺手）。
#
# 用法：
#   powershell -File tools\verify_frames.ps1
#   powershell -File tools\verify_frames.ps1 -TexDir <目录> -Prefix xin_glow

param(
  [string]$TexDir = '',
  [string]$Prefix = 'xin_glow',
  [int]$ExpectCount = 48,
  [int]$ExpectSize = 512,
  [int]$ExpectHeight = 0,        # 非方形画布时单独指定高度（默认与宽度相同）
  [int]$BorderPx = 2,            # 四边各查几像素
  [double]$AlphaTol = 1.0        # 边界允许的最大 alpha（0~255）
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
if (-not $TexDir) { $TexDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim' }
if (-not (Test-Path -LiteralPath $TexDir)) { throw "找不到帧目录：$TexDir" }
if ($ExpectHeight -le 0) { $ExpectHeight = $ExpectSize }

function Read-Alpha([string]$path) {
  $bmp = New-Object System.Drawing.Bitmap($path)
  $rect = New-Object System.Drawing.Rectangle -ArgumentList @(0, 0, $bmp.Width, $bmp.Height)
  $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $buf = New-Object byte[] ($data.Stride * $bmp.Height)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
  $bmp.UnlockBits($data)
  $w = $bmp.Width; $h = $bmp.Height; $stride = $data.Stride
  $bmp.Dispose()
  # BGRA → 取 A
  $alpha = New-Object byte[] ($w * $h)
  for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $w; $x++) { $alpha[$y * $w + $x] = $buf[$row + $x * 4 + 3] }
  }
  return @{ W = $w; H = $h; A = $alpha }
}

$files = Get-ChildItem -LiteralPath $TexDir -File -Filter ($Prefix + '_*.png') | Sort-Object Name
Write-Output "── 帧校验：$TexDir ──"
Write-Output ("  张数：{0}（期望 {1}）{2}" -f $files.Count, $ExpectCount, $(if ($files.Count -eq $ExpectCount) { ' OK' } else { ' ✗ 不一致' }))

# 连贯编号
$bad = @()
for ($i = 1; $i -le $files.Count; $i++) {
  $want = '{0}_{1:D2}.png' -f $Prefix, $i
  if ($files[$i - 1].Name -ne $want) { $bad += $want }
}
if ($bad.Count -gt 0) { Write-Output ("  编号不连续，缺：{0}" -f ($bad -join ', ')) } else { Write-Output '  编号连续 01..N OK' }

$sums = @(); $borderMax = 0; $borderWhere = ''
$prev = $null; $prevName = ''
$diffs = @()
$sizeBad = 0
foreach ($f in $files) {
  $a = Read-Alpha $f.FullName
  if ($a.W -ne $ExpectSize -or $a.H -ne $ExpectHeight) { $sizeBad++; Write-Output ("  ✗ {0} 尺寸 {1}x{2}" -f $f.Name, $a.W, $a.H) }
  $sum = 0
  for ($k = 0; $k -lt $a.A.Length; $k++) { $sum += $a.A[$k] }
  $sums += $sum

  # 四边
  for ($y = 0; $y -lt $a.H; $y++) {
    for ($x = 0; $x -lt $a.W; $x++) {
      $edge = ($y -lt $BorderPx) -or ($y -ge ($a.H - $BorderPx)) -or ($x -lt $BorderPx) -or ($x -ge ($a.W - $BorderPx))
      if (-not $edge) { continue }
      $v = $a.A[$y * $a.W + $x]
      if ($v -gt $borderMax) { $borderMax = $v; $borderWhere = "$($f.Name) @($x,$y)" }
    }
  }

  if ($null -ne $prev) {
    $d = 0.0
    for ($k = 0; $k -lt $a.A.Length; $k++) { $d += [math]::Abs([int]$a.A[$k] - [int]$prev[$k]) }
    $diffs += @{ From = $prevName; To = $f.Name; Mean = ($d / $a.A.Length) }
  }
  $prev = $a.A; $prevName = $f.Name
}

# 接缝：最后一帧 → 第一帧
$first = Read-Alpha $files[0].FullName
$last = Read-Alpha $files[$files.Count - 1].FullName
$d = 0.0
for ($k = 0; $k -lt $first.A.Length; $k++) { $d += [math]::Abs([int]$last.A[$k] - [int]$first.A[$k]) }
$seam = $d / $first.A.Length

$inFrame = $diffs | ForEach-Object { $_.Mean }
$dMin = ($inFrame | Measure-Object -Minimum).Minimum
$dMax = ($inFrame | Measure-Object -Maximum).Maximum
$dAvg = ($inFrame | Measure-Object -Average).Average
$sumMin = ($sums | Measure-Object -Minimum).Minimum
$sumMax = ($sums | Measure-Object -Maximum).Maximum

Write-Output ("  尺寸：{0} 张 {1}x{2}{3}" -f $files.Count, $ExpectSize, $ExpectHeight, $(if ($sizeBad -eq 0) { ' OK' } else { " ✗ 有 $sizeBad 张不对" }))
Write-Output ("  alpha 总量：最小 {0:N0} / 最大 {1:N0}（{2}）" -f $sumMin, $sumMax, $(if ($sumMin -gt 0) { '没有空帧 OK' } else { '✗ 有空帧' }))
Write-Output ("  四边 {0}px 内最大 alpha：{1}{2}" -f $BorderPx, $borderMax, $(if ($borderMax -le $AlphaTol) { '（≤ 容差，无裁切 OK）' } else { "（> 容差 ✗ 位置 $borderWhere）" }))
Write-Output ("  相邻帧平均差：min {0:N3} / avg {1:N3} / max {2:N3}" -f $dMin, $dAvg, $dMax)
$verdict = if ($seam -ge ($dMin * 0.5) -and $seam -le ($dMax * 1.5)) { '在正常范围内 OK' } else { '✗ 明显偏离其它相邻帧，需检查' }
Write-Output ("  接缝（第 {0} 帧 → 第 1 帧）平均差：{1:N3} → {2}" -f $files.Count, $seam, $verdict)
