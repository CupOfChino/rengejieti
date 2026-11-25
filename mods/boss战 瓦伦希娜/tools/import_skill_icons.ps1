param(
    [string]$WikiImages
)

$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$project = Join-Path $mod 'Project_Depersonal'
$folder = Join-Path $project 'Assets/Resources/Texture/SkillIcon'
$registry = Join-Path $project 'Assets/Resources/Config/InternalConfigure.txt'
$icons = @(131401..131408 | ForEach-Object {
    $size = if ($_ -eq 131408) { 55 } else { 60 }
    $key = "valenna_skill_$_"
    [pscustomobject]@{
        Source = "${size}px-$_.png"
        Key = $key
        Target = Join-Path $folder "$key.png"
        Size = $size
    }
})

# 带 Wiki 文件夹参数时导入源图。省略参数时使用已经随项目保存的 PNG。
foreach ($icon in $icons) {
    $path = if ($WikiImages) { Join-Path $WikiImages $icon.Source } else { $icon.Target }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or [Convert]::ToHexString($bytes,0,8) -ne '89504E470D0A1A0A' -or
        $bytes[19] -ne $icon.Size -or $bytes[23] -ne $icon.Size) {
        throw "技能图标格式或尺寸不符：$path"
    }
}

$mapJson = Get-Content -LiteralPath $registry -Raw
$envelope = $mapJson -replace '(?m)^(\s*)(\d+)(?=\s*:)', '$1"$2"' | ConvertFrom-Json -AsHashtable
$resourceMap = $envelope.ResourceReferenceConfigure.value.ResourceMap
$iconKeys = @($icons.Key)
$resourceMap['1006'] = @(
    $resourceMap['1006'] | Where-Object { $_ -and $_.Key -notin $iconKeys }
    foreach ($icon in $icons) {
        [ordered]@{ Key = $icon.Key; Path = "Resources/Texture/SkillIcon/$($icon.Key)"; Ext = '.png' }
    }
)
$mapJson = ($envelope | ConvertTo-Json -Depth 8) -replace '(?m)^(\s*)"(\d+)"(?=\s*:)', '$1$2'

if ($WikiImages) {
    [void][IO.Directory]::CreateDirectory($folder)
    foreach ($icon in $icons) {
        $source = Join-Path $WikiImages $icon.Source
        if ((Test-Path -LiteralPath $icon.Target) -and
            (Get-FileHash -LiteralPath $icon.Target).Hash -eq (Get-FileHash -LiteralPath $source).Hash) {
            continue
        }
        Copy-Item -LiteralPath $source -Destination $icon.Target
    }
}
$sync = Join-Path (Split-Path -Parent $mod) '跑团伙伴/tools/sync_editor_textures.ps1'
& $sync -ProjectPath $project -TextureFolders 'SkillIcon'
$mapJson += [Environment]::NewLine
if ($mapJson -cne [IO.File]::ReadAllText($registry)) {
    [IO.File]::WriteAllText($registry, $mapJson)
}
Write-Output "技能图标已注册 $($icons.Count) 张"