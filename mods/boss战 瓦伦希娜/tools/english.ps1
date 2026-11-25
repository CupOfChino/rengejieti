# 外层键为生成表的相对路径（不含扩展名）。内层键支持点分路径（数组下标用数字段）。
$ValennaEnglish = @{}
function Add-ValennaEnglish([string]$Path, [string]$NameField, [string]$Name, [string]$DescriptionField, [string]$Description) {
    $ValennaEnglish[$Path] = @{ $NameField = $Name; $DescriptionField = $Description }
}

$poiseBonus = '[On Use] At 6 or more Poise, each hit deals +1 damage (max +1).'
$stackBonus = '[On Use] Each hit deals +1 damage per 6 combined stacks of Burn and Tremor on the target (max +2).'
$spendRound = 'Spend 1 Acceleration Round if available.'
$scorch = '[On Hit] Convert Tremor to Tremor - Scorch.'
$burst = '[On Hit] Trigger Tremor Burst once.'
$skillText = @{
    885121 = @('Colpi di Taglio', (
        'Brawl check | 2 hits', $poiseBonus,
        '【I】 1d4', '[On Hit] Inflict 1 Burn.',
        '【II】 1d4', '[On Hit] Immediately resolve the target''s Burn once.' -join "`n"))
    885124 = @('Colpo di Punta', (
        'Combined Brawl and Shoot check | 2 hits', $poiseBonus,
        '【I】 1d3', $spendRound, '[On Hit] Inflict 1 Burn if a round was spent.',
        '【II】 1d4', $spendRound, '[On Hit] Inflict 2 Tremor.' -join "`n"))
    885125 = @('Sezionatura di Coniglio', (
        'Brawl check | 2 hits', $poiseBonus,
        '【I】 1d3', '[On Hit] Inflict 1 Tremor.',
        '【II】 1d4', $scorch, $burst -join "`n"))
    885126 = @('Sezionatura di Cervo', (
        'Combined Brawl and Dexterity check | 3 hits', $poiseBonus, $stackBonus, '[On Use] Gain 4 Poise.',
        '【I】 1d6', '[On Hit] Inflict 2 Burn.',
        '【II】 1d6', '[On Hit] Inflict 2 Tremor.',
        '【III】 1d6', $scorch, '[On Hit] Trigger Tremor Burst twice.' -join "`n"))
    885127 = @('I''ll Gladly Blast a Hole Through You', (
        'Combined Brawl and Shoot check | 3 hits', $poiseBonus,
        '[On Use] At 6 or more combined stacks of Burn and Tremor on the target, each hit deals +1 damage.',
        '[On Use] Gain 2 Poise.',
        '【I】 1d4', $spendRound, '[On Hit] Inflict 1 Burn if a round was spent.',
        '【II】 1d4', $spendRound, '[On Hit] Inflict 2 Tremor.',
        '【III】 1d6', $burst -join "`n"))
    885128 = @('Sezionatura di Elefante', (
        'Combined Brawl and Dexterity check | 4 hits', $poiseBonus, $stackBonus, '[On Use] Gain 3 Poise.',
        '【I】 1d4', $spendRound, '[On Hit] Inflict 1 Burn.', '[On Hit] Inflict 2 Tremor.',
        '【II】 1d4', $spendRound, '[On Hit] Inflict 1 Burn.', '[On Hit] Inflict 2 Tremor.',
        '【III】 1d6', $spendRound, $scorch,
        '【IV】 1d6', '[On Hit] Trigger Tremor Burst twice.', '[On Critical Hit] Trigger 2 additional bursts.' -join "`n"))
    885129 = @('Disposal', (
        'Brawl check, requires a success or better | 5 hits', $poiseBonus,
        'Automatically selects the Game Target when chosen. The target cannot dodge.',
        'Each hit deals 1 base damage. Below 10 Eye of Precognition stacks, roll no additional damage die.',
        'At 10 Eye of Precognition stacks, roll 1d2. At 20, roll 1d4. At 30, roll 1d6.',
        $stackBonus, '[On Use] Double the damage of the hit if the target is at half HP or lower.',
        '[On Use] Gain 3 Poise.',
        '[On Use] With at least 2 SP and while free of Frail and Unhinged, spend 2 SP and gain 1 Mang.',
        '[After Attack] Refill Acceleration Rounds to 10.',
        '[After Attack] Remove the target''s Game Target mark.',
        '[On Kill] Mark another enemy next round.',
        '【I】 1 + Eye of Precognition damage die', '[On Hit] Inflict 1 Tremor.',
        '【II】 1 + Eye of Precognition damage die', '[On Hit] Inflict 1 Burn.',
        '【III】 1 + Eye of Precognition damage die', '[On Hit] Inflict 1 Burn.',
        '【IV】 1 + Eye of Precognition damage die', $scorch,
        '【V】 1 + Eye of Precognition damage die', 'Spend up to 5 Acceleration Rounds.',
        'If Mang is spent, this hit gains +2 base damage.',
        '[On Hit] Trigger Tremor Burst 3 times.', '[On Critical Hit] Trigger 2 additional bursts.' -join "`n"))
    885151 = @('Reload', '[On Use] Refill Acceleration Rounds.')
    885152 = @('Precognition', (
        'Each stack of Eye of Precognition increases the check success range by 1.',
        'You can dodge normally undodgeable attacks, including thrown items, spells, and counterattacks after an enemy dodges your attack. On a success, you avoid the effect on yourself.',
        'These special dodges do not trigger [Sezionatura di Coniglio]. Dodging every hit of a normal attack still lets you counter with [Sezionatura di Coniglio].',
        '[On Use] Refill Acceleration Rounds, once per action round.',
        '[On Dodge] Gain 1 Poise, up to twice per action round.',
        '[On Dodge] Restore 1 SP, up to twice per battle.' -join "`n"))
}
foreach ($entry in $skillText.GetEnumerator()) {
    Add-ValennaEnglish "BattleSkill/$($entry.Key)" 'Name' $entry.Value[0] 'Description' $entry.Value[1]
}
Add-ValennaEnglish 'BattleSkill/885122' 'Name' 'Returning Flame' 'Description' 'Follow a close-range rising slash with a backswing. Hits twice for 1 base damage each.'
Add-ValennaEnglish 'BattleSkill/885123' 'Name' 'Crossflash Pursuit' 'Description' 'Follow two crossing slashes with a flash of the gunblade. Hits 3 times for 1 base damage each.'
Add-ValennaEnglish 'BattleSkill/885130' 'Name' 'Precognition' 'Description' 'Prepare Precognition. After dodging every hit of one enemy attack, counter with Sezionatura di Coniglio.'

