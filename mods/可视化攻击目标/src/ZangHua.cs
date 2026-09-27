// 茉莉专属武器「葬花」（Item 880001）的插件逻辑。
//
// 数据侧（由 tools\make_zanghua_item.ps1 / make_zanghua_buffs.ps1 生成）：
//   Item 880001        葬花：1D8+2、单手、连击 2、充能上限 12、神话+不可出售、绑定角色
//   Buff 880040        剑痕本体（层数容器 + 图标 + 描述，最高 5 层）
//   Buff 880041~880045 剑痕的隐藏数值档位（护甲 -x / 闪避 -10x / 运动 -10x）
//
// 这里做的事：
//   1. 行动前敏捷检定：成功扣 2 充能、困难成功及以上扣 1 充能 → 本次攻击段数 +1
//      （同步掷骰，标记交给 GetSkillEffectCount 消费；骰子动画异步补播，不挡流程）
//   2. 击杀敌人 → 恢复 1 充能
//   3. 命中并造成伤害 → 目标 +1 层剑痕（每次伤害 1 层）
//   4. 用葬花攻击有剑痕的目标 → 每层 +10% 伤害
//   5. 回合结束：剑痕层数减半（向下取整）→ 对中状态者补等量【流血】；同步隐藏档位
//   6. 剑痕不可被驱散（拦 RemoveBuff）
//   7. 战斗开始时检查：没有葬花就发一把，并把初始充能设成 7（游戏那个字段同时管上限和初始）
//   8. 【2026-09-27】剑痕的"受到伤害 +15%/层"（易伤）：按层数同步到目标的
//      `BeDamageChangePercentData`（对所有伤害类型生效），位置在 SyncMarkBeDamage；
//      速度削减已去掉（减速归荆棘的【束缚】），闪避 -10、运动 -10 走数据档位。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using Game.Role.AI;
using Game.SkillData;
using GamePlayEvent;
using HarmonyLib;
using MOD;

namespace AttackTargetVisualizer
{
    internal static class ZangHua
    {
        internal const int WeaponItemId = 880001;
        internal const int MarkBuffId = 880040;      // 剑痕本体
        internal const int MarkTierBase = 880040;    // 880041~880045 = 按层数的隐藏档位
        internal const int MaxMark = 5;
        internal const int BleedBuffId = 103;        // 本体【流血】
        internal const int InitCharge = 7;

        /// <summary>充能低于这个值时，每回合开始用 1 点精神值换 1 点充能（2026-09-22 用户要求）。</summary>
        internal const int LowChargeThreshold = 5;

        /// <summary>装备在主手时的武器伤害加成（2026-09-23 用户要求）。</summary>
        internal const int MasterDamageBonus = 1;

        /// <summary>装备在副手时的护甲加成（2026-09-23 用户要求）。</summary>
        internal const int OffhandArmorBonus = 2;

        /// <summary>装备在副手时，受到的魔法伤害减免（2026-09-23 用户要求）。</summary>
        internal const int OffhandMagicReduce = 1;

        /// <summary>副手护甲加成的属性来源 key（加/摘都用它，保证成对）。</summary>
        private const string OffhandArmorKey = "zanghua_offhand_armor";

        /// <summary>
        /// 剑痕的"受到伤害 +15%/层"（2026-09-27 用户口径 —— 剑痕定位改成"破甲 + 易伤"）。
        /// 挂在目标的 `BeDamageChangePercentData` 上（不是属性），所以不受"属性不随层数缩放"的限制，
        /// 直接按当前层数写一个百分比即可。
        /// </summary>
        private const string MarkBeDamageKey = "zanghua_mark_be_damage";
        private const float MarkBeDamagePerLayer = 0.15f;

        /// <summary>所有伤害类型（"受到伤害 +x%"对全部类型生效；共享同一份只读列表）。</summary>
        private static readonly List<EDamageType> MarkBeDamageTypes =
            new List<EDamageType>((EDamageType[])Enum.GetValues(typeof(EDamageType)));

        /// <summary>检定的结果要不要给这次行动 +1 段（按角色记，GetSkillEffectCount 消费后清掉）。</summary>
        private static readonly Dictionary<BattleRole, bool> ExtraSegmentPending = new Dictionary<BattleRole, bool>();

