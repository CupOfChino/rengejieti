param(
    [string]$GameTables = 'E:/SteamLibrary/steamapps/common/Depersonalization/Depersonalization-Release_Data/StreamingAssets/Game'
)

$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$project = Join-Path $mod 'Project_Depersonal'
$utf8 = [Text.UTF8Encoding]::new($false)
$tables = [ordered]@{}
if (-not ('ValennaBattle.StrikeProfiles' -as [type])) {
    Add-Type -Path (Join-Path $mod 'src/StrikeProfiles.cs')
}

function Read-Table([string]$Path) {
    (Get-Content -LiteralPath (Join-Path $GameTables "$Path.txt") -Raw -Encoding UTF8 |
        ConvertFrom-Json -AsHashtable).Data.value
}

function Text-Data([string]$Text) {
    [ordered]@{ TarKey = ''; SheetKey = ''; InputText = $Text; Characteristic = $null }
}

function Ids([int]$Type, [int[]]$Values) {
    [ordered]@{ Type = $Type; Ids = @($Values) }
}

function Add-Table([string]$Path, [string]$Type, $Value) {
    $tables["Assets/Resources/Config/Game/$Path.txt"] = [ordered]@{
        Data = [ordered]@{ __type = "$Type,Assembly-CSharp"; value = $Value }
    }
}

function Attr-Change([string]$Field, [int]$Type, [string]$Value, [bool]$Track = $true) {
    $change = (Read-Table 'Trait/1').CreateEvent.Inherent[0].Datas[0]
    $change.IsTrackSource = $Track
    $change.RoleAttr.Type = 0
    $change.RoleAttr.Value = $null
    $change[$Field].Type = $Type
    $change[$Field].Value = $Value
    $change
}

function Trait-Effects($Changes, $Extra = @()) {
    $inherent = @(
        if ($Changes.Count) {
            [ordered]@{ __type = 'MOD.TraitEvent.ChangeRoleValueDataOption,Assembly-CSharp'
                TargetType = 1; Datas = @($Changes); IsRemove = $false }
        }
        $Extra | Where-Object { $null -ne $_ }
    )
    [ordered]@{ Localization_Functin = Text-Data ''; Inherent = $inherent; Triggers = @() }
}

$moves = [ValennaBattle.StrikeProfiles]::Shows
$statusLinks = @{
    885121 = @(885131, 885132)
    885127 = @(885137)
    885128 = @(885132, 885134, [ValennaBattle.StrikeProfiles]::BurnId)
    885129 = @(885139)
}

$partner = Read-Table 'PartnerData/6'
$partner.PartnerId = 885101
$partner.DefaultUnLock = $true
$partner.ModuleKey = ''
$partner.UseNamePool = $false
$partner.CharacterName = Text-Data '瓦伦希娜'
$partner.CharacterDes = Text-Data '<color=#FF4040>二老板</color> · 手指 · 拇指 · 蜘蛛巢 · 战争英雄 · <color=#FF4040>七大家族</color> · 烟霾战争'
$partner.HeadCoverIcon = [ordered]@{ ReferenceType = 1001; Key = 'valenna_portrait' }
$partner.CharacterIconReference = $partner.HeadCoverIcon
$partner.RoleModeName = 'valenna_test_model'
$partner.ModelPathReference = [ordered]@{ ReferenceType = 10002; Key = 'valenna_test_model' }
$partner.Strength = 60
$partner.Dexterity = 60
$partner.Intelligence = 50
$partner.WillPower = 50
$partner.Constitution = 80 # 酒鬼特质扣 10，初始面板为 70。
$partner.Luck = 50
$partner.Armor = 0
$skills = [ordered]@{
    '101' = 80; '102' = 65; '201' = 60; '203' = 40
    '301' = 50; '302' = 50; '403' = 50; '405' = 35
    '501' = 30; '502' = 20; '601' = 30; '603' = 30
}
$partner.SkillSets = @(foreach ($entry in $skills.GetEnumerator()) {
    [ordered]@{ Id = [int]$entry.Key; Value = $entry.Value }
})
foreach ($field in 'MythicalItemList', 'MagicUnlockList', 'EndingKeys', 'ExItems', 'BattleTips', 'CharacterTags') {
    $partner[$field] = @()
}
foreach ($field in 'ItemBagList', 'ItemEquipmentList', 'ItemWeaponList', 'ItemBagBindList', 'ItemEquipmentBindList', 'ItemPoolList') {
    $partner[$field] = Ids 7 @()
}
$partner.CustomTags = @('手指', '拇指', '蜘蛛巢', '战争英雄', '烟霾战争')
$partner.ItemWeaponBindList = Ids 7 @(885111, 885113)
$partner.ItemBagBindList = Ids 7 @(885114, 885115)
$partner.ItemEquipmentBindList = Ids 7 @(885116)
$partner.MythicalItemList = @([ordered]@{ Grade = 1; Index = [ValennaBattle.StrikeProfiles]::EyeItemId })
$partner.Specialist = Ids 2 @((885141..885147) + [ValennaBattle.StrikeProfiles]::HeartTraitId)
$partner.Magiclist = Ids 6 @()
$partner.Bufflist = Ids 5 @()
$partner.CharacterSkillList = Ids 8 @($moves.Id)
$partner.MonsterSkillList = Ids 9 @()
$partner.CanControlInBattle = $true
$partner.AI = [ordered]@{
    CommonActions = @([ordered]@{
        IsMonster = $false; IsMagic = $false; IsItemUsage = $false; IsThrowItem = $false
        Name = $moves[0].Name; Id = $moves[0].Id
        RandomUseItem = $false; ItemId = 0; ItemConfigIndex = -1; PreferData = $null
    })
    FixedOrderAction = $false; RandomAction = $false; ReserveAfterSelect = $false; Strategys = @()
}
Add-Table 'PartnerData/885101' 'PartnerData' $partner

