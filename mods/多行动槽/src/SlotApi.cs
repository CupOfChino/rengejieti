// 对外接口：其它模组可以用它查询 / 设置 / 增减某个角色的行动槽数。
//
// 约定：
//   · 所有方法在任何时刻调用都安全（非战斗、空角色、非法参数都只是安静返回）；
//   · 额外槽数按 sourceKey 记账，成对增删，重复调同一个 key = 覆盖；
//   · 动态提供者每幕重算时被调用一次，返回该角色的额外槽数（异常按 0 处理）；
//   · 修改在"下一幕开始"时生效（战斗中调用不会打断当前幕的选择 / 执行）。

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game;
using UnityEngine;

namespace DuoActionSlot
{
    /// <summary>
    /// 一个行动槽的快照（给外部模组读的简单数据）。
    /// 每次调用都是当场生成的副本，不会跟着战斗继续变化。
    /// </summary>
    public sealed class ExtraSlotInfo
    {
        /// <summary>槽序号（0 起，从左到右）。</summary>
        public int Index;

        /// <summary>选行动阶段：这个槽是否已经选好行动。</summary>
        public bool Chosen;

        /// <summary>出手阶段：这个槽是否已经执行过。</summary>
        public bool Executed;

        /// <summary>本幕这个槽被跳过（角色倒下 / 无法行动等）。</summary>
        public bool Skipped;

        /// <summary>槽里是不是还没有行动。</summary>
        public bool IsEmpty;

        /// <summary>行动类型（(int)EBattleActionType；没有行动时 0 = None）。</summary>
        public int ActionType;

        /// <summary>目标类型（(int)ETargetSelect；没有目标时 0 = None）。</summary>
        public int TargetSelectType;

        /// <summary>技能 / 法术编号（没有为 0）。</summary>
        public int ActionId;

        /// <summary>当前锁定的目标列表（快照副本）。</summary>
        public List<BattleRole> Targets = new List<BattleRole>();
    }

    public static class ExtraActionSlotApi
    {
        private sealed class ExtraBox
        {
            internal readonly Dictionary<string, int> BySource = new Dictionary<string, int>();
        }

        private static readonly ConditionalWeakTable<BattleRole, ExtraBox> ExtraTable =
            new ConditionalWeakTable<BattleRole, ExtraBox>();

        private static readonly Dictionary<string, Func<BattleRole, int>> Providers =
            new Dictionary<string, Func<BattleRole, int>>();

        private sealed class PresetRule
        {
            internal string SourceKey;
            internal string RoleKey;
            internal int ConfigId;
            internal string RoleName;
            internal int Extra;
            internal string Identity;
        }

        private static readonly object PresetGate = new object();
        private static readonly List<PresetRule> PresetRules = new List<PresetRule>();

        /// <summary>某个角色的槽数变化时触发（角色, 新槽数）。只在该模组重算时触发。</summary>
        public static event Action<BattleRole, int> SlotCountChanged;

        /// <summary>某个行动槽第一次选好行动时触发（角色, 槽序号）。</summary>
        public static event Action<BattleRole, int> SlotChosen;

        /// <summary>某个行动槽执行完成时触发（角色, 槽序号）。</summary>
        public static event Action<BattleRole, int> SlotExecuted;

        // ---------------- 查询 ----------------

        /// <summary>当前幕的实际行动槽数（非战斗 / 未知角色返回 1）。</summary>
        public static int GetSlotCount(BattleRole role)
        {
            try
            {
                return SlotManager.GetSlotCountSafe(role);
            }
            catch (Exception)
            {
                return 1;
            }
        }

