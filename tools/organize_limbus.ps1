# 把提取出来的原始目录，整理成"按角色归组"的素材库
#
# 规则：
#   Prefab/SD/<...>Appearance      → 素材库\<角色>\SD\<角色立绘名>\
#   Prefab/Battle/.../SkillViewEGO_<ID> → 素材库\<角色>\特效\<名称>\   （ID 第 2~3 位是角色编号）
#   Prefab/Battle 其他              → 素材库\_通用特效\
#   Buf\<buff名>                    → 素材库\_状态图标\<buff名>\
#   Sprite / DUI / Assets           → 素材库\_图标与UI\<原路径>\
#   Prefab/SD 里的 NPC/敌人         → 素材库\_NPC与敌人\
#
# 用法：
#   powershell -File organize_limbus.ps1 -DryRun     # 只出报表，不动文件
#   powershell -File organize_limbus.ps1            # 真正整理（移动文件夹，很快）

param(
  [string]$RawDir  = 'E:\lim\_save\_raw\Assets\Resources_moved',
  [string]$LibDir  = 'E:\lim\_save\素材库',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# 罪人编号 → 英文名（整理时若从资源名里读不到就用这个兜底）
$SinnerName = @{
  1 = 'YiSang'; 2 = 'Faust'; 3 = 'DonQuixote'; 4 = 'Ryoshu'; 5 = 'Meursault'; 6 = 'Honglu'
  7 = 'Heathcliff'; 8 = 'Ishmael'; 9 = 'Rodion'; 10 = 'Sinclair'; 11 = 'Outis'; 12 = 'Gregor'
}

function Get-CharCode([string]$text) {
  # 只认"罪人"编号：
  #   10409 / 10105   身份编号      = 1 + 角色(2位) + 身份(2位)
  #   20606 / 2110721 EGO 编号      = 2 + 角色(2位) + ...
  # 其余（8105、8366、90046、400038…）是敌人/NPC，不当作角色
  $m = [regex]::Match($text, '(\d{5,8})')
  if (-not $m.Success) { return $null }
  $d = $m.Groups[1].Value
  if ($d[0] -ne '1' -and $d[0] -ne '2') { return $null }
  $code = [int]$d.Substring(1, 2)
  if ($code -ge 1 -and $code -le 12) { return $code }
  return $null
}

function Get-SinnerFolderName([int]$code, [string]$hint) {
  # hint 形如 10409_Ryoshu_YuroAppearance，可抽出英文名
  if ($hint) {
    $m = [regex]::Match($hint, '^\d+_([A-Za-z][A-Za-z0-9_]*)')
    if ($m.Success) { return ('{0:D2}_{1}' -f $code, $m.Groups[1].Value) }
  }
  return ('{0:D2}_{1}' -f $code, $SinnerName[$code])
}

if (-not (Test-Path -LiteralPath $RawDir)) { throw "找不到原始目录：$RawDir" }
if (-not $DryRun) { New-Item -ItemType Directory -Force -Path $LibDir | Out-Null }

$plan = New-Object System.Collections.Generic.List[object]

function Add-Plan([string]$src, [string]$dstFolder, [string]$bucket) {
  $plan.Add([PSCustomObject]@{ Src = $src; DstFolder = $dstFolder; Bucket = $bucket })
}

# 取"直接含文件的叶子文件夹"，并排除已被上层选中过的子目录，避免重复搬运
function Get-LeafFolders([string]$root) {
  $all = Get-ChildItem -LiteralPath $root -Recurse -Directory -ErrorAction SilentlyContinue
  $picked = New-Object System.Collections.Generic.List[string]
  foreach ($d in ($all | Sort-Object { $_.FullName.Length })) {
    $hasFile = (Get-ChildItem -LiteralPath $d.FullName -File -ErrorAction SilentlyContinue).Count -gt 0
    if (-not $hasFile) { continue }
    $covered = $false
    foreach ($p in $picked) { if ($d.FullName.StartsWith($p + '\')) { $covered = $true; break } }
    if (-not $covered) { $picked.Add($d.FullName) }
  }
  return $picked
}

# ---- 1) SD 小人 ----
$sdRoot = Join-Path $RawDir 'Prefab\SD'
if (Test-Path -LiteralPath $sdRoot) {
  # 直接挂着资源文件的叶子文件夹（一个 prefab 一个文件夹）
  foreach ($leafPath in (Get-LeafFolders $sdRoot)) {
    $leaf = Get-Item -LiteralPath $leafPath
    $code = Get-CharCode $leaf.Name
    if ($code) {
      $charName = Get-SinnerFolderName $code $leaf.Name
      $dst = Join-Path $LibDir (Join-Path $charName 'SD')
      Add-Plan $leaf.FullName $dst "SD/$charName"
    } else {
      Add-Plan $leaf.FullName (Join-Path $LibDir '_NPC与敌人\SD') 'SD/NPC'
    }
  }
}

# ---- 2) 战斗特效 ----
$battleRoot = Join-Path $RawDir 'Prefab\Battle'
if (Test-Path -LiteralPath $battleRoot) {
  foreach ($leafPath in (Get-LeafFolders $battleRoot)) {
    $leaf = Get-Item -LiteralPath $leafPath
    $code = Get-CharCode $leaf.Name
    if ($code -and $leaf.Name -match 'SkillViewEGO|EgoSkill|Personality') {
      $charName = Get-SinnerFolderName $code $null
      Add-Plan $leaf.FullName (Join-Path $LibDir (Join-Path $charName '特效\EGO技能特效')) "FX/$charName"
    } else {
      Add-Plan $leaf.FullName (Join-Path $LibDir '_通用特效') 'FX/通用'
    }
  }
}

# ---- 3) 状态图标 buff / 4) 图标与UI ----
foreach ($pair in @(
    @{ Src = 'Buf'; Dst = '_状态图标'; Bucket = 'Buf' },
    @{ Src = 'Sprite'; Dst = '_图标与UI\Sprite'; Bucket = 'Icon' },
    @{ Src = 'DUI'; Dst = '_图标与UI\DUI'; Bucket = 'Icon' },
    @{ Src = 'Assets'; Dst = '_图标与UI\Assets'; Bucket = 'Icon' }
  )) {
  $root = Join-Path $RawDir $pair.Src
  if (-not (Test-Path -LiteralPath $root)) { continue }
  foreach ($leafPath in (Get-LeafFolders $root)) {
    $leaf = Get-Item -LiteralPath $leafPath
    $rel = $leaf.FullName.Substring($root.Length).TrimStart('\')
    $parent = Split-Path -Parent $rel
    $sub = ''
    if ($parent) { $sub = $parent }
    $dst = Join-Path $LibDir (Join-Path $pair.Dst $sub)
    Add-Plan $leaf.FullName $dst $pair.Bucket
  }
  # 直接躺在根下的散文件
  $rootFiles = Get-ChildItem -LiteralPath $root -File -ErrorAction SilentlyContinue
  if ($rootFiles) { Add-Plan $root (Join-Path $LibDir $pair.Dst) $pair.Bucket }
}

# ---- 输出报表 / 执行 ----
Write-Output ('共 {0} 个来源文件夹' -f $plan.Count)
Write-Output '=== 按目标分类统计 ==='
$plan | Group-Object Bucket | Sort-Object Count -Descending |
  Select-Object Count, Name | Format-Table -AutoSize | Out-String -Width 120

if ($DryRun) {
  Write-Output '（DryRun：没有移动任何文件）'
  Write-Output '=== 前 15 条计划 ==='
  $plan | Select-Object -First 15 @{n='来源';e={$_.Src.Replace($RawDir,'')}}, @{n='去向';e={$_.DstFolder.Replace($LibDir,'')}} |
    Format-Table -AutoSize | Out-String -Width 200
  exit 0
}

$moved = 0; $skipped = 0; $failed = 0
$failList = New-Object System.Collections.Generic.List[string]
foreach ($item in $plan) {
  if (-not (Test-Path -LiteralPath $item.Src)) { $skipped++; continue }
  New-Item -ItemType Directory -Force -Path $item.DstFolder | Out-Null
  $target = Join-Path $item.DstFolder (Split-Path -Leaf $item.Src)
  if (Test-Path -LiteralPath $target) { $target = $target + '_' + (Get-Random -Maximum 9999) }
  try {
    Move-Item -LiteralPath $item.Src -Destination $target -Force -ErrorAction Stop
    $moved++
  } catch {
    # 单个搬不动不要拖垮整轮，记下来最后一并报告
    $failed++
    $failList.Add(('{0}  =>  {1}  :  {2}' -f $item.Src, $item.DstFolder, $_.Exception.Message))
  }
}
Write-Output ("整理完成：移动 {0} 个，跳过 {1} 个，失败 {2} 个。素材库：{3}" -f $moved, $skipped, $failed, $LibDir)
if ($failed -gt 0) {
  Write-Output '=== 搬不动的（前 20 条）==='
  $failList | Select-Object -First 20 | ForEach-Object { Write-Output ('  ' + $_) }
}
