// 运行时造一颗「心」的状态数据。
//
// 游戏的 buff 表在内存里就是个公开列表（BaseFactory<BuffTableData>.UGC），
// 往里塞一条自己 new 出来的 BuffTableData，再清掉它的缓存，游戏就能按编号取到，
// 于是名字、数值全都能在运行时决定，不需要预先写死成 txt。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.SkillData;
using GamePlayEvent;
using MOD;

namespace XinEditor
{
    internal static class HeartBuffBuilder
    {
        // 确保这颗心在游戏状态表里存在；返回可用的 buff 编号，0 表示失败
        internal static int EnsureRegistered(HeartDefinition def)
        {
            if (def == null)
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
                XinEditorPlugin.LogError("拿不到状态表：" + e.Message);
                return 0;
            }
            if (factory == null)
            {
                return 0;
            }

            if (def.BuffId < HeartConstants.CustomBuffIdMin || def.BuffId > HeartConstants.CustomBuffIdMax)
            {
                def.BuffId = XinEditorPlugin.Store.AllocateBuffId();
            }
            if (def.BuffId == 0)
            {
                return 0;
            }

            BuffTableData existing = factory.GetCache(def.BuffId.ToString());
            if (existing != null)
            {
                if (IsOurs(existing))
                {
                    return def.BuffId;
                }
                // 编号被别人占了，换一个
                int newId = XinEditorPlugin.Store.AllocateBuffId();
                if (newId == 0)
                {
                    return 0;
                }
                def.BuffId = newId;
            }

            factory.UGC.Add(Build(def));
            ClearCache(factory);
            XinEditorPlugin.LogInfo("已生成自定义心状态：" + def.DisplayName + "（编号 " + def.BuffId + "）");
            return def.BuffId;
        }

        private static bool IsOurs(BuffTableData cfg)
        {
            return cfg != null && cfg.Comment != null && cfg.Comment.StartsWith(HeartConstants.CommentTag);
        }

        // 数值改过以后按同一个编号重建这条状态
        internal static void Rebuild(HeartDefinition def)
        {
            try
            {
                if (def == null || def.BuffId <= 0)
                {
                    return;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return;
                }
                Unregister(def);
                factory.UGC.Add(Build(def));
                ClearCache(factory);
                XinEditorPlugin.LogInfo("已更新自定义心状态：" + def.DisplayName + "（编号 " + def.BuffId + "）");
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("重建状态失败：" + e);
            }
        }

