// 「自定义心」的数据模型。
// 一颗心 = 速度（固定项，必填） + 最多 3 项其它加成 + 一个自己起的后缀名。

using System.Collections.Generic;

namespace XinEditor
{
    /// <summary>「心」允许加成的几项战斗数值；括号里是游戏内部字段。</summary>
    public enum HeartStatType
    {
        DamageBonus = 0,   // 伤害加成（徒手伤害 104），上限 5
        DamageReduce = 1,  // 伤害减免（普通/爆炸/法术 三类各减 N），上限 3
        Dodge = 2,         // 闪避（额外属性 118），上限 30
        Willpower = 3,     // 意志（基础属性 POW 4），上限 30
        Occultism = 4,     // 神秘学（技能 502），上限 30
        Brawl = 5,         // 斗殴（技能 101），上限 30
        Shooting = 6,      // 射击（技能 102），上限 30
        Athletics = 7      // 运动（技能 201），上限 30
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
        public const int MaxExtraStats = 3;

        public static int MaxValue(HeartStatType type)
        {
            switch (type)
            {
                case HeartStatType.DamageBonus: return 5;
                case HeartStatType.DamageReduce: return 3;
                default: return 30;
            }
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
            HeartStatType.Athletics
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

        /// <summary>除速度外的加成项，最多 3 项。</summary>
        public List<HeartStat> Stats = new List<HeartStat>();

        /// <summary>给这颗心分配的 buff id；0 表示还没分配。</summary>
        public int BuffId;

        public string DisplayName
        {
            get { return "心（" + RoleName + "）——" + Suffix; }
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
                   "每回合开始时消耗1点精神值。\n" +
                   "精神值陷入衰弱或衰竭时解除，解除时恢复5点精神值。\n" +
                   "战斗结束时解除。\n\n" +
                   "<i>心灵力量具象化的体现之一。\n金黄色的光芒从人的身体中绽放而出。</i>";
        }
    }
}
