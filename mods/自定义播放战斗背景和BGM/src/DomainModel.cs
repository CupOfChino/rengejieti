// 「自定义播放战斗背景和BGM」的数据模型与常量。
//
// 一个调查员 = 一条「领域」配置：
//   · 显示名固定为「调查员名字——后缀」，后缀自己填（全局不可重名）；
//   · 背景：CustomBattleAssets\Background 里的一张图/一个视频；
//   · BGM ：CustomBattleAssets\Bgm 里的一首曲，可选是否循环。

using System.Collections.Generic;
using System.IO;

namespace CustomBattleBg
{
    public static class DomainConstants
    {
        /// <summary>【领域】特质。</summary>
        public const int TraitId = 881001;

        /// <summary>「编辑自定义战斗背景和BGM」幕间入口。</summary>
        public const int EditVacatId = 881002;

        /// <summary>「添加「领域」」幕间入口。</summary>
        public const int AddVacatId = 881003;

        /// <summary>「移除「领域」」幕间入口。</summary>
        public const int RemoveVacatId = 881004;

        /// <summary>「领域展开」技艺（战斗技能）。</summary>
        public const int SkillId = 881005;

        /// <summary>技能来源标记，摘技能时按它来。</summary>
        public const string SkillSourceKey = "custombattlebg_domain_expand";

        /// <summary>资源库目录名（在游戏存档目录 %LocalLow%\MeowNature\Depersonalization-Release\ 下）。</summary>
        public const string AssetsFolderName = "CustomBattleBg";
        public const string BackgroundFolderName = "Background";
        public const string BgmFolderName = "Bgm";
        public const string StoreFileName = "profiles.cfg";

        /// <summary>数据里的备注前缀，方便识别我们自己的东西。</summary>
        public const string CommentTag = "自定义播放战斗背景和BGM：";

        /// <summary>领域状态用的图标：原版「开辟」（Buff 436）的图标，直接引用不用导素材。</summary>
        public const string DomainStatusIconKey = "icon_buff_zhenli";

        /// <summary>本 mod 运行时注册状态用的编号区间（881011 起，模组清单已登记）。</summary>
        public const int CustomBuffIdMin = 881011;
        public const int CustomBuffIdMax = 881999;

        /// <summary>运行时 CurrentBgm 的 Key 前缀（我们自己的 BGM 用它标记，便于比较/复原）。</summary>
        public const string CustomBgmKeyPrefix = "custombgm_";

        /// <summary>视频背景接管音乐通道时，给 CurrentBgm 打的标记（表示"游戏自带 BGM 已被我们关掉"）。</summary>
        public const string VideoMuteKey = "customvideo_silence";

        public static readonly string[] BackgroundExtensions = new string[] { ".mp4", ".png", ".jpg" };
        public static readonly string[] BgmExtensions = new string[] { ".ogg", ".mp3" };

        /// <summary>背景图片/视频的推荐分辨率文案。</summary>
        public const string BackgroundResolutionTip = "图片推荐 1920×1080（16:9，png/jpg）；视频推荐 1920×1080 MP4（H.264）";

        /// <summary>背景展开动画时长（秒）。</summary>
        public const float BgRevealDuration = 1f;

        /// <summary>"只占上半屏"模式默认遮住的高度比例（0.55 = 屏幕上方 55%）。</summary>
        public const float HalfScreenRatio = 0.55f;

        /// <summary>
        /// "只占上半屏"模式下视频四周的黑边比例（相对上方区域高度，0.05 = 上下各留 5% 黑边）。
        /// </summary>
        public const float VideoHalfEdge = 0.05f;

        // ---- 图片背景的"轻微摇晃"（视频不晃）----

        /// <summary>图片比屏幕多留的余量倍数（1.06 = 多放大 6%，给摇晃留空间）。</summary>
        public const float BgSwayOverscan = 1.06f;

        /// <summary>默认摇晃幅度（uv 单位；0.012 ≈ 屏幕宽度的 1.2%）。</summary>
        public const float BgSwayAmplitude = 0.012f;

        /// <summary>默认摇晃周期（秒/个来回）。</summary>
        public const float BgSwayPeriod = 12f;
    }

    /// <summary>某个调查员的「领域」配置。</summary>
    public class DomainProfile
    {
        /// <summary>存盘用的小节名，例如 domain_001。</summary>
        public string Section = "";

        /// <summary>调查员在存档里的稳定编号（RoleLibraryKey）。</summary>
        public string RoleKey = "";

        /// <summary>调查员名字，用于显示「某某——后缀」以及按名字兜底匹配。</summary>
        public string RoleName = "";

