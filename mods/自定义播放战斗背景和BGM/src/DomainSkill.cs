// 【领域展开】技艺（战斗技能 881005）。
//
// 数据里只有技能名/说明和一个"行动开始时"的空效果位；
// 插件在运行时做两件事：
//   · 给"拥有【领域】特质 + 有配置"的角色发这条技能（战斗开始时发、战斗结束时摘）；
//   · 往技能的效果列表里注入真正的效果（DomainExpandOption），运行时切换背景与BGM。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using GamePlayEvent;
using MOD;

namespace CustomBattleBg
{
    internal static class DomainSkill
    {
        private static bool _injected;
        private static bool _injectFailed;

        /// <summary>
        /// 战斗开始：有特质且有配置 → 发技艺；否则摘掉。
        /// 注意不看特质的苏醒状态：苏醒时战斗开始会自动展开（见 DomainBattle），
        /// 沉睡时靠这条技艺主动展开——用户口径是"处于苏醒**或者**进行了领域展开行动"。
        /// </summary>
        internal static void OnBattleStart(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !role.IsAlly)
                {
                    return;
                }
                MOD_Dynamic_Trait trait = role.Data.GetTraitData(DomainConstants.TraitId);
                if (trait == null)
                {
                    RemoveSkill(role);
                    return;
                }
                DomainProfile profile = CustomBattleBgPlugin.Store.Find(role.Data);
                if (profile == null || !profile.HasAnything)
                {
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
                CustomBattleBgPlugin.LogError("处理「领域展开」技艺失败：" + e.Message);
            }
        }

        internal static void OnBattleEnd(BattleRole role)
        {
            RemoveSkill(role);
        }

        // 把数据写不出来的效果塞进技能配置，只做一次
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
                if (cfg.EffectList == null)
                {
                    _injectFailed = true;
                    CustomBattleBgPlugin.LogError("「领域展开」技艺注入失败：技能没有效果列表");
                    return false;
                }

                bool ok = false;
                for (int i = 0; i < cfg.EffectList.Count; i++)
                {
                    BattleSkillEffectData effect = cfg.EffectList[i];
                    if (effect == null || effect.Trigger != EBattleSkillTrigger.ActionStart)
                    {
                        continue;
                    }
                    if (effect.EffectOption == null)
                    {
                        effect.EffectOption = new List<BattleSkillBaseOption>();
                    }
                    bool has = false;
                    for (int j = 0; j < effect.EffectOption.Count; j++)
                    {
                        if (effect.EffectOption[j] is DomainExpandOption)
                        {
                            has = true;
                            break;
                        }
                    }
                    if (!has)
                    {
                        effect.EffectOption.Add(new DomainExpandOption());
                    }
                    ok = true;
                    break;
                }

                if (!ok)
                {
                    _injectFailed = true;
                    CustomBattleBgPlugin.LogError("「领域展开」技艺注入失败：找不到「行动开始时」的效果位");
                    return false;
                }

                _injected = true;
                CustomBattleBgPlugin.LogInfo("「领域展开」技艺已就绪（技能 " + DomainConstants.SkillId + "）");
                return true;
            }
            catch (Exception e)
            {
                _injectFailed = true;
                CustomBattleBgPlugin.LogError("「领域展开」技艺注入出错：" + e);
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
                    (BattleSkillTableData o) => o != null && o.Id == DomainConstants.SkillId);
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
                await role.AddBattleSkill(DomainConstants.SkillId, false, DomainConstants.SkillSourceKey);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("发「领域展开」技艺失败：" + e.Message);
            }
        }

        private static async void RemoveSkill(BattleRole role)
        {
            try
            {
                await role.RemoveBattleSkill(DomainConstants.SkillId, false, DomainConstants.SkillSourceKey);
            }
            catch (Exception)
            {
                // 身上没有这条技能时游戏会抛，忽略即可
            }
        }
    }

    /// <summary>行动开始时：切换成自己的背景 + BGM。</summary>
    internal class DomainExpandOption : BattleSkillBaseOption
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
                DomainBattle.ExpandFor(self, "领域展开");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("「领域展开」行动出错：" + e);
            }
            await Task.CompletedTask;
        }
    }
}
