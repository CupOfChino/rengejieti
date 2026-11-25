using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal static class ValennaHeart
    {
        internal static bool IsPreset(RoleData role) => role?.ConfigId == ValennaPlugin.PartnerId &&
            (role is PartnerRoleData || role.ConfigData is PartnerData);

        internal static void RefreshSpeed(RoleData role, MOD_Dynamic_Trait heart)
        {
            if (role.ExtraSpeed == null || string.IsNullOrEmpty(heart?.SourceKey)) return;
            var values = role.ExtraSpeed._sources.FindAll(entry => entry.Key == heart.SourceKey);
            if (values.Count == 1 && values[0].Value == StrikeProfiles.HeartSpeed) return;
            if (values.Count != 0) role.ExtraSpeed.Remove(heart.SourceKey);
            role.ExtraSpeed.Add(heart.SourceKey, StrikeProfiles.HeartSpeed);
        }

        private static void ExplainPreset() => PrefabSingleton<UIMsgHintPanel>.Instance.ShowPopup(
            ValennaText.Key("fixedHeart", "瓦伦希娜已固定装备专属「心」。"), false);

        [HarmonyPatch(typeof(UIInterludePanel), "_onClickVacationMode")]
        private static class FixedHeartMenu
        {
            [HarmonyPriority(Priority.First), HarmonyBefore("codex.depersonal.xineditor")]
            private static bool Prefix(InterludeVacationConfig vacatConfig, HeroRoleData tarRole, ref Task __result)
            {
                if (vacatConfig?.VacatId is not (880901 or 880902 or 880903) || !IsPreset(tarRole)) return true;
                __result = Task.CompletedTask;
                ExplainPreset();
                return false;
            }
        }

        [HarmonyPatch]
        private static class FixedHeartEditor
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string entry in new[] { "XinEditor.HeartEditorWindow:Open", "XinEditor.HeartVisualEditor:Open",
                    "XinEditor.HeartMenu:AddHeartTrait", "XinEditor.HeartMenu:RemoveHeartTrait" })
                    yield return AccessTools.Method(entry, new[] { typeof(HeroRoleData) });
            }

            private static bool Prefix(HeroRoleData __0)
            {
                if (!IsPreset(__0)) return true;
                ExplainPreset();
                return false;
            }
        }

        // 旧档即使曾添加通用心，也只激活伙伴的预设，不操作自定义心保存的定义。
        [HarmonyPatch(typeof(MOD.TraitEvent.ActiveBuffOption), "Active")]
        private static class IgnoreLegacyHeart
        {
            [HarmonyPriority(Priority.First), HarmonyBefore("codex.depersonal.xineditor")]
            private static bool Prefix(MOD.TraitEvent.ActiveBuffOption __instance, RoleData role, ref Task __result)
            {
                if (__instance.BuffId != 880901 || !IsPreset(role)) return true;
                __result = Task.CompletedTask;
                return false;
            }
        }

        [HarmonyPatch]
        private static class FixedHeartActivation
        {
            private static MethodBase TargetMethod() => AccessTools.Method("XinEditor.HeartBattleSkill:AddSkill");
            private static bool Prefix(BattleRole __0) => !IsPreset(__0?.Data);
        }

        [HarmonyPatch]
        private static class PresetVisualOrigin
        {
            private static MethodBase TargetMethod() => AccessTools.Method("XinEditor.HeartVisualPlacement:LateUpdate");
            private static void Postfix(MonoBehaviour __instance, RoleModel ____model)
            {
                if (____model != null && IsPreset(____model.Role?.Data))
                    __instance.transform.localPosition = Vector3.zero;
            }
        }

        internal static int SkillLayers(BattleRole source, int layers)
        {
            if (layers <= 0 || layers == int.MaxValue || !BattleHelper.IsInBattle || source?.Data == null)
                return layers;
            var heart = source.GetTraitData(StrikeProfiles.HeartTraitId);
            return heart?.Config != null && heart.CurrentState == ETraitState.Wake ? layers + 1 : layers;
        }

        private static int NativeLayers(int layers, BuffChangeOption effect, BattleActiveBehaviorData action,
            List<BattleRole> targets, int targetIndex)
        {
            if (effect.BuffId != StrikeProfiles.TremorId && effect.BuffId != StrikeProfiles.BreathId)
                return layers;
            if (action?.BattleSkillData == null || action.IsThrowItemSkill ||
                action.ActionType is not (EBattleActionType.Attack or EBattleActionType.Skill))
                return layers;
            BattleRole bearer = effect.BuffId == StrikeProfiles.BreathId ? targets[targetIndex] : action.Self;
            return SkillLayers(bearer, layers);
        }

        // 只调整技能事件本身的增加次数。通用 AddBuff 也供物品、转换和被动调用。
        [HarmonyPatch]
        private static class NativeSkillGrant
        {
            private static MethodBase TargetMethod()
            {
                var entry = AccessTools.Method(typeof(BuffChangeOption), nameof(BuffChangeOption.Run));
                return AccessTools.Method(entry.GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType,
                    nameof(IAsyncStateMachine.MoveNext));
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
                MethodBase original)
            {
                var code = instructions.ToList();
                Type state = original.DeclaringType;
                FieldInfo count = AccessTools.GetDeclaredFields(state).SingleOrDefault(field =>
                    field.FieldType == typeof(int) && field.Name.StartsWith("<changeCount>", StringComparison.Ordinal));
                FieldInfo effect = AccessTools.Field(state, "<>4__this");
                FieldInfo action = AccessTools.Field(state, "data");
                FieldInfo targets = AccessTools.GetDeclaredFields(state).SingleOrDefault(field =>
                    field.FieldType == typeof(List<BattleRole>) && field.Name.StartsWith("<targets>", StringComparison.Ordinal));
                FieldInfo index = AccessTools.GetDeclaredFields(state).SingleOrDefault(field =>
                    field.FieldType == typeof(int) && field.Name.StartsWith("<i>", StringComparison.Ordinal));
                if (count == null || effect?.FieldType != typeof(BuffChangeOption) ||
                    action?.FieldType != typeof(BattleActiveBehaviorData) || targets == null || index == null ||
                    code.Count(instruction => instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, count)) != 2)
                {
                    ValennaPlugin.Instance.Warn("原生技能加层结构已改变，跳过心的通用加层补丁。");
                    return code;
                }
                var adjusted = new List<CodeInstruction>();
                MethodInfo bonus = AccessTools.Method(typeof(ValennaHeart), nameof(NativeLayers));
                foreach (CodeInstruction instruction in code)
                {
                    if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, count))
                    {
                        // 此处是默认次数与骰值次数两次赋值，后者覆盖前者，实际只加一次。
                        var loadEffect = new CodeInstruction(OpCodes.Ldarg_0);
                        loadEffect.labels.AddRange(instruction.labels);
                        instruction.labels.Clear();
                        adjusted.Add(loadEffect);
                        adjusted.Add(new CodeInstruction(OpCodes.Ldfld, effect));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldarg_0));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldfld, action));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldarg_0));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldfld, targets));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldarg_0));
                        adjusted.Add(new CodeInstruction(OpCodes.Ldfld, index));
                        adjusted.Add(new CodeInstruction(OpCodes.Call, bonus));
                    }
                    adjusted.Add(instruction);
                }
                return adjusted;
            }
        }
    }
}
