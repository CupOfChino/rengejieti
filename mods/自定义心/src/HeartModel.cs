// 「自定义心」的数据模型。
// 一颗心 = 速度（固定项，必填） + 最多 4 项其它加成 + 一个自己起的后缀名。

using System.Collections.Generic;

namespace XinEditor
{
    /// <summary>「心」允许加成的几项战斗数值；括号里是游戏内部字段。</summary>
    public enum HeartStatType
    {
        DamageBonus = 0,   // 伤害加成（徒手伤害 104），上限 5
        DamageReduce = 1,  // 伤害减免（普通/爆炸/法术 三类各减 N），上限 2
        Dodge = 2,         // 闪避（额外属性 118），上限 50
        Willpower = 3,     // 意志（基础属性 POW 4），上限 50
        Occultism = 4,     // 神秘学（技能 502），上限 50
        Brawl = 5,         // 斗殴（技能 101），上限 50
        Shooting = 6,      // 射击（技能 102），上限 50
        Athletics = 7,     // 运动（技能 201），上限 50
        // 下面 4 项 2026-09-17 新增，用 Buff_ChangeAddOrReducePercentOption 实现（按 SourceKey 追踪，不会叠加）
        PhysicalDamageReducePercent = 8,   // 物理伤害减少 %（只算普通伤害），上限 50
        MagicDamageReducePercent = 9,      // 法术伤害减少 %，上限 50
        PhysicalDamageBonusPercent = 10,   // 物理伤害加成 %（只算普通伤害），上限 100
        MagicDamageBonusPercent = 11       // 法术伤害加成 %，上限 100
    }

    /// <summary>心效果的目标范围。</summary>
    public enum HeartEffectTarget
    {
        Self = 0,          // 自己
        AllAllies = 1,     // 所有友方（含自己）
        AlliesOnly = 2,    // 仅限友方（不含自己）
        SingleEnemy = 3,   // 单个敌人
        AllEnemies = 4     // 所有敌人
    }

    /// <summary>
    /// 心效果的触发时机。
    /// RoundStart 对齐游戏里的 BeforeRoundStart（自定义BGM 的“每个行动轮开始”用的就是它）。
    /// </summary>
    public enum HeartEffectTrigger
    {
        RoundStart = 0,
        RoundEnd = 1,
        Hit = 2,
        ActionStart = 3,
        ActionEnd = 4
    }

    public static class HeartConstants
    {
        /// <summary>数据包里的默认「心」特质（进战斗时挂状态）。</summary>
        public const int DefaultHeartTraitId = 880901;

        /// <summary>数据包里的默认「心」状态；没有自定义心的角色走这个。</summary>
        public const int DefaultHeartBuffId = 880901;

        /// <summary>本插件给自定义心分配的 buff id 区间（已在 docs\模组清单.md 登记）。</summary>
        public const int CustomBuffIdMin = 880910;
        public const int CustomBuffIdMax = 880999;

        /// <summary>复用数据包里的「心」图标。</summary>
        public const string HeartIconKey = "xin_icon";

        /// <summary>插件造出来的状态都带这个标记，便于识别和清理。</summary>
        public const string CommentTag = "自定义心：";

        /// <summary>预设效果用的隐藏状态标记，和心本体分开，避免两者互相误删。</summary>
        public const string EffectCommentTag = "自定义心效果：";

        /// <summary>默认心的外观特效，保持与默认心一致。</summary>
        public const string HeartEffectKey = "LightGather_01";

        /// <summary>一颗心除速度外最多还能选几项。</summary>
        public const int MaxExtraStats = 4;

        /// <summary>一颗心最多可以配置几条附加效果。</summary>
        public const int MaxEffects = 2;

        /// <summary>“具体状态”编码：值 - 偏移 = 游戏状态编号。</summary>
        public const int EffectBuffOffset = 100000;

        /// <summary>层数输入框允许的最大值（只用于挡住误输入的超长数字）。</summary>
        public const int MaxEffectLayers = 99999;

        public static int MaxValue(HeartStatType type)
        {
            switch (type)
            {
                case HeartStatType.DamageBonus: return 5;
                case HeartStatType.DamageReduce: return 2;
                case HeartStatType.PhysicalDamageReducePercent:
                case HeartStatType.MagicDamageReducePercent:
                    return 50;
                case HeartStatType.PhysicalDamageBonusPercent:
                case HeartStatType.MagicDamageBonusPercent:
                    return 100;
                default: return 50;
            }
        }

