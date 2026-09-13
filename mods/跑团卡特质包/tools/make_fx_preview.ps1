# 把 ExtraAnim 里生成好的帧图拼成预览：一张定格表 + 一个会动的 gif。
# 用途：改完特效参数先看图，别急着进游戏。
#
# 用法： powershell -File tools\make_fx_preview.ps1 -Prefix xin_flame

param(
  [string]$Prefix = 'xin_flame',
  [string]$Dir = '',
  [string]$OutDir = '',
  [int]$Cell = 200,
  [int]$Cols = 7,
  [int]$DelayMs = 70
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
if ($Dir) { $AnimDir = $Dir } else { $AnimDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim' }
if (-not $OutDir) { $OutDir = Join-Path $ModRoot 'tools\preview' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$frames = Get-ChildItem -LiteralPath $AnimDir -File -Filter "$Prefix*.png" | Sort-Object Name
if ($frames.Count -eq 0) { throw "没找到帧图：$AnimDir\$Prefix*.png" }
Write-Output ("找到 {0} 帧" -f $frames.Count)

# ---- 定格表（深色底，看得清金光）----
$rows = [Math]::Ceiling($frames.Count / [double]$Cols)
$sheet = New-Object System.Drawing.Bitmap(($Cell * $Cols), ($Cell * $rows), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 26))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$i = 0
foreach ($f in $frames) {
  $img = [System.Drawing.Image]::FromFile($f.FullName)
  $x = ($i % $Cols) * $Cell
  $y = [Math]::Floor($i / $Cols) * $Cell
  $g.DrawImage($img, $x, $y, $Cell, $Cell)
  $img.Dispose()
  $i++
}
$g.Dispose()
$sheetPath = Join-Path $OutDir "$Prefix`_sheet.png"
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Output "定格表：$sheetPath"

# ---- 会动的 gif（用 WPF 的编码器，能写帧延时）----
try {
  Add-Type -AssemblyName PresentationCore
  Add-Type -AssemblyName WindowsBase
  # gif 对透明支持很差，先把每帧贴到深色底上再编码，免得出现"背景残留"的假象
  $tmpDir = Join-Path $OutDir '_flat'
  New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
  $flatFiles = @()
  foreach ($f in $frames) {
    $src = [System.Drawing.Image]::FromFile($f.FullName)
    $flat = New-Object System.Drawing.Bitmap($src.Width, $src.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g2 = [System.Drawing.Graphics]::FromImage($flat)
    $g2.Clear([System.Drawing.Color]::FromArgb(255, 20, 20, 22))
    $g2.DrawImage($src, 0, 0, $src.Width, $src.Height)
    $g2.Dispose()
    $src.Dispose()
    $flatPath = Join-Path $tmpDir $f.Name
    $flat.Save($flatPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $flat.Dispose()
    $flatFiles += $flatPath
  }
  $enc = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
  $delay = [uint16][Math]::Max(2, [Math]::Round($DelayMs / 10.0))
  foreach ($flatPath in $flatFiles) {
    $fs = [System.IO.File]::OpenRead($flatPath)
    $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create(
      $fs,
      [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
      [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $meta = New-Object System.Windows.Media.Imaging.BitmapMetadata('gif')
    $meta.SetQuery('/grctlext/Delay', [uint16]$delay)
    $frame2 = [System.Windows.Media.Imaging.BitmapFrame]::Create($frame, $null, $meta, $null)
    $enc.Frames.Add($frame2)
    $fs.Dispose()
  }
  $gifPath = Join-Path $OutDir "$Prefix.gif"
  $out = [System.IO.File]::Create($gifPath)
  $enc.Save($out)
  $out.Dispose()
  Write-Output "动图：$gifPath"
} catch {
  Write-Output ("gif 生成失败（定格表还能看）：" + $_.Exception.Message)
}
