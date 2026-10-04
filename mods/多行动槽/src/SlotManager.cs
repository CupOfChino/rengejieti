// 行动槽的核心管理：
//   · 每个战斗角色一份 SlotState（战斗内才有）；
//   · 每幕开始重算槽数（速战速决特质 + 外部 API）；
//   · 选行动阶段的"推进到下一个槽"；
//   · 战斗结束清理 + HUD 刷新队列。
//
// 所有对外入口都做了兜底：拿不到状态、参数非法、内部异常 → 安静返回 / 走原版单槽，
// 绝不把异常抛进游戏的主循环。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using MOD;

namespace DuoActionSlot
{
    internal static class SlotManager
    {
        private static readonly Dictionary<BattleRole, SlotState> States = new Dictionary<BattleRole, SlotState>();
        private static readonly Queue<BattleRole> DirtyQueue = new Queue<BattleRole>();
        private static readonly HashSet<BattleRole> DirtySet = new HashSet<BattleRole>();

        // ---------------- 查询 ----------------

        internal static SlotState Peek(BattleRole role)
        {
            if (role == null)
            {
                return null;
            }
            SlotState state;
            return States.TryGetValue(role, out state) ? state : null;
        }

        internal static SlotState GetOrCreate(BattleRole role)
        {
            if (role == null)
            {
                return null;
            }
            SlotState state;
            if (!States.TryGetValue(role, out state))
            {
                state = new SlotState();
                state.Role = role;
                States[role] = state;
            }
            return state;
        }

        /// <summary>这个角色当前是不是多槽（没状态 = 不是）。</summary>
        internal static bool IsMultiSlot(BattleRole role)
        {
            SlotState state = Peek(role);
            return state != null && state.SlotCount > 1;
        }

        internal static int GetSlotCountSafe(BattleRole role)
        {
            SlotState state = Peek(role);
            if (state != null)
            {
                return Math.Max(1, state.SlotCount);
            }
            return 1;
        }

        // ---------------- 每幕重算 ----------------

        internal static void RecalculateAll(string reason)
        {
            try
            {
                BattleFightContent content = BattleHelper.FightContent;
                if (content == null)
                {
                    return;
                }
                List<BattleRole> roles = new List<BattleRole>();
                if (content.Allies != null)
                {
                    roles.AddRange(content.Allies);
                }
                if (content.CurWaveEnemies != null)
                {
                    for (int i = 0; i < content.CurWaveEnemies.Count; i++)
                    {
                        BattleRole role = content.CurWaveEnemies[i];
                        if (role != null)
                        {
                            roles.Add(role);
                        }
                    }
                }
                int round = content.Round;
                for (int i = 0; i < roles.Count; i++)
                {
                    RecalculateOne(roles[i], round, reason);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("重算行动槽失败：" + e);
            }
        }

        private static void RecalculateOne(BattleRole role, int round, string reason)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                SlotState state = GetOrCreate(role);
                if (state.LastCalcRound == round && state.SlotCount > 0)
                {
                    return;
                }
                state.LastCalcRound = round;

                int target = CalcTargetCount(role);
                int old = state.SlotCount;
                if (target > 1)
                {
                    state.EverMulti = true;
                }
                if (target != old || old == 0)
                {
                    Resize(role, state, target);
                    DuoActionSlotPlugin.LogInfo(string.Format(
                        "槽数确定：{0} 速度 {1} → {2} 个行动槽（{3}，第 {4} 幕）",
                        SafeName(role), GetSpeed(role), target, reason, round));
                    ExtraActionSlotApi.NotifySlotCountChanged(role, target);
                }
                else
                {
                    DuoActionSlotPlugin.LogInfo(string.Format(
                        "槽数保持：{0} 速度 {1} → {2} 个行动槽（第 {3} 幕）",
                        SafeName(role), GetSpeed(role), target, round));
                }

                // 重置本幕的槽状态：每个槽的数据都要清空——
                // 上一幕装好的技能不能留到下一幕（否则第二个槽会继续显示上一幕的图标）。
                // 槽 0 也一样处理：原版每幕结束只会清"当前指针指着的那个槽"，不一定轮到槽 0。
                state.EditingIndex = 0;
                state.ExecIndex = 0;
                state.Executing = false;
                state.Advancing = false;
                for (int i = 0; i < state.Slots.Count; i++)
                {
                    SlotEntry entry = state.Slots[i];
                    entry.Chosen = false;
                    entry.Executed = false;
                    entry.Skipped = false;
                    // 只清"我们接管过"的角色的槽：单槽角色完全按原版跑，别动它的 CurrentBehaviourData。
                    if (state.EverMulti)
                    {
                        ResetSlotData(role, entry);
                    }
                }
                state.SetPointer(role, 0);
                MarkDirty(role);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("重算单个角色的行动槽失败：" + e);
            }
        }