        /// <summary>是不是百分比类（显示要带 %，取整规则也不同）。</summary>
        public static bool IsPercent(HeartStatType type)
        {
            return type == HeartStatType.PhysicalDamageReducePercent
                || type == HeartStatType.MagicDamageReducePercent
                || type == HeartStatType.PhysicalDamageBonusPercent
                || type == HeartStatType.MagicDamageBonusPercent;
        }

        /// <summary>是不是"伤害加成/伤害减免"这两项（它们不按 5 的倍数取整、最低值是 1）。</summary>
        public static bool IsFlatDamage(HeartStatType type)
        {
            return type == HeartStatType.DamageBonus || type == HeartStatType.DamageReduce;
        }

        /// <summary>取值提示里的单位后缀。</summary>
        public static string UnitSuffix(HeartStatType type)
        {
            return IsPercent(type) ? "%" : "";
        }

        public static string StatName(HeartStatType type)
        {
            switch (type)
            {
                case HeartStatType.DamageBonus: return "伤害加成";
                case HeartStatType.DamageReduce: return "伤害减免";
                case HeartStatType.Dodge: return "闪避";
                case HeartStatType.Willpower: return "意志";
                case HeartStatType.Occultism: return "神秘学";
                case HeartStatType.Brawl: return "斗殴";
                case HeartStatType.Shooting: return "射击";
                case HeartStatType.Athletics: return "运动";
                case HeartStatType.PhysicalDamageReducePercent: return "物理伤害减免%";
                case HeartStatType.MagicDamageReducePercent: return "法术伤害减免%";
                case HeartStatType.PhysicalDamageBonusPercent: return "物理伤害加成%";
                case HeartStatType.MagicDamageBonusPercent: return "法术伤害加成%";
                default: return "未知";
            }
        }

        /// <summary>所有可选项（不含速度，速度是固定项）。</summary>
        public static readonly HeartStatType[] SelectableStats = new HeartStatType[]
        {
            HeartStatType.DamageBonus,
            HeartStatType.DamageReduce,
            HeartStatType.Dodge,
            HeartStatType.Willpower,
            HeartStatType.Occultism,
            HeartStatType.Brawl,
            HeartStatType.Shooting,
            HeartStatType.Athletics,
            HeartStatType.PhysicalDamageReducePercent,
            HeartStatType.MagicDamageReducePercent,
            HeartStatType.PhysicalDamageBonusPercent,
            HeartStatType.MagicDamageBonusPercent
        };
    }

    public class HeartStat
    {
        public HeartStatType Type;
        public int Value;

        public HeartStat()
        {
        }

        public HeartStat(HeartStatType type, int value)
        {
            Type = type;
            Value = value;
        }
    }

    /// <summary>一条心附加效果：在某个时机，对某类目标施加/获得某个状态若干层。</summary>
    public class HeartEffect
    {
        public HeartEffectTarget Target = HeartEffectTarget.Self;
        public HeartEffectTrigger Trigger = HeartEffectTrigger.RoundStart;

        /// <summary>
        /// 0 = 未选；2/3/5/6/11/12/13/14 = 自定义BGM 的预设（按层数，每层 10%）；
        /// 100000 + buffId = 游戏里的具体状态。
        /// </summary>
        public int EffectType;

        /// <summary>施加/获得的层数；负数不允许。</summary>
        public int Layers;

        public HeartEffect()
        {
        }

        public HeartEffect(HeartEffectTarget target, HeartEffectTrigger trigger, int effectType, int layers)
        {
            Target = target;
            Trigger = trigger;
            EffectType = effectType;
            Layers = layers < 0 ? 0 : layers;
        }

        public bool IsConfigured
        {
            get { return EffectType != 0 && Layers > 0; }
        }

        public HeartEffect Clone()
        {
            return new HeartEffect(Target, Trigger, EffectType, Layers);
        }
    }

    /// <summary>
    /// 自定义BGM 的 8 个“按层数”预设（编号与 DomainEffectPreset 保持一致）。
    /// 这里不引用 CustomBattleBg.dll，未订阅/未启用时也能安全编译和运行。
    /// </summary>
    public static class HeartEffectPreset
    {
        public const int PhysicalDamageBonus = 2;
        public const int PhysicalDamageReduce = 3;
        public const int MagicDamageBonus = 5;
        public const int MagicDamageReduce = 6;
        public const int PhysicalDamageDown = 11;
        public const int PhysicalVulnerable = 12;
        public const int MagicDamageDown = 13;
        public const int MagicVulnerable = 14;

