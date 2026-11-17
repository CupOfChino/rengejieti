// 幕间的HeroRoleData可能没有回填RoleLibraryKey，稳定编号应从角色库取回。
using System;
using System.Collections.Generic;

namespace XinEditor
{
    internal static class HeartVisualRoleKey
    {
        internal static string Resolve(HeroRoleData role)
        {
            if (role == null) return "";
            if (!string.IsNullOrEmpty(role.RoleLibraryKey)) return role.RoleLibraryKey;
            try
            {
                if (Singleton<HallWorld>.HasInstance && Singleton<HallWorld>.Instance.HallData != null)
                    return FromLibrary(role, Singleton<HallWorld>.Instance.HallData.HallLibrary.LibraryRoles);
            }
            catch (Exception e) { XinEditorPlugin.LogError("查询心外观调查员编号失败：" + e.Message); }
            return "";
        }

        internal static string FromLibrary(HeroRoleData role, List<RoleLibraryData> library)
        {
            if (role == null) return "";
            if (!string.IsNullOrEmpty(role.RoleLibraryKey)) return role.RoleLibraryKey;
            if (library == null) return "";
            foreach (RoleLibraryData item in library)
                if (item != null && ReferenceEquals(item.BaseData, role) && !string.IsNullOrEmpty(item.Key))
                    return item.Key;
            // 幕间若传了副本，只在大厅中名字唯一时匹配，避免同名调查员串用配置。
            string name = HeartStore.SafeRoleName(role), found = "";
            if (string.IsNullOrEmpty(name)) return "";
            foreach (RoleLibraryData item in library)
            {
                if (item == null || item.BaseData == null || string.IsNullOrEmpty(item.Key) ||
                    HeartStore.SafeRoleName(item.BaseData) != name) continue;
                if (!string.IsNullOrEmpty(found)) return "";
                found = item.Key;
            }
            return found;
        }
    }
}
