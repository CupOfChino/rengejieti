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
        '伤害强化' = @{ Color = 'EABA6D'; Buff = 885153 }
        '预知眼' = @{ Color = '8CD6EB'; Buff = $eyeId }
        '呼吸法' = @{ Color = 'EBC556'; Buff = 885131 }
        '加速弹' = @{ Color = '79CAD7'; Buff = 885137 }
        '燃烧' = @{ Color = 'F35D50'; Buff = [ValennaBattle.StrikeProfiles]::BurnId }
        '烧伤' = @{ Color = 'F35D50'; Buff = [ValennaBattle.StrikeProfiles]::BurnId }
        '震颤' = @{ Color = 'FFA15E'; Buff = 885132 }
        '醉酒' = @{ Color = 'CBB1DD'; Buff = [ValennaBattle.StrikeProfiles]::DrunkId }
        '恐惧' = @{ Color = 'D196C7'; Buff = 128 }
        '护甲' = @{ Color = 'AAC4D2' }
        '处置' = @{ Color = 'EE827B' }
        '「望」' = @{ Color = 'F9CA75'; Buff = [ValennaBattle.StrikeProfiles]::FocusId }
    }

    $keywords = ($styles.Keys | Sort-Object Length -Descending |
        ForEach-Object { [regex]::Escape($_) }) -join '|'
    # 已经标记的段落整体保留，其它现存标签也不参与关键词匹配。
    $markup = [regex]::new("(?is)<color\b[^>]*>.*?</color>|<[^>]+>|(?<keyword>$keywords)")
    $keywordOnly = [regex]::new($keywords)
    $numberUnit = [regex]::new('(?<=\d)[\p{Zs}]*(?:\r?\n[\p{Zs}]*)?(?<unit>点(?:的)?(?:爆炸|额外|普通)?伤害|点(?:理智|精神)(?:值)?|点充能|层|发|次|颗)')
    $attributeDelta = [regex]::new('(?<stat>速度|每段伤害|基础伤害)[\p{Zs}]*(?:\r?\n[\p{Zs}]*)?(?<sign>[+＋−-])[\p{Zs}]*(?=\d)')
    $timingColors = @{
        '[使用时]' = '55D8EF'; '[命中时]' = 'A8E96D'; '[暴击命中时]' = 'B3F26E'
        '[攻击后]' = 'A8E96D'; '[击杀时]' = 'A8E96D'; '[伤害加成]' = 'F4C16B'
    }
    $timingPattern = '(?m)^\[(使用时|命中时|暴击命中时|攻击后|击杀时|伤害加成)\]'
    $joiner = [string][char]0x2060
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
            $formatted = $numberUnit.Replace([string]$field.InputText, {
                param($match)
                return $joiner + ($match.Groups['unit'].Value.ToCharArray() -join $joiner)
            })
            $formatted = $attributeDelta.Replace($formatted, {
                param($match)
                return ($match.Groups['stat'].Value.ToCharArray() -join $joiner) +
                    $joiner + $match.Groups['sign'].Value + $joiner
            })
            $formatted = $markup.Replace($formatted, {
                param($match)
                if (!$match.Groups['keyword'].Success) { return $match.Value }
                $word = $match.Value
                $style = $styles[$word]
                if ($word -eq '预知眼·过热') { return "<color=#8CD6EB>预知眼</color><color=#$($style.Color)>·过热</color>" }
                return "<color=#$($style.Color)>$word</color>"
            })
            if ($category -eq 'BattleSkill') {
                $formatted = [regex]::Replace($formatted, $timingPattern, {
                    param($match)
                    "<color=#$($timingColors[$match.Value])>$($match.Value)</color>"
                })
                $formatted = [regex]::Replace($formatted, '(?m)^第 [0-9、]+ 段', '<b><color=#EBCB91>$0</color></b>')
            }
            if ($formatted -ne $field.InputText) {
                $field.InputText = $formatted
                $stats.StyledFields++
            }
        }
        if ($row.Contains('BuffsLink')) { $row.BuffsLink = $links.ToArray() }
    }
    return $stats
}
