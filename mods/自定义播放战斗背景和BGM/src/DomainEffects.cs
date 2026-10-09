// 领域效果的运行时逻辑（2026-10-07 加）。
//
// 规则（用户口径）：
//   · 加成/减免类（伤害增加/减免%）→ 持续：展开时挂到目标身上，领域被移除时立刻消失；
//   · 造成伤害类 → 展开时结算一次（借 buff 的 Stable 触发走游戏自己的伤害流程）；
//   · 资源类（扣/回 魔法值、精神值）→ 展开时直接结算一次；
//   · 施加 buff/debuff → 每轮开始时施加一次（层数 = 配置值）；
//   · 支付代价 → 每轮开始 + 领域成功展开时，各结算一次（扣展开者的 生命/精神/魔法）；
//   · 同一方同时只有一个领域：新领域出现时旧领域立刻移除（常驻效果与领域自身状态一起消失，
//     施加出去的其他 buff 不管，让它们自己到期）。
//
// 容错：任何一个环节抛异常 → 把这份配置的"效果部分"标记为失效（背景/BGM 不受影响），日志留痕。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Game;
using MOD;

namespace CustomBattleBg
{
    internal static class DomainEffects
    {
        private static bool _active;
        private static DomainProfile _current;
        private static BattleRole _owner;
        private static bool _broken;
        private static readonly List<int> _trackedBuffs = new List<int>();   // 领域挂出去、不会随回合衰减的状态

        internal static bool HasActiveDomain
        {
            get { return _active && _current != null; }
        }

        internal static DomainProfile Current
        {
            get { return _current; }
        }

        internal static void ApplyOnOpen(BattleRole caster, DomainProfile p, string reason)
        {
            ApplyOnOpenAsync(caster, p, reason);
        }

        private static async void ApplyOnOpenAsync(BattleRole caster, DomainProfile p, string reason)
        {
            try
            {
                if (caster == null || p == null)
                {
                    return;
                }
                await RemoveCurrentAsync("新领域展开，旧领域被移除");

                _active = true;
                _current = p;
                _owner = caster;
                _broken = false;

                int statusId = DomainBuffBuilder.EnsureBuff(p, DomainBuffBuilder.KindStatus);
                if (statusId > 0)
                {
                    await caster.AddBuff(caster, statusId);
                }

                // 展开时只处理"不会随回合衰减"的手选 buff（挂一次，领域结束时收回）；
                // 预设效果、资源结算、会衰减的 buff 都留到每轮开始（见 OnRoundStartAsync）。
                await ApplyPersistentGameBuffsAsync(p, caster, reason);
                await PayCostAsync(p, caster, reason);
            }
            catch (Exception e)
            {
                BreakEffects("领域展开时出错：" + e);
            }
        }

        /// <summary>每轮开始（每个角色都会收到一次这个触发，只认当前领域的展开者）。</summary>
        internal static void OnRoundStart(BattleRole role, string reason)
        {
            if (_owner == null || role == null || role != _owner)
            {
                return;
            }
            OnRoundStartAsync(reason);
        }

        private static async void OnRoundStartAsync(string reason)
        {
            try
            {
                if (!_active || _current == null || _owner == null || _broken)
                {
                    return;
                }
                await PayCostAsync(_current, _owner, reason);
                await ApplyRoundEffectsAsync(_current, _owner, reason);
            }
            catch (Exception e)
            {
                BreakEffects("每轮开始时出错：" + e);
            }
        }

        internal static void RemoveCurrent(string reason)
        {
            _ = RemoveCurrentAsync(reason);
        }

