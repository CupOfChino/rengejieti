param(
    [ValidatePattern('^[0-9]+$')][string]$HostModId,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$workshop = 'E:\SteamLibrary\steamapps\workshop\content\1477070'
$publishedId = '3845388588'
if (!$HostModId) {
    $HostModId = if (!$Remove -and [IO.File]::Exists("$workshop\$publishedId\Uploader")) { $publishedId } else { '3813306323' }
}
$hostPath = Join-Path $workshop $HostModId
$published = $HostModId -eq $publishedId
$manifest = Join-Path $mod "temp/installed-$HostModId.json"
if (-not [IO.Directory]::Exists($hostPath)) { throw "找不到测试宿主 $HostModId" }
if ($published) {
    if ($Remove) { throw '撤挂只用于寄放在其他模组中的测试文件。' }
    $item = Get-Content -LiteralPath (Join-Path $hostPath 'Uploader') -Raw | ConvertFrom-Json
    # Steam 下载的内容可能保留首次发布前的 FileId=0；此时用伙伴表核实身份。
    $fileId = [string]$item.Uploader.value.FileId
    if ($fileId -eq '0') {
        $partnerFile = Join-Path $hostPath 'Project_Depersonal/Assets/Resources/Config/Game/PartnerData/885101.txt'
        $partner = (Get-Content -LiteralPath $partnerFile -Raw | ConvertFrom-Json).Data.value
        if ($partner.PartnerId -ne 885101 -or $partner.RoleModeName -ne 'valenna_test_model') {
            throw "订阅目录中的伙伴表不符 $hostPath"
        }
    } elseif ($fileId -ne $publishedId) { throw "订阅目录的工坊编号不符 $hostPath" }
}
if (Get-Process -Name 'Depersonalization-Release' -ErrorAction SilentlyContinue) {
    throw '请退出人格解体再同步测试文件'
}

function Get-TargetPath([string]$Relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $hostPath $Relative))
    if (-not $full.StartsWith($hostPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "路径超出宿主范围 $Relative"
    }
    $full
}

$previous = if (!$published -and [IO.File]::Exists($manifest)) {
    Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json -AsHashtable
} else { [ordered]@{} }
foreach ($entry in $previous.GetEnumerator()) {
    $target = Get-TargetPath $entry.Key
    if ([IO.File]::Exists($target) -and (Get-FileHash -LiteralPath $target).Hash -ne $entry.Value) {
        throw "宿主中的文件已被修改 $target"
    }
}

if ($Remove) {
    $backupDir = Join-Path $mod ('temp/unmount-' + [guid]::NewGuid())
    [void][IO.Directory]::CreateDirectory($backupDir)
    $backedUp = [ordered]@{}
    try {
        foreach ($relative in $previous.Keys) {
            $target = Get-TargetPath $relative
            if (![IO.File]::Exists($target)) { continue }
            $copy = Join-Path $backupDir ($backedUp.Count.ToString() + '.bak')
            [IO.File]::Copy($target, $copy)
            $backedUp[$relative] = $copy
            if ((Get-FileHash -LiteralPath $copy).Hash -ne $previous[$relative]) { throw "撤挂备份不符 $target" }
        }
        try {
            foreach ($relative in $backedUp.Keys) { [IO.File]::Delete((Get-TargetPath $relative)) }
            if ([IO.File]::Exists($manifest)) { [IO.File]::Delete($manifest) }
        } catch {
            foreach ($relative in $backedUp.Keys) {
                $target = Get-TargetPath $relative
                if (![IO.File]::Exists($target)) { [IO.File]::Copy($backedUp[$relative], $target) }
            }
            throw
        }
    } catch {
        Write-Warning "撤挂中止，备份位于 $backupDir"
        throw
    }
    foreach ($copy in $backedUp.Values) { [IO.File]::Delete($copy) }
    [IO.Directory]::Delete($backupDir)
    Write-Output "已撤下 $HostModId 的瓦伦希娜测试文件"
    return
}

