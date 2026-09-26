# 生成「束缚」状态（Buff 880050）—— 荆棘命中后、下个行动轮开始时给敌人挂的那层。
#
# 2026-09-26 用户口径：
#   · 荆棘每次攻击命中造成伤害后，**下个行动轮开始**给敌人施加 1 层【束缚】
#   · 层数无上限，每层让自身速度 -10，属于负面状态
#   · 回合结束后（所有人行动完成后的时机）移除
#
# 注意：属性**不按层数缩放**（游戏的 Arrts 只在挂上/摘下那一刻写一次），
# 所以 Arrts 留空，"每层速度 -10"由插件按 CurLayer 同步（和花香一个路子）。
#
# 图标用边狱巴士的「Binding」（橙色下箭头），已放在 Texture\Buff\icon_buff_shufu.png，
# 注册表 1007 段也要有对应条目（见 InternalConfigure.txt）。

param(
  [string]$ModRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Buff'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$buff = [ordered]@{
  Data = [ordered]@{
    '__type' = 'BaseSheetData,Assembly-CSharp'
    value    = [ordered]@{
      '__type'           = 'MOD.BuffTableData,Assembly-CSharp'
      IconPath           = ''
      IconPathReference  = [ordered]@{ ReferenceType = 1007; Key = 'icon_buff_shufu' }
      Id                 = 880050
      Name               = [ordered]@{ TarKey = ''; InputText = '束缚' }
      Des                = [ordered]@{ TarKey = ''; InputText = "被缠住的地方又紧了一分，连抬手都慢半拍。`n每一层使自身速度-10（最低降到10点）。回合结束后（所有人行动完成时）移除。" }
      Comment            = '私货武器「荆棘」的束缚状态：命中后下个行动轮开始挂上，回合结束移除。速度削减由插件按层数同步。'
      BuffType           = 1
      BuffEffectType     = 2      # 负面
      OverlayType        = 1      # 叠加层数
      LayerCount         = [ordered]@{ Value = '1' }
      OverrideInitLayer  = $false
      InitLayer          = 0
      MaxLayer           = 0      # 无上限
      FxPlayType         = 1
      UseFxPrefab        = [ordered]@{
        FxPath    = $null
        FxRes     = [ordered]@{ ReferenceType = 0; Key = $null }
        PlayPoint = 0
      }
      EffectClip         = [ordered]@{
        AudioRes       = $null
        AudioReference = [ordered]@{ ReferenceType = 0; Key = $null }
        Volume         = 1
        FadeTime       = 1
      }
      IsClipLoop         = $false
      Duration           = 0
      Priority           = 0
      IsDeathClear       = $true
      IsShowUI           = $true
      Arrts              = @()
      Events             = @()
      EditorSheetKey     = $null
    }
  }
}

$file = Join-Path $outDir '880050.txt'
$json = $buff | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("已生成：{0}" -f $file)
Write-Output ("  Id={0} 名字={1} 负面={2} 叠加={3} 上限={4} 图标={5}" -f `
  880050, $buff.Data.value.Name.InputText, $buff.Data.value.BuffEffectType, `
  $buff.Data.value.OverlayType, $buff.Data.value.MaxLayer, $buff.Data.value.IconPathReference.Key)

# ---- 再生成一个「荆棘」（Buff 880051）：命中时的"预告"状态 ----
# 2026-09-26 用户口径：荆棘命中时先挂这个，行动轮开始时把它整个换成等量的【束缚】。
# 这样状态栏在命中当轮就能看到"下轮会挨什么"，比"什么都不显示、下轮突然冒出束缚"清楚。
# 图标用原版 icon_buff_chanfu（缠缚，暗红缠绕线条），已放到 Texture\Buff\icon_buff_jingji.png。
$thorn = [ordered]@{
  Data = [ordered]@{
    '__type' = 'BaseSheetData,Assembly-CSharp'
    value    = [ordered]@{
      '__type'          = 'MOD.BuffTableData,Assembly-CSharp'
      IconPath          = ''
      IconPathReference = [ordered]@{ ReferenceType = 1007; Key = 'icon_buff_jingji' }
      Id                = 880051
      Name              = [ordered]@{ TarKey = ''; InputText = '荆棘' }
      Des               = [ordered]@{ TarKey = ''; InputText = "缠上伤口的尖刺无声收紧，越是挣扎，扎得越深。`n行动轮开始时移除此效果，并对自身施加等量的【束缚】。" }
      Comment           = '私货武器「荆棘」的预告状态：命中时挂上，行动轮开始时整颗换成等量的束缚（880050）。本身不带任何数值效果。'
      BuffType          = 1
      BuffEffectType    = 2
      OverlayType       = 1
      LayerCount        = [ordered]@{ Value = '1' }
      OverrideInitLayer = $false
      InitLayer         = 0
      MaxLayer          = 0
      FxPlayType        = 1
      UseFxPrefab       = [ordered]@{
        FxPath    = $null
        FxRes     = [ordered]@{ ReferenceType = 0; Key = $null }
        PlayPoint = 0
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
$thornFile = Join-Path $outDir '880051.txt'
[System.IO.File]::WriteAllText($thornFile, ($thorn | ConvertTo-Json -Depth 100), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("已生成：{0}" -f $thornFile)
Write-Output ("  Id=880051 名字={0} 图标={1}" -f $thorn.Data.value.Name.InputText, $thorn.Data.value.IconPathReference.Key)
