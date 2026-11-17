# 离线验证外观参数、存读、草稿隔离及幕间空编号回退；不启动游戏。
param(
    [string]$GameManaged = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\Managed',
    [string]$BepInExCore = 'E:\SteamLibrary\steamapps\workshop\content\1477070\2925170762\BepInEx\core'
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $modRoot)
$dotnet = Join-Path $repoRoot 'tools-external\dotnet\dotnet.exe'
$csc = Join-Path $repoRoot 'tools-external\dotnet\sdk\8.0.425\Roslyn\bincore\csc.dll'
$refs = Join-Path $repoRoot 'tools-external\dotnet\packs\Microsoft.NETCore.App.Ref\8.0.31\ref\net8.0'
$testRoot = Join-Path $modRoot 'temp\visual_tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testDll = Join-Path $testRoot 'VisualChecks.dll'
$arguments = @('-target:exe', '-nostdlib+', ('-out:' + $testDll))
foreach ($reference in Get-ChildItem -LiteralPath $refs -Filter '*.dll') {
    $arguments += '-r:' + $reference.FullName
}
$arguments += Join-Path $modRoot 'tests\VisualChecks.cs'
$response = Join-Path $testRoot 'checks.rsp'
$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllLines($response, [string[]]($arguments | ForEach-Object { '"' + $_ + '"' }), $encoding)
[System.IO.File]::WriteAllText((Join-Path $testRoot 'VisualChecks.runtimeconfig.json'),
    '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.31"}}}', $encoding)
& $dotnet exec $csc -noconfig ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw '验证程序编译失败' }
$config = Join-Path $testRoot ('visuals_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff') + '.cfg')
& $dotnet exec $testDll (Join-Path $modRoot 'plugins\XinEditor.dll') $GameManaged $BepInExCore $config
if ($LASTEXITCODE -ne 0) { throw '外观参数验证失败' }
