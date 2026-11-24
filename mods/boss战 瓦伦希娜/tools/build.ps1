param(
    [string]$Managed = 'E:/SteamLibrary/steamapps/common/Depersonalization/Depersonalization-Release_Data/Managed',
    [string]$BepInEx = 'E:/SteamLibrary/steamapps/workshop/content/1477070/2925170762/BepInEx/core'
)

$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $mod)
$sdk = Join-Path $repo 'tools-external/dotnet'
$compiler = Join-Path $sdk 'sdk/8.0.425/Roslyn/bincore/csc.dll'
$stage = Join-Path $mod ('temp/build-' + [guid]::NewGuid())
$dll = Join-Path $stage 'ValennaBattle.dll'
$response = Join-Path $stage 'compile.rsp'
$destination = Join-Path $mod 'plugins/ValennaBattle.dll'
$backup = Join-Path $stage 'previous.dll'
[void][IO.Directory]::CreateDirectory($stage)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))

try {
    $arguments = @('-target:library', '-nostdlib+', '-langversion:latest', '-nologo', '-optimize+', "-out:$dll")
    foreach ($folder in $Managed, $BepInEx) {
        $arguments += @(Get-ChildItem -LiteralPath $folder -File -Filter '*.dll' |
            Where-Object Name -ne '0Harmony20.dll' | ForEach-Object { "-r:$($_.FullName)" })
    }
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $mod 'src') -File -Filter '*.cs' | ForEach-Object FullName)
    $effectDir = Join-Path $mod 'Project_Depersonal/Assets/Resources/Texture/Effect'
    foreach ($folder in 'valenna_arc_preview/idle', 'valenna_execution_wave/idle', 'valenna_cutting_wind/idle', 'valenna_elephant_wind/idle',
        'valenna_companion_sweep/idle', 'valenna_execution_sweep/idle', 'valenna_execution_companion/idle', 'valenna_twin_thrust/idle',
        'valenna_attack_sweep/idle', 'valenna_attack_companion/idle', 'valenna_peak_sweep/idle',
        'valenna_aim_charge/idle', 'valenna_gun_wake/idle', 'valenna_shot/idle',
        'valenna_finish_pierce/idle', 'valenna_long_thrust/idle',
        'valenna_impact_fx/idle', 'valenna_eye_glint/idle',
        'valenna_sheath_glint/idle', 'valenna_parry_fx/idle', 'valenna_foreseen_mark/idle', 'valenna_hunt_marker/idle',
        'valenna_pose_echoes', 'valenna_vfx') {
        $assets = @(Get-ChildItem -LiteralPath (Join-Path $effectDir $folder) -File -Filter '*.png')
        if (!$assets.Count) { throw "请先生成演出贴图 $folder" }
        $arguments += @($assets | ForEach-Object { "-resource:$($_.FullName),ValennaBattle.Art.$($_.Name)" })
    }
    [IO.File]::WriteAllLines($response, @($arguments | ForEach-Object { '"' + $_ + '"' }), [Text.UTF8Encoding]::new($false))
    & (Join-Path $sdk 'dotnet.exe') $compiler "@$response"
    if ($LASTEXITCODE -ne 0) { throw "瓦伦希娜插件编译失败，退出码 $LASTEXITCODE" }

    $identity = [Reflection.AssemblyName]::GetAssemblyName($dll)
    if ($identity.Name -ne 'ValennaBattle') { throw '程序集名称错误' }
    $expected = (Get-FileHash -LiteralPath $dll).Hash
    $replacing = [IO.File]::Exists($destination)
    if ($replacing) {
        [IO.File]::Replace($dll, $destination, $backup)
    } else {
        [IO.File]::Move($dll, $destination)
    }
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $expected) {
        if ($replacing) { [IO.File]::Copy($backup, $destination, $true) }
        throw '编译产物校验失败'
    }
    if ($replacing) { [IO.File]::Delete($backup) }
    Write-Output $destination
}
finally {
    foreach ($file in $response, $dll) { [IO.File]::Delete($file) }
    if (-not [IO.File]::Exists($backup)) { [IO.Directory]::Delete($stage) }
}
