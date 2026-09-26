# 生成「百合花」系列另外三件装备：百合花链 / 百合花戒指 / 百合花环。
#
# 底子还是原版 10135「阿特拉克人偶」（神话 + 装备后不可卸下的饰品），
# 与 880002「百合花」共用同一套字段处理，差别只在：Id / 名字 / 描述 / 图标 / 槽位 / 自带效果。
#
# 槽位（EEquipType）：0 = None（通用，最多装 4 件）／1 = Book（书籍，1 件）／2 = Cloth（衣物＝护具，1 件）
#   · 百合花链 880003 → None（和百合花同槽）
#   · 百合花戒指 880004 → None
#   · 百合花环 880005 → **Cloth（护具槽）**
#
# 效果（除"习得法术 / 属性加成"用数据外，其余全在插件里实现，见 src\SecretTraits.cs）：
#   · 百合花链：装备时常驻**生命上限 +4**；每回合随机吸取一名敌人 2 点生命 + 叠 2~3 层【百合花芳香】
#   · 百合花戒指：装备时常驻**魔法值上限 +8**、习得【枯萎术(1)/赤印术(12)/真理之拳(16)/祝福术(28)】；
#     每回合回 2 点魔法值；施法后按消耗的魔法值对全体敌人造成等量法术伤害（2026-09-27：原为"消耗的一半"）
#   · 百合花环：装备时常驻**体质 +10 / 意志 +10**；中毒/流血/燃烧/骨折效果不生效；
#     每轮随机解除一个负面状态并按其层数叠【花香】

