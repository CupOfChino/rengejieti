// 运行时造「领域」相关的状态数据（照抄「自定义心」的 HeartBuffBuilder 那套）：
//   1. 领域状态 buff —— 显示用，挂在展开者身上；名字=领域显示名、图标=原版「开辟」的图标；
//   2. 友方效果 buff —— 挂给友方目标的（伤害增加/减免% 持续；造成伤害类在挂上时结算一次）；
//   3. 敌方效果 buff —— 挂给敌人的，同上。
//
// 游戏的状态表在内存里是公开列表（BaseFactory<BuffTableData>.UGC），往里塞自己 new 的
// BuffTableData、清掉缓存，游戏就能按编号取到——名字、描述、效果全都能运行时决定。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.SkillData;
using MOD;

namespace CustomBattleBg
{
    internal static class DomainBuffBuilder
    {
        internal const int KindStatus = 0;
        internal const int KindAlly = 1;
        internal const int KindEnemy = 2;

        /// <summary>确保这条配置对应的 buff 已注册；返回可用的编号（0 = 不需要/失败）。</summary>
        internal static int EnsureBuff(DomainProfile p, int kind)
        {
            if (p == null || !NeedsBuff(p, kind))
            {
                return 0;
            }
            BuffResFactory factory;
            try
            {
                factory = Singleton<ResManager>.Instance.BuffFactory;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("拿不到状态表：" + e.Message);
                return 0;
            }
            if (factory == null)
            {
                return 0;
            }

            int id = GetBuffId(p, kind);
            if (id < DomainConstants.CustomBuffIdMin || id > DomainConstants.CustomBuffIdMax)
            {
                id = CustomBattleBgPlugin.Store.AllocateBuffId();
                SetBuffId(p, kind, id);
            }
            if (id == 0)
            {
                return 0;
            }

            BuffTableData existing = factory.GetCache(id.ToString());
            if (existing != null && !IsOurs(existing))
            {
                // 编号被别人占了，换一个
                id = CustomBattleBgPlugin.Store.AllocateBuffId();
                if (id == 0)
                {
                    return 0;
                }
                SetBuffId(p, kind, id);
                existing = null;
            }
            if (existing != null)
            {
                return id;   // 已经注册过
            }

            factory.UGC.Add(Build(p, kind, id));
            ClearCache(factory);
            CustomBattleBgPlugin.LogInfo("已生成领域状态（" + KindName(kind) + "）：" +
                p.DisplayName + "（编号 " + id + "）");
            return id;
        }

        /// <summary>这条配置的这一类 buff 需不需要（没内容就不生成）。</summary>
        internal static bool NeedsBuff(DomainProfile p, int kind)
        {
            if (p == null)
            {
                return false;
            }
            switch (kind)
            {
                case KindStatus:
                    return true;   // 状态一直要有（显示"领域展开"）
                case KindAlly:
                    return p.AllyTarget != 0 && NeedsEffectBuff(p.AllyEffectType, p.AllyEffectValue);
                case KindEnemy:
                    return p.EnemyTarget && NeedsEffectBuff(p.EnemyEffectType, p.EnemyEffectValue);
                default:
                    return false;
            }
        }

        // 只有"百分比类（持续）"和"造成伤害"需要走 buff；资源变化在插件里直接结算
        private static bool NeedsEffectBuff(int type, int value)
        {
            if (type == DomainEffectPreset.None || value == 0)
            {
                return false;
            }
            return DomainEffectPreset.IsPersistent(type) ||
                   type == DomainEffectPreset.PhysicalDamage ||
                   type == DomainEffectPreset.MagicDamage;
        }

        private static int GetBuffId(DomainProfile p, int kind)
        {
            switch (kind)
            {
                case KindStatus: return p.StatusBuffId;
                case KindAlly: return p.AllyEffectBuffId;
                case KindEnemy: return p.EnemyEffectBuffId;
                default: return 0;
            }
        }