Add-ValennaEnglish 'PartnerData/885101' 'CharacterName' 'Valencina' 'CharacterDes' '<color=#FF4040>Underboss</color> · The Fingers · The Thumb · House of Spiders · War Hero · <color=#FF4040>Seven Great Families</color> · Smoke War'
$ValennaEnglish['PartnerData/885101']['AI.CommonActions.0.Name'] = 'Colpi di Taglio'
$ValennaEnglish['PartnerData/885101']['CustomTags'] = @('The Fingers', 'The Thumb', 'House of Spiders', 'War Hero', 'Smoke War')

$traitText = @{
    885141 = @('La Spada di Palermo', "Stealth -10.`nUse the seven Palermo sword techniques in the attack menu.`nWith Time to Hunt, automatically designate a Game Target at the start of battle.")
    885142 = @('Precognition', ("Automatically equip the exclusive Eye of Precognition when entering a scenario or battle.",
        'While the Eye of Precognition is active, [Precognition] replaces Dodge and Brawl checks gain 1 bonus die.',
        'After dodging every hit of a normal attack, counter with Sezionatura di Coniglio.' -join "`n"))
    885143 = @('Time to Hunt', (
        '[Battle Start] Randomly mark one enemy as the Game Target.',
        'Deal +20% damage to the Game Target.',
        'If the Game Target has more than 10 Speed, reduce its Speed by 50 to a minimum of 10.',
        'Disposal locks onto the Game Target and clears the mark after attacking. If it kills the Game Target, mark another enemy next round.',
        'At the end of every third action round, if no mark remains, designate a new Game Target next round.',
        '[On Killing the Game Target] Other human enemies make a Willpower check. On failure, they gain Fear.' -join "`n"))
    885144 = @('The Shame of Famiglia Bognatelli', (
        '[On Entering a Scenario] Gain 1,000 funds.',
        'On entering or clearing a scenario, replenish your supply to 3 Fine Cigars and 1 bottle of High-Proof Vodka, and refill Acceleration Rounds.',
        '[Battle Start] Refill Acceleration Rounds to 10.' -join "`n"))
    885145 = @('War Hero - Valencina', "Willpower +10.`nMaximum SP +15.")
    885146 = @('Chronic Drunkard', "Brawl -5, Ranged Attack -5, Strength -5.`nConstitution -10, Craft -10.")
    885147 = @('Golden Ticket from a Dream', 'Observation +5, Perception +5.')
    885155 = @('Shin (心) - Valencina', (
        "Speed +$([ValennaBattle.StrikeProfiles]::HeartSpeed).",
        '[While Awakened] When a combat skill inflicts Tremor, inflict 1 extra stack.',
        '[While Awakened] When a combat skill grants Poise, gain 1 extra stack.' -join "`n"))
}
foreach ($entry in $traitText.GetEnumerator()) {
    Add-ValennaEnglish "Trait/$($entry.Key)" 'Localization_Name' $entry.Value[0] 'Localization_Des' $entry.Value[1]
}