        private static async Task RemoveCurrentAsync(string reason)
        {
            DomainProfile p = _current;
            BattleRole owner = _owner;
            _active = false;
            _current = null;
            _owner = null;
            _broken = false;
            if (p == null)
            {
                _trackedBuffs.Clear();
                return;
            }
            try
            {
                if (p.StatusBuffId > 0 && owner != null)
                {
                    await SafeRemoveBuff(owner, p.StatusBuffId);
                }
                if (p.AllyEffectBuffId > 0 || p.EnemyEffectBuffId > 0)
                {
                    List<BattleRole> all = GetAllBattleRoles();
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (p.AllyEffectBuffId > 0)
                        {
                            await SafeRemoveBuff(all[i], p.AllyEffectBuffId);
                        }
                        if (p.EnemyEffectBuffId > 0)
                        {
                            await SafeRemoveBuff(all[i], p.EnemyEffectBuffId);
                        }
                    }
                }
                // 领域挂出去的"不会随回合衰减"的状态：跟着领域一起收回（战斗结束/被别的领域顶掉）
                if (_trackedBuffs.Count > 0)
                {
                    List<BattleRole> all = GetAllBattleRoles();
                    for (int i = 0; i < all.Count; i++)
                    {
                        for (int k = 0; k < _trackedBuffs.Count; k++)
                        {
                            await SafeRemoveBuff(all[i], _trackedBuffs[k]);
                        }
                    }
                    _trackedBuffs.Clear();
                }
                CustomBattleBgPlugin.LogInfo("领域已移除（" + reason + "）：" + p.DisplayName);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("移除领域效果时出错：" + e.Message);
            }
        }

        // ---------------- 效果应用 ----------------