        /// <summary>把槽里的行为数据换成全新的空对象（旧对象交给游戏自己卸技能，不等它）。</summary>
        private static void ResetSlotData(BattleRole role, SlotEntry entry)
        {
            try
            {
                BattleActiveBehaviorData old = entry.Data;
                if (old != null)
                {
                    _ = old.Clear();   // async，但主要副作用（卸技能/武器/buff）会立刻开始跑
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("清理槽数据失败：" + e.Message);
            }
            BattleActiveBehaviorData fresh = new BattleActiveBehaviorData();
            fresh.Self = role;
            fresh.RollDice.Role = role;
            entry.Data = fresh;
        }

        private static int CalcTargetCount(BattleRole role)
        {
            int count = 1;
            try
            {
                // 速战速决：苏醒时按速度加槽
                MOD_Dynamic_Trait trait = role.GetTraitData(SlotConstants.TraitId);
                if (trait != null && trait.CurrentState == ETraitState.Wake)
                {
                    int speed = GetSpeed(role);
                    if (speed >= SlotConstants.SpeedTier1)
                    {
                        count++;
                    }
                    if (speed >= SlotConstants.SpeedTier2)
                    {
                        count++;
                    }
                }
            }
            catch (Exception)
            {
            }
            // 外部 API 给的额外槽
            count += ExtraActionSlotApi.GetExtraCount(role);
            return Math.Max(1, count);
        }

        /// <summary>只增删尾部的槽：增加的槽是空的；减少时丢掉尾部的槽。</summary>
        private static void Resize(BattleRole role, SlotState state, int count)
        {
            while (state.Slots.Count < count)
            {
                SlotEntry entry = new SlotEntry();
                if (state.Slots.Count == 0 && role.CurrentBehaviourData != null)
                {
                    // 槽 0 复用游戏原生的对象（保住它身上已有的运行时关联）。
                    entry.Data = role.CurrentBehaviourData;
                }
                else
                {
                    entry.Data = new BattleActiveBehaviorData();
                    entry.Data.Self = role;
                    entry.Data.RollDice.Role = role;
                }
                state.Slots.Add(entry);
            }
            while (state.Slots.Count > count && state.Slots.Count > 1)
            {
                int last = state.Slots.Count - 1;
                SlotEntry entry = state.Slots[last];
                state.Slots.RemoveAt(last);
                if (entry.Data != null && ReferenceEquals(role.CurrentBehaviourData, entry.Data))
                {
                    state.SetPointer(role, 0);
                }
            }
        }

        // ---------------- 选行动阶段 ----------------

        /// <summary>当前编辑槽之后还有没有待选择的槽（用于按钮文案 / 是否需要拦截确认）。</summary>
        internal static bool HasNextPending(BattleRole role)
        {
            SlotState state = Peek(role);
            if (state == null || state.SlotCount <= 1)
            {
                return false;
            }
            for (int i = state.EditingIndex + 1; i < state.SlotCount; i++)
            {
                if (!state.Slots[i].Skipped)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 玩家确认当前槽、推进到下一个待选槽。成功返回 true，并把 nextIndex 设为新槽位。
        /// 拿不到状态 / 已经是最后一个槽时返回 false（调用方应走原版）。
        /// </summary>
        internal static bool AdvanceEditing(BattleRole role, out int nextIndex)
        {
            nextIndex = -1;
            SlotState state = Peek(role);
            if (state == null || state.SlotCount <= 1)
            {
                return false;
            }
            state.SyncFromGame(role);
            SlotEntry current = state.GetSlot(state.EditingIndex);
            if (current != null)
            {
                SetChosen(role, state.EditingIndex, !IsEmptyAction(current.Data));
            }
            int next = state.EditingIndex + 1;
            while (next < state.SlotCount && state.Slots[next].Skipped)
            {
                next++;
            }
            if (next >= state.SlotCount)
            {
                return false;
            }
            state.EditingIndex = next;
            SetChosen(role, next, false);
            state.SetPointer(role, next);
            state.UiDirty = true;
            nextIndex = next;
            DuoActionSlotPlugin.LogInfo(string.Format("选行动推进：{0} → 第 {1}/{2} 个行动槽",
                SafeName(role), next + 1, state.SlotCount));
            return true;
        }

        /// <summary>标记槽的"已选好行动"状态（true 且发生变化时才发 SlotChosen 事件）。</summary>
        internal static void SetChosen(BattleRole role, int index, bool value)
        {
            try
            {
                SlotState state = Peek(role);
                SlotEntry entry = state != null ? state.GetSlot(index) : null;
                if (entry == null || entry.Chosen == value)
                {
                    return;
                }
                entry.Chosen = value;
                if (value)
                {
                    ExtraActionSlotApi.NotifySlotChosen(role, index);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("标记行动槽选择状态失败：" + e.Message);
            }
        }

        /// <summary>标记槽"执行完成"（首次才发 SlotExecuted 事件）。</summary>
        internal static void SetExecuted(BattleRole role, int index)
        {
            try
            {
                SlotState state = Peek(role);
                SlotEntry entry = state != null ? state.GetSlot(index) : null;
                if (entry == null || entry.Executed)
                {
                    return;
                }
                entry.Executed = true;
                ExtraActionSlotApi.NotifySlotExecuted(role, index);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("标记行动槽执行状态失败：" + e.Message);
            }
        }

        internal static bool IsEmptyAction(BattleActiveBehaviorData data)
        {
            if (data == null)
            {
                return true;
            }
            return data.BattleSkillData == null && data.MagicData == null && data.ActionType == EBattleActionType.None;
        }

        /// <summary>这个多槽角色是否已经至少选好一个槽（用于选行动阶段的牌子 / 先攻数字）。</summary>
        internal static bool HasChosenSlot(BattleRole role)
        {
            SlotState state = Peek(role);
            if (state == null)
            {
                return false;
            }
            for (int i = 0; i < state.SlotCount; i++)
            {
                if (!IsEmptyAction(state.GetData(i)))
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>当前编辑槽的行动是否和别的已选槽重复（供 AllowDuplicate = false 时拦截确认）。</summary>
        internal static bool HasDuplicateAction(BattleRole role)
        {
            try
            {
                SlotState state = Peek(role);
                if (state == null || state.SlotCount <= 1)
                {
                    return false;
                }
                BattleActiveBehaviorData current = role.CurrentBehaviourData;
                if (current == null)
                {
                    return false;
                }
                for (int i = 0; i < state.SlotCount; i++)
                {
                    if (i == state.EditingIndex)
                    {
                        continue;
                    }
                    if (SameAction(state.GetData(i), current))
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("检查重复行动出错：" + e.Message);
            }
            return false;
        }

        private static bool SameAction(BattleActiveBehaviorData left, BattleActiveBehaviorData right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            if (left.BattleSkillData != null && right.BattleSkillData != null)
            {
                return left.BattleSkillData.Id == right.BattleSkillData.Id;
            }
            if (left.MagicData != null && right.MagicData != null)
            {
                return left.MagicData.Id == right.MagicData.Id;
            }
            return false;
        }

        // ---------------- 生命周期 ----------------

        /// <summary>战斗结束 / 重开：把指针复位、清空状态表。</summary>
        internal static void ClearAll(string reason)
        {
            try
            {
                if (States.Count > 0)
                {
                    foreach (KeyValuePair<BattleRole, SlotState> pair in States)
                    {
                        try
                        {
                            BattleRole role = pair.Key;
                            SlotState state = pair.Value;
                            if (role != null && state != null && state.Slots.Count > 0 && state.Slots[0].Data != null)
                            {
                                role.CurrentBehaviourData = state.Slots[0].Data;
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                    DuoActionSlotPlugin.LogInfo(string.Format("已清理行动槽状态（{0}，{1} 个角色）", reason, States.Count));
                }
                States.Clear();
                DirtyQueue.Clear();
                DirtySet.Clear();
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("清理行动槽状态失败：" + e);
            }
        }

        internal static int Count
        {
            get { return States.Count; }
        }

        // ---------------- HUD 刷新队列 ----------------

        internal static void MarkDirty(BattleRole role)
        {
            if (role == null)
            {
                return;
            }
            SlotState state = Peek(role);
            if (state != null)
            {
                state.UiDirty = true;
            }
            if (DirtySet.Add(role))
            {
                DirtyQueue.Enqueue(role);
            }
        }

        /// <summary>每帧处理一个待刷新的角色（由插件 Update 调）。</summary>
        internal static void Tick()
        {
            try
            {
                if (DirtyQueue.Count == 0)
                {
                    return;
                }
                BattleRole role = DirtyQueue.Dequeue();
                DirtySet.Remove(role);
                SlotState state = Peek(role);
                if (state == null || !state.UiDirty)
                {
                    return;
                }
                state.UiDirty = false;
                SlotHudView.RefreshRoleAsync(role);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新行动槽 HUD 失败：" + e.Message);
            }
        }

        // ---------------- 小工具 ----------------

        internal static int GetSpeed(BattleRole role)
        {
            try
            {
                return role != null && role.Data != null ? role.Data.Speed : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        internal static string SafeName(BattleRole role)
        {
            try
            {
                if (role != null && role.Data != null && !string.IsNullOrEmpty(role.Data.Name))
                {
                    return role.Data.Name;
                }
            }
            catch (Exception)
            {
            }
            return role != null ? role.ToString() : "?";
        }
    }
}