$eyeName = 'Eye of Precognition'
$eyeLore = 'This eye does not see the future through magic. It works more like a computer, analyzing everything in its field of view to predict what will happen next. The relic known as Odin''s Eye is one fragment of an artifact split into three pieces.'
$eyeRules = (
    '[Turn Start] If the Eye of Precognition is not overheated, replace your Dodge with [Precognition].',
    'While the Eye of Precognition is not overheated, Brawl checks gain 1 bonus die.')
$itemText = @{
    885111 = @('Palermo - Gunblade', 'Valencina carries this gunblade slung across her back. Famiglia Bognatelli custom Acceleration Rounds fire the instant she strikes at close range.', (
        '[Battle Start] Refill Acceleration Rounds to 10.',
        '[On Hit] If this hit spent an Acceleration Round and passed the Shoot check, inflict 1 Burn and 1 Tremor.',
        '[On Burst] Gain 1 Damage Up, up to 3 stacks per action round.',
        '[Turn End] Clear all Damage Up stacks.' -join "`n"), '1D6')
    885112 = @($eyeName, $eyeLore, ((@('Valencina''s exclusive armor. Cannot be removed once equipped.') + $eyeRules + @(
        '[Turn Start] Recover 5 charge in combat and exploration, up to 30.',
        '[When Hit] Lose 5 charge. An assisted dodge also costs 5 charge.',
        '[Assisted Dodge] Gain 2 bonus dice if you have more than 10 charge before activation. Otherwise, gain 1 bonus die.',
        'Within one attack, a successful dodge carries the bonus dice to the next hit. After a failed dodge, assistance can activate again on the next hit.',
        '[On Dodge] Restore 1 SP. If the eye was not overheated when you rolled, also gain 1 Accelerating Future.',
        'At zero charge, enter Eye of Precognition - Overheat. Dodge checks receive 1 penalty die.',
        'While overheated, build 10 stacks at the start of each turn. Recover at 30 stacks.')) -join "`n"))
    885113 = @('Palermo - Cutlass', 'Valencina wears the other Palermo blade at her hip. She uses this one-handed sword for Colpi di Taglio.', (
        '[On Attack] Count the target''s combined Burn and Tremor stacks.',
        'Deal +5% damage per stack, up to +100%.',
        'If the target still has armor, deal +10% damage per stack instead, up to +200%.' -join "`n"), '2D4')
    885114 = @('Fine Cigars', 'A chain smoker''s favorite. “Where does that chain smoker get the money for custom rounds and expensive cigars?”', (
        'Holds up to 3 cigars.',
        '[Savor the Cigar] Spend 1 cigar and 1 hour to restore 1D2 SP.',
        '[Hold It in Your Mouth] Keep the cigar. SP stays the same.' -join "`n"))
    885115 = @('High-Proof Vodka', 'A drunkard''s favorite. Valencina always keeps a bottle for herself.', (
        'Carries 1 bottle.',
        '[Drink] Restore 1D4 SP and become Drunk.',
        'Drunk lasts 12 hours and adds 1 penalty die to every check.' -join "`n"))
    885116 = @('Commander''s Hat', 'A deep-red, wide-brimmed hat with gold trim on the crown and brim. Standard issue for the family''s second-rank commanders.', '[While Equipped] Diplomacy +5.')
}
foreach ($entry in $itemText.GetEnumerator()) {
    $id = [int]$entry.Key
    $translation = $entry.Value
    $fields = @{ ItemName = $translation[0]; ItemDes = $translation[1]; ItemEffectDes = $translation[2] }
    switch ($id) {
        { $_ -in 885111, 885113 } { $fields['EquipmentConfigData.OverrideDesc'] = "Weapon damage: $($translation[3]).`n$($translation[2])" }
        { $_ -in 885112, 885116 } { $fields['EquipmentConfigData.OverrideDesc'] = $translation[2] }
        { $_ -in 885114, 885115 } { $fields['ConfigData.0.OverrideDesc'] = $translation[2] }
    }
    $ValennaEnglish["Item/$id"] = $fields
}
$ValennaEnglish['MythicalItem/885112'] = @{
    Localization_Name = $eyeName
    Localization_Des = $eyeLore
    Localization_Effct = "Restore 1 SP after a successful dodge.`nIf the eye was not overheated when you rolled, also gain 1 Accelerating Future."
}
$buffText = @{
    885131 = @('Poise', (
        '[On Hit] Each hit rolls for a critical independently. Each stack adds 5% critical chance, up to 100%.',
        '[On Critical] Deal 1.2 times damage for this hit and spend 1 stack.',
        '[Turn End] Lose 1 stack.' -join "`n"))
    885132 = @('Tremor', (
        '[On Burst] Increase the Stagger meter by one third of the Tremor stacks, rounded up, then consume 1 Tremor.',
        'Each increase is capped at one third of the target''s Willpower, rounded down. If Willpower is positive, the cap is at least 1.',
        'The Stagger meter fills at the target''s Willpower. At full, the target falls into a Coma for 1 action round. Afterward, the meter resets to zero.',
        'The meter cannot build while the target is in a Coma.',
        '[Turn End] Lose 1 stack.' -join "`n"))
    885133 = @('Burn', 'This status follows the standard Burn rules.')
    885134 = @('Tremor - Scorch', (
        'Converted from Tremor. Retains existing stacks and absorbs new Tremor stacks.',
        '[On Burst] Resolve a normal Tremor Burst, then deal explosion damage.',
        'Explosion damage equals half of the combined Burn and Tremor stacks before the burst, rounded down.',
        'Burn and Tremor each lose an additional third of their original stacks, rounded down separately.',
        '[Turn End] Lose 1 stack.' -join "`n"))
    885135 = @($eyeName, ((@('Maximum charge is 30. With Precognition, each stack increases the check success range by 1.') + $eyeRules + @(
        '[Turn Start] Recover 5 charge in combat and exploration.',
        'Using assisted dodge or taking a hit costs 5 charge. At zero charge, enter Eye of Precognition - Overheat.')) -join "`n"))
    885136 = @('Eye of Precognition - Overheat', (
        '[While Overheated] Dodge checks receive 1 penalty die.',
        '[Turn Start] Build 10 stacks in combat and exploration.',
        'At 30 stacks, return to Eye of Precognition.' -join "`n"))
    885137 = @('Acceleration Round', (
        'Store up to 10 rounds.',
        '[On Spending] Each hit makes only one hidden Shoot check. The round is spent even if the check fails.',
        '[On Successful Check] This hit deals +10% damage. Gain 1 Poise per round spent.' -join "`n"))
    885138 = @('Accelerating Future', (
        '[On Dodge] Gain 1 stack if the Eye of Precognition was not overheated when you rolled, up to 5.',
        'Each stack grants Brawl +2 and Sport +2.',
        'At 5 stacks, gain 1 bonus die on checks for actions other than spells.',
        '[On Attack] The next attack you initiate deals +5% damage per stack. Clear all stacks afterward.',
        'Sezionatura di Coniglio neither consumes the stacks nor gains their damage bonus.' -join "`n"))
    885139 = @('Game Target', (
        'Take +20% damage from whoever marked you.',
        'If your Speed is above 10, reduce it by 50 to a minimum of 10.',
        'Disposal can only select the Game Target.' -join "`n"))
    885140 = @('Shin (心) - Valencina', ($traitText[885155][1].Replace('[While Awakened] ', '')))
    885148 = @('Tremor Burst', 'Trigger the target''s Tremor Burst and resolve each burst separately. If the status has become Tremor - Scorch, also resolve its explosion.')
    885150 = @('Drunk', "Lasts 12 hours.`n[On Check] Receive 1 penalty die in combat and exploration.")
    885153 = @('Damage Up', "Deal +10% normal damage per stack.`n[Turn End] Clear all stacks.")
    885154 = @('Mang', 'The fifth hit of this Disposal gains +2 base damage.')
}
foreach ($entry in $buffText.GetEnumerator()) {
    Add-ValennaEnglish "Buff/$($entry.Key)" 'Name' $entry.Value[0] 'Des' $entry.Value[1]
}