param(
  [string]$SourceItem = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\StreamingAssets\Game\Item\10135.txt',
  [string]$ModRoot    = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$gameDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game'

if (-not (Test-Path -LiteralPath $SourceItem)) { throw "找不到底子文件：$SourceItem" }

$raw = Get-Content -LiteralPath $SourceItem -Encoding UTF8 -Raw
$raw = $raw -replace '\\“', '“' -replace '\\”', '”'

# ---- 三件装备的配置 ----
$equips = @(
  @{
    Id = 880003; Name = '百合花链'; Icon = 'icon_item_baihehualian'; EquipType = 0
    Des = "一朵小小的百合花，安静地悬在腕间。"
    Effect = "装备后无法卸下。`n进入副本时自动获得并装备。`n最大生命值+4。`n每回合开始：为随机一名敌人叠加 2~3 层【百合花芳香】，并吸取其 2 点生命。"
    InfoDatas = 'chain'
  },
  @{
    Id = 880004; Name = '百合花戒指'; Icon = 'icon_item_baihehuajiezhi'; EquipType = 0
    Des = "戒面上凝着一朵不会再谢的百合。"
    Effect = "装备后无法卸下。`n进入副本时自动获得并装备。`n魔法值上限+8。`n装备时习得法术「枯萎术」「赤印术」「真理之拳」「祝福术」。`n每回合开始恢复 2 点魔法值。`n施放法术后，对全体敌人造成等同于本次消耗魔法值的法术伤害。"
    InfoDatas = 'ring'
  },
  @{
    Id = 880005; Name = '百合花环'; Icon = 'icon_item_baihehuahuan'; EquipType = 2
    Des = "编作环状的百合与青枝，戴上它，像是被什么温柔地护着。"
    Effect = "装备后无法卸下。`n进入副本时自动获得并装备。`n体质+10、意志+10。`n骨折、流血、中毒、燃烧照样会挂上，但效果不生效。`n每轮开始：随机解除一个【昏迷 / 中毒 / 流血 / 燃烧 / 骨折 / 混乱 / 恐惧 / 弱点暴露】，并按被解除状态的层数为自己叠加【花香】。"
    InfoDatas = 'wreath'
  }
)

# ---- 三件装备的 InfoDatas（直接写 JSON 文本，ConvertTo-Json 会吃掉 __type，见上面的坑）----
# ExtraAttr：101 = 生命上限（走 Item_AddAttrMaxValueOption 时代表"最大值"）、103 = 魔法值上限；
# 角色属性用 Item_RoleAttrOption：EHeroAttribute 4 = 意志、6 = 体质，IsTrackSource 必须为 true（否则数值永久叠加）。

# 百合花链：装备时 生命上限 +4
$chainInfo = @'
[
  {
    "DisableTrigger": false,
    "TriggerType": 2,
    "ItemCheckList": [],
    "BaseOptions": [
      {
        "__type": "MOD.Item_AddAttrMaxValueOption,Assembly-CSharp",
        "IsConversionBuff": false,
        "Attribute": 0,
        "Skill": 0,
        "ExtraAttr": 101,
        "AddValue": "4",
        "IsRemove": false,
        "SourceKey": null
      }
    ]
  }
]
'@

# 百合花戒指：装备时 魔法值上限 +8 + 习得四个法术（枯萎术 1 / 赤印术 12 / 真理之拳 16 / 祝福术 28）
$ringInfo = @'
[
  {
    "DisableTrigger": false,
    "TriggerType": 2,
    "ItemCheckList": [],
    "BaseOptions": [
      {
        "__type": "MOD.Item_AddAttrMaxValueOption,Assembly-CSharp",
        "IsConversionBuff": false,
        "Attribute": 0,
        "Skill": 0,
        "ExtraAttr": 103,
        "AddValue": "8",
        "IsRemove": false,
        "SourceKey": null
      },
      {
        "__type": "MOD.Item_AddMagicOption,Assembly-CSharp",
        "Magic": { "Id": 1 },
        "IsRemove": false,
        "IsRandom": false,
        "RandomCount": 1,
        "SourceKey": null
      },
      {
        "__type": "MOD.Item_AddMagicOption,Assembly-CSharp",
        "Magic": { "Id": 12 },
        "IsRemove": false,
        "IsRandom": false,
        "RandomCount": 1,
        "SourceKey": null
      },
      {
        "__type": "MOD.Item_AddMagicOption,Assembly-CSharp",
        "Magic": { "Id": 16 },
        "IsRemove": false,
        "IsRandom": false,
        "RandomCount": 1,
        "SourceKey": null
      },
      {
        "__type": "MOD.Item_AddMagicOption,Assembly-CSharp",
        "Magic": { "Id": 28 },
        "IsRemove": false,
        "IsRandom": false,
        "RandomCount": 1,
        "SourceKey": null
      }
    ]
  }
]
'@

# 百合花环：装备时 体质 +10、意志 +10
$wreathInfo = @'
[
  {
    "DisableTrigger": false,
    "TriggerType": 2,
    "ItemCheckList": [],
    "BaseOptions": [
      {
        "__type": "MOD.Item_RoleAttrOption,Assembly-CSharp",
        "IsConversionBuff": false,
        "Datas": [
          {
            "IsTrackSource": true,
            "RoleAttr": { "Type": 6, "Value": "10", "IsRatio": false, "Ratio": 0 },
            "RoleExAttr": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "RoleSkill": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "RoleSystemAttr": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "ExDiceData": {
              "ExploreSkill": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 },
              "HeroAttribute": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 },
              "ExtraAttribute": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 }
            }
          },
          {
            "IsTrackSource": true,
            "RoleAttr": { "Type": 4, "Value": "10", "IsRatio": false, "Ratio": 0 },
            "RoleExAttr": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "RoleSkill": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "RoleSystemAttr": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
            "ExDiceData": {
              "ExploreSkill": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 },
              "HeroAttribute": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 },
              "ExtraAttribute": { "Type": 0, "ChangeAll": false, "IsTemp": false, "Value": 0 }
            }
          }
        ],
        "IsRemove": false,
        "SourceKey": null
      }
    ]
  }
]
'@

