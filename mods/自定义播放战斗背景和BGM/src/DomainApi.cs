// 给其它模组用的扩展接口。
//
// 提供两件事：
//   1. 开关幕间入口 —— 任何一个注册方说"关"（enabled = false），这个入口就不展示；
//   2. 重写幕间入口 —— 注册一个回调，玩家点这条幕间入口时走你的实现，而不是本模组的默认行为。
//
// 用法（在自己的插件里调用；CrossAssembly 的话用反射或引用本 DLL 都行）：
//   CustomBattleBg.DomainApi.SetMenuEnabled(CustomBattleBg.DomainApi.MenuEdit, "我的模组", false);
//   CustomBattleBg.DomainApi.SetMenuOverride(CustomBattleBg.DomainApi.MenuEdit, "我的模组", 我的回调);
//
// 选择后日志里会打印每个入口当前的状态（谁关的），方便排查。

using System;
using System.Collections.Generic;
using System.Text;

namespace CustomBattleBg
{
    public static class DomainApi
    {
        /// <summary>幕间入口：添加「领域」。</summary>
        public const string MenuAdd = "add";

        /// <summary>幕间入口：编辑自定义面板。</summary>
        public const string MenuEdit = "edit";

        /// <summary>幕间入口：移除「领域」。</summary>
        public const string MenuRemove = "remove";

        // menuKey → (requester → 是否允许)
        private static readonly Dictionary<string, Dictionary<string, bool>> EnabledStates =
            new Dictionary<string, Dictionary<string, bool>>();

        // menuKey → (请求方, 回调)；同一个入口取最后注册的那个
        private static readonly Dictionary<string, KeyValuePair<string, Action<HeroRoleData>>> OverrideHandlers =
            new Dictionary<string, KeyValuePair<string, Action<HeroRoleData>>>();

        /// <summary>
        /// 设置某个幕间入口的开/关。requester 填自己模组的名字（用于日志排查）。
        /// 任何一方设为 false，这个入口就不会展示。
        /// </summary>
        public static void SetMenuEnabled(string menuKey, string requester, bool enabled)
        {
            try
            {
                if (string.IsNullOrEmpty(menuKey))
                {
                    return;
                }
                if (string.IsNullOrEmpty(requester))
                {
                    requester = "未知模组";
                }
                Dictionary<string, bool> states;
                if (!EnabledStates.TryGetValue(menuKey, out states))
                {
                    states = new Dictionary<string, bool>();
                    EnabledStates[menuKey] = states;
                }
                states[requester] = enabled;
                CustomBattleBgPlugin.LogInfo("幕间入口「" + DescribeKey(menuKey) + "」被「" + requester + "」设置为 " +
                    (enabled ? "开启" : "关闭"));
                CustomBattleBgPlugin.LogInfo(DescribeStates());
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("设置幕间入口开关失败：" + e.Message);
            }
        }

        /// <summary>这个入口现在是否可用（所有注册方都允许才可用；没人注册过就是可用）。</summary>
        public static bool IsMenuEnabled(string menuKey)
        {
            try
            {
                Dictionary<string, bool> states;
                if (string.IsNullOrEmpty(menuKey) || !EnabledStates.TryGetValue(menuKey, out states))
                {
                    return true;
                }
                foreach (KeyValuePair<string, bool> kv in states)
                {
                    if (!kv.Value)
                    {
                        return false;   // 只要有一个模组关了，就不展示
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 重写某个幕间入口的行为：玩家点击时执行 handler（传入选中的调查员），不再走默认逻辑。
        /// 同一个入口重复注册时，最后注册的生效。
        /// </summary>
        public static void SetMenuOverride(string menuKey, string requester, Action<HeroRoleData> handler)
        {
            try
            {
                if (string.IsNullOrEmpty(menuKey) || handler == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(requester))
                {
                    requester = "未知模组";
                }
                OverrideHandlers[menuKey] = new KeyValuePair<string, Action<HeroRoleData>>(requester, handler);
                CustomBattleBgPlugin.LogInfo("幕间入口「" + DescribeKey(menuKey) + "」的行为被「" + requester + "」重写");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("注册幕间入口重写失败：" + e.Message);
            }
        }

        /// <summary>取消自己的重写（只有当前生效的是自己时才清掉）。</summary>
        public static void ClearMenuOverride(string menuKey, string requester)
        {
            try
            {
                if (string.IsNullOrEmpty(menuKey))
                {
                    return;
                }
                KeyValuePair<string, Action<HeroRoleData>> cur;
                if (OverrideHandlers.TryGetValue(menuKey, out cur) && cur.Key == requester)
                {
                    OverrideHandlers.Remove(menuKey);
                    CustomBattleBgPlugin.LogInfo("幕间入口「" + DescribeKey(menuKey) + "」的重写被「" + requester + "」取消");
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("取消幕间入口重写失败：" + e.Message);
            }
        }

        /// <summary>内部：取当前生效的重写回调。</summary>
        internal static Action<HeroRoleData> GetMenuOverride(string menuKey)
        {
            KeyValuePair<string, Action<HeroRoleData>> cur;
            if (!string.IsNullOrEmpty(menuKey) && OverrideHandlers.TryGetValue(menuKey, out cur))
            {
                return cur.Value;
            }
            return null;
        }

        /// <summary>把当前状态拼成一段日志（谁关了哪些入口）。</summary>
        internal static string DescribeStates()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("幕间入口状态：");
            string[] keys = new string[] { MenuAdd, MenuEdit, MenuRemove };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                sb.Append("\n  ").Append(DescribeKey(key)).Append("：");
                Dictionary<string, bool> states;
                if (!EnabledStates.TryGetValue(key, out states) || states.Count == 0)
                {
                    sb.Append("正常（没有模组注册开关）");
                }
                else
                {
                    List<string> off = new List<string>();
                    List<string> on = new List<string>();
                    foreach (KeyValuePair<string, bool> kv in states)
                    {
                        if (kv.Value)
                        {
                            on.Add(kv.Key);
                        }
                        else
                        {
                            off.Add(kv.Key);
                        }
                    }
                    if (off.Count > 0)
                    {
                        sb.Append("已关闭（关闭者：").Append(string.Join("、", off.ToArray())).Append("）");
                    }
                    else
                    {
                        sb.Append("正常（开启者：").Append(string.Join("、", on.ToArray())).Append("）");
                    }
                }
            }
            return sb.ToString();
        }

        internal static string DescribeKey(string menuKey)
        {
            switch (menuKey)
            {
                case MenuAdd: return "添加「领域」";
                case MenuEdit: return "编辑自定义面板";
                case MenuRemove: return "移除「领域」";
                default: return menuKey;
            }
        }
    }
}