function Format-ValennaEnglishRule([string]$Text) {
    $colors = @{
        'Eye of Precognition' = '8CD6EB'; 'Tremor - Scorch' = 'F35D50'; 'Tremor Burst' = 'FFA15E'
        'Accelerating Future' = '77D1DC'; 'Game Target' = 'E76160'; 'Time to Hunt' = 'E76160'
        'Damage Up' = 'EABA6D'; 'Poise' = 'EBC556'; 'Acceleration Round' = '79CAD7'; 'Acceleration Rounds' = '79CAD7'
        'Burn' = 'F35D50'; 'Tremor' = 'FFA15E'; 'Drunk' = 'CBB1DD'; 'Coma' = 'D196C7'; 'Fear' = 'D196C7'
        'armor' = 'AAC4D2'; 'Disposal' = 'EE827B'; 'Sezionatura di Coniglio' = 'FFA15E'; 'Mang' = 'F9CA75'
        'Precognition' = '8CD6EB'
    }
    $triggers = @{
        'On Use' = '55D8EF'; 'While Equipped' = '55D8EF'; 'On Hit' = 'A8E96D'; 'On Critical Hit' = 'B3F26E'
        'On Dodge' = 'A8E96D'; 'After Attack' = 'A8E96D'; 'On Kill' = 'A8E96D'; 'On Killing the Game Target' = 'A8E96D'
        'Battle Start' = '55D8EF'; 'On Entering a Scenario' = '55D8EF'; 'While Awakened' = '55D8EF'
        'Turn Start' = '55D8EF'; 'Turn End' = '55D8EF'; 'Battle End' = '55D8EF'
        'On Spending' = '55D8EF'; 'Savor the Cigar' = '55D8EF'; 'Hold It in Your Mouth' = '55D8EF'; 'Drink' = '55D8EF'
        'Assisted Dodge' = '55D8EF'; 'On Check' = '55D8EF'; 'On Successful Check' = 'A8E96D'
        'On Critical' = 'B3F26E'; 'On Attack' = '55D8EF'; 'On Burst' = 'FFA15E'; 'When Hit' = 'EE827B'
        'While Overheated' = 'E77167'
    }
    $keywords = (($colors.Keys + @('Eye of Precognition - Overheat')) | Sort-Object Length -Descending |
        ForEach-Object { [regex]::Escape($_) }) -join '|'
    [regex]::Replace($Text, "(?im)^\[(?<trigger>[^\]\r\n]+)\]|^(?<stage>【[IV]+】)|\b(?<word>$keywords)\b", {
        param($match)
        $trigger = $match.Groups['trigger'].Value
        if ($trigger) {
            if (!$triggers.ContainsKey($trigger)) { throw "缺少触发标签色 $trigger" }
            return "<color=#$($triggers[$trigger])>$($match.Value)</color>"
        }
        if ($match.Groups['stage'].Success) { return "<b><color=#EBCB91>$($match.Value)</color></b>" }
        if ($match.Value -eq 'Eye of Precognition - Overheat') {
            return '<color=#8CD6EB>Eye of Precognition</color><color=#E77167> - Overheat</color>'
        }
        return "<color=#$($colors[$match.Value])>$($match.Value)</color>"
    })
}

