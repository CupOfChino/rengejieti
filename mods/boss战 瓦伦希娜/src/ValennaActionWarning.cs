using System;
using System.Reflection;
using Game;
using Game.SkillData;
using HarmonyLib;
using UnityEngine;

namespace ValennaBattle
{
    internal static class ValennaActionWarning
    {
        private static readonly Type ExtraSlotView = AccessTools.TypeByName("DuoActionSlot.SlotHudView");
        private static readonly Type ExtraSlotApi = AccessTools.TypeByName("DuoActionSlot.ExtraActionSlotApi");
        private static readonly Func<BattleRole, int, BattleActiveBehaviorData> GetSlotData =
            BindOptional<Func<BattleRole, int, BattleActiveBehaviorData>>("GetSlotData");
        private static readonly Func<BattleRole, int, RectTransform> GetSlotAnchor =
            BindOptional<Func<BattleRole, int, RectTransform>>("GetSlotAnchor");

        private static int WarningLevel(BattleSkillData skill) =>
            skill?.IsItemSkill == false ? ValennaWarningArt.Level(skill.Id) : 0;

        private static void SetNative(HUDElement hud, int level)
        {
            if (hud.Anim_SkillShow != null && hud.Anim_SkillShow.transform is RectTransform frame)
                ValennaWarningArt.SetSlot(frame, level);
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.ShowSkillIcon))]
        private static class BindNativeIcon
        {
            private static void Postfix(HUDElement __instance, LocalizationKeyData __1, bool __runOriginal)
            {
                var skill = __instance.Model?.Role?.CurrentBehaviourData?.BattleSkillData;
                // 行动种类的通用图标和多行动槽接管时都要清掉原生牌面上的徽标。
                SetNative(__instance, __runOriginal && __1 != null && ReferenceEquals(__1, skill?.NameKey)
                    ? WarningLevel(skill) : 0);
            }
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.CloseSkillIcon))]
        private static class CloseNativeIcon
        {
            private static void Prefix(HUDElement __instance) => SetNative(__instance, 0);
        }

        private static T BindOptional<T>(string name) where T : Delegate
        {
            if (ExtraSlotApi == null) return null;
            MethodInfo method = AccessTools.Method(ExtraSlotApi, name,
                new[] { typeof(BattleRole), typeof(int) });
            return method == null ? null : Delegate.CreateDelegate(typeof(T), method, false) as T;
        }

        private static MethodInfo FindExtraSlotMethod(string name) =>
            ExtraSlotView == null || GetSlotData == null || GetSlotAnchor == null
                ? null : AccessTools.Method(ExtraSlotView, name);

        private static void RefreshExtraSlot(BattleRole role, int index)
        {
            RectTransform frame = GetSlotAnchor(role, index);
            if (frame != null) ValennaWarningArt.SetSlot(frame, WarningLevel(GetSlotData(role, index)?.BattleSkillData));
        }

        [HarmonyPatch]
        private static class BindExtraIcon
        {
            private static bool Prepare() => TargetMethod() != null;
            private static MethodBase TargetMethod() => FindExtraSlotMethod("SetSlotIcon");

            private static void Postfix(int __0, HUDElement ____hud)
            {
                BattleRole role = ____hud?.Model?.Role;
                if (role != null) RefreshExtraSlot(role, __0);
            }
        }

        [HarmonyPatch]
        private static class SyncExtraSlots
        {
            private static bool Prepare() => TargetMethod() != null;
            private static MethodBase TargetMethod() => FindExtraSlotMethod("SyncSlotsVisible");

            private static void Postfix(HUDElement ____hud, int ____slotCount)
            {
                BattleRole role = ____hud?.Model?.Role;
                if (role == null) return;
                // 技能数据先于异步图标更新，按各格数据刷新才能及时移除被替换的徽标。
                for (int index = 0; index < ____slotCount; index++)
                    RefreshExtraSlot(role, index);
            }
        }
    }
}