        /// <summary>额外行动槽数（不含基础 1 个；含预设规则、运行时记账和动态提供者）。</summary>
        public static int GetExtraSlotCount(BattleRole role)
        {
            try
            {
                return GetExtraCount(role);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static bool IsMultiSlot(BattleRole role)
        {
            try
            {
                return SlotManager.IsMultiSlot(role);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>选行动阶段：正在编辑第几个槽（0 起；未知返回 0）。</summary>
        public static int GetEditingIndex(BattleRole role)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                return state != null ? state.EditingIndex : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>出手阶段：该角色是否正在执行某个行动槽。</summary>
        public static bool IsExecuting(BattleRole role)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                return state != null && state.Executing;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>某个槽是否已经选好行动（非战斗 / 无效序号返回 false）。</summary>
        public static bool IsSlotChosen(BattleRole role, int index)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                SlotEntry entry = state != null ? state.GetSlot(index) : null;
                return entry != null && entry.Chosen;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>某个槽是否已经执行过（非战斗 / 无效序号返回 false）。</summary>
        public static bool IsSlotExecuted(BattleRole role, int index)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                SlotEntry entry = state != null ? state.GetSlot(index) : null;
                return entry != null && entry.Executed;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>某个槽本幕是否被跳过（非战斗 / 无效序号返回 false）。</summary>
        public static bool IsSlotSkipped(BattleRole role, int index)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                SlotEntry entry = state != null ? state.GetSlot(index) : null;
                return entry != null && entry.Skipped;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 取某个角色的全部行动槽快照（从槽 0 开始，和 UI 从左到右一致）。
        /// 非战斗 / 没有接管时返回空列表；永远不返回 null。
        /// </summary>
        public static List<ExtraSlotInfo> GetSlotInfos(BattleRole role)
        {
            List<ExtraSlotInfo> list = new List<ExtraSlotInfo>();
            try
            {
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return list;
                }
                for (int i = 0; i < state.SlotCount; i++)
                {
                    SlotEntry entry = state.GetSlot(i);
                    BattleActiveBehaviorData data = entry != null ? entry.Data : null;
                    ExtraSlotInfo info = new ExtraSlotInfo();
                    info.Index = i;
                    info.Chosen = entry != null && entry.Chosen;
                    info.Executed = entry != null && entry.Executed;
                    info.Skipped = entry != null && entry.Skipped;
                    info.IsEmpty = SlotManager.IsEmptyAction(data);
                    if (data != null)
                    {
                        info.ActionType = (int)data.ActionType;
                        info.TargetSelectType = (int)data.TargetSelectType;
                        if (data.BattleSkillData != null)
                        {
                            info.ActionId = data.BattleSkillData.Id;
                        }
                        else if (data.MagicData != null)
                        {
                            info.ActionId = data.MagicData.Id;
                        }
                        if (data.Targets != null)
                        {
                            info.Targets.AddRange(data.Targets);
                        }
                    }
                    list.Add(info);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("GetSlotInfos 出错：" + e.Message);
            }
            return list;
        }

        /// <summary>
        /// 取某个行动槽的行为数据（目标列表 / 目标类型 / 技能都在里面）——
        /// 给「可视化攻击目标」这类外部模组"每个槽画一条箭头"用。
        /// 槽不存在 / 参数非法返回 null。
        /// </summary>
        public static BattleActiveBehaviorData GetSlotData(BattleRole role, int index)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                return state != null ? state.GetData(index) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 取某个行动槽的 UI 节点（RectTransform）——外部模组用它定位箭头起点
        /// （比如取它的顶边中点）。没有接管 / 槽不存在返回 null。
        /// </summary>
        public static RectTransform GetSlotAnchor(BattleRole role, int index)
        {
            try
            {
                SlotHudView view = SlotHudView.Resolve(role);
                RectTransform rect;
                if (view != null && view.TryGetSlotRect(index, out rect))
                {
                    return rect;
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // ---------------- 修改 ----------------

        public static void SetExtraSlotCount(BattleRole role, int extra, string sourceKey)
        {
            try
            {
                if (role == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(sourceKey))
                {
                    sourceKey = "default";
                }
                if (extra < 0)
                {
                    extra = 0;
                }
                ExtraBox box = ExtraTable.GetValue(role, _ => new ExtraBox());
                box.BySource[sourceKey] = extra;
                MarkRecalc(role);
                DuoActionSlotPlugin.LogInfo(string.Format("API：{0} 额外行动槽 [{1}] = {2}（下一幕生效）",
                    SlotManager.SafeName(role), sourceKey, extra));
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API SetExtraSlotCount 失败：" + e.Message);
            }
        }

        public static void AddExtraSlotCount(BattleRole role, int delta, string sourceKey)
        {
            try
            {
                if (role == null || delta == 0)
                {
                    return;
                }
                if (string.IsNullOrEmpty(sourceKey))
                {
                    sourceKey = "default";
                }
                ExtraBox box = ExtraTable.GetValue(role, _ => new ExtraBox());
                int current;
                box.BySource.TryGetValue(sourceKey, out current);
                int value = Math.Max(0, current + delta);
                box.BySource[sourceKey] = value;
                MarkRecalc(role);
                DuoActionSlotPlugin.LogInfo(string.Format("API：{0} 额外行动槽 [{1}] = {2}（下一幕生效）",
                    SlotManager.SafeName(role), sourceKey, value));
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API AddExtraSlotCount 失败：" + e.Message);
            }
        }

        public static void RemoveExtraSlotCount(BattleRole role, string sourceKey)
        {
            try
            {
                if (role == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(sourceKey))
                {
                    sourceKey = "default";
                }
                ExtraBox box;
                if (ExtraTable.TryGetValue(role, out box))
                {
                    box.BySource.Remove(sourceKey);
                }
                MarkRecalc(role);
                DuoActionSlotPlugin.LogInfo(string.Format("API：{0} 移除额外行动槽来源 [{1}]",
                    SlotManager.SafeName(role), sourceKey));
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API RemoveExtraSlotCount 失败：" + e.Message);
            }
        }

        // ---------------- 预设角色 / 怪物（创作时登记，进战斗自动生效） ----------------

        /// <summary>
        /// 按调查员的稳定编号（RoleLibraryKey）预设额外行动槽。
        /// 在插件 Awake / 角色创建时登记即可，不必等战斗里拿到 BattleRole。
        /// 同一 sourceKey 对同一角色只应用一次（多条规则命中取最大值）。
        /// </summary>
        public static void SetExtraSlotCountForRoleKey(string sourceKey, string roleLibraryKey, int extra)
        {
            SetPresetRule(sourceKey, CleanKey(roleLibraryKey), 0, "", extra, "RoleKey");
        }

        /// <summary>
        /// 按角色配置编号（RoleData.ConfigId）预设额外行动槽——怪物 / NPC 用这个。
        /// 同样可以在战斗开始前登记。
        /// </summary>
        public static void SetExtraSlotCountForConfigId(string sourceKey, int configId, int extra)
        {
            if (configId <= 0)
            {
                DuoActionSlotPlugin.LogError("API 预设行动槽失败：ConfigId 必须大于 0");
                return;
            }
            SetPresetRule(sourceKey, "", configId, "", extra, "ConfigId");
        }

        /// <summary>
        /// 按角色名字预设额外行动槽。适合没有稳定编号的自建单位；
        /// 同名角色会一起命中，正式内容优先用 RoleKey / ConfigId。
        /// </summary>
        public static void SetExtraSlotCountForRoleName(string sourceKey, string roleName, int extra)
        {
            SetPresetRule(sourceKey, "", 0, CleanKey(roleName), extra, "RoleName");
        }

        /// <summary>移除某个模组登记的全部预设规则；返回移除条数。</summary>
        public static int RemovePresetExtraSlotCounts(string sourceKey)
        {
            sourceKey = CleanKey(sourceKey);
            if (string.IsNullOrEmpty(sourceKey))
            {
                return 0;
            }
            int removed = 0;
            lock (PresetGate)
            {
                for (int i = PresetRules.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(PresetRules[i].SourceKey, sourceKey, StringComparison.Ordinal))
                    {
                        PresetRules.RemoveAt(i);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                DuoActionSlotPlugin.LogInfo("API：移除预设行动槽 [" + sourceKey + "] 共 " + removed + " 条");
            }
            return removed;
        }

        /// <summary>只查询预设规则给出的额外槽数（不含运行时 API 记账和动态提供者）。</summary>
        public static int GetPresetExtraSlotCount(BattleRole role)
        {
            try
            {
                return GetPresetCount(role);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void SetPresetRule(string sourceKey, string roleKey, int configId,
            string roleName, int extra, string kind)
        {
            try
            {
                sourceKey = CleanKey(sourceKey);
                if (string.IsNullOrEmpty(sourceKey))
                {
                    DuoActionSlotPlugin.LogError("API 预设行动槽失败：sourceKey 不能为空");
                    return;
                }
                extra = Math.Max(0, extra);
                string match = kind == "RoleKey" ? roleKey :
                               kind == "ConfigId" ? configId.ToString() : roleName;
                if (string.IsNullOrEmpty(match) || match == "0")
                {
                    DuoActionSlotPlugin.LogError("API 预设行动槽失败：" + kind + " 不能为空");
                    return;
                }
                string identity = sourceKey + "\u001f" + kind + "\u001f" + match;
                lock (PresetGate)
                {
                    for (int i = PresetRules.Count - 1; i >= 0; i--)
                    {
                        if (PresetRules[i].Identity == identity)
                        {
                            PresetRules.RemoveAt(i);
                        }
                    }
                    PresetRule rule = new PresetRule();
                    rule.SourceKey = sourceKey;
                    rule.RoleKey = roleKey;
                    rule.ConfigId = configId;
                    rule.RoleName = roleName;
                    rule.Extra = extra;
                    rule.Identity = identity;
                    PresetRules.Add(rule);
                }
                DuoActionSlotPlugin.LogInfo("API：预设 " + kind + "=" + match + " 额外行动槽 [" +
                    sourceKey + "] = " + extra + "（进入战斗 / 下一幕自动生效）");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API 预设行动槽失败：" + e.Message);
            }
        }

        private static int GetPresetCount(BattleRole role)
        {
            if (role == null || role.Data == null)
            {
                return 0;
            }
            // 同一来源即使命中多条规则也只算一次，取最大值；不同来源叠加。
            Dictionary<string, int> perSource = new Dictionary<string, int>();
            lock (PresetGate)
            {
                for (int i = 0; i < PresetRules.Count; i++)
                {
                    PresetRule rule = PresetRules[i];
                    if (!PresetMatches(rule, role))
                    {
                        continue;
                    }
                    int old;
                    if (!perSource.TryGetValue(rule.SourceKey, out old) || rule.Extra > old)
                    {
                        perSource[rule.SourceKey] = rule.Extra;
                    }
                }
            }
            int total = 0;
            foreach (int value in perSource.Values)
            {
                total += value;
            }
            return Math.Max(0, total);
        }

        private static bool PresetMatches(PresetRule rule, BattleRole role)
        {
            try
            {
                if (!string.IsNullOrEmpty(rule.RoleKey) &&
                    !string.Equals(role.Data.RoleLibraryKey, rule.RoleKey, StringComparison.Ordinal))
                {
                    return false;
                }
                if (rule.ConfigId != 0 && role.Data.ConfigId != rule.ConfigId)
                {
                    return false;
                }
                if (!string.IsNullOrEmpty(rule.RoleName) &&
                    !string.Equals(role.Data.Name, rule.RoleName, StringComparison.Ordinal))
                {
                    return false;
                }
                return !string.IsNullOrEmpty(rule.RoleKey) || rule.ConfigId != 0 ||
                       !string.IsNullOrEmpty(rule.RoleName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string CleanKey(string value)
        {
            return value == null ? "" : value.Trim();
        }

        // ---------------- 动态提供者 ----------------

        public static void RegisterSlotCountProvider(string sourceKey, Func<BattleRole, int> provider)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceKey) || provider == null)
                {
                    return;
                }
                lock (Providers)
                {
                    Providers[sourceKey] = provider;
                }
                DuoActionSlotPlugin.LogInfo("API：注册行动槽提供者 [" + sourceKey + "]");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API RegisterSlotCountProvider 失败：" + e.Message);
            }
        }

        public static void UnregisterSlotCountProvider(string sourceKey)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceKey))
                {
                    return;
                }
                lock (Providers)
                {
                    Providers.Remove(sourceKey);
                }
                DuoActionSlotPlugin.LogInfo("API：移除行动槽提供者 [" + sourceKey + "]");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("API UnregisterSlotCountProvider 失败：" + e.Message);
            }
        }

        // ---------------- 内部 ----------------

        /// <summary>API 记账的额外槽数（额外来源 + 动态提供者）。</summary>
        internal static int GetExtraCount(BattleRole role)
        {
            if (role == null)
            {
                return 0;
            }
            int total = 0;
            ExtraBox box;
            if (ExtraTable.TryGetValue(role, out box))
            {
                foreach (int value in box.BySource.Values)
                {
                    total += value;
                }
            }
            total += GetPresetCount(role);
            lock (Providers)
            {
                foreach (KeyValuePair<string, Func<BattleRole, int>> pair in Providers)
                {
                    try
                    {
                        int value = pair.Value(role);
                        if (value > 0)
                        {
                            total += value;
                        }
                    }
                    catch (Exception e)
                    {
                        DuoActionSlotPlugin.LogError("行动槽提供者 [" + pair.Key + "] 执行出错：" + e.Message);
                    }
                }
            }
            return Math.Max(0, total);
        }

        private static void MarkRecalc(BattleRole role)
        {
            SlotState state = SlotManager.Peek(role);
            if (state != null)
            {
                state.LastCalcRound = -1;
            }
        }

        internal static void NotifySlotCountChanged(BattleRole role, int count)
        {
            try
            {
                Action<BattleRole, int> handler = SlotCountChanged;
                if (handler != null)
                {
                    handler(role, count);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("SlotCountChanged 事件处理出错：" + e.Message);
            }
        }

        internal static void NotifySlotChosen(BattleRole role, int index)
        {
            try
            {
                Action<BattleRole, int> handler = SlotChosen;
                if (handler != null)
                {
                    handler(role, index);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("SlotChosen 事件处理出错：" + e.Message);
            }
        }

        internal static void NotifySlotExecuted(BattleRole role, int index)
        {
            try
            {
                Action<BattleRole, int> handler = SlotExecuted;
                if (handler != null)
                {
                    handler(role, index);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("SlotExecuted 事件处理出错：" + e.Message);
            }
        }
    }
}
