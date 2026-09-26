# 用阿里云百炼（DashScope）的 qwen-image 系列模型生成图片。
#
# 为什么要这个脚本：mod 里要用自制素材（图标、贴图）时，与其手搓，
# 不如直接调图像模型出一张，再用别的脚本做后处理（缩放、打包成游戏认的三件套）。
#
# 用法：
#   powershell -File tools\qwen_image.ps1 -Prompt "一朵白色百合花，像素风图标，透明背景" -OutFile "mods\可视化攻击目标\temp\图标待导入\icon_xxx.png"
#   powershell -File tools\qwen_image.ps1 -Prompt "..." -OutFile "..." -Mode DashScope -Size 2048x2048
#
# 两种接口（2026-09-22 实测都能用）：
#   -Mode Compat     （默认）OpenAI 兼容：<BaseUrl>/compatible-mode/v1/images/generations
#                    body = { model, prompt, n, size }，图片在 data[0].url；1024x1024 大约 700KB
#   -Mode DashScope  原生：<BaseUrl>/api/v1/services/aigc/multimodal-generation/generation
#                    body = { model, input.messages..., parameters }，图片在 output.choices[0].message.content[].image
#
# API Key 从哪来（按优先级）：
#   1. -ApiKey 参数（临时用，不建议，会留在命令历史里）
#   2. 环境变量 DASHSCOPE_API_KEY
#   3. tools\qwen_api_key.txt —— 一行纯文本，**这个文件不进 git**（见仓库根 .gitignore）
#
# 常见返回：
#   · 成功 → 自动下载到 -OutFile 并打印图片 URL（URL 有时效，要留存就赶紧下）；
#   · 401 InvalidApiKey → key 不对或过期；
#   · 超时 → 出图一般 20~60 秒，慢的时候调 -TimeoutSec。
#
# 提示词小抄（要透明背景就明写，qwen-image 能出真透明 PNG）：
#   "……像素风格游戏道具图标，深色硬描边，构图居中，纯透明背景，没有文字"

param(
  [Parameter(Mandatory = $true)][string]$Prompt,
  [Parameter(Mandatory = $true)][string]$OutFile,
  [ValidateSet('Compat', 'DashScope')][string]$Mode = 'Compat',
  [string]$Model = 'qwen-image-3.0-pro',
  [string]$Size = '1024x1024',
  [string]$ApiKey = '',
  [string]$BaseUrl = 'https://ws-hh2fer584bfwnrvk.cn-beijing.maas.aliyuncs.com',
  [int]$TimeoutSec = 300,
  [switch]$NoPromptExtend
)

$ErrorActionPreference = 'Stop'

# ---- 找 key ----
if (-not $ApiKey) { $ApiKey = $env:DASHSCOPE_API_KEY }
if (-not $ApiKey) {
  $keyFile = Join-Path $PSScriptRoot 'qwen_api_key.txt'
  if (Test-Path -LiteralPath $keyFile) {
    $ApiKey = (Get-Content -LiteralPath $keyFile -Encoding UTF8 -TotalCount 1).Trim()
  }
}
if (-not $ApiKey) {
  throw "没有找到 API Key。请三选一：`n  1) 设环境变量 DASHSCOPE_API_KEY`n  2) 把 key 写进 $PSScriptRoot\qwen_api_key.txt（一行）`n  3) 用 -ApiKey 参数传"
}

Write-Output ("接口：{0}   模型：{1}" -f $Mode, $Model)
Write-Output ("提示词：{0}" -f $Prompt)
Write-Output '正在生成（一般 20~60 秒）……'

$headers = @{ Authorization = "Bearer $ApiKey" }
$url = $null

if ($Mode -eq 'Compat') {
  $body = @{ model = $Model; prompt = $Prompt; n = 1; size = $Size } | ConvertTo-Json -Depth 10
  $resp = Invoke-RestMethod -Uri "$BaseUrl/compatible-mode/v1/images/generations" -Method Post `
    -Headers $headers -ContentType 'application/json; charset=utf-8' `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $TimeoutSec
  if ($resp.data -and $resp.data.Count -gt 0) { $url = $resp.data[0].url }
} else {
  $body = @{
    model      = $Model
    input      = @{ messages = @(@{ role = 'user'; content = @(@{ text = $Prompt }) }) }
    parameters = @{ prompt_extend = (-not $NoPromptExtend) }
  } | ConvertTo-Json -Depth 10
  $resp = Invoke-RestMethod -Uri "$BaseUrl/api/v1/services/aigc/multimodal-generation/generation" -Method Post `
    -Headers $headers -ContentType 'application/json; charset=utf-8' `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $TimeoutSec
  try {
    foreach ($c in $resp.output.choices[0].message.content) {
      if ($c.image) { $url = $c.image; break }
    }
  } catch { }
}

if (-not $url) {
  Write-Output '=== 没拿到图片 URL，接口原始返回如下 ==='
  $resp | ConvertTo-Json -Depth 10
  throw '接口没有返回图片（看上面的原始返回判断原因）'
}

# ---- 下载 ----
$outDir = Split-Path -Parent $OutFile
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
Invoke-WebRequest -Uri $url -OutFile $OutFile -TimeoutSec $TimeoutSec

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($OutFile)
Write-Output ("已保存：{0}（{1}×{2}，{3} 字节）" -f $OutFile, $img.Width, $img.Height, (Get-Item -LiteralPath $OutFile).Length)
$img.Dispose()
Write-Output ("图片 URL（有时效，想留存就赶紧另存）：{0}" -f $url)