$traitId = [ValennaBattle.StrikeProfiles]::TraitId
$trait = [ordered]@{
    __type = 'MOD.TraitData,Assembly-CSharp'
    Id = $traitId
    Type = 4 # 经历
    CanRepeate = $false
    IsCanCreateGet = $false
    Localization_Name = Text-Data '巴勒莫之剑'
    Localization_Des = Text-Data (('隐匿-10。', '攻击栏使用七项巴勒莫剑术。', '同时拥有「狩猎时刻」时，开战自动指定狩猎目标。') -join "`n")
    BuffsLink = @()
    Terms = @()
    CreateEvent = Trait-Effects @((Attr-Change 'RoleSkill' 203 '-10'))
    WakeEvent = $null
}
Add-Table "Trait/$traitId" 'BaseSheetData' $trait
$prescienceId = [ValennaBattle.StrikeProfiles]::PrescienceTraitId
$prescience = [ordered]@{}
foreach ($entry in $trait.GetEnumerator()) { $prescience[$entry.Key] = $entry.Value }
$prescience.Id = $prescienceId
$prescience.Localization_Name = Text-Data '预知'
$prescience.Localization_Des = Text-Data (('进入剧本或战斗时，自动装备专属神话物品预知眼。', '预知眼未过热时，以[预知]替换闪避，斗殴检定获得1颗奖励骰。成功闪避敌方一次普通攻击的全部段数后，以「解兔」反击该敌人。') -join "`n")
$prescience.CreateEvent = $null
Add-Table "Trait/$prescienceId" 'BaseSheetData' $prescience
$heartSpeed = [ValennaBattle.StrikeProfiles]::HeartSpeed
$heartRules = "速度+${heartSpeed}。", '[苏醒期间] 战斗技能施加震颤时，额外施加1层。', '[苏醒期间] 通过战斗技能获得呼吸法时，额外获得1层。' -join "`n"
foreach ($definition in @(
    @{ Id = 885143; Name = '狩猎时刻'; Desc = (@(
       '[战斗开始时] 随机标记一名敌人为狩猎目标。'
       '对猎物造成的伤害+20%。'
       '猎物速度高于10时，速度-50，最低降至10。'
       '处置锁定猎物，攻击后解除标记。若处置击杀猎物，下轮标记另一名敌人。'
       '每3个行动轮结束时，若场上没有标记，下轮重新指定猎物。'
       '[击杀猎物时] 其余人类敌人进行意志检定，失败则陷入恐惧。'
    ) -join "`n"); Links = @(885139,128) },
    @{ Id = 885144; Name = '伯纳特利家族之耻'; Desc = (@(
       '[进入剧本时] 获得1000资金。'
       '进入剧本及通关时，将上等雪茄补至3支、高浓度伏特加补至1瓶，并补满加速弹。'
       '[战斗开始时] 加速弹补至10发。'
    ) -join "`n"); Links = @(885137) },
    @{ Id = 885145; Name = '战争英雄——瓦伦希娜'; Desc = ('意志+10。', '精神值上限+15。' -join "`n"); Changes = @((Attr-Change 'RoleAttr' 4 '10'))
       Extra = @([ordered]@{ __type = 'MOD.TraitEvent.ChangeMaxRoleValueDataOption,Assembly-CSharp'; ExtraAttr = 102; AddValue = '15' }) },
    @{ Id = 885146; Name = '长期酗酒的酒鬼'; Desc = ('斗殴-5，远程攻击-5，力量-5。', '体质-10，能工巧匠-10。' -join "`n"); Changes = @(
        (Attr-Change 'RoleSkill' 101 '-5'), (Attr-Change 'RoleSkill' 102 '-5'), (Attr-Change 'RoleAttr' 1 '-5'),
        (Attr-Change 'RoleAttr' 6 '-10'), (Attr-Change 'RoleSkill' 601 '-10')) },
    @{ Id = 885147; Name = '梦中的黄金票券'; Desc = '侦察+5，感知+5。'
       Changes = @((Attr-Change 'RoleSkill' 301 '5'), (Attr-Change 'RoleSkill' 302 '5')) },
    @{ Id = [ValennaBattle.StrikeProfiles]::HeartTraitId; Name = '心——瓦伦希娜';
       Desc = $heartRules;
       Changes = @((Attr-Change 'RoleExAttr' 119 ([string]$heartSpeed))) }
)) {
    $entry = [ordered]@{}
    foreach ($field in $prescience.GetEnumerator()) { $entry[$field.Key] = $field.Value }
    $entry.Id = $definition.Id
    if ($definition.Id -eq 885143) { $entry.Type = 2 } # 人格
    if ($definition.Id -eq 885147) { $entry.Type = 3 } # 信念
    $entry.Localization_Name = Text-Data $definition.Name
    $entry.Localization_Des = Text-Data $definition.Desc
    $entry.BuffsLink = @($definition.Links | Where-Object { $_ })
    if ($definition.Changes) { $entry.CreateEvent = Trait-Effects $definition.Changes $definition.Extra }
    Add-Table "Trait/$($entry.Id)" 'BaseSheetData' $entry
}
function New-TraitCheck([int]$Id) {
    [ordered]@{
        __type = 'BattleSkillEvent.FilterCheck,Assembly-CSharp'
        FilterType = 3
        SelfTraitFilter = [ordered]@{
            TraitFilter = @([ordered]@{ FilterType = 0; TraitId = $Id; Not = $false })
        }
        CompareType = 3
        CompareValue = 0
    }
}
$traitCheck = New-TraitCheck $traitId

