# 检查注册表 InternalConfigure.txt 和实际图片文件对不对得上。
#
# 为什么需要它：游戏编辑器导入 PNG 时会**重建**注册表，但它只认得
# Texture\Item\ 与 Texture\Buff\ 两个目录 —— Texture\BuffIcon\ 和 Texture\ExtraAnim\
# 里的条目会被静默抹掉（2026-09-24 导入「荆棘」图标时踩到，丢了 5 条：
# secret_shuangjian_icon / zanghua_hen_01 / secret_heart_01~03）。
# 丢了之后游戏不报错，只是那些图标和帧动画不显示，很难往注册表上想。
#
# 用法：powershell -File tools\verify_resource_map.ps1

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$config = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\InternalConfigure.txt'
$assets = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources'
if (-not (Test-Path -LiteralPath $config)) { throw "找不到注册表：$config" }

$text = Get-Content -LiteralPath $config -Raw -Encoding UTF8

# ---- 1. 注册表里登记了什么 ----
$entries = @()
foreach ($m in [regex]::Matches($text, '\{\s*"Key"\s*:\s*"([^"]+)"\s*,\s*"Path"\s*:\s*"([^"]+)"\s*,\s*"Ext"\s*:\s*"([^"]*)"\s*\}')) {
  $entries += [pscustomobject]@{
    Key  = $m.Groups[1].Value
    Path = $m.Groups[2].Value -replace '\\/', '/'
    Ext  = $m.Groups[3].Value
  }
}

# ---- 2. 注册表里每一条，对应的文件在不在 ----
$missing = @()
foreach ($e in $entries) {
  $rel = $e.Path -replace '^Resources/', ''
  $file = Join-Path $assets (($rel + $e.Ext) -replace '/', '\')
  if (-not (Test-Path -LiteralPath $file)) { $missing += $e }
}

# ---- 3. 目录里有哪些图片（用来提示"有文件但没登记"）----
$onDisk = @()
Get-ChildItem $assets -Recurse -File -Filter '*.png' | ForEach-Object {
  # 跳过 .rtmeta / .rtview（它们不是图片本体）
  if ($_.Name -notmatch '\.rt(meta|view)$') {
    $onDisk += ($_.FullName.Substring($assets.Length + 1) -replace '\\', '/')
  }
}
$registered = $entries | ForEach-Object { (($_.Path -replace '\\/', '/') -replace '^Resources/', '') + $_.Ext }
$notRegistered = $onDisk | Where-Object { $registered -notcontains $_ }

Write-Output ("注册表条目：{0} 条｜目录里的图片：{1} 张" -f $entries.Count, $onDisk.Count)

if ($missing.Count -eq 0) {
  Write-Output '注册表 -> 文件：全部命中'
} else {
  Write-Output ('注册表 -> 文件：**{0} 条找不到文件**（这些图标/贴图在游戏里会空白）' -f $missing.Count)
  foreach ($e in $missing) { Write-Output ('   {0}  ->  {1}{2}' -f $e.Key, $e.Path, $e.Ext) }
}

if ($notRegistered.Count -gt 0) {
  Write-Output '下面这些图片没登记（备用图是正常的，如果本该显示却空白就来这里找）：'
  foreach ($n in $notRegistered) { Write-Output ('   {0}' -f $n) }
} else {
  Write-Output '目录 -> 注册表：没有漏登记的图片'
}