        // 这颗心被删了：把运行时塞进状态表的那条拿掉
        internal static void Unregister(HeartDefinition def)
        {
            try
            {
                if (def == null || def.BuffId <= 0)
                {
                    return;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return;
                }
                int removed = factory.UGC.RemoveAll(o => o.Id == def.BuffId && IsOurs(o));
                if (removed > 0)
                {
                    ClearCache(factory);
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("移除状态失败：" + e.Message);
            }
        }

        // 往 buff 表里塞了新东西之后要清它自己的缓存，否则按类型枚举的地方看不到
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
                XinEditorPlugin.LogError("清状态表缓存失败：" + e.Message);
            }
        }

        // 按一颗心的配置造出完整状态数据，结构照抄数据包里的默认心
        private static BuffTableData Build(HeartDefinition def)
        {
            BuffTableData cfg = new BuffTableData();
            cfg.Id = def.BuffId;
            cfg.Name = new LocalizationBuffKeyData();
            cfg.Name.TarKey = "";
            cfg.Name.SheetKey = "";
            cfg.Name.InputText = def.DisplayName;
            cfg.Des = new LocalizationBuffKeyData();
            cfg.Des.TarKey = "";
            cfg.Des.SheetKey = "";
            cfg.Des.InputText = def.BuildDescription();
            cfg.IconPathReference = new TextureResourceReference();
            cfg.IconPathReference.ReferenceType = ETextureReferenceType.BuffIcon;
            cfg.IconPathReference.Key = HeartConstants.HeartIconKey;
            cfg.Comment = HeartConstants.CommentTag + def.DisplayName;
            cfg.BuffType = EBuffType.Other;
            cfg.BuffEffectType = EBuffEffectType.Positive;
            cfg.OverlayType = EBuffOverlyingType.None;
            cfg.FxPlayType = EBuffFXPlayType.Stable;
            cfg.UseFxPrefab = new ItemFxInfoData();
            cfg.UseFxPrefab.FxRes = new PrefabResoureReference();
            // 用数据包里那份帧动画特效（Game/ExtraAnim/xin_shine.txt）：
            // 帧序、循环、大小都在那份配置里调，这里只负责指过去
            cfg.UseFxPrefab.UseFrameEffect = true;
            cfg.UseFxPrefab.EffectName = XinEditorPlugin.FrameEffectName;
            cfg.UseFxPrefab.EffectAnimName = XinEditorPlugin.FrameEffectAnim;
            cfg.UseFxPrefab.PlayPoint = (ERolePointType)XinEditorPlugin.FrameEffectPoint;
            cfg.PlayFxInBattle = true;
            cfg.PlayFxInExplore = false;
            cfg.IsDeathClear = true;
            cfg.IsShowUI = true;
            cfg.Arrts = new List<ChangeAttrData>();
            cfg.Events = new List<BuffEventData>();

            if (def.Speed != 0)
            {
                cfg.Arrts.Add(ExAttr(ERoleExtraAttribute.Speed, def.Speed));
            }

            int reduce = 0;
            int physReducePercent = 0;
            int magicReducePercent = 0;
            int physBonusPercent = 0;
            int magicBonusPercent = 0;
            for (int i = 0; i < def.Stats.Count; i++)
            {
                HeartStat st = def.Stats[i];
                if (st.Value == 0)
                {
                    continue;
                }
                switch (st.Type)
                {
                    case HeartStatType.DamageBonus:
                        cfg.Arrts.Add(ExAttr(ERoleExtraAttribute.AddDamage, st.Value));
                        break;
                    case HeartStatType.DamageReduce:
                        reduce += st.Value;
                        break;
                    case HeartStatType.Dodge:
                        cfg.Arrts.Add(ExAttr(ERoleExtraAttribute.Dodge, st.Value));
                        break;
                    case HeartStatType.Willpower:
                        cfg.Arrts.Add(HeroAttr(EHeroAttribute.POW, st.Value));
                        break;
                    case HeartStatType.Occultism:
                        cfg.Arrts.Add(SkillAttr(EExploreSkill.Occultism, st.Value));
                        break;
                    case HeartStatType.Brawl:
                        cfg.Arrts.Add(SkillAttr(EExploreSkill.Combat, st.Value));
                        break;
                    case HeartStatType.Shooting:
                        cfg.Arrts.Add(SkillAttr(EExploreSkill.LongRange, st.Value));
                        break;
                    case HeartStatType.Athletics:
                        cfg.Arrts.Add(SkillAttr(EExploreSkill.Motion, st.Value));
                        break;
                    case HeartStatType.PhysicalDamageReducePercent:
                        physReducePercent += st.Value;
                        break;
                    case HeartStatType.MagicDamageReducePercent:
                        magicReducePercent += st.Value;
                        break;
                    case HeartStatType.PhysicalDamageBonusPercent:
                        physBonusPercent += st.Value;
                        break;
                    case HeartStatType.MagicDamageBonusPercent:
                        magicBonusPercent += st.Value;
                        break;
                }
            }

            if (reduce > 0)
            {
                cfg.Events.Add(MakeDamageReduceEvent(reduce));
            }
            // 百分比类：走 Buff_ChangeAddOrReducePercentOption（按 SourceKey 追踪，摘状态时正常退回）
            if (physReducePercent > 0)
            {
                cfg.Events.Add(MakePercentDamageEvent(isBonus: false, EDamageType.Ordinary, physReducePercent));
            }
            if (magicReducePercent > 0)
            {
                cfg.Events.Add(MakePercentDamageEvent(isBonus: false, EDamageType.Magic, magicReducePercent));
            }
            if (physBonusPercent > 0)
            {
                cfg.Events.Add(MakePercentDamageEvent(isBonus: true, EDamageType.Ordinary, physBonusPercent));
            }
            if (magicBonusPercent > 0)
            {
                cfg.Events.Add(MakePercentDamageEvent(isBonus: true, EDamageType.Magic, magicBonusPercent));
            }
            // 回合开始：精神值正常才扣 1 点维持；处于衰弱/衰竭则解除
            cfg.Events.Add(MakeSanCostEvent());
            cfg.Events.Add(MakeSanBreakRoundStartEvent());
            // 战斗中精神值刚掉进衰弱/衰竭的那一刻，立刻解除
            cfg.Events.Add(MakeSanBreakEvent(ESanState.Weak));
            cfg.Events.Add(MakeSanBreakEvent(ESanState.Collapse));
            // 重伤（血量见底）或倒下时解除
            cfg.Events.Add(MakeLifeStateClearEvent());
            cfg.Events.Add(MakeBattleEndEvent());
            return cfg;
        }

        private static ChangeAttrData ExAttr(ERoleExtraAttribute type, int value)
        {
            ChangeAttrData d = new ChangeAttrData();
            // 必须是 true！false 时游戏会把 sourceKey 清成空串，
            // 而 VariableData.Add("", v) 会走"直接加进 _baseValue、不记来源"的分支，
            // 摘状态时 Remove("") 又找不到来源 → 每挂一次就永久叠一层（踩过大坑）。
            d.IsTrackSource = true;
            d.RoleExAttr = new RoleParamVariableData<ERoleExtraAttribute>();
            d.RoleExAttr.Type = type;
            d.RoleExAttr.Value = value.ToString(CultureInfo.InvariantCulture);
            return d;
        }

        private static ChangeAttrData HeroAttr(EHeroAttribute type, int value)
        {
            ChangeAttrData d = new ChangeAttrData();
            d.IsTrackSource = true;   // 同上：false 会导致数值永久叠加
            d.RoleAttr = new RoleParamVariableData<EHeroAttribute>();
            d.RoleAttr.Type = type;
            d.RoleAttr.Value = value.ToString(CultureInfo.InvariantCulture);
            return d;
        }

        private static ChangeAttrData SkillAttr(EExploreSkill type, int value)
        {
            ChangeAttrData d = new ChangeAttrData();
            d.IsTrackSource = true;   // 同上：false 会导致数值永久叠加
            d.RoleSkill = new RoleParamVariableData<EExploreSkill>();
            d.RoleSkill.Type = type;
            d.RoleSkill.Value = value.ToString(CultureInfo.InvariantCulture);
            return d;
        }

        // 受到伤害减免：创建时挂上减免数据（三类伤害各减 N）
        private static BuffEventData MakeDamageReduceEvent(int reduce)
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.Stable;

            Buff_ImmuneDamageOption opt = new Buff_ImmuneDamageOption();
            opt.Data = new ImmunDamageData();
            opt.Data.IsOpen = true;
            opt.Data.ImmuMaxDamage = false;
            opt.Data.ImmuFall = false;
            opt.Data.OnlyImmuEnemyDamage = false;
            opt.Data.ReductionList = new List<DamageReductionData>();
            opt.Data.ReductionList.Add(MakeReduction(EDamageType.Ordinary, reduce));
            opt.Data.ReductionList.Add(MakeReduction(EDamageType.Explosion, reduce));
            opt.Data.ReductionList.Add(MakeReduction(EDamageType.Magic, reduce));
            ev.Funcs.Add(opt);
            return ev;
        }

        private static DamageReductionData MakeReduction(EDamageType type, int value)
        {
            DamageReductionData d = new DamageReductionData();
            d.Type = type;
            d.ReduceValue = value;
            return d;
        }

        // 百分比增减伤：isBonus=true 是"造成伤害加成"，false 是"受到伤害减少"
        // 物理伤害只算普通伤害（游戏里伤害类型只有 普通/爆炸/法术 三种）
        private static BuffEventData MakePercentDamageEvent(bool isBonus, EDamageType damageType, int percent)
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.Stable;

            Buff_ChangeAddOrReducePercentOption opt = new Buff_ChangeAddOrReducePercentOption();
            opt.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            opt.ChangeType = isBonus
                ? Buff_ChangeAddOrReducePercentOption.EHitType.CauseDamage
                : Buff_ChangeAddOrReducePercentOption.EHitType.BeDamaged;
            opt.IsRemove = false;
            opt.ChangeByValue = false;              // false＝按比例
            opt.Percent = percent / 100f;           // 50 → 0.5
            opt.FloorValue = 0;
            opt.DamageTypes = new List<EDamageType>();
            opt.DamageTypes.Add(damageType);
            ev.Funcs.Add(opt);
            return ev;
        }

        // 每回合开始前消耗 1 点精神值（和默认心一致）
        // 只在自己精神状态正常时扣：衰弱/衰竭交给下面那条事件去解除
        private static BuffEventData MakeSanCostEvent()
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.BeforeRoundStart;
            ev.SatisfyAny = false;   // 两个条件都要满足：既不衰弱、也不衰竭
            ev.Checks.Add(MakeSanStateCheck(ESanState.Weak, true));
            ev.Checks.Add(MakeSanStateCheck(ESanState.Collapse, true));

            Buff_RoleAttrOption opt = new Buff_RoleAttrOption();
            opt.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            opt.Datas = new List<ChangeAttrData>();
            opt.Datas.Add(ExAttr(ERoleExtraAttribute.CurrentSan, -1));
            opt.CannnotRecoverOnRemove = true;
            opt.IsRemove = false;
            ev.Funcs.Add(opt);
            return ev;
        }

        // 回合开始时的兜底复检：进战斗那一刻就已经是衰弱/衰竭的，SanStateChange 不会再触发，
        // 只能靠这里在第一个回合开始时把它解掉
        private static BuffEventData MakeSanBreakRoundStartEvent()
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.BeforeRoundStart;
            ev.SatisfyAny = true;    // 衰弱或衰竭，任一即解除
            ev.Checks.Add(MakeSanStateCheck(ESanState.Weak, false));
            ev.Checks.Add(MakeSanStateCheck(ESanState.Collapse, false));

            AddHealAndRemoveSelf(ev);
            return ev;
        }

        // 精神值陷入衰弱/衰竭时：补 5 点精神值并解除自己
        private static BuffEventData MakeSanBreakEvent(ESanState state)
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.SanStateChange;

            ev.Checks.Add(MakeSanStateCheck(state, false));

            AddHealAndRemoveSelf(ev);
            return ev;
        }

        // 战斗中的倒下：解除【心】（不补精神值，回血留给游戏自己的结算）
        // 注意：重伤（SevereWound，血量见底锁 1 血）不解除——只要精神值没垮，心就还在
        private static BuffEventData MakeLifeStateClearEvent()
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.LifeStateChangeBeforeFIghtOverCheck;
            ev.SatisfyAny = false;   // 只有一个条件：倒下
            ev.Checks.Add(MakeLifeStateCheck(ELifeState.Death));

            AddRemoveSelf(ev);
            return ev;
        }

        private static Buff_SelfSanStateCheck MakeSanStateCheck(ESanState state, bool not)
        {
            Buff_SelfSanStateCheck chk = new Buff_SelfSanStateCheck();
            chk.SanState = state;
            chk.Not = not;
            return chk;
        }

        private static Buff_SelfLifeStateCheck MakeLifeStateCheck(ELifeState state)
        {
            Buff_SelfLifeStateCheck chk = new Buff_SelfLifeStateCheck();
            chk.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            chk.LifeState = state;
            chk.SatisfyAny = false;
            chk.Not = false;
            return chk;
        }

        // 战斗结束时：补 5 点精神值并解除自己
        private static BuffEventData MakeBattleEndEvent()
        {
            BuffEventData ev = new BuffEventData();
            ev.EBuffTrigger = EBuffTriggerType.BattleEnd;
            AddHealAndRemoveSelf(ev);
            return ev;
        }

        private static void AddHealAndRemoveSelf(BuffEventData ev)
        {
            Buff_RoleAttrOption heal = new Buff_RoleAttrOption();
            heal.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            heal.Datas = new List<ChangeAttrData>();
            heal.Datas.Add(ExAttr(ERoleExtraAttribute.CurrentSan, 5));
            heal.CannnotRecoverOnRemove = true;
            heal.IsRemove = false;
            ev.Funcs.Add(heal);

            AddRemoveSelf(ev);
        }

        private static void AddRemoveSelf(BuffEventData ev)
        {
            Buff_BuffChangeOption remove = new Buff_BuffChangeOption();
            remove.ChangeType = EAddChangeMode.Remove;
            remove.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;
            remove.IsSelf = true;
            ev.Funcs.Add(remove);
        }
    }
}
