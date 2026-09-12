# 跑团卡特质包 —— 特质文件生成脚本
# 作用：按下面的 $traits 定义表，生成游戏能读取的 Trait 数据文件
# 输出：<模组根>\Project_Depersonal\Assets\Resources\Config\Game\Trait\<Id>.txt
# 注意：生成出来的 txt 就是游戏真正读取的文件。要改内容请改本脚本再重跑，避免手改被下次覆盖。

$ErrorActionPreference = 'Stop'

$ModRoot  = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$TraitDir = Join-Path $ModRoot 'Project_Depersonal\Assets\Resources\Config\Game\Trait'
New-Item -ItemType Directory -Force -Path $TraitDir | Out-Null

$TAB = [string][char]9
function L([int]$n, [string]$s) { return ($TAB * $n) + $s }

function JV($v) {
  # 未使用的字段要写成 null（跟游戏本体的写法保持一致），空字符串同样按未使用处理
  if ($null -eq $v -or '' -eq $v) { return 'null' }
  return '"' + [string]$v + '"'
}
function JB([bool]$b) { if ($b) { return 'true' } else { return 'false' } }

# ---------- 构建一条数据修改项 ----------
function New-Change {
  param(
    [int]$Attr = 0,       [string]$AttrValue = $null,
    [int]$ExAttr = 0,     [string]$ExAttrValue = $null,
    [int]$Skill = 0,      [string]$SkillValue = $null,
    [int]$SysAttr = 0,    [string]$SysAttrValue = $null,
    [int]$SkillDieType = 0, [int]$SkillDieValue = 0, [bool]$SkillDieAll = $false,
    [int]$HeroDieType = 0,  [int]$HeroDieValue = 0,  [bool]$HeroDieAll = $false,
    [int]$ExtraDieType = 0, [int]$ExtraDieValue = 0, [bool]$ExtraDieAll = $false
  )
  $l = New-Object System.Collections.Generic.List[string]
  $l.Add((L 7  '{'))
  $l.Add((L 8  '"IsTrackSource" : true,'))
  $l.Add((L 8  '"RoleAttr" : {'))
  $l.Add((L 9  ('"Type" : ' + $Attr + ',')))
  $l.Add((L 9  ('"Value" : ' + (JV $AttrValue) + ',')))
  $l.Add((L 9  '"IsRatio" : false,'))
  $l.Add((L 9  '"Ratio" : 0'))
  $l.Add((L 8  '},'))
  $l.Add((L 8  '"RoleExAttr" : {'))
  $l.Add((L 9  ('"Type" : ' + $ExAttr + ',')))
  $l.Add((L 9  ('"Value" : ' + (JV $ExAttrValue) + ',')))
  $l.Add((L 9  '"IsRatio" : false,'))
  $l.Add((L 9  '"Ratio" : 0'))
  $l.Add((L 8  '},'))
  $l.Add((L 8  '"RoleSkill" : {'))
  $l.Add((L 9  ('"Type" : ' + $Skill + ',')))
  $l.Add((L 9  ('"Value" : ' + (JV $SkillValue) + ',')))
  $l.Add((L 9  '"IsRatio" : false,'))
  $l.Add((L 9  '"Ratio" : 0'))
  $l.Add((L 8  '},'))
  $l.Add((L 8  '"RoleSystemAttr" : {'))
  $l.Add((L 9  ('"Type" : ' + $SysAttr + ',')))
  $l.Add((L 9  ('"Value" : ' + (JV $SysAttrValue) + ',')))
  $l.Add((L 9  '"IsRatio" : false,'))
  $l.Add((L 9  '"Ratio" : 0'))
  $l.Add((L 8  '},'))
  $l.Add((L 8  '"ExDiceData" : {'))
  $l.Add((L 9  '"ExploreSkill" : {'))
  $l.Add((L 10 ('"Type" : ' + $SkillDieType + ',')))
  $l.Add((L 10 ('"ChangeAll" : ' + (JB $SkillDieAll) + ',')))
  $l.Add((L 10 '"IsTemp" : false,'))
  $l.Add((L 10 ('"Value" : ' + $SkillDieValue)))
  $l.Add((L 9  '},'))
  $l.Add((L 9  '"HeroAttribute" : {'))
  $l.Add((L 10 ('"Type" : ' + $HeroDieType + ',')))
  $l.Add((L 10 ('"ChangeAll" : ' + (JB $HeroDieAll) + ',')))
  $l.Add((L 10 '"IsTemp" : false,'))
  $l.Add((L 10 ('"Value" : ' + $HeroDieValue)))
  $l.Add((L 9  '},'))
  $l.Add((L 9  '"ExtraAttribute" : {'))
  $l.Add((L 10 ('"Type" : ' + $ExtraDieType + ',')))
  $l.Add((L 10 ('"ChangeAll" : ' + (JB $ExtraDieAll) + ',')))
  $l.Add((L 10 '"IsTemp" : false,'))
  $l.Add((L 10 ('"Value" : ' + $ExtraDieValue)))
  $l.Add((L 9  '}'))
  $l.Add((L 8  '}'))
  $l.Add((L 7  '}'))
  return $l.ToArray()
}