foreach ($weapon in @(
    @{ Id = 885111; Key = 'valenna_stage_blade'; Icon = 'valenna_gunblade_icon'; Name = '巴勒莫——铳剑'; Damage = '1D6'; Lore = '瓦伦希娜斜挎在背后的铳剑。伯纳特利家族的定制加速弹用在贴身出剑的一瞬。'; Extra = (@(
        '[战斗开始时] 加速弹补至10发。'
        '[命中时] 若本段消耗了加速弹且射击检定成功，施加1层燃烧和1层震颤。'
        '[引爆时] 获得1层伤害强化，每行动轮最多获得3层。'
    ) -join "`n"); Buffs = @(885137) },
    @{ Id = 885113; Key = 'valenna_cutlass'; Icon = 'valenna_cutlass_icon'; Name = '巴勒莫——切斩刀'; Damage = '2D4'; Lore = '巴勒莫的另一把刀，刀鞘系在瓦伦希娜腰侧。她以这把单手刀使出切割攻击。'; Extra = (@(
        '[攻击时] 合计目标的燃烧与震颤层数。'
        '每层使伤害+5%，上限100%。'
        '目标尚有护甲时，每层改为伤害+10%，上限200%。'
    ) -join "`n"); Buffs = @() }
)) {
    $item = Read-Table 'Item/10502'
    $item.ItemId = $weapon.Id
    $item.UniqueKey = $weapon.Key
    $item.ItemName = Text-Data $weapon.Name
    $item.ItemDes = Text-Data $weapon.Lore
    $item.ItemEffectDes = Text-Data $weapon.Extra
    $item.IconPath = "UI/Textures/Icon/Item/$($weapon.Icon)"
    $item.IconPathReference = [ordered]@{ ReferenceType = 1004; Key = $weapon.Icon }
    $item.ItemDynamicAffixList = @()
    $item.ItemStaticAffixList = @()
    $item.BuffsLink = @($weapon.Buffs)
    $item.Price = 0
    $item.ThrowConfigData = $null
    $item.EquipmentConfigData.Arrts = @()
    $item.EquipmentConfigData.Damage.Value = $weapon.Damage
    $item.EquipmentConfigData.Damage.DB_Bonus = 0
    $item.EquipmentConfigData.Damage.AddAttrs = @()
    $item.EquipmentConfigData.EnableOverrideDesc = $true
    $item.EquipmentConfigData.OverrideDesc = Text-Data ("武器面板伤害$($weapon.Damage)。`n" + $weapon.Extra)
    Add-Table "Item/$($item.ItemId)" 'MOD.ItemData' $item
}

foreach ($supply in @(
    @{ Id = 885114; Key = 'valenna_cigars'; Name = '上等雪茄'; Count = 3; Restore = '1D2'; Source = 10640; Lore = '烟鬼的最爱。「这个烟鬼哪里的钱用定制的子弹和名贵雪茄？」'; Desc = ('满装3支。', '[品味雪茄] 消耗1支，花费1小时，恢复1D2点精神值。', '[叼在嘴里] 保留雪茄，精神值不变。' -join "`n") },
    @{ Id = 885115; Key = 'valenna_vodka'; Name = '高浓度伏特加'; Count = 1; Restore = '1D4'; Source = 10410; Lore = '酒鬼的最爱。瓦伦希娜总会为自己留上一瓶。'; Desc = ('携带1瓶。', '[饮用时] 恢复1D4点精神值，进入醉酒。', '醉酒持续12小时，期间每次检定获得1颗惩罚骰。' -join "`n") }
)) {
    $item = Read-Table "Item/$($supply.Source)"
    $template = (Read-Table 'Item/10410').ConfigData[0]
    $options = $template.ResultFuncs['2']
    $options[1].Datas[0].RoleExAttr.Value = $supply.Restore
    $use = @($options[0], $options[1])
    if ($supply.Id -eq 885115) {
        $options[2].BuffList = @(885150)
        $use += $options[2]
    }
    $template.LimitType = 2
    $template.TargetSelect = 1
    $template.ResultFuncs['2'] = $use
    $template.OverrideDesc = Text-Data $supply.Desc
    $item.ConfigData = @($template)
    $item.ItemId = $supply.Id
    $item.UniqueKey = $supply.Key
    $item.ChargingCount = $supply.Count
    $supplyIcon = if ($supply.Id -eq 885114) { 'valenna_cigar_icon' } else { 'valenna_vodka_icon' }
    $item.IconPath = "UI/Textures/Icon/Item/$supplyIcon"
    $item.IconPathReference = [ordered]@{ ReferenceType = 1004; Key = $supplyIcon }
    $item.AutoDestruct = $true
    $item.ItemName = Text-Data $supply.Name
    $item.ItemDes = Text-Data $supply.Lore
    $item.ItemEffectDes = Text-Data $supply.Desc
    $item.ItemStaticAffixList = @()
    $item.ItemDynamicAffixList = @()
    $item.GetItemOpts = @()
    $item.EndingOpts = @()
    $item.Terms = @()
    $item.BuffsLink = @(if ($supply.Id -eq 885115) { 885150 })
    Add-Table "Item/$($item.ItemId)" 'MOD.ItemData' $item
}
$cap = Read-Table 'Item/10640'
$cap.ItemId = 885116
$cap.UniqueKey = 'valenna_captain_hat'
$cap.ItemName = Text-Data '指挥官的帽子'
$cap.ItemDes = Text-Data '深红宽檐，帽身和帽檐镶着金色饰带。家族二级指挥官的标配。'
$capRules = '[装备时] 交涉+5。'
$cap.ItemEffectDes = Text-Data $capRules
$cap.IconPath = 'UI/Textures/Icon/Item/valenna_cap_icon'
$cap.IconPathReference = [ordered]@{ ReferenceType = 1004; Key = 'valenna_cap_icon' }
$cap.ConfigData = @()
$cap.Terms = @()
$cap.BuffsLink = @()
$cap.ItemStaticAffixList = @()
$cap.ItemDynamicAffixList = @()
$cap.GetItemOpts = @()
$cap.EndingOpts = @()
$cap.ChargingCount = 0
$cap.AutoDestruct = $false
$cap.ItemType = 3
$cap.ItemAttrValue = @()
$cap.EquipmentConfigData = (Read-Table 'Item/10135').EquipmentConfigData
$cap.EquipmentConfigData.EquipType = 0
$cap.EquipmentConfigData.Arrts = @((Attr-Change 'RoleSkill' 403 '5'))
$cap.EquipmentConfigData.Skills = @()
$cap.EquipmentConfigData.InfoDatas = @()
$cap.EquipmentConfigData.EnableOverrideDesc = $true
$cap.EquipmentConfigData.OverrideDesc = Text-Data $capRules
$cap.EquipmentConfigData.ConfirmEquipDesc = Text-Data ''
if ($cap.EquipmentConfigData.Contains('ShowConfirmEquipPopup')) {
    $cap.EquipmentConfigData.ShowConfirmEquipPopup = $false
}
Add-Table 'Item/885116' 'MOD.ItemData' $cap

