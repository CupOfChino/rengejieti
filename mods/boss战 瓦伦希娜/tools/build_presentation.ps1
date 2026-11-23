param(
    [string]$GameTables = 'E:/SteamLibrary/steamapps/common/Depersonalization/Depersonalization-Release_Data/StreamingAssets/Game',
    [string]$PortraitPath,
    [string]$SpriteSource,
    [string]$EffectSource
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$mod = Split-Path -Parent $PSScriptRoot
$project = Join-Path $mod 'Project_Depersonal'
$tempRoot = Join-Path $mod 'temp'
$stage = Join-Path $tempRoot ('presentation-' + [guid]::NewGuid())
$stagedProject = Join-Path $stage 'Project_Depersonal'
$assets = Join-Path $stagedProject 'Assets'
$texture = Join-Path $assets 'Resources/Texture'
$actorName = 'valenna_test_model'
$portraitName = 'valenna_portrait'
$effectName = 'valenna_arc_preview'
$utf8 = [Text.UTF8Encoding]::new($false)
$effectRoots = @(
    if ($EffectSource) { $EffectSource }
    Join-Path $mod 'resource/可能的粒子和材质'
    'E:/lim/_save/素材库/09_Rodion_Thumb_FatherAppearance/SD/10916_Rodion_Thumb_FatherAppearance'
)
if (-not ('ValennaBattle.StrikeOpenings' -as [type])) {
    Add-Type -Path @((Join-Path $mod 'src/StrikeProfiles.cs'), (Join-Path $mod 'src/StrikeOpenings.cs'))
}
if (-not ('ValennaBattle.IdleBreathPainter' -as [type])) {
    Add-Type -Path @((Join-Path $mod 'src/ValennaPoses.cs'), (Join-Path $PSScriptRoot 'IdleBreathPainter.cs'))
}
if (-not ('ValennaBattle.SlashFramePainter' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'SlashFramePainter.cs')
}
if (-not ('ValennaBattle.HuntingMarkArt' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'HuntingMarkArt.cs')
}

function Save-Text([string]$RelativePath, [string]$Text) {
    $path = Join-Path $assets $RelativePath
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Text + [Environment]::NewLine, $utf8)
}

function Save-Config([string]$RelativePath, [string]$Type, $Value) {
    $record = [ordered]@{ Data = [ordered]@{ __type = "$Type,Assembly-CSharp"; value = $Value } }
    Save-Text "Resources/Config/Game/$RelativePath.txt" ($record | ConvertTo-Json -Depth 55)
}

function Asset-Ref([int]$Type, [string]$Key) {
    [ordered]@{ ReferenceType = $Type; Key = $Key }
}

function Write-Picture([string]$Path, [int]$Width, [int]$Height, [byte[]]$Pixels) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $rect = [Drawing.Rectangle]::new(0, 0, $Width, $Height)
        $locked = $bitmap.LockBits($rect, 'WriteOnly', 'Format32bppArgb')
        try {
            [Runtime.InteropServices.Marshal]::Copy($Pixels, 0, $locked.Scan0, $Pixels.Length)
        } finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

function Read-ImagePixels([string]$Source) {
    $original = [Drawing.Bitmap]::new($Source)
    try {
        $rect = [Drawing.Rectangle]::new(0, 0, $original.Width, $original.Height)
        $bitmap = $original.Clone($rect, 'Format32bppArgb')
        try {
            $locked = $bitmap.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
            try {
                $bytes = [byte[]]::new($bitmap.Width * $bitmap.Height * 4)
                for ($row = 0; $row -lt $bitmap.Height; $row++) {
                    $line = [IntPtr]::Add($locked.Scan0, $row * $locked.Stride)
                    [Runtime.InteropServices.Marshal]::Copy($line, $bytes, $row * $bitmap.Width * 4, $bitmap.Width * 4)
                }
                @{ Bytes = $bytes; Width = $bitmap.Width; Height = $bitmap.Height }
            } finally { $bitmap.UnlockBits($locked) }
        } finally { $bitmap.Dispose() }
    } finally { $original.Dispose() }
}

