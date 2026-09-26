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
  (New-TermData -Id 880001 -Name '剑痕' -Des '葬花命中时叠加的负面状态。每层：护甲-1、受到伤害+15%、闪避-10、运动-10。回合结束时层数减半（向下取整），并按减少的层数获得等量【流血】；最高5层，不可驱散。'),
  (New-TermData -Id 880002 -Name '先发' -Des "进入战斗时判定一次：副手装备荆棘（或视为双手）才获得先发资格。战斗中会持续跟踪，一旦不再满足条件就失去资格（本场不再恢复）；中途才换上荆棘也不会补发资格。`n每个行动轮：轮开始前给随机一名敌人挂【目标锁定】，回合开始时用武器对它打一次。`n该次攻击无需检定（按普通成功结算），敌人无法闪避；伤害固定为武器基础伤害，只受目标护甲/减伤等影响（自己身上的加成一律不算），命中给目标叠1~2层【荆棘】。"),
  (New-TermData -Id 880003 -Name '持握·葬花' -Des "装备在主手：此武器伤害+1，可施展二连斩与横扫。`n装备在副手：护甲+2，受到的魔法伤害-1。`n主/副手未装备其他武器时视为双手：同时触发主手与副手效果，且此武器施加的负面状态+1层。"),
  (New-TermData -Id 880004 -Name '持握·荆棘' -Des "装备在主手：此武器伤害+3。`n装备在副手：【先发】进入战斗时判定资格；之后每轮先给随机敌人挂【目标锁定】，再对它打一次。`n主/副手未装备其他武器时视为双手：同时触发主手与副手效果。`n速度差：每高于目标100点，此武器伤害+10%；双手持握时每50点一档。"),
  (New-TermData -Id 880005 -Name '荆棘' -Des "命中时给目标叠加的预告状态，本身没有任何效果。每1次伤害叠1~2层（大成功时翻倍）。行动轮开始时，它整颗转为等量的【束缚】；【束缚】每层使速度-10（最低降到10点），回合结束时移除。"),
  (New-TermData -Id 880006 -Name '目标锁定' -Des "荆棘先发用的标记：轮开始前挂给随机一名敌人，回合开始时用武器对它打一次；回合结束、目标死亡或战斗结束时解除。")
)

foreach ($t in $terms) {
  $id = $t.Data.value.Id
  $file = Join-Path $outDir ("$id.txt")
  $json = $t | ConvertTo-Json -Depth 100
  [System.IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("已生成：{0}  [{1}] {2}" -f $file, $id, $t.Data.value.Name.InputText)
}