# ---------- 构建一个事件块（CreateEvent / WakeEvent / SleepyEvent），返回从第 3 层缩进开始的整块 ----------
function New-EventBlock([string]$text, $changes) {
  $l = New-Object System.Collections.Generic.List[string]
  $l.Add((L 3 '{'))
  $l.Add((L 4 '"Localization_Functin" : {'))
  $l.Add((L 5 '"TarKey" : "",'))
  $l.Add((L 5 '"SheetKey" : "",'))
  $l.Add((L 5 ('"InputText" : "' + $text + '",')))
  $l.Add((L 5 '"Characteristic" : null'))
  $l.Add((L 4 '},'))
  $l.Add((L 4 '"Inherent" : ['))
  if ($changes -and $changes.Count -gt 0) {
    $l.Add((L 5 '{'))
    $l.Add((L 6 '"__type" : "MOD.TraitEvent.ChangeRoleValueDataOption,Assembly-CSharp",'))
    $l.Add((L 6 '"TargetType" : 1,'))
    $l.Add((L 6 '"Datas" : ['))
    $entries = @()
    foreach ($c in $changes) { $entries += ,(New-Change @c) }
    for ($i = 0; $i -lt $entries.Count; $i++) {
      $e = $entries[$i]
      for ($j = 0; $j -lt $e.Count; $j++) {
        $line = $e[$j]
        if ($j -eq ($e.Count - 1) -and $i -lt ($entries.Count - 1)) { $line = $line + ',' }
        $l.Add($line)
      }
    }
    $l.Add((L 6 '],'))
    $l.Add((L 6 '"IsRemove" : false'))
    $l.Add((L 5 '}'))
  }
  $l.Add((L 4 '],'))
  $l.Add((L 4 '"Triggers" : ['))
  $l.Add((L 5 ''))
  $l.Add((L 4 ']'))
  $l.Add((L 3 '}'))
  return $l.ToArray()
}

# ---------- 构建完整特质文件 ----------
function New-TraitText($t) {
  $l = New-Object System.Collections.Generic.List[string]
  $l.Add('{')
  $l.Add((L 1 '"Data" : {'))
  $l.Add((L 2 '"__type" : "BaseSheetData,Assembly-CSharp",'))
  $l.Add((L 2 '"value" : {'))
  $l.Add((L 3 '"__type" : "MOD.TraitData,Assembly-CSharp",'))
  $l.Add((L 3 ('"Id" : ' + $t.Id + ',')))
  $l.Add((L 3 ('"IsCanCreateGet" : ' + (JB $t.CanCreateGet) + ',')))
  $l.Add((L 3 '"IsCloseInherit" : false,'))
  $l.Add((L 3 ('"Type" : ' + $t.Type + ',')))
  $l.Add((L 3 '"CanRepeate" : false,'))
  $l.Add((L 3 '"TriggerLimit" : 0,'))
  $l.Add((L 3 '"Localization_Name" : {'))
  $l.Add((L 4 '"TarKey" : "",'))
  $l.Add((L 4 '"SheetKey" : "",'))
  $l.Add((L 4 ('"InputText" : "' + $t.Name + '",')))
  $l.Add((L 4 '"Characteristic" : null'))
  $l.Add((L 3 '},'))
  $l.Add((L 3 '"Localization_Des" : {'))
  $l.Add((L 4 '"TarKey" : "",'))
  $l.Add((L 4 '"SheetKey" : "",'))
  $l.Add((L 4 ('"InputText" : "' + $t.Des + '",')))
  $l.Add((L 4 '"Characteristic" : null'))
  $l.Add((L 3 '},'))
  $l.Add((L 3 '"BuffsLink" : ['))
  $l.Add((L 4 ''))
  $l.Add((L 3 '],'))
  $l.Add((L 3 '"Terms" : ['))
  $l.Add((L 4 ''))
  $l.Add((L 3 '],'))

  foreach ($slot in @(
    @{ Key = 'CreateEvent'; Prefix = '"CreateEvent" : '; Data = $t.CreateEvent },
    @{ Key = 'WakeEvent';   Prefix = '"WakeEvent" : ';   Data = $t.WakeEvent },
    @{ Key = 'SleepyEvent'; Prefix = '"SleepyEvent" : '; Data = $t.SleepyEvent }
  )) {
    if ($slot.Data) {
      $ev = New-EventBlock $slot.Data.Text $slot.Data.Changes
      $l.Add((L 3 $slot.Prefix) + $ev[0].TrimStart([char]9))
      for ($i = 1; $i -lt $ev.Count; $i++) { $l.Add($ev[$i]) }
      $l[$l.Count - 1] = $l[$l.Count - 1] + ','
    } else {
      $l.Add((L 3 ($slot.Prefix + 'null,')))
    }
  }

  $l.Add((L 3 '"ConceptTraitTriggerDatas" : ['))
  $l.Add((L 4 ''))
  $l.Add((L 3 '],'))
  $l.Add((L 3 '"CrazyTraitTriggerDatas" : ['))
  $l.Add((L 4 ''))
  $l.Add((L 3 '],'))
  $l.Add((L 3 '"ActiveToHideCustom" : {'))
  $l.Add((L 4 '"Gold" : 0,'))
  $l.Add((L 4 '"InterludeTime" : 0,'))
  $l.Add((L 4 '"SpaceTimePoint" : 0'))
  $l.Add((L 3 '},'))
  $l.Add((L 3 '"HideToActiveCustom" : {'))
  $l.Add((L 4 '"Gold" : 0,'))
  $l.Add((L 4 '"InterludeTime" : 0,'))
  $l.Add((L 4 '"SpaceTimePoint" : 0'))
  $l.Add((L 3 '},'))
  $l.Add((L 3 '"EditorSheetKey" : null'))
  $l.Add((L 2 '}'))
  $l.Add((L 1 '}'))
  $l.Add('}')
  return (($l -join "`r`n") + "`r`n")
}