        private static async Task ApplyImmediateAsync(DomainProfile p, BattleRole target, BattleRole caster,
            bool isAlly, string reason)
        {
            if (target == null)
            {
                return;
            }
            int type = isAlly ? p.AllyEffectType : p.EnemyEffectType;
            int value = isAlly ? p.AllyEffectValue : p.EnemyEffectValue;
            if (type == 0)
            {
                return;
            }
            if (DomainEffectPreset.IsBuff(type))
            {
                return;   // buff 类留给"每轮开始"统一施加
            }
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamage:
                case DomainEffectPreset.MagicDamage:
                case DomainEffectPreset.PhysicalDamageBonus:
                case DomainEffectPreset.PhysicalDamageReduce:
                case DomainEffectPreset.MagicDamageBonus:
                case DomainEffectPreset.MagicDamageReduce:
                case DomainEffectPreset.PhysicalDamageDown:
                case DomainEffectPreset.PhysicalVulnerable:
                case DomainEffectPreset.MagicDamageDown:
                case DomainEffectPreset.MagicVulnerable:
                    {
                        int effId = DomainBuffBuilder.EnsureBuff(p,
                            isAlly ? DomainBuffBuilder.KindAlly : DomainBuffBuilder.KindEnemy);
                        if (effId > 0)
                        {
                            await target.AddBuff(caster, effId);
                        }
                        break;
                    }
                case DomainEffectPreset.MpDrain:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentMp, -Math.Abs(value));
                    break;
                case DomainEffectPreset.MpRestore:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentMp, Math.Abs(value));
                    break;
                case DomainEffectPreset.SanDrain:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentSan, -Math.Abs(value));
                    break;
                case DomainEffectPreset.SanRestore:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentSan, Math.Abs(value));
                    break;
            }
        }

        private static async Task AddBuffLayers(BattleRole caster, BattleRole target, int buffId, int layers)
        {
            try
            {
                if (target == null || buffId <= 0 || layers <= 0)
                {
                    return;
                }
                BuffData data = null;
                try
                {
                    data = Singleton<ResManager>.Instance.BuffFactory.GetData(buffId);
                }
                catch (Exception)
                {
                }
                if (data == null)
                {
                    CustomBattleBgPlugin.LogError("要施加的状态不存在，跳过：" + buffId);
                    return;
                }
                await target.AddBuff(caster, buffId);
                if (layers > 1)
                {
                    BuffData buff = target.GetBuff(buffId);
                    if (buff != null)
                    {
                        await buff.ChangeLayer(target, layers - 1, true, false);
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("施加状态层数失败（" + buffId + "）：" + e.Message);
            }
        }

        // ---------------- 三种情况的分流 ----------------
        //
        // 情况1：扣/回 生命·精神·魔法 → 每轮开始结算一次
        // 情况2：会随回合衰减的手选 buff + 预设增减益/伤害（挂上后回合结束自动解除）→ 每轮开始施加
        // 情况3：不会随回合衰减的手选 buff → 展开时挂一次，并追踪，领域结束/被顶掉时收回

        /// <summary>展开时：挂"不会随回合衰减"的手选 buff（情况3）。</summary>
        private static async Task ApplyPersistentGameBuffsAsync(DomainProfile p, BattleRole caster, string reason)
        {
            if (p.AllyTarget != 0 && DomainEffectPreset.IsBuff(p.AllyEffectType))
            {
                int buffId = DomainEffectPreset.BuffIdOf(p.AllyEffectType);
                if (!DomainBuffBuilder.IsDecayingBuff(buffId))
                {
                    List<BattleRole> allies = GetAllyTargets(caster, p.AllyTarget);
                    for (int i = 0; i < allies.Count; i++)
                    {
                        await AddPersistentBuff(caster, allies[i], buffId, p.AllyEffectValue);
                    }
                }
            }
            if (p.EnemyTarget && DomainEffectPreset.IsBuff(p.EnemyEffectType))
            {
                int buffId = DomainEffectPreset.BuffIdOf(p.EnemyEffectType);
                if (!DomainBuffBuilder.IsDecayingBuff(buffId))
                {
                    List<BattleRole> enemies = GetEnemyTargets();
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        await AddPersistentBuff(caster, enemies[i], buffId, p.EnemyEffectValue);
                    }
                }
            }
        }

        private static async Task AddPersistentBuff(BattleRole caster, BattleRole target, int buffId, int layers)
        {
            await AddBuffLayers(caster, target, buffId, layers);
            if (!_trackedBuffs.Contains(buffId))
            {
                _trackedBuffs.Add(buffId);
                CustomBattleBgPlugin.LogInfo("领域挂上了不会衰减的状态，开始追踪：" + buffId);
            }
        }

        /// <summary>每轮开始：预设效果 + 资源结算 + 会衰减的手选 buff（情况1、2）。</summary>
        private static async Task ApplyRoundEffectsAsync(DomainProfile p, BattleRole caster, string reason)
        {
            if (p == null || caster == null)
            {
                return;
            }
            if (p.AllyTarget != 0)
            {
                List<BattleRole> allies = GetAllyTargets(caster, p.AllyTarget);
                for (int i = 0; i < allies.Count; i++)
                {
                    await ApplyRoundEffectTo(p, allies[i], caster, true);
                }
            }
            if (p.EnemyTarget)
            {
                List<BattleRole> enemies = GetEnemyTargets();
                for (int i = 0; i < enemies.Count; i++)
                {
                    await ApplyRoundEffectTo(p, enemies[i], caster, false);
                }
            }
        }

        private static async Task ApplyRoundEffectTo(DomainProfile p, BattleRole target, BattleRole caster, bool isAlly)
        {
            if (target == null)
            {
                return;
            }
            int type = isAlly ? p.AllyEffectType : p.EnemyEffectType;
            int value = isAlly ? p.AllyEffectValue : p.EnemyEffectValue;
            if (type == 0)
            {
                return;
            }
            if (DomainEffectPreset.IsBuff(type))
            {
                int buffId = DomainEffectPreset.BuffIdOf(type);
                // 只会衰减的那种每轮补（不会衰减的在展开时挂过了）
                if (DomainBuffBuilder.IsDecayingBuff(buffId))
                {
                    await AddBuffLayers(caster, target, buffId, value);
                }
                return;
            }
            switch (type)
            {
                case DomainEffectPreset.PhysicalDamage:
                case DomainEffectPreset.MagicDamage:
                case DomainEffectPreset.PhysicalDamageBonus:
                case DomainEffectPreset.PhysicalDamageReduce:
                case DomainEffectPreset.MagicDamageBonus:
                case DomainEffectPreset.MagicDamageReduce:
                    {
                        // 每轮挂一份"领域效果"状态：加成在挂上时生效，回合结束自己解除（巴士节奏）
                        int effId = DomainBuffBuilder.EnsureBuff(p,
                            isAlly ? DomainBuffBuilder.KindAlly : DomainBuffBuilder.KindEnemy);
                        if (effId > 0)
                        {
                            await target.AddBuff(caster, effId);
                            // 玩家填的就是层数（每层 10%），图标角标直接显示。
                            // 注意 ChangeLayer 内部是 CurLayer += layer（永远"加"），
                            // 而 AddBuff 已经给了 1 层，所以要按"差额"调，不能直接传目标层数。
                            int layers = Math.Max(1, Math.Abs(value));
                            BuffData buff = target.GetBuff(effId);
                            if (buff != null)
                            {
                                int delta = layers - buff.CurLayer;
                                if (delta != 0)
                                {
                                    await buff.ChangeLayer(target, delta, false, false);
                                }
                            }
                        }
                        break;
                    }
                case DomainEffectPreset.MpDrain:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentMp, -Math.Abs(value));
                    break;
                case DomainEffectPreset.MpRestore:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentMp, Math.Abs(value));
                    break;
                case DomainEffectPreset.SanDrain:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentSan, -Math.Abs(value));
                    break;
                case DomainEffectPreset.SanRestore:
                    await ChangeAttrAsync(target, ERoleExtraAttribute.CurrentSan, Math.Abs(value));
                    break;
            }
        }

        private static async Task ChangeAttrAsync(BattleRole target, ERoleExtraAttribute attr, int delta)
        {
            try
            {
                if (target == null || target.Data == null || delta == 0)
                {
                    return;
                }
                ChangeAttrData data = new ChangeAttrData();
                data.IsTrackSource = true;
                data.RoleExAttr = new RoleParamVariableData<ERoleExtraAttribute>();
                data.RoleExAttr.Type = attr;
                data.RoleExAttr.Value = delta.ToString(CultureInfo.InvariantCulture);
                await target.Data.ChangeAttr(true, data, "", false, false);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("改变资源数值失败：" + e.Message);
            }
        }

        private static async Task PayCostAsync(DomainProfile p, BattleRole owner, string reason)
        {
            try
            {
                if (p == null || owner == null || p.CostType == 0 || p.CostValue <= 0)
                {
                    return;
                }
                ERoleExtraAttribute attr;
                switch (p.CostType)
                {
                    case 1: attr = ERoleExtraAttribute.CurrentHp; break;
                    case 2: attr = ERoleExtraAttribute.CurrentSan; break;
                    case 3: attr = ERoleExtraAttribute.CurrentMp; break;
                    default: return;
                }
                await ChangeAttrAsync(owner, attr, -Math.Abs(p.CostValue));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("结算领域代价失败：" + e.Message);
            }
        }

        private static async Task SafeRemoveBuff(BattleRole role, int buffId)
        {
            try
            {
                if (role != null && buffId > 0)
                {
                    await role.RemoveBuff(buffId);
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------- 目标列表 ----------------

        private static List<BattleRole> GetAllyTargets(BattleRole caster, int allyTarget)
        {
            List<BattleRole> result = new List<BattleRole>();
            if (allyTarget == 1)
            {
                result.Add(caster);
                return result;
            }
            try
            {
                List<BattleRole> allies = BattleHelper.FightContent.Allies;
                for (int i = 0; i < allies.Count; i++)
                {
                    if (allies[i] != null && !allies[i].IsDeath)
                    {
                        result.Add(allies[i]);
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("取友方列表失败：" + e.Message);
            }
            return result;
        }

        private static List<BattleRole> GetEnemyTargets()
        {
            List<BattleRole> result = new List<BattleRole>();
            try
            {
                List<BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] != null && !enemies[i].IsDeath)
                    {
                        result.Add(enemies[i]);
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("取敌方列表失败：" + e.Message);
            }
            return result;
        }

        private static List<BattleRole> GetAllBattleRoles()
        {
            List<BattleRole> result = new List<BattleRole>();
            try
            {
                List<BattleRole> allies = BattleHelper.FightContent.Allies;
                for (int i = 0; i < allies.Count; i++)
                {
                    if (allies[i] != null)
                    {
                        result.Add(allies[i]);
                    }
                }
                List<BattleNpcRole> enemies = BattleHelper.FightContent.CurWaveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] != null)
                    {
                        result.Add(enemies[i]);
                    }
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private static void BreakEffects(string message)
        {
            _broken = true;
            CustomBattleBgPlugin.LogError("【领域效果已失效，本次战斗不再尝试】" + message +
                "（背景与BGM不受影响）");
        }
    }
}
