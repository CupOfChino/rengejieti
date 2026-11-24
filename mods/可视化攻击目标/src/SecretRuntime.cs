// 私货特质的运行时入口（2026-09-22 从「自定义心」的 XinEditorPlugin.cs 拆过来）。
//
// 干两件事：
//   · Install()：把 SecretTraits.cs / ZangHua.cs 里那批 Harmony 补丁挂上（补丁类本身在各自文件里）；
//   · Tick()：每帧推进"排队的回击 / 段间缓冲"，每 10 秒补一次"进出模组监听 + 数值对齐 + 英文文案"。
//
// 为什么要拆出来单放：原来这些补丁是「自定义心」的插件挂的，私货逻辑等于借住在别人的 DLL 里。
// 现在私货的数据、插件都归「可视化攻击目标」，两个 mod 才算真正各走各的。

using System;
using System.IO;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using HarmonyLib;

namespace AttackTargetVisualizer
{
    internal static class SecretRuntime
    {
        private static float _timer;
        private static bool _slowStarted;
        private static string _configRoot;
        private static string _termDirectory;

        /// <summary>插件启动时调一次：挂上私货的全部 Harmony 补丁。</summary>
        internal static void Install(Harmony harmony, string pluginLocation)
        {
            string pluginDirectory = Path.GetDirectoryName(pluginLocation);
            if (!string.IsNullOrEmpty(pluginDirectory))
            {
                _configRoot = Path.GetFullPath(Path.Combine(pluginDirectory, "..", "Project_Depersonal",
                    "Assets", "Resources", "Config", "Game"));
                _termDirectory = Path.Combine(_configRoot, "TermData");
            }
            PatchOne(harmony, typeof(Patch_SecretTerms_ConfigLoaded));
            PatchOne(harmony, typeof(Patch_BuffData_AddBuff));             // 百合花环：免疫骨折/流血/中毒/燃烧
            PatchOne(harmony, typeof(Patch_BattleBaseAI_AutoSelectTarget)); // 百合花：敌人魅惑行动优先指向她
            PatchOne(harmony, typeof(Patch_UpdateRollDiceTmpDiceCount));   // 百合花：魅惑行动的奖惩骰
            PatchOne(harmony, typeof(Patch_GetSkillEffectCount));          // 灰暗孤影 / 葬花：近战攻击整套再来一遍
            PatchOne(harmony, typeof(Patch_CalculationDamage));            // 百合花与记忆的双剑：伤害增减和法术免伤
            PatchOne(harmony, typeof(Patch_BattleRole_SetLiftState));      // 灰暗孤影：每局 1 次免死；葬花：击杀回充能
            PatchOne(harmony, typeof(Patch_GetAttackAdditionalEffect));    // 记忆的双剑：近战命中上流血
            PatchOne(harmony, typeof(Patch_DiceShowAndTriggerEffectProcess)); // 记忆的双剑：副手免惩罚骰
            PatchOne(harmony, typeof(Patch_UpdateCrazyBuffLayer));         // 私货特质不算疯狂特质
            PatchOne(harmony, typeof(Patch_CheckCrazyTraitWakeAchievements));
            PatchOne(harmony, typeof(SecretTraitAvailability));
            PatchOne(harmony, typeof(HideDefaultSecretTraits));
            PatchOne(harmony, typeof(Patch_BuffDiceCheck_GetDiceResultList)); // 百合花：被魅惑时意志检定 +1 惩罚骰
            PatchOne(harmony, typeof(Patch_GetDiceData));                     // 同上（面板投骰那条路）
            PatchOne(harmony, typeof(Patch_Item_IsNeedBoth));                 // 记忆的双剑：双手近战武器只占 1 个槽
            PatchOne(harmony, typeof(Patch_RoleData_EquipWeapon));            // 同上：装备前把物品归属补上
            PatchOne(harmony, typeof(Patch_ActionEffectProcess));             // 灰暗孤影：多段攻击的段间缓冲
            PatchOne(harmony, typeof(Patch_ZangHua_DeathClearMark));          // 葬花：标记"死亡清理中"（好让剑痕能被清掉）
            PatchOne(harmony, typeof(Patch_ZangHua_MarkCantBeRemoved));       // 葬花：剑痕不可被驱散
            PatchOne(harmony, typeof(Patch_ZangHua_MarkOnHit));               // 葬花：每一段命中各叠 1 层剑痕
            PatchOne(harmony, typeof(Patch_ZangHua_SlashEffect));             // 葬花：按攻击动作选择单体斩击或横扫
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockDamage));          // 百合花环：流血/燃烧的效果不生效
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockPercent));         // 百合花环：中毒的削属性不生效
            PatchOne(harmony, typeof(Patch_LilyWreath_BlockFracture));        // 百合花环：骨折不生效
            PatchOne(harmony, typeof(Patch_JingJi_SetDamage));              // 荆棘：命中特效与叠层
            PatchOne(harmony, typeof(Patch_JingJi_SplitHudDamage));         // 荆棘：同一帧显示三个伤害数字
            PatchOne(harmony, typeof(Patch_Moli_RecordMeleeHit));
            PatchOne(harmony, typeof(Patch_Moli_FlowerScent));
            PatchOne(harmony, typeof(Patch_Moli_ReduceSanLoss));
            PatchOne(harmony, typeof(Patch_Moli_DodgeCounter));
            PatchOne(harmony, typeof(Patch_JingJi_DamageDisplay));         // 仅标记荆棘主动命中的扣血跳字
            PatchOne(harmony, typeof(Patch_SecretTraits_BattleRole_TriggerBuffs)); // 战斗开始/结束/回合结束等节点
            PatchOne(harmony, typeof(Patch_FlowerScent_DiceCheckValue));           // 花香：意志检定 +5/层
            PatchOne(harmony, typeof(Patch_WhiteHair_StrengthDice));               // 白毛少女：力量检定/对抗 +敏捷的一半
            PatchOne(harmony, typeof(Patch_FlowerScent_ExploreRound));             // 花香：探索回合也减半
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
            MoliAmbientFx.Tick();
            BattleBladeFx.Tick();

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
                return;
            }
            if (_timer >= 10f)
            {
                // 语言能在游戏设置里随时改，隔一会儿重贴一次英文文案；
                // TickSlow 里带"随特质苏醒的数值状态"对齐，也一起复核
                _timer = 0f;
                SecretText.ApplyDataOverlay();
                SecretTraits.TickSlow();
            }
        }

        // 工坊术语有时未进原版工厂。配置加载完成后，从模组自己的术语文件补上缺项。
        internal static void ConfigureWhiteHairAction()
        {
            WhiteHairAction.EnsureSkill(_configRoot);
        }

        internal static void EnsureTerms()
        {
            if (_termDirectory == null || !Singleton<ResManager>.HasInstance)
            {
                return;
            }
            TermFactory factory = Singleton<ResManager>.Instance.TermResFactory;
            if (factory == null)
            {
                return;
            }

            const int firstId = 880001;
            const int lastId = 880014;
            const int total = lastId - firstId + 1;
            int added = 0;
            int missing = 0;
            for (int id = firstId; id <= lastId; id++)
            {
                if (factory.GetCache(id.ToString()) != null)
                {
                    continue;
                }
                string path = Path.Combine(_termDirectory, id + ".txt");
                TermData term = File.Exists(path) ? GameHelper.ESLoad<TermData>("Data", path) : null;
                if (term == null || term.Id != id)
                {
                    missing++;
                    AttackTargetPlugin.LogError("私货术语文件无法读取：" + path);
                    continue;
                }
                factory.UGC.Add(term);
                added++;
            }
            if (added > 0)
            {
                factory.ClearCachData();
            }
            AttackTargetPlugin.LogInfo("私货术语：已就绪 " + (total - missing) + " / " + total + " 条，本次补载 " + added + " 条");
        }
    }

    [HarmonyPatch(typeof(WorkshopHelper), nameof(WorkshopHelper.EndLoadConfigData))]
    internal static class Patch_SecretTerms_ConfigLoaded
    {
        private static void Postfix()
        {
            try
            {
                SecretRuntime.EnsureTerms();
                SecretRuntime.ConfigureWhiteHairAction();
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货术语：载入失败：" + e.Message);
            }
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

        private static void Postfix(BattleRole __instance, EBuffTriggerType trigger, ref Task __result)
        {
            if (__instance?.Data == null)
            {
                return;
            }
            switch (trigger)
            {
                case EBuffTriggerType.BattleStart:
                case EBuffTriggerType.ActionEnd:
                case EBuffTriggerType.AttackDodgeSuccess:
                    __result = FinishTrigger(__result, __instance, trigger);
                    break;
            }
        }

        private static async Task FinishTrigger(Task original, BattleRole role, EBuffTriggerType trigger)
        {
            await original;
            try
            {
                switch (trigger)
                {
                    case EBuffTriggerType.BattleStart:
                        await WhiteHairAction.OnBattleStart(role);
                        break;
                    case EBuffTriggerType.ActionEnd:
                        await MoliCombat.FinishMeleeAction(role);
                        break;
                    case EBuffTriggerType.AttackDodgeSuccess:
                        await MoliCombat.GiveStealthForDodge(role);
                        break;
                }
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("茉莉战斗效果（" + trigger + "）：" + error);
            }
        }
    }
}
