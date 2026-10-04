// 每个战斗角色的"行动槽状态"。

using System.Collections.Generic;
using Game;

namespace DuoActionSlot
{
    internal sealed class SlotEntry
    {
        /// <summary>这个槽的行为数据（由游戏原版的 BattleActiveBehaviorData 承载）。</summary>
        internal BattleActiveBehaviorData Data;

        /// <summary>选行动阶段：这个槽是否已经选好行动。</summary>
        internal bool Chosen;

        /// <summary>出手阶段：这个槽是否已经执行过。</summary>
        internal bool Executed;

        /// <summary>这个槽本幕被跳过（角色无法行动等）。</summary>
        internal bool Skipped;
    }

    internal sealed class SlotState
    {
        internal BattleRole Role;

        /// <summary>槽位表。[0] 复用角色原生的 CurrentBehaviourData，[1..] 由插件新建。</summary>
        internal readonly List<SlotEntry> Slots = new List<SlotEntry>();

        /// <summary>选行动阶段：当前正在编辑第几个槽（0 起）。</summary>
        internal int EditingIndex;

        /// <summary>出手阶段：当前执行到第几个槽（0 起）。</summary>
        internal int ExecIndex;

        /// <summary>本幕的先攻数字（角色级，所有槽共享）。</summary>
        internal int ActionOrder;

        /// <summary>UI 正在推进到下一个槽（防连点）。</summary>
        internal bool Advancing;

        /// <summary>出手阶段：本角色是否正在执行（用于 HUD 判断"只收当前槽的牌子"）。</summary>
        internal bool Executing;

        /// <summary>上次重算槽数的幕号（防同一幕重复重算）。</summary>
        internal int LastCalcRound = -1;

        /// <summary>这个角色本场战斗里是否曾经多槽（降回单槽后仍要按"我们接管的槽"清理数据）。</summary>
        internal bool EverMulti;

        /// <summary>API 给的额外槽数，按来源记账。</summary>
        internal readonly Dictionary<string, int> ExtraSources = new Dictionary<string, int>();

        /// <summary>HUD 需要刷新（图标 / 数字）。</summary>
        internal bool UiDirty;

        /// <summary>重算后记录的"本幕槽数"（槽增删以它为准）。</summary>
        internal int SlotCount
        {
            get { return Slots.Count; }
        }

        internal SlotEntry GetSlot(int index)
        {
            if (index < 0 || index >= Slots.Count)
            {
                return null;
            }
            return Slots[index];
        }

        internal BattleActiveBehaviorData GetData(int index)
        {
            SlotEntry entry = GetSlot(index);
            return entry != null ? entry.Data : null;
        }

        /// <summary>把游戏指针切到第 index 个槽（越界时切到 0；全空时不动）。</summary>
        internal void SetPointer(BattleRole role, int index)
        {
            if (role == null)
            {
                return;
            }
            if (index < 0 || index >= Slots.Count)
            {
                index = 0;
            }
            if (Slots.Count == 0)
            {
                return;
            }
            BattleActiveBehaviorData data = Slots[index].Data;
            if (data != null)
            {
                role.CurrentBehaviourData = data;
            }
        }

        /// <summary>
        /// 游戏里有些逻辑会直接把 role.CurrentBehaviourData 换成它自己的 Copy
        /// （昏迷恢复、怀表重放等）。进入我们自己的逻辑前先对齐：
        /// 以游戏当前指针为准，把它接纳成"当前逻辑槽"的数据。
        /// </summary>
        internal void SyncFromGame(BattleRole role)
        {
            if (role == null || Slots.Count == 0)
            {
                return;
            }
            BattleActiveBehaviorData current = role.CurrentBehaviourData;
            if (current == null)
            {
                return;
            }
            int index = EditingIndex;
            if (index < 0 || index >= Slots.Count)
            {
                index = 0;
            }
            if (!ReferenceEquals(Slots[index].Data, current))
            {
                // 游戏换了指针：把新对象收编到当前槽（旧的丢掉）。
                Slots[index].Data = current;
            }
        }

        // 注："后面还有没有待选的槽"由 SlotManager.HasNextPending 统一判断（依赖本幕的 Skipped 标记）。
    }
}