$eye = Read-Table 'Item/10135'
$eye.ItemId = [ValennaBattle.StrikeProfiles]::EyeItemId
$eye.UniqueKey = 'valenna_precognition_eye'
$eye.ItemName = Text-Data '预知眼'
$eye.ItemDes = Text-Data '并非像魔法那样预测未来。这只眼睛更接近于通过分析进入视野的一切，预测即将到来的瞬间的计算机。被称为奥丁之眼的这件遗物，实际上是被分为三份的某件遗物的碎片。'
$eyeText = @(
    '瓦伦希娜的专属护具，装备后锁定。'
    '[回合开始] 未处于过热状态，则以[预知]替换你的闪避。'
    '未处于过热状态时，斗殴检定获得1颗奖励骰。'
    '[回合开始时] 战斗与探索均恢复5层充能，上限30层。'
    '[受击时] 损失5层充能。发动辅助闪避也消耗5层。'
    '[辅助闪避] 发动前充能超过10层，获得2颗奖励骰；否则获得1颗。'
    '同一次攻击中，闪避成功可继承奖励骰，失败后下一段可再次发动。'
    '[闪避成功时] 恢复1点精神值。开骰时未过热，另获得1层加速的未来。'
    '充能耗尽时转为预知眼·过热，闪避获得1颗惩罚骰。'
    '过热期间，每回合开始积累10层，满30层恢复。'
) -join "`n"
$eye.ItemEffectDes = Text-Data $eyeText
$eye.IconPath = 'UI/Textures/Icon/Item/valenna_eye_equip_icon'
$eye.IconPathReference = [ordered]@{ ReferenceType = 1004; Key = 'valenna_eye_equip_icon' }
$eye.Price = 0
$eye.ChargingCount = 0
$eye.AutoDestruct = $false
$eye.IsCantDisboard = $true
$eye.IsBindCharacter = $true
$eye.ItemAttrValue = @(3, 6)
$eye.MythicalId = $eye.ItemId
$eye.ItemStaticAffixList = @()
$eye.ItemDynamicAffixList = @()
$eye.BuffsLink = @([ValennaBattle.StrikeProfiles]::EyeChargeId, [ValennaBattle.StrikeProfiles]::EyeOverheatId, [ValennaBattle.StrikeProfiles]::AcceleratedFutureId)
$eye.Terms = @()
$eye.ConfigData = @()
$eye.GetItemOpts = @()
$eye.EndingOpts = @()
$eye.Disassemble = [ordered]@{ Items = Ids 7 @(); PartsCount = 0 }
$eye.EquipmentConfigData.EquipType = 2 # Cloth，原生护具栏
$eye.EquipmentConfigData.ShowConfirmEquipPopup = $false
$eye.EquipmentConfigData.ConfirmEquipDesc = Text-Data ''
$eye.EquipmentConfigData.InfoDatas = @()
$eye.EquipmentConfigData.Arrts = @()
$eye.EquipmentConfigData.Skills = @()
$eye.EquipmentConfigData.EnableOverrideDesc = $true
$eye.EquipmentConfigData.OverrideDesc = Text-Data $eyeText
Add-Table "Item/$($eye.ItemId)" 'MOD.ItemData' $eye

$eyeMythical = Read-Table 'MythicalItem/2'
$eyeMythical.Index = $eye.ItemId
$eyeMythical.ItemId = $eye.ItemId
$eyeMythical.Icon = 'UI/Textures/Icon/MythologicalObj/valenna_eye_mythical_card'
$eyeMythical.ProjectionIcon = 'UI/Textures/Icon/MythologicalObj/valenna_eye_mythical_projection'
$eyeMythical.IconReference = [ordered]@{ ReferenceType = 1005; Key = 'valenna_eye_mythical_card' }
$eyeMythical.ProjectionIconReference = [ordered]@{ ReferenceType = 1005; Key = 'valenna_eye_mythical_projection' }
$eyeMythical.Localization_Name = $eye.ItemName
$eyeMythical.Localization_Des = $eye.ItemDes
$eyeMythical.Localization_Effct = Text-Data ("闪避成功时恢复1点精神值。`n开骰时未过热，另获得1层加速的未来。")
$eyeMythical.DefaultUnLock = $false
$eyeMythical.TriggerEven.Opts[0].ItemList = @($eye.ItemId)
$eyeMythical.TriggerEven.Opts[0]._baseKey = [string]$eye.ItemId
Add-Table "MythicalItem/$($eye.ItemId)" 'MOD.MythicalItemData' $eyeMythical

$skillIconIds = @{
    885121 = 131401
    885124 = 131402
    885125 = 131403
    885126 = 131404
    885127 = 131405
    885128 = 131406
    885129 = 131407
    885152 = 131408
}

function Set-ValennaSkillIcon($Skill) {
    $wikiIcon = $skillIconIds[[int]$Skill.Id]
    if (!$wikiIcon) { return }

    $key = "valenna_skill_$wikiIcon"
    $path = "UI/Textures/Icon/Skill/$key"
    $Skill.UseCustomIcon = $true
    $Skill.BattleSkillIcon = $path
    $Skill.BattleSkillIconReference = [ordered]@{ ReferenceType = 1006; Key = $key }
    $Skill.UseCustomSkillElementIcon = $true
    $Skill.CustomSkillElementIconPath = $path
    $Skill.CustomSkillElementIconReference = [ordered]@{ ReferenceType = 1006; Key = $key }
}

