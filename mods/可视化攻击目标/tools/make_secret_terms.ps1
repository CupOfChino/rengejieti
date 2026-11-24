# 生成私货武器的「关键词」术语（装备面板右边那一栏显示的说明）。
#
# 为什么能做：物品数据里有个 Terms 字段（术语 Id 列表），装备界面的
# MOD_Dynamic_Item.GetAffixPair() 会把里面每条都查出来，名字当标题、描述当正文排在右边。
# 原版自己的「大成功 / 困难成功 / 唯一型装备」就是这么显示的（Game\TermData\31.txt 等）。
# 所以我们把「剑痕」「先发」这些本来要写在效果描述里的长解释挪到这里，
# 描述里只留关键词，面板就不臃肿了。
#
# 用法：pwsh -File tools\make_secret_terms.ps1
#
# 编号：术语是独立的一套编号（TermFactory），和 Item / Buff 不冲突，这里取 880001 起。

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\TermData'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 一条术语。结构照抄原版 Game\TermData\31.txt（基类 TermData，不带枚举字段）。
# TarKey 一律留空 —— 非空的话游戏会去本地化表取文案，把 InputText 顶掉（见 AI协作文档 19.1）。
function New-TermData {
  param([int]$Id, [string]$Name, [string]$Des)
  [ordered]@{
    Data = [ordered]@{
      '__type' = 'BaseSheetData,Assembly-CSharp'
      value    = [ordered]@{
        '__type'       = 'TermData,Assembly-CSharp'
        Id             = $Id
        Name           = [ordered]@{ TarKey = ''; SheetKey = ''; InputText = $Name; Characteristic = $null }
        Des            = [ordered]@{ TarKey = ''; SheetKey = ''; InputText = $Des;  Characteristic = $null }
        EditorSheetKey = $null
      }
    }
  }
}

$terms = @(
  (New-TermData -Id 880001 -Name '<color=#85CCFF>剑痕</color>' -Des "每层：受到伤害+2%，闪避与运动各-5。每2层护甲-1。`n受到一套完整的近战攻击后，按（层数+1）÷3向下取整，受到固定法术伤害，可被法术减免。`n回合结束层数向下减半，失去的层数转为等量<color=#FF818C>【流血】</color>。上限10层，不可驱散。"),
  (New-TermData -Id 880002 -Name '<color=#F4A07F>先发</color>' -Des "进战时副手持<color=#F4A07F>荆棘</color>（含双手）才获得资格。失去条件则本场失效，中途换上也不补发。`n每个行动轮先随机给1名敌人<color=#F4A07F>【目标锁定】</color>，回合开始时对其打1次。此击无需检定、无法闪避（按普通成功）。`n只结算武器基础伤害与目标的护甲、减伤、易伤等。命中仍以隐藏敏捷检定决定是否叠<color=#F4A07F>【荆棘】</color>。`n标记在回合结束、目标死亡或战斗结束时解除。"),
  (New-TermData -Id 880003 -Name '<color=#C7ADFF>持握·葬花</color>' -Des "主手：此武器伤害+1，可用<color=#C7ADFF>【二连斩】</color>与<color=#C7ADFF>【横扫】</color>。`n副手：护甲+2，受到的魔法伤害-1。`n另一手空着时视为双手：两侧效果同时生效，此武器施加的负面状态+1层。"),
  (New-TermData -Id 880004 -Name '<color=#F4A07F>持握·荆棘</color>' -Des "主手：此武器伤害+2。副手：获得<color=#F4A07F>【先发】</color>资格的条件见右栏。`n另一手空着时视为双手，两侧效果同时生效。`n速度每高于目标100点，武器伤害+5%。双手每50点一档。"),
  (New-TermData -Id 880005 -Name '<color=#F4A07F>荆棘</color>' -Des "命中掷1次隐藏敏捷：失败不叠。主动攻击成功叠3层，先发成功叠1层；大成功额外+1层。`n下个行动轮开始，所有层数转为等量<color=#F4A07F>【束缚】</color>。每层降10点速度（最低10），回合结束移除。"),
  (New-TermData -Id 880006 -Name '<color=#F4A07F>目标锁定</color>' -Des '<color=#F4A07F>荆棘</color>的<color=#F4A07F>【先发】</color>标记。回合开始对标记者打1次，回合结束、目标死亡或战斗结束时解除。'),
  (New-TermData -Id 880007 -Name '<color=#C7ADFF>充能斩</color>' -Des "攻击前掷敏捷，普通成功耗2点充能，困难或大成功耗1点。付得起才加1段。`n击杀敌人回复1点充能。战斗回合开始，充能低于5且精神值大于1时，消耗1点精神值回复1点充能。"),
  (New-TermData -Id 880008 -Name '<color=#F4A07F>三段刺击</color>' -Des "斗殴命中后算一次总伤害并扣血。总伤尽量均分为3个整数，同时跳出。`n普通、困难、大成功的伤害倍率依次为×0.5、×0.75、×1。"),
  (New-TermData -Id 880009 -Name '<color=#AEE9D0>百合未谢</color>' -Des '每个副本限1次。战斗中免除一次死亡，生命值变为50%，并获得1回合伤害免疫。'),
  (New-TermData -Id 880010 -Name '<color=#AEE9D0>法术庇护</color>' -Des '战斗中免疫1次法术伤害。触发后至少间隔2回合才可再次免疫。'),
  (New-TermData -Id 880011 -Name '<color=#AEE9D0>百合花芳香</color>' -Des "行动轮开始：不足5层时，每满2层失去1点生命。达到5层则移除芳香，失去「层数×1%最大生命值＋层数」点生命。`n受到火焰伤害会触发<color=#AEE9D0>【粉尘爆炸】</color>并移除芳香，同阵营每人各受到2×层数点爆炸伤害。"),
  (New-TermData -Id 880012 -Name '<color=#AEE9D0>戒中法术</color>' -Des '装备时习得枯萎术、赤印术、真理之拳与祝福术。'),
  (New-TermData -Id 880013 -Name '<color=#AEE9D0>花环守护</color>' -Des "<color=#DCB5A1>骨折</color>、<color=#FF818C>流血</color>、<color=#9CDA71>中毒</color>、<color=#FF9F62>燃烧</color>可附加，效果被压制。`n每轮开始，从<color=#A3B8ED>昏迷</color>、<color=#9CDA71>中毒</color>、<color=#FF818C>流血</color>、<color=#FF9F62>燃烧</color>、<color=#DCB5A1>骨折</color>、<color=#CE93EC>混乱</color>、<color=#F1AECD>恐惧</color>、<color=#FFCB81>弱点暴露</color>中随机解除1项，按其层数叠<color=#D5A8FF>【花香】</color>。"),
  (New-TermData -Id 880014 -Name '<color=#D5A8FF>花香</color>' -Des "解除状态时获得，取其层数（最低1、最高5，无上限按5层算）。`n每层使意志检定成功区间+5。生命减少时消耗1层，战斗或探索回合结束时层数向下减半。每次因这两种效果减少层数，回复生命与精神各1点。`n进出副本时清空。")
)

foreach ($t in $terms) {
  $id = $t.Data.value.Id
  $file = Join-Path $outDir ("$id.txt")
  $json = $t | ConvertTo-Json -Depth 100
  [System.IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("已生成：{0}  [{1}] {2}" -f $file, $id, $t.Data.value.Name.InputText)
}
