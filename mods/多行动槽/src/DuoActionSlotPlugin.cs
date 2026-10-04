// 「多行动槽」插件本体。
//
// 做的事：
//   · 战斗中给每个角色维护 N 个行动槽（数据在 SlotManager）；
//   · 【速战速决】特质（882001）按速度加槽；其它模组可以走 ExtraActionSlotApi 加槽；
//   · 选行动阶段逐槽选择、出手阶段逐槽执行；
//   · 行动槽 HUD 一字排开、中心对称，并保持与「可视化攻击目标」兼容（不改那个模组）。
//
// 数据（特质 / 幕间入口）在 Project_Depersonal\Assets\Resources\Config\Game\ 下。
// 设计与调研见 docs\多行动槽-需求与设计.md。
//
// 重要约定：任何补丁出错都不允许影响游戏运行——所有 Prefix 都有 try/catch，
// 失败时退回原版逻辑；插件总开关关掉后完全不介入。

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DuoActionSlot
{
    [BepInPlugin(Guid, "多行动槽", "0.1.0")]
    public class DuoActionSlotPlugin : BaseUnityPlugin
    {
        public const string Guid = "codex.depersonal.duoactionslot";

        internal static ManualLogSource Log;

        /// <summary>总开关（config）。关掉后插件完全不介入战斗逻辑。
        /// 注意：不能叫 Enabled —— BaseUnityPlugin 自己有一个 Enabled 属性，会撞名。</summary>
        internal static bool ModEnabled = true;

        /// <summary>每个槽都触发行动开始/结束 buff。默认关；打开可能和原版 buff 预期冲突，不建议。</summary>
        internal static ConfigEntry<bool> EachSlotBuffs;

        /// <summary>允许同一行动装进多个槽。默认开（预留开关，当前只有提示与日志用途）。</summary>
        internal static bool AllowDuplicate = true;

        /// <summary>详细日志（调试用）。</summary>
        internal static ConfigEntry<bool> VerboseLog;

        internal static DuoActionSlotPlugin Instance;

        private Harmony _harmony;
        private float _textTimer;
        private float _notInBattleTime;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            try
            {
                ModEnabled = Config.Bind("常规", "EnableDuoActionSlot", true,
                    "总开关。关掉后本插件完全不改变战斗行为（原版单行动槽）。").Value;
                EachSlotBuffs = Config.Bind("兼容", "EachSlotTriggerBuffs", false,
                    "【可能有问题，不建议打开】true = 每个行动槽都触发一次行动开始/行动结束 buff；" +
                    "false（默认）= 只对首个行动槽判定一次。");
                AllowDuplicate = Config.Bind("常规", "AllowDuplicateAction", true,
                    "允许同一个行动装进多个行动槽（例如两个槽都选同一个技能）。").Value;
                VerboseLog = Config.Bind("调试", "VerboseLog", false,
                    "输出详细日志（槽指针切换、HUD 布局等），排查问题时打开。");

                _harmony = new Harmony(Guid);
                PatchFlow.Register(_harmony);
                PatchUi.Register(_harmony);
                PatchMenu.Register(_harmony);

                LogInfo("插件已加载（总开关 " + (ModEnabled ? "开" : "关") + "）");
                if (EachSlotBuffs != null && EachSlotBuffs.Value)
                {
                    LogInfo("注意：EachSlotTriggerBuffs = true，每个槽都会触发行动开始/结束 buff；" +
                            "这可能与部分原版 buff 的预期不符，建议改回 false。");
                }
            }
            catch (Exception e)
            {
                LogError("插件初始化失败：" + e);
            }
        }

        private void Update()
        {
            try
            {
                if (!ModEnabled)
                {
                    return;
                }
                SlotManager.Tick();
                SlotHudView.TickAll();
                PatchArrowCompat.Tick();
                CheckBattleState();

                // 语言表可能比插件晚加载：隔一会再尝试覆盖一次英文文案（只动我们的编号）。
                _textTimer += Time.unscaledDeltaTime;
                if (_textTimer > 3f)
                {
                    _textTimer = 0f;
                    SlotText.ApplyDataOverlay();
                }
            }
            catch (Exception e)
            {
                LogError("Update 出错：" + e.Message);
            }
        }

        /// <summary>兜底：不在战斗里超过 2 秒还有残留状态 → 清理（正常由 ExitBattle 钩子清）。</summary>
        private void CheckBattleState()
        {
            try
            {
                if (BattleHelper.IsInBattle)
                {
                    _notInBattleTime = 0f;
                    return;
                }
                if (SlotManager.Count <= 0)
                {
                    return;
                }
                _notInBattleTime += Time.unscaledDeltaTime;
                if (_notInBattleTime > 2f)
                {
                    _notInBattleTime = 0f;
                    SlotCleanup.Run("不在战斗状态（兜底清理）");
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void LogInfo(string message)
        {
            if (Log != null)
            {
                Log.LogInfo("[多行动槽] " + message);
            }
        }

        internal static void LogError(string message)
        {
            if (Log != null)
            {
                Log.LogError("[多行动槽] " + message);
            }
        }

        internal static bool Verbose
        {
            get { return VerboseLog != null && VerboseLog.Value; }
        }

        /// <summary>用插件自己的协程等游戏时间（受 timeScale 影响，和原版 WaitForSeconds 一致）。</summary>
        internal static void StartRoutine(System.Collections.IEnumerator routine)
        {
            if (Instance != null && routine != null)
            {
                Instance.StartCoroutine(routine);
            }
        }
    }
}