$skillRules = @{
    885121 = @(
        '斗殴检定｜2 段'
        '【I】 1d4'
        '[命中时] 施加 1 层燃烧。'
        '【II】 1d4'
        '[命中时] 立即结算目标的燃烧 1 次。'
    )
    885124 = @(
        '斗殴＋射击联合检定｜2 段'
        '【I】 1d3'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 已消耗加速弹则施加 1 层燃烧。'
        '【II】 1d4'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 施加 2 层震颤。'
    )
    885125 = @(
        '斗殴检定｜2 段'
        '【I】 1d3'
        '[命中时] 施加 1 层震颤。'
        '【II】 1d4'
        '[命中时] 将震颤转为震颤·灼热。'
        '[命中时] 引爆震颤 1 次。'
    )
    885126 = @(
        '斗殴＋敏捷联合检定｜3 段'
        '[使用时] 目标燃烧与震颤每合计 6 层，每段伤害+1，至多+2。'
        '[使用时] 获得 4 层呼吸法。'
        '【I】 1d6'
        '[命中时] 施加 2 层燃烧。'
        '【II】 1d6'
        '[命中时] 施加 2 层震颤。'
        '【III】 1d6'
        '[命中时] 将震颤转为震颤·灼热。'
        '[命中时] 引爆震颤 2 次。'
    )
    885127 = @(
        '斗殴＋射击联合检定｜3 段'
        '[使用时] 目标燃烧与震颤合计至少 6 层时，每段伤害+1。'
        '[使用时] 获得 2 层呼吸法。'
        '【I】 1d4'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 已消耗加速弹则施加 1 层燃烧。'
        '【II】 1d4'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 施加 2 层震颤。'
        '【III】 1d6'
        '[命中时] 引爆震颤 1 次。'
    )
    885128 = @(
        '斗殴＋敏捷联合检定｜4 段'
        '[使用时] 目标燃烧与震颤每合计 6 层，每段伤害+1，至多+2。'
        '[使用时] 获得 3 层呼吸法。'
        '【I】 1d4'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 施加 1 层燃烧。'
        '[命中时] 施加 2 层震颤。'
        '【II】 1d4'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 施加 1 层燃烧。'
        '[命中时] 施加 2 层震颤。'
        '【III】 1d6'
        '有弹时消耗 1 发加速弹。'
        '[命中时] 将震颤转为震颤·灼热。'
        '【IV】 1d6'
        '[命中时] 引爆震颤 2 次。'
        '[暴击命中时] 额外引爆 2 次。'
    )
    885129 = @(
        '斗殴检定，至少成功｜5 段'
        '仅限狩猎目标，选择技能时自动选中。目标无法闪避。'
        '每段基础伤害为 1。预知眼不足 10 层时没有额外伤害骰。'
        '预知眼达到 10 层掷 1d2，20 层掷 1d4，30 层掷 1d6。'
        '[使用时] 目标燃烧与震颤每合计 6 层，每段伤害+1，至多+2。'
        '[使用时] 目标生命值不高于一半时，该段伤害翻倍。'
        '[使用时] 获得 3 层呼吸法。'
        '[使用时] 至少有 2 点理智且未陷入精神衰弱或精神枯竭时，消耗 2 点理智并获得 1 层「望」。'
        '[攻击后] 加速弹补至 10 发。'
        '[攻击后] 移除目标的狩猎目标印记。'
        '[击杀时] 下回合标记另一名敌人。'
        '【I】 1＋预知眼伤害骰'
        '[命中时] 施加 1 层震颤。'
        '【II】 1＋预知眼伤害骰'
        '[命中时] 施加 1 层燃烧。'
        '【III】 1＋预知眼伤害骰'
        '[命中时] 施加 1 层燃烧。'
        '【IV】 1＋预知眼伤害骰'
        '[命中时] 将震颤转为震颤·灼热。'
        '【V】 1＋预知眼伤害骰'
        '消耗至多 5 发加速弹。'
        '消耗「望」时，本段基础伤害+2。'
        '[命中时] 引爆震颤 3 次。'
        '[暴击命中时] 额外引爆 2 次。'
    )
}
for ($index = 0; $index -lt $moves.Length; $index++) {
    $move = $moves[$index]
    $rules = $skillRules[[int]$move.Id]
    $description = (@($rules[0], '[使用时] 自身呼吸法达到 6 层时，每段伤害+1（最多+1）。') + $rules[1..($rules.Length - 1)]) -join "`n"
    $skill = Read-Table 'BattleSkill/3'
    $skill.Id = $move.Id
    $skill.Name = Text-Data $move.Name
    Set-ValennaSkillIcon $skill
    $skill.Description = Text-Data $description
    $skill.EffectDesc = Text-Data ''
    $skill.ShowPriority = $moves.Length - $index
    $skill.IsMachiningSkill = $true
    $skill.BuffsLink = @(if ($statusLinks.ContainsKey($move.Id)) { $statusLinks[$move.Id] })
    $skill.Terms = @()
    $skill.ShowCheck = @($traitCheck)
    $skill.HideInSkillSelectList = $false
    $skill.UseItemTargetType = $false
    $skill.TargetSelectDatas = @([ordered]@{
        Checks = @(); TargetType = 2; TargetId = 0
        UseWeaponTargetType = $false; RandomSelectTarget = $false; CanChooseDeathRole = $false
    })
    $skill.MaxSelectCount = 1
    if ($move.Id -eq [ValennaBattle.StrikeProfiles]::ExecutionId) {
        $skill.BuffsLink += [ValennaBattle.StrikeProfiles]::FocusId
    }
    $skill.UseWeaponSelectCount = $false
    $skill.NeedDice = $true
    $skill.IgnoreSelfDice = $false
    $skill.UseCustomAttackEffectType = $false
    $skill.DiceConfig = (Read-Table 'BattleSkill/6').DiceConfig
    $skill.DiceConfig.SelfDiceData.Skill = 101
    if ($move.Id -in 885124, 885127) {
        $skill.DiceConfig.SelfDiceData.CheckType = 5
        $skill.DiceConfig.SelfDiceData.UniteSkill = 102
    }
    if ($move.Id -ne [ValennaBattle.StrikeProfiles]::ExecutionId) {
        $skill.DiceConfig.TargetDiceData = [ordered]@{
            CheckType = 3; Attr = 0; ExtraAttr = 118; Skill = 0; UniteSkill = 0
            TargetResult = 4; IsOverrideCheck = $false; OverrideCheckValue = 0
            OverrideCheckString = $null; CheckValueRatio = 1; TargetType = 1
        }
    }
    $skill.UseWeaponDiceConfig = $false
    $skill.UseWeaponAttackEffectType = $false
    $skill.NeedDamage = $false
    $skill.UseWeaponDamage = $false
    $skill.DisableOffhandWeapon = $true
    $skill.CustomDamage = [ordered]@{
        DamageType = 0; AttackType = 0; AddAttrs = @(); Value = '1'; DB_Bonus = 0
        UseCommonEffect = $false; EffectShow = [ordered]@{ Duration = 0; EffectShows = @() }
    }
    $skill.EffectList = @([ordered]@{ Trigger = 5; EffectChecks = @(); EffectOption = @(); AnyCheckReach = $false })
    Add-Table "BattleSkill/$($move.Id)" 'BaseSheetData' $skill
}

