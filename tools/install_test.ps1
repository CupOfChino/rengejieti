# 本地测试用：把一个还没上传的 mod 临时挂到游戏里"已启用的 mod"下面。
# 为什么需要：游戏只加载 Steam 已订阅且已启用的 mod 的数据内容，没上传的本地内容直接丢进
# 创意工坊目录不生效（详见 docs\AI协作文档.md 第 4 节）。
#
# 用法：
#   powershell -File install_test.ps1 -ModName 跑团卡特质包
#   powershell -File install_test.ps1 -ModName 跑团卡特质包 -HostModId 3031614456
# 还原：
#   powershell -File uninstall_test.ps1 -ModName 跑团卡特质包 -HostModId 3031614456

param(
  [Parameter(Mandatory = $true)][string]$ModName,
  [string]$HostModId = '2919360679'
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Source   = Join-Path $RepoRoot "mods\$ModName\Project_Depersonal"
$HostDir  = "E:\SteamLibrary\steamapps\workshop\content\1477070\$HostModId"
$Target   = Join-Path $HostDir 'Project_Depersonal'

if (-not (Test-Path -LiteralPath $Source))  { throw "找不到 mod 本体：$Source" }
if (-not (Test-Path -LiteralPath $HostDir)) { throw "找不到宿主 mod 目录：$HostDir" }
if (Test-Path -LiteralPath $Target) {
  Write-Output "宿主 $HostModId 里已经有 Project_Depersonal（可能是别的 mod 挂着的）。"
  Write-Output "先跑 uninstall_test.ps1 -ModName <那个mod名> -HostModId $HostModId 清掉再装。"
  exit 1
}

Copy-Item -LiteralPath $Source -Destination $Target -Recurse -Force

# 顺手把宿主 mod 原内容备份一份，方便回头核对
$Backup = Join-Path $RepoRoot "temp\宿主备份\$HostModId"
if (-not (Test-Path -LiteralPath $Backup)) {
  New-Item -ItemType Directory -Force -Path $Backup | Out-Null
  Get-ChildItem -LiteralPath $HostDir -Force | Where-Object { $_.Name -ne 'Project_Depersonal' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Backup -Recurse -Force }
  Write-Output "宿主原内容已备份到：$Backup"
}

Write-Output "已把 [$ModName] 挂到宿主 mod $HostModId"
Write-Output "…记得重启游戏才生效。"

