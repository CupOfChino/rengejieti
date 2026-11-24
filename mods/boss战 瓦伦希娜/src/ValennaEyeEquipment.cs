using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    internal static class ValennaEyeEquipment
    {
        private static readonly HashSet<RoleData> Provisioning = new HashSet<RoleData>();
        private static GameEventManager _events;

        internal static bool HasPrescience(BattleRole role) =>
            role?.Data != null && role.GetTraitData(StrikeProfiles.PrescienceTraitId)?.Config != null;

        private static bool IsEye(MOD_Dynamic_Item item) => ValennaKit.IsItem(item, StrikeProfiles.EyeItemId);

        private static bool IsOwner(RoleData data) => data?.ConfigId == ValennaPlugin.PartnerId &&
            (data is PartnerRoleData || data.ConfigData is PartnerData);

        internal static bool IsWorn(BattleRole role) =>
            IsOwner(role?.Data) && role.Data.Equips?.Exists(IsEye) == true;

        private static MOD_Dynamic_Item Find(BattleRole role) =>
            role.Data.Equips?.Find(IsEye) ?? role.Data.BagItems?.Find(IsEye);

        internal static void ListenForModuleEntry()
        {
            if (!Singleton<GameEventManager>.HasInstance) return;
            GameEventManager manager = Singleton<GameEventManager>.Instance;
            if (ReferenceEquals(manager, _events)) return;
            StopListening();
            _events = manager;
            _events.AddListener(EGameEvent_GamePlay.EnterModule, OnEnterModule);
            _events.AddListener(EGameEvent_GamePlay.FinishModule, OnFinishModule);
        }

        internal static void StopListening()
        {
            if (_events != null)
            {
                _events.RemoveListener(EGameEvent_GamePlay.EnterModule, OnEnterModule);
                _events.RemoveListener(EGameEvent_GamePlay.FinishModule, OnFinishModule);
            }
            _events = null;
        }

        private static async Task OnEnterModule(int eventId, params object[] arguments)
        {
            if (arguments == null) return;
            foreach (object argument in arguments)
            {
                if (argument is not BattleRole role || role.Data == null) continue;
                try
                {
                    await ValennaKit.EnterModule(role);
                    await EnsureEquipped(role);
                }
                catch (Exception error) { ValennaPlugin.Instance.Report(error); }
            }
        }

        [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Install), new[] { typeof(GameWorldCreateInfo) })]
        private static class RestoreLoadedPartners
        {
            private static void Postfix(ref Task __result) => __result = RestoreAfterWorldLoad(__result);

            private static async Task RestoreAfterWorldLoad(Task original)
            {
                await original;
                foreach (BattleRole role in BattleHelper.GetAllTeamRoleList(containsHero: true))
                {
                    if (!ValennaPlugin.IsValennaPartner(role)) continue;
                    try
                    {
                        await ValennaKit.UpgradePartner(role, equipStartingGear: true);
                        await EnsureEquipped(role);
                    }
                    catch (Exception error) { ValennaPlugin.Instance.Report(error); }
                }
            }
        }

        private static async Task OnFinishModule(int eventId, params object[] arguments)
        {
            if (arguments == null) return;
            foreach (object argument in arguments)
                if (argument is BattleRole role) await ValennaKit.FinishModule(role);
        }

        [HarmonyPatch(typeof(HallWorld), nameof(HallWorld.FreshLibPartner))]
        private static class RestoreLibraryEye
        {
            private static void Postfix(HallWorld __instance, ref Task __result) =>
                __result = RestoreAfterRefresh(__result, __instance);

            private static async Task RestoreAfterRefresh(Task original, HallWorld hall)
            {
                await original;
                var partner = hall.HallData.PartnerData.LibraryPartners.Find(entry =>
                    entry?.BaseData?.ConfigId == ValennaPlugin.PartnerId);
                RoleData data = partner?.BaseData;
                if (data == null || Singleton<ResManager>.Instance.MythicalFactory.Find(StrikeProfiles.EyeItemId) == null)
                    return;

                if (data.RoleMythicalItemDatas?.Exists(entry => entry?.MythicalId == StrikeProfiles.EyeItemId) != true)
                    partner.ResetMythicalItem(isAdd: false);

                if (data.Role == null) data.Role = new BattleRole { Data = data };
                MOD_Dynamic_Item eye = await ValennaKit.KeepOneEquipment(data.Role, StrikeProfiles.EyeItemId);
                if (eye == null)
                {
                    await data.AddItem(StrikeProfiles.EyeItemId, showAddItemTip: false, triggerAutoEquip: false);
                    eye = Find(data.Role);
                }
                if (eye != null) eye.IsLockedOnNpc = true;
            }
        }

        internal static async Task EnsureEquipped(BattleRole role)
        {
            RoleData data = role?.Data;
            if (!IsOwner(data) || role.IsDeath || data.Equips == null || data.BagItems == null ||
                !Provisioning.Add(data)) return;

            try
            {
                MOD_Dynamic_Item eye = await ValennaKit.KeepOneEquipment(role, StrikeProfiles.EyeItemId);
                // 已招募的旧版瓦伦娜补齐新增的初始特质。
                if (eye == null && !HasPrescience(role))
                    await role.AddTrait(StrikeProfiles.PrescienceTraitId, showChangeInfo: false, isInitTrait: true);

                if (HasPrescience(role))
                {
                    if (eye == null)
                    {
                        await data.AddItem(StrikeProfiles.EyeItemId, showAddItemTip: false, triggerAutoEquip: false);
                        eye = Find(role);
                    }
                    if (eye != null) eye.IsLockedOnNpc = true;
                    if (eye != null && !IsWorn(role))
                    {
                        await eye.EquipItem(role, false);
                        if (IsWorn(role))
                            ValennaPlugin.Instance.Report($"已在护具栏穿戴预知眼：{data.Name}");
                        else
                            ValennaPlugin.Instance.Warn($"预知眼留在背包，未能穿戴到护具栏：{data.Name}");
                    }
                }

                if (IsWorn(role)) await ValennaPrescience.InstallEye(role);
                else if (data.BuffList != null)
                {
                    // 兼容早期演出存档中由特质直接挂上的眼睛状态。
                    await role.RemoveBuff(StrikeProfiles.EyeChargeId, showChangeTip: false);
                    await role.RemoveBuff(StrikeProfiles.EyeOverheatId, showChangeTip: false);
                }
            }
            catch (Exception error)
            {
                ValennaPlugin.Instance.Report(error);
            }
            finally { Provisioning.Remove(data); }
        }

        [HarmonyPatch(typeof(HallWorld), nameof(HallWorld.AddMythicalItem))]
        private static class KeepOutOfGlobalUnlocks
        {
            private static bool Prefix(int __0) => __0 != StrikeProfiles.EyeItemId;
        }

        [HarmonyPatch(typeof(HallWorld), nameof(HallWorld.GetMythicalItemList))]
        private static class KeepOutOfGlobalCatalogue
        {
            private static void Postfix(List<MOD_Dynamic_MythicalItem> __result) =>
                __result?.RemoveAll(item => item?.Index == StrikeProfiles.EyeItemId);
        }

        [HarmonyPatch]
        private static class RemoveForeignSlots
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(RoleData), nameof(RoleData.GetMythicalItems));
                yield return AccessTools.Method(typeof(RoleData), nameof(RoleData.GetMythicalItem),
                    new[] { typeof(EMythicalItemPriceGrade) });
                yield return AccessTools.Method(typeof(RoleData), nameof(RoleData.GetMythicalItem),
                    new[] { typeof(int) });
            }

            private static void Prefix(RoleData __instance)
            {
                if (IsOwner(__instance) || __instance.RoleMythicalItemDatas == null) return;
                foreach (MythicalGradeData grade in __instance.RoleMythicalItemDatas)
                    if (grade?.MythicalId == StrikeProfiles.EyeItemId) grade.MythicalId = -1;
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.SetMythicalItem))]
        private static class KeepFixedSlot
        {
            private static bool Prefix(RoleData __instance, MythicalGradeData __0)
            {
                if (__0 == null) return true;
                if (__0.MythicalId == StrikeProfiles.EyeItemId) return false;
                return !IsOwner(__instance) ||
                    __instance.GetMythicalItem(__0.Grade)?.MythicalId != StrikeProfiles.EyeItemId;
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.AddItem), new[]
        {
            typeof(MOD_Dynamic_Item), typeof(string), typeof(bool), typeof(bool), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
        private static class BlockForeignItemGrant
        {
            private static bool Prefix(RoleData __instance, MOD_Dynamic_Item __0, ref Task __result)
            {
                if (!IsEye(__0) || IsOwner(__instance)) return true;
                __result = Task.CompletedTask;
                return false;
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.AddItem), new[]
        {
            typeof(List<MOD_Dynamic_Item>), typeof(string), typeof(bool), typeof(bool), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool)
        })]
        private static class BlockForeignBatchGrant
        {
            private static bool Prefix(RoleData __instance, ref List<MOD_Dynamic_Item> __0, ref Task __result)
            {
                if (IsOwner(__instance) || __0 == null || !__0.Exists(IsEye)) return true;
                __0 = __0.FindAll(item => !IsEye(item));
                if (__0.Count > 0) return true;
                __result = Task.CompletedTask;
                return false;
            }
        }

        [HarmonyPatch(typeof(MOD_Dynamic_Item), nameof(MOD_Dynamic_Item.EquipItem))]
        private static class ApplyEyeAfterEquip
        {
            private static bool Prefix(MOD_Dynamic_Item __instance, BattleRole __0, ref Task __result)
            {
                if (!IsEye(__instance) || IsOwner(__0?.Data)) return true;
                __result = Task.CompletedTask;
                return false;
            }

            private static void Postfix(MOD_Dynamic_Item __instance, BattleRole __0, ref Task __result)
            {
                if (IsEye(__instance))
                    __result = InstallAfterEquip(__result, __0);
            }

            private static async Task InstallAfterEquip(Task original, BattleRole role)
            {
                await original;
                if (IsWorn(role)) await ValennaPrescience.InstallEye(role);
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.RemoveItem), new[] { typeof(bool), typeof(MOD_Dynamic_Item) })]
        private static class ProtectOwnedEye
        {
            private static bool Prefix(RoleData __instance, bool __0, MOD_Dynamic_Item __1, ref Task __result)
            {
                if (__0 || !IsOwner(__instance) || !IsEye(__1)) return true;
                __result = Task.CompletedTask;
                return false;
            }
        }

        [HarmonyPatch(typeof(MOD_Dynamic_Item), nameof(MOD_Dynamic_Item.DisassembleOrDestroy),
            new[] { typeof(BattleRole) })]
        private static class ProtectFromDisassembly
        {
            private static bool Prefix(MOD_Dynamic_Item __instance, ref Task __result)
            {
                if (!IsEye(__instance)) return true;
                __result = Task.CompletedTask;
                return false;
            }
        }
    }
}
