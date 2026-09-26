# 生成「百合花」饰品的数据文件（Item 880002）。
#
# 做法：拿游戏本体 10135「阿特拉克人偶」做底子 —— 它是原版的"神话 + 装备后不可卸下"饰品，
# 结构（EquipConfigData / 确认弹窗 / 各字段）都是游戏认得的样子，只改我们需要的部分最不容易踩坑。
#
# 特性（2026-09-22 用户确认）：
#   · 神话（ItemAttrValue 3）+ 不可出售（6）+ 装备后不可卸下（IsCantDisboard）
#   · 装备类型（ItemType 3 / EquipConfigData），绑定茉莉
#   · 效果（免死 / 每 2 回合法术免伤）**全部在插件里实现**，数据侧只写描述；
#     进副本自动获得并装备也在插件里（见 src\SecretTraits.cs）。
#
# 用法： powershell -File tools\make_baihehua_item.ps1

param(
  [string]$SourceItem = 'E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\StreamingAssets\Game\Item\10135.txt',
  [string]$ModRoot    = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$dstFile = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Item\880002.txt'

if (-not (Test-Path -LiteralPath $SourceItem)) { throw "找不到底子文件：$SourceItem" }

# 游戏数据文件里的 \"全角引号" 写法不是合法 JSON，先清掉多余反斜杠
$raw = Get-Content -LiteralPath $SourceItem -Encoding UTF8 -Raw
$raw = $raw -replace '\\“', '“' -replace '\\”', '”'
$json = $raw | ConvertFrom-Json
$v = $json.Data.value

# ---- 基础信息 ----
$v.ItemId = 880002
$v.UniqueKey = $null
$v.ItemName.TarKey = ""
$v.ItemName.SheetKey = ""
$v.ItemName.InputText = "百合花"
$v.ItemName.Characteristic = $null

$v.ItemDes.TarKey = ""
$v.ItemDes.SheetKey = ""
$v.ItemDes.InputText = "一朵洁白的百合花。花瓣上凝着薄薄的露水，安静得像是谁没有说完的话。"
$v.ItemDes.Characteristic = $null

$v.ItemEffectDes.TarKey = ""
$v.ItemEffectDes.SheetKey = ""
$v.ItemEffectDes.InputText = "装备后无法卸下。`n进入副本时自动获得并装备。`n最大生命值+6。`n探索中每回合恢复1点精神值。`n每场战斗限1次：战斗死亡时免疫此次死亡，生命值变为50%，并获得1回合伤害免疫。`n每2回合：免疫1次法术伤害。"
$v.ItemEffectDes.Characteristic = $null

# ---- 图标（先生成 PNG 用游戏编辑器导入，生成三件套后再把 Key 填成 icon_item_baihehua）----
$v.IconPath = "UI/Textures/Icon/Item/icon_item_baihehua"
$v.IconPathReference.ReferenceType = 1004
$v.IconPathReference.Key = "icon_item_baihehua"

# ---- 属性 / 类型 ----
$v.Price = 0
$v.ChargingCount = 0
$v.AutoDestruct = $false
$v.IsCantDisboard = $true              # 装备后不可卸下
$v.DesignTag = 1
$v.ItemAttrValue = @(3, 6)             # 3 = 神话物品，6 = 不可出售
$v.MythicalId = 0                      # 不关联神话投影（0/-1 都是"无投影"的合法值）
$v.ItemType = 3                        # EItemType.Equipe
$v.IsBindCharacter = $true             # 绑定角色：不能被偷窃/赠与

# ---- 效果全部在插件里，数据侧清空 ----
$v.ItemStaticAffixList = @()
$v.ItemDynamicAffixList = @()
$v.BuffsLink = @()
$v.Terms = @()
$v.ConfigData = @()
$v.GetItemOpts = @()
$v.EndingOpts = @()
$v.Disassemble.Items.Type = 7
$v.Disassemble.Items.Ids = @()
$v.Disassemble.PartsCount = 0

$v.EquipmentConfigData.EquipType = 0
$v.EquipmentConfigData.ShowConfirmEquipPopup = $false
$v.EquipmentConfigData.ConfirmEquipDesc.TarKey = ""
$v.EquipmentConfigData.ConfirmEquipDesc.SheetKey = ""
$v.EquipmentConfigData.ConfirmEquipDesc.InputText = $null
$v.EquipmentConfigData.ConfirmEquipDesc.Characteristic = $null
$v.EquipmentConfigData.InfoDatas = @()

# ⚠ 装备面板上那段"效果描述"走的是 OverrideDesc（**不是** ItemEffectDes —— 那个字段游戏 UI 根本不读）。
#   底子（10135 阿特拉克人偶）自带它的 OverrideDesc + 一条本地化 TarKey，不清掉就会把**人偶的效果描述**
#   显示在百合花上（2026-09-22 用户发现；葬花当年也是栽在同一个地方）。
#   TarKey 必须留空：非空时游戏会去本地化表按 key 取值，把我们写的 InputText 顶掉。
$v.EquipmentConfigData.EnableOverrideDesc = $true
$v.EquipmentConfigData.OverrideDesc.TarKey = ""
$v.EquipmentConfigData.OverrideDesc.SheetKey = ""
$v.EquipmentConfigData.OverrideDesc.InputText = "装备后无法卸下。`n进入副本时自动获得并装备。`n最大生命值+6。`n探索中每回合恢复1点精神值。`n每场战斗限1次：战斗死亡时免疫此次死亡，生命值变为50%，并获得1回合伤害免疫。`n每2回合：免疫1次法术伤害。"
$v.EquipmentConfigData.OverrideDesc.Characteristic = $null

# ⚠ 大坑（2026-09-22 踩）：PowerShell 5.1 的 ConvertTo-Json 会把对象里的 `__type` 字段弄丢，
#   而游戏**正是靠 `__type` 认类型的**（少了它 ES3 直接抛 ArgumentNullException，"读取道具失败：880002"）。
#   所以序列化完必须把游戏要认的那两个 `__type` 补回去。
$out = $json | ConvertTo-Json -Depth 100
if ($out -notmatch '"Data"\s*:\s*\{\s*"__type"') {
  $out = [regex]::Replace($out, '("Data"\s*:\s*\{)', ('$1' + "`n" + '        "__type": "MOD.ItemData,Assembly-CSharp",'), 1)
}
if ($out -notmatch '"EquipmentConfigData"\s*:\s*\{\s*"__type"') {
  $out = [regex]::Replace($out, '("EquipmentConfigData"\s*:\s*\{)', ('$1' + "`n" + '            "__type": "MOD.EquipConfigData,Assembly-CSharp",'), 1)
}

# ---- 装备自带效果（InfoDatas）----
# 这里**直接写 JSON 文本**、不走 PowerShell 对象：ConvertTo-Json 会把条目里的 `__type` 吃掉（同上面的坑）。
# 两个效果：
#   1) 装备时（TriggerType 2）加**最大生命值 +6**（ExtraAttr 101 = 生命值，Item_AddAttrMaxValueOption）
#   2) 探索回合变化（TriggerType 21，游戏探索里每 12 秒算一回合）**回复 1 点精神值**（ExtraAttr 102 = 当前精神值）
#      （TriggerType 21 的取值参考原版 Item\1041.txt「灵玉符文」）
$infoDatas = @'
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
                "AddValue": "6",
                "IsRemove": false,
                "SourceKey": null
              }
            ]
          },
          {
            "DisableTrigger": false,
            "TriggerType": 21,
            "ItemCheckList": [],
            "BaseOptions": [
              {
                "__type": "MOD.Item_RoleAttrOption,Assembly-CSharp",
                "IsConversionBuff": false,
                "Datas": [
                  {
                    "IsTrackSource": false,
                    "RoleAttr": { "Type": 0, "Value": null, "IsRatio": false, "Ratio": 0 },
                    "RoleExAttr": { "Type": 102, "Value": "1", "IsRatio": false, "Ratio": 0 },
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
$out = [regex]::Replace($out, '"InfoDatas"\s*:\s*\[\s*\]', ('"InfoDatas": ' + $infoDatas.Trim()), 1)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstFile) | Out-Null
[System.IO.File]::WriteAllText($dstFile, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("已生成：{0}" -f $dstFile)
Write-Output ("  名字={0}  类型={1}  属性={2}  不可卸下={3}  绑定={4}" -f `
  $v.ItemName.InputText, $v.ItemType, ($v.ItemAttrValue -join ','), $v.IsCantDisboard, $v.IsBindCharacter)
Write-Output '提示：图标 Key 先填着 icon_item_baihehua，等 PNG 导入生成三件套后就能直接对上。'
