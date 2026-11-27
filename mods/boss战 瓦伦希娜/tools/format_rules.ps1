# 只整理瓦伦希娜的规则字段。物品故事、神话介绍、名称和语音字幕保持原文。
function Format-ValennaRules([System.Collections.IDictionary]$Tables) {
    $eyeId = [ValennaBattle.StrikeProfiles]::EyeChargeId
    $overheatId = [ValennaBattle.StrikeProfiles]::EyeOverheatId
    $styles = [ordered]@{
        '预知眼·过热' = @{ Color = 'E77167'; Buff = $overheatId }
        '震颤·灼热' = @{ Color = 'F35D50'; Buff = 885134 }
        '灼热·震颤' = @{ Color = 'F35D50'; Buff = 885134 }
        '震颤引爆' = @{ Color = 'FFA15E'; Buff = 885148 }
        '加速的未来' = @{ Color = '77D1DC'; Buff = 885138 }
        '狩猎目标' = @{ Color = 'E76160'; Buff = 885139 }
        '狩猎时刻' = @{ Color = 'E76160' }
        '伤害强化' = @{ Color = 'EABA6D'; Buff = 885153 }
        '预知眼' = @{ Color = '8CD6EB'; Buff = $eyeId }
        '呼吸法' = @{ Color = 'EBC556'; Buff = 885131 }
        '加速弹' = @{ Color = '79CAD7'; Buff = 885137 }
        '燃烧' = @{ Color = 'F35D50'; Buff = [ValennaBattle.StrikeProfiles]::BurnId }
        '烧伤' = @{ Color = 'F35D50'; Buff = [ValennaBattle.StrikeProfiles]::BurnId }
        '震颤' = @{ Color = 'FFA15E'; Buff = 885132 }
        '醉酒' = @{ Color = 'CBB1DD'; Buff = [ValennaBattle.StrikeProfiles]::DrunkId }
        '昏迷' = @{ Color = 'D196C7'; Buff = [ValennaBattle.StrikeProfiles]::StunnedId }
        '恐惧' = @{ Color = 'D196C7'; Buff = 128 }
        '护甲' = @{ Color = 'AAC4D2' }
        '处置' = @{ Color = 'EE827B' }
        '解兔' = @{ Color = 'FFA15E' }
        '[预知]' = @{ Color = '8CD6EB' }
        '「望」' = @{ Color = 'F9CA75'; Buff = [ValennaBattle.StrikeProfiles]::FocusId }
    }

    $keywords = ($styles.Keys | Sort-Object Length -Descending |
        ForEach-Object { [regex]::Escape($_) }) -join '|'
    # 已经标记的段落整体保留，其它现存标签也不参与关键词匹配。
    $markup = [regex]::new("(?is)<color\b[^>]*>.*?</color>|<[^>]+>|(?<keyword>$keywords)")
    $keywordOnly = [regex]::new($keywords)
    # 首尾标记只供旧版 UGUI 测量，适配不同面板宽度。先划分短语，再给里面的关键词上色。
    $open = [string][char]0x2063
    $close = [string][char]0x2064
    $gap = '[\p{Zs}\t]*(?:\r?\n[\p{Zs}\t]*)?'
    $number = '[0-9]+(?:[dD][0-9]+|\.[0-9]+)?(?:[-~～][0-9]+)?'
    $stat = '精神值上限|(?:检定)?成功区间|震颤层数|[本每]段伤害|基础伤害|普通伤害|攻击伤害|伤害|速度|意志|体质|暴击率|隐匿|远程攻击|斗殴|运动|力量|能工巧匠|侦察|感知|交涉'
    $unit = "点(?:的?(?:爆炸|额外|普通)?伤害|(?:理智|精神|生命)值?|充能)?|层(?:${gap}(?:\[加速的未来\]|$keywords|充能))?|颗(?:${gap}(?:奖励骰|惩罚骰))?|发(?:${gap}加速弹)?|个行动轮|小时|回合|精神值|理智值|资金|支|瓶|次(?:${gap}射击暗骰)?|倍|面|段|[%％](?:${gap}伤害)?"
    $groups = [regex]::new("<[^>]+>|(?<phrase>第${gap}[0-9、]+${gap}段|(?:充能上限|至少)${gap}${number}层|(?:恢复|损失|消耗)${gap}${number}层充能|最多储存${gap}${number}发|(?:最低降至|速度高于)${gap}$number|(?:$stat)${gap}[+＋−-]${gap}$number(?:[%％])?|[+＋−-]?${number}${gap}(?:$unit)|[+＋−-]$number|[0-9]+[dD][0-9]+)")
    $timingColors = @{
        '[使用时]' = '55D8EF'; '[装备时]' = '55D8EF'; '[命中时]' = 'A8E96D'; '[暴击命中时]' = 'B3F26E'
        '[消耗加速弹并命中时]' = 'A8E96D'
        '[闪避成功时]' = 'A8E96D'; '[攻击后]' = 'A8E96D'; '[击杀时]' = 'A8E96D'; '[回合开始]' = '55D8EF'
        '[战斗开始时]' = '55D8EF'; '[进入剧本时]' = '55D8EF'; '[击杀猎物时]' = 'A8E96D'; '[苏醒时]' = '55D8EF'
        '[苏醒期间]' = '55D8EF'; '[回合开始时]' = '55D8EF'; '[行动轮结束时]' = '55D8EF'; '[回合结束时]' = '55D8EF'; '[战斗结束时]' = '55D8EF'
        '[消耗时]' = '55D8EF'; '[品味雪茄]' = '55D8EF'; '[叼在嘴里]' = '55D8EF'; '[饮用时]' = '55D8EF'; '[辅助闪避]' = '55D8EF'
        '[检定时]' = '55D8EF'; '[检定成功时]' = 'A8E96D'; '[暴击时]' = 'B3F26E'; '[攻击时]' = '55D8EF'
        '[引爆时]' = 'FFA15E'; '[受击时]' = 'EE827B'; '[过热期间]' = 'E77167'
    }
    $timingPattern = '(?m)^(?:' + (($timingColors.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')'
    $stats = @{ RuleFields = 0; StyledFields = 0; BuffLinks = 0 }

    foreach ($entry in $Tables.GetEnumerator()) {
        $row = $entry.Value.Data.value
        $category = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($entry.Key))
        $fields = @(switch ($category) {
            'BattleSkill' { $row.Description; $row.EffectDesc }
            'Item' {
                $row.ItemEffectDes
                $row.EquipmentConfigData.OverrideDesc
                foreach ($use in $row.ConfigData) { $use.OverrideDesc }
            }
            'MythicalItem' { $row.Localization_Effct }
            'Buff' { $row.Des }
            'Trait' { $row.Localization_Des }
        })
        $seenBuffs = [Collections.Generic.HashSet[int]]::new()
        $links = [Collections.Generic.List[int]]::new()
        foreach ($id in $row.BuffsLink) { if ($seenBuffs.Add([int]$id)) { $links.Add([int]$id) } }

        foreach ($field in $fields) {
            if ($field -isnot [System.Collections.IDictionary] -or
                [string]::IsNullOrWhiteSpace($field.InputText)) { continue }
            $stats.RuleFields++
            $plain = [regex]::Replace([string]$field.InputText, '<[^>]+>', '')
            if ($row.Contains('BuffsLink')) {
                foreach ($match in $keywordOnly.Matches($plain)) {
                    $id = $styles[$match.Value].Buff
                    if ($id -and $seenBuffs.Add([int]$id)) {
                        $links.Add([int]$id)
                        $stats.BuffLinks++
                    }
                }
            }
            # 退役技能从现有表读取；新表可能已经排版，旧表则仍带 U+2060。
            if ($field.InputText.Contains($open)) { continue }
            $source = $field.InputText.Replace([string][char]0x2060, '')
            $formatted = [regex]::Replace($source, '(?<=[\p{IsCJKUnifiedIdeographs}]) +(?=[+＋−-]?[0-9])', '')
            $formatted = $groups.Replace($formatted, {
                param($match)
                if (!$match.Groups['phrase'].Success) { return $match.Value }
                return $open + ($match.Value -replace '\s', '') + $close
            })
            $formatted = [regex]::Replace($formatted, $timingPattern, {
                param($match)
                "<color=#$($timingColors[$match.Value])>$($match.Value)</color>"
            })
            $formatted = $markup.Replace($formatted, {
                param($match)
                if (!$match.Groups['keyword'].Success) { return $match.Value }
                $word = $match.Value
                $style = $styles[$word]
                if ($word -eq '预知眼·过热') { return "<color=#8CD6EB>预知眼</color><color=#$($style.Color)>·过热</color>" }
                return "<color=#$($style.Color)>$word</color>"
            })
            if ($category -in 'BattleSkill','Trait') {
                $formatted = [regex]::Replace($formatted, "(?m)(?<=^$open)第[0-9、]+段", '<b><color=#EBCB91>$0</color></b>')
            }
            if (![string]::Equals($formatted, $field.InputText, [StringComparison]::Ordinal)) {
                $field.InputText = $formatted
                $stats.StyledFields++
            }
        }
        if ($row.Contains('BuffsLink')) { $row.BuffsLink = $links.ToArray() }
    }
    return $stats
}
