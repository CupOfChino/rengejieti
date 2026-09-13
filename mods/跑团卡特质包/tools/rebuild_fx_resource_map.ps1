# 重建 InternalConfigure.txt 里的 1023（额外动画与特效）注册表。
#
# 为什么需要：帧图增删之后（比如从 24 帧变 48 帧），注册表必须跟着变，
# 否则多出来的帧游戏读不到。这个脚本直接按 ExtraAnim 目录里的实际文件重建，
# 不用手数。1007（Buff 图标）那一段保持不变。
#
# 用法： powershell -File tools\rebuild_fx_resource_map.ps1

param(
  [string]$ConfigFile = '',
  [string]$TexDir = ''
)

$ErrorActionPreference = 'Stop'

$ModRoot = Split-Path -Parent $PSScriptRoot
if (-not $ConfigFile) { $ConfigFile = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\InternalConfigure.txt' }
if (-not $TexDir)     { $TexDir     = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Texture\ExtraAnim' }
if (-not (Test-Path -LiteralPath $ConfigFile)) { throw "找不到 $ConfigFile" }
if (-not (Test-Path -LiteralPath $TexDir))     { throw "找不到 $TexDir" }

$files = Get-ChildItem -LiteralPath $TexDir -File -Filter 'xin_*.png' | Sort-Object Name
if ($files.Count -eq 0) { throw "ExtraAnim 目录里没有 xin_*.png" }

# 找 1007 那一段里的图标条目，原样保留
$text = [System.IO.File]::ReadAllText($ConfigFile, (New-Object System.Text.UTF8Encoding($false)))
$iconEntry = '{ "Key" : "xin_icon", "Path" : "Resources\/Texture\/BuffIcon\/xin_icon", "Ext" : ".png" }'

$nl = "`r`n"
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append($text.Substring(0, $text.IndexOf('"ResourceMap"')))
[void]$sb.Append('"ResourceMap" : {1007:[' + $nl)
[void]$sb.Append('					' + $iconEntry + $nl)
[void]$sb.Append('				],1023:[' + $nl)
$i = 0
foreach ($f in $files) {
  $i++
  $k = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
  $comma = if ($i -lt $files.Count) { ',' } else { '' }
  [void]$sb.Append('					{ "Key" : "' + $k + '", "Path" : "Resources\/Texture\/ExtraAnim\/' + $k + '", "Ext" : ".png" }' + $comma + $nl)
}
[void]$sb.Append('				]' + $nl + '			}' + $nl + '		}' + $nl + '	}' + $nl + '}')

[System.IO.File]::WriteAllText($ConfigFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$json = Get-Content -LiteralPath $ConfigFile -Encoding UTF8 -Raw | ConvertFrom-Json
$n23 = $json.ResourceReferenceConfigure.value.ResourceMap.'1023'.Count
$n07 = $json.ResourceReferenceConfigure.value.ResourceMap.'1007'.Count
Write-Output ("重建完成：1023 注册 {0} 条（对应 {1} 个帧文件），1007 保持 {2} 条" -f $n23, $files.Count, $n07)