        /// <summary>回合结束的减半只做一次（同一角色在极短时间内被重复触发时靠它去重）。</summary>
        private static readonly Dictionary<BattleRole, float> LastMarkTime = new Dictionary<BattleRole, float>();

        /// <summary>已经发出过葬花的角色（避免重复发）。</summary>
        private static readonly HashSet<BattleRole> WeaponGiven = new HashSet<BattleRole>();

        /// <summary>
        /// 正在做死亡清理的角色。
        ///
        /// 为什么单独记一笔：`ClearBuffAfterDeath` 跑的时候，角色的 LifeState **还没**写成 Death
        /// （那一步在 `SetLiftState` 后半段），所以 `unit.IsDeath` 还是 false ——
        /// 只看它会连死亡清理一起拦下来，剑痕永远摘不掉。
        /// </summary>
        private static readonly HashSet<BattleRole> DeathClearing = new HashSet<BattleRole>();

        internal static void MarkDeathClearing(BattleRole role)
        {
            if (role != null)
            {
                DeathClearing.Add(role);
            }
        }

        internal static bool IsDeathClearing(BattleRole role)
        {
            return role != null && DeathClearing.Contains(role);
        }

        /// <summary>战斗开始/结束时清一遍，别攒着（每场战斗的角色对象都是新的）。</summary>
        internal static void ClearDeathClearing()
        {
            DeathClearing.Clear();
        }

        // =====================================================================
        // 通用
        // =====================================================================

        internal static bool IsZangHua(MOD_Dynamic_Item item)
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
        /// 葬花现在装在哪：主手 / 副手 / 视为双手。
        /// "视为双手" = 另一个武器槽是空的（这把武器是身上唯一一把）。
        /// </summary>
        internal static void GetSlot(BattleRole role, out bool onMaster, out bool onOffhand, out bool asTwoHanded)
        {
            onMaster = false;
            onOffhand = false;
            asTwoHanded = false;
            MOD_Dynamic_Item main = WeaponAt(role, 0);
            MOD_Dynamic_Item off = WeaponAt(role, 1);
            if (IsZangHua(main))
            {
                onMaster = true;
            }
            if (IsZangHua(off))
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

        /// <summary>找这个人身上/背包里的葬花（武器栏优先）。</summary>
        internal static MOD_Dynamic_Item FindWeapon(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return null;
                }
                if (role.Data.Weapons != null)
                {
                    for (int i = 0; i < role.Data.Weapons.Count; i++)
                    {
                        if (IsZangHua(role.Data.Weapons[i]))
                        {
                            return role.Data.Weapons[i];
                        }
                    }
                }
                if (role.Data.BagItems != null)
                {
                    for (int i = 0; i < role.Data.BagItems.Count; i++)
                    {
                        if (IsZangHua(role.Data.BagItems[i]))
                        {
                            return role.Data.BagItems[i];
                        }
                    }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>剑痕层数（0 = 没中）。</summary>
        internal static int MarkLayer(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return 0;
                }
                BuffData buff = role.GetBuff(MarkBuffId);
                return buff != null ? buff.CurLayer : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // =====================================================================
        // 1. 行动前的敏捷检定
        // =====================================================================

        /// <summary>
        /// 玩家的攻击行动开始时（EBuffTriggerType.Attack）调用。
        /// 这里**必须同步出结果** —— 因为马上要用它决定"这次打几段"，
        /// 所以自己掷骰算结果，骰子动画稍后异步补播（只影响观感，不影响结算）。
        /// </summary>
        internal static void OnAttackDeclared(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !BattleHelper.IsInBattle)
                {
                    return;
                }
                MOD_Dynamic_Item weapon = FindWeapon(role);
                if (weapon == null || weapon.Config == null)
                {
                    return;   // 手上没有葬花就不管
                }
                int charge = weapon.ChargeCount;
                if (charge <= 0)
                {
                    return;   // 充能为 0 也能正常打，只是触发不了额外段数
                }

                // 目标值 = 角色的敏捷（走游戏自己的取值函数，吃 buff / 难度修正）
                int target = BattleHelper.GetDiceCheckValue(role, EExploreSkill.None, EHeroAttribute.DEX,
                    ERoleExtraAttribute.None, EExploreSkill.None, false);
                int roll = BattleHelper.RandomRange(1, 101);

                EDiceResult result;
                if (roll <= target / 5)
                {
                    result = EDiceResult.GreatSuccess;
                }
                else if (roll <= target / 2)
                {
                    result = EDiceResult.DiffSuc;
                }
                else if (roll <= target)
                {
                    result = EDiceResult.Success;
                }
                else
                {
                    result = roll >= 96 ? EDiceResult.GreatFail : EDiceResult.Fail;
                }

                int cost;
                if (result == EDiceResult.GreatSuccess || result == EDiceResult.DiffSuc)
                {
                    cost = 1;   // 困难成功及以上：只花 1 点
                }
                else if (result == EDiceResult.Success)
                {
                    cost = 2;
                }
                else
                {
                    cost = 0;
                }

                if (cost > 0 && charge >= cost)
                {
                    weapon.ChargeCount = charge - cost;
                    ExtraSegmentPending[role] = true;
                    AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」敏捷检定 " + result +
                        "（目标 " + target + "，骰 " + roll + "）→ 消耗 " + cost + " 充能，本次攻击段数 +1（剩 " +
                        weapon.ChargeCount + " 点）");
                }
                else
                {
                    AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」敏捷检定 " + result +
                        "（目标 " + target + "，骰 " + roll + "）→ 不触发额外段数" +
                        (cost > 0 ? "（充能不足：" + charge + " < " + cost + "）" : ""));
                }

