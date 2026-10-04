// 多行动槽 HUD 视图：把原版的一个"技能牌子"扩成 N 个，一字排开、中心对称。
//
// 每个槽两份节点：
//   · DuoActionSlot_i  —— 定位容器：位置由我们算（中心对称 + 固定步进），槽的"站位"靠它；
//   · └ SkillShow      —— Anim_SkillShow 的克隆：保留 Animator（入场 "Show" + 上下起伏动画）。
//                          动画只改它自己的本地位移 / 缩放，不会把整个槽挪走。
//
// 原版节点（图标 / 按钮 / 数字 / 模板本体）一律隐藏，让位给克隆体——
// 顺带让「可视化攻击目标」的锚点回落到 Trans_SkillShow（整组中心）。
//
// 「编辑行动」牌：每个空槽一个克隆（原版节点隐藏），同样保留动画；
// 位置用 SlotEditorFollower 在 LateUpdate 里压住（不让动画位移把它挪走）。
//
// 每帧 TickAll() 会把"应该显示却被游戏关掉"的槽组重新打开——
// 玩家按返回键 / 切换界面时游戏会 CloseSkillIcon，不兜住的话已选槽会消失。
//
// 任何一步失败都会退回原版显示（Dispose 恢复节点），不影响游戏运行。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using UnityEngine;
using UnityEngine.UI;

namespace DuoActionSlot
{
    internal sealed class SlotHudView
    {
        private static readonly Dictionary<HUDElement, SlotHudView> Views =
            new Dictionary<HUDElement, SlotHudView>();

        private sealed class SlotVisual
        {
            internal GameObject Root;          // 定位容器
            internal RectTransform RootRect;
            internal GameObject Body;          // Anim_SkillShow 克隆
            internal RectTransform BodyRect;
            internal Animator Animator;
            internal Image Icon;
            internal Button Button;
            internal Image OrderImage;
            internal UIImageSwitch OrderSwitch;
            internal bool IconTried;                       // 这个槽已经尝试取过图标（失败也不反复重试）
        }

        private readonly HUDElement _hud;
        private readonly List<SlotVisual> _visuals = new List<SlotVisual>();
        private readonly List<GameObject> _editorClones = new List<GameObject>();
        private readonly List<SlotEditorFollower> _editorFollowers = new List<SlotEditorFollower>();

        private RectTransform _template;
        private Vector2 _baseAnchored;
        private Vector2 _baseSize;
        private Vector2 _editorBaseAnchored;
        private bool _ready;
        private bool _failed;
        private int _order;
        private int _slotCount;

        private bool _origButtonActive;
        private bool _origIconActive;
        private bool _origOrderActive;
        private bool _origTemplateActive;
        private bool _origEditorActive;

        private SlotHudView(HUDElement hud)
        {
            _hud = hud;
        }

        // ---------------- 静态入口 ----------------

