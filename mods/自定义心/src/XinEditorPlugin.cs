// 「自定义心」插件本体。
//
// 干的事：让每个调查员的【心】各有一套自己的数值和名字。
//   · 数据包里的默认心（特质 880901 / 状态 880901）原样保留，没有自定义心的角色走它；
//   · 有自定义心的角色，进战斗挂状态时被这里拦下来，换成运行时生成的那颗；
//   · 自定义心的内容和游戏存档无关，存在游戏目录下的 XinEditor\hearts.cfg。
//
// 第一步只做「数据 + 战斗生效」；幕间里的编辑窗口是第二步。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MOD;

namespace XinEditor
{
    [BepInPlugin(Guid, "自定义心", "0.1.0")]
    public class XinEditorPlugin : BaseUnityPlugin
    {
        public const string Guid = "codex.depersonal.xineditor";

        internal static ManualLogSource Log;
        internal static HeartStore Store;

        private ConfigEntry<bool> _debugAutoHeart;
        private float _timer;
        private bool _startupDone;

        private void Awake()
        {
            Log = Logger;
            Store = new HeartStore();
            try
            {
                Store.Load();
            }
            catch (Exception e)
            {
                LogError("读自定义心数据失败：" + e);
            }

            _debugAutoHeart = Config.Bind("调试", "自动生成测试心", false,
                "开发期开关：开启后，如果一条自定义心都没有，会自动给大厅第一个调查员生成一颗测试心。平时保持关闭。");

            LogInfo("插件已加载，现有自定义心 " + Store.Hearts.Count + " 条");
            LogInfo("数据文件：" + Store.FilePath);

            try
            {
                Harmony harmony = new Harmony(Guid);
                PatchOne(harmony, typeof(Patch_ActiveBuffOption_Active));
                PatchOne(harmony, typeof(Patch_ActiveBuffOption_UnActive));
                PatchOne(harmony, typeof(Patch_InterludeVacation));
                PatchOne(harmony, typeof(Patch_InputResponseData_Run));
                PatchOne(harmony, typeof(Patch_KeyboardEventManager_MoveDir));
                PatchOne(harmony, typeof(Patch_InputModule_Move));
                PatchOne(harmony, typeof(Patch_InputModule_Submit));
                LogInfo("钩子挂载结束");
            }
            catch (Exception e)
            {
                LogError("挂钩子失败：" + e);
            }
        }

        // 一个一个挂：万一某个方法在游戏更新后改名了，也只丢它自己，不会连累别的钩子
        private static void PatchOne(Harmony harmony, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                LogInfo("钩子已挂：" + type.Name);
            }
            catch (Exception e)
            {
                LogError("钩子挂失败（" + type.Name + "）：" + e.Message);
            }
        }

        private void Update()
        {
            if (_startupDone)
            {
                return;
            }
            _timer += UnityEngine.Time.unscaledDeltaTime;
            if (_timer < 3f)
            {
                return;
            }
            _timer = 0f;

            try
            {
                if (!IsGameReady())
                {
                    return;
                }
                _startupDone = true;
                EnsureAllRegistered();
                LogRoles();
                if (_debugAutoHeart != null && _debugAutoHeart.Value && Store.Hearts.Count == 0)
                {
                    CreateTestHeart();
                }
            }
            catch (Exception e)
            {
                LogError("初始化出错：" + e);
            }
        }

        private static bool IsGameReady()
        {
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return false;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                return factory != null && factory.All.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 把文件里所有的自定义心都注册进游戏状态表
        internal static bool EnsureAllRegistered()
        {
            bool allOk = true;
            for (int i = 0; i < Store.Hearts.Count; i++)
            {
                HeartDefinition h = Store.Hearts[i];
                int before = h.BuffId;
                int id = HeartBuffBuilder.EnsureRegistered(h);
                if (id == 0)
                {
                    allOk = false;
                }
                else if (id != before)
                {
                    Store.Save(h);
                }
            }
            return allOk;
        }

        internal static void LogInfo(string msg)
        {
            if (Log != null)
            {
                Log.LogInfo(msg);
            }
        }

        internal static void LogError(string msg)
        {
            if (Log != null)
            {
                Log.LogError(msg);
            }
        }

        // 把当前存档里的调查员名单打到日志里，方便确认编号和名字
        private static void LogRoles()
        {
            try
            {
                if (!Singleton<HallWorld>.HasInstance)
                {
                    LogInfo("还不在大厅里，调查员名单稍后再列");
                    return;
                }
                HallWorld hall = Singleton<HallWorld>.Instance;
                if (hall == null || hall.HallData == null)
                {
                    return;
                }
                List<RoleLibraryData> roles = hall.HallData.HallLibrary.LibraryRoles;
                LogInfo("当前存档里的调查员共 " + roles.Count + " 个：");
                for (int i = 0; i < roles.Count; i++)
                {
                    HeroRoleData baseData = roles[i].BaseData;
                    string name = baseData != null ? baseData.Name : "?";
                    LogInfo("  " + (i + 1) + ". 名字=" + name + "  编号=" + roles[i].Key);
                }
            }
            catch (Exception e)
            {
                LogError("列调查员失败：" + e.Message);
            }
        }

        // 开发期用：给第一个调查员造一颗测试心，验证数值是不是真的加对了
        private static void CreateTestHeart()
        {
            try
            {
                if (!Singleton<HallWorld>.HasInstance)
                {
                    return;
                }
                List<RoleLibraryData> roles = Singleton<HallWorld>.Instance.HallData.HallLibrary.LibraryRoles;
                if (roles.Count == 0)
                {
                    return;
                }
                RoleLibraryData lib = roles[0];

                HeartDefinition h = new HeartDefinition();
                h.Section = Store.NextSection();
                h.RoleKey = lib.Key;
                h.RoleName = HeartStore.SafeRoleName(lib.BaseData);
                h.Suffix = "测试";
                h.Speed = 40;
                h.Stats.Add(new HeartStat(HeartStatType.DamageBonus, 5));
                h.Stats.Add(new HeartStat(HeartStatType.DamageReduce, 2));
                h.Stats.Add(new HeartStat(HeartStatType.Occultism, 20));
                h.BuffId = Store.AllocateBuffId();
                Store.Save(h);
                int id = HeartBuffBuilder.EnsureRegistered(h);
                LogInfo("调试用测试心已生成给「" + h.RoleName + "」：" + h.DisplayName + "，状态编号 " + id);
            }
            catch (Exception e)
            {
                LogError("生成测试心失败：" + e);
            }
        }
    }

