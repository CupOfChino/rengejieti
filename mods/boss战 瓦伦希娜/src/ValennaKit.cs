using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    internal static partial class ValennaKit
    {
        private const string LegacyStats = "valenna_legacy_bases";
        private static readonly int[] InitialTraits =
        {
            StrikeProfiles.TraitId, StrikeProfiles.PrescienceTraitId, StrikeProfiles.DisgraceTraitId,
            StrikeProfiles.VeteranTraitId, StrikeProfiles.DrinkerTraitId, StrikeProfiles.GoldenTicketTraitId,
            StrikeProfiles.HuntTraitId, StrikeProfiles.HeartTraitId
        };
        private static readonly int[] InitialWeapons = { StrikeProfiles.GunbladeId, StrikeProfiles.CutlassId };
        private static readonly HashSet<RoleData> Upgrading = new HashSet<RoleData>();
        private static readonly ConditionalWeakTable<ModuleRecordData, HashSet<RoleData>> ModuleStarts =
            new ConditionalWeakTable<ModuleRecordData, HashSet<RoleData>>();

        private static bool Has(BattleRole role, int trait) => role?.Data != null &&
            role.GetTraitData(trait)?.Config != null;

        // 配了 UniqueKey 的物品在存档中的实例 Id 与表里的 ItemId 不同。
        internal static bool IsItem(MOD_Dynamic_Item item, int id) => item?.Config?.ItemId == id || item?.Id == id;

        private static MOD_Dynamic_Item FindItem(BattleRole role, int id) =>
            role.GetCurrentAllItem().Find(item => IsItem(item, id));

        internal static bool IsInitialTrait(int id) => InitialTraits.Contains(id);

        internal static async Task<MOD_Dynamic_Item> KeepOneEquipment(BattleRole role, int id)
        {
            RoleData data = role.Data;
            var matches = data.Weapons.Concat(data.Equips).Concat(data.BagItems)
                .Where(item => IsItem(item, id)).Distinct().ToArray();
            if (matches.Length == 0) return null;
            // 已穿戴的放在最前，保留它的运行状态及此次装备的来源。
            foreach (MOD_Dynamic_Item duplicate in matches.Skip(1))
                await data.RemoveItem(true, duplicate);
            return matches[0];
        }

        internal static bool HasStartingAmmo(BattleRole role) =>
            Has(role, StrikeProfiles.DisgraceTraitId) ||
            role?.Data?.Weapons?.Exists(item => IsItem(item, StrikeProfiles.GunbladeId)) == true;

        internal static async Task UpgradePartner(BattleRole role, bool equipStartingGear = false)
        {
            if (!ValennaPlugin.IsValennaPartner(role) || !StrikeSequence.CanHit(role) ||
                !Upgrading.Add(role.Data)) return;

            try
            {
                var data = role.Data;
                bool changed = await RepairExistingTraits(data);
                bool missingGear = InitialWeapons.Any(id => FindItem(role, id)?.IsLockedOnNpc != true) ||
                    FindItem(role, StrikeProfiles.HatId) == null;
                if (!Has(role, StrikeProfiles.HuntTraitId))
                {
                    // 旧伙伴的基础面板已经计入这些数值，先还原特质加减值。
                    var offsets = new List<ChangeAttrData>
                    {
                        new ChangeAttrData(EHeroAttribute.STR, "5", true),
                        new ChangeAttrData(EHeroAttribute.CON, "10", true),
                        new ChangeAttrData(EHeroAttribute.POW, "-10", true),
                        new ChangeAttrData(EExploreSkill.Combat, "5", true),
                        new ChangeAttrData(EExploreSkill.LongRange, "5", true),
                        new ChangeAttrData(EExploreSkill.SleightOfHand, "10", true),
                        new ChangeAttrData(EExploreSkill.Stealth, "10", true),
                        new ChangeAttrData(EExploreSkill.Listen, "-5", true),
                        new ChangeAttrData(EExploreSkill.Scout, "-5", true)
                    };
                    await data.ChangeAttr(false, offsets, LegacyStats, playAnim: false, showChangeTip: false);
                    await data.ChangeAttr(true, offsets, LegacyStats, playAnim: false, showChangeTip: false);

                    var palermo = role.GetTraitData(StrikeProfiles.TraitId);
                    if (palermo?.Config != null)
                    {
                        var stealth = new ChangeAttrData(EExploreSkill.Stealth, "-10", true);
                        await data.ChangeAttr(false, stealth, palermo.SourceKey, playAnim: false, showChangeTip: false);
                        await data.ChangeAttr(true, stealth, palermo.SourceKey, playAnim: false, showChangeTip: false);
                    }
                }

                foreach (int trait in InitialTraits)
                {
                    if (trait != StrikeProfiles.HuntTraitId && trait != StrikeProfiles.HeartTraitId && !Has(role, trait))
                    {
                        await role.AddTrait(trait, showChangeInfo: false, isInitTrait: true);
                        changed = true;
                    }
                }
                await RefreshPartnerProfile(data);
                bool equip = equipStartingGear || missingGear;
                foreach (int weapon in InitialWeapons) await GiveStartingWeapon(role, weapon, equip);
                await GiveStartingHat(role, equip);
                if (missingGear || changed) await Restock(role);
                if (!Has(role, StrikeProfiles.HuntTraitId))
                {
                    await role.AddTrait(StrikeProfiles.HuntTraitId, showChangeInfo: false, isInitTrait: true);
                    changed = true;
                }
                // 旧存档随特质更新补齐「心」，沿用常规库存。
                if (!Has(role, StrikeProfiles.HeartTraitId))
                {
                    await role.AddTrait(StrikeProfiles.HeartTraitId, showChangeInfo: false, isInitTrait: true);
                    changed = true;
                }
                ValennaHeart.RefreshSpeed(data, role.GetTraitData(StrikeProfiles.HeartTraitId));
                if (changed || missingGear || equipStartingGear)
                {
                    int traits = data.Traits.Count(trait => trait != null && IsInitialTrait(trait.Id));
                    int armed = InitialWeapons.Count(id => data.Weapons.Exists(weapon => IsItem(weapon, id)));
                    bool wearingHat = data.Equips.Exists(item => IsItem(item, StrikeProfiles.HatId));
                    ValennaPlugin.Instance.Report($"瓦伦希娜伙伴资料：特质 {traits}/{InitialTraits.Length}，已装备初始武器 {armed}/{InitialWeapons.Length}，帽子已装备 {wearingHat}");
                }
            }
            finally { Upgrading.Remove(role.Data); }
        }

        private const string OldPartnerName = "瓦伦娜";
        private const string PartnerName = "瓦伦希娜";
        private static readonly string OldPartnerNameKey = new LocalizationKeyData
        {
            InputText = OldPartnerName
        }.TargetKey;

        private static bool IsPartnerNameOwner(RoleData data) =>
            data is PartnerRoleData partner && partner.PartnerId == ValennaPlugin.PartnerId;

        private static bool IsOldPartnerName(string value) =>
            value == OldPartnerName || value == OldPartnerNameKey;

        private static LocalizationKeyData GetPartnerName(RoleData data)
        {
            var name = data.ConfigData?.CharacterName;
            return name?.InputText == PartnerName ? name : new LocalizationKeyData { InputText = PartnerName };
        }

        // 伙伴库与入队后的角色卡都会读取这两个结果。旧 RoleData 会保留建档时的本地化键。
        [HarmonyPatch(typeof(RoleData), nameof(RoleData.Name), MethodType.Getter)]
        private static class PartnerNameDisplay
        {
            private static void Postfix(RoleData __instance, ref string __result)
            {
                if (IsPartnerNameOwner(__instance) && IsOldPartnerName(__result))
                    __result = PartnerName;
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.NameKeyData), MethodType.Getter)]
        private static class PartnerNameKeyDisplay
        {
            private static void Postfix(RoleData __instance, ref LocalizationKeyData __result)
            {
                if (IsPartnerNameOwner(__instance) && __result != null &&
                    (IsOldPartnerName(__result.InputText) || IsOldPartnerName(__result.TargetKey)))
                    __result = GetPartnerName(__instance);
            }
        }

        private static async Task RefreshPartnerProfile(RoleData data)
        {
            if (data.Attribute?.AttrDict == null || data.Hp == null ||
                !data.Attribute.AttrDict.TryGetValue(EHeroAttribute.CON, out DiceVariableData con)) return;
            int previousOffset = con._sources?.Where(source => source.Key == LegacyStats)
                .Sum(source => source.Value) ?? 0;
            int template = data.ConfigData.Constitution;
            // 最老的伙伴从最终面板建档，后来通过同源修正还原基础。保留其余来源和成长。
            if (con._baseValue != 60 && !(previousOffset == 10 && con._baseValue == 50)) return;
            int nextBase = template - previousOffset;
            if (con._baseValue >= nextBase) return;
            con.Assign(nextBase);
            await data.UpdateValueOnAttrChanged(EHeroAttribute.CON, showChangeTip: false);
        }

        private static async Task<bool> RepairExistingTraits(RoleData data)
        {
            bool changed = false;
            foreach (int id in InitialTraits)
            {
                int first = data.Traits.FindIndex(trait => trait?.Id == id);
                if (first < 0) continue;
                MOD_Dynamic_Trait keeper = data.Traits[first];
                if (keeper.Config == null) await keeper.Reload(data);
                for (int index = data.Traits.Count - 1; index > first; index--)
                {
                    MOD_Dynamic_Trait duplicate = data.Traits[index];
                    if (duplicate?.Id != id) continue;
                    if (!ReferenceEquals(duplicate, keeper))
                    {
                        // 同源副本共用一条属性记录，独立来源则用游戏原有撤销流程。
                        if (duplicate.SourceKey == keeper.SourceKey || string.IsNullOrEmpty(duplicate.SourceKey))
                            duplicate.ClearAllListener();
                        else
                        {
                            duplicate.Role = data;
                            duplicate.Config = keeper.Config;
                            await duplicate.OnRemoveTrait();
                        }
                    }
                    data.Traits.RemoveAt(index);
                    if (id != StrikeProfiles.HeartTraitId) changed = true;
                }
            }
            return changed;
        }

        private static async Task GiveStartingWeapon(BattleRole role, int id, bool equip)
        {
            RoleData data = role.Data;
            MOD_Dynamic_Item item = await KeepOneEquipment(role, id);
            if (item == null)
            {
                await data.AddItem(id, showAddItemTip: false, triggerAutoEquip: false);
                item = data.BagItems.Find(candidate => IsItem(candidate, id));
            }
            if (item == null) return;
            item.IsLockedOnNpc = true;
            if (!equip || data.Weapons.Contains(item) || data.Weapons.Any(weapon => weapon?.IsNeedBoth == true)) return;
            int slot = data.Weapons.FindIndex(weapon => weapon == null);
            if (slot < 0) slot = data.Weapons.Count;
            if (slot >= 2) return;
            await item.EquipWeapon(role, slot, false);
        }

        private static async Task GiveStartingHat(BattleRole role, bool equip)
        {
            RoleData data = role.Data;
            int id = StrikeProfiles.HatId;
            MOD_Dynamic_Item item = await KeepOneEquipment(role, id);
            if (item == null)
            {
                await data.AddItem(id, showAddItemTip: false, triggerAutoEquip: false);
                item = FindItem(role, id);
            }
            if (item == null) return;
            item.IsLockedOnNpc = true;
            if (data.Equips.Contains(item))
            {
                // 老档的帽子已在装备栏，读档不会再触发原生穿戴事件。
                var persuade = data.RoleSkills.Find(skill => skill.SkillType == EExploreSkill.Persuade);
                var bonus = item.Config?.Equip?.Arrts?.Find(attr => attr.RoleSkill.Type == EExploreSkill.Persuade);
                if (bonus != null && persuade != null && !string.IsNullOrEmpty(item.SourceKey) &&
                    !persuade.SkillValue._sources.Exists(entry => entry.Key == item.SourceKey))
                    await data.ChangeAttr(true, bonus, item.SourceKey, playAnim: false, showChangeTip: false);
            }
            else if (equip &&
                data.Equips.Count(gear => gear?.Config?.Equip != null && gear.EquipType == EEquipType.None) < 4)
                await item.EquipItem(role, false);
        }

        [HarmonyPatch(typeof(HallWorld), nameof(HallWorld.FreshLibPartner))]
        private static class RestoreLibraryPartner
        {
            private static void Postfix(HallWorld __instance, ref Task __result) =>
                __result = RestoreAfterRefresh(__result, __instance);

            private static async Task RestoreAfterRefresh(Task original, HallWorld hall)
            {
                await original;
                var data = hall.HallData.PartnerData.LibraryPartners
                    .Find(entry => entry?.BaseData?.ConfigId == ValennaPlugin.PartnerId)?.BaseData;
                if (data?.GetConfig() is not PartnerData config) return;
                data.ConfigData = config;
                if (data.Role == null) data.Role = new BattleRole { Data = data };
                try { await UpgradePartner(data.Role, equipStartingGear: true); }
                catch (Exception error) { ValennaPlugin.Instance.Report(error); }
            }
        }

        internal static async Task EnterModule(BattleRole role)
        {
            await UpgradePartner(role, equipStartingGear: true);
            if (!Has(role, StrikeProfiles.DisgraceTraitId) || role.IsDeath) return;

            ModuleRecordData record = BattleHelper.Record;
            if (record != null && ModuleStarts.GetOrCreateValue(record).Add(role.Data))
                await role.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.Gold, "1000"),
                    playAnim: false, showChangeTip: false);
            await Restock(role);
            await ValennaBattleStatus.StartAmmo(role);
        }

        internal static async Task FinishModule(BattleRole role)
        {
            if (!Has(role, StrikeProfiles.DisgraceTraitId) || role.IsDeath) return;
            await Restock(role);
            await ValennaBattleStatus.StartAmmo(role);
        }

        private static async Task Restock(BattleRole role)
        {
            await Fill(role, StrikeProfiles.CigarId, 3);
            await Fill(role, StrikeProfiles.VodkaId, 1);
        }

        private static async Task Fill(BattleRole role, int id, int count)
        {
            MOD_Dynamic_Item item = FindItem(role, id);
            if (item == null)
            {
                await role.Data.AddItem(id, showAddItemTip: false);
                item = FindItem(role, id);
            }
            if (item != null && item.GetCharge(role.Data) < count) await item.SetCharge(role, count);
        }
    }
}