        public static readonly int[] Supported = new int[]
        {
            PhysicalDamageBonus,
            PhysicalDamageReduce,
            PhysicalDamageDown,
            PhysicalVulnerable,
            MagicDamageBonus,
            MagicDamageReduce,
            MagicDamageDown,
            MagicVulnerable
        };

        public static bool IsSupported(int type)
        {
            for (int i = 0; i < Supported.Length; i++)
            {
                if (Supported[i] == type)
                {
                    return true;
                }
            }
            return false;
        }

        public static string DisplayName(int type)
        {
            switch (type)
            {
                case PhysicalDamageBonus: return XinText.T("物理伤害强化（层）", "Physical Damage Up (layers)");
                case PhysicalDamageReduce: return XinText.T("物理守护（层）", "Physical Guard (layers)");
                case PhysicalDamageDown: return XinText.T("物理伤害弱化（层）", "Physical Damage Down (layers)");
                case PhysicalVulnerable: return XinText.T("物理易损（层）", "Physical Vulnerable (layers)");
                case MagicDamageBonus: return XinText.T("法术伤害强化（层）", "Magic Damage Up (layers)");
                case MagicDamageReduce: return XinText.T("法术守护（层）", "Magic Guard (layers)");
                case MagicDamageDown: return XinText.T("法术伤害弱化（层）", "Magic Damage Down (layers)");
                case MagicVulnerable: return XinText.T("法术易损（层）", "Magic Vulnerable (layers)");
                default: return XinText.T("（无）", "(None)");
            }
        }

        public static string ShortName(int type)
        {
            switch (type)
            {
                case PhysicalDamageBonus: return XinText.T("物理强化", "Physical Up");
                case PhysicalDamageReduce: return XinText.T("物理守护", "Physical Guard");
                case PhysicalDamageDown: return XinText.T("物理弱化", "Physical Down");
                case PhysicalVulnerable: return XinText.T("物理易损", "Physical Vulnerable");
                case MagicDamageBonus: return XinText.T("法术强化", "Magic Up");
                case MagicDamageReduce: return XinText.T("法术守护", "Magic Guard");
                case MagicDamageDown: return XinText.T("法术弱化", "Magic Down");
                case MagicVulnerable: return XinText.T("法术易损", "Magic Vulnerable");
                default: return XinText.T("（无）", "(None)");
            }
        }

        /// <summary>状态图标：订阅/启用自定义BGM 时借它的四套图标，否则用心的图标兜底。</summary>
        public static string IconKey(int type)
        {
            if (HeartBuffCatalog.HasCustomBattleBg())
            {
                switch (type)
                {
                    case PhysicalDamageBonus: return "icon_domain_phys_up";
                    case PhysicalDamageReduce: return "icon_domain_phys_guard";
                    case PhysicalDamageDown: return "icon_domain_phys_down";
                    case PhysicalVulnerable: return "icon_domain_phys_vuln";
                    case MagicDamageBonus: return "icon_domain_magic_up";
                    case MagicDamageReduce: return "icon_domain_magic_guard";
                    case MagicDamageDown: return "icon_domain_magic_down";
                    case MagicVulnerable: return "icon_domain_magic_vuln";
                }
            }
            return HeartConstants.HeartIconKey;
        }

        public static bool IsNegative(int type)
        {
            return type == PhysicalDamageDown || type == PhysicalVulnerable ||
                   type == MagicDamageDown || type == MagicVulnerable;
        }

        public static string Description(int type, int layers)
        {
            int percent = layers * 10;
            switch (type)
            {
                case PhysicalDamageBonus: return XinText.T("造成的物理伤害 +" + percent + "%", "Physical damage dealt +" + percent + "%");
                case PhysicalDamageReduce: return XinText.T("受到的物理伤害 -" + percent + "%", "Physical damage taken -" + percent + "%");
                case PhysicalDamageDown: return XinText.T("造成的物理伤害 -" + percent + "%", "Physical damage dealt -" + percent + "%");
                case PhysicalVulnerable: return XinText.T("受到的物理伤害 +" + percent + "%", "Physical damage taken +" + percent + "%");
                case MagicDamageBonus: return XinText.T("造成的法术伤害 +" + percent + "%", "Magic damage dealt +" + percent + "%");
                case MagicDamageReduce: return XinText.T("受到的法术伤害 -" + percent + "%", "Magic damage taken -" + percent + "%");
                case MagicDamageDown: return XinText.T("造成的法术伤害 -" + percent + "%", "Magic damage dealt -" + percent + "%");
                case MagicVulnerable: return XinText.T("受到的法术伤害 +" + percent + "%", "Magic damage taken +" + percent + "%");
                default: return "";
            }
        }
    }

