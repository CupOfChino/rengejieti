// 幕间时光（度假列表）里的三个入口。
//
// 入口本身是数据（Project_Depersonal\...\Game\InterludeVacat\880901/880902/880903.txt），
// 游戏会照着数据把选项摆进列表；点下去以后走 UIInterludePanel._onClickVacationMode，
// 这里把这些编号拦下来，改成我们自己的行为（打开编辑窗 / 加上【心】/ 移除【心】）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MOD;

namespace XinEditor
{
    internal static class HeartMenu
    {
        /// <summary>「修改当前角色【心】」的幕间编号。</summary>
        public const int EditHeartVacatId = 880901;

        /// <summary>「添加心特质」的幕间编号。</summary>
        public const int AddHeartVacatId = 880902;

        /// <summary>「移除心特质」的幕间编号。</summary>
        public const int RemoveHeartVacatId = 880903;

        /// <summary>用游戏自己的提示条弹一句话。</summary>
        internal static void Hint(string text)
        {
            try
            {
                text = XinText.L(text);
                LocalizationKeyData key = new LocalizationKeyData();
                key.TarKey = "";
                key.SheetKey = "";
                key.InputText = text;
                PrefabSingleton<UIMsgHintPanel>.Instance.ShowPopup(key, false);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("弹提示失败：" + e.Message);
            }
        }

        /// <summary>这个角色身上有没有【心】特质。</summary>
        internal static bool HasHeartTrait(RoleData role)
        {
            if (role == null)
            {
                return false;
            }
            try
            {
                MOD_Dynamic_Trait trait = role.GetTraitData(HeartConstants.DefaultHeartTraitId);
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

        /// <summary>给角色加上【心】特质（已经有的话游戏自己会拒绝重复添加）。</summary>
        internal static void AddHeartTrait(HeroRoleData role)
        {
            AddHeartTraitAsync(role);
        }

        /// <summary>移除【心】：摘掉特质，并把他自己那颗自定义心从数据和状态表里一起删掉。</summary>
        internal static void RemoveHeartTrait(HeroRoleData role)
        {
            RemoveHeartTraitAsync(role);
        }

        private static async void AddHeartTraitAsync(HeroRoleData role)
        {
            try
            {
                await role.AddTrait(HeartConstants.DefaultHeartTraitId, true);
                Hint("「" + HeartStore.SafeRoleName(role) + "」获得了【心】特质。");
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("添加【心】特质失败：" + e);
                Hint("添加【心】特质失败，看日志。");
            }
        }

        private static async void RemoveHeartTraitAsync(HeroRoleData role)
        {
            string name = HeartStore.SafeRoleName(role);
            try
            {
                HeartDefinition def = XinEditorPlugin.Store.Find(role);

                // 先摘特质：游戏会把身上那颗心一起摘掉，自定义心的摘除由 UnActive 钩子负责
                await role.RemoveTrait(HeartConstants.DefaultHeartTraitId);

                // 再把自定义心本身删干净：身上的状态、状态表里的定义、文件里的记录
                if (def != null)
                {
                    try
                    {
                        if (role.Role != null)
                        {
                            await role.Role.RemoveBuff(def.BuffId);
                        }
                    }
                    catch (Exception e)
                    {
                        XinEditorPlugin.LogError("摘自定义心状态失败：" + e.Message);
                    }
                    HeartBuffBuilder.Unregister(def);
                    XinEditorPlugin.Store.Delete(def);
                    XinEditorPlugin.LogInfo("已删除「" + name + "」的自定义心：" + def.DisplayName);
                }

                Hint("「" + name + "」的「心」buff 已移除。");
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("移除【心】失败：" + e);
                Hint("移除「心」失败，看日志。");
            }
        }
    }

    // 幕间里点「修改当前角色【心】」/「添加心特质」时改走我们的逻辑
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
                if (vacatConfig.VacatId == HeartMenu.AddHeartVacatId)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (HeartMenu.HasHeartTrait(tarRole))
                    {
                        HeartMenu.Hint("「" + HeartStore.SafeRoleName(tarRole) + "」已经有【心】特质了。");
                        return false;
                    }
                    HeartMenu.AddHeartTrait(tarRole);
                    return false;
                }
                if (vacatConfig.VacatId == HeartMenu.EditHeartVacatId)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (!HeartMenu.HasHeartTrait(tarRole))
                    {
                        HeartMenu.Hint("「" + HeartStore.SafeRoleName(tarRole) + "」还没有【心】特质，先用「添加「心」」。");
                        return false;
                    }
                    HeartEditorWindow.Open(tarRole);
                    return false;
                }
                if (vacatConfig.VacatId == HeartMenu.RemoveHeartVacatId)
                {
                    if (tarRole == null)
                    {
                        return false;
                    }
                    if (!HeartMenu.HasHeartTrait(tarRole))
                    {
                        HeartMenu.Hint("「" + HeartStore.SafeRoleName(tarRole) + "」没有【心】特质，不用移除。");
                        return false;
                    }
                    HeartMenu.RemoveHeartTrait(tarRole);
                    return false;
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("幕间入口出错：" + e);
            }
            return true;
        }
    }
}
