// 幕间时光（度假列表）里的两个入口。
//
// 两个入口本身是数据（Project_Depersonal\...\Game\InterludeVacat\880901.txt / 880902.txt），
// 游戏会照着数据把选项摆进列表；点下去以后走 UIInterludePanel._onClickVacationMode，
// 这里把这两个编号拦下来，改成我们自己的行为（打开编辑窗 / 给角色加上【心】特质）。

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

        /// <summary>用游戏自己的提示条弹一句话。</summary>
        internal static void Hint(string text)
        {
            try
            {
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
                        HeartMenu.Hint("「" + HeartStore.SafeRoleName(tarRole) + "」已经有【心】了。");
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
                        HeartMenu.Hint("「" + HeartStore.SafeRoleName(tarRole) + "」还没有【心】，先用「添加心特质」。");
                        return false;
                    }
                    HeartEditorWindow.Open(tarRole);
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