        /// <summary>玩家自己起的后缀。</summary>
        public string Suffix = "";

        /// <summary>背景文件名（不含路径），位于 CustomBattleAssets\Background。</summary>
        public string BgFile = "";

        /// <summary>BGM 文件名（不含路径），位于 CustomBattleAssets\Bgm。</summary>
        public string BgmFile = "";

        /// <summary>BGM 是否循环。</summary>
        public bool BgmLoop = true;

        /// <summary>背景只占上半屏（下方保留原来的战斗背景，角色看起来还站在地上）。</summary>
        public bool HalfScreen = false;

        /// <summary>
        /// 挂载行动：战斗中用了这个行动就展开领域。
        /// 0 = 不挂载；1 = 具体战斗技能（MountId = 技能 id）；2 = 任意法术；3 = 具体法术（MountId = 法术 id）。
        /// </summary>
        public int MountType = 0;

        /// <summary>挂载的 id（战斗技能 id 或法术 id；"任意法术"时为 0）。</summary>
        public int MountId = 0;

        /// <summary>挂载行动的显示名（只用于界面回显和日志）。</summary>
        public string MountName = "";

        // ==================== 领域效果（2026-10-07）====================

        /// <summary>领域作用于友方：0=无，1=自身，2=所有友方（含自己）。</summary>
        public int AllyTarget = 0;

        /// <summary>领域是否作用于敌方（勾选后对所有敌人施加敌方效果）。</summary>
        public bool EnemyTarget = false;

        /// <summary>友方效果（DomainEffectPreset 常量或 BuffOffset+层 buffId）。</summary>
        public int AllyEffectType = 0;

        /// <summary>友方效果数值（挂多少层 / 加多少百分比 / 扣回多少点）。</summary>
        public int AllyEffectValue = 0;

        /// <summary>敌方效果。</summary>
        public int EnemyEffectType = 0;
        public int EnemyEffectValue = 0;

        /// <summary>支付代价：0=无，1=扣除生命值，2=扣除精神值，3=扣除魔法值。</summary>
        public int CostType = 0;

        /// <summary>支付代价数值（不可为负）。</summary>
        public int CostValue = 0;

        /// <summary>是否配过"数据相关"的内容（效果/代价），保存时用来决定要不要弹"后果自负"警告。</summary>
        public bool HasAnyEffectData
        {
            get
            {
                return AllyTarget != 0 || EnemyTarget || AllyEffectType != 0 || EnemyEffectType != 0 || CostType != 0;
            }
        }

        /// <summary>运行时分配的领域状态 buff 编号（显示用）。</summary>
        public int StatusBuffId;

        /// <summary>运行时分配的友方效果 buff 编号（隐藏）。</summary>
        public int AllyEffectBuffId;

        /// <summary>运行时分配的敌方效果 buff 编号（隐藏）。</summary>
        public int EnemyEffectBuffId;

        public bool HasBackground
        {
            get { return !string.IsNullOrEmpty(BgFile); }
        }

        public bool HasBgm
        {
            get { return !string.IsNullOrEmpty(BgmFile); }
        }

        public bool HasAnything
        {
            get { return HasBackground || HasBgm; }
        }

        public string DisplayName
        {
            get { return DomainText.BuildDisplayName(RoleName, Suffix); }
        }
    }

    /// <summary>「挂载行动」下拉框里的一项。</summary>
    public class ActionOption
    {
        public string Label = "";
        public int Type;   // 同 DomainProfile.MountType
        public int Id;

        public ActionOption(string label, int type, int id)
        {
            Label = label;
            Type = type;
            Id = id;
        }
    }

    /// <summary>领域效果的预设项（框A 前几行），以及"具体 buff"的编码方式。</summary>
    public static class DomainEffectPreset
    {
        public const int None = 0;
        public const int PhysicalDamage = 1;        // 造成物理伤害（展开时结算一次）
        public const int PhysicalDamageBonus = 2;   // 物理伤害增加%（持续）
        public const int PhysicalDamageReduce = 3;  // 物理伤害减免%（持续）
        public const int MagicDamage = 4;           // 造成法术伤害（展开时结算一次）
        public const int MagicDamageBonus = 5;      // 法术伤害增加%（持续）
        public const int MagicDamageReduce = 6;     // 法术伤害减免%（持续）
        public const int MpDrain = 7;               // 扣除魔法值（展开时结算一次）
        public const int MpRestore = 8;             // 恢复魔法值（展开时结算一次）
        public const int SanDrain = 9;              // 扣除精神值（展开时结算一次）
        public const int SanRestore = 10;           // 恢复精神值（展开时结算一次）

