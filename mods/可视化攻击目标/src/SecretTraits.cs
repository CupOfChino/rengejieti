// 「私货」特质的插件部分（百合花 / 白毛少女 / 灰暗孤影）。
//
// 数据侧：特质 880021~880023、状态 880024 / 880026~880029（见 Project_Depersonal）。
// 施工备忘在 temp\私货特质_进度备忘.md（不进 git、不上传）。
//
// 为什么这几条必须写插件：
//   · 百合花：魅惑时长 ×2（改这条 buff 的 EffectRounds）、
//             敌方对持有者的魅惑行动 +1 奖励骰、对抗 +1 惩罚骰（改骰子）
//   · 白毛少女：战斗开始给每个敌人做意志检定 / 无理智的叠弱点暴露（要批量跑 + 掷骰 + 出动画）
//   · 灰暗孤影：近战武器攻击整套再来一遍、独行条件数值、每 2 回合 1 次法术免伤、每模组 1 次免死
//
// 两个容易踩的点（都在这里绕开了）：
//   1. 游戏里大量方法是 async Task。Harmony 的 **Postfix 会在方法"第一次 await"处就跑**，
//      不是等方法跑完。所以凡是"改完还要被后面的代码用到"的，都得挑同步方法，或者写成 Prefix。
//   2. "追加一次攻击"没有用游戏自带的追击：追击只是**一次命中结算**，而且被「追击次数」属性
//      卡成每行动 1 次（二连会变成 2+1=3 下）。这里改成把「连击段数」GetSkillEffectCount() ×2，
//      于是整套行动原样再跑一遍（二连→4 段、横扫→两遍），每段都是游戏自己掷骰自己结算。

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using Game.FixedSkill;
using Game.Role.AI;
using Game.SkillData;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace AttackTargetVisualizer
{
    /// <summary>私货特质的编号常量。</summary>
    internal static class SecretIds
    {
        internal const int LilyTrait = 880021;          // 特质：百合花
        internal const int WhiteHairTrait = 880022;     // 特质：白毛少女
        internal const int LoneShadowTrait = 880023;    // 特质：灰暗孤影
        internal const int MemoryTrait = 880030;        // 特质：记忆的双剑

        internal const int LilyEquip = 880002;          // 装备：百合花（免死 / 法术无效的载体，2026-09-22 从灰暗孤影移过来）
        internal const int LilyChain = 880003;          // 装备：百合花链（每回合吸血 + 叠【百合花芳香】）
        internal const int LilyRing = 880004;           // 装备：百合花戒指（回魔法值 / 施法后追加伤害 / 习得枯萎术）
        internal const int LilyWreath = 880005;         // 装备：百合花环（护具槽；免疫四种状态 + 每回合解一个负面 = 叠【花香】）
        internal const int LilyAromaBuff = 880048;      // 状态：百合花芳香（敌人身上的层数 debuff）
        internal const int FlowerScentBuff = 880049;    // 状态：花香（每层意志检定 +5，2026-09-27 改）

        internal const int LilyBuff = 880024;           // 状态：百合花（纯显示）
        internal const int LoneShadowBuff = 880026;     // 状态：灰暗孤影（纯标记）
        internal const int LoneBonusHalfBuff = 880047;  // 隐藏状态：独行加成·半额（1 名队友在场时用）
        internal const int UndyingBuff = 880027;        // 状态：百合未谢（免死标记 + 台词 / 半血 / 免伤）

        /// <summary>茉莉身上不要的疯狂特质（2026-09-25 用户要求移除）：66「逃避」。</summary>
        internal const int AvoidCrazyTrait = 66;
        internal const int LoneBonusBuff = 880028;      // 隐藏状态：独行加成（闪避/速度/物理伤害）
        internal const int DamageImmuneBuff = 880029;   // 隐藏状态：1 回合伤害免疫
        internal const int MemoryBuff = 880031;         // 状态：记忆的双剑（纯显示）
        internal const int WhiteHairWeakBuff = 880032;  // 隐藏状态：白毛少女的力量-10

        internal const int VanillaCharmBuff = 113;      // 本体「魅惑」
        internal const int VanillaWeakPointBuff = 135;  // 本体「弱点暴露」
    }

    internal static class SecretTraits
    {
        // ---- 状态 ----

        /// <summary>「法术免伤」上次生效的战斗回合，每场战斗开始/结束时清空。</summary>
        private static readonly Dictionary<BattleRole, int> MagicImmuneRound = new Dictionary<BattleRole, int>();

        /// <summary>模组边界监听（收回「百合未谢」）的日志只打一次。</summary>
        private static bool _moduleListenerLogged;

        // ---- 通用 ----

        /// <summary>角色身上某个特质是不是处于"苏醒"状态。</summary>
        internal static bool HasTraitWake(BattleRole role, int traitId)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return false;
                }
                MOD_Dynamic_Trait trait = role.Data.GetTraitData(traitId);
                return trait != null && trait.CurrentState == ETraitState.Wake;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string NameOf(BattleRole role)
        {
            try
            {
                return role != null && role.Data != null ? role.Data.Name : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>
        /// 每帧调用：处理排队的回击（记忆的双剑）。
        /// </summary>
        internal static void Tick()
        {
            TickPendingCounters();
            TickSegmentHold();
            TickExtraAction();   // 灰暗孤影：排队的"追加一次行动"（等行动结束的事件链跑完再执行）
        }

        /// <summary>
        /// 每 10 秒调用一次：补上"进/出模组"监听 + 把随特质苏醒的数值状态对齐。
        /// AddListener 自己是幂等的（内部会判重），所以每次调用都注册一遍最省心 ——
        /// 万一 GameEventManager 被重建，10 秒内也就补回来了。
        /// </summary>
        internal static void TickSlow()
        {
            try
            {
                if (!Singleton<GameEventManager>.HasInstance)
                {
                    return;
                }
                Singleton<GameEventManager>.Instance.AddListener<EGameEvent_GamePlay>(OnGamePlayEvent);
                if (!_moduleListenerLogged)
                {
                    _moduleListenerLogged = true;
                    AttackTargetPlugin.LogInfo("私货特质：模组进入/结束监听已就绪（用来收回「百合未谢」）");
                }
                EnsureAllRolesTraitStatuses(false);   // 定时补跑：只补"缺失"的状态，不校验数值（免得反复重挂）
            }
            catch (Exception e)
            {
                if (!_moduleListenerLogged)
                {
                    _moduleListenerLogged = true;
                    AttackTargetPlugin.LogError("私货特质：注册模组边界监听失败：" + e.Message);
                }
            }
        }

        // =====================================================================
        // 一、百合花（880021 / 状态 880024）
        // =====================================================================

        /// <summary>百合花环免疫的四个状态：中毒 102 / 流血 103 / 燃烧 104 / 骨折 105（原版 ID）。</summary>
        private static readonly int[] LilyWreathImmune = new int[] { 102, 103, 104, 105 };

        /// <summary>
        /// 2026-09-23 用户改口径：这四个状态**可以被挂上**（不然头环的"每回合解一个负面"就没东西可解了），
        /// 但**效果不生效** —— 拦截改到了 buff 的"效果执行"那一层，见 ShouldNegateBuffEffect。
        /// 这里保留函数壳（AddBuff 补丁还在用它），直接放行。
        /// </summary>
        internal static bool ShouldBlockBuff(BuffData buff, BattleRole role)
        {
            return false;
        }

        /// <summary>
        /// 百合花环：这四个状态可以挂在身上，但**它们的效果一律不执行**（不掉血、不削属性、不骨折）。
        /// 挂在 buff 的效果选项 OnAction 上拦（中毒=属性削减、流血/燃烧=扣血、骨折=专门选项）。
        /// </summary>
        internal static bool ShouldNegateBuffEffect(BuffData buff, BattleRole target)
        {
            try
            {
                if (buff == null || target == null || target.Data == null)
                {
                    return false;
                }
                if (!HasLilyEquip(target, SecretIds.LilyWreath))
                {
                    return false;
                }
                for (int i = 0; i < LilyWreathImmune.Length; i++)
                {
                    if (buff.Id == LilyWreathImmune[i])
                    {
                        AttackTargetPlugin.LogInfo("百合花环：「" + NameOf(target) + "」不受「" + BuffName(buff) + "」的效果影响");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string BuffName(BuffData buff)
        {
            try
            {
                if (buff != null && buff.Config != null && buff.Config.Name != null)
                {
                    return buff.Config.Name.GetValue();
                }
                return buff != null ? buff.Id.ToString() : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>
        /// 挂状态瞬间（AddBuff 的 Prefix）的处理。
        /// 放在 Prefix 里是有讲究的：AddBuff 是 async，Postfix 会在第一次 await 处就跑，
        /// 而 Prefix 稳稳定在"这条 buff 刚构造好、还没进角色列表"的时候。
        /// </summary>
        internal static void OnBeforeBuffAdd(BuffData buff, BattleRole role)
        {
            if (buff == null || role == null || role.Data == null)
            {
                return;
            }

            // 百合花：被魅惑的时长 +100%
            // 游戏判定是「EffectRounds 每回合 +1（RoundStart），追平 Config.Duration 就解除」，
            // 所以起手把 EffectRounds 压成 -Duration，就正好要多花一倍回合才追平（3 回合 → 6 回合）。
            // 注意 Config.Duration 是全局模板，这里只改这一条实例的 EffectRounds，不会污染别的角色。
            if (buff.Id == SecretIds.VanillaCharmBuff
                && buff.Config != null
                && buff.Config.Duration > 0
                && HasTraitWake(role, SecretIds.LilyTrait))
            {
                buff.EffectRounds = -buff.Config.Duration;
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(role) + "」被魅惑的时长 ×2（" + buff.Config.Duration + " → " + (buff.Config.Duration * 2) + " 回合）");
            }
        }

        /// <summary>
        /// 骰子刚算好、还没投出去的时候（_UpdateRollDiceTmpDiceCount 之后）。
        /// 百合花：敌方发起的「魅惑行动」→ 攻击方 +1 奖励骰；持有者对抗 → +1 惩罚骰。
        /// </summary>
        internal static void OnBeforeDiceRoll(BattleActiveBehaviorData data, List<DiceResultData> targetDices, List<BattleRole> overrideTargets)
        {
            try
            {
                // 百合花第四条：被魅惑期间的意志检定 / 意志对抗 +1 惩罚骰（跟"魅惑行动"那条互不影响）
                ApplyCharmedWillPunish(data, targetDices);

                if (data == null || data.Self == null || !BattleHelper.IsInBattle)
                {
                    return;
                }
                // 只认"敌方发起"的魅惑行动；茉莉自己去魅惑别人不受影响
                if (data.Self.IsAlly)
                {
                    return;
                }
                if (!IsCharmAction(data))
                {
                    return;
                }

                bool lilyInvolved = false;
                if (targetDices != null)
                {
                    for (int i = 0; i < targetDices.Count; i++)
                    {
                        DiceResultData dice = targetDices[i];
                        if (dice == null || dice.Role == null)
                        {
                            continue;
                        }
                        if (!HasTraitWake(dice.Role, SecretIds.LilyTrait) || !FirstTouch(dice))
                        {
                            continue;
                        }
                        dice.TmpPunishDice += 1;      // 对魅惑行动的对抗 +1 惩罚骰
                        lilyInvolved = true;
                    }
                }
                if (!lilyInvolved && overrideTargets != null)
                {
                    for (int i = 0; i < overrideTargets.Count; i++)
                    {
                        if (HasTraitWake(overrideTargets[i], SecretIds.LilyTrait))
                        {
                            lilyInvolved = true;
                            break;
                        }
                    }
                }
                if (!lilyInvolved)
                {
                    return;
                }
                if (data.RollDice != null && data.RollDice.CheckType != EDiceValueType.None && FirstTouch(data.RollDice))
                {
                    data.RollDice.TmpRewardDice += 1;   // 敌方的魅惑行动 +1 奖励骰
                }
                AttackTargetPlugin.LogInfo("百合花：敌方魅惑行动 +1 奖励骰（" + NameOf(data.Self) + "），对持有者的对抗 +1 惩罚骰");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：改骰子出错：" + e);
            }
        }

        // ---- 百合花第四条（2026-09-19 七改）：被魅惑期间，意志检定 / 意志对抗 +1 惩罚骰 ----
        //
        // 落点一共三处，覆盖游戏里"造骰子"的所有路径：
        //   ① 战斗内由 buff 触发的检定（魅惑自身的挣脱意志检定就走这条）→ Buff_DiceCheckOption._GetDiceResultList
        //   ② 战斗行动里的检定/对抗（技能、法术，攻击方一颗、目标方一颗）→ BattleActiveBehaviorData._UpdateRollDiceTmpDiceCount（本文件已有钩子）
        //   ③ 面板投骰（探索 / 幕间 / 非战斗）→ BattleHelper.GetDiceData 造出来的 CheckDiceData 有 PunishDiceCount
        // 只认「意志」(EHeroAttribute.POW = 4) 这一项；条件 = 拥有者身上挂着本体魅惑 113 且持有百合花特质。

        /// <summary>这个角色现在是不是"被魅惑中的百合花持有者"。</summary>
        private static bool IsLilyCharmed(BattleRole role)
        {
            try
            {
                return role != null
                    && role.Data != null
                    && HasTraitWake(role, SecretIds.LilyTrait)
                    && role.HaveBuff(SecretIds.VanillaCharmBuff);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>这颗骰子是不是"意志检定"。</summary>
        private static bool IsWillDice(DiceResultData dice)
        {
            return dice != null
                && dice.CheckType == EDiceValueType.Attr
                && dice.CheckId == (int)EHeroAttribute.POW;
        }

        /// <summary>同一颗骰子只让我们动一次（行动方那颗骰子在多目标时会走好几遍这个钩子）。</summary>
        private static readonly ConditionalWeakTable<DiceResultData, object> TouchedDice =
            new ConditionalWeakTable<DiceResultData, object>();

        private static bool FirstTouch(DiceResultData dice)
        {
            if (dice == null)
            {
                return false;
            }
            object dummy;
            if (TouchedDice.TryGetValue(dice, out dummy))
            {
                return false;
            }
            TouchedDice.Add(dice, null);
            return true;
        }

        /// <summary>② 战斗行动里的意志检定/对抗：谁身上挂着魅惑，就给谁那颗骰子 +1 惩罚骰。</summary>
        private static void ApplyCharmedWillPunish(BattleActiveBehaviorData data, List<DiceResultData> targetDices)
        {
            if (data == null)
            {
                return;
            }
            if (IsWillDice(data.RollDice) && IsLilyCharmed(data.Self) && FirstTouch(data.RollDice))
            {
                data.RollDice.TmpPunishDice += 1;
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(data.Self) + "」被魅惑中的意志检定 +1 惩罚骰（行动方）");
            }
            if (targetDices == null)
            {
                return;
            }
            for (int i = 0; i < targetDices.Count; i++)
            {
                DiceResultData dice = targetDices[i];
                if (!IsWillDice(dice) || !IsLilyCharmed(dice.Role) || !FirstTouch(dice))
                {
                    continue;
                }
                dice.TmpPunishDice += 1;
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(dice.Role) + "」被魅惑中的意志对抗 +1 惩罚骰");
            }
        }

        /// <summary>① buff 触发的意志检定（战斗内）：给每颗属于"被魅惑的持有者"的骰子加 1 惩罚骰。</summary>
        internal static void OnBuffDiceBuilt(DiceCheckElementData checkData, List<DiceResultData> dices)
        {
            try
            {
                if (checkData == null || dices == null)
                {
                    return;
                }
                if (checkData.CheckType != EDiceValueType.Attr || checkData.Attr != EHeroAttribute.POW)
                {
                    return;
                }
                for (int i = 0; i < dices.Count; i++)
                {
                    DiceResultData dice = dices[i];
                    if (!IsLilyCharmed(dice != null ? dice.Role : null))
                    {
                        continue;
                    }
                    dice.TmpPunishDice += 1;
                    AttackTargetPlugin.LogInfo("百合花：「" + NameOf(dice.Role) + "」被魅惑中的意志检定 +1 惩罚骰（buff 检定）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：buff 意志检定加惩罚骰出错：" + e);
            }
        }

        /// <summary>③ 面板投骰的意志检定（探索/幕间/非战斗）。</summary>
        internal static void OnExploreDiceBuilt(CheckDiceData data)
        {
            try
            {
                if (data == null || data.RollType != EDiceValueType.Attr || data.RoleAttr != EHeroAttribute.POW)
                {
                    return;
                }
                if (!IsLilyCharmed(data.Source))
                {
                    return;
                }
                data.PunishDiceCount += 1;
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(data.Source) + "」被魅惑中的意志检定 +1 惩罚骰（面板投骰）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：面板意志检定加惩罚骰出错：" + e);
            }
        }

        /// <summary>这条行动是不是"魅惑行动"（技能/法术的关联 buff 里有本体魅惑 113）。</summary>
        private static bool IsCharmAction(BattleActiveBehaviorData data)
        {
            try
            {
                if (data.BattleSkillData != null && data.BattleSkillData.Config != null
                    && data.BattleSkillData.Config.BuffsLink != null
                    && data.BattleSkillData.Config.BuffsLink.Contains(SecretIds.VanillaCharmBuff))
                {
                    return true;
                }
                if (data.MagicData != null && data.MagicData.Config != null
                    && data.MagicData.Config.BuffsLink != null
                    && data.MagicData.Config.BuffsLink.Contains(SecretIds.VanillaCharmBuff))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// 百合花：敌人带魅惑的行动优先指向持有者。
        /// 挂在 BattleBaseAI.AutoSelectTarget 之后 —— 敌方 AI 已经选完目标、写进
        /// CurrentBehaviourData.Targets 了，这时再把它改成茉莉（只有她确实是合法目标时才改）。
        /// 判断"合法"用的是游戏自己过滤过的候选表 roles，尊重不可选中之类规则。
        /// </summary>
        internal static void OnAiSelectTarget(BattleBaseAI ai, List<BattleRole> roles)
        {
            try
            {
                if (ai == null || ai.Self == null || roles == null || roles.Count == 0)
                {
                    return;
                }
                BattleRole attacker = ai.Self;
                if (attacker.IsAlly)
                {
                    return;   // 只动敌方 AI
                }
                BattleActiveBehaviorData behavior = attacker.CurrentBehaviourData;
                if (behavior == null || behavior.Targets == null || behavior.Targets.Count == 0)
                {
                    return;
                }
                if (behavior.TargetSelectType != ETargetSelect.Enemy_Single)
                {
                    return;   // 只改"单体指向"的魅惑行动；全体指向本来就会打到她
                }
                if (!IsCharmAction(behavior))
                {
                    return;
                }
                BattleRole lily = null;
                for (int i = 0; i < roles.Count; i++)
                {
                    BattleRole candidate = roles[i];
                    if (candidate == null || candidate.IsDeath || !candidate.IsAlly)
                    {
                        continue;
                    }
                    if (!candidate.CheckCanBeSelectedAsTarget(attacker))
                    {
                        continue;
                    }
                    if (!HasTraitWake(candidate, SecretIds.LilyTrait))
                    {
                        continue;
                    }
                    lily = candidate;
                    break;
                }
                if (lily == null || behavior.Targets.Contains(lily))
                {
                    return;   // 没找到人 / 本来就指着她
                }
                behavior.Targets.Clear();
                int count = behavior.GetMaxSelectCount();
                if (count < 1)
                {
                    count = 1;
                }
                for (int i = 0; i < count; i++)
                {
                    behavior.Targets.Add(lily);
                }
                AttackTargetPlugin.LogInfo("百合花：敌方「" + NameOf(attacker) + "」的魅惑行动被改指向「" + NameOf(lily) + "」");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：改 AI 指向出错：" + e);
            }
        }

        // =====================================================================
        // 二、战斗节点分发（BattleStart / BattleEnd / RoundStart）
        // =====================================================================

        internal static void OnBattleTrigger(BattleRole role, EBuffTriggerType trigger)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                switch (trigger)
                {
                    case EBuffTriggerType.BattleStart:
                        MagicImmuneRound.Remove(role);
                        ZangHua.ClearDeathClearing();        // 新一场战斗，清掉上一场残留的"死亡清理中"标记
                        ZangHua.ClearMarkBeDamage(role);     // 葬花：清掉上一场残留的剑痕易伤（防跨场残留）
                        JingJi.ClearState();              // 荆棘：清掉上一场战斗的状态缓存（束缚转换闸门 / 先发资格等）
                        JingJi.MarkFirstStrikeReady(role);   // 荆棘：先发资格只在这一刻判一次（2026-09-27 用户口径）
                        // 花香：2026-09-27 起战斗开始**不再**清空（战斗之间保留层数），清空时机挪到了副本边界
                        // 2026-09-23 用户口径：状态数值的对齐**只在进入副本时做一次**（见 OnGamePlayEvent 的 EnterModule），
                        // 不要每场战斗都查一遍 —— 那样会反复重挂状态、属性来回跳。
                        ZangHua.EnsureWeapon(role);          // 葬花：战斗开始时确保茉莉手上有这把武器
                        JingJi.EnsureWeapon(role);        // 荆棘：同上
                        ZangHua.SyncOffhandBonus(role);      // 葬花：副手（护甲 +2 / 魔减 1）对齐一次
                        if (role.IsAlly && HasTraitWake(role, SecretIds.WhiteHairTrait))
                        {
                            WhiteHairBattleStart(role);
                        }
                        RefreshLoneBonusForAllies();
                        break;
                    case EBuffTriggerType.Attack:
                        ZangHua.OnAttackDeclared(role);      // 葬花：行动前的敏捷检定
                        break;
                    case EBuffTriggerType.RoundEnd:
                        JingJi.ClearShufu(role);              // 荆棘：回合结束（所有人行动完成后）移除【束缚】
                        JingJi.ClearAllTargetLocks();         // 荆棘：回合结束也把【目标锁定】清掉（剩下的两种情况由死亡清理/战斗结束兜底）
                        ZangHua.OnRoundEndFor(role);         // 葬花：剑痕层数减半 + 补流血
                        FlowerScentRoundEnd(role);           // 花香：层数减半（并回 1 点生命 + 1 点精神值）
                        break;
                    case EBuffTriggerType.ActionEnd:
                        QueueExtraAction(role);              // 灰暗孤影：近战武器攻击结束后，整套再来一遍
                        LilyRingAfterAction(role);           // 百合花戒指：施法后按消耗的魔法值对全体敌人造成伤害
                        break;
                    case EBuffTriggerType.ActionStart:
                        LilyRingRememberMp(role);            // 百合花戒指：记住行动开始时的魔法值
                        ZangHua.SyncOffhandBonus(role);   // 葬花：每次行动开始时再对一次持握加成（用户要求：进战斗 + 每回合 + 每次行动）
                        break;
                    case EBuffTriggerType.RoundStart:
                        ZangHua.OnRoundStartFor(role);       // 葬花：充能不足 5 点时，消耗 1 点精神值回 1 点充能
                        ZangHua.SyncOffhandBonus(role);      // 葬花：副手加成对齐（护甲 +2 / 魔减 1）
                        JingJi.ExecuteFirstStrike(role);  // 荆棘：对带【目标锁定】的敌人打一次（先发）
                        RefreshLoneBonusForAllies();
                        LilyChainRoundStart(role);           // 百合花链：随机吸一名敌人 2 点生命 + 叠【百合花芳香】
                        LilyRingRoundStart(role);            // 百合花戒指：回 2 点魔法值
                        LilyWreathRoundStart(role);          // 百合花环：随机解一个负面状态 + 叠【花香】
                        break;
                    case EBuffTriggerType.BeforeRoundStart:
                        LilyAromaRoundStart(role);           // 百合花芳香：轮开始按层数扣血（5 层时移除 + 爆）
                        JingJi.ApplyPendingShufu(role);      // 荆棘：命中攒下的【荆棘】在这里整颗转成【束缚】
                                                             //（2026-09-27 修：之前只写了转换函数、没接时机，所以束缚一直不出现）
                        JingJi.VerifyFirstStrikeReady(role);  // 荆棘：复核先发资格（只取消、不补发）
                        JingJi.MarkFirstStrikeTarget(role);  // 荆棘：轮开始前给随机敌人挂【目标锁定】
                        // 独行：每回合开始时判定（这是保底）
                        RefreshLoneBonusForAllies();
                        // 百合花环：也在这一个时机试一次解除（2026-09-25 用户要求"每一轮开始"都判一次）。
                        // 和下面 RoundStart 那处一起构成两个时机，同一轮内靠时间戳去重，不会重复解。
                        LilyWreathRoundStart(role);
                        break;
                    case EBuffTriggerType.Death:
                    case EBuffTriggerType.AfterDeath:
                        // 葬花：角色倒下后剑痕会被一起清掉，把"易伤"那条也摘干净
                        // （注意不能放在 BuffChanged 里 —— 剑痕自己叠层就会触发它，会把易伤误清）
                        ZangHua.ClearMarkBeDamage(role);
                        RefreshLoneBonusForAllies();
                        break;
                    case EBuffTriggerType.LifeStateChangeBeforeFIghtOverCheck:
                    case EBuffTriggerType.ChangeTeammate:
                    case EBuffTriggerType.QuitTeam:
                    case EBuffTriggerType.BuffChanged:
                        // 更细的判定：队友倒下/昏迷/被魅惑/入队离队/buff 变动时也立刻复核一次
                        RefreshLoneBonusForAllies();
                        break;
                    case EBuffTriggerType.BattleEnd:
                        MagicImmuneRound.Remove(role);
                        RemoveLoneBonus(role);
                        ZangHua.ClearDeathClearing();        // 战斗结束，标记不再需要
                        ZangHua.ClearMarkBeDamage(role);     // 葬花：战斗结束把剑痕易伤摘掉
                        JingJi.ClearState();              // 荆棘：状态缓存清掉
                        JingJi.ClearAllTargetLocks();         // 荆棘：战斗结束清掉所有【目标锁定】
                        JingJi.ClearFirstStrikeReady();       // 荆棘：先发资格也清掉
                        break;
                    case EBuffTriggerType.AttackDodgeSuccess:
                        // 灰暗孤影：闪避成功 → 立刻回击一次（2026-09-22 用户要求，从「记忆的双剑」转过来）
                        LoneShadowCounterAttack(role);
                        break;
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质：战斗节点（" + trigger + "）处理出错：" + e);
            }
        }

        // =====================================================================
        // 六、百合花装备（880002）
        //   · 持有「百合花」特质的人，进副本时自动获得并装备（装备后不可卸下）
        //   · 免死 / 每 2 回合法术免伤 两条效果挂在这件装备上
        //     （2026-09-22 用户要求：从「灰暗孤影」特质移植到装备上）
        // =====================================================================

        /// <summary>角色身上有没有「百合花」装备（装备栏里就算）。</summary>
        internal static bool HasBaiHeHua(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.Data.Equips == null)
                {
                    return false;
                }
                for (int i = 0; i < role.Data.Equips.Count; i++)
                {
                    MOD_Dynamic_Item item = role.Data.Equips[i];
                    if (item != null && item.Id == SecretIds.LilyEquip)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>在身上（装备栏）或背包里找「百合花」。</summary>
        private static MOD_Dynamic_Item FindBaiHeHua(BattleRole role)
        {
            return FindLilyItem(role, SecretIds.LilyEquip);
        }

        /// <summary>在身上（装备栏）或背包里找某件百合花系列装备。</summary>
        private static MOD_Dynamic_Item FindLilyItem(BattleRole role, int itemId)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return null;
                }
                if (role.Data.Equips != null)
                {
                    for (int i = 0; i < role.Data.Equips.Count; i++)
                    {
                        MOD_Dynamic_Item item = role.Data.Equips[i];
                        if (item != null && item.Id == itemId)
                        {
                            return item;
                        }
                    }
                }
                if (role.Data.BagItems != null)
                {
                    for (int i = 0; i < role.Data.BagItems.Count; i++)
                    {
                        MOD_Dynamic_Item item = role.Data.BagItems[i];
                        if (item != null && item.Id == itemId)
                        {
                            return item;
                        }
                    }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>装备栏里有没有某件百合花系列装备。</summary>
        internal static bool HasLilyEquip(BattleRole role, int itemId)
        {
            try
            {
                if (role == null || role.Data == null || role.Data.Equips == null)
                {
                    return false;
                }
                for (int i = 0; i < role.Data.Equips.Count; i++)
                {
                    MOD_Dynamic_Item item = role.Data.Equips[i];
                    if (item != null && item.Id == itemId)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>进副本时：给队里该有百合花的人都检查一遍（主角队 + 后备队）。</summary>
        internal static void EnsureBaiHeHuaForTeam()
        {
            try
            {
                List<BattleRole> team = BattleHelper.GetHeroTeamRoleList();
                if (team != null)
                {
                    for (int i = 0; i < team.Count; i++)
                    {
                        EnsureBaiHeHua(team[i]);
                    }
                }
                List<BattleRole> backup = BattleHelper.GetHeroBackupTeamRoleList();
                if (backup != null)
                {
                    for (int i = 0; i < backup.Count; i++)
                    {
                        EnsureBaiHeHua(backup[i]);
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：进副本发装备出错：" + e.Message);
            }
        }

        internal static void EnsureBaiHeHua(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !role.IsAlly)
                {
                    return;
                }
                if (!HasTraitWake(role, SecretIds.LilyTrait))
                {
                    return;   // 只有带「百合花」特质的人才有这件装备
                }
                // 四件套：百合花（通用槽）／百合花链（通用槽）／百合花戒指（通用槽）／百合花环（护具槽）
                EnsureOneEquip(role, SecretIds.LilyEquip, "百合花");
                EnsureOneEquip(role, SecretIds.LilyChain, "百合花链");
                EnsureOneEquip(role, SecretIds.LilyRing, "百合花戒指");
                EnsureOneEquip(role, SecretIds.LilyWreath, "百合花环");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：发装备出错：" + e.Message);
            }
        }

        /// <summary>一件装备：有就确保装上（不可卸下，装上就一直戴着），没有就发一件再装。</summary>
        private static void EnsureOneEquip(BattleRole role, int itemId, string label)
        {
            MOD_Dynamic_Item item = FindLilyItem(role, itemId);
            if (item != null)
            {
                if (item.InState != EItemInState.InEquip)
                {
                    EquipBaiHeHuaAsync(item, role, label);   // 躺在背包里就装上
                }
                return;
            }
            GiveBaiHeHua(role, itemId, label);
        }

        private static async void GiveBaiHeHua(BattleRole role, int itemId, string label)
        {
            try
            {
                await role.Data.AddItem(itemId);
                MOD_Dynamic_Item item = FindLilyItem(role, itemId);
                if (item == null)
                {
                    AttackTargetPlugin.LogError("百合花：" + label + " 发出去以后没找到，跳过装备");
                    return;
                }
                if (item.InState != EItemInState.InEquip)
                {
                    await item.EquipItem(role, false);
                }
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(role) + "」拿到并装备了" + label + "（装备后不可卸下）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：发「" + label + "」出错：" + e.Message);
            }
        }

        private static async void EquipBaiHeHuaAsync(MOD_Dynamic_Item item, BattleRole role, string label)
        {
            try
            {
                await item.EquipItem(role, false);
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(role) + "」把背包里的" + label + "装备上了");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("百合花：装备「" + label + "」出错：" + e.Message);
            }
        }

        // =====================================================================
        // 九、百合花链 / 戒指 / 头环（2026-09-23 用户需求）
        //   链：回合开始随机吸一名敌人 2 点生命 + 叠 1 层【百合花芳香】
        //   芳香（880048）：回合结束每层扣 1 点生命；满 5 层时立刻扣（最大生命 10% + 5）并清除
        //   戒指：回合开始回 2 点魔法值（用游戏自带的"每回合魔法回复"属性承载）
        //   头环：回合开始随机解除一个可解除的非永久负面状态，每解一个叠 1 层【花香】
        //   花香（880049）：每层意志检定 +5；战斗/探索回合结束层数减半并回 1 血 1 SAN
        //                    （2026-09-27 用户口径：原"每层伤害加成"已撤掉）
        // =====================================================================

        /// <summary>身上有某件百合花装备 + 在战斗中 + 没死 —— 这类"装备带来的每回合效果"的通用前置。</summary>
        private static bool CanRunLilyEquipEffect(BattleRole role, int itemId)
        {
            return role != null && role.Data != null && !role.IsDeath && role.IsAlly
                && BattleHelper.IsInBattle && BattleHelper.FightContent != null
                && HasLilyEquip(role, itemId);
        }

        private static T PickRandomAlive<T>(List<T> list) where T : BattleRole
        {
            try
            {
                if (list == null) return null;
                List<T> alive = new List<T>();
                for (int i = 0; i < list.Count; i++)
                {
                    T r = list[i];
                    if (r != null && !r.IsDeath) alive.Add(r);
                }
                if (alive.Count == 0) return null;
                return alive[UnityEngine.Random.Range(0, alive.Count)];
            }
            catch (Exception) { return null; }
        }

        /// <summary>百合花链：回合开始随机挑一名敌人吸 2 点生命，并叠 2~3 层【百合花芳香】。</summary>
        private static async void LilyChainRoundStart(BattleRole self)
        {
            try
            {
                if (!CanRunLilyEquipEffect(self, SecretIds.LilyChain)) return;
                BattleRole target = PickRandomAlive(BattleHelper.FightContent.CurWaveEnemies);
                if (target == null) return;
                // 2026-09-24：原来是无脑 AddBuff，用户反馈层数一直是 1（像是每次都被刷回 1 层）。
                // 这里改成显式取现有实例再叠 1 层，并把前后层数打进日志 ——
                // 万一还是没涨，日志会直接告诉我们卡在哪儿（MaxLayer / DisableOverlayBuffs）。
                int addLayer = UnityEngine.Random.Range(2, 4);   // 2~3 层（2026-09-27 用户口径：原 1~3）
                BuffData aroma = target.GetBuff(SecretIds.LilyAromaBuff);
                if (aroma != null)
                {
                    int beforeLayer = aroma.CurLayer;
                    await aroma.ChangeLayer(target, addLayer);
                    AttackTargetPlugin.LogInfo("百合花芳香：叠加层数 " + beforeLayer + " → " + aroma.CurLayer +
                        (aroma.CurLayer == beforeLayer
                            ? "（没涨！MaxLayer=" + aroma.Config.MaxLayer + " OverlayType=" + aroma.Config.OverlayType + "）"
                            : "，共 " + aroma.CurLayer + " 层") +
                        "｜目标「" + NameOf(target) + "」");
                }
                else
                {
                    // 诊断：万一真的"每次都当成首次挂上"，把目标身上现有的 buff id 打出来，好判断是列表里查不到、
                    // 还是这个状态被别的东西摘掉了（2026-09-24 用户反馈"每次吸血都会重新添加"）。
                    string existing = "";
                    if (target.Data != null && target.Data.BuffList != null)
                    {
                        for (int bi = 0; bi < target.Data.BuffList.Count; bi++)
                        {
                            BuffData b = target.Data.BuffList[bi];
                            if (b != null)
                            {
                                existing += b.Id + " ";
                            }
                        }
                    }
                    await target.AddBuff(self, SecretIds.LilyAromaBuff);
                    if (addLayer > 1)
                    {
                        BuffData fresh = target.GetBuff(SecretIds.LilyAromaBuff);
                        if (fresh != null) await fresh.ChangeLayer(target, addLayer - 1);   // 首次直接补到 1~3 层
                    }
                    // 挂完立刻回查一次：能区分"根本没进列表"和"进了但显示不出来"（2026-09-25 用户反馈敌人身上看不到）。
                    BuffData check = target.GetBuff(SecretIds.LilyAromaBuff);
                    AttackTargetPlugin.LogInfo("百合花芳香：首次挂上 → 目标「" + NameOf(target) +
                        "」挂之前身上是：[" + existing + "]，挂完回查：" +
                        (check != null ? check.CurLayer + " 层" : "**查不到（没挂上）**"));
                }
                await target.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, "-2"), "", true, true);
                await self.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, "2"), "", true, true);
                // 原版血之花手镯那套吸血是有表现的：敌人身上闪一下 + 自己身上播恢复特效与音效。
                // 我们之前只改数值、什么都没有，看着像没发生（2026-09-24 用户反馈）。
                SecretFx.Drain(self, target);
                SecretFx.Recover(self);
                AttackTargetPlugin.LogInfo("百合花链：「" + NameOf(self) + "」吸取「" + NameOf(target) +
                    "」2 点生命");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花链：回合开始结算出错：" + e.Message); }
        }

        /// <summary>恍惚：处于魅惑状态时意志 -25（伤害减半那条在 OnDamageCalculated 里）。</summary>
        private const string CharmDazeSourceKey = "secret_charm_daze";

        private static async void SyncCharmDaze(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                bool charmed = role.GetBuff(SecretIds.VanillaCharmBuff) != null;
                await role.Data.ChangeAttr(false, new ChangeAttrData(EHeroAttribute.POW, "0"), CharmDazeSourceKey);
                if (charmed)
                {
                    await role.Data.ChangeAttr(true, new ChangeAttrData(EHeroAttribute.POW, "-25"), CharmDazeSourceKey);
                }
            }
            catch (Exception e) { AttackTargetPlugin.LogError("恍惚：意志同步出错：" + e.Message); }
        }
        /// <summary>百合花芳香层数（0 = 没中）。</summary>
        private static int LilyAromaLayer(BattleRole role)
        {
            try
            {
                BuffData b = role != null ? role.GetBuff(SecretIds.LilyAromaBuff) : null;
                return b != null ? b.CurLayer : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 粉尘爆炸：中心角色带着【百合花芳香】被打中火焰伤害时触发。
        /// 它和它同阵营的所有人各受 2×层数 的爆炸伤害，中心那个身上的芳香被移除（立刻触发）。
        /// </summary>
        private static async void DustExplosion(BattleRole center, int layer)
        {
            try
            {
                if (center == null || center.Data == null) return;
                int dmg = layer * 2;
                AttackTargetPlugin.LogInfo("百合花芳香：粉尘爆炸！「" + NameOf(center) + "」被火焰打中，" +
                    layer + " 层 → 同阵营每人受到 " + dmg + " 点爆炸伤害");
                await center.RemoveBuff(SecretIds.LilyAromaBuff);
                List<BattleRole> sameSide = new List<BattleRole>();
                if (BattleHelper.FightContent != null)
                {
                    if (center.IsAlly && BattleHelper.FightContent.Allies != null)
                    {
                        sameSide.AddRange(BattleHelper.FightContent.Allies);
                    }
                    else if (!center.IsAlly && BattleHelper.FightContent.CurWaveEnemies != null)
                    {
                        sameSide.AddRange(BattleHelper.FightContent.CurWaveEnemies);
                    }
                }
                for (int i = 0; i < sameSide.Count; i++)
                {
                    BattleRole r = sameSide[i];
                    if (r == null || r.IsDeath) continue;
                    await r.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, (-dmg).ToString()), "", true, true);
                }
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花芳香：粉尘爆炸出错：" + e.Message); }
        }
        /// <summary>百合花芳香：每个行动轮开始按层数扣血；层数达到 5 层时移除该效果，并额外扣「层数% + 层数」的伤害。</summary>
        private static async void LilyAromaRoundStart(BattleRole target)
        {
            try
            {
                if (target == null || target.Data == null || target.IsDeath) return;
                BuffData buff = target.GetBuff(SecretIds.LilyAromaBuff);
                if (buff == null || buff.CurLayer <= 0) return;
                int layer = buff.CurLayer;
                if (layer >= 5)
                {
                    int maxHp = target.Data.GetRoleExtraAttrMaxValue(ERoleExtraAttribute.CurrentHp);
                    int dmg = (int)(maxHp * 0.01f * layer) + layer;   // x% + x（x = 层数，2026-09-25 用户口径）
                    AttackTargetPlugin.LogInfo("百合花芳香：「" + NameOf(target) + "」达到 " + layer + " 层，失去 " + dmg + " 点生命并移除该效果");
                    await target.RemoveBuff(SecretIds.LilyAromaBuff);
                    await target.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, (-dmg).ToString()), "", true, true);
                }
                else
                {
                    AttackTargetPlugin.LogInfo("百合花芳香：「" + NameOf(target) + "」轮开始，失去 " + layer + " 点生命");
                    await target.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, (-layer).ToString()), "", true, true);
                }
                SyncLilyAromaTier(target);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花芳香：结算出错：" + e.Message); }
        }

        /// <summary>芳香层数变化后把隐藏档位对齐（档位只负责"层数显示的数感"，效果由插件按 CurLayer 算）。</summary>
        private static void SyncLilyAromaTier(BattleRole target)
        {
            // 这里暂时什么都不做：层数本身就是 CurLayer，扣血逻辑按它算。
            // 留这个空方法是给以后"按层数改属性"留的口子（那时再补隐藏档位）。
        }

        /// <summary>百合花戒指：回合开始回 2 点魔法值。</summary>
        private static async void LilyRingRoundStart(BattleRole self)
        {
            try
            {
                if (!CanRunLilyEquipEffect(self, SecretIds.LilyRing)) return;
                int max = self.Data.GetRoleExtraAttrMaxValue(ERoleExtraAttribute.CurrentMp);
                int cur = self.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentMp);
                if (cur >= max) return;   // 满了就不加
                int add = Math.Min(2, max - cur);
                await self.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentMp, add.ToString()), "", true, true);
                AttackTargetPlugin.LogInfo("百合花戒指：「" + NameOf(self) + "」恢复 " + add + " 点魔法值");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花戒指：回合开始结算出错：" + e.Message); }
        }

        /// <summary>头环："不可解除"的负面状态名单 —— 魅惑 + 我们自己的标记类，不参与随机解除。</summary>
        private static bool IsUnRemovableNegative(int buffId)
        {
            return buffId == SecretIds.VanillaCharmBuff        // 魅惑
                || buffId == SecretIds.UndyingBuff            // 百合未谢（免死标记）
                || buffId == SecretIds.LilyBuff               // 百合花（特质自带的显示状态）
                || buffId == SecretIds.LilyAromaBuff          // 百合花芳香（这是敌人身上的，保险起见也排掉）
                || buffId == SecretIds.LoneShadowBuff         // 灰暗孤影（纯标记）
                || buffId == SecretIds.MemoryBuff;            // 记忆的双剑（纯显示）
        }

        /// <summary>
        /// 百合花环能解的状态白名单（2026-09-25 用户口径：只解这几种常见的，别再靠"负面+有层数"这种宽判据
        /// 去猜 —— 那样容易顺手解掉副本环境挂上来的东西）。
        /// 编号取自游戏数据：
        ///   101 昏迷（用户说的"晕眩"，游戏里没有单独叫晕眩的状态）、102 中毒、103 流血、104 燃烧、
        ///   105 骨折、128 恐惧、135 弱点暴露。
        /// </summary>
        private static readonly int[] WreathRemovable =
        {
            101,   // 昏迷（用户说的"晕眩"）
            102,   // 中毒
            103,   // 流血
            104,   // 燃烧
            105,   // 骨折
            108,   // 混乱（2026-09-25 用户追加）
            128,   // 恐惧
            135    // 弱点暴露
        };

        private static bool IsWreathRemovable(int buffId)
        {
            for (int i = 0; i < WreathRemovable.Length; i++)
            {
                if (WreathRemovable[i] == buffId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>百合花环上一次尝试解除的时间 —— 同一轮内 BeforeRoundStart / RoundStart 两个时机去重用。</summary>
        private static readonly Dictionary<BattleRole, float> WreathLastTry = new Dictionary<BattleRole, float>();

        /// <summary>百合花环：每一轮开始随机解除白名单里的一个负面状态，并按被解除状态的层数叠【花香】。</summary>
        private static async void LilyWreathRoundStart(BattleRole self)
        {
            try
            {
                if (!CanRunLilyEquipEffect(self, SecretIds.LilyWreath)) return;
                // 去重：BeforeRoundStart / RoundStart 两个时机都会叫到这里，同一个很短的时间窗里只跑一次，
                // 免得一轮里连着解两个负面（2026-09-25 加了 BeforeRoundStart 这个时机）。
                float now = UnityEngine.Time.unscaledTime;
                if (WreathLastTry.TryGetValue(self, out float lastTry) && now - lastTry < 0.5f)
                {
                    return;
                }
                WreathLastTry[self] = now;
                if (self.Data.BuffList == null) return;
                List<BuffData> candidates = new List<BuffData>();
                for (int i = 0; i < self.Data.BuffList.Count; i++)
                {
                    BuffData b = self.Data.BuffList[i];
                    if (b == null || b.Config == null) continue;
                    // 2026-09-25 用户口径：改成白名单制 —— 只解指定那几种常见负面，别的（包括副本/环境挂的）
                    // 一律不碰。白名单之外的判断（负面类型 / 层数 / 时限）都不再需要。
                    if (!IsWreathRemovable(b.Id)) continue;
                    candidates.Add(b);
                }
                if (candidates.Count == 0) return;
                BuffData pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                int pickedId = pick.Id;
                string pickedName = pick.Config.Name != null ? pick.Config.Name.GetValue() : pickedId.ToString();
                // 花香层数 = 被解除那个状态的层数（2026-09-23 用户口径）：
                //   · 至少 1 层（层数取不到 / 是 0 时）
                //   · 至多 5 层
                //   · "无穷"（层数无上限 = MaxLayer <= 0）按 5 层算
                int pickedLayer = pick.CurLayer;
                int gain;
                if (pick.Config.MaxLayer <= 0)
                {
                    gain = 5;                        // 无上限的状态 = 无穷 → 按 5 层算
                }
                else
                {
                    gain = pickedLayer;
                    if (gain < 1) gain = 1;
                    if (gain > 5) gain = 5;
                }
                await self.RemoveBuff(pickedId);
                for (int i = 0; i < gain; i++)
                {
                    await self.AddBuff(self, SecretIds.FlowerScentBuff);
                }
                ClearFlowerScentAttr(self);      // 清理旧版伤害加成记录（幂等，2026-09-27 起花香不再加伤害）
                AttackTargetPlugin.LogInfo("百合花环：解除了「" + pickedName + "」（" + pickedLayer +
                    " 层），获得 " + gain + " 层「花香」");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花环：回合开始结算出错：" + e.Message); }
        }

        /// <summary>花香：战斗回合结束时层数减半（探索回合走 OnEquipTrigger 那条）。</summary>
        private static void FlowerScentRoundEnd(BattleRole self)
        {
            _ = HalveFlowerScent(self, "回合结束");
        }

        /// <summary>
        /// 花香：层数减半（向下取整）。**层数减少时**回复 1 点生命 + 1 点精神值
        /// （2026-09-27 用户口径：不再按减少的层数回血，每次"减少"事件只回 1+1）。
        /// 战斗回合结束、探索回合都会调它。
        /// </summary>
        private static async Task HalveFlowerScent(BattleRole self, string reason)
        {
            try
            {
                if (self == null || self.Data == null || self.IsDeath) return;
                BuffData buff = self.GetBuff(SecretIds.FlowerScentBuff);
                if (buff == null || buff.CurLayer <= 0) return;
                // 注意：BuffData.ChangeLayer 是**累加**（CurLayer += layer），不是"设成"。
                // 所以这里必须传差值（负数），传 half 的话层数会变成 cur + half（5 层 → 7 层），
                // 看起来就是"无限叠加"（2026-09-25 用户反馈）。
                int cur = buff.CurLayer;
                int half = cur / 2;
                if (half <= 0)
                {
                    await self.RemoveBuff(SecretIds.FlowerScentBuff);
                }
                else
                {
                    await buff.ChangeLayer(self, half - cur);
                }
                if (!self.IsDeath)
                {
                    await self.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, "1"), "", true, true);
                    await self.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentSan, "1"), "", true, true);
                }
                ClearFlowerScentAttr(self);
                AttackTargetPlugin.LogInfo("花香：「" + NameOf(self) + "」" + reason + "层数 " + cur + " → " + half +
                    "，回复 1 点生命与 1 点精神值");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("花香：层数减半出错：" + e.Message); }
        }

        /// <summary>
        /// 「花香」**旧版伤害加成**的来源键（2026-09-26 之前用过）：现在只用来"清理老存档残留"，
        /// 每次层数变化都按它摘一遍（幂等），保证不会再有旧的徒手伤害加值挂在身上。
        /// </summary>
        private const string FlowerScentSourceKey = "secret_baihehua_scent";

        /// <summary>
        /// 花香：每层让"意志检定"的成功区间 +5（2026-09-27 用户口径 —— 取代原来的伤害加成）。
        /// 实现挂在 `BattleHelper.GetDiceCheckValue` 的 Postfix 上，
        /// 战斗检定、buff 检定、探索/面板检定三条路都会经过它（面板那条也走 GetDiceCheckValue）。
        /// </summary>
        private const int FlowerScentPowPerLayer = 5;

        /// <summary>
        /// 花香：直接清空（**进入副本 / 副本结束**时用）。
        /// 2026-09-27 用户口径：战斗开始不再清空（战斗之间保留层数），清空时机改成副本边界；
        /// 清空本身不算"层数减少"的回复（那是减半时的待遇）。
        /// </summary>
        private static async void ClearFlowerScent(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                BuffData buff = role.GetBuff(SecretIds.FlowerScentBuff);
                if (buff != null)
                {
                    int layer = buff.CurLayer;
                    await role.RemoveBuff(SecretIds.FlowerScentBuff);
                    AttackTargetPlugin.LogInfo("花香：清空 " + layer + " 层（进入副本 / 副本结束）");
                }
                ClearFlowerScentAttr(role);   // 不管有没有 buff 都按当前层数对齐一次属性
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("花香：清空出错：" + e.Message);
            }
        }

        /// <summary>
        /// 清理「花香」旧版的伤害加成记录（每层 +1 徒手伤害那套，2026-09-26 之前的实现）。
        /// 现在花香不再提供伤害加成，这个函数只负责"按 SourceKey 把老记录摘掉"，
        /// 反复调用是幂等的（这正是 12.1 那个 IsTrackSource 坑的正解）。
        /// </summary>
        private static async void ClearFlowerScentAttr(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                // 幂等：按 SourceKey 摘掉那条来源。老存档里如果还挂着旧版的"徒手伤害 +N"，这里会清干净。
                await role.Data.ChangeAttr(false, new ChangeAttrData(ERoleExtraAttribute.AddDamage, "0"), FlowerScentSourceKey);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("花香：清理旧伤害加成出错：" + e.Message); }
        }

        /// <summary>花香层数（0 = 没有）。</summary>
        private static int FlowerScentLayer(BattleRole role)
        {
            try
            {
                BuffData buff = role != null ? role.GetBuff(SecretIds.FlowerScentBuff) : null;
                return buff != null ? buff.CurLayer : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 花香：意志检定 +5/层（2026-09-27 用户口径：把"伤害加成"换成"更容易检定成功"）。
        /// 挂在 `BattleHelper.GetDiceCheckValue` 的 Postfix —— 战斗技能检定、buff 检定、
        /// 探索/面板检定三条路最终都走它，所以一处改动三处生效。
        /// </summary>
        internal static void OnQueryWillDiceValue(BattleRole target, EHeroAttribute attrType, ref int result)
        {
            try
            {
                if (attrType != EHeroAttribute.POW || target == null || result <= 0)
                {
                    return;
                }
                int layer = FlowerScentLayer(target);
                if (layer <= 0)
                {
                    return;
                }
                int bonus = FlowerScentPowPerLayer * layer;
                result += bonus;
                AttackTargetPlugin.LogInfo("花香：「" + NameOf(target) + "」" + layer +
                    " 层 → 本次意志检定 +" + bonus + "（成功区间 " + (result - bonus) + " → " + result + "）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("花香：意志检定加值出错：" + e.Message);
            }
        }

        /// <summary>
        /// 花香：探索中每个"探索回合"也减半（2026-09-27 用户口径：让探索里也能吃到回复与检定加成）。
        /// 游戏在探索时逐个角色调 `TriggerEquipItemsEffect(ExploreRoundChange)`，我们挂在那上面。
        /// </summary>
        internal static void OnEquipTrigger(BattleRole role, ESkillTriggerType type)
        {
            try
            {
                if (type != ESkillTriggerType.ExploreRoundChange || role == null || role.Data == null)
                {
                    return;
                }
                if (FlowerScentLayer(role) <= 0)
                {
                    return;
                }
                _ = HalveFlowerScent(role, "探索回合");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("花香：探索回合减半出错：" + e.Message);
            }
        }

        // ---- 百合花戒指：施法后按消耗的魔法值对全体敌人造成伤害 ----

        /// <summary>每次行动开始时的魔法值（按角色记），用来算"这次行动消耗了多少魔法值"。</summary>
        private static readonly Dictionary<BattleRole, int> RingMpAtActionStart = new Dictionary<BattleRole, int>();

        /// <summary>行动开始：记住当前魔法值（只有戴戒指的人需要）。</summary>
        private static void LilyRingRememberMp(BattleRole self)
        {
            try
            {
                if (!CanRunLilyEquipEffect(self, SecretIds.LilyRing)) return;
                RingMpAtActionStart[self] = self.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentMp);
            }
            catch (Exception) { }
        }

        /// <summary>行动结束：如果这次是法术、且消耗了魔法值 —— 对全体敌人造成等量法术伤害。</summary>
        private static async void LilyRingAfterAction(BattleRole self)
        {
            try
            {
                if (!CanRunLilyEquipEffect(self, SecretIds.LilyRing)) return;
                int before;
                if (!RingMpAtActionStart.TryGetValue(self, out before)) return;
                RingMpAtActionStart.Remove(self);
                BattleActiveBehaviorData behaviour = self.CurrentBehaviourData;
                if (behaviour == null || behaviour.MagicData == null) return;   // 这次不是法术
                int now = self.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentMp);
                int cost = before - now;
                if (cost <= 0) return;
                int dmg = cost;                          // 伤害 = 消耗的魔法值（2026-09-27 用户口径：原为"一半"）
                if (dmg <= 0) return;
                List<Game.BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                if (enemies == null) return;
                int hit = 0;
                for (int i = 0; i < enemies.Count; i++)
                {
                    BattleRole e = enemies[i];
                    if (e == null || e.IsDeath) continue;
                    // 走游戏自己的伤害计算：会按"法术伤害"吃抗性 / 免疫 / 各种加成，
                    // 不是直接扣血（用户明确要求"就得是法术伤害"）。
                    DamageData damage = new DamageData();
                    damage.DamageType = EDamageType.Magic;
                    damage.Value = dmg.ToString();
                    int final = BattleHelper.CalculationDamage(damage, self, e, null);
                    if (final <= 0) continue;
                    await e.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentHp, (-final).ToString()), "", true, true);
                    hit++;
                }
                AttackTargetPlugin.LogInfo("百合花戒指：「" + NameOf(self) + "」施法消耗 " + cost +
                    " 点魔法值 → 对 " + hit + " 名敌人各造成 " + dmg + " 点法术伤害（已过法术结算）");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("百合花戒指：施法后结算出错：" + e.Message); }
        }

        // =====================================================================
        // 七、灰暗孤影：行动结束后"整套再来一遍"（改用原版「战斗触发技能效果」）
        //   2026-09-22 用户确认的口径：
        //     · 只有近战武器攻击才追加 —— 远程武器不触发，免得突击步枪扫射两轮；
        //     · 记录里的目标全死了就不执行；
        //     · 追加的这次行动结束不会再触发一遍（原版 IsTriggerActionEnd 默认 false）。
        //   为什么排队到 Tick 再执行：触发点是 TriggerBuffs 的 Prefix（同步方法），
        //   当场 await 会卡住 buff 事件链；等下一帧、事件链跑完再动手最稳。
        // =====================================================================

        private static readonly HashSet<BattleRole> PendingExtraAction = new HashSet<BattleRole>();
        private static bool _extraActionRunning;

        private static void QueueExtraAction(BattleRole role)
        {
            try
            {
                if (_extraActionRunning || role == null || role.Data == null)
                {
                    return;
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;
                }
                if (role.IsDeath || role.IsUnableAct)
                {
                    return;
                }
                if (!HasTraitWake(role, SecretIds.LoneShadowTrait))
                {
                    return;
                }
                BattleActiveBehaviorData behaviour = role.CurrentBehaviourData;
                if (behaviour == null || behaviour.BattleSkillData == null)
                {
                    return;
                }
                MOD_Dynamic_Item weapon = behaviour.BattleSkillData.MasterHandWeapon;
                if (!IsMeleeWeapon(weapon))
                {
                    return;   // 只认近战武器攻击
                }
                HeroSkillRecord record = role.LastSkillRecord;
                if (record == null || record.Targets == null)
                {
                    return;
                }
                bool anyAlive = false;
                for (int i = 0; i < record.Targets.Count; i++)
                {
                    BattleRole t = record.Targets[i];
                    if (t != null && !t.IsDeath)
                    {
                        anyAlive = true;
                        break;
                    }
                }
                if (!anyAlive)
                {
                    return;   // 目标都死了就不执行
                }
                PendingExtraAction.Add(role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：追加行动排队出错：" + e.Message);
            }
        }

        private static void TickExtraAction()
        {
            if (PendingExtraAction.Count == 0)
            {
                return;
            }
            List<BattleRole> ready = new List<BattleRole>(PendingExtraAction);
            PendingExtraAction.Clear();
            for (int i = 0; i < ready.Count; i++)
            {
                RunExtraAction(ready[i]);
            }
        }

        private static async void RunExtraAction(BattleRole role)
        {
            try
            {
                if (_extraActionRunning || role == null || role.IsDeath || role.IsUnableAct)
                {
                    return;
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;
                }
                _extraActionRunning = true;
                AttackTargetPlugin.LogInfo("灰暗孤影：「" + NameOf(role) + "」追加一次行动（原版机制：整套再来一遍）");
                Buff_TriggerBattleActionEffect opt = new Buff_TriggerBattleActionEffect();
                opt.TargetType = BaseBuffOption.EBuffOptionTargetType.BuffTarget;   // 自己
                opt.UseLastRecord = true;          // 用记录里的技能（就是刚刚这次）
                opt.UseLastActionTargets = true;   // 沿用记录里的目标
                opt.IsTriggerActionEnd = false;    // 追加的这次结束不再触发一遍，防递归
                await opt.OnAction(null, role, null);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：追加行动出错：" + e);
            }
            finally
            {
                _extraActionRunning = false;
            }
        }

        // =====================================================================
        // 三、白毛少女（880022）
        // =====================================================================

        private static void WhiteHairBattleStart(BattleRole self)
        {
            if (BattleHelper.FightContent == null)
            {
                return;
            }
            List<BattleNpcRole> wave = BattleHelper.FightContent.CurWaveEnemies;
            if (wave == null || wave.Count == 0)
            {
                return;
            }
            List<BattleRole> targets = new List<BattleRole>();
            for (int i = 0; i < wave.Count; i++)
            {
                BattleRole enemy = wave[i];
                if (enemy == null || enemy.IsDeath || enemy.IsUnableAct)
                {
                    continue;
                }
                targets.Add(enemy);
            }
            if (targets.Count == 0)
            {
                return;
            }
            AttackTargetPlugin.LogInfo("白毛少女：战斗开始，对 " + targets.Count + " 个敌人结算（" + NameOf(self) + "）");
            ProcessWhiteHair(self, targets);   // async void：故意不挡战斗开始流程
        }

        private static async void ProcessWhiteHair(BattleRole self, List<BattleRole> enemies)
        {
            try
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    BattleRole enemy = enemies[i];
                    if (enemy == null || enemy.IsDeath)
                    {
                        continue;
                    }
                    if (HasSan(enemy))
                    {
                        EDiceResult result = await RollAttrCheck(enemy, EHeroAttribute.POW);
                        if (result.GetLevel() < EDiceResult.Success.GetLevel())
                        {
                            await PlayCharmShow(enemy);
                            await enemy.AddBuff(self, SecretIds.VanillaCharmBuff);
                            AttackTargetPlugin.LogInfo("白毛少女：「" + NameOf(enemy) + "」意志检定失败（" + result + "），被魅惑 3 回合");
                        }
                        else
                        {
                            AttackTargetPlugin.LogInfo("白毛少女：「" + NameOf(enemy) + "」意志检定 " + result + "，抵抗住了魅惑");
                        }
                    }
                    else
                    {
                        await enemy.AddBuff(self, SecretIds.VanillaWeakPointBuff);
                        await enemy.AddBuff(self, SecretIds.VanillaWeakPointBuff);
                        AttackTargetPlugin.LogInfo("白毛少女：「" + NameOf(enemy) + "」没有理智值，获得 2 层弱点暴露");
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：结算出错：" + e);
            }
        }

        /// <summary>
        /// 白毛少女：给目标补一次本体「魅惑」技能（BattleSkill 14）的命中演出。
        /// 用户反馈"只有骰子没有魅惑行动的演出"——原版魅惑技能在 Trigger 5 里会播
        /// `Effect/Prefabs/FX_Skill_Charm`（时长 1.5 秒）+ 音效 `skill_charm`，
        /// 我们这里是插件直接挂 buff 的，所以得自己把这段演出补上（先演出、再上状态）。
        /// </summary>
        private static async Task PlayCharmShow(BattleRole target)
        {
            try
            {
                if (target == null || target.Model == null)
                {
                    return;
                }
                await target.Model.PlayFx(CharmFxPath, false, ERolePointType.Center);
                PrefabSingleton<AudioManager>.Instance.PlaySound(CharmFxSoundKey);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：魅惑演出出错（不影响结算）：" + e.Message);
            }
        }

        /// <summary>本体魅惑技能用的命中特效（见 BattleSkill\14.txt）。</summary>
        private const string CharmFxPath = "Effect/Prefabs/FX_Skill_Charm";

        /// <summary>本体魅惑技能用的命中音效 key（见 BattleSkill\14.txt 的 AudioReference.Key）。</summary>
        private const string CharmFxSoundKey = "skill_charm";

        /// <summary>「有理智」的口径（用户明确）：只看有没有理智值这一项，不看是不是人形。</summary>
        private static bool HasSan(BattleRole role)
        {
            if (role == null || role.Data == null)
            {
                return false;
            }
            int max = role.Data.GetRoleExtraAttrMaxValue(ERoleExtraAttribute.CurrentSan);
            int cur = role.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentSan);
            return max > 0 || cur > 0;
        }

        /// <summary>掷一次属性检定并把骰子演出来，返回结果。</summary>
        private static async Task<EDiceResult> RollAttrCheck(BattleRole role, EHeroAttribute attr)
        {
            DiceResultData dice = new DiceResultData();
            dice.Role = role;
            dice.CheckType = EDiceValueType.Attr;
            dice.CheckId = (int)attr;
            dice.OriginAttr = attr;
            dice.CheckNameKey = attr.GetLocalizationKeyData();
            dice.CheckValue = BattleHelper.GetDiceCheckValue(
                role, EExploreSkill.None, attr, ERoleExtraAttribute.None, EExploreSkill.None, false);

            List<DiceResultData> list = new List<DiceResultData>();
            list.Add(dice);
            await BattleHelper.UpdateDiceResultData(list, false, false);
            try
            {
                await PrefabSingleton<UIBattlePanel>.Instance.PlayDiceAnim(dice, false);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：骰子动画出错（不影响结算）：" + e.Message);
            }
            return dice.Result;
        }

        // =====================================================================
        // 四、灰暗孤影（880023）
        // =====================================================================

        // =====================================================================
        // 四·零、随特质苏醒的两个「数值状态」
        //   880024 百合花：意志 -15
        //   880026 灰暗孤影：敏捷 +20
        // 这两个数值走的是 BuffTableData.Arrts，而 Arrts **只在 AddBuff 那一刻**写进属性；
        // 存档里手塞的状态（或别人拿旧存档）不会补上这两个数值，所以这里统一由插件对齐：
        //   · 特质醒着、状态不在 → 补挂；
        //   · 状态在、但属性的来源表里没有它的 SourceKey → 重挂一次把数值补上；
        //   · 特质睡着 → 摘掉（数值随 Arrts 退回去）。
        // =====================================================================

        private static void EnsureAllRolesTraitStatuses(bool verifyValue = true)
        {
            ForEachKnownRole(r => EnsureTraitStatuses(r, verifyValue));
        }

        private static void EnsureTraitStatuses(BattleRole role, bool verifyValue = true)
        {
            // ⚠ 2026-09-26 修一个严重 bug（用户报"读档进游戏后属性又被扣了一遍"）：
            //
            // 原来这里传 verifyValue=true，SyncStatus 会去查"属性来源表里有没有这个状态的 SourceKey、数值对不对"，
            // 不对就重挂一次。问题是 VariableData 的 **_sources（来源表）根本不会随存档留下来** ——
            // 读档后只剩 _finalValue（最终值，已经含了状态的贡献），来源表是空的。
            // 于是每次读档都被误判成"数值不对" → 重挂 → 属性再扣一遍，越读越多。
            //
            // 现在一律传 false：**只补"状态缺失"，不再校验数值**。
            // 想改数值、又要让老存档跟上，就摘掉特质再挂一次（游戏内就能做），或者用 temp 里的脚本改档。
            SyncStatus(role, SecretIds.LilyTrait, SecretIds.LilyBuff, EHeroAttribute.POW, -30, "百合花", false);
            SyncStatus(role, SecretIds.WhiteHairTrait, SecretIds.WhiteHairWeakBuff, EHeroAttribute.STR, -40, "白毛少女", false);
            // 「柔弱的白毛少女」（880032）的数值迁移：进游戏后每个角色只试一次
            // （跨读档的防重复靠状态层数，见 MigrateWeaknessNumbers 的说明）
            if (WeaknessMigrationTried.Add(role))
            {
                MigrateWeaknessNumbers(role);
            }
            SyncStatus(role, SecretIds.LoneShadowTrait, SecretIds.LoneShadowBuff, EHeroAttribute.DEX, 20, "灰暗孤影", false);
            SyncStatus(role, SecretIds.MemoryTrait, SecretIds.MemoryBuff, EHeroAttribute.None, 0, "记忆的双剑", false);
            SyncWhiteHairDice(role);
            // 花香（880049）：2026-09-27 起不再提供伤害加成（改成"意志检定 +5/层"），
            // 这里只负责把老存档里残留的旧属性加成按 SourceKey 摘干净（幂等）。
            ClearFlowerScentAttr(role);
            // RemoveAvoidTrait(role);   // 2026-09-25 用户口径：改成改存档 —— 把 66「逃避」的 CurrentState 置成 2（Sleepy，沉睡），
            //                           // 用 temp\set_trait_state.ps1。函数本体留着，以后说不定能用在别的特质上。
            SyncCharmDaze(role);         // 恍惚：按当前是否处于魅惑，对齐意志 -25
        }

        // =====================================================================
        // 四·零点五、「柔弱的白毛少女」（880032）的数值迁移
        //
        // 背景：这个状态 2026-09-27 从"力量 -10"改成了"力量 -40 / 体质 -40 / 运动 -20"。
        // 但老存档里旧值已经**固化**进属性最终值，而读档后来源表是空的
        // （见上面 EnsureTraitStatuses 里 2026-09-26 的注释）——
        // 直接重挂会把旧值留在身上、再叠一遍新值，越扣越多（就是那个"读档又扣一遍"的 bug）。
        //
        // 所以：进游戏后的**第一次**对齐时做一次迁移 ——
        //   · 来源表里还有这条状态（本进程挂的）→ 直接重挂，数值自然刷新；
        //   · 来源表里没有（读档固化的旧值）→ 先手动补回旧值（力量 +10），再重挂。
        // 迁移完成后把状态层数设成 2，当"已迁移"的版本标记
        // （这个状态隐藏、数值不随层数缩放、MaxLayer 无上限），所以**只迁移一次**，
        // 之后无论读档多少次都不会重复扣。
        // 以后若再改这个状态的数值：把 WeaknessMigratedLayer +1、在这里补一段新的"补回旧值"逻辑即可。
        // =====================================================================

        /// <summary>「柔弱的白毛少女」迁移后的层数（当版本标记用：1 = 旧版数值，2 = 新版数值）。</summary>
        private const int WeaknessMigratedLayer = 2;

        /// <summary>本进程里已经尝试过迁移的角色（跨读档的防重复靠状态层数，见上）。</summary>
        private static readonly HashSet<BattleRole> WeaknessMigrationTried = new HashSet<BattleRole>();

        private static async void MigrateWeaknessNumbers(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (!HasTraitWake(role, SecretIds.WhiteHairTrait))
                {
                    return;   // 特质没醒着，等它醒了自己就是新数值
                }
                BuffData buff = role.GetBuff(SecretIds.WhiteHairWeakBuff);
                if (buff == null)
                {
                    return;   // 状态不在：SyncStatus 会按新配置补挂，不需要迁移
                }
                if (buff.CurLayer >= WeaknessMigratedLayer)
                {
                    return;   // 已经迁移过（层数就是版本标记），绝不再动 —— 防"读档重复扣"
                }
                bool tracked = HasAttrSourceValue(role, EHeroAttribute.STR, buff.SourceKey, -40)
                            || HasAttrSourceValue(role, EHeroAttribute.STR, buff.SourceKey, -10);
                if (!tracked)
                {
                    // 读档固化的旧值（力量 -10）：先补回来（不带来源键，直接改最终值）
                    await role.Data.ChangeAttr(true, new ChangeAttrData(EHeroAttribute.STR, "10"), "", false, false);
                    AttackTargetPlugin.LogInfo("白毛少女：检测到读档固化的旧数值，补回 力量+10 后重挂「柔弱的白毛少女」");
                }
                await role.RemoveBuff(SecretIds.WhiteHairWeakBuff);
                await role.AddBuff(role, SecretIds.WhiteHairWeakBuff);
                BuffData fresh = role.GetBuff(SecretIds.WhiteHairWeakBuff);
                if (fresh != null)
                {
                    await fresh.ChangeLayer(role, WeaknessMigratedLayer - 1);   // 层数 = 2，标记已迁移
                }
                AttackTargetPlugin.LogInfo("白毛少女：「柔弱的白毛少女」数值已刷新到最新配置（力量-40 / 体质-40 / 运动-20）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：数值迁移出错：" + e.Message);
            }
        }

        /// <summary>茉莉身上不要的疯狂特质：66「逃避」（2026-09-25 用户要求移除）。</summary>
        private static async void RemoveAvoidTrait(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                if (!HasTraitWake(role, SecretIds.MemoryTrait)) return;      // 只处理茉莉
                if (role.Data.GetTraitData(SecretIds.AvoidCrazyTrait) == null) return;
                await role.RemoveTrait(SecretIds.AvoidCrazyTrait);
                AttackTargetPlugin.LogInfo("茉莉：移除了「逃避」疯狂特质");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("茉莉：移除逃避特质出错：" + e.Message); }
        }
        /// <summary>
        /// 白毛少女：交涉 / 心理学 +1 奖励骰。
        /// 这条本来是数据里 WakeEvent.Inherent 的效果，只有"特质苏醒"那一刻才触发；
        /// 直接写进存档的特质没走过那一步，所以这里补跑一次 —— 用游戏自己的 SetActive 入口，
        /// 效果和自然苏醒完全一样（会按特质的 SourceKey 记来源，摘特质时也会退回去）。
        /// </summary>
        private static void SyncWhiteHairDice(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                MOD_Dynamic_Trait trait = role.Data.GetTraitData(SecretIds.WhiteHairTrait);
                if (trait == null || trait.CurrentState != ETraitState.Wake)
                {
                    return;
                }
                bool need = (HasSkill(role, EExploreSkill.Persuade) && !HasDiceSource(role, EExploreSkill.Persuade, trait.SourceKey))
                         || (HasSkill(role, EExploreSkill.Psychology) && !HasDiceSource(role, EExploreSkill.Psychology, trait.SourceKey));
                if (!need)
                {
                    return;
                }
                AttackTargetPlugin.LogInfo("白毛少女：补上「交涉 / 心理学 +1 奖励骰」");
                ApplyTraitWake(trait, role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：补奖励骰出错：" + e.Message);
            }
        }

        /// <summary>
        /// 白毛少女（880022）：**力量检定 / 力量对抗**时，敏捷的一半额外加进检定值
        /// （2026-09-27 用户口径 —— 她力量太弱，"碰上力量对抗基本不能成功"；
        ///  后又要求做成通用版：只要这枚检定是力量检定就加，不限于对抗）。
        ///
        /// 覆盖范围：所有"用力量做检定"的场合都经过 `BattleHelper.GetDiceCheckValue` ——
        ///   · 战斗技能的力量检定 / 力量对抗（`UseCheck.CheckAttr = STR`，反编译 115717 / 55956）；
        ///   · 探索、面板里的力量检定（`GetDiceData` 内部也调它）；
        ///   · **其它模组**只要用游戏标准的"属性检定/对抗"配置配技能，同样吃得到（我们挂的是底层入口）。
        /// 只有"自己另写一套判定、不调游戏检定函数"的模组技能覆盖不到。
        ///
        /// 读敏捷时再次调 `GetDiceCheckValue`（attrType=DEX）不会命中本分支，无递归问题。
        /// </summary>
        internal static void OnQueryStrengthDiceValue(BattleRole target, EHeroAttribute attrType, ref int result)
        {
            try
            {
                if (attrType != EHeroAttribute.STR)
                {
                    return;
                }
                if (target == null || target.Data == null || !HasTraitWake(target, SecretIds.WhiteHairTrait))
                {
                    return;
                }
                int dex = BattleHelper.GetDiceCheckValue(target, EExploreSkill.None, EHeroAttribute.DEX,
                    ERoleExtraAttribute.None, EExploreSkill.None, false);   // 当前敏捷（含各种加成）
                if (dex <= 0)
                {
                    return;
                }
                int bonus = dex / 2;   // "敏捷数值的一半"，向下取整
                if (bonus <= 0)
                {
                    return;
                }
                result += bonus;
                AttackTargetPlugin.LogInfo("白毛少女：「" + NameOf(target) + "」力量检定/对抗 +敏捷的一半 " + bonus +
                    "（敏捷 " + dex + "，检定值 → " + result + "）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女：力量对抗加值出错：" + e.Message);
            }
        }

        private static bool HasSkill(BattleRole role, EExploreSkill skill)
        {
            try
            {
                return role != null && role.Data != null && role.Data.RoleSkills != null
                    && role.Data.RoleSkills.Find(o => o.SkillType == skill) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>这个技能的奖励骰来源表里有没有记着某条来源（判断"+1 奖励骰"到底生效没生效）。</summary>
        private static bool HasDiceSource(BattleRole role, EExploreSkill skill, string sourceKey)
        {
            try
            {
                if (role == null || role.Data == null || role.Data.RoleSkills == null || string.IsNullOrEmpty(sourceKey))
                {
                    return false;
                }
                RoleSkill roleSkill = role.Data.RoleSkills.Find(o => o.SkillType == skill);
                if (roleSkill == null || roleSkill.SkillValue == null || roleSkill.SkillValue._exDiceSources == null)
                {
                    return false;
                }
                for (int i = 0; i < roleSkill.SkillValue._exDiceSources.Count; i++)
                {
                    if (roleSkill.SkillValue._exDiceSources[i] != null
                        && roleSkill.SkillValue._exDiceSources[i].Key == sourceKey)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async void ApplyTraitWake(MOD_Dynamic_Trait trait, BattleRole role)
        {
            try
            {
                if (trait.Config == null || trait.Config.WakeEvent == null)
                {
                    return;
                }
                await trait.Config.WakeEvent.SetActive(true, role.Data, trait);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("走特质苏醒入口失败：" + e);
            }
        }

        private static void SyncStatus(BattleRole role, int traitId, int buffId, EHeroAttribute checkAttr, int expectedValue,
            string label, bool verifyValue = true)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                bool awake = HasTraitWake(role, traitId);
                BuffData buff = role.GetBuff(buffId);
                if (!awake)
                {
                    if (buff != null)
                    {
                        AttackTargetPlugin.LogInfo(label + "：特质没醒着，收回状态 " + buffId);
                        RemoveBuffAsync(role, buffId);
                    }
                    return;
                }
                if (buff == null)
                {
                    AddBuffAsync(role, buffId, role, label + "：补上状态 " + buffId + "（数值随状态生效）");
                    return;
                }
                if (checkAttr == EHeroAttribute.None || !verifyValue)
                {
                    return;   // 不需要校验数值：只要状态在就行（定时补跑 / 这条状态本来就没有数值）
                }
                // 状态在，但属性来源表里没有它（手塞存档会这样）、或者记的数值跟现在配置不一致（改过数值）
                // → 重挂一次，让数值和当前配置对齐
                if (!HasAttrSourceValue(role, checkAttr, buff.SourceKey, expectedValue))
                {
                    AttackTargetPlugin.LogInfo(label + "：状态在但数值不对（期望 " + checkAttr + " " + expectedValue + "），重挂一次");
                    RefreshStatus(role, buffId);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError(label + "：对齐状态出错：" + e.Message);
            }
        }

        /// <summary>属性来源表里这条来源在不在、数值对不对（判断状态 Arrts 有没有生效/是不是旧值）。</summary>
        private static bool HasAttrSourceValue(BattleRole role, EHeroAttribute attr, string sourceKey, int expectedValue)
        {
            try
            {
                if (role == null || role.Data == null || role.Data.Attribute == null
                    || string.IsNullOrEmpty(sourceKey))
                {
                    return false;
                }
                VariableData<int> value = role.Data.Attribute.AttrDict[attr];
                if (value == null || value._sources == null)
                {
                    return false;
                }
                for (int i = 0; i < value._sources.Count; i++)
                {
                    if (value._sources[i] != null && value._sources[i].Key == sourceKey)
                    {
                        return value._sources[i].Value == expectedValue;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void RefreshStatusAsync(BattleRole role, int buffId)
        {
            RefreshStatus(role, buffId);
        }

        private static async void RefreshStatus(BattleRole role, int buffId)
        {
            try
            {
                await role.RemoveBuff(buffId);
                await role.AddBuff(role, buffId);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("重挂状态 " + buffId + " 失败：" + e);
            }
        }

        /// <summary>遍历"知道的调查员"：当前模组的队长 + 大厅名单。</summary>
        private static void ForEachKnownRole(Action<BattleRole> action)
        {
            if (action == null)
            {
                return;
            }
            try
            {
                BattleRole hero = BattleHelper.SceneHeroInfo;
                if (hero != null)
                {
                    action(hero);
                }
            }
            catch (Exception)
            {
            }
            try
            {
                if (!Singleton<HallWorld>.HasInstance || Singleton<HallWorld>.Instance.HallData == null)
                {
                    return;
                }
                List<RoleLibraryData> roles = Singleton<HallWorld>.Instance.HallData.HallLibrary.LibraryRoles;
                for (int i = 0; i < roles.Count; i++)
                {
                    HeroRoleData data = roles[i].BaseData;
                    if (data != null && data.Role != null)
                    {
                        action(data.Role);
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质：遍历调查员名单失败：" + e.Message);
            }
        }

        /// <summary>
        /// 效果 1：近战武器攻击后，整套攻击行动再来一遍。
        ///
        /// 为什么不用游戏自带的"追击"：追击是「一次命中结算」（不是整套行动），
        /// 而且次数被角色的「追击次数」属性卡死（默认每行动 1 次），
        /// 二连会变成 2+1=3 下，做不到"再使用一次二连"。
        ///
        /// 改成放大「连击段数」：游戏算一套行动打几下，看的就是
        /// `GetSkillEffectCount()` = 武器 ContinuousAttackCount（二连＝2）+ 额外段数，
        /// 而它全场只有三个调用点（都在攻击方跑行动的时候：Run 的无骰循环、
        /// DiceCheckProcess 的主手/副手循环）。所以这里直接把它 ×2：
        ///   二连 → 4 段（＝二连再来一遍）、横扫 → 2 遍（＝横扫再来一遍）、
        ///   三连 → 6 段，副手那一轮也照样翻倍；远程武器不碰。
        /// 每段都是游戏自己去掷骰、自己结算，所以"额外攻击不再触发此效果"是天然的（我们不改循环）。
        /// 唯一的取舍：段数在掷骰前就定了，所以**这一轮就算全miss，第二遍照样会挥出去**。
        /// </summary>
        internal static void OnQuerySkillEffectCount(BattleSkillData skill, bool isOffhand, ref int count)
        {
            try
            {
                if (skill == null || skill.Config == null || count <= 0)
                {
                    return;
                }
                // 葬花：行动前敏捷检定成功 → 本次攻击段数 +1（放在最前面，和灰暗孤影的翻倍互不影响）
                if (ZangHua.TryConsumeExtraSegment(skill, isOffhand))
                {
                    count += 1;
                    AttackTargetPlugin.LogInfo("葬花：本次攻击段数 +1 → " + count);
                }
                // 2026-09-22：灰暗孤影的"整套再来一遍"不再翻倍段数 —— 改用原版「战斗触发技能效果」
                // 在行动结束后重新执行一次（见 TryExtraAction）。这里只剩葬花的 +1 段。
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：连击段数翻倍出错：" + e);
            }
        }

        /// <summary>
        /// 把"独行"条件对所有友方复核一遍。
        /// 判定时机：每回合开始（RoundStart / BeforeRoundStart 保底）＋
        /// 队友倒下、昏迷、被魅惑、入队离队、任何 buff 变化时立刻再看一次。
        /// </summary>
        private static void RefreshLoneBonusForAllies()
        {
            try
            {
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;
                }
                List<BattleRole> allies = BattleHelper.FightContent.Allies;
                if (allies == null)
                {
                    return;
                }
                for (int i = 0; i < allies.Count; i++)
                {
                    RefreshLoneBonus(allies[i]);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：复核独行出错：" + e.Message);
            }
        }

        /// <summary>
        /// 效果 2：独行加成。2026-09-22 改成**梯度**（用户要求）：
        ///   · 除自己外 0 名友方存活 → 满额（880028：闪避+20 / 速度+20 / 物理伤害+50%）
        ///   · 除自己外 1 名友方存活 → 半额（880047：闪避+10 / 速度+10 / 物理伤害+25%）
        ///   · 2 名及以上 → 都不挂（增益归零，但**不会变成减益**）
        /// 判定时机不变：回合开始，外加队友状态变化时的即时复核。
        /// </summary>
        private static void RefreshLoneBonus(BattleRole role)
        {
            if (role == null || role.Data == null)
            {
                return;
            }
            if (!BattleHelper.IsInBattle)
            {
                // 数值只在战斗里生效，出战斗统一摘掉（走 RemoveLoneBonus）
                return;
            }
            bool awake = HasTraitWake(role, SecretIds.LoneShadowTrait);
            int mates = CountOtherAliveAllies(role);
            bool wantFull = awake && mates <= 0;
            bool wantHalf = awake && mates == 1;

            bool hasFull = role.HaveBuff(SecretIds.LoneBonusBuff);
            if (wantFull && !hasFull)
            {
                AddBuffAsync(role, SecretIds.LoneBonusBuff, role, "灰暗孤影：独行（满额，无队友）闪避+20 / 速度+20 / 物理伤害+50%");
            }
            else if (!wantFull && hasFull)
            {
                RemoveBuffAsync(role, SecretIds.LoneBonusBuff);
            }

            bool hasHalf = role.HaveBuff(SecretIds.LoneBonusHalfBuff);
            if (wantHalf && !hasHalf)
            {
                AddBuffAsync(role, SecretIds.LoneBonusHalfBuff, role, "灰暗孤影：独行（半额，1 名队友在场）闪避+10 / 速度+10 / 物理伤害+25%");
            }
            else if (!wantHalf && hasHalf)
            {
                RemoveBuffAsync(role, SecretIds.LoneBonusHalfBuff);
            }
        }

        private static void RemoveLoneBonus(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (role.HaveBuff(SecretIds.LoneBonusBuff))
                {
                    RemoveBuffAsync(role, SecretIds.LoneBonusBuff);
                }
                if (role.HaveBuff(SecretIds.LoneBonusHalfBuff))
                {
                    RemoveBuffAsync(role, SecretIds.LoneBonusHalfBuff);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：收回独行加成出错：" + e.Message);
            }
        }

        /// <summary>
        /// 除自己外，还有几名"能行动的"队友（死亡、昏迷、被魅惑的都不算）。
        /// 0 = 真正独行（满额加成）；1 = 半额；≥2 = 无加成。
        /// </summary>
        private static int CountOtherAliveAllies(BattleRole role)
        {
            try
            {
                List<BattleRole> team;
                if (BattleHelper.IsInBattle && BattleHelper.FightContent != null)
                {
                    team = BattleHelper.FightContent.Allies;
                }
                else
                {
                    team = BattleHelper.GetHeroTeamRoleList();
                }
                if (team == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < team.Count; i++)
                {
                    BattleRole mate = team[i];
                    if (mate == null || mate == role)
                    {
                        continue;
                    }
                    if (mate.IsDeath || mate.IsUnableAct)
                    {
                        continue;
                    }
                    if (mate.HaveRoleTag(ERoleTag.BeCharmed))
                    {
                        continue;   // 被魅惑的队友不算队友
                    }
                    count++;
                }
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 效果 3：每 2 回合 1 次的法术伤害免疫。
        /// 游戏算伤害走的是同步方法 BattleHelper.CalculationDamage，在这里把它归零最干净。
        /// </summary>
        internal static bool TryBlockMagicDamage(DamageData damageData, DamageAdditionalData addData, BattleRole target)
        {
            try
            {
                if (damageData == null || target == null || target.Data == null)
                {
                    return false;
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return false;
                }
                // 2026-09-22：判定从「灰暗孤影」特质改成「百合花」装备（用户要求把这两个效果移植到装备上）
                if (!HasBaiHeHua(target))
                {
                    return false;
                }
                if (!IsMagicDamage(damageData, addData))
                {
                    return false;
                }
                int round = BattleHelper.FightContent.Round;
                int last;
                if (MagicImmuneRound.TryGetValue(target, out last) && round - last < 2)
                {
                    return false;   // 还在 2 回合的间隔里
                }
                MagicImmuneRound[target] = round;
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(target) + "」免疫了 1 次法术伤害（第 " + round + " 回合，冷却 2 回合）");
                return true;
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：法术免伤判定出错：" + e.Message);
                return false;
            }
        }

        private static bool IsMagicDamage(DamageData damageData, DamageAdditionalData addData)
        {
            if (damageData.DamageType == EDamageType.Magic)
            {
                return true;
            }
            return addData != null && addData.MagicId > 0;
        }

        /// <summary>
        /// 效果 4：每局限 1 次的免死。
        /// SetLiftState 是 async，但这里只做同步的事（塞免疫次数 + 异步挂状态），所以用 Prefix。
        /// 游戏自己的流程：ImmuDeathTable 里有他 → 消耗 1 次、血量拉回 1、不再判死。
        /// 之后「百合未谢」挂上去，状态自己的创建事件负责：报台词、血量设 50%、给 1 回合伤害免疫。
        /// </summary>
        internal static void OnBeforeDeath(BattleRole role, ref ELifeState state)
        {
            try
            {
                ZangHua.OnRoleDeath(role, state);   // 葬花：击杀敌人恢复 1 点充能
                if (state != ELifeState.Death)
                {
                    return;
                }
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;   // "战斗死亡时"才免
                }
                // 2026-09-22：免死也从「灰暗孤影」特质移到「百合花」装备上
                if (!HasBaiHeHua(role))
                {
                    return;
                }
                if (role.HaveBuff(SecretIds.UndyingBuff))
                {
                    return;   // 已经用掉这一局的机会
                }
                if (role.ImmuneFallCheck())
                {
                    return;   // 游戏自己有"免疫倒地"，让他先处理，别抢
                }
                BattleHelper.FightContent.AddImmuDeathData(role, 1);
                AttackTargetPlugin.LogInfo("百合花：「" + NameOf(role) + "」触发免死（每局 1 次）");
                AddBuffAsync(role, SecretIds.UndyingBuff, role, "百合花：挂上「百合未谢」");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：免死处理出错：" + e);
            }
        }

        // =====================================================================
        // 四·一、记忆的双剑（880030 / 状态 880031）
        //   1. 近战伤害 +2                  → CalculationDamage 的 Postfix
        //   2. 近战命中施加 1 层流血         → _GetAttackAdditionalEffect 里塞一个 Bleed（游戏自己会挂流血 buff）
        //   3. 闪避成功且带近战武器 → 回击   → AttackDodgeSuccess 触发点，调游戏自己的 StrickBackProcess（固定 1 次伤害）
        //   4. 副手近战武器不受惩罚骰        → DiceShowAndTriggerEffectProcess 开头抵掉那颗惩罚骰
        // =====================================================================

        /// <summary>这把武器算不算近战（"均可"的武器也能近战用，算在内）。</summary>
        private static bool IsMeleeWeapon(MOD_Dynamic_Item weapon)
        {
            try
            {
                if (weapon == null || weapon.Config == null || weapon.Config.Weapon == null)
                {
                    return false;
                }
                EAttackType type = weapon.Config.Weapon.AttackType;
                return type == EAttackType.Melee || type == EAttackType.Both;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasMeleeWeapon(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.Data.Weapons == null)
                {
                    return false;
                }
                for (int i = 0; i < role.Data.Weapons.Count; i++)
                {
                    if (IsMeleeWeapon(role.Data.Weapons[i]))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 效果 1（输出方）：近战伤害 +2 —— 算在每一次伤害实例上，包括回击那一下。
        /// 效果 5（承受方）：受到近战攻击时伤害 -1。
        /// </summary>
        internal static void OnDamageCalculated(DamageData damageData, BattleRole source, BattleRole target,
            DamageAdditionalData addData, ref int result)
        {
            try
            {
                if (result <= 0 || addData == null)
                {
                    return;
                }
                // 葬花：装备在副手时，受到的魔法伤害 -1（2026-09-23 用户要求）
                if (target != null && IsMagicDamage(damageData, addData))
                {
                    result = ZangHua.ReduceMagicDamage(target, result);
                    if (result <= 0)
                    {
                        return;
                    }
                }
                // 灰暗孤影的反击、荆棘的先发：2026-09-27 起统一改成"只造成武器基础伤害"，
                // 由 JingJi 在最终扣血入口（BattleRole.SetDamage）接管 —— 这里不再改数值。
                // 荆棘的挂层（普通攻击拆 3 次 = 3 层、先发 1 次 = 1 层）也一并挪到那边。

                // 花香（880049）：2026-09-27 用户口径 —— **不再提供伤害加成**，
                // 改成"每层让意志检定的成功区间 +5"，实现在 OnQueryWillDiceValue（GetDiceCheckValue 的 Postfix）。
                // 粉尘爆炸（2026-09-25 用户追加）：带着【百合花芳香】的目标被火焰伤害打中时，
                // 它和它的同阵营全体一起受 2×层数 的爆炸伤害，并移除它自己身上的芳香。
                if (target != null && damageData != null && damageData.AddAttrs != null
                    && damageData.AddAttrs.Contains(EDamageAttr.FireSource))
                {
                    int aromaLayer = LilyAromaLayer(target);
                    if (aromaLayer > 0)
                    {
                        DustExplosion(target, aromaLayer);
                    }
                }
                // 恍惚（2026-09-25 用户口径，替代原来没用的"魅惑时长 +100%"）：处于魅惑状态时，造成的伤害 -50%。
                if (source != null && result > 0 && source.GetBuff(SecretIds.VanillaCharmBuff) != null)
                {
                    int beforeDaze = result;
                    result = (result + 1) / 2;
                    AttackTargetPlugin.LogInfo("恍惚：「" + NameOf(source) + "」处于魅惑中，伤害减半 " + beforeDaze + " → " + result);
                }
                if (!addData.IsWeaponDamage || !IsMeleeWeapon(addData.Weapon))
                {
                    return;
                }
                if (source != null && HasTraitWake(source, SecretIds.MemoryTrait))
                {
                    result += 3;
                }
                if (target != null && HasTraitWake(target, SecretIds.MemoryTrait))
                {
                    result = Math.Max(0, result - 1);
                }
                // 葬花：目标每层剑痕 +10%，并在每次造成伤害时叠 1 层剑痕
                ZangHua.OnDamage(source, target, addData, ref result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("记忆的双剑：近战增减伤出错：" + e.Message);
            }
        }

        /// <summary>
        /// 同一次伤害实例只播一次斩击特效（伤害计算会被调用多次：预览、日志、结算…）。
        /// 注：2026-09-27 起剑痕挂层改由 `ZangHua.OnDamageLanded`（每段各挂），不再用它去重；
        /// 这个标记现在只服务于"整次攻击只播一次 Slash"。
        /// 键用弱引用，伤害数据被回收就自动清理。
        /// </summary>
        private static readonly ConditionalWeakTable<DamageAdditionalData, object> MarkDamageSeen =
            new ConditionalWeakTable<DamageAdditionalData, object>();

        internal static bool MarkAppliedThisDamage(DamageAdditionalData addData)
        {
            try
            {
                if (addData == null)
                {
                    return true;
                }
                if (MarkDamageSeen.TryGetValue(addData, out object _))
                {
                    return true;
                }
                MarkDamageSeen.Add(addData, new object());
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        internal static void LogDamageBonus(DamageAdditionalData addData, int layer, int bonus)
        {
            AttackTargetPlugin.LogInfo("葬花：目标有 " + layer + " 层剑痕 → 本次伤害 +" + bonus);
        }

        /// <summary>
        /// 效果 2：近战攻击命中时给目标 1 层流血。
        /// 直接往"附加攻击效果"表里塞一个 Bleed —— 游戏在结算伤害时会自己 `AddBuff(BLEED_BUFF_ID)`，
        /// 塞几个就是几层（原版锐器武器走的就是这条路）。
        /// 这个方法本身是同步的，Postfix 时机精确。
        /// </summary>
        internal static void OnAttackAdditionalEffect(BattleActiveBehaviorData data, EDiceResult result,
            MOD_Dynamic_Item weapon, List<EBattleAttackAddEffectType> effects)
        {
            try
            {
                if (data == null || data.Self == null || effects == null)
                {
                    return;
                }
                if (result.GetLevel() < EDiceResult.Success.GetLevel())
                {
                    return;   // 没命中不上流血
                }
                if (!IsMeleeWeapon(weapon))
                {
                    return;
                }
                if (!HasTraitWake(data.Self, SecretIds.MemoryTrait))
                {
                    return;
                }
                if (effects.Contains(EBattleAttackAddEffectType.Bleed))
                {
                    return;   // 武器自带流血就不重复塞（避免一层变两层）
                }
                effects.Add(EBattleAttackAddEffectType.Bleed);
                AttackTargetPlugin.LogInfo("记忆的双剑：「" + NameOf(data.Self) + "」近战命中 " + result + "，附加 1 层流血");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("记忆的双剑：上流血出错：" + e);
            }
        }

        /// <summary>
        /// 受到攻击且闪避成功时，用武器回击一次（**2026-09-22 用户要求：从「记忆的双剑」转给「灰暗孤影」**）。
        /// `EBuffTriggerType.AttackDodgeSuccess`（62）就是游戏"闪避成功"那一刻触发的节点
        /// （BattleDefenceData.Run 里，只在这次攻击没打中时才会走）。
        /// 回击直接调游戏自己的 `BattleHelper.StrickBackProcess`：它内部固定 attackCount = 1，
        /// 而且不走攻击行动那套循环，所以**不会触发灰暗孤影的追击**。
        /// </summary>
        private static void LoneShadowCounterAttack(BattleRole dodger)
        {
            try
            {
                if (dodger == null || dodger.Data == null)
                {
                    return;
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;
                }
                if (!HasTraitWake(dodger, SecretIds.LoneShadowTrait))
                {
                    return;
                }
                if (!HasMeleeWeapon(dodger))
                {
                    return;   // 没带近战武器就不回击
                }
                BattleRole attacker = BattleHelper.FightContent.CurActionRole;   // 正在出手的那个人
                if (attacker == null || attacker == dodger || attacker.IsDeath)
                {
                    return;
                }
                AttackTargetPlugin.LogInfo("灰暗孤影：「" + NameOf(dodger) + "」闪避成功，回击「" + NameOf(attacker) + "」");
                QueueCounter(dodger, attacker);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：回击判定出错：" + e);
            }
        }

        /// <summary>
        /// 回击前的起手缓冲（秒）。0 = 闪避一结束就砍出去（第一版的表现，会觉得"啪一下"很急）；
        /// 太小会跟敌人的行动演出叠在一起，太大拖节奏，0.2~0.4 比较自然。
        /// </summary>
        private const float CounterWindUpSeconds = 0.25f;

        /// <summary>排队中的回击。</summary>
        private class PendingCounter
        {
            internal BattleRole Dodger;
            internal BattleRole Attacker;
            internal float FireAt;
        }

        private static readonly List<PendingCounter> PendingCounters = new List<PendingCounter>();

        /// <summary>战斗演出那个"暂停开关"是不是我们按下的（按下的才由我们放开）。</summary>
        private static bool CounterPaused;
        private static BattleFightContent CounterPausedContent;

        /// <summary>
        /// 排一次回击（不立刻执行）。
        /// 为什么要这样：
        ///   1. 我们是在"闪避成功"那一刻触发的，但那时**敌人的行动还在演**（可能还有后续段数/收尾），
        ///      立刻砍出去会和敌人的演出叠在一起 —— 看起来就是"莫名其妙很快地反了一下"；
        ///   2. 游戏自己有个演出暂停开关 `BattleFightContent.BattlePerformPause`，
        ///      演出流程里一堆 `while (BattlePerformPause) await WaitForEndOfFrame();` 检查点。
        ///      我们把它按住（原本就是 true 的话不碰），等回击演完再放开 —— 回击就会插在"敌人打完"之后，
        ///      而不是从旁边飘出来。
        /// 用每帧调度（`Tick`）而不是 await 等待：一来插件里引那套协程等待 API 麻烦，
        /// 二来暂停期间游戏就是靠每帧检查点等的，我们在 Update 里照样能跑。
        /// </summary>
        private static void QueueCounter(BattleRole dodger, BattleRole attacker)
        {
            try
            {
                BattleFightContent content = BattleHelper.FightContent;
                if (content != null && !content.BattlePerformPause)
                {
                    content.BattlePerformPause = true;
                    CounterPaused = true;
                    CounterPausedContent = content;
                }
                PendingCounter pending = new PendingCounter();
                pending.Dodger = dodger;
                pending.Attacker = attacker;
                pending.FireAt = UnityEngine.Time.unscaledTime + CounterWindUpSeconds;
                PendingCounters.Add(pending);
                    AttackTargetPlugin.LogInfo("灰暗孤影：回击排队（" + CounterWindUpSeconds.ToString("0.##") + " 秒后出手）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：回击排队出错：" + e);
            }
        }

        /// <summary>每帧调用：到点就把排队的回击执行掉（顺手做超时兜底，免得把战斗卡在暂停状态）。</summary>
        private static void TickPendingCounters()
        {
            if (PendingCounters.Count == 0)
            {
                return;
            }
            try
            {
                float now = UnityEngine.Time.unscaledTime;
                for (int i = PendingCounters.Count - 1; i >= 0; i--)
                {
                    PendingCounter pending = PendingCounters[i];
                    bool timeout = now - pending.FireAt > 5f;
                    if (!timeout && now < pending.FireAt)
                    {
                        continue;
                    }
                    PendingCounters.RemoveAt(i);
                    if (timeout || pending.Dodger == null || pending.Attacker == null || pending.Dodger.IsDeath)
                    {
                        ReleaseCounterPause();
                        continue;
                    }
                    CounterRun(pending);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：回击调度出错：" + e.Message);
                PendingCounters.Clear();
                ReleaseCounterPause();
            }
        }

        private static async void CounterRun(PendingCounter pending)
        {
            try
            {
                SecretFx.Slash(pending.Dodger, pending.Attacker);   // 原版刃器命中特效 + 音效
                await BattleHelper.StrickBackProcess(pending.Dodger, pending.Attacker, true);
                // 2026-09-25 用户要求：反击除了打伤害，还给敌人挂 1 层【流血】。
                // 这里显式挂（不走武器的附加效果表），免得受"反击只跑一次伤害"的影响。
                if (pending.Attacker != null && !pending.Attacker.IsDeath && !pending.Dodger.IsDeath)
                {
                    await pending.Attacker.AddBuff(pending.Dodger, ZangHua.BleedBuffId);
                    AttackTargetPlugin.LogInfo("灰暗孤影：反击额外给「" + NameOf(pending.Attacker) + "」挂 1 层流血");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：回击出错：" + e);
            }
            finally
            {
                ReleaseCounterPause();
            }
        }

        /// <summary>放开我们按下的那个演出暂停开关（还有别的回击排队时先不放）。</summary>
        private static void ReleaseCounterPause()
        {
            try
            {
                if (!CounterPaused || CounterPausedContent == null)
                {
                    return;
                }
                if (PendingCounters.Count > 0)
                {
                    return;
                }
                CounterPausedContent.BattlePerformPause = false;
                CounterPaused = false;
                CounterPausedContent = null;
            }
            catch (Exception)
            {
                CounterPaused = false;
                CounterPausedContent = null;
            }
        }

        /// <summary>
        /// 效果 4：副手近战武器不受惩罚骰。
        /// 游戏是在 DiceShowAndTriggerEffectProcess 开头给副手硬加一颗惩罚骰的，
        /// 我们在它前面（Prefix）先补一颗奖励骰，两颗正好抵消，其他副手逻辑一概不动。
        /// </summary>
        internal static void OnOffhandDiceStart(BattleActiveBehaviorData data, bool isOffhandCheck)
        {
            try
            {
                if (!isOffhandCheck || data == null || data.Self == null || !BattleHelper.IsInBattle)
                {
                    return;
                }
                if (data.RollDice == null || data.RollDice.CheckType == EDiceValueType.None)
                {
                    return;
                }
                MOD_Dynamic_Item weapon = data.BattleSkillData != null ? data.BattleSkillData.OffHandWeapon : null;
                if (!IsMeleeWeapon(weapon))
                {
                    return;
                }
                if (!HasTraitWake(data.Self, SecretIds.MemoryTrait))
                {
                    return;
                }
                data.RollDice.TmpRewardDice += 1;
                AttackTargetPlugin.LogInfo("记忆的双剑：副手近战武器免掉那颗惩罚骰（「" + NameOf(data.Self) + "」）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("记忆的双剑：副手惩罚骰出错：" + e);
            }
        }

        // =====================================================================
        // 五、记忆的双剑·效果 6：可以单手装备双手近战武器（只占 1 个槽）
        //   游戏对"双手武器"的判定只有一个入口：`MOD_Dynamic_Item.IsNeedBoth`
        //   （= `Config.Weapon.DoubleWeapon`，反编译 167395）。而它会被下面这一串逻辑共用：
        //     · 装备流程        RoleData.EquipWeapon / RoleData.InstallConfigEquipAndWeapons
        //       （双手武器的"清空所有槽只留 [0]"和"装单手武器时先卸掉双手武器"都在那里的 IsNeedBoth 分支里）
        //     · 能不能卸副手    RoleData.CheckWeaponCanUnequip（"主手不可卸下的双手武器会锁住副手"那条）
        //     · 装备界面显示    UIChangeEquipPanel / UIItemManagementPanel / UICharacterDetailPanel
        //       （双手武器会把第二格复制一份并盖上遮罩）
        //   → 所以只改这一处 getter 就够了：对"持有 880030 且特质苏醒"的角色，
        //     近战双手武器一律按单手武器算。上面那一串行为会自动全部跟上，
        //     一件 UI 代码都不用动。
        //   · 只认近战（Melee / Both）；双手远程武器（枪）照旧占两格。
        //   · 效果随特质苏醒，和这个特质的其它条目一个口径（沉睡时恢复原版"双手占两格"）。
        //   · 物品的归属角色 = `MOD_Dynamic_Item.SourceRole`（拿物品时 AddItem 会写，读档 Reload 也会写）。
        //     但读档那一批是 ItemFactory 新建的实例、可能还没绑归属，所以再挂一个
        //     RoleData.EquipWeapon 的前缀把归属补上（只在为空时补，不抢游戏自己写的值）。
        // =====================================================================

        /// <summary>这个物品算谁的东西（查不到就返回 null，当作没有这回事）。</summary>
        private static BattleRole FindItemOwner(MOD_Dynamic_Item item)
        {
            try
            {
                if (item == null)
                {
                    return null;
                }
                if (item.SourceRole != null)
                {
                    return item.SourceRole;
                }
                // 还没绑归属（工厂新建的实例）：去队伍里按引用找一遍，找到就顺手绑上
                BattleRole owner = FindOwnerIn(BattleHelper.GetHeroTeamRoleList(), item);
                if (owner == null)
                {
                    owner = FindOwnerIn(BattleHelper.GetHeroBackupTeamRoleList(), item);
                }
                if (owner != null)
                {
                    item.SourceRole = owner;
                }
                return owner;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BattleRole FindOwnerIn(List<BattleRole> roles, MOD_Dynamic_Item item)
        {
            if (roles == null)
            {
                return null;
            }
            for (int i = 0; i < roles.Count; i++)
            {
                BattleRole role = roles[i];
                if (role == null || role.Data == null)
                {
                    continue;
                }
                if (HoldsItem(role.Data.Weapons, item) || HoldsItem(role.Data.BagItems, item)
                    || HoldsItem(role.Data.Equips, item))
                {
                    return role;
                }
            }
            return null;
        }

        /// <summary>列表里有没有这一件（按引用认，不按 Id 认）。</summary>
        private static bool HoldsItem(List<MOD_Dynamic_Item> list, MOD_Dynamic_Item item)
        {
            if (list == null)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 效果 6：双手近战武器对持有者只算单手（占 1 个槽）。
        /// 挂在 IsNeedBoth 的 getter 上：不是双手武器就立刻放行（绝大多数物品走这条，开销可以忽略）。
        /// </summary>
        internal static void OnQueryIsNeedBoth(MOD_Dynamic_Item item, ref bool result)
        {
            if (!result)
            {
                return;
            }
            try
            {
                if (!IsMeleeWeapon(item))
                {
                    return;   // 双手远程武器（枪）不享受
                }
                BattleRole owner = FindItemOwner(item);
                if (owner == null || !HasTraitWake(owner, SecretIds.MemoryTrait))
                {
                    return;
                }
                result = false;
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("记忆的双剑：单手拿双手武器的判定出错：" + e.Message);
            }
        }

        /// <summary>
        /// 装备前把物品的归属补上（只补空的）。读档时这一批物品是 ItemFactory 新建的，
        /// SourceRole 是空的 —— 不补的话 IsNeedBoth 认不出它属于谁，双手武器又会顶掉副手武器。
        /// </summary>
        internal static void OnBeforeEquipWeapon(RoleData data, MOD_Dynamic_Item item)
        {
            try
            {
                if (item != null && item.SourceRole == null && data != null)
                {
                    item.SourceRole = data.Role;
                }
            }
            catch (Exception)
            {
            }
        }

        // =====================================================================
        // 六、多段攻击的"段间缓冲"：每一段演完，再进下一段（2026-09-21 加）
        //   现象：灰暗孤影把近战攻击整套翻倍后（2 段 → 4 段），第一段的挥砍还没看清，
        //         下一段（甚至下下一段）就出来了，打起来"噼里啪啦"。
        //   原因（反编译确认，不是我们改坏的）：武器检定类型是"逐个检定"（OneByOne，蔷薇黑剑就是）
        //         时，那段分支里的伤害演出**不等**：
        //             ActionEffectProcess(...)                                  ← 没有 await，演出自己跑
        //             while (curFinishCount < totalCount) await WaitForEndOfFrame();
        //         而这个计数器是靠"受击回调"推进的，受击发生在演出开始后 0.15 秒 ——
        //         于是整段 0.8 秒的演出才播了个头，流程就已经走到下一段（下一段的骰子动画压上来）。
        //         以前 2 段时，多出来的那 0.6 秒正好被下一段的骰子动画盖住，看不出来；
        //         段数一多（灰暗孤影 ×2，再加副手那一轮）就明显了。
        //   做法：每段伤害演出开始时，把游戏自己的演出暂停开关 `BattleFightContent.BattlePerformPause`
        //         按住 `SegmentHoldSeconds` 秒（默认 0.8 = 通用演出的整段时长）。
        //         游戏会在"本段末尾"那个 `while (BattlePerformPause) await WaitForEndOfFrame();`
        //         检查点上等我们放开 —— 下一段自然就排在上一段演完之后。
        //         （和"闪避回击"用的是同一个开关，那套已经验证过不会自锁；这里同样带超时兜底。）
        //   只对"持有灰暗孤影且苏醒"的角色、且只在多段武器攻击时出手；别的角色、单段攻击不受影响。
        //   想调快慢/关掉就改 `SegmentHoldSeconds`（0 = 关闭）。
        // =====================================================================

        /// <summary>每段演出要按住多久（秒）。0 = 关掉段间缓冲。</summary>
        internal const float SegmentHoldSeconds = 0.8f;

        private static bool SegmentHoldActive;
        private static float SegmentHoldStartedAt;
        private static BattleFightContent SegmentHoldContent;
        private static bool SegmentHoldLogged;

        /// <summary>每段伤害演出开始时调用：把演出暂停开关按住一小会儿（只在多段近战攻击时）。</summary>
        internal static void OnDamagePerformStart(BattleActiveBehaviorData data, bool isOffhandEffect)
        {
            try
            {
                if (SegmentHoldSeconds <= 0f || SegmentHoldActive)
                {
                    return;   // 关着，或者上一下还按着（同一段里连着的几次演出调用只按住一次）
                }
                if (!BattleHelper.IsInBattle || BattleHelper.FightContent == null || data == null || data.Self == null)
                {
                    return;
                }
                BattleSkillData skill = data.BattleSkillData;
                if (skill == null || skill.Config == null || !skill.Config.NeedDamage || !skill.Config.UseWeaponDamage)
                {
                    return;   // 只认"用武器造成伤害"的攻击
                }
                if (!HasTraitWake(data.Self, SecretIds.LoneShadowTrait))
                {
                    return;   // 只对持有灰暗孤影的人（这套翻倍是我们加的，节奏也由我们补上）
                }
                // 直接用武器自己的连击段数判断 —— 不要调 skill.GetSkillEffectCount()：
                // 那个方法被我们打了"灰暗孤影翻倍"的补丁，每调一次都会多写一条日志。
                MOD_Dynamic_Item weapon = isOffhandEffect ? skill.OffHandWeapon : skill.MasterHandWeapon;
                if (weapon == null || weapon.Config == null || weapon.Config.Weapon == null)
                {
                    return;
                }
                int segments = weapon.Config.Weapon.ContinuousAttackCount + weapon.ExtraContinuousAttackCount.Value;
                if (segments <= 1)
                {
                    return;   // 单段不用管
                }
                BattleFightContent content = BattleHelper.FightContent;
                if (content.BattlePerformPause)
                {
                    return;   // 游戏/玩家自己按着（战斗日志、回击排队等），别抢
                }
                content.BattlePerformPause = true;
                SegmentHoldActive = true;
                SegmentHoldContent = content;
                SegmentHoldStartedAt = UnityEngine.Time.unscaledTime;
                if (!SegmentHoldLogged)
                {
                    SegmentHoldLogged = true;
                    AttackTargetPlugin.LogInfo("灰暗孤影：多段攻击加了段间缓冲（每段等演出演完再进下一段，约 " +
                        SegmentHoldSeconds.ToString("0.##") + " 秒）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("灰暗孤影：段间缓冲出错：" + e.Message);
            }
        }

        /// <summary>
         /// 每帧调用：到点就把段间缓冲放开。
         /// 同时也兜住"时间被重置"这种情况（elapsed 变负就立刻放开，免得把战斗卡在暂停里）。
         /// </summary>
        private static void TickSegmentHold()
        {
            if (!SegmentHoldActive)
            {
                return;
            }
            try
            {
                float elapsed = UnityEngine.Time.unscaledTime - SegmentHoldStartedAt;
                if (elapsed >= 0f && elapsed < SegmentHoldSeconds)
                {
                    return;   // 还没到点
                }
                SegmentHoldActive = false;
                BattleFightContent content = SegmentHoldContent;
                SegmentHoldContent = null;
                if (content != null)
                {
                    content.BattlePerformPause = false;
                }
            }
            catch (Exception e)
            {
                SegmentHoldActive = false;
                SegmentHoldContent = null;
                AttackTargetPlugin.LogError("灰暗孤影：段间缓冲放开出错：" + e.Message);
            }
        }

        // =====================================================================
        // 四·二、这些私货特质"不算疯狂特质"（框还是疯狂框）
        //   游戏里 `Type == ETraitType.Crazy` 同时决定"疯狂框"和"疯狂特质"的各种逻辑，
        //   没办法只改一半；所以框保持疯狂（好看），逻辑上把它们从"疯狂特质"里摘出来：
        //     · 不参与【陷入疯狂】(buff 148) 的层数计算（原文：每 3 个疯狂特质 1 层，每层所有检定 +1 惩罚骰）
        //     · 不参与"10 个疯狂特质"成就
        //   其余（幕间不出现转换项、不进随机疯狂切换池）本来就因为疯狂特质被排除，正好合意。
        // =====================================================================

        internal static bool IsOurSecretTrait(int traitId)
        {
            return traitId == SecretIds.LilyTrait
                || traitId == SecretIds.WhiteHairTrait
                || traitId == SecretIds.LoneShadowTrait
                || traitId == SecretIds.MemoryTrait;
        }

        /// <summary>接过 TraitHelper.UpdateCrazyBuffLayerByWakeCrazyTraitCount：算法照抄，只是把我们的特质跳过。</summary>
        internal static bool ReplaceCrazyBuffUpdate(BattleRole role, ref Task result)
        {
            result = UpdateCrazyBuffSkippingOurs(role);
            return false;
        }

        private static async Task UpdateCrazyBuffSkippingOurs(BattleRole role)
        {
            if (role == null || role.Data == null)
            {
                return;
            }
            await role.RemoveBuff(GameConstants.CRAZY_BUFF_ID, showChangeTip: false);
            if (BattleHelper.IsInHall)
            {
                return;
            }
            int awakeCrazy = 0;
            List<MOD_Dynamic_Trait> traits = role.Data.Traits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    MOD_Dynamic_Trait trait = traits[i];
                    if (trait == null || !trait.IsCrazyTrait || trait.CurrentState != ETraitState.Wake)
                    {
                        continue;
                    }
                    if (IsOurSecretTrait(trait.Id))
                    {
                        continue;   // 私货特质不算疯狂
                    }
                    awakeCrazy++;
                }
            }
            int layers = 0;
            if (awakeCrazy >= 9)
            {
                layers = 3;
            }
            else if (awakeCrazy >= 6)
            {
                layers = 2;
            }
            else if (awakeCrazy >= 3)
            {
                layers = 1;
            }
            for (int i = 0; i < layers; i++)
            {
                await role.AddBuff(role, GameConstants.CRAZY_BUFF_ID);
            }
        }

        /// <summary>接过 TraitHelper.CheckCrazyTraitWakeAchievements：同样把我们的特质跳过。</summary>
        internal static bool ReplaceCrazyAchievement(RoleData data)
        {
            try
            {
                if (data == null || data.RoleType != ERoleType.Investigator || data.Traits == null)
                {
                    return true;
                }
                int awakeCrazy = 0;
                for (int i = 0; i < data.Traits.Count; i++)
                {
                    MOD_Dynamic_Trait trait = data.Traits[i];
                    if (trait == null || !trait.IsCrazyTrait || trait.CurrentState != ETraitState.Wake)
                    {
                        continue;
                    }
                    if (IsOurSecretTrait(trait.Id))
                    {
                        continue;
                    }
                    awakeCrazy++;
                }
                if (awakeCrazy < 10)
                {
                    return true;
                }
                GameAchievementTableData achievement = Singleton<ResManager>.Instance.GameAchievementList
                    .Find((GameAchievementTableData o) => o.Id == GameConstants.WAKE_CRAZY_TRAIT_COUNT_10);
                if (achievement != null && MonoSingleton<SteamRuntimeManager>.HasInstance
                    && MonoSingleton<SteamRuntimeManager>.Instance.SteamManagerInitialized)
                {
                    MonoSingleton<SteamRuntimeManager>.Instance.UnlockAchievement(achievement.SteamId);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("疯狂特质成就判定出错：" + e.Message);
            }
            return true;
        }

        // =====================================================================
        // 五、模组边界：收回「百合未谢」
        // =====================================================================

        private static Task OnGamePlayEvent(int eventId, params object[] objs)
        {
            try
            {
                if (eventId == (int)EGameEvent_GamePlay.EnterModule || eventId == (int)EGameEvent_GamePlay.FinishModule)
                {
                    AttackTargetPlugin.LogInfo("私货特质：进入/离开模组，收回所有调查员身上的「百合未谢」");
                    RemoveUndyingEverywhere();
                    ClearFlowerScentEverywhere();   // 花香：副本边界清空（2026-09-27 用户口径）
                    // 进模组时顺手把随特质苏醒的两个数值状态对齐一次
                    EnsureAllRolesTraitStatuses();
                    if (eventId == (int)EGameEvent_GamePlay.EnterModule)
                    {
                        ZangHua.EnsureWeaponForTeam();   // 葬花：进入副本时就把武器发到持有者手里
                        JingJi.EnsureWeaponForTeam();    // 荆棘：同上（两把剑一起给）
                        EnsureBaiHeHuaForTeam();         // 百合花：给持有百合花特质的人发装备并装上（不可卸下）
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质：模组边界处理出错：" + e.Message);
            }
            return Task.CompletedTask;
        }

        private static void RemoveUndyingEverywhere()
        {
            ForEachKnownRole(RemoveUndying);
        }

        /// <summary>花香：把全队（主角队 + 后备队）身上的层数清空 —— 进入副本 / 副本结束时用。</summary>
        private static void ClearFlowerScentEverywhere()
        {
            ForEachKnownRole(ClearFlowerScent);
        }

        private static void RemoveUndying(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (role.HaveBuff(SecretIds.UndyingBuff))
                {
                    RemoveBuffAsync(role, SecretIds.UndyingBuff);
                }
            }
            catch (Exception)
            {
            }
        }

        // ---- 挂/摘状态的小工具 ----

        private static async void AddBuffAsync(BattleRole role, int buffId, BattleRole source, string log)
        {
            try
            {
                await role.AddBuff(source, buffId);
                if (!string.IsNullOrEmpty(log))
                {
                    AttackTargetPlugin.LogInfo(log + "（" + NameOf(role) + "）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("挂状态 " + buffId + " 失败：" + e);
            }
        }

        private static async void RemoveBuffAsync(BattleRole role, int buffId)
        {
            try
            {
                await role.RemoveBuff(buffId);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("摘状态 " + buffId + " 失败：" + e);
            }
        }
    }

    // =====================================================================
    // 补丁
    // =====================================================================

    /// <summary>百合花：魅惑时长 ×2。必须用 Prefix（AddBuff 是 async）。</summary>
    [HarmonyPatch(typeof(BuffData), "AddBuff")]
    internal static class Patch_BuffData_AddBuff
    {
        /// <summary>拦下时交出去的"已完成任务"—— AddBuff 返回 Task，跳过就得自己给，不然 await 会对着 null 崩。</summary>
        private static readonly Task CompletedTask = Task.FromResult<object>(null);

        private static bool Prefix(BuffData __instance, BattleRole role, ref Task __result)
        {
            try
            {
                // 百合花环：免疫骨折 / 流血 / 中毒 / 燃烧 —— 在"要挂上去"这一刻直接拦掉
                if (SecretTraits.ShouldBlockBuff(__instance, role))
                {
                    __result = CompletedTask;
                    return false;
                }
                SecretTraits.OnBeforeBuffAdd(__instance, role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（AddBuff）：" + e);
            }
            return true;
        }
    }

    /// <summary>百合花：敌人带魅惑的行动优先指向持有者（改 AI 已经选好的目标）。</summary>
    [HarmonyPatch(typeof(BattleBaseAI), "AutoSelectTarget")]
    internal static class Patch_BattleBaseAI_AutoSelectTarget
    {
        private static void Postfix(BattleBaseAI __instance, List<BattleRole> roles)
        {
            try
            {
                SecretTraits.OnAiSelectTarget(__instance, roles);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（AI 指向）：" + e);
            }
        }
    }

    /// <summary>百合花：敌方魅惑行动的奖惩骰。选在骰子算完、还没投出去的那一刻。</summary>
    [HarmonyPatch(typeof(BattleActiveBehaviorData), "_UpdateRollDiceTmpDiceCount")]
    internal static class Patch_UpdateRollDiceTmpDiceCount
    {
        private static void Postfix(BattleActiveBehaviorData __instance, List<DiceResultData> targetDices,
            List<BattleRole> overrideTargets)
        {
            try
            {
                SecretTraits.OnBeforeDiceRoll(__instance, targetDices, overrideTargets);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（奖惩骰）：" + e);
            }
        }
    }

    /// <summary>灰暗孤影：近战武器攻击的连击段数 ×2（＝整套攻击行动再来一遍）。</summary>
    [HarmonyPatch(typeof(BattleSkillData), "GetSkillEffectCount")]
    internal static class Patch_GetSkillEffectCount
    {
        private static void Postfix(BattleSkillData __instance, bool isOffhand, ref int __result)
        {
            try
            {
                SecretTraits.OnQuerySkillEffectCount(__instance, isOffhand, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（连击段数）：" + e);
            }
        }
    }

    /// <summary>灰暗孤影：每 2 回合 1 次的法术伤害免疫（伤害直接归零）。</summary>
    [HarmonyPatch(typeof(BattleHelper), "CalculationDamage")]
    internal static class Patch_CalculationDamage
    {
        /// <summary>上一次调用是不是被"法术免伤"整段归零的（归零时不能再加近战伤害）。</summary>
        private static bool _blockedByMagicImmunity;

        /// <summary>上一次调用是不是被荆棘/孤影的特殊口径接管了（接管时不再补攻击侧效果）。</summary>
        private static bool _takenOverByJingJi;

        private static bool Prefix(DamageData damageData, BattleRole source, BattleRole target,
            DamageAdditionalData addData, ref int __result)
        {
            _takenOverByJingJi = false;
            try
            {
                if (JingJi.InGuardedCalc)
                {
                    // 我们自己的内层计算（只算防御侧 / 半破甲的第二遍）：原方法照跑，
                    // 但私货的附加处理一律不参与 —— 这里只要一个干净的数值。
                    _blockedByMagicImmunity = false;
                    return true;
                }
                if (SecretTraits.TryBlockMagicDamage(damageData, addData, target))
                {
                    __result = 0;
                    _blockedByMagicImmunity = true;
                    return false;
                }
                _blockedByMagicImmunity = false;
                JingJi.OnDamageCalculate(source, target, addData);   // 荆棘：每段倍率 / 主手加成 / 双手速度加成
                if (JingJi.TryBaseDamageWithDefense(damageData, source, target, addData, ref __result))
                {
                    _takenOverByJingJi = true;
                    return false;   // 先发 / 灰暗孤影的反击：只算防御侧，数值已由它算好
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（法术免伤）：" + e);
            }
            return true;
        }

        private static void Postfix(DamageData damageData, BattleRole source, BattleRole target,
            DamageAdditionalData addData, ref int __result)
        {
            bool blocked = _blockedByMagicImmunity;
            _blockedByMagicImmunity = false;
            bool takenOver = _takenOverByJingJi;
            _takenOverByJingJi = false;
            if (blocked || takenOver || JingJi.InGuardedCalc)
            {
                return;   // 归零 / 已被荆棘接管 / 内层计算：都不再补攻击侧效果
            }
            try
            {
                SecretTraits.OnDamageCalculated(damageData, source, target, addData, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（近战加伤）：" + e);
            }
        }
    }

    /// <summary>记忆的双剑：近战命中给目标挂 1 层流血（往附加效果表里塞 Bleed）。</summary>
    [HarmonyPatch(typeof(BattleActiveBehaviorData), "_GetAttackAdditionalEffect",
        new Type[] { typeof(EDiceResult), typeof(MOD_Dynamic_Item) })]
    internal static class Patch_GetAttackAdditionalEffect
    {
        private static void Postfix(BattleActiveBehaviorData __instance, EDiceResult diceResult,
            MOD_Dynamic_Item weapon, List<EBattleAttackAddEffectType> __result)
        {
            try
            {
                SecretTraits.OnAttackAdditionalEffect(__instance, diceResult, weapon, __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（附加流血）：" + e);
            }
        }
    }

    /// <summary>记忆的双剑：副手近战武器不受惩罚骰（在游戏加那颗惩罚骰之前先补一颗奖励骰抵消）。</summary>
    [HarmonyPatch(typeof(BattleActiveBehaviorData), "DiceShowAndTriggerEffectProcess",
        new Type[] { typeof(bool), typeof(bool) })]
    internal static class Patch_DiceShowAndTriggerEffectProcess
    {
        private static void Prefix(BattleActiveBehaviorData __instance, bool isOffhandCheck)
        {
            try
            {
                SecretTraits.OnOffhandDiceStart(__instance, isOffhandCheck);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（副手惩罚骰）：" + e);
            }
        }
    }

    /// <summary>私货特质不算疯狂特质：接管【陷入疯狂】的层数计算。</summary>
    [HarmonyPatch(typeof(TraitHelper), "UpdateCrazyBuffLayerByWakeCrazyTraitCount")]
    internal static class Patch_UpdateCrazyBuffLayer
    {
        private static bool Prefix(BattleRole role, ref Task __result)
        {
            try
            {
                return SecretTraits.ReplaceCrazyBuffUpdate(role, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（疯狂层数）：" + e);
                return true;
            }
        }
    }

    /// <summary>私货特质不算疯狂特质：成就判定同理跳过它们。</summary>
    [HarmonyPatch(typeof(TraitHelper), "CheckCrazyTraitWakeAchievements")]
    internal static class Patch_CheckCrazyTraitWakeAchievements
    {
        private static bool Prefix(RoleData role)
        {
            try
            {
                return SecretTraits.ReplaceCrazyAchievement(role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（疯狂成就）：" + e);
                return true;
            }
        }
    }

    /// <summary>灰暗孤影：每局 1 次的免死（把免疫次数塞进游戏自己的表）。</summary>
    [HarmonyPatch(typeof(BattleRole), "SetLiftState")]
    internal static class Patch_BattleRole_SetLiftState
    {
        private static void Prefix(BattleRole __instance, ref ELifeState state)
        {
            try
            {
                SecretTraits.OnBeforeDeath(__instance, ref state);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（免死）：" + e);
            }
        }
    }

    /// <summary>百合花：被魅惑期间，buff 触发的意志检定 +1 惩罚骰（魅惑自身的挣脱检定也走这条）。</summary>
    [HarmonyPatch(typeof(Buff_DiceCheckOption), "_GetDiceResultList",
        new Type[] { typeof(DiceCheckElementData), typeof(List<BattleRole>), typeof(bool) })]
    internal static class Patch_BuffDiceCheck_GetDiceResultList
    {
        private static void Postfix(DiceCheckElementData checkData, List<DiceResultData> __result)
        {
            try
            {
                SecretTraits.OnBuffDiceBuilt(checkData, __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（buff 意志检定）：" + e);
            }
        }
    }

    /// <summary>百合花：被魅惑期间，面板投骰的意志检定 +1 惩罚骰（探索 / 幕间 / 非战斗）。</summary>
    [HarmonyPatch(typeof(BattleHelper), "GetDiceData",
        new Type[] { typeof(BattleRole), typeof(EExploreSkill), typeof(EHeroAttribute),
                     typeof(ERoleExtraAttribute), typeof(bool) })]
    internal static class Patch_GetDiceData
    {
        private static void Postfix(CheckDiceData __result)
        {
            try
            {
                SecretTraits.OnExploreDiceBuilt(__result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（面板意志检定）：" + e);
            }
        }
    }

    /// <summary>
    /// 花香：意志检定 +5/层（2026-09-27 用户口径）。
    /// 所有"技能检定 / 属性检定 / buff 检定 / 探索面板检定"最终都会经过
    /// `BattleHelper.GetDiceCheckValue`，所以只在它这里加一次，天然不会重复。
    /// </summary>
    [HarmonyPatch(typeof(BattleHelper), "GetDiceCheckValue")]
    internal static class Patch_FlowerScent_DiceCheckValue
    {
        private static void Postfix(BattleRole target, EHeroAttribute attrType, ref int __result)
        {
            try
            {
                SecretTraits.OnQueryWillDiceValue(target, attrType, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("花香补丁出错（意志检定）：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 白毛少女：**力量检定 / 力量对抗**时，敏捷的一半一起参与（2026-09-27 用户口径，通用版）。
    /// 挂在 `GetDiceCheckValue` 上 —— 战斗技能、探索/面板、以及其它模组用标准配置做的
    /// 力量检定（`UseCheck.CheckAttr = STR`）都经过它。
    /// </summary>
    [HarmonyPatch(typeof(BattleHelper), "GetDiceCheckValue")]
    internal static class Patch_WhiteHair_StrengthDice
    {
        private static void Postfix(BattleRole target, EHeroAttribute attrType, ref int __result)
        {
            try
            {
                SecretTraits.OnQueryStrengthDiceValue(target, attrType, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("白毛少女补丁出错（力量检定）：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 花香：探索里每回合也减半（游戏探索时逐个角色调用 `TriggerEquipItemsEffect(ExploreRoundChange)`，
    /// 见反编译 `RoundChange()` / `TimeChangeInRuleModule()`）。
    /// </summary>
    [HarmonyPatch(typeof(BattleRole), "TriggerEquipItemsEffect",
        new Type[] { typeof(ESkillTriggerType), typeof(MOD_Dynamic_Item), typeof(MOD_Dynamic_Item) })]
    internal static class Patch_FlowerScent_ExploreRound
    {
        private static void Prefix(BattleRole __instance, ESkillTriggerType type)
        {
            try
            {
                SecretTraits.OnEquipTrigger(__instance, type);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("花香补丁出错（探索回合）：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 记忆的双剑：双手近战武器只占 1 个槽。
    /// 改的是 IsNeedBoth 这一个 getter —— 装备流程 / 卸下判定 / 三个装备界面读的都是它，
    /// 所以一处改动全套生效（详见 SecretTraits 里"五、"那一段的说明）。
    /// </summary>
    [HarmonyPatch(typeof(MOD_Dynamic_Item), "get_IsNeedBoth")]
    internal static class Patch_Item_IsNeedBoth
    {
        private static void Postfix(MOD_Dynamic_Item __instance, ref bool __result)
        {
            try
            {
                SecretTraits.OnQueryIsNeedBoth(__instance, ref __result);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（单手拿双手武器）：" + e);
            }
        }
    }

    /// <summary>
    /// 记忆的双剑（配套）：装备前把物品的归属角色补上。
    /// 读档时物品是 ItemFactory 新建的实例、SourceRole 为空，不补的话上面那条判定认不出主人。
    /// </summary>
    [HarmonyPatch(typeof(RoleData), "EquipWeapon")]
    internal static class Patch_RoleData_EquipWeapon
    {
        private static void Prefix(RoleData __instance, MOD_Dynamic_Item __0)
        {
            try
            {
                SecretTraits.OnBeforeEquipWeapon(__instance, __0);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（装备前补归属）：" + e);
            }
        }
    }

    /// <summary>
    /// 灰暗孤影：多段攻击的段间缓冲 ——
    /// 每段伤害演出开始时按住游戏自己的演出暂停开关，到点再放开，
    /// 让"上一段演完"成为"下一段开始"的前提。详见 SecretTraits "六、"那一段的说明。
    /// </summary>
    [HarmonyPatch(typeof(BattleActiveBehaviorData), "ActionEffectProcess")]
    internal static class Patch_ActionEffectProcess
    {
        private static void Prefix(BattleActiveBehaviorData __instance, bool isOffhandEffect)
        {
            try
            {
                SecretTraits.OnDamagePerformStart(__instance, isOffhandEffect);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特质补丁出错（段间缓冲）：" + e);
            }
        }
    }

    // =====================================================================
    // 百合花环：中毒 / 流血 / 燃烧 / 骨折 的**效果执行**全部跳过
    //   2026-09-23 用户改口径：这四个状态**可以挂上**（不然头环的"每回合解一个负面"没东西可解），
    //   但它们的**效果一律不生效**（不掉血、不削属性、不骨折）。
    //   为什么盯这三个类（从原版数据里挖出来的）：
    //     · Buff_ChangeAddOrReducePercentOption —— 中毒 102 用它削属性
    //     · Buff_DamageOption                    —— 流血 103 / 燃烧 104 用它回合扣血
    //     · Buff_FractureBuff                    —— 骨折 105
    //   注意这几个 OnAction 都返回 Task：跳过时必须自己塞 __result（第 10 节坑 6）。
    // =====================================================================

    [HarmonyPatch(typeof(Buff_DamageOption), "OnAction")]
    internal static class Patch_LilyWreath_BlockDamage
    {
        private static readonly Task CompletedTask = Task.FromResult<object>(null);

        private static bool Prefix(BuffData buff, BattleRole target, ref Task __result)
        {
            try
            {
                if (SecretTraits.ShouldNegateBuffEffect(buff, target))
                {
                    __result = CompletedTask;
                    return false;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Buff_ChangeAddOrReducePercentOption), "OnAction")]
    internal static class Patch_LilyWreath_BlockPercent
    {
        private static readonly Task CompletedTask = Task.FromResult<object>(null);

        private static bool Prefix(BuffData buff, BattleRole target, ref Task __result)
        {
            try
            {
                if (SecretTraits.ShouldNegateBuffEffect(buff, target))
                {
                    __result = CompletedTask;
                    return false;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Buff_FractureBuff), "OnAction")]
    internal static class Patch_LilyWreath_BlockFracture
    {
        private static readonly Task CompletedTask = Task.FromResult<object>(null);

        private static bool Prefix(BuffData buff, BattleRole target, ref Task __result)
        {
            try
            {
                if (SecretTraits.ShouldNegateBuffEffect(buff, target))
                {
                    __result = CompletedTask;
                    return false;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }
    }
}
