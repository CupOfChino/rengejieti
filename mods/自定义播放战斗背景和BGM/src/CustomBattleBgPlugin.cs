// 「自定义播放战斗背景和BGM」插件本体。
//
// 干的事：给每个调查员配一套自己的战斗背景 + 战斗BGM。
//   · 特质【领域】（881001）苏醒（进战斗）时自动展开：切换背景 + 播放 BGM；
//   · 战斗里还能用技艺「领域展开」（881005）手动再展开一次；
//   · 战斗结束（离开战斗）时，背景层销毁、BGM 比较后复原。
//
// 数据（特质/幕间入口/技艺）在 Project_Depersonal\Assets\Resources\Config\Game\ 下，
// 玩家的资源与配置存在 <mod 根目录>\CustomBattleAssets\ 下（不进上传包）。

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CustomBattleBg
{
    [BepInPlugin(Guid, "自定义播放战斗背景和BGM", "0.1.0")]
    public class CustomBattleBgPlugin : BaseUnityPlugin
    {
        public const string Guid = "codex.depersonal.custombattlebg";

        internal static ManualLogSource Log;
        internal static DomainStore Store;

        /// <summary>图片背景左右摇晃的幅度（uv 单位，0 = 不晃）。</summary>
        internal static float SwayAmplitude = DomainConstants.BgSwayAmplitude;

        /// <summary>图片背景左右摇晃一个来回的秒数。</summary>
        internal static float SwayPeriod = DomainConstants.BgSwayPeriod;

        /// <summary>"只占上半屏"模式遮住的高度比例（0.55 = 屏幕上方 55%）。</summary>
        internal static float HalfScreenRatio = DomainConstants.HalfScreenRatio;

        /// <summary>"只占上半屏"模式下视频上下黑边的比例（0.05 = 上下各留 5%）。</summary>
        internal static float VideoHalfEdge = DomainConstants.VideoHalfEdge;

        private float _timer;
        private bool _startupDone;

        private void Awake()
        {
            Log = Logger;
            Store = new DomainStore();
            try
            {
                Store.Load();
            }
            catch (Exception e)
            {
                LogError("读领域配置失败：" + e);
            }

            LogInfo("插件已加载，现有领域配置 " + Store.Profiles.Count + " 条");
            LogInfo("资源目录：" + Store.ModRoot);
            LogInfo("数据文件：" + Store.FilePath);

            SwayAmplitude = Config.Bind("背景摇晃", "幅度", DomainConstants.BgSwayAmplitude,
                "图片背景左右缓慢摇晃的幅度（uv 单位；0 = 不晃，0.005~0.02 比较自然；视频不受影响）").Value;
            SwayPeriod = Config.Bind("背景摇晃", "周期秒", DomainConstants.BgSwayPeriod,
                "图片背景摇晃一个来回的秒数（越大越慢）").Value;
            LogInfo("背景摇晃参数：幅度 " + SwayAmplitude + "，周期 " + SwayPeriod + " 秒");

            HalfScreenRatio = Config.Bind("背景显示", "上半屏比例", DomainConstants.HalfScreenRatio,
                "\"只占上半屏\"模式遮住屏幕上方多少（0.1~1；0.55 = 上方 55% 放背景、下方 45% 保留原背景）").Value;
            LogInfo("上半屏比例：" + HalfScreenRatio);

            VideoHalfEdge = Config.Bind("背景显示", "视频黑边", DomainConstants.VideoHalfEdge,
                "\"只占上半屏\"模式下，视频四周留出的黑边（相对上方区域高度；0.05 = 上下各留 5%）").Value;
            LogInfo("视频黑边比例：" + VideoHalfEdge);

            try
            {
                Harmony harmony = new Harmony(Guid);
                PatchOne(harmony, typeof(Patch_InterludeVacation));
                PatchOne(harmony, typeof(Patch_UpdateVacationInfo));
                PatchOne(harmony, typeof(Patch_BattleRole_TriggerBuffs));
                PatchOne(harmony, typeof(Patch_GameWorld_ExitBattle));
                PatchOne(harmony, typeof(Patch_GameWorld_RestartBattle));
                PatchOne(harmony, typeof(Patch_BattleActiveBehaviorData_Run));
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
            _timer += UnityEngine.Time.unscaledDeltaTime;
            if (_startupDone)
            {
                // 语言能在游戏设置里随时改，隔一会儿重贴一次英文文案
                if (_timer >= 10f)
                {
                    _timer = 0f;
                    DomainText.ApplyDataOverlay();
                }
                return;
            }
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
                DomainText.ApplyDataOverlay();
                LogInfo(DomainApi.DescribeStates());
                LogRoles();
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
                TraitResFactory factory = Singleton<ResManager>.Instance.TraitFactory;
                return factory != null && factory.All.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
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
                System.Collections.Generic.List<RoleLibraryData> roles = hall.HallData.HallLibrary.LibraryRoles;
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
    }

    // 编辑窗开着的时候，把游戏自己的快捷键整个屏蔽掉：
    // 不然在输入框里打字会触发游戏里的功能（Tab/I/P/E/Q 那些），名字根本没法取。
    // 只留 Esc，由窗口自己处理成"关闭"。
    [HarmonyPatch(typeof(InputResponseData), "Run", new Type[0])]
    internal static class Patch_InputResponseData_Run
    {
        private static bool Prefix()
        {
            return !DomainEditorWindow.IsOpen;
        }
    }

    // 顺带停掉 WASD 的移动方向检测，免得打字时角色乱走
    [HarmonyPatch(typeof(KeyboardEventManager), "_UpdateRoleMoveDir")]
    internal static class Patch_KeyboardEventManager_MoveDir
    {
        private static bool Prefix()
        {
            return !DomainEditorWindow.IsOpen;
        }
    }

    // 光拦游戏自己的快捷键还不够：Unity 的 UGUI 输入模块会把 W/S/A/D、方向键当成
    // "上下左右"来切换选中的控件。窗口开着时把导航事件也挡掉（鼠标点击和文本输入不受影响）。
    [HarmonyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "SendMoveEventToSelectedObject")]
    internal static class Patch_InputModule_Move
    {
        private static bool Prefix()
        {
            return !DomainEditorWindow.IsOpen;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "SendSubmitEventToSelectedObject")]
    internal static class Patch_InputModule_Submit
    {
        private static bool Prefix()
        {
            return !DomainEditorWindow.IsOpen;
        }
    }
}