$data = Join-Path $mod 'Project_Depersonal/Assets/Resources/Config/Game'
$assets = Join-Path $mod 'Project_Depersonal/Assets/Resources'
$plugin = Join-Path $mod 'plugins/ValennaBattle.dll'
$files = @((Get-Item -LiteralPath $plugin).FullName)
if ($published) { $files += (Get-Item -LiteralPath (Join-Path $mod 'Project_Depersonal/Project.rtmeta')).FullName }
$files += (Get-Item -LiteralPath (Join-Path $mod 'resource/voice-subtitles.json')).FullName
$files += @(Get-ChildItem -LiteralPath $data -Recurse -File -Filter '*.txt' | ForEach-Object FullName)
$files += @((Get-Item -LiteralPath (Join-Path $assets 'Config/InternalConfigure.txt')).FullName)
$files += @(Get-ChildItem -LiteralPath (Join-Path $assets 'Texture') -Recurse -File |
    Where-Object { $_.Extension -in '.png', '.rtmeta', '.rtview' -or $_.Name -eq 'pose-pivots.json' } | ForEach-Object FullName)
$soundRoot = Join-Path $mod 'resource/sound'
if ([IO.Directory]::Exists($soundRoot)) {
    $files += @(Get-ChildItem -LiteralPath $soundRoot -Recurse -File -Filter '*.ogg' | ForEach-Object FullName)
}
$audioPresets = Join-Path $mod 'resource/skill-audio'
$presets = if ([IO.Directory]::Exists($audioPresets)) {
    @(Get-ChildItem -LiteralPath $audioPresets -File -Filter '*.json' |
        Where-Object BaseName -Match '^88512[1456789]$')
} else { @() }
$next = [ordered]@{}
$changes = [Collections.Generic.List[object]]::new()
$stage = Join-Path $mod ('temp/install-' + [guid]::NewGuid())

foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($mod, $file)
    $target = Get-TargetPath $relative
    $present = [IO.File]::Exists($target)
    if ($present -and !$published -and -not $previous.Contains($relative)) { throw "目标文件已存在 $target" }
    $next[$relative] = (Get-FileHash -LiteralPath $file).Hash
    $before = if ($published -and $present) { (Get-FileHash -LiteralPath $target).Hash } else { $previous[$relative] }
    if ($present -and $before -eq $next[$relative]) { continue }
    $changes.Add(@{
        Source = $file; Target = $target; Previous = $present
        PreviousHash = $before; Hash = $next[$relative]
    })
}
foreach ($relative in $previous.Keys) {
    if (-not $next.Contains($relative)) { throw "挂载文件清单已缩减，请先撤下旧版 $relative" }
}

[void][IO.Directory]::CreateDirectory($stage)
$applied = [Collections.Generic.List[object]]::new()
$newPresets = [Collections.Generic.List[string]]::new()
try {
    foreach ($change in $changes) {
        $backup = Join-Path $stage ($applied.Count.ToString() + '.bak')
        if ($change.Previous) { [IO.File]::Copy($change.Target, $backup) }
        $change.Backup = $backup
        $applied.Add($change)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($change.Target))
        [IO.File]::Copy($change.Source, $change.Target, $true)
        if ((Get-FileHash -LiteralPath $change.Target).Hash -ne $change.Hash) {
            throw "复制校验失败 $($change.Target)"
        }
    }
    # 技能音轨可由编辑器与玩家修改。安装仅补齐缺少的配置，清单不管理它们。
    foreach ($preset in $presets) {
        $target = Get-TargetPath ([IO.Path]::GetRelativePath($mod, $preset.FullName))
        if ([IO.File]::Exists($target)) { continue }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($preset.FullName, $target, $false)
        $newPresets.Add($target)
        if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $preset.FullName).Hash) {
            throw "技能音轨复制校验失败 $target"
        }
    }
    if (!$published) {
        $stagedManifest = Join-Path $stage 'manifest.json'
        [IO.File]::WriteAllText($stagedManifest, ($next | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
        if ([IO.File]::Exists($manifest)) {
            [IO.File]::Replace($stagedManifest, $manifest, (Join-Path $stage 'manifest.bak'))
        } else {
            [IO.File]::Move($stagedManifest, $manifest)
        }
    }
}
catch {
    foreach ($target in $newPresets) { [IO.File]::Delete($target) }
    foreach ($change in $applied) {
        $exists = [IO.File]::Exists($change.Target)
        if ($change.Previous) {
            if (-not $exists -or (Get-FileHash -LiteralPath $change.Target).Hash -ne $change.PreviousHash) {
                [IO.File]::Copy($change.Backup, $change.Target, $true)
            }
        } elseif ($exists) {
            [IO.File]::Delete($change.Target)
        }
    }
    throw
}
Get-ChildItem -LiteralPath $stage -File | ForEach-Object { [IO.File]::Delete($_.FullName) }
[IO.Directory]::Delete($stage)
Write-Output "宿主 $HostModId 已挂载瓦伦希娜，核对 $($next.Count) 个文件"
