using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using Game.SkillData;
using GamePlayEvent;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.Events;

namespace ValennaBattle
{
    internal static partial class ValennaPrescience
    {
        private sealed class SpecialDodgeEffects
        {
            internal BattleActiveBehaviorData Action;
            internal readonly HashSet<BattleRole> Evaded = new HashSet<BattleRole>();
        }

        private static readonly AsyncLocal<SpecialDodgeEffects> SpecialEffects = new AsyncLocal<SpecialDodgeEffects>();

        private static bool IsThrowAction(BattleActiveBehaviorData action) => action.ActionType == EBattleActionType.Throw;

        private static bool IsHostileAction(BattleActiveBehaviorData action)
        {
            if (action.MagicData?.Config is { } magic)
                return magic.Type is not (EMagicType.Defence or EMagicType.Gain) &&
                    (magic.Type == EMagicType.Attack || magic.NeedDamage ||
                        action.TargetSelectType is ETargetSelect.Enemy_Single or ETargetSelect.Enemy_All or ETargetSelect.Enemy_Random);
            return IsThrowAction(action) || action.IsAttackAction || action.BattleSkillData?.Config?.NeedDamage == true;
        }

        [HarmonyPatch(typeof(DamageData), nameof(DamageData.Play),
            new[] { typeof(BattleRole), typeof(List<BattleRole>), typeof(EventModifier), typeof(bool), typeof(bool) })]
        private static class PreserveSpecialDodgeCallback
        {
            private static void Prefix(BattleRole __0, List<BattleRole> __1, ref EventModifier __2)
            {
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (effects == null || effects.Action.Self != __0 || __2 == null ||
                    __1?.Exists(effects.Evaded.Contains) != true) return;

                EventModifier callback = __2;
                __2 = async () =>
                {
                    SpecialDodgeEffects previous = SpecialEffects.Value;
                    SpecialEffects.Value = effects;
                    try { await callback(); }
                    finally { SpecialEffects.Value = previous; }
                };
            }
        }

        private static bool CanSpecialDodge(BattleRole defender, BattleRole attacker) =>
            BattleHelper.IsInBattle && !BattleHelper.GameOptionIsBreak &&
            attacker != null && CanDefend(defender) && defender.IsAlly != attacker.IsAlly &&
            !IsOverheated(defender) && defender.GetBuff(StrikeProfiles.EyeChargeId) is BuffData eye &&
            !eye.DisableBuff && eye.CurLayer >= StrikeProfiles.EyeUseCost;

