using System;
using System.Threading.Tasks;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal static class ValennaBattleStatus
    {
        internal static async Task StartAmmo(BattleRole role)
        {
            BuffData ammo = role.GetBuff(StrikeProfiles.AccelBulletId);
            if (ammo == null)
                await role.AddBuff(role, StrikeProfiles.AccelBulletId, showChangeTip: false);
            else if (ammo.CurLayer < StrikeProfiles.MaxAccelBullets)
                await ammo.ChangeLayer(role, StrikeProfiles.MaxAccelBullets - ammo.CurLayer);
        }

        internal static async Task DecayBreath(BattleRole role)
        {
            BuffData breath = role.GetBuff(StrikeProfiles.BreathId);
            if (breath != null && breath.CurLayer > 0 && !breath.DisableBuff)
                await breath.ChangeLayer(role, -1);
        }

        internal static BuffData TryCritical(BattleRole source, DamageAdditionalData damageData,
            DamageSourceRecord record, int damage)
        {
            if (!BattleHelper.IsInBattle || damage <= 0 || damageData == null || damageData.IsCritical) return null;

            BattleActiveBehaviorData action = source.CurrentBehaviourData;
            bool attack = damageData.IsWeaponDamage || (record?.BattleSkill != null &&
                StrikeSequence.Clips.ContainsKey(record.BattleSkill.Id)) ||
                (damageData.SourceType == EDamageSourceType.BattleSkill &&
                 action?.IsRunning == true && action.IsAttackAction);
            if (!attack) return null;

            BuffData breath = source.GetBuff(StrikeProfiles.BreathId);
            if (breath == null || breath.DisableBuff || breath.CurLayer <= 0 ||
                UnityEngine.Random.Range(0, 100) >= Math.Min(breath.CurLayer, 20) * 5) return null;

            return breath;
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.ChangeLayer))]
        private static class KeepEmptyMagazine
        {
            private static bool Prefix(BuffData __instance, BattleRole __0, int __1, ref Task __result)
            {
                if (__instance.Id != StrikeProfiles.AccelBulletId || !BattleHelper.IsInBattle ||
                    __0 == null || __1 >= 0 || (long)__instance.CurLayer + __1 > 0) return true;

                __instance.CurLayer = 0;
                if (PrefabSingleton<UIBattlePanel>.HasInstance)
                    PrefabSingleton<UIBattlePanel>.Instance.UpdateRoleBuffShow(__0);
                __result = Task.CompletedTask;
                return false;
            }
        }
    }
}
