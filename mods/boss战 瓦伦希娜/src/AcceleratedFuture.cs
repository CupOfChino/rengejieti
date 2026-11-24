using System;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;

namespace ValennaBattle
{
    internal static class AcceleratedFuture
    {
        internal static async Task Grant(BattleRole role, bool startedWithEyeReady)
        {
            if (!startedWithEyeReady || !StrikeSequence.CanHit(role) || !ValennaEyeEquipment.IsWorn(role)) return;

            BuffData future = role.GetBuff(StrikeProfiles.AcceleratedFutureId);
            if (future == null)
                await role.AddBuff(role, StrikeProfiles.AcceleratedFutureId, showChangeTip: false);
            else if (future.CurLayer < StrikeProfiles.AcceleratedFutureMax)
                await future.ChangeLayer(role, 1);
        }

        internal static AttackUse BeginAttack(BattleActiveBehaviorData action)
        {
            if (!action.IsAttackAction || action.BattleSkillData?.Id == StrikeProfiles.RabbitId ||
                action.Self?.Data?.BuffList == null) return null;

            BuffData future = action.Self.GetBuff(StrikeProfiles.AcceleratedFutureId);
            return StackCount(future) > 0 ? new AttackUse(future) : null;
        }

        private static int StackCount(BuffData future) => future == null || future.DisableBuff ? 0 :
            Math.Max(0, Math.Min(future.CurLayer, StrikeProfiles.AcceleratedFutureMax));

        internal static int RewardDice(BattleRole role, bool magic)
        {
            if (!BattleHelper.IsInBattle || magic || role?.Data?.BuffList == null) return 0;
            return StackCount(role.GetBuff(StrikeProfiles.AcceleratedFutureId)) == StrikeProfiles.AcceleratedFutureMax ? 1 : 0;
        }

        private static int StatBonus(RoleData role, EExploreSkill skill)
        {
            if (skill is not (EExploreSkill.Combat or EExploreSkill.Motion) || role?.BuffList == null) return 0;
            BuffData future = role.BuffList.Find(buff => buff != null && buff.Id == StrikeProfiles.AcceleratedFutureId);
            return 2 * StackCount(future);
        }

        private static int WithStatBonus(RoleData role, EExploreSkill skill, int value, int bonus)
        {
            int max = role.GetSkillMaxValue(skill);
            return max > 0 ? Math.Min(max, value + bonus) : value + bonus;
        }

        internal sealed class AttackUse
        {
            private readonly BuffData _future;
            private readonly int _percent;
            private int _remainder;

            internal AttackUse(BuffData future)
            {
                _future = future;
                _percent = StrikeProfiles.AcceleratedFuturePercent * StackCount(future);
            }

            internal int Boost(int damage)
            {
                if (damage <= 0) return damage;

                // 同一招式累计小数，避免把每个 1 点伤害段都向上取整为 2 点。
                long scaled = (long)damage * _percent + _remainder;
                _remainder = (int)(scaled % 100);
                return (int)Math.Min(int.MaxValue, damage + scaled / 100);
            }

            internal async Task Consume(BattleRole role)
            {
                if (ReferenceEquals(role.GetBuff(StrikeProfiles.AcceleratedFutureId), _future))
                    await role.RemoveBuff(StrikeProfiles.AcceleratedFutureId, showChangeTip: false);
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.GetSkillValue), new[] { typeof(EExploreSkill) })]
        private static class SkillBonus
        {
            private static void Postfix(RoleData __instance, EExploreSkill __0, ref int __result)
            {
                int bonus = StatBonus(__instance, __0);
                if (bonus > 0) __result = WithStatBonus(__instance, __0, __result, bonus);
            }
        }

        [HarmonyPatch(typeof(BattleHelper), nameof(BattleHelper.GetFinalCheckDetailBaseValue))]
        private static class SkillDetailBonus
        {
            private static void Postfix(BattleRole __0, EDiceValueType __1, int __2, VariableData<int> __3,
                int __4, float __5, ref int __result)
            {
                if (__1 is not (EDiceValueType.Skill or EDiceValueType.Unite) || __4 > 0 || __3 == null) return;
                var skill = (EExploreSkill)__2;
                int bonus = StatBonus(__0?.Data, skill);
                if (bonus == 0) return;
                // 幸运攻击重算底值和检定说明直接从变量数据读取，未经过 GetSkillValue。
                int baseValue = BattleHelper.CalculateCheckDetailBaseValue(
                    BattleHelper.GetSourceDetailValueDatas(__0, __3), __3);
                __result = (int)(WithStatBonus(__0.Data, skill, baseValue, bonus) * __5);
            }
        }
    }
}
