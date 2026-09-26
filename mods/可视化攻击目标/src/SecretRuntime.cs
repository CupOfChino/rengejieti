// 私货特质的运行时入口（2026-09-22 从「自定义心」的 XinEditorPlugin.cs 拆过来）。
//
// 干两件事：
//   · Install()：把 SecretTraits.cs / ZangHua.cs 里那批 Harmony 补丁挂上（补丁类本身在各自文件里）；
//   · Tick()：每帧推进"排队的回击 / 段间缓冲"，每 10 秒补一次"进出模组监听 + 数值对齐 + 英文文案"。
//
// 为什么要拆出来单放：原来这些补丁是「自定义心」的插件挂的，私货逻辑等于借住在别人的 DLL 里。
// 现在私货的数据、插件都归「可视化攻击目标」，两个 mod 才算真正各走各的。

using System;
using System.Collections.Generic;
using Game;
using GamePlayEvent;
using HarmonyLib;

namespace AttackTargetVisualizer
{
    internal static class SecretRuntime
    {
        private static float _timer;
        private static bool _slowStarted;

        /// <summary>插件启动时调一次：挂上私货的全部 Harmony 补丁。</summary>
        internal static void Install(Harmony harmony)
        {
            PatchOne(harmony, typeof(Patch_BuffData_AddBuff));             // 百合花：魅惑时长 ×2
            PatchOne(harmony, typeof(Patch_BattleBaseAI_AutoSelectTarget)); // 百合花：敌人魅惑行动优先指向她
            PatchOne(harmony, typeof(Patch_UpdateRollDiceTmpDiceCount));   // 百合花：魅惑行动的奖惩骰
            PatchOne(harmony, typeof(Patch_GetSkillEffectCount));          // 灰暗孤影 / 葬花：近战攻击整套再来一遍
            PatchOne(harmony, typeof(Patch_CalculationDamage));            // 灰暗孤影：法术免伤；记忆的双剑：近战增减伤
            PatchOne(harmony, typeof(Patch_BattleRole_SetLiftState));      // 灰暗孤影：每局 1 次免死；葬花：击杀回充能
            PatchOne(harmony, typeof(Patch_GetAttackAdditionalEffect));    // 记忆的双剑：近战命中上流血
            PatchOne(harmony, typeof(Patch_DiceShowAndTriggerEffectProcess)); // 记忆的双剑：副手免惩罚骰
            PatchOne(harmony, typeof(Patch_UpdateCrazyBuffLayer));         // 私货特质不算疯狂特质
            PatchOne(harmony, typeof(Patch_CheckCrazyTraitWakeAchievements));
            PatchOne(harmony, typeof(Patch_BuffDiceCheck_GetDiceResultList)); // 百合花：被魅惑时意志检定 +1 惩罚骰
            PatchOne(harmony, typeof(Patch_GetDiceData));                     // 同上（面板投骰那条路）
            PatchOne(harmony, typeof(Patch_Item_IsNeedBoth));                 // 记忆的双剑：双手近战武器只占 1 个槽
            PatchOne(harmony, typeof(Patch_RoleData_EquipWeapon));            // 同上：装备前把物品归属补上
            PatchOne(harmony, typeof(Patch_ActionEffectProcess));             // 灰暗孤影：多段攻击的段间缓冲
            PatchOne(harmony, typeof(Patch_ZangHua_DeathClearMark));          // 葬花：标记"死亡清理中"（好让剑痕能被清掉）
            PatchOne(harmony, typeof(Patch_ZangHua_MarkCantBeRemoved));       // 葬花：剑痕不可被驱散
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockDamage));          // 百合花环：流血/燃烧的效果不生效
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockPercent));         // 百合花环：中毒的削属性不生效
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockFracture));        // 百合花环：骨折不生效
            PatchOne(harmony, typeof(Patch_JingJi_GetDiceCheckValue));     // 荆棘：斗殴 + 敏捷联合检定
            PatchOne(harmony, typeof(Patch_JingJi_SetDamage));             // 荆棘：伤害接管（先发/反击 = 武器基础伤害；主动攻击拆 3 次）
            PatchOne(harmony, typeof(Patch_SecretTraits_BattleRole_TriggerBuffs)); // 战斗开始/结束/回合结束等节点
        }

        // 一个一个挂：万一某个方法在游戏更新后改名了，也只丢它自己，不会连累别的钩子
        private static void PatchOne(Harmony harmony, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                AttackTargetPlugin.LogInfo("钩子已挂：" + type.Name);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("钩子挂失败（" + type.Name + "）：" + e.Message);
            }
        }

        /// <summary>每帧调用（AttackTargetPlugin.Update 里）。</summary>
        internal static void Tick()
        {
            // 每帧：处理排队的回击（记忆的双剑）、放行段间缓冲
            SecretTraits.Tick();

            _timer += UnityEngine.Time.unscaledDeltaTime;
            if (!_slowStarted)
            {
                // 游戏刚起来时状态表还没加载好，等 3 秒再做第一轮
                if (_timer < 3f)
                {
                    return;
                }
                _slowStarted = true;
                _timer = 0f;
                SecretText.ApplyDataOverlay();
                SecretTraits.TickSlow();
                EnsureTerms();
                return;
            }
            if (_timer >= 10f)
            {
                // 语言能在游戏设置里随时改，隔一会儿重贴一次英文文案；
                // TickSlow 里带"随特质苏醒的数值状态"对齐，也一起复核
                _timer = 0f;
                SecretText.ApplyDataOverlay();
                SecretTraits.TickSlow();
                EnsureTerms();
            }
        }

        // =====================================================================
        // 关键词术语（装备面板右边那一栏）
        //
        // 数据侧也有一份（Project_Depersonal\...\Config\Game\TermData\88000x.txt），
        // 理论上会被 LoadAllFactoryData 扫进去，但 2026-09-24 实测装备面板读不到
        // （同一套机制里的 BuffsLink 倒是好的）。所以这里直接把 TermData 对象塞进
        // 工厂的 UGC 列表再清缓存 —— 这条路完全可控（见 AI协作文档 第 10 节"运行时新增数据"）。
        // 已经有的（数据文件加载成功 / 之前注入过）就跳过，不会重复。
        // =====================================================================

        internal static void EnsureTerms()
        {
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return;
                }
                TermFactory factory = Singleton<ResManager>.Instance.TermResFactory;
                if (factory == null)
                {
                    return;
                }
                int added = 0;
                added += InjectTerm(factory, 880001, "剑痕",
                    "葬花命中时叠加的负面状态。每层：护甲-1、受到伤害+15%、闪避-10、运动-10。回合结束时层数减半（向下取整），并按减少的层数获得等量【流血】；最高5层，不可驱散。");
                added += InjectTerm(factory, 880002, "先发",
                    "进入战斗时判定一次：副手装备荆棘（或视为双手）才获得先发资格。战斗中会持续跟踪，一旦不再满足条件就失去资格（本场不再恢复）；中途才换上荆棘也不会补发资格。\n每个行动轮：轮开始前给随机一名敌人挂【目标锁定】，回合开始时用武器对它打一次。\n该次攻击无需检定（按普通成功结算），敌人无法闪避；伤害固定为武器基础伤害，只受目标护甲/减伤等影响（自己身上的加成一律不算），命中给目标叠1~2层【荆棘】。");
                added += InjectTerm(factory, 880003, "持握·葬花",
                    "装备在主手：此武器伤害+1，可施展二连斩与横扫。\n装备在副手：护甲+2，受到的魔法伤害-1。\n主/副手未装备其他武器时视为双手：同时触发主手与副手效果，且此武器施加的负面状态+1层。");
                added += InjectTerm(factory, 880004, "持握·荆棘",
                    "装备在主手：此武器伤害+3。\n装备在副手：【先发】进入战斗时判定资格；之后每轮先给随机敌人挂【目标锁定】，再对它打一次。\n主/副手未装备其他武器时视为双手：同时触发主手与副手效果。\n速度差：每高于目标100点，此武器伤害+10%；双手持握时每50点一档。");
                added += InjectTerm(factory, 880005, "荆棘",
                    "命中时给目标叠加的预告状态，本身没有任何效果。每1次伤害叠1~2层（大成功时翻倍）。行动轮开始时，它整颗转为等量的【束缚】；【束缚】每层使速度-10（最低降到10点），回合结束时移除。");
                added += InjectTerm(factory, 880006, "目标锁定",
                    "荆棘先发用的标记：轮开始前挂给随机一名敌人，回合开始时用武器对它打一次；回合结束、目标死亡或战斗结束时解除。");
                if (added > 0)
                {
                    factory.ClearCachData();
                    AttackTargetPlugin.LogInfo("私货术语：注入 " + added + " 条关键词说明（装备面板右栏用）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货术语：注入出错：" + e.Message);
            }
        }

        private static int InjectTerm(TermFactory factory, int id, string name, string des)
        {
            if (HasTerm(factory, id))
            {
                return 0;
            }
            TermData term = new TermData();
            term.Id = id;
            term.Name = new LocalizationTermKeyData();
            term.Name.InputText = name;
            term.Des = new LocalizationTermKeyData();
            term.Des.InputText = des;
            factory.UGC.Add(term);
            return 1;
        }

        private static bool HasTerm(TermFactory factory, int id)
        {
            try
            {
                List<TermData> all = factory.All;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] != null && all[i].Id == id)
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
    }

    // 战斗开始/结束/回合结束这些节点：私货特质（白毛少女 / 灰暗孤影 / 葬花）要接一下。
    // 原来这条补丁长在「自定义心」的 HeartBattleSkill.cs 里（顺带转发一句），分离后归私货自己。
    [HarmonyPatch(typeof(BattleRole), "TriggerBuffs")]
    internal static class Patch_SecretTraits_BattleRole_TriggerBuffs
    {
        private static void Prefix(BattleRole __instance, EBuffTriggerType trigger)
        {
            SecretTraits.OnBattleTrigger(__instance, trigger);
        }
    }
}
