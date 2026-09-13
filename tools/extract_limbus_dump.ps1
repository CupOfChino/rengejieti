# 边狱巴士"官方特效参数"提取：把特效相关的材质 / 预制体 dump 成 JSON。
#
# 为什么要这趟：主提取脚本只导了贴图（tex2d/sprite），没有材质和预制体，
# 所以"某个特效用了哪张贴图、什么颜色、怎么叠"是看不到的。
# 这趟走 dump 模式，把 FXv2 材质、特效预制体、SD 小人（含身上挂的特效）导成 JSON。
#
# 注意：AssetStudio 的 --decompress-to-disk 会在包**旁边**写临时文件，
# 所以这个脚本必须在沙箱外（提权）跑；分批 + 断点续跑，和主提取脚本一个思路。
#
# 用法：
#   powershell -File extract_limbus_dump.ps1                 # 全量（1460 个包，约 2 小时）
#   powershell -File extract_limbus_dump.ps1 -MaxBundles 40  # 先试跑 40 个包
#   powershell -File extract_limbus_dump.ps1 -Reset          # 清空进度重跑

param(
  [int]$BatchSize = 10,
  [int]$MaxBundles = 0,
  [switch]$Reset,
  [string]$CacheDir = 'D:\limbus_Data\ProjectMoon_LimbusCompany',
  [string]$OutDir   = 'E:\lim\_save\_dump',
  [string]$StageDir = 'D:\limbus_Data\_dump_stage',
  [string]$LogDir   = 'E:\lim\_save\_dump_progress',
  [int]$MinFreeMemMB = 1500
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe      = Join-Path $RepoRoot 'tools-external\AssetStudioCLI\AssetStudioModCLI_net472_win32_64\AssetStudioModCLI.exe'
$LogFile  = Join-Path (Split-Path -Parent $LogDir) 'dump.log'
$UnityVer = '6000.3.12f1'

# 只要特效相关的：FXv2 材质/着色器、特效预制体、SD 小人（身上挂着特效）、各类特效挂件
$Filter = 'V2_Material|Grp_Mat|V2_Shader|Prefab/Effect|Prefab/Battle|Prefab/SD|UserInfoEffect'

function Write-Log([string]$msg) {
  $line = ('[{0}] {1}' -f (Get-Date -Format 'MM-dd HH:mm:ss'), $msg)
  Write-Output $line
  Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
}

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

  $free = Get-FreeMemMB
  while ($free -ge 0 -and $free -lt $MinFreeMemMB) {
    Write-Log ("可用内存仅 {0} MB（低于 {1} MB），等 60 秒再看…" -f $free, $MinFreeMemMB)
    Start-Sleep -Seconds 60
    $free = Get-FreeMemMB
  }

  $argList = @($stage, '-m', 'dump', '-t', 'all', '-o', $OutDir,
               '--filter-by-text', $Filter, '--filter-with-regex',
               '--unity-version', $UnityVer, '--max-export-tasks', '1',
               '--decompress-to-disk', '--log-level', 'error')
  $p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -WindowStyle Hidden
  $peak = 0
  while (-not $p.HasExited) {
    try { $p.Refresh(); if ($p.WorkingSet64 -gt $peak) { $peak = $p.WorkingSet64 } } catch {}
    Start-Sleep -Milliseconds 500
  }
  $sw.Stop()

  # 临时区随手清掉，别占空间
  Remove-Item -LiteralPath $stage -Recurse -Force
  New-Item -ItemType File -Path $marker -Force | Out-Null

  $done = (Get-ChildItem -LiteralPath $LogDir -File -Filter '*.done').Count
  $sizeMB = [math]::Round((Get-ChildItem -LiteralPath $OutDir -Recurse -File -ErrorAction SilentlyContinue |
                           Measure-Object Length -Sum).Sum / 1MB, 0)
  Write-Log ('批 {0}/{1} 完成：{2:N1} 秒，内存峰值 {3:N0} MB，累计产出 {4} MB，进度 {5}/{6}' -f `
             ($b + 1), $batches, $sw.Elapsed.TotalSeconds, ($peak / 1MB), $sizeMB, $done, $batches)
}

$swAll.Stop()
Write-Log ("全部完成，总耗时 {0:N1} 分钟。输出目录：{1}" -f $swAll.Elapsed.TotalMinutes, $OutDir)