foreach ($technique in @(
    @{ Id = 885151; Name = '子弹装填'; Description = '[使用时] 补满加速弹。'; Priority = 10000 },
    @{ Id = 885152; Name = '预知'; Description = (@('每层预知眼使检定成功区间 +1。', '可以闪避投掷物品、法术及自身攻击被敌人成功闪避后受到的回击等原本不可闪避的攻击。成功时，避开自己受到的该次效果。', '这些特殊闪避不会触发[解兔]。成功闪避敌方一次普通攻击的全部段数后，仍可用[解兔]反击。', '[使用时] 补满加速弹，每行动轮限 1 次。', '[闪避成功时] 获得 1 层呼吸法，每行动轮最多 2 次。', '[闪避成功时] 恢复 1 点精神值，每场战斗最多 2 次。') -join "`n"); Priority = 9999 }
)) {
    $skill = Read-Table 'BattleSkill/3'
    $skill.Id = $technique.Id
    $skill.Name = Text-Data $technique.Name
    Set-ValennaSkillIcon $skill
    $skill.Description = Text-Data $technique.Description
    $skill.EffectDesc = Text-Data ''
    $skill.ShowPriority = $technique.Priority
    $skill.ActionType = 2
    $skill.SkillType = 2
    $skill.IsMachiningSkill = $true
    $skill.BuffsLink = @(885137)
    $skill.Terms = @()
    $skill.ShowCheck = @()
    if ($skill.Id -eq 885151) {
        $skill.BindingCharacter = $true
        $skill.BindingCharacters = $true
        $skill.BindingHero = $false
        $skill.BindingDataList = @([ordered]@{ IsPartner = $true; PartnerId = 885101; CharacterId = 0 })
    } else {
        $skill.ShowCheck = @(New-TraitCheck ([ValennaBattle.StrikeProfiles]::PrescienceTraitId))
    }
    $skill.HideInSkillSelectList = $false
    $skill.UseItemTargetType = $false
    $skill.UseWeaponSelectCount = $false
    $skill.TargetSelectDatas = @([ordered]@{
        Checks = @(); TargetType = 1; TargetId = 0
        UseWeaponTargetType = $false; RandomSelectTarget = $false; CanChooseDeathRole = $false
    })
    $skill.MaxSelectCount = 1
    $skill.NeedDice = $false
    $skill.UseWeaponDiceConfig = $false
    $skill.UseWeaponAttackEffectType = $false
    $skill.NeedDamage = $false
    $skill.UseWeaponDamage = $false
    $skill.DisableOffhandWeapon = $true
    $skill.EffectList = @([ordered]@{ Trigger = 5; EffectChecks = @(); EffectOption = @(); AnyCheckReach = $false })
    Add-Table "BattleSkill/$($skill.Id)" 'BaseSheetData' $skill
}

foreach ($retiredId in 885122, 885123, 885130) {
    $path = Join-Path $project "Assets/Resources/Config/Game/BattleSkill/$retiredId.txt"
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $retired = (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable).Data.value
        $retired.HideInSkillSelectList = $true
        # 原版新技能提醒单独读取 IsMachiningSkill，不查询菜单的隐藏标记。
        $retired.IsMachiningSkill = $false
        Add-Table "BattleSkill/$retiredId" 'BaseSheetData' $retired
    }
}

