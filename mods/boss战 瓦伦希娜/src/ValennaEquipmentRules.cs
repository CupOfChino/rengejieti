using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using Game.SkillData;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    internal static partial class ValennaKit
    {
        internal const int ReloadId = 885151;
        internal const int PrescienceSkillId = 885152;
        internal const int DamageUpId = 885153;

        private sealed class EquipmentRound
        {
            internal int Round = -1;
            internal bool PrescienceReloaded;
            internal int BreathTriggers;
            internal int SanTriggers;
            internal int DamageUpGained;
        }

        private static readonly ConditionalWeakTable<BattleFightContent, Dictionary<BattleRole, EquipmentRound>> EquipmentFights =
            new ConditionalWeakTable<BattleFightContent, Dictionary<BattleRole, EquipmentRound>>();

        private static EquipmentRound EquipmentState(BattleRole role)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null) return null;

            Dictionary<BattleRole, EquipmentRound> roles = EquipmentFights.GetOrCreateValue(fight);
            if (!roles.TryGetValue(role, out EquipmentRound state))
                roles.Add(role, state = new EquipmentRound());
            if (state.Round != fight.Round)
            {
                state.Round = fight.Round;
                state.PrescienceReloaded = false;
                state.BreathTriggers = 0;
                state.DamageUpGained = 0;
            }
            return state;
        }

        internal static async Task<bool> SpendShot(BattleRole role, int count)
        {
            BuffData ammo = role.GetBuff(StrikeProfiles.AccelBulletId);
            if (count <= 0 || ammo == null || ammo.DisableBuff || ammo.CurLayer < count) return false;

            await ammo.ChangeLayer(role, -count);
            var shot = new DiceResultData
            {
                Role = role, CheckType = EDiceValueType.Skill, CheckId = (int)EExploreSkill.LongRange,
                OriginSkill = EExploreSkill.LongRange,
                CheckValue = BattleHelper.GetDiceCheckValue(role, EExploreSkill.LongRange)
            };
            await BattleHelper.UpdateDiceResultData(new List<DiceResultData> { shot });
            int before = Layers(role, StrikeProfiles.BreathId);
            if (shot.IsSuccess)
                await ValennaTremor.AddLayers(role, role, StrikeProfiles.BreathId, count, true, null);
            ValennaPlugin.Instance.Report($"加速弹：消耗{count}发，射击暗骰{shot.ResultValue}/{shot.CheckValue}（{shot.Result}），呼吸法{before}→{Layers(role, StrikeProfiles.BreathId)}");
            return shot.IsSuccess;
        }

        private static bool Wields(BattleRole source, int id)
        {
            var weapon = source.CurrentBehaviourData?.BattleSkillData?.CurrentWeapon;
            return weapon != null ? IsItem(weapon, id) :
                source.Data.Weapons?.Exists(item => IsItem(item, id)) == true;
        }

        internal static async Task ApplyShotHit(BattleRole source, BattleRole target)
        {
            if (!Wields(source, StrikeProfiles.GunbladeId) || !StrikeSequence.CanHit(target)) return;
            await ValennaTremor.AddLayers(source, target, StrikeProfiles.BurnId, 1, true, null);
            await target.AddBuff(source, StrikeProfiles.TremorId);
        }

        private static int Layers(BattleRole target, int id)
        {
            BuffData data = target.GetBuff(id);
            return data == null || data.DisableBuff ? 0 : Math.Max(0, data.CurLayer);
        }

        internal static int BoostBlade(BattleRole source, BattleRole target, int damage)
        {
            if (damage <= 0 || !Wields(source, StrikeProfiles.CutlassId)) return damage;
            int layers = (int)Math.Min(20L, (long)Layers(target, StrikeProfiles.BurnId) +
                ValennaTremor.CountLayers(target));
            int percent = layers * (target.HaveArmor() && target.GetArmorValue() > 0 ? 10 : 5);
            return (int)Math.Min(int.MaxValue, (damage * (100L + percent) + 50) / 100);
        }

        internal static void RewardBurst(BattleRole source)
        {
            if (!StrikeSequence.CanHit(source) || !Wields(source, StrikeProfiles.GunbladeId)) return;
            EquipmentRound state = EquipmentState(source);
            if (state == null || state.DamageUpGained >= 3) return;
            if (ValennaNextRoundBuffs.Queue(source, source, DamageUpId)) state.DamageUpGained++;

        }

        internal static async Task RewardDodge(BattleRole role)
        {
            EquipmentRound state = EquipmentState(role);
            if (state == null) return;

            int san = ValennaEyeEquipment.IsWorn(role) ? 1 : 0;
            if (ValennaEyeEquipment.HasPrescience(role))
            {
                if (state.SanTriggers < 2)
                {
                    state.SanTriggers++;
                    san++;
                }
                if (state.BreathTriggers < 2)
                {
                    state.BreathTriggers++;
                    // 这是预知技能的直接奖励，加速弹和装备效果不经此处。
                    await ValennaTremor.AddLayers(role, role, StrikeProfiles.BreathId,
                        ValennaHeart.SkillLayers(role, 1), true, null);
                }
            }
            if (san > 0)
                await role.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentSan, san.ToString()));
        }

        [HarmonyPatch(typeof(BattleHelper), nameof(BattleHelper.CalculationDamage))]
        private static class NativeBladeDamage
        {
            private static void Postfix(BattleRole __1, BattleRole __2, DamageAdditionalData __3, ref int __result)
            {
                if (!BattleHelper.IsInBattle || __1 == null || __2 == null || __3 is StrikeResolution.SegmentHit ||
                    __3?.IsWeaponDamage != true || !IsItem(__3.Weapon, StrikeProfiles.CutlassId) ||
                    StrikeSequence.Clips.ContainsKey(__1.CurrentBehaviourData?.BattleSkillData?.Id ?? 0)) return;
                __result = BoostBlade(__1, __2, __result);
            }
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.TriggerBuffs), new[] { typeof(EBuffTriggerType), typeof(object[]) })]
        private static class EquipTechniques
        {
            private static void Postfix(BattleRole __instance, EBuffTriggerType __0, ref Task __result)
            {
                if (__0 == EBuffTriggerType.BattleStart)
                    __result = Register(__result, __instance);
                else if (__0 == EBuffTriggerType.BattleEnd && __instance.Data?.BuffList != null &&
                    __instance.GetBuff(DamageUpId) != null)
                    __result = ClearBonus(__result, __instance);
            }

            private static async Task Register(Task original, BattleRole role)
            {
                await original;
                if (!StrikeSequence.CanHit(role)) return;
                if (ValennaPlugin.IsValennaPartner(role)) await AddTechnique(role, ReloadId);
                if (ValennaEyeEquipment.HasPrescience(role)) await AddTechnique(role, PrescienceSkillId);
            }

            private static async Task AddTechnique(BattleRole role, int id)
            {
                var skill = role.Data.BattleSkills.Find(item => item?.Id == id);
                if (skill == null) await role.AddBattleSkill(id, false, "valenna_techniques");
                else if (skill.Config == null) skill.Reload(role);
            }

            private static async Task ClearBonus(Task original, BattleRole role)
            {
                await original;
                await role.RemoveBuff(DamageUpId, showChangeTip: false);
            }
        }

        [HarmonyPatch(typeof(BattleFightContent), nameof(BattleFightContent.UninstallOnRestart))]
        private static class ResetRoundState
        {
            private static void Prefix(BattleFightContent __instance)
            {
                if (!EquipmentFights.TryGetValue(__instance, out Dictionary<BattleRole, EquipmentRound> roles))
                    return;

                // 重开绕过部分战斗结束事件，先清理角色面板保存的加成记录。
                foreach (BattleRole role in roles.Keys)
                {
                    BuffData bonus = role.GetBuff(DamageUpId);
                    if (bonus != null)
                        role.Data.CauseDamageChangePercentData.Remove(bonus.SourceKey);
                }
                EquipmentFights.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.RemoveBuff))]
        private static class RemoveDamageBonus
        {
            private static void Prefix(BuffData __instance, BattleRole __0)
            {
                if (__instance.Id == DamageUpId)
                    __0?.Data?.CauseDamageChangePercentData?.Remove(__instance.SourceKey);
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.TriggeOptionEffect),
            new[] { typeof(EBattleSkillTrigger), typeof(ParamTransmitionData) })]
        private static class ReloadSkill
        {
            private static void Postfix(BattleActiveBehaviorData __instance, EBattleSkillTrigger __0, ref Task __result)
            {
                if (__0 != EBattleSkillTrigger.DiceSuccess || !BattleHelper.IsInBattle ||
                    BattleHelper.GameOptionIsBreak) return;

                int id = __instance.BattleSkillData?.Id ?? 0;
                if (id == ReloadId || id == PrescienceSkillId)
                    __result = Reload(__result, __instance.Self, id);
            }

            private static async Task Reload(Task original, BattleRole role, int id)
            {
                BattleFightContent fight = BattleHelper.FightContent;
                await original;
                if (fight == null || !ReferenceEquals(fight, BattleHelper.FightContent) || BattleHelper.GameOptionIsBreak)
                    return;
                if (id == ReloadId)
                {
                    if (!ValennaPlugin.IsValennaPartner(role)) return;
                }
                else
                {
                    if (!ValennaEyeEquipment.HasPrescience(role)) return;
                    EquipmentRound state = EquipmentState(role);
                    if (state == null || state.PrescienceReloaded) return;
                    state.PrescienceReloaded = true;
                }
                await ValennaBattleStatus.StartAmmo(role);
                if (id == ReloadId && ReferenceEquals(fight, BattleHelper.FightContent) &&
                    !fight.IsFightOver && !BattleHelper.GameOptionIsBreak)
                    ValennaBattleScene.Play();
            }
        }
    }
}
