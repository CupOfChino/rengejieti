using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BattleSkillEvent;
using Game;
using HarmonyLib;

namespace ValennaBattle
{
    internal static class ValennaHeart
    {
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