$stockStatus = Read-Table 'Buff/113'
$names = '呼吸法', '震颤', '烧伤', '震颤·灼热', '预知眼', '预知眼·过热', '加速弹', '加速的未来', '狩猎目标', '心——瓦伦希娜'
$stackedStatuses = @{
    885131 = @{ Icon = 'valenna_breath_icon'; Initial = 1; Maximum = 0 }
    885132 = @{ Icon = 'valenna_tremor_icon'; Initial = 1; Maximum = 0 }
    885134 = @{ Icon = 'valenna_ignition_icon'; Initial = 0; Maximum = 0 }
    885137 = @{ Icon = 'valenna_accel_bullet_icon'; Initial = 10; Maximum = 10 }
    885138 = @{ Icon = 'valenna_accelerated_future_icon'; Initial = 1; Maximum = 5 }
}
for ($index = 0; $index -lt $names.Count; $index++) {
    $id = 885131 + $index
    $description = switch ($id) {
        885131 { ('[命中时] 各段独立判定暴击。每层提供5%暴击率，上限100%。', '[暴击时] 本段伤害按1.2倍结算，消耗1层。', '[回合结束时] 减少1层。' -join "`n") }
        885132 { (@(
            '[引爆时] 混乱值增加震颤层数的三分之一，向上取整，再消耗1层震颤。'
            '单次增量上限为目标意志的三分之一，向下取整；意志大于零时，上限至少为1点。'
            '混乱条上限等于目标意志。填满后昏迷1个行动轮，解除后从零开始。'
            '昏迷期间暂停积累混乱值。'
            '[回合结束时] 减少1层。'
        ) -join "`n") }
        885133 { '烧伤按燃烧的规则结算。' }
        885134 { (@(
            '由震颤转化，保留已有层数。后续施加的震颤并入此状态。'
            '[引爆时] 结算普通震颤引爆，再造成爆炸伤害。'
            '爆炸伤害为引爆前燃烧与震颤总层数的一半，向下取整。'
            '燃烧与震颤各额外减少原层数的三分之一，分别向下取整。'
            '[回合结束时] 减少1层。'
        ) -join "`n") }
        885137 { ('最多储存10发。', '[消耗时] 每段只进行1次暗中射击检定，失败也会扣弹。', '[检定成功时] 本段伤害+10%。每消耗1发，获得1层呼吸法。' -join "`n") }
        885139 { ('受到标记者的伤害+20%。', '速度高于10时：', '速度-50，最低降至10。', '处置只能锁定狩猎目标。' -join "`n") }
        ([ValennaBattle.StrikeProfiles]::EyeChargeId) {
            ('充能上限30层。拥有预知时，每层使检定成功区间+1。', '[回合开始] 未处于过热状态，则以[预知]替换你的闪避。', '未处于过热状态时，斗殴检定获得1颗奖励骰。', '[回合开始时] 战斗与探索均恢复5层充能。', '发动辅助闪避或受到攻击命中时，消耗5层充能。耗尽后转为预知眼·过热。' -join "`n")
        }
        ([ValennaBattle.StrikeProfiles]::EyeOverheatId) {
            ('[过热期间] 闪避检定获得1颗惩罚骰。', '[回合开始时] 战斗与探索均积累10层。', '达到30层时转回预知眼。' -join "`n")
        }
        ([ValennaBattle.StrikeProfiles]::AcceleratedFutureId) {
            (@(
                '[闪避成功时] 若开骰时预知眼未过热，获得1层，上限5层。'
                '每层提供斗殴+2、运动+2。'
                '满5层时，非法术行动的检定获得1颗奖励骰。'
                '[攻击时] 每层使下次主动攻击伤害+5%，攻击后清空。'
                '解兔保留层数，也不触发这项增伤。'
            ) -join "`n")
        }
        885140 { $heartRules.Replace('[苏醒期间] ', '') }
        default { '效果待设定。' }
    }
    $buff = [ordered]@{
        __type = 'MOD.BuffTableData,Assembly-CSharp'
        Id = $id
        Name = Text-Data $names[$index]
        Des = Text-Data $description
        IconPathReference = $stockStatus.IconPathReference
        BuffType = 4; BuffEffectType = 1; OverlayType = 0
        IsShowUI = $true; IsDeathClear = $true
        FxPlayType = 0; PlayFxInBattle = $true; PlayFxInExplore = $false
        UseFxPrefab = [ordered]@{ UseFrameEffect = $false; FxPath = $null }
        Arrts = @(); Events = @()
    }
    if ($stackedStatuses.ContainsKey($id)) {
        $stacked = $stackedStatuses[$id]
        $buff.IconPathReference = [ordered]@{ ReferenceType = 1007; Key = $stacked.Icon }
        $buff.OverlayType = 1
        $buff.LayerCount = [ordered]@{ Value = '1' }
        $buff.OverrideInitLayer = $true
        $buff.InitLayer = $stacked.Initial
        $buff.MaxLayer = $stacked.Maximum
    }
    if ($id -in 885132, 885134, 885139) { $buff.BuffEffectType = 2 }
    if ($id -eq 885133) {
        $buff.IsShowUI = $false
        $buff.IconPathReference = (Read-Table 'Buff/104').IconPathReference
    }
    if ($id -eq 885140) {
        # 只登记原模组的外观引用，专属特质独立处理数值。
        $heart = (Get-Content -LiteralPath (Join-Path $mod '../自定义心/Project_Depersonal/Assets/Resources/Config/Game/Buff/880901.txt') -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable).Data.value
        $buff.IconPathReference = $heart.IconPathReference
        $buff.FxPlayType = $heart.FxPlayType
        $buff.UseFxPrefab = $heart.UseFxPrefab
    }
    if ($id -eq 885137) {
        $buff.BuffEffectType = 3
        $buff.IsDeathClear = $false
    }
    if ($id -eq 885139) {
        $buff.IconPathReference = [ordered]@{ ReferenceType = 1007; Key = 'valenna_hunting_prey_icon' }
        $buff.OverlayType = 2
    }
    if ($id -in [ValennaBattle.StrikeProfiles]::EyeChargeId, [ValennaBattle.StrikeProfiles]::EyeOverheatId) {
        $overheated = $id -eq [ValennaBattle.StrikeProfiles]::EyeOverheatId
        $icon = if ($overheated) { 'valenna_eye_overheat_icon' } else { 'valenna_eye_charge_icon' }
        $buff.IconPathReference = [ordered]@{ ReferenceType = 1007; Key = $icon }
        $buff.BuffEffectType = if ($overheated) { 2 } else { 1 }
        $buff.OverlayType = 1
        $buff.LayerCount = [ordered]@{ Value = '1' }
        $buff.OverrideInitLayer = $true
        $buff.InitLayer = if ($overheated) { 0 } else { [ValennaBattle.StrikeProfiles]::MaxEyeCharge }
        $buff.MaxLayer = [ValennaBattle.StrikeProfiles]::MaxEyeCharge
    }
    Add-Table "Buff/$id" 'BaseSheetData' $buff
}
foreach ($definition in @(
    @{ Id = 885148; Name = '震颤引爆'; Des = '引爆目标的震颤，每次分别结算。若已转为震颤·灼热，同时结算其爆炸效果。'; Visible = $false; Duration = 0
       Icon = [ordered]@{ ReferenceType = 1007; Key = 'valenna_tremor_burst_icon' } },
    @{ Id = 885150; Name = '醉酒'; Des = ('持续12小时。', '[检定时] 战斗与探索均获得1颗惩罚骰。' -join "`n"); Visible = $true; Duration = 0
       Icon = $stockStatus.IconPathReference }
)) {
    Add-Table "Buff/$($definition.Id)" 'BaseSheetData' ([ordered]@{
        __type = 'MOD.BuffTableData,Assembly-CSharp'
        Id = $definition.Id; Name = Text-Data $definition.Name; Des = Text-Data $definition.Des
        IconPathReference = $definition.Icon
        BuffType = 4; BuffEffectType = 2; OverlayType = 3
        IsShowUI = $definition.Visible; IsDeathClear = $true; Duration = $definition.Duration
        PlayFxInBattle = $false; PlayFxInExplore = $false
        UseFxPrefab = [ordered]@{ UseFrameEffect = $false; FxPath = $null }
        Arrts = @(); Events = @()
    })
}

