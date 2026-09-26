# 把一张 PNG 打成"图标三件套"（<key>.png + <key>.png.rtmeta + <key>.png.rtview）。
#
# 关键（2026-09-22 回读代码后确认，改前必看）：运行时是**按注册表里的 Ext 决定怎么找文件**的 ——
#   · Ext = ".png" → 直接读 `<mod>\Project_Depersonal\Assets\<Path>.png`（内部数据文件的引用类型是 1004/1007 这类）
#   · Ext = ""（空）或含 "rt" → 走 RTE，才去读 .rtmeta / .rtview 那套
# 所以图标能不能显示，**取决于有没有那张实体 .png**。
# 之前这份脚本只生成 `.txt` 三件套（而不写 .png），注册表里 Ext 又填的 ".png" →
# 运行时找不到文件，图标就是空白（2026-09-22 用户重新导入 PNG 后才修好）。
#
# 做法：拿游戏本体某个现成图标的三件套当模板（默认 icon_item_qiangweiheijian），
# 把 PNG 本体抄过去、把 .rtview 里的 PNG 数据换掉并重算长度；.rtmeta / .rtview 是给
# 游戏编辑器认的（编辑器导出的就是这个格式），运行时用不上，但在旁边放着没有副作用。
#
# 用法：
#   powershell -File tools\make_zanghua_icon_assets.ps1 -SourcePng <png路径> -Key icon_item_moli_jian

param(
  [Parameter(Mandatory = $true)][string]$SourcePng,
  [string]$Key = 'icon_item_moli_jian',
  [string]$TemplateKey = 'icon_item_qiangweiheijian',
  [string]$TemplateDir = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\StreamingAssets\DLCUGCProject\MDJX\Project_Depersonal\Assets\Resources\Texture\Item',
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$OutSubDir = 'Project_Depersonal\Assets\Resources\Texture\Item'
)

$ErrorActionPreference = 'Stop'

function Get-VarintBytes([long]$v) {
  $list = New-Object System.Collections.Generic.List[byte]
  while ($true) {
    $b = [byte]($v -band 0x7F)
    $v = $v -shr 7
    if ($v -gt 0) { $b = $b -bor 0x80 }
    $list.Add($b)
    if ($v -le 0) { break }
  }
  return $list.ToArray()
}

$tplView = [System.IO.File]::ReadAllBytes((Join-Path $TemplateDir "$TemplateKey.txt.rtview"))
$tplMeta = [System.IO.File]::ReadAllBytes((Join-Path $TemplateDir "$TemplateKey.txt.rtmeta"))
$png = [System.IO.File]::ReadAllBytes($SourcePng)

# ---- 1. .png：图标本体（运行时按 Ext=".png" 直接读它，这张才是真正要用的）----
$outDir = Join-Path $ModRoot $OutSubDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $outDir "$Key.png"), $png)
Write-Output ("  {0}.png（{1} 字节）" -f $Key, $png.Length)

# ---- 2. .rtmeta：模板原样抄（41 字节，缓存/时间戳信息）----
[System.IO.File]::WriteAllBytes((Join-Path $outDir "$Key.png.rtmeta"), $tplMeta)
Write-Output ("  {0}.png.rtmeta（{1} 字节，抄模板）" -f $Key, $tplMeta.Length)

# ---- 3. .rtview：模板前 11 字节 + 重算的长度 + 我们的 PNG ----
# 模板结构： 08 <varint field1> 12 <varint 长度> <PNG 数据>
$prefixLen = 0
for ($i = 0; $i -lt $tplView.Length; $i++) {
  if ($tplView[$i] -eq 0x89 -and $tplView[$i + 1] -eq 0x50 -and $tplView[$i + 2] -eq 0x4E -and $tplView[$i + 3] -eq 0x47) { $prefixLen = $i; break }
}
if ($prefixLen -le 0) { throw "模板 .rtview 里找不到 PNG 起点" }

$head = New-Object System.Collections.Generic.List[byte]
# field1 原样保留（模板里 08 后面那串 varint，到 0x12 为止）
$i = 0
$head.Add($tplView[$i]); $i++                      # 08
while ($i -lt $prefixLen -and $tplView[$i] -ne 0x12) { $head.Add($tplView[$i]); $i++ }
$head.Add(0x12)                                    # 12 = field2 起始
foreach ($b in (Get-VarintBytes $png.Length)) { $head.Add($b) }

$view = New-Object System.Collections.Generic.List[byte]
$view.AddRange($head)
$view.AddRange($png)
[System.IO.File]::WriteAllBytes((Join-Path $outDir "$Key.png.rtview"), $view.ToArray())
Write-Output ("  {0}.png.rtview（头 {1} 字节 + PNG {2} 字节 = {3}）" -f $Key, $head.Count, $png.Length, $view.Count)

Write-Output ("已输出到：{0}" -f $outDir)
