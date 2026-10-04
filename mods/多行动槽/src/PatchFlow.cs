// 战斗流程补丁：
//   1) 每幕开始重算槽数（UpdateControlRoleOnRoundStart，同步方法）；
//   2) 选行动阶段逐槽选择（RoleEnterActionStage）；
//   3) 出手阶段逐槽执行（OnCalculationResult 整体替换）；
//   4) 离开 / 重开战斗时清理状态。
//
// 所有 Prefix：先 try；异常 → 记日志 → return true（走原版）。
// 被跳过的 async Task 方法必须自己塞 __result（见 AI协作文档 第 10 节坑 6）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.Events;

namespace DuoActionSlot
{
    internal static class SlotCleanup
    {
        internal static void Run(string reason)
        {
            try
            {
                SlotManager.ClearAll(reason);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("清理状态失败：" + e.Message);
            }
            try
            {
                SlotHudView.ClearAll(reason);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("清理 HUD 失败：" + e.Message);
            }
        }
    }

    // ---------------- 1) 每幕开始重算槽数 ----------------

    [HarmonyPatch(typeof(BattleFightContent), "UpdateControlRoleOnRoundStart")]
    internal static class Patch_RoundStart
    {
        private static void Postfix()
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return;
                }
                SlotManager.RecalculateAll("进入战斗 / 每一幕开始");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("重算行动槽出错：" + e);
            }
        }
    }

    // ---------------- 2) 选行动阶段：逐槽选择 ----------------

    [HarmonyPatch(typeof(BattleFightContent), "RoleEnterActionStage")]
    internal static class Patch_RoleEnterActionStage
    {
        private static bool Prefix(BattleRole role, UnityAction callback)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                SlotState state = SlotManager.Peek(role);
                if (state == null || state.SlotCount <= 1)
                {
                    return true;   // 单槽：走原版
                }
                RunSelectLoop(role, state, callback);
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("选行动循环挂载失败，退回原版：" + e);
                return true;
            }
        }

        private static async void RunSelectLoop(BattleRole role, SlotState state, UnityAction callback)
        {
            try
            {
                state.SyncFromGame(role);
                int count = state.SlotCount;
                bool unable = false;
                bool uiDriven = false;
                try
                {
                    unable = role.IsUnableAct || role.SkipBattleAction;
                    // 玩家 / 可控制的友方：ActionSelect 会等 UI 的 IsActionDecision，
                    // 推进槽位的动作放在 UI 钩子（Patch_NextPhaseClicked）里做。
                    //
                    // 注意：判断"玩家角色"不能用 Data.CanControlAI——它对调查员（BattlePlayerRole）恒为 false，
                    // 只有"可控制 AI 的友方/NPC"才是 true。玩家角色要用 IsHero / 类型来判断。
                    uiDriven = role.IsHero
                        || role is BattlePlayerRole
                        || (role.IsAlly && role.Data != null && role.Data.CanControlAI);
                }
                catch (Exception)
                {
                }
                if (unable)
                {
                    // 原版分支：清骰子 / 消耗跳过标记；所有槽按跳过处理。
                    try
                    {
                        await role.ActionSelect(role);
                    }
                    catch (Exception e)
                    {
                        DuoActionSlotPlugin.LogError("跳过选行动出错：" + e.Message);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        state.Slots[i].Skipped = true;
                    }
                    DuoActionSlotPlugin.LogInfo(string.Format("跳过选行动：{0}（{1} 个槽）",
                        SlotManager.SafeName(role), count));
                }
                else if (uiDriven)
                {
                    // 关键：原版的 ActionSelect 是"整个选行动阶段只等一次 IsActionDecision"，
                    // 不是每个槽返回一次。所以这里只调一次，让玩家在一次 ActionSelect 里
                    // 逐槽选完（每次点确认由 Patch_NextPhaseClicked 把指针推到下一个槽）。
                    state.EditingIndex = 0;
                    state.SetPointer(role, 0);
                    role.IsActionDecision = false;
                    role.IsActionSelectionFinish = false;
                    DuoActionSlotPlugin.LogInfo(string.Format("开始逐槽选择：{0}（共 {1} 个槽）",
                        SlotManager.SafeName(role), count));
                    try
                    {
                        await role.ActionSelect(role);
                    }
                    catch (Exception e)
                    {
                        DuoActionSlotPlugin.LogError("逐槽选择出错：" + e);
                    }
                }
                else
                {
                    // AI：每个槽单独跑一次 EnterAction（BattleNpcRole 会自己设置 IsActionDecision）。
                    for (int i = 0; i < count; i++)
                    {
                        bool dead = false;
                        try
                        {
                            dead = role.IsDeath || role.IsUnableAct;
                        }
                        catch (Exception)
                        {
                        }
                        if (dead)
                        {
                            state.Slots[i].Skipped = true;
                            continue;
                        }
                        state.EditingIndex = i;
                        state.SetPointer(role, i);
                        role.IsActionDecision = false;
                        role.IsActionSelectionFinish = false;
                        DuoActionSlotPlugin.LogInfo(string.Format("选行动开始：{0} 第 {1}/{2} 个槽",
                            SlotManager.SafeName(role), i + 1, count));
                        try
                        {
                            await role.ActionSelect(role);
                        }
                        catch (Exception e)
                        {
                            DuoActionSlotPlugin.LogError(string.Format("选行动出错（{0} 第 {1} 个槽）：{2}",
                                SlotManager.SafeName(role), i + 1, e));
                            state.Slots[i].Skipped = true;
                            break;
                        }
                        SlotManager.SetChosen(role, i, !SlotManager.IsEmptyAction(state.Slots[i].Data));
                        if (DuoActionSlotPlugin.Verbose)
                        {
                            DuoActionSlotPlugin.LogInfo(string.Format("选行动完成：{0} 第 {1}/{2} 个槽（{3}）",
                                SlotManager.SafeName(role), i + 1, count,
                                state.Slots[i].Chosen ? "已选" : "空"));
                        }
                    }
                }
                state.EditingIndex = 0;
                state.SetPointer(role, 0);
                state.UiDirty = true;
                SlotManager.MarkDirty(role);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("选行动循环出错：" + e);
            }
            finally
            {
                // 无论成败都要推进主循环，绝不能卡住 ActionStage。
                try
                {
                    if (callback != null)
                    {
                        callback.Invoke();
                    }
                }
                catch (Exception e)
                {
                    DuoActionSlotPlugin.LogError("选行动回调出错：" + e.Message);
                }
            }
        }
    }

    // ---------------- 3) 出手阶段：逐槽执行 ----------------

    [HarmonyPatch(typeof(BattleFightContent), "OnCalculationResult")]
    internal static class Patch_OnCalculationResult
    {
        private static bool Prefix(BattleFightContent __instance, ref Task __result)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                if (!HasAnyMultiSlot(__instance))
                {
                    return true;   // 整场没有多槽角色：完全走原版
                }
                __result = RunCalculation(__instance);
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("出手循环挂载失败，退回原版：" + e);
                return true;
            }
        }

        private static bool HasAnyMultiSlot(BattleFightContent content)
        {
            try
            {
                if (content.Allies != null)
                {
                    for (int i = 0; i < content.Allies.Count; i++)
                    {
                        if (SlotManager.IsMultiSlot(content.Allies[i]))
                        {
                            return true;
                        }
                    }
                }
                if (content.CurWaveEnemies != null)
                {
                    for (int i = 0; i < content.CurWaveEnemies.Count; i++)
                    {
                        if (SlotManager.IsMultiSlot(content.CurWaveEnemies[i]))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>照抄原版 OnCalculationResult，只把"每角色执行一次"改成"逐槽执行"。</summary>
        private static async Task RunCalculation(BattleFightContent content)
        {
            content.CurActionRole = null;
            await PrepareMultiSlotRoundStart(content);
            while (content.SequenceList.Count > 0)
            {
                if (content.CurrentResult != EFightResult.None)
                {
                    continue;
                }
                BattleRole attackRole = content.SequenceList[0];
                await SlotWait.Seconds(0.3f);

                if (attackRole.IsEscape)
                {
                    content.SequenceList.RemoveAt(0);
                    if (content.IsFightOver)
                    {
                        break;
                    }
                    await content.CheckFightOver(includeRound: true);
                    if (content.IsFightOver)
                    {
                        break;
                    }
                    continue;
                }

                SlotState state = SlotManager.Peek(attackRole);
                int count = state != null && state.SlotCount > 0 ? state.SlotCount : 1;
                // 角色级的"行动开始 / 行动结束"判定默认只做一次（首个槽）。
                // 原版类似怀表（Buff_TriggerBattleActionEffect）的"行动结束后重复上一次行动"，
                // 因此只会以第一个行动槽为基准触发一次——**该场景作者未实测，实际效果未知**。
                bool perSlot = DuoActionSlotPlugin.EachSlotBuffs != null && DuoActionSlotPlugin.EachSlotBuffs.Value;
                if (state != null)
                {
                    state.SyncFromGame(attackRole);
                    state.Executing = true;
                    state.ExecIndex = 0;
                }

                for (int index = 0; index < count; index++)
                {
                    if (content.IsFightOver || content.CurrentResult != EFightResult.None)
                    {
                        break;
                    }
                    try
                    {
                        if (attackRole.IsDeath)
                        {
                            break;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    if (state != null)
                    {
                        state.ExecIndex = index;
                        state.SetPointer(attackRole, index);
                    }

                    BattleActiveBehaviorData behavior = attackRole.CurrentBehaviourData;
                    if (behavior == null)
                    {
                        if (state != null)
                        {
                            state.Slots[index].Skipped = true;
                        }
                        continue;
                    }
                    if (SlotManager.IsEmptyAction(behavior))
                    {
                        // 空行动也照原版走一遍 Run：Run 内部会走 ClearOnActionEnd 收尾，
                        // 保持牌子的显隐与原版一致（正常流程不会出现空槽，这里只是兜底）。
                        DuoActionSlotPlugin.LogInfo(string.Format("执行空行动槽：{0} 第 {1}/{2} 个",
                            SlotManager.SafeName(attackRole), index + 1, count));
                    }

                    content.CurActionRole = attackRole;
                    EBattleActionType actionType = EBattleActionType.None;
                    BattleSkillData battleSkill = null;
                    MagicSkillData magicData = null;
                    ETargetSelect targetType = ETargetSelect.None;
                    List<BattleRole> actionTargets = new List<BattleRole>();
                    if (behavior.BattleSkillData == null || behavior.MagicData == null)
                    {
                        actionType = behavior.ActionType;
                        battleSkill = behavior.BattleSkillData;
                        magicData = behavior.MagicData;
                        targetType = behavior.TargetSelectType;
                        actionTargets.AddRange(behavior.Targets);
                    }

                    DuoActionSlotPlugin.LogInfo(string.Format("执行行动：{0} 第 {1}/{2} 个槽（{3}）",
                        SlotManager.SafeName(attackRole), index + 1, count,
                        battleSkill != null ? "技能/攻击" : (magicData != null ? "法术" : "其它")));

                    await Singleton<GameEventManager>.Instance.TriggerEvent(EGameEvent_BattleTrigger.ActionStart);
                    if (index == 0 || perSlot)
                    {
                        await attackRole.ActionStart();
                    }
                    await UpdateHudSkillShow(content, true);
                    await behavior.Run();
                    if (actionType != EBattleActionType.None && (battleSkill != null || magicData != null) &&
                        targetType != ETargetSelect.None)
                    {
                        content.SaveRoleSkillSelectRecord(attackRole, actionType, battleSkill, magicData,
                            targetType, actionTargets);
                    }
                    if (index == 0 || perSlot)
                    {
                        // 怀表一类"行动结束触发"的效果走这里：默认只对首个槽触发一次。
                        await attackRole.ActionEnd();
                    }
                    content.CurActionRole = null;
                    await UpdateHudSkillShow(content, false);
                    if (state != null)
                    {
                        SlotManager.SetExecuted(attackRole, index);
                    }

                    while (content.BattlePerformPause)
                    {
                        await SlotWait.EndOfFrame();
                    }
                    if (content.IsRestartBattleBreak || content.IsGiveUpBattleBreak || content.IsFightOver)
                    {
                        break;
                    }
                    await content.CheckFightOver(includeRound: true);
                    if (content.IsFightOver)
                    {
                        break;
                    }
                    content.CurActionRole = attackRole;
                }

                if (state != null)
                {
                    state.Executing = false;
                }
                content.CurActionRole = null;
                content.SequenceList.RemoveAt(0);
                if (content.IsRestartBattleBreak || content.IsGiveUpBattleBreak || content.IsFightOver)
                {
                    break;
                }
                await content.CheckFightOver(includeRound: true);
                if (content.IsFightOver)
                {
                    break;
                }
            }
        }

        /// <summary>原版 _UpdateHUDSkillShow 的等价实现（成员全是 public，不需要反射）。</summary>
        private static async Task UpdateHudSkillShow(BattleFightContent content, bool beforeAction)
        {
            try
            {
                if (content.Allies != null)
                {
                    for (int i = 0; i < content.Allies.Count; i++)
                    {
                        await UpdateOneHud(content.Allies[i], beforeAction);
                    }
                }
                if (content.CurWaveEnemies != null)
                {
                    for (int i = 0; i < content.CurWaveEnemies.Count; i++)
                    {
                        await UpdateOneHud(content.CurWaveEnemies[i], beforeAction);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新行动槽显隐失败：" + e.Message);
            }
        }

        private static async Task UpdateOneHud(BattleRole role, bool beforeAction)
        {
            try
            {
                if (role == null || role.Model == null || role.Model.Hud == null)
                {
                    return;
                }
                if (role.CheckCloseSkillIcon(beforeAction))
                {
                    await role.Model.Hud.CloseSkillIcon();
                }
                else
                {
                    role.Model.Hud.ActiveSkillIcon();
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新角色牌子失败：" + e.Message);
            }
        }

        /// <summary>
        /// 原版 ResultStage 开头只对 CurrentBehaviourData（槽 0）做
        /// ConfirmNPCFinalTargets / TriggeOptionEffect(RoundStart)；
        /// 这里把其余槽的这两步补齐（放在演出之前）。
        /// </summary>
        private static async Task PrepareMultiSlotRoundStart(BattleFightContent content)
        {
            try
            {
                if (content.Allies != null)
                {
                    for (int i = 0; i < content.Allies.Count; i++)
                    {
                        await PrepareOneRole(content.Allies[i]);
                    }
                }
                if (content.CurWaveEnemies != null)
                {
                    for (int i = 0; i < content.CurWaveEnemies.Count; i++)
                    {
                        await PrepareOneRole(content.CurWaveEnemies[i]);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("补齐多槽回合开始处理失败：" + e.Message);
            }
        }

        private static async Task PrepareOneRole(BattleRole role)
        {
            SlotState state = SlotManager.Peek(role);
            if (state == null || state.SlotCount <= 1)
            {
                return;
            }
            for (int i = 1; i < state.SlotCount; i++)
            {
                BattleActiveBehaviorData data = state.GetData(i);
                if (data == null)
                {
                    continue;
                }
                try
                {
                    data.ConfirmNPCFinalTargets();
                }
                catch (Exception e)
                {
                    DuoActionSlotPlugin.LogError("确认槽目标失败：" + e.Message);
                }
                try
                {
                    await data.TriggeOptionEffect(EBattleSkillTrigger.RoundStart, null);
                }
                catch (Exception e)
                {
                    DuoActionSlotPlugin.LogError("槽的回合开始效果出错：" + e.Message);
                }
            }
        }
    }

    // ---------------- 4) 离开 / 重开战斗：清理 ----------------

    [HarmonyPatch(typeof(GameWorld), "ExitBattle")]
    internal static class Patch_ExitBattle
    {
        private static void Prefix()
        {
            try
            {
                SlotCleanup.Run("离开战斗");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("离开战斗清理失败：" + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(GameWorld), "RestartBattle")]
    internal static class Patch_RestartBattle
    {
        private static void Prefix()
        {
            try
            {
                SlotCleanup.Run("重开战斗");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("重开战斗清理失败：" + e.Message);
            }
        }
    }

    internal static class PatchFlow
    {
        internal static void Register(Harmony harmony)
        {
            DuoActionSlotPlugin.LogInfo("开始挂载战斗流程钩子……");
            PatchOne(harmony, typeof(Patch_RoundStart));
            PatchOne(harmony, typeof(Patch_RoleEnterActionStage));
            PatchOne(harmony, typeof(Patch_OnCalculationResult));
            PatchOne(harmony, typeof(Patch_ExitBattle));
            PatchOne(harmony, typeof(Patch_RestartBattle));
        }

        private static void PatchOne(Harmony harmony, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                DuoActionSlotPlugin.LogInfo("钩子已挂：" + type.Name);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("钩子挂载失败 " + type.Name + "：" + e.Message);
            }
        }
    }
}
