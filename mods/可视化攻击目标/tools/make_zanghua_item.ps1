# 生成茉莉专属武器「葬花」的数据文件。
#
# 做法：不手写整份 JSON，而是拿游戏本体 10680「蔷薇黑剑」做底子改字段 ——
# 它的结构（武器配置、伤害配置、序列化字段）已经是游戏认得的样子，
# 只改我们需要的部分最不容易踩坑（尤其 ES3 反序列化是按字段名读的）。
#
# 用法：powershell -File tools\make_zanghua_item.ps1

param(
  [string]$SourceItem = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\StreamingAssets\Game\Item\10680.txt',
  [string]$ModRoot    = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$dstFile = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Item\880001.txt'

if (-not (Test-Path -LiteralPath $SourceItem)) { throw "找不到底子文件：$SourceItem" }

# 游戏的数据文件里用了自己的转义习惯（例如 \"全角引号" 写成 \“ ”），不是合法 JSON，
# 直接喂给 ConvertFrom-Json 会报 "Bad JSON escape sequence"。先把这类多余的反斜杠去掉。
$raw = Get-Content -LiteralPath $SourceItem -Encoding UTF8 -Raw
$raw = $raw -replace '\\“', '“' -replace '\\”', '”'
$json = $raw | ConvertFrom-Json
$v = $json.Data.value

# ---- 属性变化条目（对应 MOD.ChangeAttrData）----
# RoleAttr = EHeroAttribute（1 力量 / 2 敏捷 / 3 智力 / 4 意志 / 6 体质）
# RoleExAttr = ERoleExtraAttribute（112 护甲 / 118 闪避 / 119 速度 …）
# RoleSkill = EExploreSkill（101 斗殴）
function New-ChangeAttr {
  param([int]$Attr = 0, [int]$ExAttr = 0, [int]$Skill = 0, [string]$Value = '')
  [ordered]@{
    IsTrackSource  = $true
    RoleAttr       = [ordered]@{ Type = $Attr;    Value = $(if ($Attr  -ne 0) { $Value } else { $null }); IsRatio = $false; Ratio = 0 }
    RoleExAttr     = [ordered]@{ Type = $ExAttr;  Value = $(if ($ExAttr -ne 0) { $Value } else { $null }); IsRatio = $false; Ratio = 0 }
    RoleSkill      = [ordered]@{ Type = $Skill;   Value = $(if ($Skill -ne 0) { $Value } else { $null }); IsRatio = $false; Ratio = 0 }
    RoleSystemAttr = [ordered]@{ Type = 0;        Value = $null;  IsRatio = $false; Ratio = 0 }
    ExDiceData     = [ordered]@{
      ExploreSkill   = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
      HeroAttribute  = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
      ExtraAttribute = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
    }
  }
}

function New-AttrInfoData {
  param([object[]]$Datas, [bool]$Remove)
  [ordered]@{
    DisableTrigger = $false
    TriggerType    = $(if ($Remove) { 7 } else { 2 })   # 7 = 卸下时，2 = 装备时
    ItemCheckList  = @()
    BaseOptions    = @(
      [ordered]@{
        '__type'          = 'MOD.Item_RoleAttrOption,Assembly-CSharp'
        IsConversionBuff  = $false
        Datas             = $Datas
        IsRemove          = $Remove
        SourceKey         = $null
      }
    )
  }
}

# ---- 基础信息 ----
$v.ItemId = 880001
$v.ItemName.InputText = '<color=#C7ADFF>葬花</color>'
$v.ItemDes.InputText = "花瓣落尽之处，旧日的记忆随之入土。"
# 从蔷薇黑剑抄来的本地化 key 必须清掉 —— TarKey 非空且本地化表里查得到时，
# 游戏会拿表里的文案把 InputText 顶掉（这里会显示成「蔷薇黑剑」）。见 docs\AI协作文档.md 19.1
$v.ItemName.TarKey = ""
$v.ItemDes.TarKey = ""
$v.ItemEffectDes.TarKey = ""
$v.ItemEffectDes.InputText = "以<color=#C7ADFF>【充能斩】</color>追加攻击段数。每段造成伤害叠1层<color=#85CCFF>【剑痕】</color>，目标每有1层，此击伤害+2%。`n装备：斗殴+10、力量+5、速度-5。见<color=#C7ADFF>【持握·葬花】</color>。"
# （Item 数据没有 Comment 字段，说明写在 ItemEffectDes 里了；这份数据由 tools\make_zanghua_item.ps1 生成）

# ---- 图标（我们做的那张茉莉剑）----
$v.IconPathReference.Key = "icon_item_moli_jian"

# ---- 物品属性：神话物品(3) + 不可出售(6)。去掉 8（法术伤害），这是物理武器 ----
$v.ItemAttrValue = @(3, 6)
$v.MythicalId = 26

# ---- 绑定角色（不可转移）。不可卸下不设（那是 IsCantDisboard，用户只要求不可转移）----
$v.IsBindCharacter = $true

# ---- 充能上限 12（初始值由插件写，游戏这个字段同时管上限和初始，所以初始 7 得插件补）----
$v.ChargingCount = 12

# ---- 武器本体：单手、近战、2 连击 ----
# 武器类型：灵活型(2) + 技巧型(3)。
# 2026-09-26 用户口径：葬花是长剑不是力量型武器，数据里不带力量型(1)；
# 但它"主手 / 视为双手"时应该能施展【横扫】，那由插件临时补上力量型标签来实现（ZangHua.SyncWeaponType）。
$v.EquipmentConfigData.WeaponTypes = @(2, 3)
$v.EquipmentConfigData.DoubleWeapon = $false
$v.EquipmentConfigData.ContinuousAttackCount = 2
$v.EquipmentConfigData.AttackCount = 1
$v.EquipmentConfigData.AttackType = 1
$v.EquipmentConfigData.TargetSelect = 2
$v.EquipmentConfigData.Damage.DamageType = 0
$v.EquipmentConfigData.Damage.Value = "1D8+1"
$v.EquipmentConfigData.Damage.IsNotBlock = $false

# ---- 命中表现：保留原版利刃受击的动作、声音和时序 ----
# 预制体条目的 EffectName 标记用于插件识别，设置缩小的单体斩击或宽扁的横扫。
$v.EquipmentConfigData.Damage.UseCommonEffect = $false
$v.EquipmentConfigData.Damage.CommonEffectId = 4
$v.EquipmentConfigData.Damage.EffectShow = [ordered]@{
  Duration = 0.6
  EffectShows = @(
    [ordered]@{
      '__type' = 'MOD.PlayRoleAnimationData,Assembly-CSharp'
      IsSelf = $true
      AnimtorName = 'Attack'
      SpriteAnimName = 'attack'
      SpriteAnimFirst = $false
      DelayTime = 0
    },
    [ordered]@{
      '__type' = 'MOD.PlayRoleEffectData,Assembly-CSharp'
      IsSelf = $false
      FxPath = 'Effect/Prefabs/FX_BladeHit'
      Duration = 0.5
      PlayCenterPoint = $false
      UseSameFx = $true
      PlayCenterPointFxPath = $null
      PointType = 2
      IsGroupCenter = $false
      DelayTime = 0.1
      FxPathReference = [ordered]@{
        ReferenceType = 10001
        Key = 'FX_BladeHit'
      }
      UseFrameEffect = $false
      EffectName = 'ZangHuaSlash'
    },
    [ordered]@{
      '__type' = 'MOD.PlaySoundData,Assembly-CSharp'
      AudioClip = [ordered]@{
        AudioRes = 'Sound/Audio/FX/Battle/blade_hit'
        AudioReference = [ordered]@{
          ReferenceType = 2
          Key = 'blade_hit'
        }
        Volume = 1
        FadeTime = 1
      }
      DelayTime = 0.1
    },
    [ordered]@{
      '__type' = 'MOD.PlayHitData,Assembly-CSharp'
      DelayTime = 0.2
    }
  )
}

# ---- 装备时 / 卸下时的属性加成：斗殴 +10、力量 +5、速度 -5（2026-09-23 用户要求）----
# 充能相关的数据层效果全部清掉（蔷薇黑剑那套"满充能消耗3点额外攻击"不是我们要的，改由插件做）
$buffDatas = @(
  (New-ChangeAttr -Skill 101 -Value '10'),   # 斗殴 +10
  (New-ChangeAttr -Attr 1    -Value '5'),    # 力量 +5
  (New-ChangeAttr -ExAttr 119 -Value '-5')   # 速度 -5
)
$v.EquipmentConfigData.InfoDatas = @(
  (New-AttrInfoData -Datas $buffDatas -Remove $false),
  (New-AttrInfoData -Datas $buffDatas -Remove $true)
)

# ---- 效果描述：底子是蔷薇黑剑，它自带一份 OverrideDesc（TarKey + 它自己的充能文案）。
#      不覆盖的话装备面板上显示的就是**蔷薇黑剑的效果描述**（2026-09-22 才发现）。
#      注意 TarKey 必须留空：非空时游戏会去本地化表按 key 取值，把我们写的 InputText 顶掉。
#      机制细节见 docs\AI协作文档.md 19.1 ----
$v.EquipmentConfigData.EnableOverrideDesc = $true
$v.EquipmentConfigData.OverrideDesc.TarKey = ""
$v.EquipmentConfigData.OverrideDesc.SheetKey = ""
$v.EquipmentConfigData.OverrideDesc.InputText = $v.ItemEffectDes.InputText

# ---- 关键词术语（装备面板右侧那栏）：880001 = 剑痕 ----
# 剑痕那条长解释从这里挪到 Game\TermData\880001.txt，描述里只留【剑痕】三个字，
# 面板就不会被一大段说明撑满。术语由 tools\make_secret_terms.ps1 生成。
$v.Terms = @(880007, 880001, 880003)

# 剑痕通过 Terms 中的 880001 显示。状态本体独立显示，免得在装备右栏重复。
$v.BuffsLink = @()

$out = $json | ConvertTo-Json -Depth 100
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstFile) | Out-Null
[System.IO.File]::WriteAllText($dstFile, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("已生成：{0}" -f $dstFile)
Write-Output ("  名字={0} 伤害={1} 连击={2} 单手={3} 充能上限={4} 属性={5}" -f `
  $v.ItemName.InputText, $v.EquipmentConfigData.Damage.Value, $v.EquipmentConfigData.ContinuousAttackCount, `
  (-not $v.EquipmentConfigData.DoubleWeapon), $v.ChargingCount, ($v.ItemAttrValue -join ','))
