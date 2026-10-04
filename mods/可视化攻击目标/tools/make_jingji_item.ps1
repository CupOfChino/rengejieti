# 生成茉莉专属武器「荆棘」（副手剑）的数据文件。
#
# 做法和「葬花」一样：拿游戏本体 10680「蔷薇黑剑」做底子改字段，
# 只动我们需要的部分（ES3 反序列化按字段名读，整体结构不能自己造）。
#
# 用法：powershell -File tools\make_JingJi_item.ps1
#
# 数据侧只写"静态"的部分：
#   · 名字 / 描述 / 图标引用 / 神话 tag / 绑定角色
#   · 武器本体：单手、近战、1D4+2、斗殴检定、单段（行动 1 次；
#     插件会把这 1 次攻击的伤害完整结算 3 次、每次各算一遍护甲 —— 2026-09-27 用户口径）
#   · 装备时 / 卸下时的属性加成：敏捷 +5、斗殴 +5、速度 +20
# 需要运行时判断的（主手 / 副手 / 双手、联合检定、伤害拆分、先发攻击）都在插件里做，
# 见 src\JingJi.cs。

param(
  [string]$SourceItem = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\StreamingAssets\Game\Item\10680.txt',
  [string]$ModRoot    = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$dstFile = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Item\880006.txt'

if (-not (Test-Path -LiteralPath $SourceItem)) { throw "找不到底子文件：$SourceItem" }

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

# 装备时加：敏捷 +5、斗殴 +5、速度 +20
$buffDatas = @(
  (New-ChangeAttr -Attr 2   -Value '5'),    # 敏捷
  (New-ChangeAttr -Skill 101 -Value '5'),   # 斗殴
  (New-ChangeAttr -ExAttr 119 -Value '20')  # 速度
)

# ---- 基础信息 ----
$v.ItemId = 880006
$v.ItemName.InputText = "荆棘"
$v.ItemDes.InputText = "荆棘缠于刃上，旧日的记忆再不放手。"
# 从蔷薇黑剑抄来的本地化 key 必须清掉 —— TarKey 非空且本地化表里查得到时，
# 游戏会拿表里的文案把 InputText 顶掉（这里会显示成「蔷薇黑剑」）。见 docs\AI协作文档.md 19.1
$v.ItemName.TarKey = ""
$v.ItemDes.TarKey = ""
$v.ItemEffectDes.TarKey = ""
$v.ItemEffectDes.InputText = "荆棘：攻击时进行斗殴检定；命中后本次伤害结算 3 次（3 个伤害数字，护甲只结算 1 次）。`n另外进行一次隐藏的敏捷检定：失败则本次不叠【荆棘】；成功则每次伤害各叠 1 层；大成功再额外叠 1 层。`n普通成功 / 困难成功 / 大成功：本次伤害 ×0.5 / ×0.75 / ×1.0。`n装备时：敏捷+5、斗殴+5、速度+20。`n速度差：每高于目标100点，此武器伤害+10%（双手持握时每50点一档）。`n【持握·荆棘】"

# ---- 图标（导入 PNG 时用的 key）----
$v.IconPath = "UI/Textures/Icon/Item/icon_item_moli_fushoujian"
$v.IconPathReference.Key = "icon_item_moli_fushoujian"

# ---- 物品属性：神话物品(3) + 不可出售(6)，和葬花一致 ----
$v.ItemAttrValue = @(3, 6)
$v.MythicalId = 26
$v.IsBindCharacter = $true
$v.ChargingCount = 0

# ---- 武器本体：单手、近战、1D4+2、斗殴检定、3 段 ----
# 武器类型：灵活型(2) + 技巧型(3) —— 特意**不带力量型(1)**。
# 游戏里「顺劈」这个战斗技能（BattleSkill 5）带 TriggerWeaponType = 1（只认力量型武器），
# 荆棘是刺剑，不该能顺劈（2026-09-26 用户反馈），所以不挂力量型这个 tag。
$v.EquipmentConfigData.WeaponTypes = @(2, 3)
$v.EquipmentConfigData.DoubleWeapon = $false
$v.EquipmentConfigData.CheckSkill = 101          # 斗殴
$v.EquipmentConfigData.UniteCheckSkill = 0       # 联合检定的"敏捷"由插件补（枚举里只有技能，没有属性）
$v.EquipmentConfigData.DiceCheckType = 1
$v.EquipmentConfigData.ResultCheckType = 1
$v.EquipmentConfigData.ContinuousAttackCount = 1 # 单段：行动 1 次；伤害由插件完整结算 3 次（2026-09-27）
$v.EquipmentConfigData.AttackCount = 1
$v.EquipmentConfigData.AttackType = 1            # 近战
$v.EquipmentConfigData.TargetSelect = 2
$v.EquipmentConfigData.Damage.DamageType = 0
$v.EquipmentConfigData.Damage.Value = "1D4+2"
$v.EquipmentConfigData.Damage.IsNotBlock = $false

# ---- 命中表现：原版「突刺」特效（FXSkillHit_Puncture + weapon_knife 刀音效）----
# 和葬花分开：葬花走通用「利刃受击」（斩击），荆棘走自定义表演序列（突刺），两边长得不一样。
# 结构照抄游戏自带 CommonBattleEffectShow\4.txt（利刃受击），只换特效/音效；
# 放在武器的 DamageData.EffectShow 里由游戏在主动攻击命中时原生播放，
# 插件只负责"先发 / 灰暗孤影反击"这种没有表现链的白送攻击（见 src\JingJi.cs）。
$v.EquipmentConfigData.Damage.UseCommonEffect = $false
$v.EquipmentConfigData.Damage.EffectShow = [ordered]@{
  Duration    = 0.7
  EffectShows = @(
    [ordered]@{
      '__type'        = 'MOD.PlayRoleAnimationData,Assembly-CSharp'
      IsSelf          = $true
      AnimtorName     = 'Attack'
      SpriteAnimName  = 'attack'
      BackToDefault   = $true
      SpriteAnimFirst = $false
      StartTime       = 0
      DelayTime       = 0
    },
    [ordered]@{
      '__type'              = 'MOD.PlayRoleEffectData,Assembly-CSharp'
      IsSelf                = $false
      UseFrameEffect        = $false
      FxPath                = 'Effect/Prefabs/FXSkillHit_Puncture'
      FxPathReference       = [ordered]@{ ReferenceType = 0; Key = $null }
      EffectName            = $null
      EffectAnimName        = $null
      Duration              = 0.5
      PlayCenterPoint       = $false
      UseSameFx             = $true
      PointUseFrameEffect   = $false
      PlayCenterPointFxPath = $null
      PointFxPathReference  = [ordered]@{ ReferenceType = 0; Key = $null }
      PointEffectName       = $null
      PointEffectAnimName   = $null
      PointType             = 2
      IsGroupCenter         = $false
      DelayTime             = 0.1
    },
    [ordered]@{
      '__type'             = 'MOD.PlaySoundData,Assembly-CSharp'
      AudioClip            = [ordered]@{
        AudioRes       = 'Sound/Audio/FX/weapon_knife'
        AudioReference = [ordered]@{ ReferenceType = 2; Key = 'weapon_knife' }
        Volume         = 1
        FadeTime       = 1
      }
      IsLoop               = $false
      StopOnBattleFinish   = $false
      DelayTime            = 0.1
    },
    [ordered]@{
      '__type'   = 'MOD.PlayHitData,Assembly-CSharp'
      DelayTime  = 0.2
    }
  )
}
$v.EquipmentConfigData.Def.ArmorValue = 0

# ---- 装备时 / 卸下时的属性加成 ----
$v.EquipmentConfigData.InfoDatas = @(
  (New-AttrInfoData -Datas $buffDatas -Remove $false),
  (New-AttrInfoData -Datas $buffDatas -Remove $true)
)

# ---- 效果描述（TarKey 必须留空，否则会被本地化表里的旧文案顶掉，见 AI协作文档 19.1）----
$v.EquipmentConfigData.EnableOverrideDesc = $true
$v.EquipmentConfigData.OverrideDesc.TarKey = ""
$v.EquipmentConfigData.OverrideDesc.SheetKey = ""
$v.EquipmentConfigData.OverrideDesc.InputText = "荆棘：攻击时进行斗殴检定；命中后本次伤害结算 3 次（3 个伤害数字，护甲只结算 1 次）。`n另外进行一次隐藏的敏捷检定：失败则本次不叠【荆棘】；成功则每次伤害各叠 1 层；大成功再额外叠 1 层。`n普通成功 / 困难成功 / 大成功：本次伤害 ×0.5 / ×0.75 / ×1.0。`n装备时：敏捷+5、斗殴+5、速度+20。`n速度差：每高于目标100点，此武器伤害+10%（双手持握时每50点一档）。`n【持握·荆棘】"

# ---- 关键词术语（装备面板右侧那栏）：880002 = 先发 ----
$v.Terms = @(880002, 880004, 880005, 880006)

$out = $json | ConvertTo-Json -Depth 100
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstFile) | Out-Null
[System.IO.File]::WriteAllText($dstFile, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("已生成：{0}" -f $dstFile)
Write-Output ("  名字={0} 伤害={1} 连击={2} 单手={3} 检定技能={4} 属性={5} InfoDatas={6} 条" -f `
  $v.ItemName.InputText, $v.EquipmentConfigData.Damage.Value, $v.EquipmentConfigData.ContinuousAttackCount, `
  (-not $v.EquipmentConfigData.DoubleWeapon), $v.EquipmentConfigData.CheckSkill, `
  ($v.ItemAttrValue -join ','), $v.EquipmentConfigData.InfoDatas.Count)