    /// <summary>目标/触发/效果类型的中英文显示。</summary>
    public static class HeartEffectText
    {
        public static string TargetName(HeartEffectTarget target)
        {
            switch (target)
            {
                case HeartEffectTarget.Self: return XinText.T("自己", "Self");
                case HeartEffectTarget.AllAllies: return XinText.T("所有友方", "All allies");
                case HeartEffectTarget.AlliesOnly: return XinText.T("仅限友方", "Allies only");
                case HeartEffectTarget.SingleEnemy: return XinText.T("单个敌人", "One enemy");
                case HeartEffectTarget.AllEnemies: return XinText.T("所有敌人", "All enemies");
                default: return XinText.T("未知", "Unknown");
            }
        }

        public static string TriggerName(HeartEffectTrigger trigger)
        {
            switch (trigger)
            {
                case HeartEffectTrigger.RoundStart: return XinText.T("行动轮开始", "Round start");
                case HeartEffectTrigger.RoundEnd: return XinText.T("行动轮结束", "Round end");
                case HeartEffectTrigger.Hit: return XinText.T("攻击命中后", "After an attack hits");
                case HeartEffectTrigger.ActionStart: return XinText.T("行动前", "Before an action");
                case HeartEffectTrigger.ActionEnd: return XinText.T("行动后", "After an action");
                default: return XinText.T("未知", "Unknown");
            }
        }

        public static string EffectName(int effectType)
        {
            if (effectType == 0)
            {
                return XinText.T("（无）", "(None)");
            }
            if (HeartEffectPreset.IsSupported(effectType))
            {
                return HeartEffectPreset.DisplayName(effectType);
            }
            return HeartBuffCatalog.BuffDisplayName(effectType);
        }
    }

    /// <summary>某个调查员自己的那颗心。</summary>
    public class HeartDefinition
    {
        /// <summary>存盘用的小节名，例如 heart_001。</summary>
        public string Section = "";

        /// <summary>调查员在存档里的稳定编号（RoleLibraryKey）。</summary>
        public string RoleKey = "";

        /// <summary>调查员名字，用于显示「心（某某）——」以及按名字兜底匹配。</summary>
        public string RoleName = "";

        /// <summary>玩家自己起的后缀。</summary>
        public string Suffix = "";

        /// <summary>速度，固定项。</summary>
        public int Speed = 20;

        /// <summary>除速度外的加成项，最多 4 项。</summary>
        public List<HeartStat> Stats = new List<HeartStat>();

        /// <summary>附加效果 1。</summary>
        public HeartEffect Effect1 = new HeartEffect();

        /// <summary>附加效果 2。</summary>
        public HeartEffect Effect2 = new HeartEffect();

        /// <summary>给这颗心分配的 buff id；0 表示还没分配。</summary>
        public int BuffId;

        /// <summary>预设效果 1 的运行时隐藏状态编号（只对预设有效）。</summary>
        public int Effect1BuffId;

        /// <summary>预设效果 2 的运行时隐藏状态编号（只对预设有效）。</summary>
        public int Effect2BuffId;

        public string DisplayName
        {
            get
            {
                if (XinText.IsEnglish)
                {
                    return "Shin (" + RoleName + ") - " + Suffix;
                }
                return "心（" + RoleName + "）——" + Suffix;
            }
        }

        public int GetStatValue(HeartStatType type)
        {
            for (int i = 0; i < Stats.Count; i++)
            {
                if (Stats[i].Type == type)
                {
                    return Stats[i].Value;
                }
            }
            return 0;
        }

