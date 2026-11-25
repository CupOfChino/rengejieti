using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BattleSkillEvent;
using BepInEx;
using Game;
using Game.SkillData;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    [BepInDependency("codex.depersonal.xineditor", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("codex.depersonal.custombattlebg", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("ttrpg.valenna-battle", "boss战 瓦伦希娜", "0.5.8")]
    [BepInDependency("codex.depersonal.duoactionslot", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ValennaPlugin : BaseUnityPlugin
    {
        internal const int PartnerId = 885101;
        private const int HeartPreviewId = 885140;
        private const int CustomHeartId = 880901;
        private const string SkillSource = "valenna_palermo";

        internal static ValennaPlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            StrikeArt.Load();
            new Harmony("ttrpg.valenna-battle").PatchAll(typeof(ValennaPlugin).Assembly);
            Logger.LogInfo($"boss战 瓦伦希娜 {Info.Metadata.Version} 已载入：" + Info.Location);
        }

        private void Update()
        {
            ValennaEyeEquipment.ListenForModuleEntry();
            ValennaEventAudio.Update();
        }

        private void OnDestroy()
        {
            ValennaEyeEquipment.StopListening();
            ValennaEventAudio.Clear();
            StrikeArt.Clear();
        }

        internal void Report(Exception error) => Logger.LogError(error);
        internal void Report(string message) => Logger.LogInfo(message);
        internal void Warn(string message) => Logger.LogWarning(message);

        internal static bool IsValennaPartner(BattleRole role) => ValennaHeart.IsPreset(role?.Data);

        internal static bool HasPalermo(BattleRole role) =>
            role?.Data != null && role.GetTraitData(StrikeProfiles.TraitId)?.Config != null;

        private static bool IsRetired(int id) => id == 885122 || id == 885123 || id == 885130;

        [HarmonyPatch(typeof(UIBattlePanel), "_SetSkillSelectButtonElement")]
        private static class AttackMenu
        {
            private static void Prefix(List<SkillInfoElementData> __0, EBattleActionType __1)
            {
                if (__1 != EBattleActionType.Attack || __0 == null) return;

                bool hasTrait = HasPalermo(BattleHelper.BattleHeroInfo);
                // 这里拿到的是当前行动槽用于创建按钮的临时列表。
                __0.RemoveAll(entry =>
                {
                    int id = entry?.Skill?.Id ?? 0;
                    bool isExclusive = StrikeSequence.Clips.ContainsKey(id);
                    return hasTrait ? !isExclusive : isExclusive || IsRetired(id);
                });
            }
        }

        [HarmonyPatch(typeof(TraitHelper), nameof(TraitHelper.AddTrait),
            new[] { typeof(RoleData), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        private static class GrantPalermo
        {
            private static bool Prefix(RoleData __0, int __1, ref Task __result)
            {
                if (!ValennaKit.IsInitialTrait(__1) || __0?.Traits?.Exists(trait => trait?.Id == __1) != true)
                    return true;
                __result = Task.CompletedTask;
                return false;
            }

            private static void Postfix(RoleData __0, int __1, ref Task __result)
            {
                if (__1 == StrikeProfiles.TraitId && __0?.Role != null)
                    __result = BindAfterTrait(__result, __0.Role);
            }

            private static async Task BindAfterTrait(Task original, BattleRole role)
            {
                await original;
                if (HasPalermo(role) && BattleHelper.IsInBattle) await BindAttacks(role);
            }
        }

        private static async Task BindAttacks(BattleRole role)
        {
            foreach (int id in StrikeSequence.Clips.Keys)
            {
                BattleSkillData skill = role.Data.BattleSkills.Find(entry => entry?.Id == id);
                if (skill == null) await role.AddBattleSkill(id, false, SkillSource);
                else if (skill.Config == null) skill.Reload(role);
            }
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.TriggerBuffs), new[] { typeof(EBuffTriggerType), typeof(object[]) })]
        private static class BattleSetup
        {
            private static void Postfix(BattleRole __instance, EBuffTriggerType __0, ref Task __result)
            {
                if (__0 == EBuffTriggerType.BattleStart)
                {
                    ValennaAppearance.ApplyAllyFacing(__instance, __instance.IsAlly);
                    if (IsValennaPartner(__instance) || HasPalermo(__instance) ||
                        ValennaKit.HasStartingAmmo(__instance) ||
                        ValennaEyeEquipment.HasPrescience(__instance) || ValennaEyeEquipment.IsWorn(__instance))
                    {
                        ValennaEventAudio.Prepare();
                        __result = InitializeBattleRole(__result, __instance);
                    }
                }
                else if (__0 == EBuffTriggerType.BeforeRoundStart && BattleHelper.IsInBattle &&
                    (ValennaEyeEquipment.IsWorn(__instance) || HasPalermo(__instance)))
                {
                    __result = RestoreAfterRoundStart(__result, __instance);
                }
                else if (__0 == EBuffTriggerType.RoundEnd && BattleHelper.IsInBattle &&
                    __instance.Data?.BuffList?.Exists(buff => buff != null &&
                        (buff.Id == StrikeProfiles.BreathId || buff.Id == StrikeProfiles.TremorId ||
                         buff.Id == StrikeProfiles.ScorchTremorId ||
                         buff.Id == StrikeProfiles.StunnedId)) == true)
                {
                    __result = OnRoundEnded(__result, __instance);
                }
                else if (__0 == EBuffTriggerType.BattleEnd)
                {
                    if (__instance.Data?.BuffList != null &&
                        (__instance.GetBuff(HeartPreviewId) != null ||
                         __instance.GetBuff(StrikeProfiles.AcceleratedFutureId) != null ||
                         __instance.GetBuff(StrikeProfiles.AccelBulletId) != null ||
                         __instance.GetBuff(StrikeProfiles.HuntingPreyId) != null ||
                         __instance.GetBuff(StrikeProfiles.BreathId) != null ||
                         __instance.GetBuff(StrikeProfiles.TremorId) != null ||
                         __instance.GetBuff(StrikeProfiles.ScorchTremorId) != null ||
                         __instance.GetBuff(StrikeProfiles.StunnedId) != null))
                        __result = ClearBattleStatus(__result, __instance);
                }
            }

            private static async Task RestoreAfterRoundStart(Task original, BattleRole role)
            {
                await original;
                if (!StrikeSequence.CanHit(role)) return;
                await ValennaPrescience.RestoreEye(role);
                await ValennaHunt.BeforeRound(role);
            }

            private static async Task ClearBattleStatus(Task original, BattleRole role)
            {
                await original;
                await role.RemoveBuff(HeartPreviewId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.AcceleratedFutureId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.AccelBulletId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.HuntingPreyId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.BreathId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.TremorId, showChangeTip: false);
                await role.RemoveBuff(StrikeProfiles.ScorchTremorId, showChangeTip: false);
                await ValennaTremor.ClearBurstStun(role, endOfBattle: true);
            }

            private static async Task OnRoundEnded(Task original, BattleRole role)
            {
                await original;
                await ValennaBattleStatus.DecayBreath(role);
                await ValennaTremor.RoundEnd(role);
            }

            private static async Task InitializeBattleRole(Task original, BattleRole role)
            {
                await original;
                try
                {
                    await ValennaKit.UpgradePartner(role);
                    if (IsValennaPartner(role))
                    {
                        if (HasPalermo(role))
                            role.Data.BattleSkills.RemoveAll(skill => IsRetired(skill?.Id ?? 0));
                    }

                    await ValennaEyeEquipment.EnsureEquipped(role);
                    if (ValennaKit.HasStartingAmmo(role)) await ValennaBattleStatus.StartAmmo(role);
                    if (!HasPalermo(role)) return;
                    await BindAttacks(role);
                    await ValennaHunt.BattleStart(role);
                    int ready = role.Data.BattleSkills.Count(skill =>
                        skill?.Config != null && StrikeSequence.Clips.ContainsKey(skill.Id));
                    Instance.Report($"巴勒莫之剑攻击栏已准备 {ready}/{StrikeSequence.Clips.Count} 条");

                    if (!IsValennaPartner(role) || role.IsDeath || role.GetBuff(HeartPreviewId) != null)
                        return;

                    var factory = Singleton<ResManager>.Instance.BuffFactory;
                    var heart = factory.GetConfig(CustomHeartId);
                    if (heart?.UseFxPrefab?.UseFrameEffect != true ||
                        string.IsNullOrEmpty(heart.UseFxPrefab.EffectName)) return;
                    var preview = factory.GetData(HeartPreviewId);
                    if (preview?.Config == null) return;
                    if (role.GetBuff(CustomHeartId) != null)
                        await role.RemoveBuff(CustomHeartId, showChangeTip: false);
                    preview.Config.FxPlayType = heart.FxPlayType;
                    preview.Config.UseFxPrefab = heart.UseFxPrefab;
                    preview.Config.IconPathReference = heart.IconPathReference;
                    await role.AddBuff(role, preview, showChangeTip: false);
                }
                catch (Exception error)
                {
                    Instance.Report(error);
                }
            }
        }

        [HarmonyPatch(typeof(BattleFightContent), nameof(BattleFightContent.UninstallOnRestart))]
        private static class RestartPlayback
        {
            private static void Prefix(BattleFightContent __instance)
            {
                StrikeSequence.StopForRestart(__instance);
                ValennaEventAudio.Clear();
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.TriggeOptionEffect),
            new[] { typeof(EBattleSkillTrigger), typeof(ParamTransmitionData) })]
        private static class StrikePlayback
        {
            private sealed class ActionStrike
            {
                internal StrikeResolution Resolution;
                internal bool Played;
            }

            private static readonly ConditionalWeakTable<BattleActiveBehaviorData, ActionStrike> Prepared =
                new ConditionalWeakTable<BattleActiveBehaviorData, ActionStrike>();

            [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.ActionEffectProcess))]
            private static class PreserveResult
            {
                private static void Prefix(BattleActiveBehaviorData __instance, EDiceResult __0)
                {
                    if (Prepared.TryGetValue(__instance, out ActionStrike strike) && strike.Resolution != null)
                        strike.Resolution.Result = __0;
                }
            }

            [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.ClearOnActionEnd))]
            private static class ClearStrike
            {
                private static void Postfix(BattleActiveBehaviorData __instance, ref Task __result)
                {
                    if (!Prepared.TryGetValue(__instance, out ActionStrike strike)) return;
                    Prepared.Remove(__instance);
                    if (strike.Resolution != null)
                        __result = Finish(__result, strike.Resolution);
                }
            }

            private static void Postfix(BattleActiveBehaviorData __instance, EBattleSkillTrigger __0,
                ParamTransmitionData __1, ref Task __result)
            {
                BattleSkillData skill = __instance.BattleSkillData;
                if (BattleHelper.GameOptionIsBreak || !HasPalermo(__instance.Self) || skill?.Config == null ||
                    !StrikeSequence.Clips.TryGetValue(skill.Id, out StrikeShow show)) return;

                if (__0 == EBattleSkillTrigger.BeforeDiceCheck)
                {
                    __result = BeginStrikeAfterOriginal(__result, __instance);
                    return;
                }
                if (__0 != EBattleSkillTrigger.DiceSuccess && __0 != EBattleSkillTrigger.DiceDiffSuccess &&
                    __0 != EBattleSkillTrigger.DiceGreatSuccess) return;

                EDiceResult result = __0 switch
                {
                    EBattleSkillTrigger.DiceDiffSuccess => EDiceResult.DiffSuc,
                    EBattleSkillTrigger.DiceGreatSuccess => EDiceResult.GreatSuccess,
                    _ => EDiceResult.Success
                };
                __result = Play(__result, __instance, __1, show, result);
            }

            private static async Task BeginStrikeAfterOriginal(Task original, BattleActiveBehaviorData data)
            {
                await original;
                if (!BattleHelper.IsInBattle || BattleHelper.GameOptionIsBreak || !StrikeSequence.CanHit(data.Self) ||
                    Prepared.TryGetValue(data, out _)) return;
                int id = data.BattleSkillData.Id;
                BattleRole target = data.Targets?.FirstOrDefault(role => role != data.Self &&
                    StrikeSequence.CanHit(role) && !role.IsRemoved &&
                    (id != StrikeProfiles.ExecutionId || ValennaHunt.CanExecute(data.Self, role)));
                if (target == null) return;
                var resolution = new StrikeResolution(data.Self, target, data.BattleSkillData, EDiceResult.None);
                Prepared.Add(data, new ActionStrike { Resolution = resolution });
                await resolution.Begin();
            }

            private static async Task Finish(Task original, StrikeResolution resolution)
            {
                await original;
                await resolution.Finish();
            }

            private static async Task Play(Task original, BattleActiveBehaviorData data,
                ParamTransmitionData param, StrikeShow show, EDiceResult result)
            {
                await original;
                try
                {
                    if (!BattleHelper.IsInBattle || BattleHelper.GameOptionIsBreak || !HasPalermo(data.Self)) return;
                    List<BattleRole> targets = param?.SourceType == ParamTransmitionData.EDataSourceType.Target
                        ? param.ParamData as List<BattleRole>
                        : null;
                    if (targets == null)
                        targets = data.CurDiceCheckTargets?.Count > 0 ? data.CurDiceCheckTargets : data.Targets;
                    BattleRole target = targets?.FirstOrDefault(role => role != data.Self &&
                        StrikeSequence.CanHit(role) && !role.IsRemoved &&
                        (show.Id != StrikeProfiles.ExecutionId || ValennaHunt.CanExecute(data.Self, role)));
                    if (target == null)
                    {
                        Instance.Warn($"瓦伦希娜动作 {show.Id} 无有效目标");
                        return;
                    }
                    ActionStrike strike = Prepared.GetValue(data, _ => new ActionStrike());
                    if (strike.Played) return;
                    strike.Played = true;
                    Instance.Report($"瓦伦希娜动作 {show.Id}「{show.Name}」开始播片，共 {show.Cues.Length} 段");
                    StrikeResolution resolution = strike.Resolution;
                    if (resolution != null && resolution.Result != EDiceResult.None) result = resolution.Result;
                    await StrikeSequence.Play(data.Self, target, data.BattleSkillData, show, result, resolution);
                }
                catch (Exception error)
                {
                    Instance.Report(error);
                }
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), "_GetDiceResultList")]
        private static class AddDexterityToCombatCheck
        {
            private static void Postfix(BattleActiveBehaviorData __instance, DiceCheckElementData __0,
                bool __3, List<DiceResultData> __result)
            {
                if (__3 || __0 == null || __0.IsOverrideCheck || __result == null ||
                    __instance.BattleSkillData?.Id is not (885126 or 885128) ||
                    !HasPalermo(__instance.Self)) return;

                foreach (DiceResultData dice in __result)
                {
                    if (dice.Role?.Data == null || dice.CheckType != EDiceValueType.Skill) continue;
                    int combat = BattleHelper.GetSkillVaule(__0.Skill, dice.Role.Data);
                    int dexterity = BattleHelper.GetDiceCheckValue(dice.Role, attrType: EHeroAttribute.DEX);
                    int combined = UnityEngine.Mathf.Clamp(combat + dexterity, dice.MinCheckValue, dice.MaxCheckValue);
                    dice.CheckValue = UnityEngine.Mathf.Clamp((int)(combined * __0.CheckValueRatio),
                        dice.MinCheckValue, dice.MaxCheckValue);
                }
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), "_GetCheckResult",
            new[] { typeof(EDiceResult), typeof(EDiceResult) })]
        private static class ExecutionResultFloor
        {
            private static void Postfix(BattleActiveBehaviorData __instance, EDiceResult __0, ref EDiceResult __result)
            {
                if (__instance.BattleSkillData?.Id == StrikeProfiles.ExecutionId && __0 != EDiceResult.None &&
                    __result.GetLevel() < EDiceResult.Success.GetLevel())
                    __result = EDiceResult.Success;
            }
        }
    }
}