                PlayCheckAnim(role, target, roll);   // 异步补个骰子演出
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：行动前检定出错：" + e);
            }
        }

        private static async void PlayCheckAnim(BattleRole role, int target, int roll)
        {
            try
            {
                DiceResultData dice = new DiceResultData();
                dice.Role = role;
                dice.CheckType = EDiceValueType.Attr;
                dice.CheckId = (int)EHeroAttribute.DEX;
                dice.OriginAttr = EHeroAttribute.DEX;
                dice.CheckNameKey = EHeroAttribute.DEX.GetLocalizationKeyData();
                dice.CheckValue = target;
                await PrefabSingleton<UIBattlePanel>.Instance.PlayDiceAnim(dice, false);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：骰子演出出错（不影响结算）：" + e.Message);
            }
        }

        /// <summary>GetSkillEffectCount 里调用：该不该给这次攻击 +1 段（只加一次）。</summary>
        internal static bool TryConsumeExtraSegment(BattleSkillData skill, bool isOffhand)
        {
            try
            {
                if (isOffhand || skill == null || !IsZangHua(skill.MasterHandWeapon))
                {
                    return false;
                }
                BattleRole actor = skill.Role;
                if (actor == null && BattleHelper.IsInBattle && BattleHelper.FightContent != null)
                {
                    actor = BattleHelper.FightContent.CurActionRole;
                }
                if (actor == null || !ExtraSegmentPending.ContainsKey(actor) || !ExtraSegmentPending[actor])
                {
                    return false;
                }
                ExtraSegmentPending[actor] = false;   // 一次行动只加一段
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        // 2. 击杀恢复充能
        // =====================================================================

        internal static void OnRoleDeath(BattleRole role, ELifeState state)
        {
            try
            {
                if (state != ELifeState.Death || !BattleHelper.IsInBattle || BattleHelper.FightContent == null)
                {
                    return;
                }
                BattleRole killer = BattleHelper.FightContent.CurActionRole;
                if (killer == null || killer == role || killer.IsAlly == role.IsAlly)
                {
                    return;   // 只认"打敌人的那一方"，自杀/队友互殴不算
                }
                MOD_Dynamic_Item weapon = FindWeapon(killer);
                if (weapon == null || weapon.Config == null)
                {
                    return;
                }
                int max = weapon.Config.ChargingCount;
                if (weapon.ChargeCount < max)
                {
                    weapon.ChargeCount = Math.Min(max, weapon.ChargeCount + 1);
                    AttackTargetPlugin.LogInfo("葬花：击杀「" + SecretTraits.NameOf(role) + "」→ 恢复 1 点充能（" +
                        weapon.ChargeCount + "/" + max + "）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：击杀回充能出错：" + e.Message);
            }
        }

        // =====================================================================
        // 2.5 副手加成（2026-09-23 用户要求）：装备在副手时护甲 +2、受到的魔法伤害 -1
        //     只在"确实装在副手"或"视为双手"时生效；属性用来源 key 追踪，加/摘成对。
        // =====================================================================

        /// <summary>
        /// 葬花：主手（含"视为双手"）时临时补上"力量型"标签 —— 游戏里「横扫」（战斗技能 5「顺劈」）
        /// 只有在主手武器带 EWeaponType.Power 时才可用；副手或不在手上就摘掉（长剑/刺剑不该能顺劈）。
        /// 2026-09-26 用户口径：数据里葬花不带力量型，靠这里按持握方式动态加。
        /// 改的是 Config 上的列表（同一份数据所有实例共用），我们这边只有茉莉用葬花，影响可控。
        /// </summary>
        internal static void SyncWeaponType(BattleRole role)
        {
            try
            {
                MOD_Dynamic_Item weapon = FindWeapon(role);
                if (weapon == null || weapon.Config == null || weapon.Config.Weapon == null)
                {
                    return;
                }
                List<EWeaponType> types = weapon.Config.Weapon.WeaponTypes;
                if (types == null)
                {
                    return;
                }
                bool onMaster, onOffhand, asTwoHanded;
                GetSlot(role, out onMaster, out onOffhand, out asTwoHanded);
                bool want = onMaster || asTwoHanded;
                bool has = types.Contains(EWeaponType.Power);
                if (want && !has)
                {
                    types.Add(EWeaponType.Power);
                    AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) +
                        "」持握在主手/双手 → 补上力量型标签（可用【横扫】）");
                }
                else if (!want && has)
                {
                    types.Remove(EWeaponType.Power);
                    AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) +
                        "」不在主手 → 摘掉力量型标签（不能【顺劈】）");
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：同步武器类型出错：" + e.Message);
            }
        }
        /// <summary>副手加成现在该不该生效。</summary>
        internal static bool ShouldHaveOffhandBonus(BattleRole role)
        {
            bool onMaster, onOffhand, asTwoHanded;
            GetSlot(role, out onMaster, out onOffhand, out asTwoHanded);
            return onOffhand || (asTwoHanded && onMaster);
        }

        /// <summary>属性来源表里有没有我们这条护甲加成。</summary>
        private static bool HasArmorSource(BattleRole role, string sourceKey)
        {
            try
            {
                if (role == null || role.Data == null || string.IsNullOrEmpty(sourceKey))
                {
                    return false;
                }
                VariableData<int> armor = role.Data.Armor;
                if (armor == null || armor._sources == null)
                {
                    return false;
                }
                for (int i = 0; i < armor._sources.Count; i++)
                {
                    if (armor._sources[i] != null && armor._sources[i].Key == sourceKey)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>副手加成要不要生效和当前身上的实际状态对齐（幂等，重复调用不会叠加）。</summary>
        internal static void SyncOffhandBonus(BattleRole role)
        {
            SyncWeaponType(role);   // 顺手对一下「力量型标签」（决定能不能【横扫】）
            try
            {
                if (role == null || role.Data == null || role.IsDeath)
                {
                    return;
                }
                bool want = ShouldHaveOffhandBonus(role);
                bool has = HasArmorSource(role, OffhandArmorKey);
                if (want == has)
                {
                    return;
                }
                ApplyOffhandBonus(role, want);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：副手加成对齐出错：" + e.Message);
            }
        }

        private static async void ApplyOffhandBonus(BattleRole role, bool add)
        {
            try
            {
                ChangeAttrData data = new ChangeAttrData();
                data.IsTrackSource = true;
                data.RoleExAttr = new RoleParamVariableData<ERoleExtraAttribute>
                {
                    Type = ERoleExtraAttribute.Armor,
                    Value = OffhandArmorBonus.ToString()
                };
                await role.Data.ChangeAttr(add, data, OffhandArmorKey, false, false);
                AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」副手加成" +
                    (add ? "生效（护甲 +" + OffhandArmorBonus + "、魔法伤害 -" + OffhandMagicReduce + "）" : "取消"));
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：副手加成出错：" + e.Message);
            }
        }

        /// <summary>受到的魔法伤害减 1（由 SecretTraits.OnDamageCalculated 调用）。</summary>
        internal static int ReduceMagicDamage(BattleRole role, int result)
        {
            try
            {
                if (role == null || result <= 0)
                {
                    return result;
                }
                if (!HasArmorSource(role, OffhandArmorKey))
                {
                    return result;
                }
                return Math.Max(0, result - OffhandMagicReduce);
            }
            catch (Exception)
            {
                return result;
            }
        }

        // =====================================================================
        // 3 & 4. 命中叠剑痕、按层数加伤害
        // =====================================================================

        internal static void OnDamage(BattleRole source, BattleRole target, DamageAdditionalData addData, ref int result)
        {
            try
            {
                if (result <= 0 || addData == null || !IsZangHua(addData.Weapon) || source == null || target == null)
                {
                    return;
                }

                // 2026-09-23 用户要求：装备在主手时此武器伤害 +1（"视为双手"时同样吃这条）
                bool onMaster, onOffhand, asTwoHanded;
                GetSlot(source, out onMaster, out onOffhand, out asTwoHanded);
                if (onMaster || asTwoHanded)
                {
                    result += MasterDamageBonus;
                }

                // 4. 每层剑痕 +10%（只对葬花；先算加成再叠层，避免本次命中吃到自己这层）
                int layer = MarkLayer(target);
                if (layer > 0)
                {
                    int bonus = (int)(result * 0.1f * layer);
                    if (bonus > 0)
                    {
                        result += bonus;
                        SecretTraits.LogDamageBonus(addData, layer, bonus);
                    }
                }

                // 3. 每次造成伤害给目标 +1 层剑痕（"视为双手"时施加的负面状态 +1 层 → 变成 2 层）
                if (!SecretTraits.MarkAppliedThisDamage(addData))
                {
                    AddMark(source, target, asTwoHanded ? 2 : 1);
                    // 原版斩击命中特效 + 刃器音效（2026-09-27 用户要求；和挂剑痕同一个"首次"判断，
                    // 所以整次攻击只播一次，多段不会连着响）
                    // 反击（StrickBack）除外 —— 孤影的反击流程自己会播一次，免得双响
                    if (addData.SourceType != EDamageSourceType.StrickBack)
                    {
                        SecretFx.Slash(source, target);
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：命中结算出错：" + e.Message);
            }
        }

        private static async void AddMark(BattleRole source, BattleRole target, int count)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    await target.AddBuff(source, MarkBuffId);
                }
                SyncMarkTier(target);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：上剑痕出错：" + e.Message);
            }
        }

        /// <summary>按当前层数挂/摘隐藏档位（护甲 / 闪避 / 运动）与"受到伤害 +15%/层"。</summary>
        internal static async void SyncMarkTier(BattleRole target)
        {
            try
            {
                if (target == null || target.Data == null)
                {
                    return;
                }
                int layer = MarkLayer(target);
                if (layer > MaxMark)
                {
                    layer = MaxMark;
                }
                // 2026-09-27 用户口径：剑痕改成"破甲 + 易伤"——
                // 每层：护甲-1、受到伤害+15%、闪避-10、运动-10。
                // 速度削减已去掉（减速归荆棘的【束缚】管，两把武器不再抢同一个活）。
                SyncMarkBeDamage(target, layer);
                if (target.IsDeath)
                {
                    return;   // 死亡时只要把增伤摘干净，不用再挂属性档位
                }
                for (int i = 1; i <= MaxMark; i++)
                {
                    bool shouldHave = i == layer;
                    bool has = target.GetBuff(MarkTierBase + i) != null;
                    if (shouldHave && !has)
                    {
                        await target.AddBuff(null, MarkTierBase + i);
                    }
                    else if (!shouldHave && has)
                    {
                        await target.RemoveBuff(MarkTierBase + i);
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：同步剑痕档位出错：" + e.Message);
            }
        }

        /// <summary>
        /// 按层数同步"受到伤害 +15%"（目标的 `BeDamageChangePercentData`，对所有伤害类型生效）。
        /// 层数为 0 就是把这条摘掉；加/摘都用同一个 SourceKey，保证不会叠加出错。
        /// </summary>
        internal static void SyncMarkBeDamage(BattleRole target, int layer)
        {
            try
            {
                if (target == null || target.Data == null || target.Data.BeDamageChangePercentData == null)
                {
                    return;
                }
                DamageChangePercentData data = target.Data.BeDamageChangePercentData;
                data.Remove(MarkBeDamageKey);
                if (layer > 0)
                {
                    data.Add(MarkBeDamageKey, false, MarkBeDamagePerLayer * layer, 0, 0, MarkBeDamageTypes);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：同步剑痕易伤出错：" + e.Message);
            }
        }

        /// <summary>把目标的"剑痕易伤"摘掉（战斗开始 / 结束 / 死亡时兜底清理）。</summary>
        internal static void ClearMarkBeDamage(BattleRole role)
        {
            SyncMarkBeDamage(role, 0);
        }

        // =====================================================================
        // 5. 回合结束：层数减半 + 补流血
        // =====================================================================

        internal static void OnRoundEndFor(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.IsDeath)
                {
                    return;
                }
                int layer = MarkLayer(role);
                if (layer <= 0)
                {
                    return;
                }
                // 一个回合里同一个角色只会被正经触发一次；这里再用时间戳兜一层，防止重复触发把层数减两次
                float now = UnityEngine.Time.unscaledTime;
                if (LastMarkTime.TryGetValue(role, out float last) && now - last < 0.5f)
                {
                    return;
                }
                LastMarkTime[role] = now;

                int half = layer / 2;                 // 向下取整
                int lost = layer - half;              // 减少的层数 = 补的流血层数
                AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」回合结束，剑痕 " + layer +
                    " → " + half + " 层，并补 " + lost + " 层流血");
                EndRoundMark(role, half, lost);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：回合结束结算出错：" + e.Message);
            }
        }

        private static async void EndRoundMark(BattleRole role, int newLayer, int bleed)
        {
            try
            {
                BuffData buff = role.GetBuff(MarkBuffId);
                if (buff != null)
                {
                    if (newLayer <= 0)
                    {
                        await role.RemoveBuff(MarkBuffId);
                    }
                    else
                    {
                        // 坑：ChangeLayer 是累加（CurLayer += layer），不是"设成 newLayer"。
                        // 传 newLayer 的话层数会变成 cur + newLayer（5 层 → 7 层），
                        // 表现就是"回合结束不减反增"。这里传差值（负数）才是真正的减半。
                        await buff.ChangeLayer(role, newLayer - buff.CurLayer);
                    }
                }
                for (int i = 0; i < bleed; i++)
                {
                    await role.AddBuff(null, BleedBuffId);
                }
                SyncMarkTier(role);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：层数减半/补流血出错：" + e.Message);
            }
        }

        // =====================================================================
        // 8. 每回合开始：充能不足时用精神值换充能（2026-09-22 用户要求）
        //    条件：充能 < 5（LowChargeThreshold）且还没满
        //    代价：1 点精神值（SAN 只剩 1 时不再换，免得把自己榨进衰弱）
        // =====================================================================

        internal static void OnRoundStartFor(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || !role.IsAlly)
                {
                    return;
                }
                if (!BattleHelper.IsInBattle)
                {
                    return;
                }
                MOD_Dynamic_Item weapon = FindWeapon(role);
                if (weapon == null || weapon.Config == null)
                {
                    return;   // 手上没有葬花就不管
                }
                int max = weapon.Config.ChargingCount;
                if (weapon.ChargeCount >= LowChargeThreshold || weapon.ChargeCount >= max)
                {
                    return;   // 充能够用、或者已经满了
                }
                int san = role.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentSan);
                if (san <= 1)
                {
                    return;   // 精神值见底就别榨了
                }
                RechargeBySan(role, weapon, max);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：回合开始回充能出错：" + e.Message);
            }
        }

        private static async void RechargeBySan(BattleRole role, MOD_Dynamic_Item weapon, int max)
        {
            try
            {
                await role.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentSan, "-1"), "", false, false);
                weapon.ChargeCount = Math.Min(max, weapon.ChargeCount + 1);
                AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」充能不足 5 点，消耗 1 点精神值回 1 点充能（" +
                    weapon.ChargeCount + "/" + max + "）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：回充能出错：" + e.Message);
            }
        }

        // =====================================================================
        // 7. 战斗开始：发武器 + 设初始充能
        // =====================================================================

        /// <summary>进副本时给队里该有葬花的人都检查一遍（主角队 + 后备队）。</summary>
        internal static void EnsureWeaponForTeam()
        {
            try
            {
                List<BattleRole> team = BattleHelper.GetHeroTeamRoleList();
                if (team != null)
                {
                    for (int i = 0; i < team.Count; i++)
                    {
                        EnsureWeapon(team[i]);
                    }
                }
                List<BattleRole> backup = BattleHelper.GetHeroBackupTeamRoleList();
                if (backup != null)
                {
                    for (int i = 0; i < backup.Count; i++)
                    {
                        EnsureWeapon(backup[i]);
                    }
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：进副本发武器出错：" + e.Message);
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
                MOD_Dynamic_Item weapon = FindWeapon(role);
                if (weapon == null)
                {
                    if (WeaponGiven.Contains(role))
                    {
                        return;
                    }
                    WeaponGiven.Add(role);
                    GiveWeapon(role);
                    return;
                }
                // 已经拿到了：只在新拿到（充能为 0 还是初始值）的时候补初始充能
                if (weapon.ChargeCount <= 0 && !WeaponGiven.Contains(role))
                {
                    WeaponGiven.Add(role);
                    weapon.ChargeCount = Math.Min(weapon.Config.ChargingCount, InitCharge);
                    AttackTargetPlugin.LogInfo("葬花：把「" + SecretTraits.NameOf(role) + "」的葬花充能设成 " + weapon.ChargeCount);
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：发武器出错：" + e.Message);
            }
        }

        private static async void GiveWeapon(BattleRole role)
        {
            try
            {
                await role.Data.AddItem(WeaponItemId);
                MOD_Dynamic_Item weapon = FindWeapon(role);
                if (weapon != null && weapon.Config != null)
                {
                    weapon.ChargeCount = Math.Min(weapon.Config.ChargingCount, InitCharge);
                }
                AttackTargetPlugin.LogInfo("葬花：「" + SecretTraits.NameOf(role) + "」拿到葬花（初始充能 " + InitCharge + "）");
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：发武器出错：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 死亡清理开始的标记。挂在 `ClearBuffAfterDeath` 上，只为在"那一刻"记一笔——
    /// 那时角色的 LifeState 还没写成 Death，`unit.IsDeath` 还是 false（踩过，见下面的注释）。
    /// </summary>
    [HarmonyPatch(typeof(BuffHelper), "ClearBuffAfterDeath")]
    internal static class Patch_ZangHua_DeathClearMark
    {
        private static void Prefix(BattleRole unit)
        {
            ZangHua.MarkDeathClearing(unit);
        }
    }

    /// <summary>
    /// 葬花：剑痕不可驱散 —— 拦住游戏/怪物对它的移除请求。
    /// 注意 RemoveBuff(int id, ...) 是 BuffHelper 里的**扩展方法**（静态），所以要挂在 BuffHelper 上；
    /// 死亡清理要放行，不然尸体会一直挂着剑痕。
    ///
    /// ⚠ 2026-09-22 修掉一个要命的坑：这个方法的返回值是 `Task`，Prefix 返回 false 跳过它时
    /// **必须自己塞一个已完成的 Task**（`__result`），否则调用方 `await unit.RemoveBuff(...)`
    /// 会对着 null 抛 NullReferenceException —— `ClearBuffAfterDeath` 就是这样被打断的，
    /// 表现是"敌人 HP 归零却死不了、战斗永远结束不了"（Player.log 里一串
    /// `at BuffHelper.ClearBuffAfterDeath` 的 NRE 就是它）。
    /// 同理：那一刻 `unit.IsDeath` 还是 false，所以要靠 `Patch_ZangHua_DeathClearMark` 记的标记放行。
    /// </summary>
    [HarmonyPatch(typeof(BuffHelper), "RemoveBuff",
        new Type[] { typeof(BattleRole), typeof(int), typeof(bool), typeof(BuffData) })]
    internal static class Patch_ZangHua_MarkCantBeRemoved
    {
        /// <summary>跳过原方法时要交出去的"已完成任务"，千万别留 null。</summary>
        private static readonly Task CompletedTask = Task.FromResult<object>(null);

        private static bool Prefix(BattleRole unit, int id, ref Task __result)
        {
            try
            {
                if (id != ZangHua.MarkBuffId)
                {
                    return true;                   // 不是剑痕，放行
                }
                if (unit == null || unit.IsDeath || ZangHua.IsDeathClearing(unit))
                {
                    return true;                   // 死亡清理放行
                }
                __result = CompletedTask;          // 给调用方一个能 await 的任务
                return false;                      // 剑痕：活着的时候谁都不许摘
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
