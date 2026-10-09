# 准备「领域效果」状态用的图标（2026-10-09）。
#
# 统一输出到 temp\icon_work\自定义背景\（仓库约定的"给用户导入游戏编辑器"的目录），
# 命名规则 icon_domain_<phys|magic>_<up|down|guard|vuln>.png。
#
#   · 物理 4 个：从《边狱巴士》状态图标直接复制（用户要求用那边那套）
#   · 法术 up/down：用户自己跑好的图（ai-output-*.png → 按内容重命名）
#   · 法术 guard（橙黄）/ vuln（暗红）：对巴士的 Protection / Vulnerable 改色
#
# 每回合造成伤害那两条不需要图标（伤害是"领域带来的"，不显示成角色状态）。
#
# 用法：powershell -File tools\prepare_buff_icons.ps1

param(
  [string]$LimbusIconDir = 'E:\lim\_save\素材库\_状态图标',
  [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ModRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ModRoot)
if (-not $OutDir) {
  $OutDir = Join-Path $RepoRoot 'temp\icon_work\自定义背景'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Get-RecoloredBitmap([System.Drawing.Bitmap]$src, [double]$hueShift, [double]$satScale, [double]$valScale) {
  $dst = New-Object System.Drawing.Bitmap($src.Width, $src.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
      $c = $src.GetPixel($x, $y)
      if ($c.A -eq 0) { $dst.SetPixel($x, $y, $c); continue }

      $r = $c.R / 255.0; $g = $c.G / 255.0; $b = $c.B / 255.0
      $max = [Math]::Max($r, [Math]::Max($g, $b))
      $min = [Math]::Min($r, [Math]::Min($g, $b))
      $d = $max - $min
      $h = 0.0
      if ($d -gt 0.00001) {
        if ($max -eq $r)      { $h = 60.0 * ((($g - $b) / $d) % 6.0) }
        elseif ($max -eq $g)  { $h = 60.0 * ((($b - $r) / $d) + 2.0) }
        else                  { $h = 60.0 * ((($r - $g) / $d) + 4.0) }
      }
      if ($h -lt 0) { $h += 360.0 }
      $s = if ($max -le 0) { 0.0 } else { $d / $max }
      $v = $max

      $h = ($h + $hueShift) % 360.0
      if ($h -lt 0) { $h += 360.0 }
      $s = [Math]::Min(1.0, $s * $satScale)
      $v = [Math]::Min(1.0, $v * $valScale)

      $c2 = $v * $s
      $x2 = $c2 * (1 - [Math]::Abs((($h / 60.0) % 2.0) - 1))
      $m = $v - $c2
      $rr = 0.0; $gg = 0.0; $bb = 0.0
      if ($h -lt 60)       { $rr = $c2; $gg = $x2; $bb = 0 }
      elseif ($h -lt 120)  { $rr = $x2; $gg = $c2; $bb = 0 }
      elseif ($h -lt 180)  { $rr = 0;  $gg = $c2; $bb = $x2 }
      elseif ($h -lt 240)  { $rr = 0;  $gg = $x2; $bb = $c2 }
      elseif ($h -lt 300)  { $rr = $x2; $gg = 0;  $bb = $c2 }
      else                 { $rr = $c2; $gg = 0;  $bb = $x2 }

      $nr = [Math]::Round(($rr + $m) * 255.0)
      $ng = [Math]::Round(($gg + $m) * 255.0)
      $nb = [Math]::Round(($bb + $m) * 255.0)
      $dst.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A, [int]$nr, [int]$ng, [int]$nb))
    }
  }
  return $dst
}

function Copy-Icon([string]$srcRel, [string]$outName) {
  $srcPath = Join-Path $LimbusIconDir $srcRel
  if (-not (Test-Path -LiteralPath $srcPath)) { throw "找不到源图标：$srcPath" }
  $src = [System.Drawing.Bitmap]::FromFile($srcPath)
  try {
    $src.Save((Join-Path $OutDir $outName), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("复制  {0}  ->  {1}" -f $srcRel, $outName)
  } finally { $src.Dispose() }
}

function Recolor-Icon([string]$srcRel, [string]$outName, [double]$hueShift, [double]$satScale, [double]$valScale) {
  $srcPath = Join-Path $LimbusIconDir $srcRel
  if (-not (Test-Path -LiteralPath $srcPath)) { throw "找不到源图标：$srcPath" }
  $src = [System.Drawing.Bitmap]::FromFile($srcPath)
  try {
    $out = Get-RecoloredBitmap $src $hueShift $satScale $valScale
    try {
      $out.Save((Join-Path $OutDir $outName), [System.Drawing.Imaging.ImageFormat]::Png)
      Write-Output ("改色  {0}  ->  {1}（色相 {2}° / 饱和 x{3} / 明度 x{4}）" -f $srcRel, $outName, $hueShift, $satScale, $valScale)
    } finally { $out.Dispose() }
  } finally { $src.Dispose() }
}

# 1) 物理 4 个（直接用巴士的）
Copy-Icon 'AttackDmgUp\AttackDmgUp.png'     'icon_domain_phys_up.png'
Copy-Icon 'AttackDmgDown\AttackDmgDown.png' 'icon_domain_phys_down.png'
Copy-Icon 'Protection\Protection.png'       'icon_domain_phys_guard.png'
Copy-Icon 'Vulnerable\Vulnerable.png'       'icon_domain_phys_vuln.png'

# 2) 法术 up/down：用户自己跑好的图（按内容对应：红色法杖↑ = 强化；蓝色法杖↓ = 弱化）
$userUp   = Join-Path $OutDir 'ai-output-1791554533676.png'
$userDown = Join-Path $OutDir 'ai-output-1791554525475.png'
if (Test-Path -LiteralPath $userUp) {
  Copy-Item -LiteralPath $userUp -Destination (Join-Path $OutDir 'icon_domain_magic_up.png') -Force
  Write-Output "复制  用户跑的图 -> icon_domain_magic_up.png"
}
if (Test-Path -LiteralPath $userDown) {
  Copy-Item -LiteralPath $userDown -Destination (Join-Path $OutDir 'icon_domain_magic_down.png') -Force
  Write-Output "复制  用户跑的图 -> icon_domain_magic_down.png"
}

# 3) 法术 guard（橙黄） / vuln（暗红）：对巴士图标改色
#    蓝盾 H≈205 -> 橙黄 H≈45：色相 -160
Recolor-Icon 'Protection\Protection.png' 'icon_domain_magic_guard.png' -160.0 1.0 1.0
#    Vulnerable 本身是紫盾（H≈285）-> 暗红：色相 +80（转红）+ 压暗 + 加饱和
Recolor-Icon 'Vulnerable\Vulnerable.png' 'icon_domain_magic_vuln.png' 80.0 1.2 0.68

Write-Output ""
Write-Output "图标已就绪：$OutDir"
Write-Output "（每回合造成伤害那两条不需要图标）"
