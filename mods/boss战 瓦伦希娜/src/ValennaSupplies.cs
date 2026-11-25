using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    internal static class ValennaSupplies
    {
        private const float Hour = 1f / 24f;
        private const string ExpiryKey = "valenna_drunk_until";

        private static bool TryDrunk(BattleRole role, out BuffData drunk)
        {
            drunk = role?.Data?.BuffList != null ? role.GetBuff(StrikeProfiles.DrunkId) : null;
            return drunk != null && !drunk.DisableBuff;
        }

        private static bool Expired(BuffData drunk)
        {
            var end = drunk.BuffRecords.Find(record => record?.Key == ExpiryKey);
            return end != null && BattleHelper.Record != null && BattleHelper.Record.GameTime >= end.Value;
        }

        private static readonly ConditionalWeakTable<RoleData, MOD_Dynamic_Item> HeldCigars =
            new ConditionalWeakTable<RoleData, MOD_Dynamic_Item>();
        private static (RoleData Role, MOD_Dynamic_Item Cigar) _cigarMenu;

        [HarmonyPatch(typeof(UIItemManagementPanel), nameof(UIItemManagementPanel.OnLeftClickElement))]
        private static class SelectCigar
        {
            private static void Prefix(UIItemManagementPanel __instance, UIItemElement __0,
                out (RoleData Role, MOD_Dynamic_Item Cigar) __state)
            {
                __state = _cigarMenu;
                RoleData role = __instance.CurSelectRole?.Data;
                MOD_Dynamic_Item cigar = __0?.ItemData;
                if (__instance.IsCommonPanelMode && !BattleHelper.IsInBattle && role != null &&
                    ValennaKit.IsItem(cigar, StrikeProfiles.CigarId) && cigar.GetCharge(role) > 0)
                    _cigarMenu = (role, cigar);
            }

            private static void Finalizer((RoleData Role, MOD_Dynamic_Item Cigar) __state) => _cigarMenu = __state;
        }

        [HarmonyPatch(typeof(UISubMenuPanel), nameof(UISubMenuPanel.Open))]
        private static class AddCigarMode
        {
            private static void Prefix(List<SubMenuSelectionViewData> __0)
            {
                (RoleData role, MOD_Dynamic_Item cigar) = _cigarMenu;
                if (role == null || __0 == null) return;
                int useIndex = __0.FindIndex(option => option.ButtonLocKey == "使用");
                if (useIndex < 0) return;

                bool holding = HeldCigars.TryGetValue(role, out MOD_Dynamic_Item held) &&
                    ReferenceEquals(held, cigar);
                __0[useIndex].ButtonLocKey = "valenna.cigar.savor";
                __0.Insert(useIndex + 1, new SubMenuSelectionViewData
                {
                    ButtonLocKey = holding ? "valenna.cigar.remove" : "valenna.cigar.hold",
                    Callback = () =>
                    {
                        HeldCigars.Remove(role);
                        if (!holding) HeldCigars.Add(role, cigar);
                    }
                });
            }
        }

        [HarmonyPatch(typeof(Item_ChargingOption), "_SetCharge")]
        private static class CigarTime
        {
            private static void Prefix(BattleRole __0, MOD_Dynamic_Item __1, out int __state)
            {
                __state = ValennaKit.IsItem(__1, StrikeProfiles.CigarId) && __0?.Data != null
                    ? __1.GetCharge(__0.Data) : 0;
            }

            private static void Postfix(BattleRole __0, MOD_Dynamic_Item __1, int __state, ref Task __result)
            {
                if (__state > 0) __result = Taste(__result, __0, __1, __state);
            }

            private static async Task Taste(Task original, BattleRole role, MOD_Dynamic_Item cigar, int before)
            {
                await original;
                if (BattleHelper.IsInBattle || cigar.GetCharge(role.Data) >= before) return;
                HeldCigars.Remove(role.Data);

                if (Singleton<GameWorld>.HasInstance)
                    await Singleton<GameWorld>.Instance.GameInstance.AddGameTime(Hour);
                else if (Singleton<HallWorld>.HasInstance)
                    await Singleton<HallWorld>.Instance.HallInstance.AddGameTime(Hour);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.AddBuff))]
        private static class StoreExpiry
        {
            private static void Prefix(BuffData __instance)
            {
                if (__instance.Id != StrikeProfiles.DrunkId || BattleHelper.Record == null) return;

                __instance.BuffRecords.RemoveAll(record => record?.Key == ExpiryKey);
                __instance.BuffRecords.Add(new MapRecordValue
                {
                    Key = ExpiryKey, Value = BattleHelper.Record.GameTime + 12f * Hour
                });
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.TriggerRoundStartEffectOnRoundChangeInMap))]
        private static class SoberOnTime
        {
            private static void Postfix(BuffData __instance, ref Task __result)
            {
                if (__instance.Id == StrikeProfiles.DrunkId && Expired(__instance))
                    __result = Sober(__result, __instance);
            }

            private static async Task Sober(Task original, BuffData drunk)
            {
                await original;
                if (ReferenceEquals(drunk.TargetRole?.GetBuff(StrikeProfiles.DrunkId), drunk))
                    await drunk.TargetRole.RemoveBuff(StrikeProfiles.DrunkId, showChangeTip: false);
            }
        }

        [HarmonyPatch(typeof(DiceResultData), "_GetExDice")]
        private static class InBattlePenalty
        {
            private static void Prefix(DiceResultData __instance)
            {
                BattleRole role = __instance.Role;
                if (BattleHelper.IsInBattle && TryDrunk(role, out BuffData drunk) && !Expired(drunk))
                    __instance.TmpPunishDice++;
            }
        }

        [HarmonyPatch(typeof(BattleHelper), nameof(BattleHelper.GetDiceData),
            new[] { typeof(BattleRole), typeof(EExploreSkill), typeof(EHeroAttribute), typeof(ERoleExtraAttribute), typeof(bool) })]
        private static class InExplorePenalty
        {
            private static void Postfix(BattleRole __0, CheckDiceData __result)
            {
                if (!BattleHelper.IsInBattle && __result != null &&
                    TryDrunk(__0 ?? BattleHelper.SceneHeroInfo, out BuffData drunk) && !Expired(drunk))
                    __result.PunishDiceCount++;
            }
        }
    }
}
