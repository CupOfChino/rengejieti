# 生成「目标锁定」相关数据（2026-09-27）：
#   · ExtraAnim\ruodian_baolu.txt  —— 敌人身上显示的帧动画（就用"弱点暴露"那张原版图标，1 帧循环）
#   · Buff\880052.txt              —— 「目标锁定」状态本体
#
# 用途：荆棘副手的「先发」改成两步走 ——
#   行动轮开始前：对随机一个敌人挂【目标锁定】
#   回合开始时：  对带【目标锁定】的那个敌人用武器打一次
# 所以这个状态本身**不带任何数值效果**，纯粹是个"标记 + 特效"。
#
# 图标/特效都复用原版资源（弱点暴露 icon_buff_ruodianbaolu），不需要额外导入。

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$animDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\ExtraAnim'
$buffDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Buff'
New-Item -ItemType Directory -Force -Path $animDir | Out-Null
New-Item -ItemType Directory -Force -Path $buffDir | Out-Null

# ---- 1. 帧动画（照 zanghua_hen.txt 的骨架）----
$anim = [ordered]@{
  Data = [ordered]@{
    '__type' = 'BaseSheetData,Assembly-CSharp'
    value    = [ordered]@{
      '__type'        = 'CustomSpriteConfigData,Assembly-CSharp'
      EffectName      = 'ruodian_baolu'
      AnimationList   = @(
        [ordered]@{
          AnimName   = 'idle'
          IsLoop     = $true
          ExtraAnims = @(
            [ordered]@{
              AnimSprite  = [ordered]@{ ReferenceType = 1023; Key = 'ruodian_baolu' }
              IsInternal  = $false
              ResLoadPath = $null
              UseAtals    = $false
              SpriteName  = $null
              ShowCount   = 0.5
            }
          )
        }
      )
      AnimSpeed       = 0.2
      AnimSize        = [ordered]@{ x = 1; y = 1 }
      SortName        = 'Chara'
      SortOrder       = 50
      DefaultPlayAnim = 'idle'
      UseLightEffect  = $false
      EditorSheetKey  = $null
    }
  }
}
$animFile = Join-Path $animDir 'ruodian_baolu.txt'
[System.IO.File]::WriteAllText($animFile, ($anim | ConvertTo-Json -Depth 100), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("已生成：{0}" -f $animFile)

# ---- 2. 「目标锁定」状态 ----
$buff = [ordered]@{
  Data = [ordered]@{
    '__type' = 'BaseSheetData,Assembly-CSharp'
    value    = [ordered]@{
      '__type'          = 'MOD.BuffTableData,Assembly-CSharp'
      IconPath          = ''
      IconPathReference = [ordered]@{ ReferenceType = 1007; Key = 'icon_buff_ruodianbaolu' }
      Id                = 880052
      Name              = [ordered]@{ TarKey = ''; InputText = '目标锁定' }
      Des               = [ordered]@{ TarKey = ''; InputText = '已经被荆棘盯上了。回合开始时，攻击者会用武器对它打一次；回合结束、目标死亡或战斗结束时解除。' }
      Comment           = '私货武器「荆棘」的先发标记：轮开始前挂上，回合开始时对中标记者打一次。本身无数值效果，特效复用原版弱点暴露的图标（ExtraAnim\ruodian_baolu）。'
      BuffType          = 1
      BuffEffectType    = 2
      OverlayType       = 2      # 独立：同一个人身上只留一个
      LayerCount        = [ordered]@{ Value = '1' }
      OverrideInitLayer = $false
      InitLayer         = 0
      MaxLayer          = 1
      FxPlayType        = 1
      UseFxPrefab       = [ordered]@{
        FxPath         = $null
        UseFrameEffect = $true
        FxRes          = [ordered]@{ ReferenceType = 0; Key = $null }
        EffectName     = 'ruodian_baolu'
        EffectAnimName = 'idle'
        PlayPoint      = 2
      }
      EffectClip        = [ordered]@{
        AudioRes       = $null
        AudioReference = [ordered]@{ ReferenceType = 0; Key = $null }
        Volume         = 1
        FadeTime       = 1
      }
      IsClipLoop        = $false
      Duration          = 0
      Priority          = 0
      IsDeathClear      = $true
      IsShowUI          = $true
      Arrts             = @()
      Events            = @()
      EditorSheetKey    = $null
    }
  }
}
$buffFile = Join-Path $buffDir '880052.txt'
[System.IO.File]::WriteAllText($buffFile, ($buff | ConvertTo-Json -Depth 100), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("已生成：{0}" -f $buffFile)
Write-Output ("  Id=880052 名字={0} 图标={1} 特效={2}" -f `
  $buff.Data.value.Name.InputText, $buff.Data.value.IconPathReference.Key, $buff.Data.value.UseFxPrefab.EffectName)