# ==========================================================================
#  特质定义表：要加/改特质，改这里就够了
#  Type: 2=性格 3=理念 4=其他特质（本包统一用 4）
#  技能ID: 101斗殴 102射击 201运动 203隐匿 301感知 302观察 403交涉 405心理学 501博学 502神秘学 601能工巧匠 603医术
#  属性ID: 1力量 2敏捷 3智力 4意志 6体质
#  额外属性ID: 108幸运 112护甲 119速度 118闪避 101生命值 102精神值 103魔法值
#  奖惩骰: SkillDieType=技能ID / HeroDieType=属性ID / ExtraDieType=额外属性ID，Value=+1奖励骰、-1惩罚骰
#  数值增减写在 CreateEvent（获得时一次生效）；奖惩骰写在 WakeEvent/SleepyEvent（激活时生效、沉睡时收回）
# ==========================================================================
$traits = @(
  @{ Id = 880001; Name = '娇弱'; Type = 4; CanCreateGet = $true
     Des = '美貌会带来许多便利，然而当没有保护自己的力量时，可能会带来许多麻烦……（来源：洁黛蒂·战地外科医生）'
     CreateEvent = $null
     WakeEvent   = @{ Text = '交涉判定获得1颗奖励骰；力量判定获得1颗惩罚骰'
                      Changes = @( @{ SkillDieType = 403; SkillDieValue = 1 },
                                   @{ HeroDieType = 1;   HeroDieValue = -1 } ) }
     SleepyEvent = @{ Text = '收回交涉奖励骰与力量惩罚骰'
                      Changes = @( @{ SkillDieType = 403; SkillDieValue = -1 },
                                   @{ HeroDieType = 1;   HeroDieValue = 1 } ) } }

  @{ Id = 880002; Name = '老兵的射击技巧'; Type = 4; CanCreateGet = $true
     Des = '女士，虽然这是大后方，治安也不错。但毕竟这是个兵荒马乱的年代，您最好学会一点防身技巧。（来源：洁黛蒂）'
     CreateEvent = @{ Text = '射击+10'; Changes = @( @{ Skill = 102; SkillValue = '10' } ) } }

  @{ Id = 880003; Name = '医生的朋友'; Type = 4; CanCreateGet = $true
     Des = '手术台前站得久了，看人也就看得懂了。（来源：马斯·五金店店长）'
     CreateEvent = @{ Text = '医学+10，心理学+10'
                      Changes = @( @{ Skill = 603; SkillValue = '10' },
                                   @{ Skill = 405; SkillValue = '10' } ) } }

  @{ Id = 880004; Name = '幸运儿'; Type = 4; CanCreateGet = $true
     Des = '他这辈子差一点完蛋过很多次。差一点在街头冻死，差一点被货车撞上。每一次都差了那么一点。（来源：马斯）'
     CreateEvent = @{ Text = '幸运+10'; Changes = @( @{ ExAttr = 108; ExAttrValue = '10' } ) } }

  @{ Id = 880005; Name = '小书虫'; Type = 4; CanCreateGet = $true
     Des = '教会的卷宗中记录着大量的神秘学知识，随时都向她敞开大门。（来源：可可·小神官）'
     CreateEvent = @{ Text = '博学+10，神秘学+5'
                      Changes = @( @{ Skill = 501; SkillValue = '10' },
                                   @{ Skill = 502; SkillValue = '5' } ) } }

  @{ Id = 880006; Name = '隐退的传奇杀手'; Type = 4; CanCreateGet = $true
     Des = '受了伤、落下病根的杀手。他曾是令敌人闻风丧胆的恐怖杀手。（来源：安格斯·五金店店员）'
     CreateEvent = @{ Text = '体质-10，斗殴-10，射击-10'
                      Changes = @( @{ Attr = 6;   AttrValue = '-10' },
                                   @{ Skill = 101; SkillValue = '-10' },
                                   @{ Skill = 102; SkillValue = '-10' } ) } }

  @{ Id = 880007; Name = '警长的交涉技巧'; Type = 4; CanCreateGet = $true
     Des = '多年破案的经验让他知道如何和不同类型的人打交道。（来源：卢浮·警长）'
     CreateEvent = @{ Text = '交涉+10'; Changes = @( @{ Skill = 403; SkillValue = '10' } ) } }

  @{ Id = 880008; Name = '心灵手巧'; Type = 4; CanCreateGet = $true
     Des = '在空闲的时候，艾玛就会捣鼓一些奇怪的小玩意。（来源：艾玛·侦探）'
     CreateEvent = $null
     WakeEvent   = @{ Text = '能工巧匠判定获得1颗奖励骰'
                      Changes = @( @{ SkillDieType = 601; SkillDieValue = 1 } ) }
     SleepyEvent = @{ Text = '收回能工巧匠奖励骰'
                      Changes = @( @{ SkillDieType = 601; SkillDieValue = -1 } ) } }

  @{ Id = 880009; Name = '岁月不饶人'; Type = 4; CanCreateGet = $true
     Des = '移动力-2，但长期劳作使力量与体质得以保持。（来源：本·司机兼搬运工）'
     CreateEvent = @{ Text = '速度-2，力量+5，体质+5'
                      Changes = @( @{ ExAttr = 119; ExAttrValue = '-2' },
                                   @{ Attr = 1;     AttrValue = '5' },
                                   @{ Attr = 6;     AttrValue = '5' } ) } }

  @{ Id = 880010; Name = '时空旅行者——异世界综合征'; Type = 4; CanCreateGet = $true
     Des = '每到一个新的世界，身体都需要很长一段时间重新适应，习得的力量也大多处于封印状态。（来源：莫离/茉莉·时空旅者）'
     CreateEvent = @{ Text = '斗殴-15、射击-15、力量-10、体质-10、敏捷-10、感知-5、观察-5、运动-5'
                      Changes = @( @{ Skill = 101; SkillValue = '-15' },
                                   @{ Skill = 102; SkillValue = '-15' },
                                   @{ Attr = 1;    AttrValue = '-10' },
                                   @{ Attr = 6;    AttrValue = '-10' },
                                   @{ Attr = 2;    AttrValue = '-10' },
                                   @{ Skill = 301; SkillValue = '-5' },
                                   @{ Skill = 302; SkillValue = '-5' },
                                   @{ Skill = 201; SkillValue = '-5' } ) } }

  @{ Id = 880011; Name = '那一日的记忆'; Type = 4; CanCreateGet = $true
     Des = '那张假面后的脸，成了她一辈子的梦魇。（来源：夜柳）'
     CreateEvent = $null
     WakeEvent   = @{ Text = '意志判定获得1颗惩罚骰'
                      Changes = @( @{ HeroDieType = 4; HeroDieValue = -1 } ) }
     SleepyEvent = @{ Text = '收回意志惩罚骰'
                      Changes = @( @{ HeroDieType = 4; HeroDieValue = 1 } ) } }
)

$utf8 = New-Object System.Text.UTF8Encoding($false)
$ok = 0
foreach ($t in $traits) {
  $text = New-TraitText $t
  $null = $text | ConvertFrom-Json   # 自检：必须是合法 JSON
  $path = Join-Path $TraitDir ($t.Id.ToString() + '.txt')
  [System.IO.File]::WriteAllText($path, $text, $utf8)
  $ok++
  Write-Output ('[OK] ' + $t.Id + '  ' + $t.Name)
}
Write-Output ('共 ' + $ok + ' 个特质 -> ' + $TraitDir)
