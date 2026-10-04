// 幕间入口：添加 / 移除【速战速决】特质。
//
// 入口本身是数据（Game\InterludeVacat\882002 / 882003.txt），
// 点下去以后走 UIInterludePanel._onClickVacationMode，这里把这两个编号拦下来，
// 换成我们自己的行为（加特质 / 摘特质）。

using System;
using System.Threading.Tasks;
using HarmonyLib;
using MOD;

namespace DuoActionSlot
{
    internal static class SlotMenu
    {
        internal static bool HasTrait(RoleData role)
        {
            try
            {
                return role != null && role.GetTraitData(SlotConstants.TraitId) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void AddTrait(HeroRoleData role)
        {
            AddTraitAsync(role);
        }

        internal static void RemoveTrait(HeroRoleData role)
        {
            RemoveTraitAsync(role);
        }

        private static async void AddTraitAsync(HeroRoleData role)
        {
            string name = SafeName(role);
            try
            {
                await role.AddTrait(SlotConstants.TraitId, true);
                DuoActionSlotPlugin.LogInfo("已给「" + name + "」添加【速战速决】");
                Hint(name + SlotText.T(" 获得了【速战速决】特质。", SlotText.AddTraitOk));
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("添加【速战速决】失败：" + e);
                Hint(SlotText.T("添加【速战速决】失败，看日志。", SlotText.AddTraitFailed));
            }
        }

        private static async void RemoveTraitAsync(HeroRoleData role)
        {
            string name = SafeName(role);
            try
            {
                await role.RemoveTrait(SlotConstants.TraitId);
                DuoActionSlotPlugin.LogInfo("已移除「" + name + "」的【速战速决】");
                Hint(name + SlotText.T(" 失去了【速战速决】特质。", SlotText.RemoveTraitOk));
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("移除【速战速决】失败：" + e);
                Hint(SlotText.T("移除【速战速决】失败，看日志。", SlotText.RemoveTraitFailed));
            }
        }

        private static string SafeName(RoleData role)
        {
            try
            {
                if (role != null && !string.IsNullOrEmpty(role.Name))
                {
                    return role.Name;
                }
            }
            catch (Exception)
            {
            }
            return "?";
        }

        internal static void Hint(string text)
        {
            try
            {
                if (!PrefabSingleton<UIMsgHintPanel>.HasInstance)
                {
                    return;
                }
                LocalizationKeyData key = new LocalizationKeyData();
                key.TarKey = "";
                key.SheetKey = "";
                key.InputText = text;
                _ = PrefabSingleton<UIMsgHintPanel>.Instance.ShowPopup(key, false);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("弹提示失败：" + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(UIInterludePanel), "_onClickVacationMode")]
    internal static class Patch_InterludeVacation
    {
        private static bool Prefix(InterludeVacationConfig vacatConfig, HeroRoleData tarRole, ref Task __result)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled || vacatConfig == null)
                {
                    return true;
                }
                int id = vacatConfig.VacatId;
                if (id != SlotConstants.AddVacatId && id != SlotConstants.RemoveVacatId)
                {
                    return true;
                }
                if (tarRole == null)
                {
                    __result = Task.CompletedTask;
                    return false;
                }
                bool has = SlotMenu.HasTrait(tarRole);
                if (id == SlotConstants.AddVacatId)
                {
                    if (has)
                    {
                        SlotMenu.Hint(SafeName(tarRole) + SlotText.T(" 已经有【速战速决】特质了。",
                            SlotText.AlreadyHasTrait));
                    }
                    else
                    {
                        SlotMenu.AddTrait(tarRole);
                    }
                }
                else
                {
                    if (!has)
                    {
                        SlotMenu.Hint(SafeName(tarRole) + SlotText.T(" 没有【速战速决】特质。",
                            SlotText.NoTraitToRemove));
                    }
                    else
                    {
                        SlotMenu.RemoveTrait(tarRole);
                    }
                }
                __result = Task.CompletedTask;
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("幕间入口出错：" + e);
                return true;
            }
        }

        private static string SafeName(RoleData role)
        {
            try
            {
                if (role != null && !string.IsNullOrEmpty(role.Name))
                {
                    return role.Name;
                }
            }
            catch (Exception)
            {
            }
            return "?";
        }
    }

    internal static class PatchMenu
    {
        internal static void Register(Harmony harmony)
        {
            DuoActionSlotPlugin.LogInfo("开始挂载幕间钩子……");
            try
            {
                harmony.CreateClassProcessor(typeof(Patch_InterludeVacation)).Patch();
                DuoActionSlotPlugin.LogInfo("钩子已挂：Patch_InterludeVacation");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("钩子挂载失败 Patch_InterludeVacation：" + e.Message);
            }
        }
    }
}
