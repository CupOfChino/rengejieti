# 编译「可视化攻击目标」插件。
#
# 用法：
#   powershell -File tools\build.ps1
#   powershell -File tools\build.ps1 -InstallHost 3801085814     # 编完挂到某个已启用 mod 上做本地测试
#   powershell -File tools\build.ps1 -UninstallHost 3801085814   # 测完撤掉
#
# 为什么直接用 csc 而不是 csproj：插件要引用游戏自己的程序集（Mono 那套），
# 直接用 SDK 里的 Roslyn 编译器 + 显式引用列表最省事，也不用联网拉包。
# 这套写法和「自定义心」的 build.ps1 一样，细节见 ..\..\docs\AI协作文档.md 第 10 节。

param(
  [string]$InstallHost = "",
  [string]$UninstallHost = "",
  [string]$GameManaged = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\Managed',
  [string]$BepInExCore  = 'E:\SteamLibrary\steamapps\workshop\content\1477070\2925170762\BepInEx\core',
  [string]$WorkshopDir  = 'E:\SteamLibrary\steamapps\workshop\content\1477070'
)

$ErrorActionPreference = 'Stop'

$ModRoot  = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ModRoot)
$SrcDir   = Join-Path $ModRoot 'src'
$OutDir   = Join-Path $ModRoot 'plugins'
$SdkDir   = Join-Path $RepoRoot 'tools-external\dotnet'
$Csc      = Join-Path $SdkDir 'sdk\8.0.425\Roslyn\bincore\csc.dll'
$Dotnet   = Join-Path $SdkDir 'dotnet.exe'

$PluginFileName = 'AttackTargetVisualizer.dll'
$OutDll         = Join-Path $OutDir $PluginFileName

# ---- 撤销本地测试挂载 ----
if ($UninstallHost) {
  $hostDir = Join-Path $WorkshopDir $UninstallHost
  $target  = Join-Path (Join-Path $hostDir 'plugins') $PluginFileName
  if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Force
    Write-Output "已卸载插件：$target"
  } else {
    Write-Output "没有找到挂载点：$target"
  }
  if (-not $InstallHost) { return }
}

# ---- 检查依赖 ----
foreach ($p in @($Csc, $Dotnet, $GameManaged, $BepInExCore)) {
  if (-not (Test-Path -LiteralPath $p)) { throw "找不到：$p" }
}

# ---- 编译 ----
$sources = Get-ChildItem -LiteralPath $SrcDir -File -Filter '*.cs' | Sort-Object Name
if ($sources.Count -eq 0) { throw "src 目录里没有 .cs 文件：$SrcDir" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$cscArgs = @($Csc, '-target:library', '-nostdlib+', '-noconfig', '-langversion:latest', "-out:$OutDll")

# 游戏程序集 + BepInEx 程序集全量引用；0Harmony20 和 0Harmony 里的类型会撞车，排掉旧的
foreach ($dir in @($GameManaged, $BepInExCore)) {
  foreach ($f in (Get-ChildItem -LiteralPath $dir -File -Filter '*.dll' |
                  Where-Object { $_.Name -ne '0Harmony20.dll' })) {
    $cscArgs += "-r:$($f.FullName)"
  }
}
foreach ($s in $sources) { $cscArgs += $s.FullName }

& $Dotnet exec @cscArgs 2>&1 | Select-Object -Last 30
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

$dll = Get-Item -LiteralPath $OutDll
Write-Output ("编译成功：{0}（{1} 字节，{2:HH:mm:ss}）" -f $dll.Name, $dll.Length, $dll.LastWriteTime)

# ---- 挂到宿主 mod 上做本地测试 ----
if ($InstallHost) {
  $hostDir = Join-Path $WorkshopDir $InstallHost
  if (-not (Test-Path -LiteralPath $hostDir)) { throw "宿主 mod 不存在：$hostDir" }
  $targetDir = Join-Path $hostDir 'plugins'
  New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
  Copy-Item -LiteralPath $OutDll -Destination (Join-Path $targetDir $PluginFileName) -Force
  Write-Output "已挂插件：$InstallHost\plugins\$PluginFileName"
  Write-Output '改完记得重启游戏才会生效。'
}
