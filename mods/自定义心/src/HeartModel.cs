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

        /// <summary>默认心的外观特效，保持与默认心一致。</summary>
        public const string HeartEffectKey = "LightGather_01";

        /// <summary>一颗心除速度外最多还能选几项。</summary>
        public const int MaxExtraStats = 4;

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

        /// <summary>给这颗心分配的 buff id；0 表示还没分配。</summary>
        public int BuffId;

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
            return line + "\n" +
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
            return line + "\n" + XinText.ShinRules;
        }
    }
}
