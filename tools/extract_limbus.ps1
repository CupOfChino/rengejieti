# 边狱巴士素材提取（分批 + 断点续跑）
#
# 为什么分批：解包工具会把加载过的资源都留在内存里，一次性跑全量 14.7GB 会吃爆内存。
# 分批后每批内存峰值约 3~4GB，中断了再跑一次会自动跳过已完成的批次。
#
# 用法：
#   powershell -File extract_limbus.ps1            # 默认每批 60 个包
#   powershell -File extract_limbus.ps1 -BatchSize 40
#   powershell -File extract_limbus.ps1 -Reset     # 清空进度重跑

param(
  [int]$BatchSize = 10,
  [switch]$Reset,
  [string]$CacheDir = 'D:\limbus_Data\ProjectMoon_LimbusCompany',
  [string]$OutDir   = 'E:\lim\_save\_raw',
  [string]$StageDir = 'D:\limbus_Data\_codex_stage',
  [string]$LogDir   = 'E:\lim\_save\_progress',
  [int]$MaxBundles  = 0,
  [int]$MinFreeMemMB = 1500
)

$ErrorActionPreference = 'Stop'

# ---- 配置 ----
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe      = Join-Path $RepoRoot 'tools-external\AssetStudioCLI\AssetStudioModCLI_net472_win32_64\AssetStudioModCLI.exe'
$LogFile  = Join-Path (Split-Path -Parent $LogDir) 'extract.log'
$UnityVer = '6000.3.12f1'

# 只要这几类：SD 小人 / 战斗特效 / 状态图标(buff) / 各类小图标 / UI 图集
# 不含：Story(CG、背景、立绘)、Sprite/Unit 立绘、Gacha、Notice
$Filter = 'Prefab/SD|Prefab/Battle|Prefab/UserInfoEffect|Buf/|Assets/FXv2|' +
          'Sprite/SkillIcon|Sprite/EgoGiftIcon|Sprite/PanicType|Sprite/UI|' +
          'Sprite/BattleAnnouncer|Sprite/Chapter|Sprite/Unit/PassiveBanner|' +
          'DUI/|Sprite/ChoiceEvent'

function Write-Log([string]$msg) {
  $line = ('[{0}] {1}' -f (Get-Date -Format 'MM-dd HH:mm:ss'), $msg)
  Write-Output $line
  Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
}

# 取当前可用物理内存（MB）；取不到返回 -1
function Get-FreeMemMB {
  try {
    $os = Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
    return [int]($os.FreePhysicalMemory / 1KB)
  } catch { return -1 }
}

if (-not (Test-Path -LiteralPath $Exe))      { throw "找不到解包工具：$Exe" }
if (-not (Test-Path -LiteralPath $CacheDir)) { throw "找不到资源缓存：$CacheDir" }
foreach ($d in @($StageDir, $OutDir, $LogDir)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

if ($Reset) {
  Write-Log '重置进度与临时区…'
  Get-ChildItem -LiteralPath $LogDir -File -Force | Remove-Item -Force
  if (Test-Path -LiteralPath $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
}

# ---- 收集并分批 ----
# 兼容两种命名：游戏缓存里是 __data，我们自己搭的样本区是 *.bundle
$bundles = Get-ChildItem -LiteralPath $CacheDir -Recurse -File |
           Where-Object { $_.Name -eq '__data' -or $_.Extension -eq '.bundle' } |
           Sort-Object FullName
if ($MaxBundles -gt 0) { $bundles = $bundles | Select-Object -First $MaxBundles }
$total   = $bundles.Count
$batches = [math]::Ceiling($total / $BatchSize)
Write-Log "共 $total 个资源包，分 $batches 批（每批 $BatchSize 个）"

$swAll = [System.Diagnostics.Stopwatch]::StartNew()
for ($b = 0; $b -lt $batches; $b++) {
  $marker = Join-Path $LogDir ("batch_{0:D3}.done" -f $b)
  if (Test-Path -LiteralPath $marker) { continue }

  $slice = $bundles | Select-Object -Skip ($b * $BatchSize) -First $BatchSize
  $stage = Join-Path $StageDir ("b{0:D3}" -f $b)
  if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $stage | Out-Null

  $i = 0
  foreach ($f in $slice) {
    $i++
    New-Item -ItemType HardLink -Path (Join-Path $stage ("x{0:D4}.bundle" -f $i)) -Target $f.FullName | Out-Null
  }

  $sw = [System.Diagnostics.Stopwatch]::StartNew()

  # 内存闸门：不够就等，避免把系统拖爆
  $free = Get-FreeMemMB
  while ($free -ge 0 -and $free -lt $MinFreeMemMB) {
    Write-Log ("可用内存仅 {0} MB（低于 {1} MB），等 60 秒再看…" -f $free, $MinFreeMemMB)
    Start-Sleep -Seconds 60
    $free = Get-FreeMemMB
  }

  $argList = @($stage, '-m', 'export', '-t', 'tex2d,sprite', '-o', $OutDir, '-g', 'containerFull',
               '--image-format', 'png', '--unity-version', $UnityVer,
               '--filter-by-text', $Filter, '--filter-with-regex',
               '--max-export-tasks', '1', '--decompress-to-disk', '--log-level', 'error')
  $p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -WindowStyle Hidden
  $peak = 0
  while (-not $p.HasExited) {
    try { $p.Refresh(); if ($p.WorkingSet64 -gt $peak) { $peak = $p.WorkingSet64 } } catch {}
    Start-Sleep -Milliseconds 500
  }
  $sw.Stop()

  Remove-Item -LiteralPath $stage -Recurse -Force
  New-Item -ItemType File -Path $marker -Force | Out-Null

  $done = (Get-ChildItem -LiteralPath $LogDir -File -Filter '*.done').Count
  $sizeMB = [math]::Round((Get-ChildItem -LiteralPath $OutDir -Recurse -File -ErrorAction SilentlyContinue |
                           Measure-Object Length -Sum).Sum / 1MB, 0)
  $msg = '批 {0}/{1} 完成：{2:N1} 秒，内存峰值 {3:N0} MB，累计产出 {4} MB，进度 {5}/{6}' -f ($b + 1), $batches, $sw.Elapsed.TotalSeconds, ($peak / 1MB), $sizeMB, $done, $batches
  Write-Log $msg
}

$swAll.Stop()
Write-Log ("全部完成，总耗时 {0:N1} 分钟。输出目录：{1}" -f $swAll.Elapsed.TotalMinutes, $OutDir)
