# 卸载本地测试挂载：删掉宿主 mod 下的 Project_Depersonal，恢复宿主 mod 原样。
# 只动 <宿主mod>\Project_Depersonal，不碰宿主 mod 原有的其他文件。

param(
  [string]$ModName    = '',
  [string]$HostModId  = '2919360679'
)

$ErrorActionPreference = 'Stop'

$HostDir = "E:\SteamLibrary\steamapps\workshop\content\1477070\$HostModId"
$Target  = Join-Path $HostDir 'Project_Depersonal'

if (-not (Test-Path -LiteralPath $Target)) { Write-Output "没有找到 $Target，无需清理。"; exit 0 }

$outside = Get-ChildItem -LiteralPath $Target -Recurse -Force -File |
  Where-Object { $_.Name -notmatch '^8800\d\d\.txt$' -and $_.Name -ne 'Project.rtmeta' }
if ($outside) {
  Write-Output "注意：这个目录里还有不属于本模组的文件，先确认清楚："
  $outside | ForEach-Object { Write-Output ("  " + $_.FullName) }
}

Remove-Item -LiteralPath $Target -Recurse -Force
Write-Output "已清理 $Target，宿主 mod $HostModId 恢复原样。"