foreach ($e in $equips) {
  $json = $raw | ConvertFrom-Json
  $v = $json.Data.value

  $v.ItemId = $e.Id
  $v.UniqueKey = $null
  $v.ItemName.TarKey = ""; $v.ItemName.SheetKey = ""; $v.ItemName.InputText = $e.Name; $v.ItemName.Characteristic = $null
  $v.ItemDes.TarKey = ""; $v.ItemDes.SheetKey = ""; $v.ItemDes.InputText = $e.Des; $v.ItemDes.Characteristic = $null
  $v.ItemEffectDes.TarKey = ""; $v.ItemEffectDes.SheetKey = ""; $v.ItemEffectDes.InputText = $e.Effect; $v.ItemEffectDes.Characteristic = $null

  $v.IconPath = "UI/Textures/Icon/Item/$($e.Icon)"
  $v.IconPathReference.ReferenceType = 1004
  $v.IconPathReference.Key = $e.Icon

  $v.Price = 0
  $v.ChargingCount = 0
  $v.AutoDestruct = $false
  $v.IsCantDisboard = $true
  $v.DesignTag = 1
  $v.ItemAttrValue = @(3, 6)
  $v.MythicalId = 0
  $v.ItemType = 3
  $v.IsBindCharacter = $true

  $v.ItemStaticAffixList = @(); $v.ItemDynamicAffixList = @()
  $v.BuffsLink = @(); $v.Terms = @(); $v.ConfigData = @()
  $v.GetItemOpts = @(); $v.EndingOpts = @()
  $v.Disassemble.Items.Type = 7; $v.Disassemble.Items.Ids = @(); $v.Disassemble.PartsCount = 0

  $v.EquipmentConfigData.EquipType = $e.EquipType
  $v.EquipmentConfigData.ShowConfirmEquipPopup = $false
  $v.EquipmentConfigData.ConfirmEquipDesc.TarKey = ""
  $v.EquipmentConfigData.ConfirmEquipDesc.SheetKey = ""
  $v.EquipmentConfigData.ConfirmEquipDesc.InputText = $null
  $v.EquipmentConfigData.ConfirmEquipDesc.Characteristic = $null
  $v.EquipmentConfigData.InfoDatas = @()

  # 效果描述：底子自带人偶的 OverrideDesc + 本地化 key，必须清掉换成自己的（葬花/百合花都栽过）
  $v.EquipmentConfigData.EnableOverrideDesc = $true
  $v.EquipmentConfigData.OverrideDesc.TarKey = ""
  $v.EquipmentConfigData.OverrideDesc.SheetKey = ""
  $v.EquipmentConfigData.OverrideDesc.InputText = $e.Effect
  $v.EquipmentConfigData.OverrideDesc.Characteristic = $null

  $out = $json | ConvertTo-Json -Depth 100
  # ConvertTo-Json 会吃掉 __type（PS 5.1 的老毛病），补回来
  if ($out -notmatch '"Data"\s*:\s*\{\s*"__type"') {
    $out = [regex]::Replace($out, '("Data"\s*:\s*\{)', ('$1' + "`n" + '        "__type": "MOD.ItemData,Assembly-CSharp",'), 1)
  }
  if ($out -notmatch '"EquipmentConfigData"\s*:\s*\{\s*"__type"') {
    $out = [regex]::Replace($out, '("EquipmentConfigData"\s*:\s*\{)', ('$1' + "`n" + '            "__type": "MOD.EquipConfigData,Assembly-CSharp",'), 1)
  }
  # InfoDatas 的 JSON 文本定义在下面（先建 $equips、再拼 JSON），这里按名字取
  $infoJson = switch ($e.InfoDatas) {
    'chain'  { $chainInfo }
    'ring'   { $ringInfo }
    'wreath' { $wreathInfo }
    default  { $null }
  }
  if ($infoJson) {
    $out = [regex]::Replace($out, '"InfoDatas"\s*:\s*\[\s*\]', ('"InfoDatas": ' + $infoJson.Trim()), 1)
  }

  $dst = Join-Path $gameDir ("Item\{0}.txt" -f $e.Id)
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
  [System.IO.File]::WriteAllText($dst, $out, (New-Object System.Text.UTF8Encoding($false)))

  $chk = Get-Content -LiteralPath $dst -Encoding UTF8 -Raw | ConvertFrom-Json
  $cv = $chk.Data.value
  Write-Output ("已生成 {0}：{1}（槽位 {2}，__type {3} 处）" -f $e.Id, $cv.ItemName.InputText, $cv.EquipmentConfigData.EquipType, (Select-String -LiteralPath $dst -Pattern '__type' -Encoding UTF8 | Measure-Object).Count)
}

Write-Output ''
Write-Output '提示：三张图标（icon_item_baihehualian / baihehuajiezhi / baihehuahuan）导入后要注册进 InternalConfigure 的 1004 段。'
