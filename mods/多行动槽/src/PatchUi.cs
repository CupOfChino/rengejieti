// UI 补丁：
//   1) 玩家点「完成选择 / 确定行动」时，多槽角色推进到下一个槽（不切换角色）；
//   2) 底部按钮文案：中间槽显示"确定行动 k/N"；
//   3) 行动槽牌子：多槽角色改走 SlotHudView；
//   4) 「编辑行动」牌子摆到正在编辑的那个槽的位置。
//
// 所有 Prefix：先 try；异常 → 记日志 → return true（走原版）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Game;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DuoActionSlot
{
    /// <summary>读 UIBattlePanel 的私有 bool 字段（_selectItem 等）。</summary>
    internal static class SlotReflect
    {
        private static readonly Dictionary<string, FieldInfo> Cache = new Dictionary<string, FieldInfo>();

        internal static bool GetBool(object instance, string name, bool fallback)
        {
            try
            {
                if (instance == null)
                {
                    return fallback;
                }
                string key = instance.GetType().FullName + "." + name;
                FieldInfo field;
                if (!Cache.TryGetValue(key, out field))
                {
                    field = AccessTools.Field(instance.GetType(), name);
                    Cache[key] = field;
                }
                if (field == null)
                {
                    return fallback;
                }
                object value = field.GetValue(instance);
                return value is bool ? (bool)value : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }

    // ---------------- 1) 玩家确认：推进到下一个槽 ----------------

    [HarmonyPatch(typeof(UIBattlePanel), "_OnNextPhaseButtonClicked")]
    internal static class Patch_NextPhaseClicked
    {
        private static bool Prefix(UIBattlePanel __instance, ref Task __result)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                BattleRole hero = BattleHelper.BattleHeroInfo;
                SlotState state = SlotManager.Peek(hero);
                if (state == null || state.SlotCount <= 1)
                {
                    return true;   // 单槽：原版
                }
                if (state.Advancing)
                {
                    // 上一次推进的 UI 还没刷完，吞掉这次点击。
                    __result = Task.CompletedTask;
                    return false;
                }
                // 特殊路径（使用道具 / 投掷）交给原版。
                if (__instance.IsUsingItem ||
                    SlotReflect.GetBool(__instance, "_selectItem", false) ||
                    SlotReflect.GetBool(__instance, "_normalThrowItem", false) ||
                    SlotReflect.GetBool(__instance, "_weaponThrowItem", false))
                {
                    return true;
                }
                BattleFightContent content = BattleHelper.FightContent;
                if (content == null || content.CurrentPhase != EFightPhase.ActionStage)
                {
                    return true;
                }
                if (!SlotManager.HasNextPending(hero))
                {
                    // 最后一个槽：标记它已确认（否则「编辑行动」牌会一直挂着），然后交给原版收尾。
                    SlotEntry last = state.GetSlot(state.EditingIndex);
                    if (last != null)
                    {
                        SlotManager.SetChosen(hero, state.EditingIndex, !SlotManager.IsEmptyAction(last.Data));
                    }
                    return true;   // 最后一个待选槽：走原版收尾（完成选择 / 开始战斗）
                }
                if (!DuoActionSlotPlugin.AllowDuplicate && SlotManager.HasDuplicateAction(hero))
                {
                    ShowHint("这个行动已经装在其它行动槽里了", "This action is already equipped in another action slot");
                    __result = Task.CompletedTask;
                    return false;
                }
                BattleActiveBehaviorData current = hero.CurrentBehaviourData;
                if (current == null || current.TargetCount == 0)
                {
                    // 原版在这里会提示"未选择行动目标"，照做但不推进。
                    ShowHint("未选择行动目标", "No action target selected");
                    __result = Task.CompletedTask;
                    return false;
                }
                int next;
                if (!SlotManager.AdvanceEditing(hero, out next))
                {
                    return true;
                }
                state.Advancing = true;
                RefreshActionShowAsync(__instance, hero, state);
                __result = Task.CompletedTask;
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("推进下一个行动槽失败，退回原版：" + e);
                return true;
            }
        }

        private static async void RefreshActionShowAsync(UIBattlePanel ui, BattleRole hero, SlotState state)
        {
            try
            {
                await ui.SwitchToShow(UIBattlePanel.EShowType.ActionShow);
                await ui.UpdateSkillIconAndActionOrder();
                await RefreshHeroMultiSlotAsync(hero, state);
                ui.UpdateNextPhaseButton(hero);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新行动选择界面失败：" + e.Message);
            }
            finally
            {
                if (state != null)
                {
                    state.Advancing = false;
                }
            }
        }

        /// <summary>
        /// 玩家的多槽角色在"选到一半"时指针指向空槽，原版的 UpdateSkillIconAndActionOrder
        /// 会把它过滤掉——这里按同样的口径补算先攻数字，并把已经选好的槽重新亮出来。
        /// </summary>
        private static async Task RefreshHeroMultiSlotAsync(BattleRole hero, SlotState state)
        {
            try
            {
                BattleFightContent content = BattleHelper.FightContent;
                if (content == null || hero == null || state == null)
                {
                    return;
                }
                await content.CheckActionSequence();
                int order = 0;
                int index = 0;
                for (int i = 0; i < content.SequenceList.Count; i++)
                {
                    BattleRole role = content.SequenceList[i];
                    if (role == null)
                    {
                        continue;
                    }
                    bool hasAction = role.CurrentBehaviourData != null &&
                                     (role.CurrentBehaviourData.BattleSkillData != null ||
                                      role.CurrentBehaviourData.MagicData != null);
                    if (!hasAction && !SlotManager.HasChosenSlot(role))
                    {
                        continue;
                    }
                    index++;
                    if (role == hero)
                    {
                        order = index;
                        break;
                    }
                }
                if (order > 0)
                {
                    hero.BattleActionOrder = order;
                    state.ActionOrder = order;
                }
                SlotHudView view = SlotHudView.Resolve(hero);
                if (view != null && view.EnsureSlots(state.SlotCount))
                {
                    view.SetOrder(order);
                    view.ShowSelectPhaseSlots(state);
                    view.ShowGroup(true);
                    SlotManager.MarkDirty(hero);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新多槽角色显示失败：" + e.Message);
            }
        }

        internal static void ShowHint(string zh, string en)
        {
            try
            {
                if (!PrefabSingleton<UIMsgHintPanel>.HasInstance)
                {
                    return;
                }
                LocalizationKeyData key = new LocalizationKeyData();
                key.TarKey = "";
                key.SheetKey = "";
                key.InputText = SlotText.T(zh, en);
                _ = PrefabSingleton<UIMsgHintPanel>.Instance.ShowPopup(key, false);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("弹提示失败：" + e.Message);
            }
        }
    }

    // ---------------- 2) 底部按钮文案 ----------------

    [HarmonyPatch(typeof(UIBattlePanel), "UpdateNextPhaseButton")]
    internal static class Patch_NextPhaseText
    {
        private static void Postfix(UIBattlePanel __instance, BattleRole role)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return;
                }
                BattleRole target = role != null ? role : BattleHelper.BattleHeroInfo;
                SlotState state = SlotManager.Peek(target);
                if (state == null || state.SlotCount <= 1)
                {
                    return;
                }
                if (state.EditingIndex < state.SlotCount - 1 && SlotManager.HasNextPending(target))
                {
                    // 中间槽：确定行动 k/N
                    string text = string.Format(
                        SlotText.T("确定行动 {0}/{1}", "Confirm action {0}/{1}"),
                        state.EditingIndex + 1, state.SlotCount);
                    if (__instance.Text_NextPhase != null)
                    {
                        __instance.Text_NextPhase.SetLocalization(text);
                    }
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新按钮文案失败：" + e.Message);
            }
        }
    }

    // ---------------- 3) 行动槽牌子：多槽改走 SlotHudView ----------------

    [HarmonyPatch(typeof(HUDElement), "ShowSkillIcon")]
    internal static class Patch_HudShowSkillIcon
    {
        private static bool Prefix(HUDElement __instance, Sprite sprite, bool activeShow)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                BattleRole role = GetRole(__instance);
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return true;
                }
                SlotHudView view = SlotHudView.Resolve(role);
                if (view == null || !view.EnsureSlots(state.SlotCount))
                {
                    return true;
                }
                int index = state.Executing ? state.ExecIndex : state.EditingIndex;
                view.SetSlotIcon(index, sprite, activeShow);
                if (activeShow)
                {
                    view.ShowGroup(true);
                }
                SlotManager.MarkDirty(role);
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示行动槽图标失败：" + e.Message);
                return true;
            }
        }

        internal static BattleRole GetRole(HUDElement hud)
        {
            try
            {
                return hud != null && hud.Model != null ? hud.Model.Role : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(HUDElement), "ShowActionOrder")]
    internal static class Patch_HudShowActionOrder
    {
        private static bool Prefix(HUDElement __instance, int order)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                BattleRole role = Patch_HudShowSkillIcon.GetRole(__instance);
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return true;
                }
                SlotHudView view = SlotHudView.Resolve(role);
                if (view == null || !view.EnsureSlots(state.SlotCount))
                {
                    return true;
                }
                state.ActionOrder = order;
                view.SetOrder(order);
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示行动顺序失败：" + e.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(HUDElement), "ActiveSkillIcon")]
    internal static class Patch_HudActiveSkillIcon
    {
        private static bool Prefix(HUDElement __instance)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                BattleRole role = Patch_HudShowSkillIcon.GetRole(__instance);
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return true;
                }
                SlotHudView view = SlotHudView.Resolve(role);
                if (view == null)
                {
                    return true;
                }
                view.ShowGroup(true);
                SlotManager.MarkDirty(role);
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("显示行动槽组失败：" + e.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(HUDElement), "CloseSkillIcon")]
    internal static class Patch_HudCloseSkillIcon
    {
        private static bool Prefix(HUDElement __instance, bool waitForAnim, ref Task __result)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled)
                {
                    return true;
                }
                BattleRole role = Patch_HudShowSkillIcon.GetRole(__instance);
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return true;
                }
                SlotHudView view = SlotHudView.Resolve(role);
                if (view == null)
                {
                    return true;
                }
                try
                {
                    if (PrefabSingleton<UIDetailedTipPanel>.HasInstance)
                    {
                        PrefabSingleton<UIDetailedTipPanel>.Instance.Close();
                    }
                }
                catch (Exception)
                {
                }
                if (state.Executing && state.ExecIndex >= 0 && state.ExecIndex < state.SlotCount)
                {
                    // 正在执行某个槽：只收这一张牌子，别关整组。
                    view.HideSlot(state.ExecIndex);
                }
                else
                {
                    view.HideGroup();
                }
                __result = Task.CompletedTask;
                return false;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("关闭行动槽牌子失败：" + e.Message);
                return true;
            }
        }
    }

    // ---------------- 4) 「编辑行动」牌子 ----------------

    [HarmonyPatch(typeof(HUDElement), "_UpdateRoleActionOverShow")]
    internal static class Patch_HudEditorAction
    {
        private static void Postfix(HUDElement __instance)
        {
            try
            {
                if (!DuoActionSlotPlugin.ModEnabled || __instance.Trans_EditorAction == null)
                {
                    return;
                }
                BattleRole role = Patch_HudShowSkillIcon.GetRole(__instance);
                SlotState state = SlotManager.Peek(role);
                if (state == null)
                {
                    return;
                }
                SlotHudView view = SlotHudView.Resolve(role);
                if (view == null)
                {
                    return;
                }
                BattleFightContent content = BattleHelper.FightContent;
                bool usingItem = PrefabSingleton<UIBattlePanel>.HasInstance &&
                                 PrefabSingleton<UIBattlePanel>.Instance.IsUsingItem;
                bool editing = content != null
                    && content.CurrentPhase == EFightPhase.ActionStage
                    && content.CurControlRole == role
                    && !role.IsUnableAct
                    && !usingItem;
                // 原版节点始终隐藏（多槽角色由克隆牌接管），避免出现"空框 + 编辑牌"看起来像三个槽。
                __instance.Trans_EditorAction.gameObject.SetActive(false);
                if (editing)
                {
                    view.ShowEditorSlots(state);
                }
                else
                {
                    view.HideEditorSlots();
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("刷新「编辑行动」牌失败：" + e.Message);
            }
        }
    }

    internal static class PatchUi
    {
        internal static void Register(Harmony harmony)
        {
            DuoActionSlotPlugin.LogInfo("开始挂载 UI 钩子……");
            PatchOne(harmony, typeof(Patch_NextPhaseClicked));
            PatchOne(harmony, typeof(Patch_NextPhaseText));
            PatchOne(harmony, typeof(Patch_HudShowSkillIcon));
            PatchOne(harmony, typeof(Patch_HudShowActionOrder));
            PatchOne(harmony, typeof(Patch_HudActiveSkillIcon));
            PatchOne(harmony, typeof(Patch_HudCloseSkillIcon));
            PatchOne(harmony, typeof(Patch_HudEditorAction));
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