        // 2026-10-09 补：巴士那套"伤害弱化 / 易损"（和增伤/守护凑齐四种）
        public const int PhysicalDamageDown = 11;   // 造成伤害减少%（弱化）
        public const int PhysicalVulnerable = 12;   // 受到伤害增加%（易损）
        public const int MagicDamageDown = 13;      // 法术版弱化
        public const int MagicVulnerable = 14;      // 法术版易损

        /// <summary>大于等于这个值表示"具体 buff"（值 - 偏移 = buffId）。</summary>
        public const int BuffOffset = 100000;

        public static bool IsBuff(int type)
        {
            return type >= BuffOffset;
        }

        public static int BuffIdOf(int type)
        {
            return type - BuffOffset;
        }

        /// <summary>这个预设是不是"持续类"（挂状态、领域移除时消失）。</summary>
        public static bool IsPersistent(int type)
        {
            return type == PhysicalDamageBonus || type == PhysicalDamageReduce ||
                   type == MagicDamageBonus || type == MagicDamageReduce ||
                   type == PhysicalDamageDown || type == PhysicalVulnerable ||
                   type == MagicDamageDown || type == MagicVulnerable;
        }

        /// <summary>这个预设是不是"百分比类"（数值 = 百分数）。</summary>
        public static bool IsPercent(int type)
        {
            return IsPersistent(type);
        }

        /// <summary>不允许为负数的预设（保存时负数自动归 0）。</summary>
        public static bool MustBeNonNegative(int type)
        {
            switch (type)
            {
                case PhysicalDamage:
                case MagicDamage:
                case MpDrain:
                case MpRestore:
                case SanDrain:
                case SanRestore:
                    return true;
                default:
                    return false;
            }
        }

        public static string Name(int type)
        {
            switch (type)
            {
                case PhysicalDamage: return "造成物理伤害";
                case PhysicalDamageBonus: return "物理伤害增加（%）";
                case PhysicalDamageReduce: return "物理伤害减免（%）";
                case MagicDamage: return "造成法术伤害";
                case MagicDamageBonus: return "法术伤害增加（%）";
                case MagicDamageReduce: return "法术伤害减免（%）";
                case PhysicalDamageDown: return "物理伤害弱化（%）";
                case PhysicalVulnerable: return "物理易损（%）";
                case MagicDamageDown: return "法术伤害弱化（%）";
                case MagicVulnerable: return "法术易损（%）";
                case MpDrain: return "扣除魔法值";
                case MpRestore: return "恢复魔法值";
                case SanDrain: return "扣除精神值";
                case SanRestore: return "恢复精神值";
                default: return "（无）";
            }
        }

        /// <summary>界面上显示的完整名称（含具体 buff）。</summary>
        public static string DisplayName(int type)
        {
            if (type == None)
            {
                return "（无）";
            }
            if (IsBuff(type))
            {
                return "【状态】" + DomainText.BuffName(BuffIdOf(type));
            }
            return Name(type);
        }

        /// <summary>挂到角色身上时显示的状态名（短一点，方便状态栏显示）。</summary>
        public static string ShortName(int type)
        {
            switch (type)
            {
                case PhysicalDamageBonus: return "伤害强化";
                case PhysicalDamageDown: return "伤害弱化";
                case PhysicalDamageReduce: return "守护";
                case PhysicalVulnerable: return "易损";
                case MagicDamageBonus: return "法术强化";
                case MagicDamageDown: return "法术弱化";
                case MagicDamageReduce: return "法术守护";
                case MagicVulnerable: return "法术易损";
                case PhysicalDamage: return "领域伤害（物理）";
                case MagicDamage: return "领域伤害（法术）";
                case MpDrain: return "领域：扣除魔法值";
                case MpRestore: return "领域：恢复魔法值";
                case SanDrain: return "领域：扣除精神值";
                case SanRestore: return "领域：恢复精神值";
                default:
                    if (IsBuff(type))
                    {
                        return DomainText.BuffName(BuffIdOf(type));
                    }
                    return Name(type);
            }
        }

        /// <summary>预设项列表（框A 前面的固定几行）。</summary>
        public static readonly int[] Presets = new int[]
        {
            None,
            PhysicalDamage,
            PhysicalDamageBonus,
            PhysicalDamageReduce,
            PhysicalDamageDown,
            PhysicalVulnerable,
            MagicDamage,
            MagicDamageBonus,
            MagicDamageReduce,
            MagicDamageDown,
            MagicVulnerable,
            MpDrain,
            MpRestore,
            SanDrain,
            SanRestore
        };
    }
}
