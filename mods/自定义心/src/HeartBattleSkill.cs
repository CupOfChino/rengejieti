// 「激活「心」」技艺行动。
//
// 规则：心的特质**沉睡**时，战斗开始不再自动挂【心】，而是给这个角色发一条技艺行动
// （战斗技能 880904），让玩家自己花 5 点精神值把它点亮：
//   · 选定技能时：本回合速度+50（临时，用于抢先手，和「诱敌」同一套机制）
//   · 行动结束时：消耗5点精神值 + 获得【心】 + 获得4点护甲（护甲是 880906，下回合开始时自解）
// 限制：身上已经有【心】（默认心或他自己那颗自定义心）、或精神值处于衰弱/衰竭时不能使用。
//
// 技能本身是数据（Project_Depersonal\...\Game\BattleSkill\880904.txt），
// 这里只补两样数据写不出来的东西：
//   · 一条自定义检查（要认得出"动态编号"的自定义心）
//   · 一个自定义效果（按角色决定该挂哪颗心）
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace XinEditor
{
    internal static class HeartBattleSkill
    {
        public const int SkillId = 880904;
        public const int SpeedBuffId = 880905;
        public const int ArmorBuffId = 880906;

        /// <summary>技能来源标记，摘技能时按它来。</summary>
        public const string SourceKey = "xineditor_heart_activate";

        private static bool _injected;
        private static bool _injectFailed;

        /// <summary>战斗开始/结束时按特质状态发或摘这条技艺。</summary>
        internal static void OnBattleTrigger(BattleRole role, EBuffTriggerType trigger)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (trigger == EBuffTriggerType.BattleEnd)
                {
                    RemoveSkill(role);
                    return;
                }
                if (trigger != EBuffTriggerType.BattleStart)
                {
                    return;
                }

                MOD_Dynamic_Trait trait = role.Data.GetTraitData(HeartConstants.DefaultHeartTraitId);
                if (trait == null || trait.CurrentState == ETraitState.Wake)
                {
                    // 没有【心】特质、或者特质醒着（走"战斗开始自动挂心"）：不该有这条技艺
                    RemoveSkill(role);
                    return;
                }

                if (!EnsureInjected())
                {
                    return;
                }
                AddSkill(role);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("处理「激活心」技艺失败：" + e.Message);
            }
        }

        // 把数据写不出来的检查/效果塞进技能配置，只做一次
        private static bool EnsureInjected()
        {
            if (_injected)
            {
                return true;
            }
            if (_injectFailed)
            {
                return false;
            }
            try
            {
                BattleSkillTableData cfg = FindSkillConfig();
                if (cfg == null)
                {
                    return false;   // 数据还没加载好，下次战斗再试
                }

                if (cfg.ShowCheck == null)
                {
                    cfg.ShowCheck = new List<BattleSkillBaseCheck>();
                }
                bool hasCheck = false;
                for (int i = 0; i < cfg.ShowCheck.Count; i++)
                {
                    if (cfg.ShowCheck[i] is HeartActivateCheck)
                    {
                        hasCheck = true;
                        break;
                    }
                }
                if (!hasCheck)
                {
                    cfg.ShowCheck.Add(new HeartActivateCheck());
                }

                bool hasOption = false;
                if (cfg.EffectList != null)
                {
                    for (int i = 0; i < cfg.EffectList.Count; i++)
                    {
                        BattleSkillEffectData effect = cfg.EffectList[i];
                        if (effect == null || effect.Trigger != EBattleSkillTrigger.ActionEnd)
                        {
                            continue;
                        }
                        if (effect.EffectOption == null)
                        {
                            effect.EffectOption = new List<BattleSkillBaseOption>();
                        }
                        for (int j = 0; j < effect.EffectOption.Count; j++)
                        {
                            if (effect.EffectOption[j] is HeartActivateOption)
                            {
                                hasOption = true;
                                break;
                            }
                        }
                        if (!hasOption)
                        {
                            effect.EffectOption.Add(new HeartActivateOption());
                            hasOption = true;
                        }
                        break;
                    }
                }

                if (!hasCheck || !hasOption)
                {
                    _injectFailed = true;
                    XinEditorPlugin.LogError("「激活心」技艺注入失败：技能数据里找不到检查/行动结束效果的位置");
                    return false;
                }

                _injected = true;
                XinEditorPlugin.LogInfo("「激活心」技艺已就绪（技能 " + SkillId + "）");
                return true;
            }
            catch (Exception e)
            {
                _injectFailed = true;
                XinEditorPlugin.LogError("「激活心」技艺注入出错：" + e);
                return false;
            }
        }

        private static BattleSkillTableData FindSkillConfig()
        {
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return null;
                }
                return Singleton<ResManager>.Instance.CharacterBattleSkillData.Find(
                    (BattleSkillTableData o) => o != null && o.Id == SkillId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async void AddSkill(BattleRole role)
        {
            try
            {
                await role.AddBattleSkill(SkillId, false, SourceKey);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("发「激活心」技艺失败：" + e.Message);
            }
        }

        private static async void RemoveSkill(BattleRole role)
        {
            try
            {
                await role.RemoveBattleSkill(SkillId, false, SourceKey);
            }
            catch (Exception)
            {
                // 身上没有这条技能时游戏会抛，忽略即可
            }
        }

        /// <summary>该挂哪颗心：有自定义心就用他那颗，否则用默认心。</summary>
        internal static int ResolveHeartBuffId(BattleRole self)
        {
            try
            {
                if (self != null && self.Data != null)
                {
                    HeartDefinition def = XinEditorPlugin.Store.Find(self.Data);
                    if (def != null)
                    {
                        int id = HeartBuffBuilder.EnsureRegistered(def);
                        if (id > 0)
                        {
                            return id;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("取自定义心编号失败，改用默认心：" + e.Message);
            }
            return HeartConstants.DefaultHeartBuffId;
        }
    }

    /// <summary>能不能用这条技艺：不能已经有心，精神值也不能是衰弱/衰竭。</summary>
    internal class HeartActivateCheck : BattleSkillBaseCheck
    {
        public override bool IsReach(BattleRole source)
        {
            try
            {
                if (source == null || source.Data == null)
                {
                    return false;
                }
                if (source.Data.SanState == ESanState.Weak || source.Data.SanState == ESanState.Collapse)
                {
                    return false;
                }
                if (source.GetBuff(HeartConstants.DefaultHeartBuffId) != null)
                {
                    return false;
                }
                HeartDefinition def = XinEditorPlugin.Store.Find(source.Data);
                if (def != null && def.BuffId > 0 && source.GetBuff(def.BuffId) != null)
                {
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>行动结束：按角色挂上对应的那颗心（数据里写不出动态编号，所以放在插件里）。</summary>
    internal class HeartActivateOption : BattleSkillBaseOption
    {
        public override async Task Run(BattleActiveBehaviorData data, ParamTransmitionData param)
        {
            try
            {
                BattleRole self = data != null ? data.Self : null;
                if (self == null || self.Data == null)
                {
                    return;
                }
                int heartId = HeartBattleSkill.ResolveHeartBuffId(self);
                await self.AddBuff(self, heartId);
                XinEditorPlugin.LogInfo("「" + HeartStore.SafeRoleName(self.Data) + "」用技艺点亮了【心】（状态 " + heartId + "）");
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("「激活心」行动出错：" + e);
            }
        }
    }

    // 战斗开始/结束时按特质状态发或摘「激活心」技艺。
    // （私货特质的战斗节点原来借这里转发一句，2026-09-22 已随私货一起拆到「可视化攻击目标」）
    [HarmonyPatch(typeof(BattleRole), "TriggerBuffs")]
    internal static class Patch_BattleRole_TriggerBuffs
    {
        private static void Prefix(BattleRole __instance, EBuffTriggerType trigger)
        {
            HeartBattleSkill.OnBattleTrigger(__instance, trigger);
        }
    }
}
