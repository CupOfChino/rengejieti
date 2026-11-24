// 茉莉专属武器「荆棘」（Item 880006）的插件逻辑 —— 和「葬花」配成一对的双剑。
//
// 数据侧（由 tools\make_JingJi_item.ps1 生成）：
//   Item 880006  荆棘：1D4+1、单手、单段、神话+不可出售、绑定角色
//                装备时 敏捷 +5 / 斗殴 +5 / 速度 +20（卸下时自动移除）
//
// 这里做的事（口径按 2026-09-27 用户最新定稿）：
//   1. 攻击检定 = **斗殴**（正常显示）；另做一次**隐藏的敏捷检定**（不弹骰子）决定挂不挂【荆棘】。
//      隐藏检定的分档与游戏一致：骰 ≤ 敏捷/5 大成功、≤ /2 困难成功、≤ 敏捷 普通成功，否则失败
//      （见 RollHiddenDexCheck；规则是对着游戏日志反推、核对过的）。
//   2. 主动攻击：一次判定、一次伤害结算，最终跳字均分成三个同屏数字。
//      隐藏敏捷失败不叠、成功叠 3 层、大成功叠 4 层。
//   3. 普通、困难、大成功的伤害倍率为 ×0.5、×0.75、×1。
//   4. 主手武器伤害 +2，副手获得先发。另一只手没有武器时视作双手持握，兼得两者。
//   5. 速度领先每 100 点增伤 5%，双手持握每 50 点一档。
//   6. 先发掷一次武器基础伤害并计入目标防御。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using GamePlayEvent;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace AttackTargetVisualizer
{
    internal static class JingJi
    {
        internal const int WeaponItemId = 880006;

        private const float NormalHitRatio = 0.5f;
        private const float HardHitRatio = 0.75f;

        internal const int StrikeCount = 3;

        /// <summary>装备在主手时的武器伤害加成。</summary>
        private const int MasterDamageBonus = 2;

        /// <summary>
        /// 速度差台阶（2026-09-27 用户口径）：**常驻**，不再要求双手 ——
        /// 双手（另一只手空）每高于目标 50 点速度 → 伤害 +5%；单手时每 100 点一档。
        /// </summary>
        private const int SpeedStepTwoHanded = 50;
        private const int SpeedStepOneHanded = 100;
        private const float SpeedStepBonus = 0.05f;

        // 注：【荆棘】挂层 2026-09-27 定稿 —— 由一次**隐藏的敏捷检定**决定（整次攻击判一次）：
        //     主动命中时，失败不叠、普通或困难成功叠 3 层、大成功叠 4 层（见 RollHiddenDexCheck）。

        // =====================================================================
        // 槽位判定
        // =====================================================================

        internal static bool IsJingJi(MOD_Dynamic_Item item)
        {
            return item != null && item.Id == WeaponItemId;
        }

        private static MOD_Dynamic_Item WeaponAt(BattleRole role, int index)
        {
            try
            {
                List<MOD_Dynamic_Item> list = role != null && role.Data != null ? role.Data.Weapons : null;
                if (list == null || index < 0 || index >= list.Count)
                {
                    return null;
                }
                return list[index];
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 荆棘现在装在哪：主手 / 副手 / 视为双手。
        /// "视为双手" = 另一个武器槽是空的（只有这一把武器）。
        /// </summary>
        internal static void GetSlot(BattleRole role, out bool onMaster, out bool onOffhand, out bool asTwoHanded)
        {
            onMaster = false;
            onOffhand = false;
            asTwoHanded = false;
            MOD_Dynamic_Item main = WeaponAt(role, 0);
            MOD_Dynamic_Item off = WeaponAt(role, 1);
            if (IsJingJi(main))
            {
                onMaster = true;
            }
            if (IsJingJi(off))
            {
                onOffhand = true;
            }
            if (onMaster && off == null)
            {
                asTwoHanded = true;
            }
            if (onOffhand && main == null)
            {
                asTwoHanded = true;
            }
        }

        /// <summary>身上有没有荆棘（任意槽、含背包）。</summary>
        internal static bool HasWeapon(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return false;
                }
                List<MOD_Dynamic_Item> list = role.Data.Weapons;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (IsJingJi(list[i]))
                        {
                            return true;
                        }
                    }
                }
                List<MOD_Dynamic_Item> bag = role.Data.BagItems;
                if (bag != null)
                {
                    for (int i = 0; i < bag.Count; i++)
                    {
                        if (IsJingJi(bag[i]))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>这次技能用的是不是荆棘（isOffhand 决定看哪个槽）。</summary>
        internal static bool IsJingJiSkill(BattleSkillData skill, bool isOffhand)
        {
            try
            {
                if (skill == null)
                {
                    return false;
                }
                return IsJingJi(isOffhand ? skill.OffHandWeapon : skill.MasterHandWeapon);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        // 1. 攻击检定（2026-09-27 改版）
        //    原来这里是「斗殴 + 敏捷」联合检定（把敏捷加进斗殴的检定值）。
        //    用户口径改成：**主检定 = 斗殴**（正常显示，结果影响伤害倍率），
        //    另做一次**隐藏的敏捷检定**（不弹骰子）决定挂不挂【荆棘】 ——
        //    实现见 RollHiddenDexCheck；挂层在 PrepareDamage / 先发 / 反击里。
        // =====================================================================

        // =====================================================================
        // 2. 伤害的接管点（2026-09-27 定稿）
        //    `CalculationDamage` 的 Prefix（在 SecretTraits 的补丁里）：
        //      · 先发 → 只算防御侧（TryBaseDamageWithDefense）
        //    `BattleRole.SetDamage` 的 Prefix：
        //      · 荆棘主动攻击 → 播突刺并决定荆棘层数；伤害由原版结算一次
        //      · 先发 → 以隐藏敏捷检定决定叠层
        //    旧版的"3 段 + 钉住判定 + 跳过骰子动画"已删：
        //    段数游标在"追加行动"（灰暗孤影）时会残留，把下一次行动的判定也钉住，属于隐患。
        // =====================================================================

        /// <summary>
        /// 战斗开始 / 结束时清一遍。
        ///
        /// ⚠ 2026-09-27 修："先发资格"（FirstStrikeReady）**不能**在这里清 ——
        /// `BattleStart` 是**逐个角色**触发的，清一次就被后来的角色清空一次：
        /// 茉莉拿到资格后，她的队友接着触发 BattleStart 又把集合清空，
        /// 等到轮开始要挂【目标锁定】时 `Contains(茉莉)` 已经是 false → 静默返回，
        /// 表现就是"有先发资格日志、但从来没有目标锁定"（用户实测发现）。
        /// 资格改由 `MarkFirstStrikeReady` 自己管（有资格就加、没资格就摘），
        /// 战斗结束时由 `ClearFirstStrikeReady` 统一清。
        /// </summary>
        internal static void ClearState()
        {
            PendingShufu.Clear();
            ConvertingShufu.Clear();
            InGuardedCalc = false;
        }

        // =====================================================================
        // 3 & 4. 伤害倍率 + 破甲 + 主手加成 + 双手速度加成
        // =====================================================================

        /// <summary>已经处理过的伤害数据（同一次伤害会被算好几遍：预览、日志、结算…）。</summary>
        private static readonly ConditionalWeakTable<DamageAdditionalData, object> Touched =
            new ConditionalWeakTable<DamageAdditionalData, object>();

        /// <summary>
        /// `BattleHelper.CalculationDamage` 的 Prefix 里调用：按判定结果改这一段的倍率与护甲规则。
        /// </summary>
        internal static void OnDamageCalculate(BattleRole source, BattleRole target, DamageAdditionalData addData)
        {
            try
            {
                if (addData == null || !addData.IsWeaponDamage || !IsJingJi(addData.Weapon))
                {
                    return;
                }
                if (addData.SourceType != EDamageSourceType.BattleSkill)
                {
                    // 只有"正常行动打出来"的伤害才吃荆棘的倍率 / 破甲 / 持握规则。
                    // 先发走另一套：只造成武器基础伤害（见 TryBaseDamageWithDefense）。
                    return;
                }
                if (Touched.TryGetValue(addData, out object _))
                {
                    return;   // 同一次伤害只处理一遍
                }
                Touched.Add(addData, new object());

                // 局部变量别叫 result —— 它会遮蔽外面那个 ref int result（那是伤害值，2026-09-27 踩）
                EDiceResult diceResult = addData.DiceResult;

                // 普通 ×0.5，困难 ×0.75，大成功 ×1。
                float ratio;
                if (diceResult == EDiceResult.Success)
                {
                    ratio = NormalHitRatio;
                }
                else if (diceResult == EDiceResult.DiffSuc)
                {
                    ratio = HardHitRatio;
                }
                else
                {
                    ratio = 1f;
                }

                // 速度差加成（2026-09-27 用户口径：常驻；双手每 50 点一档、单手每 100 点一档）
                bool onMaster, onOffhand, asTwoHanded;
                GetSlot(source, out onMaster, out onOffhand, out asTwoHanded);
                int speedStep = asTwoHanded ? SpeedStepTwoHanded : SpeedStepOneHanded;
                int speedDiff = 0;
                if (source != null && target != null && source.Data != null && target.Data != null)
                {
                    speedDiff = source.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.Speed)
                              - target.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.Speed);
                    if (speedDiff > 0)
                    {
                        int steps = speedDiff / speedStep;
                        if (steps > 0)
                        {
                            ratio *= 1f + SpeedStepBonus * steps;
                        }
                    }
                }

                addData.DamageRatio = ratio;

                // 主手（含"视为双手"）：武器伤害 +2
                if (onMaster || asTwoHanded)
                {
                    addData.ExtraItemAddDamage += MasterDamageBonus;
                }

                AttackTargetPlugin.LogInfo("荆棘：判定 " + diceResult + " → 本次伤害倍率 ×" + ratio.ToString("0.###") +
                    ((onMaster || asTwoHanded) ? "，主手 +" + MasterDamageBonus : "") +
                    ((speedDiff > 0) ? "，速度差 " + speedDiff + "（" + (asTwoHanded ? "双手" : "单手") +
                        "每 " + speedStep + " 点一档）" : ""));
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：伤害结算出错：" + e.Message);
            }
        }

        // =====================================================================
        // 4.5 进副本发武器（和葬花同一条路：带「记忆的双剑」的人手上有就跳过）
        // =====================================================================

        private static readonly HashSet<BattleRole> WeaponGiven = new HashSet<BattleRole>();

        /// <summary>
        /// 进副本时给队里该有荆棘的人都检查一遍（主角队 + 后备队）。
        /// 和葬花一样挂在"进入模组"那个节点上 —— 只挂战斗开始的话，
        /// 玩家进了副本还没打起来的时候，装备栏里是看不到这把剑的（2026-09-24 踩到）。
        /// </summary>
        internal static void EnsureWeaponForTeam()
        {
            try
            {
                int checkedCount = 0;
                List<BattleRole> team = BattleHelper.GetHeroTeamRoleList();
                if (team != null)
                {
                    for (int i = 0; i < team.Count; i++)
                    {
                        EnsureWeapon(team[i]);
                        checkedCount++;
                    }
                }
                List<BattleRole> backup = BattleHelper.GetHeroBackupTeamRoleList();
                if (backup != null)
                {
                    for (int i = 0; i < backup.Count; i++)
                    {
                        EnsureWeapon(backup[i]);
                        checkedCount++;
                    }
                }
                AttackTargetPlugin.LogInfo("荆棘：进入副本，检查了 " + checkedCount + " 名队员");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：进副本发武器出错：" + e.Message);
            }
        }

        internal static void EnsureWeapon(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !role.IsAlly)
                {
                    return;
                }
                if (!SecretTraits.HasTraitWake(role, SecretIds.MemoryTrait))
                {
                    return;   // 只有带「记忆的双剑」的那个人（茉莉）才有这把武器
                }
                if (HasWeapon(role))
                {
                    return;   // 武器栏 / 背包里已经有了
                }
                if (WeaponGiven.Contains(role))
                {
                    AttackTargetPlugin.LogInfo("荆棘：「" + SecretTraits.NameOf(role) +
                        "」这次已经发过了但手上没有（可能在仓库里），不再补发");
                    return;
                }
                WeaponGiven.Add(role);
                GiveWeapon(role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：发武器出错：" + e.Message);
            }
        }

        private static async void GiveWeapon(BattleRole role)
        {
            try
            {
                await role.Data.AddItem(WeaponItemId);
                AttackTargetPlugin.LogInfo("荆棘：「" + SecretTraits.NameOf(role) + "」拿到荆棘");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：发武器出错：" + e.Message);
            }
        }

        // =====================================================================
        // 5. 副手（先发）：回合开始时随机打 1 名敌人一次
        //    不需要联合检定（按普通成功结算），敌人也无法闪避 —— 走的是和"反击"同一条
        //    直接结算的路子（BattleHelper.StrickBackProcess 就是这么做）。
        // =====================================================================

        // =====================================================================
        // 6. 束缚（Buff 880050）：命中后攒着，**下一个行动轮开始**才挂到敌人身上；
        //    回合结束（所有人行动完成时）移除。层数无上限，每层速度 -10。
        //    2026-09-26 用户口径。
        // =====================================================================

        internal const int ShufuBuffId = 880050;

        /// <summary>「荆棘」（880051）：命中时挂的"预告"状态，行动轮开始时整颗换成等量的束缚。</summary>
        internal const int ThornBuffId = 880051;

        /// <summary>每层束缚削减的速度。</summary>
        private const int ShufuSpeedPerLayer = -10;

        /// <summary>束缚最多把速度压到这个值（2026-09-27 用户口径）。</summary>
        private const int MinSpeedAfterShufu = 10;


        /// <summary>速度削减的来源 key（加/摘成对，保证只有一份）。</summary>
        private const string ShufuSpeedKey = "jingji_shufu_speed";

        /// <summary>命中后攒着的、等下个行动轮开始要挂的束缚层数（按目标记）。</summary>
        private static readonly Dictionary<BattleRole, int> PendingShufu = new Dictionary<BattleRole, int>();

        /// <summary>
        /// 正在做"【荆棘】→【束缚】"转换的角色。
        /// 转换本身是 async 的（要先摘预告再加束缚），而"行动轮开始"这一刻
        /// TriggerBuffs 会被反复调用（敌我每个角色各一次），不加这道闸门会重复转换、层数翻倍。
        /// 2026-09-27 修"束缚一直挂不上"时补的护栏。
        /// </summary>
        private static readonly HashSet<BattleRole> ConvertingShufu = new HashSet<BattleRole>();

        /// <summary>
        /// 命中造成伤害后调：给目标挂 1 层【荆棘】（2026-09-26 用户口径）。
        /// 挂的是"预告"状态，本身没有效果；等到行动轮开始时整颗换成等量的【束缚】，
        /// 这样状态栏在命中当轮就能看出"下轮要挨什么"。
        /// </summary>
        /// <summary>
        /// 隐藏的敏捷检定（2026-09-27 用户口径）：**整次攻击只判一次**，不弹骰子、不动战斗随机记录。
        /// 判定规则和游戏自己的一致（对着日志反推、再按"目标 120 骰 10 = 大成功、骰 61/86/90 = 普通成功"核对过）：
        ///   骰 1~100 → ≤ 检定值/5 = 大成功；≤ 检定值/2 = 困难成功；≤ 检定值 = 普通成功；否则失败。
        /// 用途：决定这次攻击能不能给目标叠【荆棘】——
        /// 主动命中时，失败不叠、普通或困难成功叠 3 层、大成功叠 4 层。
        /// </summary>
        private static EDiceResult RollHiddenDexCheck(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return EDiceResult.Fail;
                }
                int checkValue = BattleHelper.GetDiceCheckValue(role, EExploreSkill.None, EHeroAttribute.DEX,
                    ERoleExtraAttribute.None, EExploreSkill.None, false);
                if (checkValue <= 0)
                {
                    return EDiceResult.Fail;
                }
                int roll = UnityEngine.Random.Range(1, 101);   // 隐藏检定：不弹骰子、不复用战斗随机记录
                if (roll <= Math.Max(1, checkValue / 5))
                {
                    AttackTargetPlugin.LogInfo("荆棘：隐藏敏捷检定 大成功（骰 " + roll + "，目标 " + checkValue + "）");
                    return EDiceResult.GreatSuccess;
                }
                if (roll <= Math.Max(1, checkValue / 2))
                {
                    AttackTargetPlugin.LogInfo("荆棘：隐藏敏捷检定 困难成功（骰 " + roll + "，目标 " + checkValue + "）");
                    return EDiceResult.DiffSuc;
                }
                if (roll <= checkValue)
                {
                    AttackTargetPlugin.LogInfo("荆棘：隐藏敏捷检定 成功（骰 " + roll + "，目标 " + checkValue + "）");
                    return EDiceResult.Success;
                }
                AttackTargetPlugin.LogInfo("荆棘：隐藏敏捷检定 失败（骰 " + roll + "，目标 " + checkValue +
                    "）→ 本次不叠【荆棘】");
                return EDiceResult.Fail;
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：隐藏敏捷检定出错：" + e.Message);
                return EDiceResult.Fail;
            }
        }

        /// <summary>
        /// 先发用：隐藏敏捷检定 → 成功挂 1 层、大成功再额外 +1 层、失败不挂。
        /// </summary>
        private static void RollThornByDex(BattleRole attacker, BattleRole target)
        {
            EDiceResult dexResult = RollHiddenDexCheck(attacker);
            if (dexResult == EDiceResult.Fail)
            {
                return;
            }
            AddThorn(target, 1);
            if (dexResult == EDiceResult.GreatSuccess)
            {
                AddThorn(target, 1);   // 大成功：整次额外 +1 层
            }
        }

        internal static void AddThorn(BattleRole target, int layers)
        {
            try
            {
                if (target == null || target.Data == null || target.IsDeath || layers <= 0) return;
                _ = AddThornAsync(target, layers);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 顺序叠加荆棘，避免并发添加状态时丢层。
        /// </summary>
        internal static async Task AddThornAsync(BattleRole target, int layers)
        {
            try
            {
                for (int i = 0; i < layers; i++)
                {
                    await target.AddBuff(null, ThornBuffId);
                }
                AttackTargetPlugin.LogInfo("荆棘：命中 → 「" + SecretTraits.NameOf(target) + "」获得 " + layers +
                    " 层【荆棘】（共 " + ThornLayer(target) + " 层）");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：挂预告状态出错：" + e.Message); }
        }

        /// <summary>「荆棘」（预告）层数。</summary>
        internal static int ThornLayer(BattleRole role)
        {
            try
            {
                BuffData b = role != null ? role.GetBuff(ThornBuffId) : null;
                return b != null ? b.CurLayer : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// 行动轮开始：把身上攒的【荆棘】整颗换成等量的【束缚】（对每个角色都会调，敌我通吃）。
        /// 2026-09-26 用户口径。
        /// </summary>
        internal static void ApplyPendingShufu(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.IsDeath) return;
                int count = ThornLayer(role);
                if (count <= 0) return;
                if (ConvertingShufu.Contains(role)) return;   // 已经在转的别重复转
                ConvertingShufu.Add(role);
                ConvertThornToShufu(role, count);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：荆棘转束缚出错：" + e.Message); }
        }

        private static async void ConvertThornToShufu(BattleRole target, int count)
        {
            try
            {
                // 2026-09-27：先打一条"开始转换"的日志 —— 之前转换没有任何输出，
                // 出问题时没法判断是"没被调用"还是"卡在中间"，所以把起点也记下来。
                AttackTargetPlugin.LogInfo("荆棘：行动轮开始 → 「" + SecretTraits.NameOf(target) + "」开始把 " +
                    count + " 层【荆棘】转成【束缚】");
                await target.RemoveBuff(ThornBuffId);
                // 一次挂上 + 补层数（原来是 for 循环逐个 AddBuff —— 层数一多会连着跑几十次事件链，既慢又容易出岔子）
                await target.AddBuff(null, ShufuBuffId);
                BuffData shufu = target.GetBuff(ShufuBuffId);
                if (shufu != null && count > 1)
                {
                    await shufu.ChangeLayer(target, count - 1);
                }
                SyncShufuSpeed(target);
                AttackTargetPlugin.LogInfo("荆棘：行动轮开始 → 「" + SecretTraits.NameOf(target) + "」的 " + count +
                    " 层预告转成【束缚】（共 " + ShufuLayer(target) + " 层）");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：荆棘转束缚出错：" + e.Message); }
            finally { ConvertingShufu.Remove(target); }
        }

        private static async void ApplyShufu(BattleRole target, int count)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    await target.AddBuff(null, ShufuBuffId);
                }
                SyncShufuSpeed(target);
                AttackTargetPlugin.LogInfo("荆棘：行动轮开始 → 「" + SecretTraits.NameOf(target) + "」获得 " +
                    count + " 层【束缚】（共 " + ShufuLayer(target) + " 层）");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：施加束缚出错：" + e.Message); }
        }

        /// <summary>回合结束：摘掉束缚（所有人行动完成后的时机）。</summary>
        internal static void ClearShufu(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                int layer = ShufuLayer(role);
                if (layer <= 0) return;
                RemoveShufu(role, layer);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：移除束缚出错：" + e.Message); }
        }

        private static async void RemoveShufu(BattleRole target, int layer)
        {
            try
            {
                await target.RemoveBuff(ShufuBuffId);
                await target.Data.ChangeAttr(false, new ChangeAttrData(ERoleExtraAttribute.Speed, "0"), ShufuSpeedKey);
                AttackTargetPlugin.LogInfo("荆棘：回合结束 → 移除「" + SecretTraits.NameOf(target) + "」的 " + layer + " 层【束缚】");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：移除束缚出错：" + e.Message); }
        }

        /// <summary>束缚层数（0 = 没中）。</summary>
        internal static int ShufuLayer(BattleRole role)
        {
            try
            {
                BuffData b = role != null ? role.GetBuff(ShufuBuffId) : null;
                return b != null ? b.CurLayer : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>按当前层数同步速度削减（先摘旧来源、再按层数加，幂等）。</summary>
        internal static async void SyncShufuSpeed(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                int layer = ShufuLayer(role);
                // 先摘掉自己这条来源 —— 一来保证幂等，二来摘完之后读到的速度才是"没被束缚削减过"的基准值。
                await role.Data.ChangeAttr(false, new ChangeAttrData(ERoleExtraAttribute.Speed, "0"), ShufuSpeedKey);
                if (layer > 0)
                {
                    int baseSpeed = role.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.Speed);
                    int maxCut = Math.Max(0, baseSpeed - MinSpeedAfterShufu);   // 最多扣到 10 点
                    int cut = -ShufuSpeedPerLayer * layer;                     // 这一层数本该扣多少（正数）
                    if (cut > maxCut)
                    {
                        cut = maxCut;   // 2026-09-27 用户口径：束缚不会把速度压到 10 以下
                    }
                    if (cut > 0)
                    {
                        await role.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.Speed,
                            (-cut).ToString()), ShufuSpeedKey);
                    }
                }
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：同步束缚速度出错：" + e.Message); }
        }
        // ---- 先发：战斗开始时定"资格"，每轮先标记、再打击（2026-09-27 用户口径）----

        /// <summary>
        /// 有先发资格的人。**进战斗时判一次**，之后战斗中换装备不再重判 ——
        /// 用户口径："先发判定只在进入战斗时进行一次判定，战斗中改成单手装备就不能触发先发了"。
        /// </summary>
        private static readonly HashSet<BattleRole> FirstStrikeReady = new HashSet<BattleRole>();

        /// <summary>「目标锁定」（880052）：先发用的标记状态，本身无数值效果。</summary>
        internal const int TargetLockBuffId = 880052;

        /// <summary>进入战斗时判一次资格：副手装着荆棘、或者"视为双手"。</summary>
        internal static void MarkFirstStrikeReady(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !role.IsAlly) return;
                bool onMaster, onOffhand, asTwoHanded;
                GetSlot(role, out onMaster, out onOffhand, out asTwoHanded);
                bool ready = onOffhand || (asTwoHanded && onMaster);
                if (ready)
                {
                    FirstStrikeReady.Add(role);
                    AttackTargetPlugin.LogInfo("荆棘：「" + SecretTraits.NameOf(role) + "」本场战斗拥有先发资格");
                }
                else
                {
                    FirstStrikeReady.Remove(role);
                }
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：先发资格判定出错：" + e.Message); }
        }

        /// <summary>
        /// 战斗中也跟踪资格，但**只会取消、不会补发**（2026-09-27 用户口径）：
        /// 每次轮开始前看一眼，如果已经不满足装备条件（荆棘既不在副手、也不是"视为双手"了），
        /// 就把资格摘掉 —— 本场战斗之后都不会再有先发。
        /// 反过来，战斗中途才把荆棘换到副手，**不会**因此获得资格（资格只在进战斗那一刻授予）。
        /// </summary>
        internal static void VerifyFirstStrikeReady(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                if (!FirstStrikeReady.Contains(role)) return;   // 本来就没资格的不用管
                if (FirstStrikeWeapon(role) != null) return;    // 还满足条件，留着
                FirstStrikeReady.Remove(role);
                AttackTargetPlugin.LogInfo("荆棘：「" + SecretTraits.NameOf(role) +
                    "」不再满足先发条件 → 本场战斗失去先发资格（不会补发）");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：先发资格复核出错：" + e.Message); }
        }
        /// <summary>战斗开始/结束时清一遍资格。</summary>
        internal static void ClearFirstStrikeReady()
        {
            FirstStrikeReady.Clear();
        }

        /// <summary>行动轮开始前：有资格的人挑一个随机敌人挂【目标锁定】。</summary>
        internal static void MarkFirstStrikeTarget(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.IsDeath) return;
                if (!FirstStrikeReady.Contains(role)) return;
                if (FirstStrikeWeapon(role) == null) return;
                BattleRole target = PickRandomEnemy(role);
                if (target == null) return;
                MarkTarget(role, target);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：目标锁定出错：" + e.Message); }
        }

        /// <summary>回合开始：对带着【目标锁定】的那个敌人用武器打一次。</summary>
        internal static void ExecuteFirstStrike(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.IsDeath) return;
                if (!FirstStrikeReady.Contains(role)) return;
                MOD_Dynamic_Item weapon = FirstStrikeWeapon(role);
                if (weapon == null) return;
                BattleRole target = FindMarkedTarget();
                if (target == null) return;
                AttackTargetPlugin.LogInfo("荆棘：「" + SecretTraits.NameOf(role) + "」先发 → 锁定目标「" +
                    SecretTraits.NameOf(target) + "」");
                FirstStrike(role, target, weapon);
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：先发攻击出错：" + e.Message); }
        }

        /// <summary>先发用的武器：副手那把，或者"视为双手"时的主手。</summary>
        private static MOD_Dynamic_Item FirstStrikeWeapon(BattleRole role)
        {
            bool onMaster, onOffhand, asTwoHanded;
            GetSlot(role, out onMaster, out onOffhand, out asTwoHanded);
            if (onOffhand) return WeaponAt(role, 1);
            if (asTwoHanded && onMaster) return WeaponAt(role, 0);
            return null;
        }

        /// <summary>场上带着【目标锁定】的敌人（没死、没被擒抱）。</summary>
        internal static BattleRole FindMarkedTarget()
        {
            try
            {
                if (BattleHelper.FightContent == null || BattleHelper.FightContent.CurWaveEnemies == null) return null;
                List<BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    BattleNpcRole e = enemies[i];
                    if (e != null && !e.IsDeath && !e.IsGrabbed && e.GetBuff(TargetLockBuffId) != null) return e;
                }
                return null;
            }
            catch (Exception) { return null; }
        }

        private static async void MarkTarget(BattleRole self, BattleRole target)
        {
            try
            {
                await target.AddBuff(self, TargetLockBuffId);
                AttackTargetPlugin.LogInfo("荆棘：先发标记 →「" + SecretTraits.NameOf(target) + "」获得【目标锁定】");
            }
            catch (Exception e) { AttackTargetPlugin.LogError("荆棘：挂目标锁定出错：" + e.Message); }
        }

        /// <summary>解掉某个人身上的【目标锁定】（回合结束 / 打完 / 战斗结束时用）。</summary>
        internal static void ClearTargetLock(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null) return;
                if (role.GetBuff(TargetLockBuffId) == null) return;
                ClearTargetLockAsync(role);
            }
            catch (Exception) { }
        }

        private static async void ClearTargetLockAsync(BattleRole role)
        {
            try { await role.RemoveBuff(TargetLockBuffId); } catch (Exception) { }
        }

        /// <summary>把场上所有敌人的【目标锁定】清掉（回合结束 / 战斗结束）。</summary>
        internal static void ClearAllTargetLocks()
        {
            try
            {
                if (BattleHelper.FightContent == null || BattleHelper.FightContent.CurWaveEnemies == null) return;
                List<BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] != null) ClearTargetLock(enemies[i]);
                }
            }
            catch (Exception) { }
        }

        private static BattleRole PickRandomEnemy(BattleRole self)
        {
            try
            {
                List<BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                if (enemies == null)
                {
                    return null;
                }
                List<BattleRole> alive = new List<BattleRole>();
                for (int i = 0; i < enemies.Count; i++)
                {
                    BattleNpcRole e = enemies[i];
                    if (e != null && !e.IsDeath && !e.IsGrabbed)
                    {
                        alive.Add(e);
                    }
                }
                if (alive.Count == 0)
                {
                    return null;
                }
                int idx = BattleHelper.RandomRange(0, alive.Count);
                if (idx < 0 || idx >= alive.Count)
                {
                    idx = 0;
                }
                return alive[idx];
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 先发（副手每回合白送的那一下，2026-09-27 用户口径）：
        /// 只造成**武器基础伤害** —— 直接掷武器自己的伤害骰，
        /// 不吃任何加成（主手 +2、速度台阶、力量、花香……），也不吃目标的护甲 / 减伤。
        /// 命中给目标叠 1 层【荆棘】（挂层在 SetDamage 的接管里兜底）。
        /// </summary>
        private static async void FirstStrike(BattleRole source, BattleRole target, MOD_Dynamic_Item weapon)
        {
            try
            {
                DamageData baseDamage = weapon.WeaponDamageData;
                if (baseDamage == null)
                {
                    return;
                }
                DamageData damageData = baseDamage.Clone();
                DamageAdditionalData addData = new DamageAdditionalData
                {
                    SourceType = EDamageSourceType.Other,   // 先发：不是正常行动打出来的伤害
                    IsWeaponDamage = true,
                    Weapon = weapon,
                    IsDirectDamage = false,
                    MaxDamage = false,
                    IsCritical = false,
                    EnableChangeDamageValue = true,
                    DiceResult = EDiceResult.Success       // 用户口径：默认为普通成功
                };
                addData.AddEffectTypes = BattleHelper.GetBattleAttackEffectByDamageAttr(damageData);
                DamageSourceRecord record = new DamageSourceRecord
                {
                    Source = source,
                    SourceType = DamageSourceRecord.EDamageSourceType.Weapon,
                    Weapon = weapon
                };
                // 伤害仍然走游戏自己的 CalculationDamage，但 Prefix 里的 TryBaseDamageWithDefense
                // 会把"攻击者一侧的加成"全部摘掉，只留下武器基础伤害 + 目标那一侧的
                // 护甲 / 减伤 / 易伤等结算（2026-09-27 用户口径）。
                int damage = BattleHelper.CalculationDamage(damageData, source, target, addData);
                SecretFx.Puncture(target, source);
                await target.OnHit(source, damage, damageData, addData, record, null);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：先发攻击出错：" + e.Message);
            }
        }

        // =====================================================================
        // 7. 先发只掷武器基础伤害，目标侧的防御仍生效。
        // =====================================================================

        /// <summary>只算防御侧的内层原版计算时，暂停附加的私货规则。</summary>
        internal static bool InGuardedCalc;

        private static DamageAdditionalData CloneAdditional(DamageAdditionalData src)
        {
            DamageAdditionalData data = new DamageAdditionalData();
            if (src == null)
            {
                return data;
            }
            data.SourceType = src.SourceType;
            data.IsWeaponDamage = src.IsWeaponDamage;
            data.Weapon = src.Weapon;
            data.IsDirectDamage = src.IsDirectDamage;
            data.MaxHpDamage = src.MaxHpDamage;
            data.MaxDamage = src.MaxDamage;
            data.IsCritical = src.IsCritical;
            data.IsImmuneMax = src.IsImmuneMax;
            data.EnableChangeDamageValue = src.EnableChangeDamageValue;
            data.DiceResult = src.DiceResult;
            data.ExtraItemAddDamage = src.ExtraItemAddDamage;
            data.MagicId = src.MagicId;
            data.DamageRatio = src.DamageRatio;
            data.BonusDamage = src.BonusDamage;
            data.AddEffectTypes = src.AddEffectTypes != null
                ? new List<EBattleAttackAddEffectType>(src.AddEffectTypes)
                : null;
            return data;
        }

        private static bool IsBaseDamageOnly(DamageAdditionalData data)
        {
            return data != null && data.IsWeaponDamage && data.SourceType == EDamageSourceType.Other
                && IsJingJi(data.Weapon);
        }

        /// <summary>掷一次武器自己的基础伤害骰。</summary>
        internal static int RollWeaponBaseDamage(MOD_Dynamic_Item weapon)
        {
            try
            {
                DamageData data = weapon != null ? weapon.WeaponDamageData : null;
                if (data == null || string.IsNullOrEmpty(data.Value))
                {
                    return 0;
                }
                DiceValueData dice = new DiceValueData();
                dice.FreshDiceValue(data.Value);
                return Math.Max(0, dice.GetValue());
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 【只算防御侧】先发：
        /// 伤害 = 掷一次武器基础伤害，然后只让"目标那一侧"的效果参与结算。
        /// 做法：克隆一份干净的伤害数据（基础骰固定成这次掷出的值、攻击侧加值清零），
        /// 再临时把攻击者身上的"造成伤害增减 / 额外伤害项"摘掉，跑一次游戏自己的 CalculationDamage。
        /// 护甲、目标减伤、易伤、目标身上的 debuff 全在游戏那套里，照常生效。
        /// </summary>
        internal static bool TryBaseDamageWithDefense(DamageData damageData, BattleRole source, BattleRole target,
            DamageAdditionalData addData, ref int result)
        {
            try
            {
                if (InGuardedCalc || target == null || addData == null || !IsBaseDamageOnly(addData))
                {
                    return false;
                }

                DamageData cleanDamage = damageData != null
                    ? damageData.Clone()
                    : addData.Weapon.WeaponDamageData.Clone();
                cleanDamage.Value = RollWeaponBaseDamage(addData.Weapon).ToString();   // 固定成"武器基础伤害"这一次的掷值
                cleanDamage.DB_Bonus = 0f;                                            // 关掉"角色加值伤害"

                DamageAdditionalData cleanAdd = CloneAdditional(addData);
                cleanAdd.SourceType = addData.SourceType;
                cleanAdd.ExtraItemAddDamage = 0;   // 装备附加伤害（主手 +2 之类）不算
                cleanAdd.BonusDamage = null;
                cleanAdd.DamageRatio = 1f;
                cleanAdd.IsCritical = false;
                cleanAdd.MaxDamage = false;
                cleanAdd.MaxHpDamage = false;

                // 攻击者侧的"造成伤害增减 / 额外伤害项"临时摘掉（同步窗口，跑完立刻恢复）
                RoleData data = source != null ? source.Data : null;
                DamageChangePercentData backPercent = null;
                List<AdditionalDamageData> backExtra = null;
                if (data != null)
                {
                    backPercent = data.CauseDamageChangePercentData;
                    data.CauseDamageChangePercentData = new DamageChangePercentData();
                    if (data.ExtraDamageData != null)
                    {
                        backExtra = data.ExtraDamageData.AdditionalDamageDatas;
                        data.ExtraDamageData.AdditionalDamageDatas = new List<AdditionalDamageData>();
                    }
                }

                InGuardedCalc = true;
                try
                {
                    result = BattleHelper.CalculationDamage(cleanDamage, source, target, cleanAdd);
                }
                finally
                {
                    InGuardedCalc = false;
                    if (data != null)
                    {
                        data.CauseDamageChangePercentData = backPercent;
                        if (backExtra != null)
                        {
                            data.ExtraDamageData.AdditionalDamageDatas = backExtra;
                        }
                    }
                }

                AttackTargetPlugin.LogInfo("荆棘/孤影：只算防御侧 → 「" + SecretTraits.NameOf(target) + "」受到 " +
                    result + " 点（武器基础伤害 + 目标护甲/减伤）");
                return true;
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：只算防御侧出错：" + e.Message);
                return false;
            }
        }

        internal static bool IsActiveStrike(RoleHitData hitData)
        {
            DamageAdditionalData addData = hitData?.AddData;
            return addData != null && addData.IsWeaponDamage
                && addData.SourceType == EDamageSourceType.BattleSkill && IsJingJi(addData.Weapon);
        }

        internal static int PrepareDamage(BattleRole target, RoleHitData hitData)
        {
            DamageAdditionalData addData = hitData?.AddData;
            if (addData == null || !addData.IsWeaponDamage || !IsJingJi(addData.Weapon))
            {
                return 0;
            }

            if (addData.SourceType == EDamageSourceType.Other)
            {
                RollThornByDex(hitData.Attacker, target);
                return 0;
            }

            if (addData.SourceType == EDamageSourceType.StrickBack)
            {
                SecretFx.Puncture(target, hitData.Attacker);
                return 0;
            }

            if (!IsActiveStrike(hitData))
            {
                return 0;
            }

            EDiceResult dexResult = RollHiddenDexCheck(hitData.Attacker);
            SecretFx.PunctureBurst(target, hitData.Attacker);
            switch (dexResult)
            {
                case EDiceResult.GreatSuccess:
                    return StrikeCount + 1;
                case EDiceResult.DiffSuc:
                case EDiceResult.Success:
                    return StrikeCount;
                default:
                    return 0;
            }
        }
    }

    [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.SetDamage),
        new Type[] { typeof(uint), typeof(bool), typeof(bool), typeof(RoleHitData) })]
    internal static class Patch_JingJi_SetDamage
    {
        private static void Prefix(BattleRole __instance, RoleHitData hitData, out int __state)
        {
            __state = 0;
            try
            {
                __state = JingJi.PrepareDamage(__instance, hitData);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：处理命中特效或叠层检定出错：" + e.Message);
            }
        }

        private static void Postfix(BattleRole __instance, int __state, ref Task __result)
        {
            if (__state > 0)
            {
                __result = AddThornsAfterDamage(__result, __instance, __state);
            }
        }

        private static async Task AddThornsAfterDamage(Task damageTask, BattleRole target, int layers)
        {
            await damageTask;
            if (target.Data != null && !target.IsDeath)
            {
                await JingJi.AddThornAsync(target, layers);
            }
        }
    }

    /// <summary>只在原版 SetDamage 应用生命变化的调用点启用三段跳字。</summary>
    [HarmonyPatch]
    internal static class Patch_JingJi_DamageDisplay
    {
        [ThreadStatic]
        internal static HUDElement SplitDamageHud;

        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(BattleRole), nameof(BattleRole.SetDamage),
                new[] { typeof(uint), typeof(bool), typeof(bool), typeof(RoleHitData) });
            Type stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType;
            return AccessTools.Method(stateMachine, "MoveNext");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            MethodInfo changeAttr = AccessTools.Method(typeof(RoleData), nameof(RoleData.ChangeAttr),
                new[] { typeof(bool), typeof(ChangeAttrData), typeof(string), typeof(bool), typeof(bool), typeof(BattleRole) });
            FieldInfo hitData = AccessTools.Field(original.DeclaringType, "hitData");
            MethodInfo display = AccessTools.Method(typeof(Patch_JingJi_DamageDisplay), nameof(ChangeHp));

            var code = new List<CodeInstruction>(instructions);
            int call = code.FindIndex(instruction => instruction.Calls(changeAttr));
            if (hitData == null || call < 0 || code.FindLastIndex(instruction => instruction.Calls(changeAttr)) != call)
            {
                throw new InvalidOperationException("找不到唯一的 SetDamage 扣血调用点");
            }

            var loadState = new CodeInstruction(OpCodes.Ldarg_0);
            loadState.labels.AddRange(code[call].labels);
            code[call].labels.Clear();
            code[call].opcode = OpCodes.Call;
            code[call].operand = display;
            code.Insert(call, loadState);
            code.Insert(call + 1, new CodeInstruction(OpCodes.Ldfld, hitData));
            return code;
        }

        private static Task ChangeHp(RoleData role, bool isAdd, ChangeAttrData attr, string sourceKey,
            bool playAnim, bool showChangeTip, BattleRole attacker, RoleHitData hitData)
        {
            HUDElement previous = SplitDamageHud;
            // 原版扣血在第一次等待之前就调用 ShowHudNum，离开这段同步调用就恢复。
            SplitDamageHud = playAnim && JingJi.IsActiveStrike(hitData) ? role.Role?.Model?.Hud : null;
            try
            {
                return role.ChangeAttr(isAdd, attr, sourceKey, playAnim, showChangeTip, attacker);
            }
            finally
            {
                SplitDamageHud = previous;
            }
        }
    }

    [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.ShowHudNum))]
    internal static class Patch_JingJi_SplitHudDamage
    {
        private static readonly Action<HUDElement, UIHudNumElement> OnAnimationFinished =
            AccessTools.MethodDelegate<Action<HUDElement, UIHudNumElement>>(
                AccessTools.Method(typeof(HUDElement), "_UpdateHUDNumElementsOnAnimFinish"));

        private static bool Prefix(HUDElement __instance, int num, Transform modelPos, bool isHit,
            List<UIHudNumElement> ____hudNumElements)
        {
            if (!isHit || num <= 0 || __instance != Patch_JingJi_DamageDisplay.SplitDamageHud
                || !__instance.CanShowChangeTip)
            {
                return true;
            }

            Patch_JingJi_DamageDisplay.SplitDamageHud = null;
            int part = num / JingJi.StrikeCount;
            int remainder = num % JingJi.StrikeCount;
            var numbers = new List<UIHudNumElement>(JingJi.StrikeCount);
            try
            {
                float spacing = 0f;
                for (int i = 0; i < JingJi.StrikeCount; i++)
                {
                    UIHudNumElement element = __instance.Pool_HudNum.Alloc<UIHudNumElement>();
                    numbers.Add(element);
                    int value = part + (i < remainder ? 1 : 0);
                    element.SetElement(value, true, finished => OnAnimationFinished(__instance, finished));
                    if (value == 0)
                    {
                        element.Text_Value.text = "0";
                    }
                    if (i == 0)
                    {
                        spacing = Mathf.Max(24f, element.Text_Value.preferredWidth + 8f);
                    }
                    Vector3 offset = new Vector3((i - 1) * spacing, i == 1 ? 18f : 0f, 0f);
                    element.transform.localPosition += modelPos.right * __instance.HudNumDistance + offset;
                }
            }
            catch (Exception e)
            {
                foreach (UIHudNumElement element in numbers)
                {
                    if (element != null)
                    {
                        __instance.Pool_HudNum.Recycle(element.transform);
                    }
                }
                AttackTargetPlugin.LogError("荆棘：三段跳字准备失败：" + e.Message);
                return true;
            }

            ____hudNumElements.AddRange(numbers);
            foreach (UIHudNumElement element in numbers)
            {
                _ = element.PlayShowAnim();
            }
            AttackTargetPlugin.LogInfo("荆棘：扣血一次，总伤 " + num + "，同屏跳字 " +
                string.Join(" / ", numbers.ConvertAll(element => element.Text_Value.text)));
            return false;
        }
    }
}