        private static void SetBuffId(DomainProfile p, int kind, int id)
        {
            switch (kind)
            {
                case KindStatus: p.StatusBuffId = id; break;
                case KindAlly: p.AllyEffectBuffId = id; break;
                case KindEnemy: p.EnemyEffectBuffId = id; break;
            }
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case KindStatus: return "领域状态";
                case KindAlly: return "友方效果";
                case KindEnemy: return "敌方效果";
                default: return "?";
            }
        }

        /// <summary>配置改过以后按同一个编号重建这几条 buff。</summary>
        internal static void Rebuild(DomainProfile p)
        {
            try
            {
                if (p == null)
                {
                    return;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return;
                }
                Unregister(p);
                EnsureBuff(p, KindStatus);
                EnsureBuff(p, KindAlly);
                EnsureBuff(p, KindEnemy);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("重建领域状态失败：" + e);
            }
        }

        /// <summary>把运行时塞进状态表的那几条拿掉（删配置/移除特质时用）。</summary>
        internal static void Unregister(DomainProfile p)
        {
            try
            {
                if (p == null)
                {
                    return;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return;
                }
                int removed = 0;
                removed += RemoveById(factory, p.StatusBuffId);
                removed += RemoveById(factory, p.AllyEffectBuffId);
                removed += RemoveById(factory, p.EnemyEffectBuffId);
                if (removed > 0)
                {
                    ClearCache(factory);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("移除领域状态失败：" + e.Message);
            }
        }

        private static int RemoveById(BuffResFactory factory, int id)
        {
            if (id <= 0)
            {
                return 0;
            }
            return factory.UGC.RemoveAll(o => o.Id == id && IsOurs(o));
        }

        private static bool IsOurs(BuffTableData cfg)
        {
            return cfg != null && cfg.Comment != null && cfg.Comment.StartsWith(DomainConstants.CommentTag);
        }

        /// <summary>
        /// 这个状态会不会"随回合衰减"——判断依据：数据里有没有"改自己层数"的选项（Buff_BuffLayerOption）。
        /// 中毒/流血/燃烧这类每回合掉层的都有；开辟那种常驻的没有。
        /// 用于把"用户手选的游戏 buff"自动分流：衰减的每轮施加、不衰减的展开时挂一次。
        /// </summary>
        internal static bool IsDecayingBuff(int buffId)
        {
            try
            {
                if (buffId <= 0 || !Singleton<ResManager>.HasInstance)
                {
                    return false;
                }
                BuffTableData cfg = Singleton<ResManager>.Instance.BuffFactory.GetConfig(buffId);
                if (cfg == null || cfg.Events == null)
                {
                    return false;
                }
                for (int i = 0; i < cfg.Events.Count; i++)
                {
                    BuffEventData ev = cfg.Events[i];
                    if (ev == null || ev.Funcs == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < ev.Funcs.Count; j++)
                    {
                        if (ev.Funcs[j] is Buff_BuffLayerOption)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// 后台预加载全部状态数据（游戏本体的 buff 是"按需加载"的，第一次要用到才读文件）。
        /// 分批跑（每帧几个），这样打开编辑器时状态列表已经就绪，不用等 2 秒。
        /// </summary>
        internal static IEnumerator PreloadAllBuffsCo()
        {
            BuffResFactory factory = null;
            try
            {
                factory = Singleton<ResManager>.Instance.BuffFactory;
            }
            catch (Exception)
            {
            }
            if (factory == null)
            {
                yield break;
            }
            List<ResloadFileData> pending = factory.LoadFileDatas;
            if (pending == null || pending.Count == 0)
            {
                yield break;
            }

            long t0 = DateTime.Now.Ticks;
            int loaded = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                ResloadFileData item = pending[i];
                if (item != null && !string.IsNullOrEmpty(item.FileName))
                {
                    try
                    {
                        if (factory.GetConfig(item.FileName) != null)
                        {
                            loaded++;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                if ((i & 3) == 3)
                {
                    yield return null;   // 每 4 个让一帧，避免卡顿
                }
            }
            long ms = (DateTime.Now.Ticks - t0) / TimeSpan.TicksPerMillisecond;
            CustomBattleBgPlugin.LogInfo("状态表后台预加载完成：共 " + pending.Count + " 个文件、读取 " +
                loaded + " 条，耗时 " + ms + " 毫秒");
        }

        private static void ClearCache(BuffResFactory factory)
        {
            try
            {
                FieldInfo f = typeof(BaseFactory<BuffTableData>).GetField("_cacheList",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null)
                {
                    return;
                }
                IList list = f.GetValue(factory) as IList;
                if (list != null)
                {
                    list.Clear();
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("清状态表缓存失败：" + e.Message);
            }
        }

        // ---------------- 构建 ----------------

        private static BuffTableData NewBase(int id, string name, string des, bool showUI, bool negative,
            string iconKey)
        {
            BuffTableData cfg = new BuffTableData();
            cfg.Id = id;
            cfg.Name = new LocalizationBuffKeyData();
            cfg.Name.TarKey = "";
            cfg.Name.SheetKey = "";
            cfg.Name.InputText = name;
            cfg.Des = new LocalizationBuffKeyData();
            cfg.Des.TarKey = "";
            cfg.Des.SheetKey = "";
            // 负面状态的描述用红色，正面/中性保持默认色（用户口径；游戏自己的描述也走富文本）
            cfg.Des.InputText = Colorize(des, negative);
            cfg.IconPathReference = new TextureResourceReference();
            cfg.IconPathReference.ReferenceType = ETextureReferenceType.BuffIcon;
            cfg.IconPathReference.Key = iconKey;
            cfg.Comment = DomainConstants.CommentTag + name;
            cfg.BuffType = EBuffType.Other;
            cfg.BuffEffectType = negative ? EBuffEffectType.Negative : EBuffEffectType.Positive;
            // 按层显示：层数 = 数值/10（每层 10%），图标角标能直接看到档位；
            // MaxLayer 必须给上限，否则角标会显示成"∞"
            cfg.OverlayType = EBuffOverlyingType.OverlyLayer;
            cfg.LayerCount = new MODValue();
            cfg.LayerCount.Value = "1";
            cfg.OverrideInitLayer = true;
            cfg.InitLayer = 1;
            cfg.MaxLayer = 999;
            cfg.FxPlayType = EBuffFXPlayType.None;
            cfg.UseFxPrefab = new ItemFxInfoData();
            cfg.UseFxPrefab.FxRes = new PrefabResoureReference();
            cfg.PlayFxInBattle = false;
            cfg.PlayFxInExplore = false;
            cfg.IsDeathClear = true;
            cfg.IsShowUI = showUI;
            cfg.Arrts = new List<ChangeAttrData>();
            cfg.Events = new List<BuffEventData>();
            return cfg;
        }

        private static string Colorize(string text, bool negative)
        {
            if (string.IsNullOrEmpty(text) || !negative)
            {
                return text;
            }
            return "<color=#FF5555>" + text + "</color>";
        }

        /// <summary>这个效果对"受益人"来说是正面的还是负面的（决定描述颜色和状态正负）。</summary>
        internal static bool IsNegativeEffect(int type)
        {
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamage:
                case DomainEffectPreset.MagicDamage:
                case DomainEffectPreset.MpDrain:
                case DomainEffectPreset.SanDrain:
                case DomainEffectPreset.PhysicalDamageDown:
                case DomainEffectPreset.PhysicalVulnerable:
                case DomainEffectPreset.MagicDamageDown:
                case DomainEffectPreset.MagicVulnerable:
                    return true;
            }
            if (DomainEffectPreset.IsBuff(type))
            {
                try
                {
                    if (Singleton<ResManager>.HasInstance)
                    {
                        BuffTableData cfg = Singleton<ResManager>.Instance.BuffFactory.GetConfig(
                            DomainEffectPreset.BuffIdOf(type));
                        if (cfg != null)
                        {
                            return cfg.BuffEffectType == EBuffEffectType.Negative;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        private static BuffTableData Build(DomainProfile p, int kind, int id)
        {
            if (kind == KindStatus)
            {
                // 领域状态：图标用原版「开辟」（Buff 436）的 icon_buff_zhenli
                return NewBase(id, p.DisplayName, BuildSummary(p), true, false,
                    DomainConstants.DomainStatusIconKey);
            }
            bool ally = kind == KindAlly;
            int type = ally ? p.AllyEffectType : p.EnemyEffectType;
            int value = ally ? p.AllyEffectValue : p.EnemyEffectValue;
            bool showUI = !IsDirectDamage(type);   // 每回合造成伤害那两条不显示图标（伤害是领域带来的）
            BuffTableData cfg = NewBase(id,
                DomainEffectPreset.ShortName(type),
                BuildSummary(p), showUI, IsNegativeEffect(type), IconKeyOf(type));
            AddPercentEffect(cfg, type, value);
            AddInstantDamage(cfg, type, value);
            AddRoundEndSelfRemove(cfg);   // 效果 buff 每轮施加、回合结束自动解除（巴士那套节奏）
            return cfg;
        }

        private static bool IsDirectDamage(int type)
        {
            return type == DomainEffectPreset.PhysicalDamage || type == DomainEffectPreset.MagicDamage;
        }

        /// <summary>预设效果对应的图标 key（导入进 Buff 目录的那批）。</summary>
        internal static string IconKeyOf(int type)
        {
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamageBonus: return "icon_domain_phys_up";
                case DomainEffectPreset.PhysicalDamageDown: return "icon_domain_phys_down";
                case DomainEffectPreset.PhysicalDamageReduce: return "icon_domain_phys_guard";
                case DomainEffectPreset.PhysicalVulnerable: return "icon_domain_phys_vuln";
                case DomainEffectPreset.MagicDamageBonus: return "icon_domain_magic_up";
                case DomainEffectPreset.MagicDamageDown: return "icon_domain_magic_down";
                case DomainEffectPreset.MagicDamageReduce: return "icon_domain_magic_guard";
                case DomainEffectPreset.MagicVulnerable: return "icon_domain_magic_vuln";
                default: return DomainConstants.DomainStatusIconKey;
            }
        }

        // 回合结束时把自己摘掉（配合"每轮开始施加"= 巴士的回合制 buff 节奏）
        private static void AddRoundEndSelfRemove(BuffTableData cfg)
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.RoundEnd;
            Buff_BuffChangeOption remove = new Buff_BuffChangeOption();
            remove.ChangeType = EAddChangeMode.Remove;
            remove.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            remove.IsSelf = true;
            ev.Funcs.Add(remove);
            cfg.Events.Add(ev);
        }

        // 百分比增减伤：走 Buff_ChangeAddOrReducePercentOption（按 SourceKey 追踪，摘状态时正常退回）
        private static void AddPercentEffect(BuffTableData cfg, int type, int value)
        {
            if (!DomainEffectPreset.IsPersistent(type) || value == 0)
            {
                return;
            }
            bool isBonus;
            EDamageType damageType;
            int percent = value;
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamageBonus:
                    isBonus = true;
                    damageType = EDamageType.Ordinary;
                    break;
                case DomainEffectPreset.PhysicalDamageDown:
                    isBonus = true;                     // 造成伤害 -X%
                    damageType = EDamageType.Ordinary;
                    percent = -value;
                    break;
                case DomainEffectPreset.PhysicalDamageReduce:
                    isBonus = false;                    // 受到伤害 -X%
                    damageType = EDamageType.Ordinary;
                    percent = -value;
                    break;
                case DomainEffectPreset.PhysicalVulnerable:
                    isBonus = false;                    // 受到伤害 +X%
                    damageType = EDamageType.Ordinary;
                    break;
                case DomainEffectPreset.MagicDamageBonus:
                    isBonus = true;
                    damageType = EDamageType.Magic;
                    break;
                case DomainEffectPreset.MagicDamageDown:
                    isBonus = true;
                    damageType = EDamageType.Magic;
                    percent = -value;
                    break;
                case DomainEffectPreset.MagicDamageReduce:
                    isBonus = false;
                    damageType = EDamageType.Magic;
                    percent = -value;
                    break;
                case DomainEffectPreset.MagicVulnerable:
                    isBonus = false;
                    damageType = EDamageType.Magic;
                    break;
                default:
                    return;
            }
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.Stable;

            Buff_ChangeAddOrReducePercentOption opt = new Buff_ChangeAddOrReducePercentOption();
            opt.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            opt.ChangeType = isBonus
                ? Buff_ChangeAddOrReducePercentOption.EHitType.CauseDamage
                : Buff_ChangeAddOrReducePercentOption.EHitType.BeDamaged;
            opt.IsRemove = false;
            opt.ChangeByValue = false;
            opt.Percent = percent / 100f;
            opt.FloorValue = 0;
            opt.DamageTypes = new List<EDamageType>();
            opt.DamageTypes.Add(damageType);
            ev.Funcs.Add(opt);
            cfg.Events.Add(ev);
        }

        // 造成伤害：挂上状态的那一刻（Stable）结算一次，之后不再触发
        private static void AddInstantDamage(BuffTableData cfg, int type, int value)
        {
            if (value == 0)
            {
                return;
            }
            EDamageType damageType;
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamage:
                    damageType = EDamageType.Ordinary;
                    break;
                case DomainEffectPreset.MagicDamage:
                    damageType = EDamageType.Magic;
                    break;
                default:
                    return;
            }

            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.Stable;

            Buff_DamageOption opt = new Buff_DamageOption();
            opt.IsDirectDamage = false;
            opt.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            DamageData dmg = new DamageData();
            dmg.DamageType = damageType;
            dmg.Value = value.ToString();
            dmg.DB_Bonus = 0;
            dmg.WithSuckBlood = false;
            dmg.SuckBloodRatio = 0;
            dmg.UseCommonEffect = false;
            dmg.CommonEffectId = 1;
            dmg.IsDirectDeath = false;
            opt.Damage = dmg;
            opt.ReplaceDamageByRecoverValue = false;
            opt.UseBuffLayer = false;
            opt.HitToSevereWound = false;
            opt.HitToDeath = false;
            opt.IsUnblock = true;
            ev.Funcs.Add(opt);
            cfg.Events.Add(ev);
        }

        /// <summary>把配置的效果/代价拼成给人的说明文字（挂在状态 buff 的描述里）。</summary>
        internal static string BuildSummary(DomainProfile p)
        {
            List<string> lines = new List<string>();
            if (p == null)
            {
                return "";
            }
            if (p.AllyTarget != 0 && p.AllyEffectType != 0)
            {
                string who = p.AllyTarget == 1 ? "自身" : "所有友方";
                lines.Add("友方（" + who + "）：" + DescribeEffect(p.AllyEffectType, p.AllyEffectValue));
            }
            if (p.EnemyTarget && p.EnemyEffectType != 0)
            {
                lines.Add("敌方：" + DescribeEffect(p.EnemyEffectType, p.EnemyEffectValue));
            }
            if (p.CostType != 0 && p.CostValue > 0)
            {
                lines.Add("代价（每轮开始与展开时）：" + DescribeCost(p.CostType, p.CostValue));
            }
            if (lines.Count == 0)
            {
                lines.Add("（没有配置领域效果）");
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string DescribeEffect(int type, int value)
        {
            if (DomainEffectPreset.IsBuff(type))
            {
                return "每轮开始施加【" + DomainText.BuffName(DomainEffectPreset.BuffIdOf(type)) + "】 " +
                       value + " 层";
            }
            string name = DomainEffectPreset.Name(type);
            if (DomainEffectPreset.IsPercent(type))
            {
                return name + " " + (value >= 0 ? "+" : "") + value + "%";
            }
            return name + " " + value;
        }

        private static string DescribeCost(int costType, int value)
        {
            switch (costType)
            {
                case 1: return "扣除生命值 " + value;
                case 2: return "扣除精神值 " + value;
                case 3: return "扣除魔法值 " + value;
                default: return "（无）";
            }
        }
    }
}