function Add-ValennaTableLocalization([System.Collections.IDictionary]$Tables) {
    $lexicon = [ordered]@{
        'valenna.cigar.savor' = [ordered]@{ zh = '品味雪茄'; en = 'Savor the Cigar' }
        'valenna.cigar.remove' = [ordered]@{ zh = '取下雪茄'; en = 'Put the Cigar Away' }
        'valenna.cigar.hold' = [ordered]@{ zh = '叼在嘴里'; en = 'Hold It in Your Mouth' }
        'valenna.damageUp.total' = [ordered]@{ zh = '当前普通伤害加成：+{0}%。'; en = 'Current normal damage bonus: +{0}%.' }
        'valenna.profession' = [ordered]@{ zh = '蜘蛛巢 拇指父辈'; en = 'House of Spiders · The Thumb Nursefather' }
        'valenna.fixedHeart' = [ordered]@{ zh = '瓦伦希娜已固定装备专属「心」。'; en = 'Valencina has her exclusive Shin permanently equipped.' }
    }
    foreach ($tablePath in ($ValennaEnglish.Keys | Sort-Object)) {
        $fullPath = "Assets/Resources/Config/Game/$tablePath.txt"
        if (!$Tables.Contains($fullPath)) {
            if ($tablePath -in 'BattleSkill/885122', 'BattleSkill/885123', 'BattleSkill/885130') { continue }
            throw "找不到要翻译的表 $tablePath"
        }
        $row = $Tables[$fullPath].Data.value
        $fieldTranslations = $ValennaEnglish[$tablePath]
        foreach ($fieldPath in ($fieldTranslations.Keys | Sort-Object)) {
            $segments = $fieldPath.Split('.')
            $parent = $row
            foreach ($segment in ($segments | Select-Object -SkipLast 1)) {
                if ($parent -is [System.Collections.IDictionary]) { $parent = $parent[$segment] }
                elseif ($parent -is [array]) { $parent = $parent[[int]$segment] }
                else { throw "找不到要翻译的字段 $tablePath/$fieldPath" }
            }
            $fieldName = $segments[-1]
            $source = $parent[$fieldName]
            $key = 'valenna.' + $tablePath.Replace('/', '.') + '.' + $fieldPath
            $english = $fieldTranslations[$fieldPath]
            if ($source -is [array]) {
                if ($english -isnot [array] -or $source.Count -ne $english.Count) {
                    throw "双语词条数量不一致 $key"
                }
                for ($i = 0; $i -lt $source.Count; $i++) {
                    $lexicon["$key.$i"] = [ordered]@{ zh = $source[$i]; en = $english[$i] }
                }
                continue
            }
            if ($source -is [System.Collections.IDictionary] -and $source.Contains('InputText')) {
                # 神话物品与装备可能引用同一个 TextData。先复制，避免改掉另一个表的词条键。
                $field = [ordered]@{}
                foreach ($pair in $source.GetEnumerator()) { $field[$pair.Key] = $pair.Value }
                $field.TarKey = $key
                $parent[$fieldName] = $field
                $source = $field.InputText
                if ($fieldName -in 'Description', 'EffectDesc', 'Des', 'ItemEffectDes', 'OverrideDesc', 'Localization_Effct' -or
                    ($tablePath.StartsWith('Trait/') -and $fieldName -eq 'Localization_Des')) {
                    $english = Format-ValennaEnglishRule $english
                }
            }
            $lexicon[$key] = [ordered]@{ zh = $source; en = $english }
        }
    }
    foreach ($entry in $lexicon.GetEnumerator()) {
        foreach ($language in 'zh', 'en') {
            if ($entry.Value[$language] -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.Value[$language])) {
                throw "本地化词条为空 $($entry.Key) ($language)"
            }
        }
    }
    return $lexicon
}
