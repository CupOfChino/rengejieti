# 生成荆棘先发用的「目标锁定」状态（Buff/880052）。
# 状态栏保留原图标，模型旁的锁定标记由插件绘制。

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$buffDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Buff'
New-Item -ItemType Directory -Force -Path $buffDir | Out-Null

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
      Comment           = '私货武器「荆棘」的先发标记：轮开始前挂上，回合开始时对中标记者打一次。本身无数值效果，头侧锁定标记由插件绘制。'
      BuffType          = 1
      BuffEffectType    = 2
      OverlayType       = 2      # 独立：同一个人身上只留一个
      LayerCount        = [ordered]@{ Value = '1' }
      OverrideInitLayer = $false
      InitLayer         = 0
      MaxLayer          = 1
      FxPlayType        = 0
      UseFxPrefab       = [ordered]@{
        FxPath         = $null
        UseFrameEffect = $false
        FxRes          = [ordered]@{ ReferenceType = 0; Key = $null }
        EffectName     = $null
        EffectAnimName = $null
        PlayPoint      = 0
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
Write-Output ("  Id=880052 名字={0} 图标={1} 特效由 DLL 绘制" -f `
  $buff.Data.value.Name.InputText, $buff.Data.value.IconPathReference.Key)
