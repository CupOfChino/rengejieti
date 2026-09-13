# 把 FXv2（特效贴图/材质/着色器）整理进素材库，并把地图背景从"_通用特效"里分出去。
#
# 背景：主整理脚本 organize_limbus.ps1 只管 Resources_moved 那一坨，
# 特效零件图在 Assets\FXv2 下，之前没进库，所以"通用特效"里看起来全是地图。
#
# 用法： powershell -File organize_limbus_fx.ps1            # 真整理
#        powershell -File organize_limbus_fx.ps1 -DryRun    # 只看计划

param(
  [string]$RawRoot = 'E:\lim\_save\_raw\Assets',
  [string]$LibDir  = 'E:\lim\_save\素材库',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$fxRoot = Join-Path $RawRoot 'FXv2'
$mapDir = Join-Path $LibDir '_地图背景'

$jobs = @()

# FXv2 下每个子目录整体搬走：V2_Texture→_特效贴图，V2_Material→_特效材质，V2_Shader→_特效着色器
$map = @{ 'V2_Texture' = '_特效贴图'; 'V2_Material' = '_特效材质'; 'V2_Shader' = '_特效着色器' }
if (Test-Path -LiteralPath $fxRoot) {
  foreach ($k in $map.Keys) {
    $src = Join-Path $fxRoot $k
    if (-not (Test-Path -LiteralPath $src)) { continue }
    foreach ($sub in (Get-ChildItem -LiteralPath $src -Directory)) {
      $jobs += [pscustomobject]@{ Src = $sub.FullName; Dst = (Join-Path (Join-Path $LibDir $map[$k]) $sub.Name) }
    }
    # 该分类下直接躺着的散文件，也一起收进去
    $loose = Get-ChildItem -LiteralPath $src -File -ErrorAction SilentlyContinue
    if ($loose.Count -gt 0) {
      $jobs += [pscustomobject]@{ Src = $null; Dst = (Join-Path $LibDir $map[$k]); Files = $loose }
    }
  }
}

# 把地图背景从"通用特效"里分出来
$genDir = Join-Path $LibDir '_通用特效'
if (Test-Path -LiteralPath $genDir) {
  foreach ($d in (Get-ChildItem -LiteralPath $genDir -Directory | Where-Object { $_.Name -like 'BattleMapPreset*' })) {
    $jobs += [pscustomobject]@{ Src = $d.FullName; Dst = (Join-Path $mapDir $d.Name) }
  }
}

Write-Output ("计划搬 {0} 个目录" -f $jobs.Count)
if ($DryRun) {
  $jobs | Select-Object -First 15 | ForEach-Object { Write-Output ("  {0}  ->  {1}" -f $_.Src, $_.Dst) }
  exit 0
}

$ok = 0; $fail = 0
foreach ($j in $jobs) {
  try {
    if ($j.Src) {
      New-Item -ItemType Directory -Force -Path (Split-Path -Parent $j.Dst) | Out-Null
      Move-Item -LiteralPath $j.Src -Destination $j.Dst -Force -ErrorAction Stop
    } else {
      New-Item -ItemType Directory -Force -Path $j.Dst | Out-Null
      foreach ($f in $j.Files) { Move-Item -LiteralPath $f.FullName -Destination (Join-Path $j.Dst $f.Name) -Force }
    }
    $ok++
  } catch {
    $fail++
    Write-Output ("搬不动：{0}（{1}）" -f $j.Src, $_.Exception.Message)
  }
}
Write-Output ("整理完成：成功 {0}，失败 {1}" -f $ok, $fail)
