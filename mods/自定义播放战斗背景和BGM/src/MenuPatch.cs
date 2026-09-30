// 幕间时光（度假列表）里的三个入口。
//
// 入口本身是数据（Project_Depersonal\...\Game\InterludeVacat\881002/881003/881004.txt），
// 游戏会照着数据把选项摆进列表；点下去以后走 UIInterludePanel._onClickVacationMode，
// 这里把这些编号拦下来，改成我们自己的行为（打开编辑窗 / 添加【领域】/ 移除【领域】）。

using System;
using System.Threading.Tasks;
using HarmonyLib;
using MOD;

namespace CustomBattleBg
{
    internal static class DomainMenu
    {
        /// <summary>幕间入口编号 → 对外接口的键（不是我们的入口返回 null）。</summary>
        internal static string KeyOfVacatId(int vacatId)
        {
            if (vacatId == DomainConstants.AddVacatId)
            {
                return DomainApi.MenuAdd;
            }
            if (vacatId == DomainConstants.EditVacatId)
            {
                return DomainApi.MenuEdit;
            }
            if (vacatId == DomainConstants.RemoveVacatId)
            {
                return DomainApi.MenuRemove;
            }
            return null;
        }

        /// <summary>幕间列表重建之后，把被外部模组关闭的入口从列表里拿掉。</summary>
        internal static void FilterDisabledEntries(UIInterludePanel panel)
        {
            try
            {
                if (panel == null || panel.ChooseAddElements == null)
                {
                    return;
                }
                for (int i = panel.ChooseAddElements.Count - 1; i >= 0; i--)
                {
                    UIChooseAddElement element = panel.ChooseAddElements[i];
                    if (element == null || element.vacatData == null)
                    {
                        continue;
                    }
                    string key = KeyOfVacatId(element.vacatData.VacatId);
                    if (key == null || DomainApi.IsMenuEnabled(key))
                    {
                        continue;
                    }
                    panel.ChooseAddElements.RemoveAt(i);
                    if (panel.Pool_ChooseAdd != null)
                    {
                        panel.Pool_ChooseAdd.Recycle(element.transform);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(element.gameObject);
                    }
                    CustomBattleBgPlugin.LogInfo("已从幕间列表隐藏被关闭的入口：" + DomainApi.DescribeKey(key));
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("过滤被关闭的幕间入口失败：" + e.Message);
            }
        }

        /// <summary>用游戏自己的提示条弹一句话。</summary>
        internal static void Hint(string text)
        {
            try
            {
                text = DomainText.L(text);
                LocalizationKeyData key = new LocalizationKeyData();
                key.TarKey = "";
                key.SheetKey = "";
                key.InputText = text;
                PrefabSingleton<UIMsgHintPanel>.Instance.ShowPopup(key, false);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("弹提示失败：" + e.Message);
            }
        }

        /// <summary>这个角色身上有没有【领域】特质（醒着）。</summary>
        internal static bool HasDomainTrait(RoleData role)
        {
            if (role == null)
            {
                return false;
            }
            try
            {
                MOD_Dynamic_Trait trait = role.GetTraitData(DomainConstants.TraitId);
                if (trait == null)
                {
                    return false;
                }
                return trait.CurrentState == ETraitState.Wake;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void AddDomainTrait(HeroRoleData role)
        {
            AddDomainTraitAsync(role);
        }

        internal static void RemoveDomainTrait(HeroRoleData role)
        {
            RemoveDomainTraitAsync(role);
        }

        private static async void AddDomainTraitAsync(HeroRoleData role)
        {
            try
            {
                await role.AddTrait(DomainConstants.TraitId, true);
                Hint("「" + DomainStore.SafeRoleName(role) + "」获得了【领域】特质。");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("添加【领域】特质失败：" + e);
                Hint("添加【领域】特质失败，看日志。");
            }
        }

        private static async void RemoveDomainTraitAsync(HeroRoleData role)
        {
            string name = DomainStore.SafeRoleName(role);
            try
            {
                DomainProfile def = CustomBattleBgPlugin.Store.Find(role);
                await role.RemoveTrait(DomainConstants.TraitId);
                if (def != null)
                {
                    // 移除特质时，配置引用的背景/BGM 文件也一起删掉
                    CustomBattleBgPlugin.Store.DeleteWithAssets(def);
                    CustomBattleBgPlugin.LogInfo("已删除「" + name + "」的领域配置：" + def.DisplayName);
                }
                Hint("「" + name + "」的「领域」特质与配置已移除。");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("移除【领域】失败：" + e);
                Hint("移除【领域】失败，看日志。");
            }
        }
    }

    // 幕间里点三个入口时改走我们的逻辑
    [HarmonyPatch(typeof(UIInterludePanel), "_onClickVacationMode")]
    internal static class Patch_InterludeVacation
    {
        private static bool Prefix(InterludeVacationConfig vacatConfig, HeroRoleData tarRole, ref Task __result)
        {
            try
            {
                if (vacatConfig == null)
                {
                    return true;
                }
                string key = DomainMenu.KeyOfVacatId(vacatConfig.VacatId);
                if (key == null)
                {
                    return true;   // 不是我们的入口
                }
                if (!DomainApi.IsMenuEnabled(key))
                {
                    return false;   // 入口已被外部模组关闭（正常也不会显示出来）
                }
                // 被其它模组重写 → 走他们的实现
                Action<HeroRoleData> handler = DomainApi.GetMenuOverride(key);
                if (handler != null)
                {
                    if (tarRole != null)
                    {
                        try
                        {
                            handler(tarRole);
                        }
                        catch (Exception e)
                        {
                            CustomBattleBgPlugin.LogError("被重写的幕间入口执行出错：" + e);
                        }
                    }
                    return false;
                }
                if (key == DomainApi.MenuAdd)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (DomainMenu.HasDomainTrait(tarRole))
                    {
                        DomainMenu.Hint("「" + DomainStore.SafeRoleName(tarRole) + "」已经有【领域】特质了。");
                        return false;
                    }
                    DomainMenu.AddDomainTrait(tarRole);
                    return false;
                }
                if (key == DomainApi.MenuEdit)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (!DomainMenu.HasDomainTrait(tarRole))
                    {
                        DomainMenu.Hint("「" + DomainStore.SafeRoleName(tarRole) + "」还没有【领域】特质，先用「添加「领域」」。");
                        return false;
                    }
                    DomainEditorWindow.Open(tarRole);
                    return false;
                }
                if (key == DomainApi.MenuRemove)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (!DomainMenu.HasDomainTrait(tarRole))
                    {
                        DomainMenu.Hint("「" + DomainStore.SafeRoleName(tarRole) + "」没有【领域】特质，不用移除。");
                        return false;
                    }
                    DomainMenu.RemoveDomainTrait(tarRole);
                    return false;
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("幕间入口出错：" + e);
            }
            return true;
        }
    }

    // 幕间度假列表重建之后：把被外部模组关闭的入口从列表里拿掉（"只要有一个模组关了就不展示"）
    [HarmonyPatch(typeof(UIInterludePanel), "UpdateVacationInfo")]
    internal static class Patch_UpdateVacationInfo
    {
        private static void Postfix(UIInterludePanel __instance)
        {
            DomainMenu.FilterDisabledEntries(__instance);
        }
    }
}
