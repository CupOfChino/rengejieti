using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using Game.SkillData;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace AttackTargetVisualizer
{
    internal static class MoliCombat
    {
        private const int StealthBuff = 112;
        private static readonly ConditionalWeakTable<BattleRole, DodgeRound> Dodges =
            new ConditionalWeakTable<BattleRole, DodgeRound>();
        private static readonly ConditionalWeakTable<BattleActiveBehaviorData, HashSet<BattleRole>> MeleeHits =
            new ConditionalWeakTable<BattleActiveBehaviorData, HashSet<BattleRole>>();

        private sealed class DodgeRound
        {
            internal BattleFightContent Fight;
            internal int Round;
        }

        internal static void AddFirstDodgeBonus(List<DiceResultData> diceList)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null || diceList == null)
            {
                return;
            }
            foreach (DiceResultData dice in diceList)
            {
                BattleRole role = dice?.Role;
                if (role == null || role.IsUnableAct || !IsDodge(dice) || dice.IsNoRoll
                    || dice.FixedResult != EDiceResult.None || !role.HaveBuff(SecretIds.MemoryBuff))
                {
                    continue;
                }
                DodgeRound used = Dodges.GetOrCreateValue(role);
                if (used.Fight != fight || used.Round != fight.Round)
                {
                    used.Fight = fight;
                    used.Round = fight.Round;
                    dice.TmpRewardDice++;
                }
            }
        }

        private static bool IsDodge(DiceResultData dice)
        {
            return dice != null && dice.CheckType == EDiceValueType.ExtraAttr
                && dice.CheckId == (int)ERoleExtraAttribute.Dodge;
        }

        internal static async Task GiveStealthForDodge(BattleRole role)
        {
            DiceResultData dice = role.CurrentDefenceData?.RollDice;
            if (IsDodge(dice) && SecretTraits.HasTraitWake(role, SecretIds.LoneShadowTrait)
                && (dice.Result == EDiceResult.DiffSuc || dice.Result == EDiceResult.GreatSuccess)
                && !role.IsDeath)
            {
                await role.AddBuff(role, StealthBuff);
            }
        }

        internal static void BeginAction(BattleRole attacker)
        {
            if (attacker.CurrentBehaviourData != null)
            {
                MeleeHits.Remove(attacker.CurrentBehaviourData);
            }
        }

        internal static void RecordMeleeHit(BattleRole target, uint damage, RoleHitData hit)
        {
            DamageAdditionalData data = hit?.AddData;
            BattleActiveBehaviorData action = hit?.Attacker?.CurrentBehaviourData;
            if (damage == 0 || target == null || action == null || !action.IsRunning || data == null
                || data.SourceType != EDamageSourceType.BattleSkill || !data.IsWeaponDamage
                || !SecretTraits.IsMeleeWeapon(data.Weapon))
            {
                return;
            }
            MeleeHits.GetOrCreateValue(action).Add(target);
        }

        internal static async Task FinishMeleeAction(BattleRole attacker)
        {
            BattleActiveBehaviorData action = attacker.CurrentBehaviourData;
            if (action == null || !MeleeHits.TryGetValue(action, out HashSet<BattleRole> targets))
            {
                return;
            }
            MeleeHits.Remove(action);
            foreach (BattleRole target in targets)
            {
                if (target == null || target.Data == null || target.IsDeath)
                {
                    continue;
                }
                int damage = (ZangHua.MarkLayer(target) + 1) / 3;
                if (damage > 0)
                {
                    await ApplySwordMarkMagic(attacker, target, damage);
                }
            }
        }

        private static async Task ApplySwordMarkMagic(BattleRole attacker, BattleRole target, int damage)
        {
            var type = EDamageType.Magic;
            var data = new DamageData { DamageType = type };
            if (target.HaveImmunizationDamageType(type, attacker) || SecretTraits.TryBlockMagicDamage(data, null, target))
            {
                return;
            }

            float reduced = target.CalculateReducDamage(type, damage);
            var factors = target.Data.BeDamageChangePercentData?.DamageChangeElements;
            if (factors != null && target.Data.SystemAttribute?.DamageReductionEnabled.Value == true)
            {
                float multiplier = 1f;
                foreach (var factor in factors)
                {
                    if (factor.DamageTypes == null || !factor.DamageTypes.Contains(type))
                    {
                        continue;
                    }
                    if (factor.ChangeByValue && factor.ChangeValue < 0)
                    {
                        reduced += factor.ChangeValue;
                    }
                    else if (!factor.ChangeByValue && factor.Percent < 0f && factor.FloorDamageValue == 0)
                    {
                        multiplier *= 1f + factor.Percent;
                    }
                }
                foreach (var factor in factors)
                {
                    if (!factor.ChangeByValue && factor.FloorDamageValue < 0 && factor.Percent < 0f
                        && factor.DamageTypes != null && factor.DamageTypes.Contains(type))
                    {
                        reduced += Mathf.Min(reduced * factor.Percent, factor.FloorDamageValue);
                    }
                }
                reduced *= multiplier;
            }
            int final = ZangHua.ReduceMagicDamage(target, Mathf.Max(0, Mathf.CeilToInt(reduced)));
            if (final > 0)
            {
                AttackTargetPlugin.LogInfo("剑痕：近战行动结束，" + SecretTraits.NameOf(target) + "受到 " + final + " 点固定法术伤害");
                var hit = new RoleHitData(data, new DamageAdditionalData { SourceType = EDamageSourceType.Other },
                    final, 0, false) { Attacker = attacker, Target = target };
                await target.SetDamage((uint)final, hitData: hit);
                target.AddHitType(type);
            }
        }
    }

    [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.SetDamage),
        new Type[] { typeof(uint), typeof(bool), typeof(bool), typeof(RoleHitData) })]
    internal static class Patch_Moli_RecordMeleeHit
    {
        private static void Prefix(BattleRole __instance, uint damage, RoleHitData hitData)
        {
            MoliCombat.RecordMeleeHit(__instance, damage, hitData);
        }
    }

    [HarmonyPatch(typeof(RoleData), nameof(RoleData._ChangeAttr))]
    internal static class Patch_Moli_FlowerScent
    {
        private static void Prefix(RoleData __instance, bool IsAdd, ChangeAttrData attrData,
            ChangeInfoData changeInfo, out int __state)
        {
            __state = IsAdd && attrData?.RoleExAttr?.Type == ERoleExtraAttribute.CurrentHp
                && __instance.Role != null && __instance.Role.HaveBuff(SecretIds.FlowerScentBuff)
                && changeInfo != null ? changeInfo.Datas.Count : -1;
        }

        private static void Postfix(RoleData __instance, ChangeInfoData changeInfo, int __state, ref Task __result)
        {
            if (__state >= 0)
            {
                __result = ConsumeAfterLoss(__result, __instance.Role, changeInfo, __state);
            }
        }

        private static async Task ConsumeAfterLoss(Task original, BattleRole role, ChangeInfoData changeInfo, int priorCount)
        {
            await original;
            for (int i = priorCount; i < changeInfo.Datas.Count; i++)
            {
                if (changeInfo.Datas[i] is RoleExtraChange change && change.ExtraType == ERoleExtraAttribute.CurrentHp
                    && change.NextVaule < change.UpVaule)
                {
                    await SecretTraits.ConsumeFlowerScentOnDamage(role);
                    return;
                }
            }
        }
    }

    // 原版也允许技能和受击状态主动喊出反击。只标记闪避对抗实际调用的两处反击。
    [HarmonyPatch]
    internal static class Patch_Moli_DodgeCounter
    {
        private sealed class Counter
        {
            internal BattleRole Source;
            internal BattleRole Target;
        }

        private static readonly AsyncLocal<Counter> Current = new AsyncLocal<Counter>();

        internal static bool IsDodgeCounter(BattleRole source, BattleRole target)
        {
            Counter counter = Current.Value;
            return counter != null && counter.Source == source && counter.Target == target;
        }

        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(BattleActiveBehaviorData),
                nameof(BattleActiveBehaviorData.DiceShowAndTriggerEffectProcess));
            Type state = method.GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType;
            return AccessTools.Method(state, "MoveNext");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo originalCounter = AccessTools.Method(typeof(BattleHelper), nameof(BattleHelper.StrickBackProcess));
            MethodInfo trackCounter = AccessTools.Method(typeof(Patch_Moli_DodgeCounter), nameof(RunDodgeCounter));
            var code = new List<CodeInstruction>(instructions);
            List<CodeInstruction> calls = code.FindAll(instruction => instruction.Calls(originalCounter));
            if (calls.Count != 2)
            {
                throw new InvalidOperationException("找不到两处原版闪避反击入口");
            }
            foreach (CodeInstruction call in calls)
            {
                call.operand = trackCounter;
            }
            return code;
        }

        private static async Task RunDodgeCounter(BattleRole source, BattleRole target, bool melee)
        {
            Counter previous = Current.Value;
            Current.Value = new Counter { Source = source, Target = target };
            try
            {
                await BattleHelper.StrickBackProcess(source, target, melee);
            }
            finally
            {
                Current.Value = previous;
            }
        }
    }

    // 在原版得到精神变化量后修正它，动态表达式和骰值仍由游戏只算一次。
    [HarmonyPatch]
    internal static class Patch_Moli_ReduceSanLoss
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(RoleData), nameof(RoleData._ChangeAttr));
            Type state = method.GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType;
            return AccessTools.Method(state, "MoveNext");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            MethodInfo getValue = AccessTools.Method(typeof(RoleParamVariableData<ERoleExtraAttribute>), "GetValue");
            FieldInfo isAdd = AccessTools.Field(original.DeclaringType, "IsAdd");
            var code = new List<CodeInstruction>(instructions);
            int index = code.FindIndex(instruction => instruction.Calls(getValue));
            if (isAdd == null || index < 0 || index != code.FindLastIndex(instruction => instruction.Calls(getValue)))
            {
                throw new InvalidOperationException("找不到唯一的额外属性数值入口");
            }
            var loadState = new CodeInstruction(OpCodes.Ldarg_0);
            loadState.labels.AddRange(code[index].labels);
            code[index].labels.Clear();
            code[index].opcode = OpCodes.Call;
            code[index].operand = AccessTools.Method(typeof(Patch_Moli_ReduceSanLoss), nameof(GetAdjustedValue));
            code.Insert(index, loadState);
            code.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, isAdd));
            return code;
        }

        private static int GetAdjustedValue(RoleParamVariableData<ERoleExtraAttribute> attribute, RoleData role, bool isAdd)
        {
            int value = attribute.GetValue(role);
            return isAdd && value < 0 && attribute.Type == ERoleExtraAttribute.CurrentSan
                && SecretTraits.HasTraitWake(role.Role, SecretIds.LoneShadowTrait) ? value + 1 : value;
        }
    }
}
