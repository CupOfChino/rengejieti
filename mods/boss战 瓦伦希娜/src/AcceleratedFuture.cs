using System;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;

namespace ValennaBattle
{
    internal static class AcceleratedFuture
    {
        internal static async Task Grant(BattleRole role)
        {
            if (!StrikeSequence.CanHit(role) || !ValennaEyeEquipment.IsWorn(role) ||
                role.GetBuff(StrikeProfiles.EyeOverheatId) != null) return;

            BuffData future = role.GetBuff(StrikeProfiles.AcceleratedFutureId);
            if (future == null)
                await role.AddBuff(role, StrikeProfiles.AcceleratedFutureId, showChangeTip: false);
            else if (future.CurLayer < StrikeProfiles.AcceleratedFutureMax)
                await future.ChangeLayer(role, 1);
        }

        internal static AttackUse BeginAttack(BattleActiveBehaviorData action)
        {
            if (!action.IsAttackAction || action.BattleSkillData?.Id == StrikeProfiles.RabbitId ||
                !ValennaEyeEquipment.IsWorn(action.Self) || action.Self.Data.BuffList == null) return null;

            BuffData future = action.Self.GetBuff(StrikeProfiles.AcceleratedFutureId);
            return future == null || future.DisableBuff || future.CurLayer <= 0 ? null : new AttackUse(future);
        }

        internal sealed class AttackUse
        {
            private readonly BuffData _future;
            private readonly int _percent;
            private int _remainder;

            internal AttackUse(BuffData future)
            {
                _future = future;
                _percent = StrikeProfiles.AcceleratedFuturePercent *
                    Math.Min(future.CurLayer, StrikeProfiles.AcceleratedFutureMax);
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
                if ((__0 != EExploreSkill.Combat && __0 != EExploreSkill.Motion) ||
                    __instance.BuffList == null) return;

                BuffData future = __instance.BuffList.Find(buff => buff != null && buff.Id == StrikeProfiles.AcceleratedFutureId);
                if (future == null || future.DisableBuff) return;
                int bonus = 2 * Math.Max(0, Math.Min(future.CurLayer, StrikeProfiles.AcceleratedFutureMax));
                int max = __instance.GetSkillMaxValue(__0);
                __result = max > 0 ? Math.Min(max, __result + bonus) : __result + bonus;
            }
        }
    }
}
