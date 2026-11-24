using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MOD;

namespace AttackTargetVisualizer
{
    [HarmonyPatch]
    internal static class SecretTraitAvailability
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.Field(typeof(TraitData), nameof(TraitData.Type));
            var isCrazy = AccessTools.PropertyGetter(typeof(MOD_Dynamic_Trait), nameof(MOD_Dynamic_Trait.IsCrazyTrait));
            yield return FindPredicate(typeof(RoleData), nameof(RoleData.CheckCrazyTraitState), typeof(TraitData), type);
            yield return FindPredicate(typeof(RoleData), nameof(RoleData.CheckCrazyTraitState), typeof(MOD_Dynamic_Trait), isCrazy);
            yield return FindPredicate(typeof(TraitHelper), nameof(TraitHelper.WakeCrazyTrait), typeof(MOD_Dynamic_Trait), isCrazy);
        }

        private static MethodInfo FindPredicate(Type owner, string entry, Type argument, MemberInfo selectionMember)
        {
            string prefix = "<" + entry + ">b__";
            return owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Single(method => method.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                    method.ReturnType == typeof(bool) && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == argument &&
                    PatchProcessor.GetOriginalInstructions(method).Any(instruction => Equals(instruction.operand, selectionMember)));
        }

        private static void Postfix(object __0, ref bool __result)
        {
            if (!__result) return;
            int id = __0 is TraitData config ? config.Id : (__0 as MOD_Dynamic_Trait)?.Id ?? 0;
            if (SecretTraits.IsOurSecretTrait(id)) __result = false;
        }
    }

    [HarmonyPatch(typeof(UICharacterTraitInfoPanel), nameof(UICharacterTraitInfoPanel.Open))]
    internal static class HideDefaultSecretTraits
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(RoleData), nameof(RoleData.Traits));
            var visible = AccessTools.Method(typeof(HideDefaultSecretTraits), nameof(VisibleTraits));
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsField(field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = visible;
                }
                yield return instruction;
            }
        }

        private static List<MOD_Dynamic_Trait> VisibleTraits(RoleData role)
        {
            // Older versions could seed dormant, non-initial entries. Keep the saved data intact.
            return role.Traits.Where(trait => trait != null &&
                (trait.IsInitGet || trait.CurrentState != ETraitState.Sleepy || !SecretTraits.IsOurSecretTrait(trait.Id))).ToList();
        }
    }
}