function Read-Mask([string]$Name, [bool]$AlphaOnly = $false) {
    $source = if ([IO.Path]::IsPathFullyQualified($Name)) { $Name } else {
        $effectRoots | ForEach-Object { Join-Path $_ $Name } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (!$source) { throw "找不到特效遮罩 $Name" }
    $image = Read-ImagePixels $source
    [ValennaBattle.SlashMask]::new($image.Bytes, $image.Width, $image.Height, $AlphaOnly)
}

function Stage-Icon([string]$RelativePath, [string]$SourcePath) {
    $existing = Join-Path $project "Assets/$RelativePath"
    if (Test-Path -LiteralPath $existing -PathType Leaf) {
        Stage-Image $RelativePath $existing
        return
    }
    if ([IO.Path]::GetExtension($SourcePath) -eq '.png') {
        Stage-Image $RelativePath $SourcePath
        return
    }

    Add-Type -AssemblyName PresentationCore
    $source = [IO.File]::OpenRead($SourcePath)
    try {
        $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create($source,
            [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($decoder.Frames[0]))
        $path = Join-Path $assets $RelativePath
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        $output = [IO.File]::Create($path)
        try { $encoder.Save($output) } finally { $output.Dispose() }
    } finally { $source.Dispose() }
}

function Stage-Image([string]$RelativePath, [string]$SourcePath) {
    if (-not $SourcePath) { $SourcePath = Join-Path $project "Assets/$RelativePath" }
    $source = Get-Item -LiteralPath $SourcePath
    $target = Join-Path $assets $RelativePath
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($source.FullName, $target)
}

function Stage-Portrait([string]$SourcePath) {
    if (-not $SourcePath) { $SourcePath = Join-Path $mod 'resource/瓦伦娜立绘.png' }
    $image = [Drawing.Bitmap]::new((Get-Item -LiteralPath $SourcePath).FullName)
    $frame = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($frame)
    try {
        # 海琳娜、艾咪、莎夜的原版头像约 64 像素，眉眼和帽檐落在头肩画幅内。
        $crop = [Drawing.Rectangle]::new(0, 0, $image.Width, $image.Height)
        if ($image.Width -eq 800 -and $image.Height -eq 1391) {
            $crop = [Drawing.Rectangle]::new(334, 0, 300, 300)
        }
        $scale = 64.0 / [Math]::Max($crop.Width, $crop.Height)
        $width = [Math]::Max(1, [int][Math]::Round($crop.Width * $scale))
        $height = [Math]::Max(1, [int][Math]::Round($crop.Height * $scale))
        $position = [Drawing.Rectangle]::new([int][Math]::Floor((64 - $width) / 2),
            [int][Math]::Floor((64 - $height) / 2), $width, $height)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, $position, $crop, [Drawing.GraphicsUnit]::Pixel)
        $target = Join-Path $texture "HeadIcon/$portraitName.png"
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        $frame.Save($target, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $frame.Dispose(); $image.Dispose() }
}

function Get-PoseLayout([ValennaBattle.ValennaPose]$Pose, [int]$Width, [int]$Height) {
    if ($Pose.FootX -lt 0 -or $Pose.FootX -ge $Width -or $Pose.FootY -lt 0 -or $Pose.FootY -ge $Height) {
        throw "姿势锚点超出图像范围 $($Pose.Key)"
    }
    $layout = [ordered]@{ x = $Pose.FootX; y = $Pose.FootY }
    foreach ($name in 'eye','sheath','gun') {
        $point = $Pose.$name
        if ($null -eq $point) { continue }
        if ($point.X -lt 0 -or $point.X -ge $Width -or $point.Y -lt 0 -or $point.Y -ge $Height) {
            throw "特效锚点超出图像范围 $($Pose.Key) $name"
        }
        $layout[$name] = [ordered]@{ x = $point.X; y = $point.Y }
    }
    $layout
}

$poses = [ValennaBattle.ValennaPoses]::All
$idleCycle = [ValennaBattle.ValennaPoses]::IdleCycle
$roleTexture = "Resources/Texture/Charator/$actorName"
$sourceDirectory = Join-Path $mod 'resource'
if ($SpriteSource -and -not (Test-Path -LiteralPath $SpriteSource -PathType Container)) {
    throw "找不到人物素材目录 $SpriteSource"
}
$posePivots = [ordered]@{}
Stage-Portrait $PortraitPath
foreach ($pose in $poses) {
    $candidates = @(
        if ($SpriteSource) {
            Join-Path $SpriteSource $pose.File
            if ($pose.LegacyFile) { Join-Path $SpriteSource $pose.LegacyFile }
        }
        Join-Path $sourceDirectory $pose.File
    )
    $source = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    $relative = "$roleTexture/$($pose.Key).png"
    Stage-Image $relative $source
    $mask = Read-Mask (Join-Path $assets $relative) $true
    $echoKey = $pose.Key.Replace('valenna_pose_', 'valenna_ghost_')
    Write-Picture (Join-Path $texture "Effect/valenna_pose_echoes/$echoKey.png") $mask.Width $mask.Height ($mask.AsAlpha())
    $posePivots["$($pose.Key).png"] = Get-PoseLayout $pose $mask.Width $mask.Height
}
$posePivots['referenceIdle'] = "$($idleCycle[0].Key).png"
$idle = Read-ImagePixels (Join-Path $assets "$roleTexture/$($posePivots['referenceIdle'])")
for ($index = 1; $index -lt $idleCycle.Length; $index++) {
    $pose = $idleCycle[$index]
    $file = "$($pose.Key).png"
    $pixels = [ValennaBattle.IdleBreathPainter]::Draw($idle.Bytes, $idle.Width, $idle.Height, $index)
    Write-Picture (Join-Path $assets "$roleTexture/$file") $idle.Width $idle.Height $pixels
    $posePivots[$file] = Get-PoseLayout $pose $idle.Width $idle.Height
}
Save-Text "$roleTexture/pose-pivots.json" ($posePivots | ConvertTo-Json -Depth 4)

function Model-Clip([string]$Key, [bool]$Loop, [double]$Interval, [string[]]$Frames, [int[]]$Holds) {
    if ($Holds -and $Holds.Length -ne $Frames.Length) { throw "姿势停留数与帧数不符 $Key" }
    [ordered]@{
        Key = $Key; IsLoop = $Loop; IsUseOldMode = $false; IntervalTime = $Interval; Sprites = $null
        SpriteDatas = @(for ($index = 0; $index -lt $Frames.Length; $index++) {
            $frame = $Frames[$index]
            $count = if ($Holds) { $Holds[$index] } else { 1 }
            [ordered]@{ Sprite = $null; AnimSprite = (Asset-Ref 1018 "valenna_pose_$frame"); ShowCount = $count; ActiveChild = $false; ChildSprites = @() }
        })
    }
}

$base = Join-Path $GameTables 'RoleModel/Model_NPC_093.txt'
$model = (Get-Content -LiteralPath $base -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable).Data.value
$model.ModelName = $actorName
$model.Speaker = '瓦伦娜'
$model.HeadIcons = @([ordered]@{
    MatchParames = @(); Type = 0; Offset = @{ x = 0; y = 0 }; Icon = $null
    IconResference = Asset-Ref 1001 $portraitName
})
$model.DeathOpts = @()
$idleFrames = @($idleCycle | ForEach-Object Name)
$idleInterval = [ValennaBattle.ValennaPoses]::IdleBreathInterval
$finishInterval = [ValennaBattle.StrikeProfiles]::SweepInterval
$model.AnimationList = @(
    (Model-Clip 'idle' $true $idleInterval $idleFrames)
    (Model-Clip 'battle_idle' $true $idleInterval $idleFrames)
    (Model-Clip 'walk' $true 0.12 @('dash1', 'dash2'))
    (Model-Clip 'move' $true 0.09 @('dash1', 'dash2'))
    (Model-Clip 'attack' $false 0.08 @('draw', 'rise', 'guard'))
    (Model-Clip 'guard' $false 0.24 @('guard'))
    (Model-Clip 'gethit' $false 0.11 @('hurt', 'guard'))
    (Model-Clip 'hurt' $false 0.11 @('hurt', 'guard'))
    (Model-Clip 'dodge' $false 0.12 @('dodge', 'idle'))
    (Model-Clip 'valenna_prescience_dodge' $true 0.12 @('dodge'))
    (Model-Clip 'death' $false 0.16 @('fallen'))
    (Model-Clip 'valenna_draw' $false ([ValennaBattle.StrikeProfiles]::DrawGripTime) @('idle', 'draw') @(1, 4))
    foreach ($show in [ValennaBattle.StrikeProfiles]::Shows) {
        $opening = [ValennaBattle.StrikeOpenings]::For($show)
        $pose = [ValennaBattle.StrikeProfiles]::OpeningPose($show)
        if ($show.Charged) {
            Model-Clip $pose $false $opening.FrameTime @('idle','draw','finish_back1','finish_back2') `
                @($opening.GripFrames, ($opening.BackFrame - $opening.GripFrames), 1, ($opening.Frames - $opening.BackFrame - 1))
        } else {
            Model-Clip $pose $false $opening.FrameTime @('idle','draw') @($opening.GripFrames, ($opening.Frames - $opening.GripFrames))
        }
    }
    (Model-Clip 'valenna_dash' $true 0.07 @('dash1', 'dash2'))
    (Model-Clip 'valenna_cut_approach' $false ([ValennaBattle.StrikeProfiles]::CutApproachTime) @('dash2'))
    (Model-Clip 'valenna_rise' $false 0.05 @('dash2', 'rise', 'rise_end') @(1, 2, 9))
    (Model-Clip 'valenna_cut_rise' $false 0.04 @('dash2','rise') @(1, 12))
    (Model-Clip 'valenna_crosscut' $false 0.05 @('dash2', 'cross', 'cross_out1', 'cross_out2') @(1, 2, 1, 10))
    (Model-Clip 'valenna_cut_crosscut' $false 0.16 @('dash2'))
    (Model-Clip 'valenna_rabbit_open' $false 0.02 @('dash2','rise') @(1, 11))
    (Model-Clip 'valenna_rabbit_sweep' $false 0.035 @('finish_dash','finish_spin') @(2, 10))
    (Model-Clip 'valenna_deer_open' $false 0.018 @('finish_turn','finish_cut') @(2, 8))
    (Model-Clip 'valenna_deer_first_sweep' $false 0.0182 @('cross','cross_out1') @(5, 6))
    (Model-Clip 'valenna_deer_raise' $false 0.0185 @('dash2','rise') @(3, 10))
    (Model-Clip 'valenna_deer_spin' $false 0.020 @('finish_guard','finish_dash','finish_spin','finish_low') @(2, 2, 2, 2))
    (Model-Clip 'valenna_deer_sweep' $false 0.0185 @('cross','cross_out1','cross_out2') @(5, 1, 39))
    (Model-Clip 'valenna_elephant_prepare' $false 0.019 @('thrust_out1','thrust_out2') @(2, 9))
    (Model-Clip 'valenna_elephant_thrust' $false 0.0186 @('aim','aim_double','aim') @(3, 1, 10))
    (Model-Clip 'valenna_elephant_volley' $false 0.0188 @('aim','aim_double','aim') @(1, 3, 21))
    (Model-Clip 'valenna_elephant_first_sweep' $false 0.0188 @('finish_low','finish_turn','finish_cut','cross','cross_out1','cross_out2') @(3, 1, 2, 2, 2, 7))
    (Model-Clip 'valenna_elephant_spin' $false 0.019 @('rise','cross','finish_spin') @(4, 3, 2))
    (Model-Clip 'valenna_elephant_sweep' $false 0.0188 @('finish_low','finish_turn','cross','cross_out1','cross_out2') @(4, 4, 3, 1, 45))
    (Model-Clip 'valenna_orbit' $false 0.06 @('finish_cut', 'finish_turn', 'finish_guard') @(1, 2, 8))
    (Model-Clip 'valenna_thrust' $false 0.02 @('aim','aim_double','aim','aim_double','aim') @(4, 4, 4, 4, 11))
    (Model-Clip 'valenna_point_recoil' $false 0.025 @('aim','thrust_out2') @(1, 9))
    (Model-Clip 'valenna_thrust_brace' $false ([ValennaBattle.StrikeProfiles]::BeatPause) @('cross'))
    (Model-Clip 'valenna_rabbit_brace' $false ([ValennaBattle.StrikeProfiles]::BeatPause) @('finish_spin'))
    (Model-Clip 'valenna_point_pass' $false 0.03 @('cross','aim') @(1, 16))
    (Model-Clip 'valenna_rabbit_pass' $false 0.04 @('finish_spin','aim') @(1, 14))
    (Model-Clip 'valenna_bullet_thrust' $false 0.0425 @('aim','aim_double','aim') @(1, 2, 1))
    (Model-Clip 'valenna_bullet_double' $false 0.02 @('aim','aim_double','aim','aim_double','aim') @(4, 5, 4, 4, 4))
    (Model-Clip 'valenna_bullet_recoil' $false 0.028 @('aim','thrust_out2') @(1, 5))
    (Model-Clip 'valenna_bullet_shot' $false 0.02 @('aim','aim_double','aim') @(5, 4, 12))
    (Model-Clip 'valenna_bullet_pass' $false 0.025 @('cross','cross_out1','cross_out2') @(3, 3, 42))
    (Model-Clip 'valenna_guard' $false 0.10 @('guard', 'guard', 'draw'))
    (Model-Clip 'valenna_muzzle' $false 0.05 @('thrust_ready', 'aim', 'aim_double', 'aim') @(1, 2, 2, 7))
    (Model-Clip 'valenna_volley' $false 0.06 @('aim', 'aim_double', 'aim', 'aim_double', 'thrust_out2') @(2, 1, 2, 1, 5))
    (Model-Clip 'valenna_recover' $false 0.12 @('draw','idle') @(1, 3))
    (Model-Clip 'valenna_thrust_recover' $false 0.11 @('aim','draw','idle'))
    (Model-Clip 'valenna_dive_recover' $false 0.11 @('cross_out2','draw','idle'))
    (Model-Clip 'valenna_elephant_hold' $false ([ValennaBattle.StrikeProfiles]::ReturnTime +
        [ValennaBattle.StrikeProfiles]::ReturnAfterglow) @('cross_out2'))
    (Model-Clip 'valenna_deer_recover' $false 0.09 @('cross_out2','cut_lower','draw','idle') @(1, 1, 1, 2))
    (Model-Clip 'valenna_finish_recover' $false 0.08 @('finish_fire','draw','idle') @(1, 2, 2))
    (Model-Clip 'valenna_cut_recover' $false 0.16 @('rise','idle'))
    (Model-Clip 'valenna_finish_turn' $false $finishInterval @('finish_cut','finish_low','finish_turn','dash2','rise') @(2, 1, 2, 4, 12))
    (Model-Clip 'valenna_finish_turn_slash' $false $finishInterval @('finish_low','finish_turn','finish_cut') @(2, 2, 3))
    (Model-Clip 'valenna_finish_sweep' $false $finishInterval @('cross','cross_out1') @(2, 5))
    (Model-Clip 'valenna_finish_raise' $false $finishInterval @('dash2','rise') @(4, 5))
    (Model-Clip 'valenna_finish_spin' $false $finishInterval @('cross','finish_spin') @(4, 4))
    (Model-Clip 'valenna_finish_low' $false 0.05 @('finish_spin', 'finish_low', 'finish_dash', 'finish_cut') @(1, 2, 2, 6))
    (Model-Clip 'valenna_finish_thrust' $false $finishInterval @('finish_low','cross','finish_dash') @(1, 3, 12))
    (Model-Clip 'valenna_finish_brace' $false ([ValennaBattle.StrikeProfiles]::BeatPause) @('finish_low'))
    (Model-Clip 'valenna_finish_insert' $false ([ValennaBattle.StrikeProfiles]::ExecutionInsertPoseTime) @('finish_low','finish_aim') @(1, 5))
    (Model-Clip 'valenna_finish_pin' $false ([ValennaBattle.StrikeProfiles]::BeatPause) @('finish_aim'))
    (Model-Clip 'valenna_finish_volley' $false ([ValennaBattle.StrikeProfiles]::ExecutionKnockbackTime +
        [ValennaBattle.StrikeProfiles]::ExecutionStandTime) @('finish_fire'))
)
$model.PointPosDatas[0].AnimKeys = @($model.AnimationList.Key)
Save-Config "RoleModel/$actorName" 'RoleModelData' $model

$frameCount = [ValennaBattle.StrikeProfiles]::SweepFrames
$frameSize = [ValennaBattle.StrikeProfiles]::SweepPixels
$frameTime = [ValennaBattle.StrikeProfiles]::SweepInterval
$particles = [ValennaBattle.SlashParticles]@{
    Wind = (Read-Mask 'Fx_T_Shape_Ground_Wind_01.png')
    Flash = (Read-Mask 'Fx_T_Shape_LineFlash_01.png')
    Star = (Read-Mask 'Fx_T_Shape_Dia_01_hw.png')
    ThrustStreaks = (Read-Mask '1.png' $true)
    ImpactDebris = (Read-Mask '2.png' $true)
    SweepFragments = (Read-Mask '3.png' $true)
    SweepWake = (Read-Mask '4.png' $true)
    ThrustWake = (Read-Mask '5.png' $true)
}
$painter = [ValennaBattle.SlashFramePainter]::new(
    (Read-Mask 'Fx_T_Shape_Slash_01_hw.png'),
    (Read-Mask 'Fx_T_Shape_Slash_05_hw_Blur.png'),
    (Read-Mask 'fx_t_noise_12.png'),
    (Read-Mask 'Fx_T_Shape_Hit_02_0.png'),
    $particles, $frameSize, [ValennaBattle.StrikeProfiles]::SweepSpan, $frameTime)
$effectDir = Join-Path $texture "Effect/$effectName/idle"
for ($index = 0; $index -lt $frameCount; $index++) {
    $key = 'valenna_slash_frame_{0:D2}' -f $index
    Write-Picture (Join-Path $effectDir "$key.png") $frameSize $frameSize ($painter.Draw($index, $false))
}
$masks = [ordered]@{
    flare = 'Fx_T_Shape_Dia_01_hw.png'
    streak = 'Fx_T_Shape_Slash_09_hw.png'
    shard = 'Fx_T_Shape_Hit_02_0.png'
    smoke = 'Fx_T_Shape_Smoke_06.png'
    flame_0 = 'Fx_T_Shape_Fire_01_2x2_0.png'
    flame_1 = 'Fx_T_Shape_Fire_01_2x2_1.png'
    flame_2 = 'Fx_T_Shape_Fire_01_2x2_2.png'
    flame_3 = 'Fx_T_Shape_Fire_01_2x2_3.png'
}
foreach ($entry in $masks.GetEnumerator()) {
    $mask = Read-Mask $entry.Value
    Write-Picture (Join-Path $texture "Effect/valenna_vfx/valenna_$($entry.Key).png") $mask.Width $mask.Height ($mask.AsAlpha())
}
function Save-EffectSequence([string]$Name, [string]$Prefix, [int]$Count, [float]$Width, [float]$Height,
    [int]$SharedFrom = $Count, [string]$SharedPrefix = $Prefix) {
    $sequence = [ordered]@{
        __type = 'CustomSpriteConfigData,Assembly-CSharp'
        EffectName = $Name
        AnimationList = @([ordered]@{
            AnimName = 'idle'; IsLoop = $false
            ExtraAnims = @(for ($index = 0; $index -lt $Count; $index++) {
                $framePrefix = if ($index -lt $SharedFrom) { $Prefix } else { $SharedPrefix }
                [ordered]@{
                    AnimSprite = Asset-Ref 1023 ('{0}{1:D2}' -f $framePrefix, $index)
                    IsInternal = $false; ResLoadPath = $null; UseAtals = $false; SpriteName = $null; ShowCount = 1
                }
            })
        })
        AnimSpeed = $frameTime; AnimSize = @{ x = $Width; y = $Height }
        SortName = 'Chara'; SortOrder = 50; DefaultPlayAnim = 'idle'; UseLightEffect = $false
    }
    Save-Config "ExtraAnim/$Name" 'BaseSheetData' $sequence
}
Save-EffectSequence $effectName 'valenna_slash_frame_' $frameCount 4.2 4.2

$executionWaveCount = [ValennaBattle.StrikeProfiles]::ExecutionOpeningFrames
$executionWaveName = 'valenna_execution_wave'
$executionWaveTilt = [ValennaBattle.StrikeProfiles]::ExecutionWaveAngle
for ($index = 0; $index -lt $executionWaveCount; $index++) {
    $key = 'valenna_execution_wave_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$executionWaveName/idle/$key.png"
    Write-Picture $path $frameSize $frameSize ($painter.DrawExecutionOpening($index, $executionWaveTilt))
}
$executionWaveSize = [ValennaBattle.StrikeProfiles]::SweepSpan
Save-EffectSequence $executionWaveName 'valenna_execution_wave_' $executionWaveCount $executionWaveSize $executionWaveSize

$cuttingWindCount = [ValennaBattle.StrikeProfiles]::CuttingWindFrames
$cuttingWindName = 'valenna_cutting_wind'
for ($index = 0; $index -lt $cuttingWindCount; $index++) {
    $key = 'valenna_cutting_wind_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$cuttingWindName/idle/$key.png"
    Write-Picture $path $frameSize $frameSize ($painter.DrawCuttingWind($index))
}
Save-EffectSequence $cuttingWindName 'valenna_cutting_wind_' $cuttingWindCount $executionWaveSize $executionWaveSize

$elephantWindCount = [ValennaBattle.StrikeProfiles]::ElephantWindFrames
$elephantWindName = 'valenna_elephant_wind'
$elephantWindTime = [ValennaBattle.StrikeProfiles]::ElephantWindDuration
for ($index = 0; $index -lt $elephantWindCount; $index++) {
    $key = 'valenna_elephant_wind_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$elephantWindName/idle/$key.png"
    $phase = ($index + 0.5) / $elephantWindCount
    Write-Picture $path $frameSize $frameSize ($painter.DrawElephantWind($phase, $elephantWindTime))
}
Save-EffectSequence $elephantWindName 'valenna_elephant_wind_' $elephantWindCount $executionWaveSize $executionWaveSize

$markName = 'valenna_foreseen_mark'
$markPath = "Resources/Texture/Effect/$markName/idle/${markName}_00.png"
$markSource = Join-Path $project "Assets/$markPath"
if (!(Test-Path -LiteralPath $markSource -PathType Leaf)) {
    $markSource = 'E:/lim/_save/素材库/_状态图标/FutureEyeOn/FutureEyeOn.png'
}
Stage-Image $markPath $markSource
Save-EffectSequence $markName "${markName}_" 1 ([ValennaBattle.StrikeProfiles]::ForeseenMarkSize) ([ValennaBattle.StrikeProfiles]::ForeseenMarkSize)
Stage-Image 'Resources/Texture/BuffIcon/valenna_eye_charge_icon.png' (Join-Path $assets $markPath)
$hotIcon = 'Resources/Texture/BuffIcon/valenna_eye_overheat_icon.png'
$hotSource = Join-Path $project "Assets/$hotIcon"
if (!(Test-Path -LiteralPath $hotSource -PathType Leaf)) {
    $hotSource = 'E:/lim/_save/素材库/_状态图标/FutureEyeOff/FutureEyeOff.png'
}
Stage-Image $hotIcon $hotSource
$futurePath = 'D:/HuaweiMoveData/Users/huawei/Desktop/钢丝/加速的未来.webp'
if (!(Test-Path -LiteralPath $futurePath -PathType Leaf)) {
    $futurePath = 'C:/Users/huawei/AppData/Local/Temp/codex-clipboard-9c5bfa7e-f47b-44b4-ab2a-e9d56ac465c6.webp'
}
Stage-Icon 'Resources/Texture/BuffIcon/valenna_accelerated_future_icon.png' $futurePath
$equipPath = 'D:/HuaweiMoveData/Users/huawei/Desktop/钢丝/饰品-9256.webp'
if (!(Test-Path -LiteralPath $equipPath -PathType Leaf)) {
    $equipPath = 'E:/lim/_save/素材库/_图标与UI/Sprite/EgoGiftIcon/9256/9256.png'
}
Stage-Icon 'Resources/Texture/ItemIcon/valenna_eye_equip_icon.png' $equipPath
Stage-Icon 'Resources/Texture/BuffIcon/valenna_accel_bullet_icon.png' 'D:/HuaweiMoveData/Users/huawei/Desktop/钢丝/加速弹.webp'
foreach ($icon in @(
    @{ Name = 'breath'; Key = 'Breath' },
    @{ Name = 'tremor'; Key = 'Vibration' },
    @{ Name = 'burn'; Key = 'Burn' },
    @{ Name = 'ignition'; Key = 'VibrationIgnition' }
)) {
    Stage-Icon "Resources/Texture/BuffIcon/valenna_$($icon.Name)_icon.png" `
        "E:/lim/_save/素材库/_状态图标/$($icon.Key)/$($icon.Key).png"
}
foreach ($icon in @(
    @{ Name = 'gunblade'; File = '铳剑.png' },
    @{ Name = 'cutlass'; File = '切斩刀.png' },
    @{ Name = 'cap'; File = '帽子.png' }
)) {
    Stage-Image "Resources/Texture/ItemIcon/valenna_$($icon.Name)_icon.png" (Join-Path $sourceDirectory $icon.File)
}

$repo = Split-Path -Parent (Split-Path -Parent $mod)
$huntImage = Read-ImagePixels (Join-Path $repo 'temp/icon_work/剑痕_对比.png')
$swords = [ValennaBattle.HuntingMarkArt]::Extract($huntImage.Bytes, $huntImage.Width * 4, $huntImage.Height)
$huntSize = [ValennaBattle.HuntingMarkArt]::Size
$huntTexture = 'Resources/Texture/Effect/valenna_hunt_marker/idle'
Stage-Icon "$huntTexture/valenna_tremor_burst_icon.png" 'D:/HuaweiMoveData/Users/huawei/Desktop/钢丝/震颤引爆.webp'
Stage-Image 'Resources/Texture/BuffIcon/valenna_tremor_burst_icon.png' (Join-Path $assets "$huntTexture/valenna_tremor_burst_icon.png")
Stage-Image "$huntTexture/valenna_scorch_burst_icon.png" (Join-Path $assets 'Resources/Texture/BuffIcon/valenna_ignition_icon.png')
$swordPath = Join-Path $assets "$huntTexture/valenna_hunt_swords_00.png"
Write-Picture $swordPath $huntSize $huntSize $swords
Write-Picture (Join-Path $assets "$huntTexture/valenna_hunt_glow_00.png") $huntSize $huntSize `
    ([ValennaBattle.HuntingMarkArt]::Glow($swords))
Stage-Image 'Resources/Texture/BuffIcon/valenna_hunting_prey_icon.png' $swordPath
Save-EffectSequence 'valenna_hunt_swords' 'valenna_hunt_swords_' 1 0.55 0.55
Save-EffectSequence 'valenna_hunt_glow' 'valenna_hunt_glow_' 1 0.55 0.55
Write-Picture (Join-Path $assets "$huntTexture/valenna_execution_frame.png") `
    ([ValennaBattle.HuntingMarkArt]::FrameWidth) ([ValennaBattle.HuntingMarkArt]::FrameHeight) `
    ([ValennaBattle.HuntingMarkArt]::Frame())

$companionCount = [ValennaBattle.StrikeProfiles]::CompanionFrames
$companionName = 'valenna_companion_sweep'
for ($index = 0; $index -lt $companionCount; $index++) {
    $key = 'valenna_companion_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$companionName/idle/$key.png"
    Write-Picture $path $frameSize $frameSize ($painter.Draw($index, $true))
}
Save-EffectSequence $companionName 'valenna_companion_frame_' $companionCount 4.2 4.2

# 亮刃单独烘焙前段，后续的红弧、剑风和碎屑共用原帧。
$executionFrames = [ValennaBattle.StrikeProfiles]::ExecutionBrightFrames
$attackFrames = [ValennaBattle.StrikeProfiles]::AttackBrightFrames
$extraLight = [ValennaBattle.StrikeProfiles]::ExtraBladeLightFrames
foreach ($blade in @(
    @{ Name = 'valenna_execution_sweep'; Prefix = 'valenna_execution_slash_'; Companion = $false
        Count = $frameCount; Tail = 'valenna_slash_frame_'; Bright = $executionFrames; Execution = $true; Extra = 0 },
    @{ Name = 'valenna_execution_companion'; Prefix = 'valenna_execution_companion_'; Companion = $true
        Count = $companionCount; Tail = 'valenna_companion_frame_'; Bright = $executionFrames; Execution = $true; Extra = 0 },
    @{ Name = 'valenna_attack_sweep'; Prefix = 'valenna_attack_slash_'; Companion = $false
        Count = $frameCount; Tail = 'valenna_slash_frame_'; Bright = $attackFrames; Execution = $false; Extra = $extraLight },
    @{ Name = 'valenna_peak_sweep'; Prefix = 'valenna_peak_slash_'; Companion = $false; Peak = $true
        Count = $frameCount; Tail = 'valenna_slash_frame_'; Bright = $attackFrames; Execution = $false; Extra = $extraLight },
    @{ Name = 'valenna_attack_companion'; Prefix = 'valenna_attack_companion_'; Companion = $true
        Count = $companionCount; Tail = 'valenna_companion_frame_'; Bright = $attackFrames; Execution = $false; Extra = $extraLight }
)) {
    for ($index = 0; $index -lt $blade.Bright; $index++) {
        $key = '{0}{1:D2}' -f $blade.Prefix, $index
        $path = Join-Path $texture "Effect/$($blade.Name)/idle/$key.png"
        Write-Picture $path $frameSize $frameSize ($painter.Draw($index, $blade.Companion, $blade.Execution, $blade.Extra, [bool]$blade.Peak))
    }
    Save-EffectSequence $blade.Name $blade.Prefix $blade.Count 4.2 4.2 $blade.Bright $blade.Tail
}

$primeSequences = @(
    @{ Eye = $true; Name = 'valenna_eye_glint'; Prefix = 'valenna_eye_frame_'; Pixels = 560
        Count = [ValennaBattle.StrikeProfiles]::EyeFrames
        Width = [ValennaBattle.StrikeProfiles]::EyeWidth; Height = [ValennaBattle.StrikeProfiles]::EyeHeight },
    @{ Eye = $false; Name = 'valenna_sheath_glint'; Prefix = 'valenna_sheath_frame_'; Pixels = 384
        Count = [ValennaBattle.StrikeProfiles]::SheathFrames
        Width = [ValennaBattle.StrikeProfiles]::SheathWidth; Height = [ValennaBattle.StrikeProfiles]::SheathHeight }
)
foreach ($prime in $primeSequences) {
    for ($index = 0; $index -lt $prime.Count; $index++) {
        $key = '{0}{1:D2}' -f $prime.Prefix, $index
        $path = Join-Path $texture "Effect/$($prime.Name)/idle/$key.png"
        Write-Picture $path $prime.Pixels 68 ($painter.DrawPrime($prime.Pixels, 68, ($index + .50) * $frameTime, $prime.Eye))
    }
    Save-EffectSequence $prime.Name $prime.Prefix $prime.Count $prime.Width $prime.Height
}

$aimCount = [ValennaBattle.StrikeProfiles]::AimFrames
$aimName = 'valenna_aim_charge'
for ($index = 0; $index -lt $aimCount; $index++) {
    $key = 'valenna_aim_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$aimName/idle/$key.png"
    Write-Picture $path 256 192 ($painter.DrawAim(256, 192, ($index + .5) / $aimCount))
}
Save-EffectSequence $aimName 'valenna_aim_frame_' $aimCount ([ValennaBattle.StrikeProfiles]::MuzzleRingWidth) ([ValennaBattle.StrikeProfiles]::MuzzleRingHeight)

$shotCount = [ValennaBattle.StrikeProfiles]::ShotFrames
$shotName = 'valenna_shot'
$thrustMask = Read-Mask 'Fx_T_Shape_Slash_09_hw.png'
for ($index = 0; $index -lt $shotCount; $index++) {
    $key = 'valenna_shot_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$shotName/idle/$key.png"
    Write-Picture $path 830 192 ($painter.DrawShot(830, 192, ($index + .54) * $frameTime, $thrustMask))
}
Save-EffectSequence $shotName 'valenna_shot_frame_' $shotCount 4.80 0.70

$wakeCount = [ValennaBattle.StrikeProfiles]::WakeFrames
$wakeName = 'valenna_gun_wake'
for ($index = 0; $index -lt $wakeCount; $index++) {
    $key = 'valenna_wake_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$wakeName/idle/$key.png"
    Write-Picture $path 480 168 ($painter.DrawWake(480, 168, ($index + .5) / $wakeCount))
}
Save-EffectSequence $wakeName 'valenna_wake_frame_' $wakeCount `
    ([ValennaBattle.StrikeProfiles]::WakeWidth) ([ValennaBattle.StrikeProfiles]::WakeHeight)

$pierceCount = [ValennaBattle.StrikeProfiles]::PierceFrames
$pierceName = 'valenna_finish_pierce'
for ($index = 0; $index -lt $pierceCount; $index++) {
    $key = 'valenna_pierce_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$pierceName/idle/$key.png"
    Write-Picture $path 540 214 ($painter.DrawPierce(540, 214, ($index + .59) * $frameTime,
        $thrustMask, [ValennaBattle.ThrustPattern]::Spread))
}
Save-EffectSequence $pierceName 'valenna_pierce_frame_' $pierceCount 3.8 1.45

$twinCount = [ValennaBattle.StrikeProfiles]::TwinThrustFrames
$twinName = 'valenna_twin_thrust'
for ($index = 0; $index -lt $twinCount; $index++) {
    $key = 'valenna_twin_thrust_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$twinName/idle/$key.png"
    Write-Picture $path 540 145 ($painter.DrawPierce(540, 145, ($index + .38) * $frameTime,
        $thrustMask, [ValennaBattle.ThrustPattern]::Twin, $extraLight))
}
Save-EffectSequence $twinName 'valenna_twin_thrust_frame_' $twinCount ([ValennaBattle.StrikeProfiles]::TwinWidth) ([ValennaBattle.StrikeProfiles]::TwinHeight)

$longThrustCount = [ValennaBattle.StrikeProfiles]::LongThrustFrames
$longThrustName = 'valenna_long_thrust'
for ($index = 0; $index -lt $longThrustCount; $index++) {
    $key = 'valenna_long_thrust_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$longThrustName/idle/$key.png"
    Write-Picture $path 560 110 ($painter.DrawPierce(560, 110, ($index + .56) * $frameTime,
        $thrustMask, [ValennaBattle.ThrustPattern]::Long, $extraLight))
}
Save-EffectSequence $longThrustName 'valenna_long_thrust_frame_' $longThrustCount `
    ([ValennaBattle.StrikeProfiles]::LongThrustWidth) ([ValennaBattle.StrikeProfiles]::LongThrustHeight)

$groundFlameCount = [ValennaBattle.StrikeProfiles]::GroundFlameFrames
$groundFlameName = 'valenna_ground_flame'
$fireMasks = @(0..3 | ForEach-Object { Read-Mask "Fx_T_Shape_Fire_01_2x2_$_.png" })
$smokeMask = Read-Mask 'Fx_T_Shape_Smoke_06.png'
for ($index = 0; $index -lt $groundFlameCount; $index++) {
    $key = 'valenna_ground_flame_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$groundFlameName/idle/$key.png"
    $fire = $fireMasks[[Math]::Floor($index / 2) % $fireMasks.Count]
    Write-Picture $path 112 222 ($painter.DrawGroundFlame(112, 222, ($index + .5) * $frameTime, $fire, $smokeMask))
}
Save-EffectSequence $groundFlameName 'valenna_ground_flame_frame_' $groundFlameCount `
    ([ValennaBattle.StrikeProfiles]::GroundFlameWidth) ([ValennaBattle.StrikeProfiles]::GroundFlameHeight)

Write-Picture (Join-Path $texture 'Effect/valenna_vfx/valenna_hit_line.png') 496 18 ([ValennaBattle.SlashFramePainter]::DrawHitLine(496, 18))
$impactCount = [ValennaBattle.StrikeProfiles]::ImpactFrames
$impactName = 'valenna_impact_fx'
for ($index = 0; $index -lt $impactCount; $index++) {
    $key = 'valenna_impact_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$impactName/idle/$key.png"
    Write-Picture $path 384 288 ($painter.DrawImpact(384, 288, ($index + .40) * $frameTime))
}
Save-EffectSequence $impactName 'valenna_impact_frame_' $impactCount 2.36 1.77

$parryCount = [ValennaBattle.StrikeProfiles]::ParryFrames
$parryName = 'valenna_parry_fx'
for ($index = 0; $index -lt $parryCount; $index++) {
    $key = 'valenna_parry_frame_{0:D2}' -f $index
    $path = Join-Path $texture "Effect/$parryName/idle/$key.png"
    Write-Picture $path 320 320 ($painter.DrawParry(320, ($index + .60) * $frameTime))
}
Save-EffectSequence $parryName 'valenna_parry_frame_' $parryCount 1.48 1.48

$resourceMap = [ordered]@{}
foreach ($group in ([ordered]@{ '1001' = 'HeadIcon'; '1004' = 'ItemIcon'; '1007' = 'BuffIcon'; '1018' = 'Charator'; '1023' = 'Effect' }).GetEnumerator()) {
    $resourceMap[$group.Key] = @(Get-ChildItem -LiteralPath (Join-Path $texture $group.Value) -Recurse -File -Filter '*.png' |
        Sort-Object FullName | ForEach-Object {
            $path = [IO.Path]::GetRelativePath($assets, $_.FullName).Replace('\', '/')
            [ordered]@{ Key = $_.BaseName; Path = $path.Substring(0, $path.Length - 4); Ext = '.png' }
        })
}
$mapEnvelope = [ordered]@{
    ResourceReferenceConfigure = [ordered]@{
        __type = 'ResourceReferenceConfigure,Assembly-CSharp'
        value = [ordered]@{ ResourceMap = $resourceMap }
    }
}
$mapJson = ($mapEnvelope | ConvertTo-Json -Depth 8) -replace '(?m)^(\s*)"(1001|1004|1007|1018|1023)"(?=\s*:)', '$1$2'
Save-Text 'Resources/Config/InternalConfigure.txt' $mapJson

Copy-Item -LiteralPath (Join-Path $project 'Project.rtmeta') -Destination (Join-Path $stagedProject 'Project.rtmeta')
foreach ($png in Get-ChildItem -LiteralPath $texture -Recurse -File -Filter '*.png') {
    $key = [IO.Path]::GetRelativePath($stagedProject, $png.FullName) + '.rtmeta'
    $old = Join-Path $project $key
    if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination (Join-Path $stagedProject $key) }
}
$sync = Join-Path (Split-Path -Parent $mod) '跑团伙伴/tools/sync_editor_textures.ps1'
& $sync -ProjectPath $stagedProject

$written = [Collections.Generic.List[object]]::new()
try {
    foreach ($source in Get-ChildItem -LiteralPath $stagedProject -Recurse -File) {
        $key = [IO.Path]::GetRelativePath($stagedProject, $source.FullName)
        $destination = Join-Path $project $key
        $previous = Join-Path $stage "previous/$key"
        $present = [IO.File]::Exists($destination)
        $sum = (Get-FileHash -LiteralPath $source.FullName).Hash
        if ($present -and (Get-FileHash -LiteralPath $destination).Hash -eq $sum) { continue }
        if ($present) {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($previous))
            [IO.File]::Copy($destination, $previous)
        }
        $written.Add(@{ Path = $destination; Previous = $previous; Present = $present })
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::Copy($source.FullName, $destination, $true)
        if ((Get-FileHash -LiteralPath $destination).Hash -ne $sum) { throw "演出资源写入校验失败 $key" }
    }
} catch {
    foreach ($change in $written) {
        if ($change.Present) { [IO.File]::Copy($change.Previous, $change.Path, $true) }
        elseif ([IO.File]::Exists($change.Path)) { [IO.File]::Delete($change.Path) }
    }
    throw
}
$verified = (Resolve-Path -LiteralPath $stage).Path
$parent = (Resolve-Path -LiteralPath $tempRoot).Path
if (-not $verified.StartsWith($parent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '演出暂存路径错误' }
Remove-Item -LiteralPath $verified -Recurse
$effectFrames = @($resourceMap['1023'] | Where-Object { $_.Path -like '*/idle/*' }).Count
Write-Output "已绑定头像和 $($poses.Count) 张人物姿势，生成 $($idleFrames.Count) 帧待机呼吸，特效 idle 序列共 $effectFrames 帧"