        public bool HasStat(HeartStatType type)
        {
            for (int i = 0; i < Stats.Count; i++)
            {
                if (Stats[i].Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        public HeartEffect GetEffect(int index)
        {
            return index == 0 ? Effect1 : (index == 1 ? Effect2 : null);
        }

        public void SetEffect(int index, HeartEffect effect)
        {
            if (index == 0)
            {
                Effect1 = effect ?? new HeartEffect();
            }
            else if (index == 1)
            {
                Effect2 = effect ?? new HeartEffect();
            }
        }

        public int GetEffectBuffId(int index)
        {
            return index == 0 ? Effect1BuffId : (index == 1 ? Effect2BuffId : 0);
        }

        public void SetEffectBuffId(int index, int id)
        {
            if (index == 0)
            {
                Effect1BuffId = id;
            }
            else if (index == 1)
            {
                Effect2BuffId = id;
            }
        }

        public bool HasAnyEffectData
        {
            get
            {
                return (Effect1 != null && Effect1.IsConfigured) ||
                       (Effect2 != null && Effect2.IsConfigured);
            }
        }

        /// <summary>生成一段人能看懂的效果描述，战斗里点开 buff 就能看到。</summary>
        public string BuildDescription()
        {
            if (XinText.IsEnglish)
            {
                return BuildDescriptionEn();
            }
            List<string> parts = new List<string>();
            for (int i = 0; i < HeartConstants.SelectableStats.Length; i++)
            {
                HeartStatType type = HeartConstants.SelectableStats[i];
                int v = GetStatValue(type);
                if (v == 0)
                {
                    continue;
                }
                if (type == HeartStatType.DamageReduce)
                {
                    parts.Add("受到的伤害-" + v);
                }
                else
                {
                    parts.Add(HeartConstants.StatName(type) + "+" + v);
                }
            }
            if (Speed > 0)
            {
                parts.Add("速度+" + Speed);
            }

            string line = parts.Count > 0 ? string.Join("，", parts.ToArray()) : "无额外加成";
            return line + "\n" + BuildEffectDescription() +
                   "每回合开始时，尝试消耗1点精神值来维持【心】。\n" +
                   "精神值陷入衰弱或衰竭时解除，解除时恢复5点精神值。\n" +
                   "战斗结束时解除。\n\n" +
                   "<i>心灵力量的具象化</i>";
        }

        private string BuildDescriptionEn()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < HeartConstants.SelectableStats.Length; i++)
            {
                HeartStatType type = HeartConstants.SelectableStats[i];
                int v = GetStatValue(type);
                if (v == 0)
                {
                    continue;
                }
                if (type == HeartStatType.DamageReduce)
                {
                    parts.Add("Damage taken -" + v);
                }
                else
                {
                    parts.Add(XinText.StatName(type) + " +" + v);
                }
            }
            if (Speed > 0)
            {
                parts.Add("Speed +" + Speed);
            }
            string line = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "No extra bonuses";
            return line + "\n" + BuildEffectDescriptionEn() + XinText.ShinRules;
        }

        private string BuildEffectDescription()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < HeartConstants.MaxEffects; i++)
            {
                HeartEffect effect = GetEffect(i);
                if (effect == null || !effect.IsConfigured)
                {
                    continue;
                }
                string effectName = HeartEffectText.EffectName(effect.EffectType);
                string layers = HeartEffectPreset.IsSupported(effect.EffectType)
                    ? effect.Layers + "层（" + (effect.Layers * 10) + "%）"
                    : effect.Layers + "层";
                sb.Append(HeartEffectText.TriggerName(effect.Trigger)).Append("时，");
                if (effect.Target == HeartEffectTarget.Self)
                {
                    sb.Append("自己获得");
                }
                else if (effect.Target == HeartEffectTarget.AlliesOnly)
                {
                    sb.Append("仅对友方（不含自己）施加");
                }
                else
                {
                    sb.Append("对").Append(HeartEffectText.TargetName(effect.Target)).Append("施加");
                }
                sb.Append("【").Append(effectName).Append("】").Append(layers).Append("。\n");
            }
            return sb.ToString();
        }

        private string BuildEffectDescriptionEn()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < HeartConstants.MaxEffects; i++)
            {
                HeartEffect effect = GetEffect(i);
                if (effect == null || !effect.IsConfigured)
                {
                    continue;
                }
                string effectName = HeartEffectText.EffectName(effect.EffectType);
                string layers = HeartEffectPreset.IsSupported(effect.EffectType)
                    ? effect.Layers + " layer(s) (" + (effect.Layers * 10) + "%)"
                    : effect.Layers + " layer(s)";
                sb.Append(HeartEffectText.TriggerName(effect.Trigger)).Append(": ");
                if (effect.Target == HeartEffectTarget.Self)
                {
                    sb.Append("self gains ");
                }
                else
                {
                    sb.Append("apply ");
                }
                sb.Append(effectName).Append(" ").Append(layers);
                if (effect.Target != HeartEffectTarget.Self)
                {
                    sb.Append(" to ").Append(HeartEffectText.TargetName(effect.Target).ToLowerInvariant());
                }
                sb.Append(".\n");
            }
            return sb.ToString();
        }
    }
}