        internal BattleRole Role
        {
            get
            {
                try
                {
                    return _hud != null && _hud.Model != null ? _hud.Model.Role : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>只为多槽角色取视图：已建过就复用；第一次遇到多槽时才创建。</summary>
        internal static SlotHudView For(BattleRole role)
        {
            try
            {
                if (role == null || role.Model == null || role.Model.Hud == null)
                {
                    return null;
                }
                HUDElement hud = role.Model.Hud;
                SlotHudView view;
                if (Views.TryGetValue(hud, out view))
                {
                    return view;
                }
                SlotState state = SlotManager.Peek(role);
                if (state == null || state.SlotCount <= 1)
                {
                    return null;   // 从没多槽过：完全不碰，走原版
                }
                view = new SlotHudView(hud);
                // 先缓存再初始化：初始化失败也留在表里，避免每次调用都重试 + 刷日志。
                Views[hud] = view;
                if (!view.EnsureReady())
                {
                    return null;
                }
                return view;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("建立 HUD 视图失败：" + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 统一入口：视图已建过就复用（哪怕这幕降回了单槽）；
        /// 没建过且当前是多槽才创建；其余返回 null（走原版）。
        /// </summary>
        internal static SlotHudView Resolve(BattleRole role)
        {
            try
            {
                if (role == null || role.Model == null || role.Model.Hud == null)
                {
                    return null;
                }
                SlotHudView view = TryGet(role.Model.Hud);
                if (view != null)
                {
                    return view;
                }
                SlotState state = SlotManager.Peek(role);
                if (state == null || state.SlotCount <= 1)
                {
                    return null;
                }
                return For(role);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("取 HUD 视图失败：" + e.Message);
                return null;
            }
        }

        /// <summary>已建过的视图（不创建）。</summary>
        internal static SlotHudView TryGet(HUDElement hud)
        {
            if (hud == null)
            {
                return null;
            }
            SlotHudView view;
            return Views.TryGetValue(hud, out view) ? view : null;
        }

        /// <summary>战斗结束：销毁克隆体、恢复原节点、清缓存。</summary>
        internal static void ClearAll(string reason)
        {
            try
            {
                if (Views.Count == 0)
                {
                    return;
                }
                foreach (KeyValuePair<HUDElement, SlotHudView> pair in Views)
                {
                    pair.Value.Dispose();
                }
                DuoActionSlotPlugin.LogInfo(string.Format("已恢复 {0} 个角色的 HUD（{1}）", Views.Count, reason));
                Views.Clear();
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("恢复 HUD 失败：" + e.Message);
            }
        }

        /// <summary>
        /// 每帧兜底：只要还在战斗、角色还能行动，就把"该显示却被游戏关掉"的槽组重新打开，
        /// 并把每个槽的显隐按当前阶段矫正一遍。玩家按返回键 / 切界面导致槽消失就靠它兜。
        /// </summary>
        internal static void TickAll()
        {
            try
            {
                if (Views.Count == 0 || !BattleHelper.IsInBattle)
                {
                    return;
                }
                BattleFightContent content = BattleHelper.FightContent;
                if (content == null || !content.EnterRoomFinish)
                {
                    return;
                }
                // 只在"选行动阶段"做兜底显示。
                // 出手演出阶段必须完全交给原版的显隐节奏：行动前显示、开打时收掉、
                // 行动完不再出现——之前这里连 ResultStage 也强制打开，导致收掉的牌子又冒出来。
                if (content.CurrentPhase != EFightPhase.ActionStage)
                {
                    return;
                }
                foreach (KeyValuePair<HUDElement, SlotHudView> pair in Views)
                {
                    SlotHudView view = pair.Value;
                    if (view == null)
                    {
                        continue;
                    }
                    BattleRole role = view.Role;
                    SlotState state = SlotManager.Peek(role);
                    if (state == null)
                    {
                        continue;
                    }
                    bool unable = role == null;
                    try
                    {
                        unable = role == null || role.IsDeath || role.IsUnableAct;
                    }
                    catch (Exception)
                    {
                    }
                    if (unable)
                    {
                        view.HideGroup();
                        continue;
                    }
                    // 还没轮到选行动、也没有已选槽的角色：不显示（和原版一致）。
                    bool editing = content.CurControlRole == role;
                    if (!editing && !SlotManager.HasChosenSlot(role))
                    {
                        continue;
                    }
                    if (!view._hud.Trans_SkillShow.gameObject.activeSelf)
                    {
                        view.ShowGroup(true);
                    }
                    view.SyncSlotsVisible(state, editing);
                    // 正在编辑的角色：已经装好技能（哪怕还没点确认）的槽要显示技能图标。
                    // 游戏要等确认后才主动调 ShowSkillIcon，这里发现"有数据但还没图标"就补一次刷新。
                    if (editing && view.NeedsIconRefresh(state))
                    {
                        SlotManager.MarkDirty(role);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("维持行动槽显示失败：" + e.Message);
            }
        }

        /// <summary>刷新某个角色的全部槽（图标 + 数字）。</summary>
        internal static async void RefreshRoleAsync(BattleRole role)
        {
            try
            {
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return;
                }
                SlotHudView view = For(role);
                if (view == null)
                {
                    return;
                }
                if (!view.EnsureSlots(state.SlotCount))
                {
                    return;
                }
                view._slotCount = state.SlotCount;
                if (!BattleHelper.IsInBattle)
                {
                    view.HideGroup();
                    return;
                }
                bool executing = state.Executing;
                bool editing = false;
                try
                {
                    BattleFightContent content = BattleHelper.FightContent;
                    editing = content != null && content.CurControlRole == role;
                }
                catch (Exception)
                {
                }
                bool anyVisible = false;
                for (int i = 0; i < state.SlotCount; i++)
                {
                    if (executing && i <= state.ExecIndex)
                    {
                        view.SetVisualActive(i, false);
                        continue;
                    }
                    SlotEntry entry = state.GetSlot(i);
                    BattleActiveBehaviorData data = entry != null ? entry.Data : null;
                    if (!executing && editing && SlotManager.IsEmptyAction(data))
                    {
                        // 正在编辑的角色：空槽由「编辑行动」牌占位。
                        view.SetVisualActive(i, false);
                        continue;
                    }
                    if (SlotManager.IsEmptyAction(data))
                    {
                        view.SetSlotIcon(i, null, true);
                        // 空槽不锁"已尝试"：等它装上技能后要能触发一次图标刷新。
                        view.ResetIconTried(i);
                        anyVisible = true;
                        continue;
                    }
                    Sprite icon = await GetIconAsync(data);
                    if (icon == null)
                    {
                        // 图标暂时取不到（异步超时等）：只保证这一格可见，别把它已经显示出来的图标关掉。
                        view.SetVisualActive(i, true);
                        view.MarkIconTried(i);
                        anyVisible = true;
                        continue;
                    }
                    view.SetSlotIcon(i, icon, true);
                    view.MarkIconTried(i);
                    anyVisible = true;
                }
                if (anyVisible)
                {
                    view.ShowGroup(true);
                }
                view.SetOrder(state.ActionOrder);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新角色行动槽失败：" + e.Message);
            }
        }

        private static async Task<Sprite> GetIconAsync(BattleActiveBehaviorData data)
        {
            Sprite icon = null;
            try
            {
                if (data == null)
                {
                    return null;
                }
                if (data.BattleSkillData != null)
                {
                    TaskCompletionSource<Sprite> source = new TaskCompletionSource<Sprite>();
                    try
                    {
                        _ = data.BattleSkillData.GetIcon(delegate(Sprite sprite)
                        {
                            source.TrySetResult(sprite);
                        });
                    }
                    catch (Exception)
                    {
                        source.TrySetResult(null);
                    }
                    Task done = await Task.WhenAny(source.Task, Task.Delay(SlotConstants.IconTimeoutMs));
                    if (done == source.Task)
                    {
                        icon = await source.Task;
                    }
                }
                else if (data.MagicData != null)
                {
                    if (BattleConfig.Instance != null && BattleConfig.Instance.BattleSkillIcon != null &&
                        BattleConfig.Instance.BattleSkillIcon.ContainsKey(EBattleActionType.Magic))
                    {
                        icon = BattleConfig.Instance.BattleSkillIcon[EBattleActionType.Magic];
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("取行动槽图标失败：" + e.Message);
            }
            return icon;
        }

        // ---------------- 初始化 ----------------

        private bool EnsureReady()
        {
            if (_ready)
            {
                return true;
            }
            if (_failed)
            {
                return false;
            }
            try
            {
                if (_hud == null || _hud.Anim_SkillShow == null || _hud.Trans_SkillShow == null)
                {
                    Fail("HUDElement 缺少 Anim_SkillShow / Trans_SkillShow");
                    return false;
                }
                _template = _hud.Anim_SkillShow.transform as RectTransform;
                if (_template == null)
                {
                    Fail("Anim_SkillShow 不是 RectTransform");
                    return false;
                }
                _baseAnchored = _template.anchoredPosition;
                _baseSize = _template.sizeDelta;

                _origButtonActive = _hud.Button_SkillIcon != null && _hud.Button_SkillIcon.gameObject.activeSelf;
                _origIconActive = _hud.Image_SkillIcon != null && _hud.Image_SkillIcon.gameObject.activeSelf;
                _origOrderActive = _hud.Image_ActionOrder != null && _hud.Image_ActionOrder.gameObject.activeSelf;
                _origTemplateActive = _template.gameObject.activeSelf;
                _origEditorActive = _hud.Trans_EditorAction != null && _hud.Trans_EditorAction.gameObject.activeSelf;

                RectTransform editor = _hud.Trans_EditorAction as RectTransform;
                if (editor != null)
                {
                    _editorBaseAnchored = editor.anchoredPosition;
                }

                // 原节点退场：显示改由克隆体负责（顺带给可视化模组的锚点让路）。
                if (_hud.Button_SkillIcon != null)
                {
                    _hud.Button_SkillIcon.gameObject.SetActive(false);
                }
                if (_hud.Image_SkillIcon != null)
                {
                    _hud.Image_SkillIcon.gameObject.SetActive(false);
                }
                if (_hud.Image_ActionOrder != null)
                {
                    _hud.Image_ActionOrder.gameObject.SetActive(false);
                }
                _template.gameObject.SetActive(false);
                if (editor != null)
                {
                    editor.gameObject.SetActive(false);
                }

                DuoActionSlotPlugin.LogInfo(string.Format(
                    "接管 HUD：槽宽 {0:F1}，步进 {1:F1}（{2}）",
                    _template.rect.width, Step(), _hud.name));
                _ready = true;
                return true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
                return false;
            }
        }

        private void Fail(string reason)
        {
            _failed = true;
            DuoActionSlotPlugin.LogError("HUD 视图初始化失败，退回原版显示：" + reason);
        }

        // ---------------- 克隆与布局 ----------------

        /// <summary>确保克隆体数量够用（不够就补；多余的隐藏）。</summary>
        internal bool EnsureSlots(int count)
        {
            if (!EnsureReady())
            {
                return false;
            }
            if (count < 1)
            {
                count = 1;
            }
            try
            {
                while (_visuals.Count < count)
                {
                    if (!CreateVisual())
                    {
                        return false;
                    }
                }
                for (int i = 0; i < _visuals.Count; i++)
                {
                    if (i >= count)
                    {
                        SetRootActive(_visuals[i], false);
                    }
                }
                _slotCount = count;
                Layout(count);
                return true;
            }
            catch (Exception e)
            {
                Fail("创建槽位克隆体失败：" + e.Message);
                return false;
            }
        }

        private bool CreateVisual()
        {
            int index = _visuals.Count;
            // 1) 定位容器：坐标系抄模板（anchor / pivot / 尺寸），位置由我们给。
            GameObject root = new GameObject("DuoActionSlot_" + index, typeof(RectTransform));
            RectTransform rootRect = root.transform as RectTransform;
            rootRect.SetParent(_hud.Trans_SkillShow, false);
            rootRect.anchorMin = _template.anchorMin;
            rootRect.anchorMax = _template.anchorMax;
            rootRect.pivot = _template.pivot;
            rootRect.sizeDelta = _baseSize;
            rootRect.anchoredPosition = _baseAnchored;
            root.SetActive(false);

            // 2) 克隆技能牌子：保留 Animator（Show 入场 + 上下起伏）。
            //    它的本地位置归零（和容器同点），动画只会在它自己身上加偏移。
            GameObject body = UnityEngine.Object.Instantiate<GameObject>(_template.gameObject, root.transform);
            body.name = "SkillShow";
            RectTransform bodyRect = body.transform as RectTransform;
            if (bodyRect != null)
            {
                bodyRect.anchorMin = _template.anchorMin;
                bodyRect.anchorMax = _template.anchorMax;
                bodyRect.pivot = _template.pivot;
                bodyRect.sizeDelta = _baseSize;
                bodyRect.anchoredPosition = Vector2.zero;
                bodyRect.localScale = Vector3.one;
            }
            body.SetActive(true);

            SlotVisual visual = new SlotVisual();
            visual.Root = root;
            visual.RootRect = rootRect;
            visual.Body = body;
            visual.BodyRect = bodyRect;
            visual.Animator = body.GetComponent<Animator>();

            Transform button = body.transform.Find("Button_SkillIcon");
            Transform icon = body.transform.Find("Image_SkillIcon");
            Transform order = body.transform.Find("Image_ActionOrder");
            if (button != null)
            {
                visual.Button = button.GetComponent<Button>();
            }
            if (icon != null)
            {
                visual.Icon = icon.GetComponent<Image>();
            }
            if (order != null)
            {
                visual.OrderImage = order.GetComponent<Image>();
                visual.OrderSwitch = order.GetComponent<UIImageSwitch>();
            }
            if (visual.Button != null)
            {
                visual.Button.onClick.RemoveAllListeners();
                visual.Button.onClick.AddListener(delegate
                {
                    try
                    {
                        _hud.OnClickSkillIcon();
                    }
                    catch (Exception e)
                    {
                        DuoActionSlotPlugin.LogError("点击行动槽出错：" + e.Message);
                    }
                });
            }
            if (visual.OrderImage != null)
            {
                visual.OrderImage.gameObject.SetActive(false);
            }
            _visuals.Add(visual);
            return true;
        }

        private float Step()
        {
            // 不要用模板的 sizeDelta 算步进：它可能是"拉伸锚点"的差值（实机表现为间距大得离谱）。
            // rect.width 是节点在本地坐标里的实际宽度，用它最稳。
            float width = 0f;
            try
            {
                if (_template != null)
                {
                    width = _template.rect.width;
                }
            }
            catch (Exception)
            {
            }
            if (width <= 1f || width > 2000f)
            {
                width = SlotConstants.DefaultSlotWidth;
            }
            // 用户口径：两个行动槽之间的空档 = 半个行动槽 → 中心距 = 1.5 个槽宽。
            return width * SlotConstants.SlotStepRatio;
        }

        private void Layout(int count)
        {
            float step = Step();
            float center = (count - 1) * 0.5f;
            for (int i = 0; i < _visuals.Count; i++)
            {
                SlotVisual visual = _visuals[i];
                if (visual.RootRect == null)
                {
                    continue;
                }
                visual.RootRect.anchoredPosition = _baseAnchored + new Vector2((i - center) * step, 0f);
            }
        }

        // ---------------- 显示控制 ----------------

        private void SetRootActive(SlotVisual visual, bool show)
        {
            if (visual == null || visual.Root == null)
            {
                return;
            }
            if (visual.Root.activeSelf == show)
            {
                return;
            }
            visual.Root.SetActive(show);
            if (show && visual.Animator != null && visual.Animator.isActiveAndEnabled)
            {
                try
                {
                    visual.Animator.Play("Show", 0, 0f);
                }
                catch (Exception)
                {
                }
            }
        }

        private void SetVisualActive(int index, bool show)
        {
            if (index < 0 || index >= _visuals.Count)
            {
                return;
            }
            SetRootActive(_visuals[index], show);
        }

        private void MarkIconTried(int index)
        {
            try
            {
                if (index < 0 || index >= _visuals.Count)
                {
                    return;
                }
                SlotVisual visual = _visuals[index];
                if (visual == null)
                {
                    return;
                }
                // 取过一次就不再反复重试；换幕时 RecalculateOne 会主动刷新一次（走 dirty），
                // 所以不需要在数据变化时重置这里。
                visual.IconTried = true;
            }
            catch (Exception)
            {
            }
        }

        private void ResetIconTried(int index)
        {
            try
            {
                if (index >= 0 && index < _visuals.Count && _visuals[index] != null)
                {
                    _visuals[index].IconTried = false;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>显示整组（Trans_SkillShow 是组容器）。</summary>
        internal void ShowGroup(bool active)
        {
            try
            {
                if (!EnsureReady())
                {
                    return;
                }
                if (_hud.Trans_SkillShow != null)
                {
                    _hud.Trans_SkillShow.gameObject.SetActive(active);
                }
                if (active && _template != null)
                {
                    _template.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示行动槽组失败：" + e.Message);
            }
        }

        internal void HideGroup()
        {
            ShowGroup(false);
        }

        /// <summary>设置第 index 个槽的图标；show 控制这个槽自己显示不显示。</summary>
        internal void SetSlotIcon(int index, Sprite icon, bool show)
        {
            try
            {
                if (index < 0 || index >= _visuals.Count)
                {
                    return;
                }
                SlotVisual visual = _visuals[index];
                bool hasIcon = icon != null;
                if (visual.Icon != null)
                {
                    if (hasIcon)
                    {
                        // 只覆盖：取不到图标（超时 / 异步失败）时保留已经显示出来的那张。
                        visual.Icon.sprite = icon;
                    }
                    if (visual.Icon.gameObject.activeSelf != hasIcon)
                    {
                        visual.Icon.gameObject.SetActive(hasIcon);
                    }
                }
                if (visual.Button != null && visual.Button.gameObject.activeSelf != hasIcon)
                {
                    visual.Button.gameObject.SetActive(hasIcon);
                }
                if (show)
                {
                    SetRootActive(visual, true);
                }
                RefreshOrderVisual();
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("设置行动槽图标失败：" + e.Message);
            }
        }

        /// <summary>只控制这一格显示 / 隐藏，不动里面的图标。</summary>
        internal void SetSlotVisible(int index, bool show)
        {
            SetVisualActive(index, show);
        }

        internal void HideSlot(int index)
        {
            SetVisualActive(index, false);
        }

        /// <summary>按当前阶段把每个槽的显隐矫正一遍（不动图标）。</summary>
        internal void SyncSlotsVisible(SlotState state, bool editing)
        {
            try
            {
                if (state == null || !EnsureSlots(state.SlotCount))
                {
                    return;
                }
                bool executing = state.Executing;
                for (int i = 0; i < state.SlotCount && i < _visuals.Count; i++)
                {
                    // 出手阶段：已经执行过的（含正在执行的）不显示；
                    // 选行动阶段：正在编辑的角色的空槽由「编辑行动」牌占位，其余都显示。
                    bool show;
                    if (executing)
                    {
                        show = i > state.ExecIndex;
                    }
                    else if (editing && SlotManager.IsEmptyAction(state.GetData(i)))
                    {
                        show = false;
                    }
                    else
                    {
                        show = true;
                    }
                    SetVisualActive(i, show);
                }
                RefreshOrderVisual();
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("矫正行动槽显隐失败：" + e.Message);
            }
        }

        /// <summary>有"已装技能但图标还没出来"的槽（给 TickAll 决定要不要补一次刷新）。</summary>
        private bool NeedsIconRefresh(SlotState state)
        {
            try
            {
                if (state == null || state.Executing)
                {
                    return false;
                }
                for (int i = 0; i < state.SlotCount && i < _visuals.Count; i++)
                {
                    BattleActiveBehaviorData data = state.GetData(i);
                    if (SlotManager.IsEmptyAction(data))
                    {
                        continue;
                    }
                    SlotVisual visual = _visuals[i];
                    if (visual != null && visual.Icon != null && visual.Icon.sprite == null && !visual.IconTried)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>选行动阶段：把所有槽（含空框）都摆出来（供 UI 推进后调用）。</summary>
        internal void ShowSelectPhaseSlots(SlotState state)
        {
            try
            {
                if (state == null)
                {
                    return;
                }
                SyncSlotsVisible(state, true);
                ShowGroup(true);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示选行动槽位失败：" + e.Message);
            }
        }

        // ---------------- 行动顺序数字 ----------------

        internal void SetOrder(int order)
        {
            _order = order;
            RefreshOrderVisual();
        }

        private void RefreshOrderVisual()
        {
            try
            {
                if (_visuals.Count == 0)
                {
                    return;
                }
                // 数字只显示在最左（第 0）个槽的左上角。
                for (int i = 0; i < _visuals.Count; i++)
                {
                    SlotVisual visual = _visuals[i];
                    if (visual.OrderImage == null)
                    {
                        continue;
                    }
                    bool show = i == 0 && _order >= 1;
                    visual.OrderImage.gameObject.SetActive(show);
                    if (show && visual.OrderSwitch != null)
                    {
                        visual.OrderSwitch.SetImage(_order);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新行动顺序数字失败：" + e.Message);
            }
        }

        // ---------------- 「编辑行动」牌 ----------------

        private void EnsureEditorClones(int count)
        {
            if (_hud.Trans_EditorAction == null || _hud.Trans_Battle == null)
            {
                return;
            }
            while (_editorClones.Count < count)
            {
                GameObject clone = UnityEngine.Object.Instantiate<GameObject>(
                    _hud.Trans_EditorAction.gameObject, _hud.Trans_Battle);
                clone.name = "DuoActionSlot_Editor_" + _editorClones.Count;
                RectTransform rect = clone.transform as RectTransform;
                if (rect != null)
                {
                    rect.anchoredPosition = _editorBaseAnchored;
                }
                SlotEditorFollower follower = clone.AddComponent<SlotEditorFollower>();
                follower.Init(rect, _editorBaseAnchored);
                clone.SetActive(false);
                _editorClones.Add(clone);
                _editorFollowers.Add(follower);
            }
        }

        /// <summary>把所有空槽摆上「编辑行动」牌（牌自带放大 / 起伏动画，位置由 follower 压住）。</summary>
        internal void ShowEditorSlots(SlotState state)
        {
            try
            {
                if (state == null || !EnsureSlots(state.SlotCount) || state.Executing)
                {
                    return;
                }
                EnsureEditorClones(state.SlotCount);
                float step = Step();
                float center = (state.SlotCount - 1) * 0.5f;
                for (int i = 0; i < state.SlotCount; i++)
                {
                    if (i >= _editorClones.Count || _editorClones[i] == null)
                    {
                        continue;
                    }
                    bool show = SlotManager.IsEmptyAction(state.GetData(i));
                    if (show)
                    {
                        if (i < _editorFollowers.Count && _editorFollowers[i] != null)
                        {
                            _editorFollowers[i].SetOffset((i - center) * step);
                        }
                        _editorClones[i].SetActive(true);
                    }
                    else
                    {
                        _editorClones[i].SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示「编辑行动」牌失败：" + e.Message);
            }
        }

        internal void HideEditorSlots()
        {
            try
            {
                for (int i = 0; i < _editorClones.Count; i++)
                {
                    if (_editorClones[i] != null)
                    {
                        _editorClones[i].SetActive(false);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------- 对外节点查询 ----------------

        /// <summary>取第 index 个槽的节点（给「可视化攻击目标」之类的外部模组定位箭头起点用）。</summary>
        internal bool TryGetSlotRect(int index, out RectTransform rect)
        {
            rect = null;
            try
            {
                if (!_ready || index < 0 || index >= _visuals.Count)
                {
                    return false;
                }
                SlotVisual visual = _visuals[index];
                // 给外部模组的是"技能牌子本体"（Body），它的 rect 就是整块槽框。
                rect = visual.BodyRect != null ? visual.BodyRect : visual.RootRect;
                return rect != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------- 清理 ----------------

        private void Dispose()
        {
            try
            {
                for (int i = 0; i < _visuals.Count; i++)
                {
                    if (_visuals[i].Root != null)
                    {
                        UnityEngine.Object.Destroy(_visuals[i].Root);
                    }
                }
                _visuals.Clear();
                for (int i = 0; i < _editorClones.Count; i++)
                {
                    if (_editorClones[i] != null)
                    {
                        UnityEngine.Object.Destroy(_editorClones[i]);
                    }
                }
                _editorClones.Clear();
                _editorFollowers.Clear();
                if (_hud != null)
                {
                    if (_hud.Button_SkillIcon != null)
                    {
                        _hud.Button_SkillIcon.gameObject.SetActive(_origButtonActive);
                    }
                    if (_hud.Image_SkillIcon != null)
                    {
                        _hud.Image_SkillIcon.gameObject.SetActive(_origIconActive);
                    }
                    if (_hud.Image_ActionOrder != null)
                    {
                        _hud.Image_ActionOrder.gameObject.SetActive(_origOrderActive);
                    }
                    if (_hud.Trans_EditorAction != null)
                    {
                        _hud.Trans_EditorAction.gameObject.SetActive(_origEditorActive);
                    }
                }
                if (_template != null)
                {
                    _template.gameObject.SetActive(_origTemplateActive);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("销毁 HUD 视图失败：" + e.Message);
            }
            _ready = false;
        }
    }
}
