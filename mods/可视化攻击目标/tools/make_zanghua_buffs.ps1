# 生成「剑痕」用到的状态数据：
#   · 880040              剑痕本体（层数容器 + 图标 + 描述；回合结束的逻辑在插件里）
#   · 880041 ~ 880045     按层数生效的隐藏状态（护甲 -x、闪避 -10x、运动 -10x）
#
# ⚠ 2026-09-27 踩坑记录：本脚本的模板曾把"游戏编辑器里改过的东西"覆盖回旧值 ——
#   880040 的 OverlayType（1=叠加层数）、自制图标 icon_buff_zanghua_hen、zanghua_hen 帧特效
#   都因为脚本还是旧模板被回退过一次（用户实测发现剑痕图标变回原版、层数叠不上去）。
#   现在这三项都写进了脚本参数（overlayType / fxName），**以后在编辑器里改了数据，记得回写到本脚本**。
#
# 为什么拆成"本体 + 5 个档位"：游戏 buff 的属性数值是**固定值**，不随层数缩放
# （Arrts 里没有"每层"的系数），所以只能按层数挂对应档位的隐藏 buff，表现上完全一致。
# "受到伤害 +15x%" 不在数据里做 —— 它挂在目标的 BeDamageChangePercentData 上，
# 由插件按层数同步（`ZangHua.SyncMarkBeDamage`），比属性档位好控制。
# 2026-09-27 用户口径：剑痕去掉速度削减（减速归荆棘的【束缚】），闪避 -5 → -10，新增运动 -10。
#
# 用法：powershell -File tools\make_zanghua_buffs.ps1

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Buff'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-AttrEntry([int]$exType, [string]$value) {
  [ordered]@{
    IsTrackSource = $true
    RoleAttr       = [ordered]@{ Type = 0; Value = $null; IsRatio = $false; Ratio = 0 }
    RoleExAttr     = [ordered]@{ Type = $exType; Value = $value; IsRatio = $false; Ratio = 0 }
    RoleSkill      = [ordered]@{ Type = 0; Value = $null; IsRatio = $false; Ratio = 0 }
    RoleSystemAttr = [ordered]@{ Type = 0; Value = $null; IsRatio = $false; Ratio = 0 }
    ExDiceData     = [ordered]@{
      ExploreSkill   = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
      HeroAttribute  = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
      ExtraAttribute = [ordered]@{ Type = 0; ChangeAll = $false; IsTemp = $false; Value = 0 }
    }
  }
}

function Write-Buff($id, $name, $des, $comment, [int]$maxLayer, [bool]$showUI, $arrts, $iconKey,
                    [int]$overlayType = 2, [string]$fxName = $null) {
  if ($iconKey) {
    $iconRef = [ordered]@{ ReferenceType = 1007; Key = $iconKey }
    $iconPath = "UI/Textures/Icon/Buff/$iconKey"
  } else {
    $iconRef = [ordered]@{ ReferenceType = 0; Key = $null }
    $iconPath = $null
  }
  $buff = [ordered]@{
    Data = [ordered]@{
      __type = "BaseSheetData,Assembly-CSharp"
      value  = [ordered]@{
        __type            = "MOD.BuffTableData,Assembly-CSharp"
        IconPath          = $iconPath
        Id                = $id
        Name              = [ordered]@{ TarKey = ""; SheetKey = ""; InputText = $name; Characteristic = $null }
        Des               = [ordered]@{ TarKey = ""; SheetKey = ""; InputText = $des; Characteristic = $null }
        IconPathReference = $iconRef
        Comment           = $comment
        BuffType          = 4
        BuffEffectType    = 2
        OverlayType       = $overlayType
        LayerCount        = [ordered]@{ Value = "1" }
        OverrideInitLayer = $false
        InitLayer         = 0
        MaxLayer          = $maxLayer
        FxPlayType        = 1
        # 帧特效：$fxName 非空就挂 ExtraAnim 帧动画（参考原版 buff 的写法）
        UseFxPrefab       = [ordered]@{
          FxPath         = $null
          UseFrameEffect = [bool]$fxName
          FxRes          = [ordered]@{ ReferenceType = 0; Key = $null }
          EffectName     = $fxName
          EffectAnimName = $(if ($fxName) { "idle" } else { $null })
          PlayPoint      = $(if ($fxName) { 2 } else { 0 })
        }
        PlayFxInBattle    = [bool]$fxName
        PlayFxInExplore   = $false
        DelayTime         = 0
        EffectClip        = [ordered]@{ AudioRes = $null; AudioReference = [ordered]@{ ReferenceType = 0; Key = $null }; Volume = 1; FadeTime = 1 }
        EndClip           = [ordered]@{ AudioRes = $null; AudioReference = [ordered]@{ ReferenceType = 0; Key = $null }; Volume = 1; FadeTime = 1 }
        IsClipLoop        = $false
        Duration          = 0
        Priority          = 0
        IsDeathClear      = $true
        IsShowUI          = $showUI
        Arrts             = $arrts
        Events            = @()
        EditorSheetKey    = $null
      }
    }
  }
  $file = Join-Path $outDir ("$id.txt")
  [System.IO.File]::WriteAllText($file, ($buff | ConvertTo-Json -Depth 100), (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("  {0}  {1}" -f $id, $name)
}

Write-Output "生成剑痕系列状态："

# ---- 880040 剑痕本体：只当"层数容器 + 显示"，数值交给下面 5 个档位 ----
Write-Buff 880040 "剑痕" `
  "空间被斩击后留下的痕迹，无需触碰便能感受到它的锋利。`n每层：受到伤害+5%、闪避-5、运动-5；每2层：护甲-1。`n回合结束时层数减半（向下取整），并按减少的层数获得等量【流血】。`n最高10层，不可驱散。" `
  "私货武器「葬花」的核心负面状态（2026-09-27 改版：上限 5→10、每层效果减半）。层数容器：护甲走 880041~880045 五个隐藏档位（每 2 层 1 档）；闪避 / 运动 / 受到伤害按层数由插件直接同步；回合结束减半、补流血、不可驱散都在插件里做。" `
  10 $true @() "icon_buff_zanghua_hen" 1 "zanghua_hen"

# ---- 880041~880045：护甲档位（第 n 档 = 剑痕 2n 层时的 护甲-n）----
# 闪避 / 运动 / 受到伤害都**按层数**由插件直接同步，不走档位（粒度不同，档位装不下）。
for ($tier = 1; $tier -le 5; $tier++) {
  $arrts = @(
    (New-AttrEntry 112 ("-" + $tier))          # 护甲 -n
  )
  Write-Buff (880040 + $tier) ("剑痕·护甲档" + $tier + "（隐藏数值）") `
    ("护甲-" + $tier + "（对应剑痕 " + (2 * $tier) + " 层）。") `
    "私货武器「葬花」的隐藏档位状态：由插件按敌人身上的剑痕层数挂/摘，玩家看不到（IsShowUI=false）。" `
    0 $false $arrts $null
}

Write-Output "完成。"