Add-Table 'Buff/885153' 'BaseSheetData' ([ordered]@{
    __type = 'MOD.BuffTableData,Assembly-CSharp'
    Id = 885153; Name = Text-Data '伤害强化'; Des = Text-Data ('每层使造成的普通伤害+10%。', '[战斗结束时] 清除全部层数。' -join "`n")
    IconPathReference = [ordered]@{ ReferenceType = 1007; Key = 'icon_domain_phys_up' }
    BuffType = 4; BuffEffectType = 1; OverlayType = 1
    InitLayer = 1; OverrideInitLayer = $true; LayerCount = [ordered]@{ Value = '1' }; MaxLayer = 0; Duration = 0; IsShowUI = $true; IsDeathClear = $true
    PlayFxInBattle = $false; PlayFxInExplore = $false
    Arrts = @(); Events = @() # 由领域的物理伤害强化预设生成实例事件。
})

Add-Table "Buff/$([ValennaBattle.StrikeProfiles]::FocusId)" 'BaseSheetData' ([ordered]@{
    __type = 'MOD.BuffTableData,Assembly-CSharp'
    Id = [ValennaBattle.StrikeProfiles]::FocusId; Name = Text-Data '望'
    Des = Text-Data '本次处置的第5段基础伤害+2。'
    IconPathReference = $stockStatus.IconPathReference
    BuffType = 4; BuffEffectType = 1; OverlayType = 1
    InitLayer = 1; MaxLayer = 1; Duration = 0; IsShowUI = $true; IsDeathClear = $true
    PlayFxInBattle = $false; PlayFxInExplore = $false
    Arrts = @(); Events = @()
})

. (Join-Path $mod 'tools/format_rules.ps1')
$ruleFormatting = Format-ValennaRules $tables
Write-Output ("规则说明 {0} 条，已格式化 {1} 条，补齐状态链接 {2} 个" -f $ruleFormatting.RuleFields, $ruleFormatting.StyledFields, $ruleFormatting.BuffLinks)

. (Join-Path $mod 'tools/english.ps1')
$lexicon = Add-ValennaTableLocalization $tables
$localizationPath = Join-Path $mod 'resource/localization.json'
$localizationBody = ($lexicon | ConvertTo-Json -Depth 4) + [Environment]::NewLine
$parsedLocalization = $localizationBody | ConvertFrom-Json -AsHashtable
if ($parsedLocalization.Count -ne $lexicon.Count) { throw '双语词条序列化数量不一致' }
foreach ($entry in $lexicon.GetEnumerator()) {
    foreach ($language in 'zh', 'en') {
        if ($parsedLocalization[$entry.Key][$language] -cne $entry.Value[$language]) {
            throw "双语词条序列化不一致 $($entry.Key) ($language)"
        }
    }
}

$scratch = Join-Path $mod ('temp/data-' + [guid]::NewGuid())
$writes = [Collections.Generic.List[object]]::new()
try {
    foreach ($entry in $tables.GetEnumerator()) {
        $target = Join-Path $project $entry.Key
        $body = ($entry.Value | ConvertTo-Json -Depth 64) + [Environment]::NewLine
        if ($entry.Key -match '/Item/88511[45]\.txt$') {
            # ES3 的 ResultFuncs 字典用枚举作键，读档时直接按整数读取。
            $body = $body -replace '("ResultFuncs": \{\r?\n[ \t]*)"2"(?=\s*:)', '${1}2'
        }
        if ([IO.File]::Exists($target) -and [string]::Equals([IO.File]::ReadAllText($target), $body, [StringComparison]::Ordinal)) { continue }
        $check = $body | ConvertFrom-Json -AsHashtable
        if (-not $check.Data.__type -or -not $check.Data.value) { throw "配置无效 $($entry.Key)" }
        $backup = Join-Path $scratch $entry.Key
        $existing = [IO.File]::Exists($target)
        if ($existing) {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            [IO.File]::Copy($target, $backup)
        }
        $writes.Add(@{ Target = $target; Backup = $backup; Existing = $existing })
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::WriteAllText($target, $body, $utf8)
        if (![string]::Equals([IO.File]::ReadAllText($target), $body, [StringComparison]::Ordinal)) { throw "写入校验失败 $target" }
    }
    if (![IO.File]::Exists($localizationPath) -or
        ![string]::Equals([IO.File]::ReadAllText($localizationPath), $localizationBody, [StringComparison]::Ordinal)) {
        $backup = Join-Path $scratch 'localization.json'
        $existing = [IO.File]::Exists($localizationPath)
        if ($existing) {
            [void][IO.Directory]::CreateDirectory($scratch)
            [IO.File]::Copy($localizationPath, $backup)
        }
        $writes.Add(@{ Target = $localizationPath; Backup = $backup; Existing = $existing })
        [IO.File]::WriteAllText($localizationPath, $localizationBody, $utf8)
        if (![string]::Equals([IO.File]::ReadAllText($localizationPath), $localizationBody, [StringComparison]::Ordinal)) {
            throw "写入校验失败 $localizationPath"
        }
    }
    $meta = Join-Path $project 'Project.rtmeta'
    if (-not [IO.File]::Exists($meta)) {
        [IO.File]::WriteAllBytes($meta, [byte[]]@(0x10, 0x01, 0x22, 0x05, 0x33, 0x2e, 0x35, 0x2e, 0x33))
    }
}
catch {
    foreach ($write in $writes) {
        if ($write.Existing) { [IO.File]::Copy($write.Backup, $write.Target, $true) }
        else { [IO.File]::Delete($write.Target) }
    }
    throw
}
foreach ($write in $writes) {
    if ($write.Existing) { [IO.File]::Delete($write.Backup) }
}
if ([IO.Directory]::Exists($scratch)) {
    Get-ChildItem -LiteralPath $scratch -Directory -Recurse |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { [IO.Directory]::Delete($_.FullName) }
    [IO.Directory]::Delete($scratch)
}
Write-Output "已校验 $($tables.Count) 张配置表及 $($lexicon.Count) 条双语词条，更新 $($writes.Count) 个文件"
