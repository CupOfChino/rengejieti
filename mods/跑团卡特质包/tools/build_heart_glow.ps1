# 生成「心」的金色气焰动画（帧图）。
#
# 绘制逻辑在 glow_gen\GlowGen.cs：宽火焰面 + 中等光带 + 短火舌 + 局部白黄亮芯 + 两级派生外晕。
# 本脚本负责：编译、出 A/B/C 单帧对比（验收用）、需要时再出整套 48 帧。
#
# 用法：
#   powershell -File tools\build_heart_glow.ps1            # 只出 A/B/C 单帧对比
#   powershell -File tools\build_heart_glow.ps1 -Full      # 同时出整套动画（会覆盖游戏用的帧）
#
# 必须用 Windows PowerShell 5.1 跑（System.Drawing 在 .NET Framework 下才顺手）。

param(
  [int]$Frames = 48,
  [int]$TestFrame = 12,          # 用第几帧做对比（1 起算）
  [int]$Seed = 20260913,
  [double]$HaloNear = 0.16,
  [double]$HaloFar = 0.05,
  [string]$Prefix = 'xin_glow',
  [string]$StandIn = 'E:\lim\_save\素材库\01_YiSang\SD\ErosionAppearance_2010221\idle.png',
  [switch]$Full
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot   = Split-Path -Parent $PSScriptRoot
$GenSource = Join-Path $PSScriptRoot 'glow_gen\GlowGen.cs'
$TexDir    = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim'
$AbcDir    = Join-Path $PSScriptRoot 'preview\abc'
$H_PX      = 245.0                 # 必须和 GlowGen.cs 里的 H_PX 一致

if (-not (Test-Path -LiteralPath $GenSource)) { throw "找不到生成器源码：$GenSource" }
New-Item -ItemType Directory -Force -Path $AbcDir | Out-Null

$src = [System.IO.File]::ReadAllText($GenSource, [System.Text.Encoding]::UTF8)
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing
Write-Output 'C# 生成器编译完成'

# 把角色按"角色高度 = H_PX 像素、身体中心在画布中央"贴上去（特效在角色下面）
function Draw-OnChar($g, $fx, $charImg, $cellX, $cellY, [double]$scale) {
  $chH = $H_PX * $scale
  $chW = $charImg.Width * ($chH / $charImg.Height)
  $cx = $cellX + $cell / 2.0
  $feet = $cellY + $cell / 2.0 + 0.5 * $H_PX      # 脚底在"身体中心 + 半个角色高"
  $g.DrawImage($fx, [float]$cellX, [float]$cellY, [float]($cell * $scale), [float]($cell * $scale))
  $g.DrawImage($charImg, [float]($cx - $chW / 2), [float]($feet - $chH), [float]$chW, [float]$chH)
}

# ---- A/B/C 单帧（同种子、同帧号）----
# A 无外晕 / B 近光晕 / C 近+远光晕
$variants = @(
  @{ Name = 'A_无外晕';   Near = 0.0;         Far = 0.0 },
  @{ Name = 'B_近光晕';   Near = $HaloNear;   Far = 0.0 },
  @{ Name = 'C_近远光晕'; Near = $HaloNear;   Far = $HaloFar }
)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($v in $variants) {
  $dir = Join-Path $AbcDir $v.Name
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Get-ChildItem -LiteralPath $dir -File -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force
  [GlowGen.Gen]::Run($dir, $Frames, $v.Near, $v.Far, $true, $Seed, $Prefix, ($TestFrame - 1))
}
Write-Output ("A/B/C 单帧生成完成（第 {0} 帧，{1:N1} 秒）" -f $TestFrame, $sw.Elapsed.TotalSeconds)

# ---- 每份再拼四种视图：透明底 / 灰底+角色 / 黑底+角色 / 游戏实际尺寸 ----
$char = [System.Drawing.Image]::FromFile($StandIn)
$cell = 512
$views = New-Object System.Collections.Generic.List[object]
foreach ($v in $variants) {
  $dir = Join-Path $AbcDir $v.Name
  $fxPath = Join-Path $dir ('{0}_{1:D2}.png' -f $Prefix, $TestFrame)
  if (-not (Test-Path -LiteralPath $fxPath)) { continue }

  $out = New-Object System.Drawing.Bitmap(($cell * 2), ($cell * 2), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($out)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

  # 视图1：透明底（用深灰棋盘近似展示透明）
  $g.Clear([System.Drawing.Color]::FromArgb(255, 120, 120, 124))
  $fx = [System.Drawing.Image]::FromFile($fxPath)
  $g.DrawImage($fx, 0, 0, $cell, $cell)

  # 视图2：灰底 + 角色
  $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 150, 155))), $cell, 0, $cell, $cell)
  Draw-OnChar $g $fx $char $cell 0 1.0

  # 视图3：黑底 + 角色
  $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 16, 16, 18))), 0, $cell, $cell, $cell)
  Draw-OnChar $g $fx $char 0 $cell 1.0

  # 视图4：游戏实际显示尺寸（角色约 83px 高 → 整体缩到 0.34 倍）
  $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 16, 16, 18))), $cell, $cell, $cell, $cell)
  Draw-OnChar $g $fx $char $cell $cell 0.34

  $fx.Dispose()
  $g.Dispose()
  $bmpPath = Join-Path $AbcDir ($v.Name + '.png')
  $out.Save($bmpPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose()
  $views.Add($bmpPath)
}
$char.Dispose()
$views | ForEach-Object { Write-Output ("四视图：$_") }

# ---- 需要时才出整套动画 ----
if ($Full) {
  $sw.Restart()
  [GlowGen.Gen]::Run($TexDir, $Frames, $HaloNear, $HaloFar, $true, $Seed, $Prefix, -1)
  Write-Output ("整套 {0} 帧已生成（{1:N1} 秒）→ {2}" -f $Frames, $sw.Elapsed.TotalSeconds, $TexDir)
}