        private static async Task<bool> RollSpecialDodge(BattleRole defender, BattleRole attacker)
        {
            if (!CanSpecialDodge(defender, attacker)) return false;

            var dice = new DiceResultData
            {
                Role = defender,
                CheckType = EDiceValueType.ExtraAttr,
                CheckId = (int)ERoleExtraAttribute.Dodge,
                OriginExtraAttr = ERoleExtraAttribute.Dodge
            };
            var state = new Defence { RollEyeReady = true, RollCharge = CheckBonus(defender), PendingRoll = dice };
            var detail = new CheckDetailAdditionalData();
            dice.CheckValue = Mathf.Clamp(BattleHelper.GetFinalCheckValue(defender, dice.CheckType, dice.CheckId, detail),
                dice.MinCheckValue, dice.MaxCheckValue);
            if (dice.CheckValue <= 0) return false;

            BuffData eye = defender.GetBuff(StrikeProfiles.EyeChargeId);
            int before = eye.CurLayer;
            state.RollReward = before <= StrikeProfiles.LowEyeCharge ? 1 : 2;
            await SpendEye(defender, eye);
            ValennaPlugin.Instance.Report($"预知眼 {before}→{Math.Max(eye.CurLayer, 0)} 层，特殊闪避 +{state.RollReward} 奖励骰");

            PrescienceDisplay display = PrescienceDisplay.Show(defender, attacker);
            state.Mark = display == null ? default : new MarkLifetime(display);
            DodgeRolls.Add(dice, state);
            BattleFightContent fight = BattleHelper.FightContent;
            bool evaded;
            try
            {
                await BattleHelper.UpdateDiceResultData(new List<DiceResultData> { dice });
                evaded = dice.Result.GetLevel() >= EDiceResult.Success.GetLevel();
                if (evaded && display != null) display.Confirm();
            }
            finally
            {
                state.ClearRoll();
                state.Mark.Fade();
            }

            if (!BattleHelper.IsInBattle || BattleHelper.GameOptionIsBreak ||
                !ReferenceEquals(fight, BattleHelper.FightContent)) return false;
            ValennaEventAudio.PlayDodge(defender, evaded);
            if (evaded)
            {
                await AcceleratedFuture.Grant(defender, state.RollEyeReady);
                await ValennaKit.RewardDodge(defender);
            }
            return evaded;
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.ActionEffectProcess))]
        private static class SpecialDodgeAction
        {
            [ThreadStatic]
            private static BattleActiveBehaviorData _resume;

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(BattleActiveBehaviorData __instance, EDiceResult __0, List<BattleRole> __1,
                bool __2, bool __3, UnityAction __4, bool __5, ParamTransmitionData __6, bool __7,
                List<DiceResultData> ____targetDices, ref Task __result)
            {
                if (ReferenceEquals(_resume, __instance))
                {
                    _resume = null;
                    return true;
                }

                if (__0.GetLevel() < EDiceResult.Success.GetLevel() || __1 == null ||
                    !Attacks.TryGetValue(__instance, out IncomingAttack watch) || !IsHostileAction(__instance) ||
                    __instance.BattleSkillData?.Id == StrikeProfiles.ExecutionId) return true;

                var candidates = new List<BattleRole>();
                foreach (BattleRole target in __1)
                {
                    if (candidates.Contains(target) || !CanSpecialDodge(target, __instance.Self) ||
                        (__instance.TargetSelectType == ETargetSelect.Enemy_Single &&
                            !target.CheckCanBeSelectedAsTarget(__instance.Self)) ||
                        (__instance.MagicData != null && target.HaveRoleTag(ERoleTag.MagicImmu)) ||
                        ____targetDices?.Exists(dice => dice?.Role == target && IsDodge(dice)) == true) continue;
                    candidates.Add(target);
                }

                if (candidates.Count == 0) return true;
                __result = ResolveAction(watch, __0, __1, candidates, __2, __3, __4, __5, __6, __7);
                return false;
            }

            private static async Task ResolveAction(IncomingAttack watch, EDiceResult result, List<BattleRole> targets,
                List<BattleRole> candidates, bool offhand, bool aoe, UnityAction callback, bool pursue,
                ParamTransmitionData data, bool ignorePursue)
            {
                BattleActiveBehaviorData action = watch.Action;
                var effects = new SpecialDodgeEffects { Action = action };
                foreach (BattleRole defender in candidates)
                {
                    watch.For(defender).BreakChain();
                    if (await RollSpecialDodge(defender, action.Self))
                        effects.Evaded.Add(defender);
                }

                Task resolution;
                SpecialDodgeEffects previous = SpecialEffects.Value;
                if (effects.Evaded.Count > 0) SpecialEffects.Value = effects;
                _resume = action;
                // 保留原版结算列表，发射扣弹、投掷消耗和施法者效果仍走原入口。
                try { resolution = action.ActionEffectProcess(result, targets, offhand, aoe, callback, pursue, data, ignorePursue); }
                finally
                {
                    _resume = null;
                    SpecialEffects.Value = previous;
                }
                await resolution;
            }
        }

        [HarmonyPatch(typeof(BattleHelper), nameof(BattleHelper.FreshDamageResult))]
        private static class PreserveSpecialDodgeDamageTargets
        {
            private static void Prefix(BattleRole __0, ref List<BattleRole> __1, DamageAdditionalData __3,
                out List<BattleRole> __state)
            {
                __state = null;
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (effects == null || effects.Action.Self != __0 || __3?.SourceType != EDamageSourceType.BattleSkill ||
                    __1?.Exists(effects.Evaded.Contains) != true) return;

                __state = __1.FindAll(effects.Evaded.Contains);
                __1 = __1.FindAll(role => !effects.Evaded.Contains(role));
            }

            private static void Postfix(List<DamageResultData> __4, List<BattleRole> __state, ref Task __result)
            {
                if (__state != null) __result = PreserveAfterCoverCheck(__result, __4, __state);
            }

            private static async Task PreserveAfterCoverCheck(Task original, List<DamageResultData> damage,
                List<BattleRole> evaded)
            {
                await original;
                foreach (BattleRole role in evaded)
                    damage.Add(new DamageResultData { Target = role, DamageValue = 0 });
            }
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.TriggerBuffs))]
        private static class FilterSpecialDodgeContactBuffs
        {
            private static bool Prefix(BattleRole __instance, EBuffTriggerType __0, object[] __1, ref Task __result)
            {
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (__0 != EBuffTriggerType.BattleBehaviorDamage || effects?.Action.Self != __instance ||
                    __1 == null || __1.Length == 0 || __1[0] is not BattleRole defender ||
                    !effects.Evaded.Contains(defender)) return true;

                __result = Task.CompletedTask;
                return false;
            }
        }

        [HarmonyPatch(typeof(BattleSkillBaseOption), nameof(BattleSkillBaseOption.GetTargets))]
        private static class FilterSpecialDodgeOptionTargets
        {
            private static void Postfix(BattleActiveBehaviorData __0, ref List<BattleRole> __result)
            {
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (effects != null && ReferenceEquals(effects.Action, __0) &&
                    __result?.Exists(effects.Evaded.Contains) == true)
                    __result = __result.FindAll(role => !effects.Evaded.Contains(role));
            }
        }

        [HarmonyPatch(typeof(ItemInfoData), nameof(ItemInfoData.Trigger))]
        private static class FilterSpecialDodgeThrowTargets
        {
            private static void Prefix(ItemInfoData __instance, BattleRole __2, ref List<BattleRole> __3)
            {
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (effects != null && effects.Action.Self == __2 &&
                    ReferenceEquals(effects.Action.BattleSkillData?.EffectItemInfo, __instance) &&
                    __3?.Exists(effects.Evaded.Contains) == true)
                    __3 = __3.FindAll(role => !effects.Evaded.Contains(role));
            }
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.OnHit))]
        private static class SpecialDodgeCounter
        {
            [ThreadStatic]
            private static BattleRole _resume;

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(BattleRole __instance, BattleRole __0, int __1, DamageData __2,
                DamageAdditionalData __3, DamageSourceRecord __4, BattleSkillEffectLogData __5, UnityAction __6,
                ref Task __result)
            {
                if (ReferenceEquals(_resume, __instance))
                {
                    _resume = null;
                    return true;
                }
                SpecialDodgeEffects effects = SpecialEffects.Value;
                if (effects != null && effects.Action.Self == __0 && __3?.SourceType == EDamageSourceType.BattleSkill &&
                    effects.Evaded.Contains(__instance))
                {
                    __6?.Invoke();
                    __result = Task.CompletedTask;
                    return false;
                }
                if (__3?.SourceType != EDamageSourceType.StrickBack || !CanSpecialDodge(__instance, __0)) return true;
                __result = ResolveCounter(__instance, __0, __1, __2, __3, __4, __5, __6);
                return false;
            }

            private static async Task ResolveCounter(BattleRole defender, BattleRole attacker, int damage,
                DamageData damageData, DamageAdditionalData additional, DamageSourceRecord source,
                BattleSkillEffectLogData log, UnityAction callback)
            {
                if (await RollSpecialDodge(defender, attacker))
                {
                    callback?.Invoke();
                    return;
                }

                Task resolution;
                _resume = defender;
                try { resolution = defender.OnHit(attacker, damage, damageData, additional, source, log, callback); }
                finally { _resume = null; }
                await resolution;
            }
        }
    }
}
