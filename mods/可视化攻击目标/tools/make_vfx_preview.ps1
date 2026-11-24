# 生成离线预览页面。订阅模型只嵌入在 temp 下的本机页面中。
param(
  [string]$MoliFrame = 'E:\SteamLibrary\steamapps\workshop\content\1477070\3253405563\ResourcesFramework\Input\all\1-1.png'
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$images = Join-Path $modRoot 'assets\Particles'
$paths = [ordered]@{
  model    = $MoliFrame
  petal    = (Join-Path $images 'moli-pink-petal.png')
  round    = (Join-Path $images 'moli-pink-petal-rounded.png')
  curled   = (Join-Path $images 'moli-pink-petal-curled.png')
  mist     = (Join-Path $images 'moli-pink-floral-mist.png')
  wind     = (Join-Path $images 'moli-white-airflow.png')
  lockMark = (Join-Path $images 'jingji-target-lock.png')
  thrust   = (Join-Path $images 'jingji-mint-thrust.png')
  slash    = (Join-Path $images 'zanghua-mint-slash.png')
  pale     = (Join-Path $images 'zanghua-pale-petal.png')
}

$assets = [ordered]@{}
foreach ($pair in $paths.GetEnumerator()) {
  $path = (Resolve-Path -LiteralPath $pair.Value).ProviderPath
  $assets[$pair.Key] = @{
    url   = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    point = $pair.Key -in 'model','petal','round','curled','lockMark','pale'
  }
}

$defaultsFile = Join-Path $modRoot 'assets\VfxPlacement.defaults.json'
$defaults = Get-Content -LiteralPath $defaultsFile -Raw -Encoding UTF8 | ConvertFrom-Json
$bundle = @{ defaults = $defaults; assets = $assets } | ConvertTo-Json -Depth 8 -Compress
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vfx_preview.html') -Raw -Encoding UTF8
$marker = '__VFX_PREVIEW_BUNDLE__'
if ([regex]::Matches($template, $marker).Count -ne 1) { throw '预览页模板缺少唯一的数据标记' }
$output = Join-Path $modRoot 'temp\特效预览器.html'
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
[IO.File]::WriteAllText($output, $template.Replace($marker, $bundle), [Text.UTF8Encoding]::new($false))
Write-Output $output