    // 进战斗挂状态时：有自定义心就换掉，没有就原样走数据包里的默认心
    [HarmonyPatch(typeof(MOD.TraitEvent.ActiveBuffOption), "Active")]
    internal static class Patch_ActiveBuffOption_Active
    {
        private static bool Prefix(MOD.TraitEvent.ActiveBuffOption __instance, RoleData role, ref Task __result)
        {
            try
            {
                if (__instance.BuffId != HeartConstants.DefaultHeartBuffId)
                {
                    return true;
                }
                if (role == null || role.Role == null)
                {
                    return true;
                }
                HeartDefinition def = XinEditorPlugin.Store.Find(role);
                if (def == null)
                {
                    XinEditorPlugin.LogInfo("「" + HeartStore.SafeRoleName(role) + "」没有自定义心，走默认心");
                    return true;
                }
                int id = HeartBuffBuilder.EnsureRegistered(def);
                if (id <= 0 || id == HeartConstants.DefaultHeartBuffId)
                {
                    return true;
                }
                XinEditorPlugin.LogInfo("「" + def.RoleName + "」挂上自定义心：" + def.DisplayName);
                __result = ApplyCustomHeart(role, id);
                return false;
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("换自定义心失败，回退默认心：" + e);
                return true;
            }
        }

        // 换心之前先把可能残留的默认心摘掉，免得一个角色身上挂两颗心
        private static async Task ApplyCustomHeart(RoleData role, int customId)
        {
            try
            {
                await role.Role.RemoveBuff(HeartConstants.DefaultHeartBuffId);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("清默认心失败：" + e.Message);
            }
            await role.Role.AddBuff(role.Role, customId);
        }
    }

    // 特质沉睡或卸下时，要移除的是自定义的那颗，而不是默认心
    [HarmonyPatch(typeof(MOD.TraitEvent.ActiveBuffOption), "UnActive")]
    internal static class Patch_ActiveBuffOption_UnActive
    {
        private static bool Prefix(MOD.TraitEvent.ActiveBuffOption __instance, RoleData role, ref Task __result)
        {
            try
            {
                if (__instance.BuffId != HeartConstants.DefaultHeartBuffId || role == null || role.Role == null)
                {
                    return true;
                }
                HeartDefinition def = XinEditorPlugin.Store.Find(role);
                if (def == null || def.BuffId < HeartConstants.CustomBuffIdMin ||
                    def.BuffId > HeartConstants.CustomBuffIdMax)
                {
                    return true;
                }
                __result = role.Role.RemoveBuff(def.BuffId);
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    // 编辑窗开着的时候，把游戏自己的快捷键整个屏蔽掉：
    // 不然在输入框里打字会触发游戏里的功能（Tab/I/P/E/Q 那些），名字根本没法取。
    // 只留 Esc，由窗口自己处理成"关闭"。
    [HarmonyPatch(typeof(InputResponseData), "Run", new Type[0])]
    internal static class Patch_InputResponseData_Run
    {
        private static bool Prefix()
        {
            return !HeartEditorWindow.IsOpen;
        }
    }

    // 顺带停掉 WASD 的移动方向检测，免得打字时角色乱走
    [HarmonyPatch(typeof(KeyboardEventManager), "_UpdateRoleMoveDir")]
    internal static class Patch_KeyboardEventManager_MoveDir
    {
        private static bool Prefix()
        {
            return !HeartEditorWindow.IsOpen;
        }
    }

    // 光拦游戏自己的快捷键还不够：Unity 的 UGUI 输入模块会把 W/S/A/D、方向键当成
    // "上下左右"来切换选中的控件——幕间列表被按走就是这么来的。
    // 窗口开着时把导航事件也挡掉（鼠标点击和文本输入不受影响）。
    [HarmonyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "SendMoveEventToSelectedObject")]
    internal static class Patch_InputModule_Move
    {
        private static bool Prefix()
        {
            return !HeartEditorWindow.IsOpen;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "SendSubmitEventToSelectedObject")]
    internal static class Patch_InputModule_Submit
    {
        private static bool Prefix()
        {
            return !HeartEditorWindow.IsOpen;
        }
    }
}
