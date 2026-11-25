using System;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal sealed class StrikeResolution
    {
        // 切斩刀的层数加成已在分段掷骰后处理，原生武器回调只需处理其它攻击。
        internal sealed class SegmentHit : DamageAdditionalData { }

        private static readonly int[][] Dice = {
            new[] { 4, 4 }, new[] { 3, 4 }, new[] { 3, 4 }, new[] { 6, 6, 6 },
            new[] { 4, 4, 6 }, new[] { 4, 4, 6, 6 }, new[] { 0, 0, 0, 0, 0 }
        };

        private readonly BattleRole _actor;
        private BattleRole _target;
        private readonly BattleSkillData _skill;
        internal EDiceResult Result { get; set; }
        internal bool HitResolved { get; private set; }
        private int _segment;
        private bool _focus;

        internal StrikeResolution(BattleRole actor, BattleRole target, BattleSkillData skill, EDiceResult result)
        {
            _actor = actor;
            _target = target;
            _skill = skill;
            Result = result;
        }

        internal async Task Begin()
        {
            int breath = _skill.Id switch { 885126 => 4, 885127 => 2, 885128 or 885129 => 3, _ => 0 };
            if (breath > 0) await Add(_actor, StrikeProfiles.BreathId, ValennaHeart.SkillLayers(_actor, breath));
            if (_skill.Id != StrikeProfiles.ExecutionId) return;
            int san = _actor.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentSan);
            if (san < 2 || _actor.Data.GetRoleSanState() is ESanState.Weak or ESanState.Collapse) return;
            await _actor.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentSan, "-2"));
            await Add(_actor, StrikeProfiles.FocusId, 1);
            _focus = Layers(_actor, StrikeProfiles.FocusId) > 0;
        }

        internal async Task Finish()
        {
            if (!_focus) return;
            _focus = false;
            if (_actor.GetBuff(StrikeProfiles.FocusId) != null)
                await _actor.RemoveBuff(StrikeProfiles.FocusId, showChangeTip: false);
        }

        internal void SetTarget(BattleRole target) => _target = target;

        internal async Task Hit()
        {
            int index = _skill.Id == 885121 ? 0 : _skill.Id - 885123;
            int segment = _segment++;
            int sides = Dice[index][segment];
            bool execution = _skill.Id == StrikeProfiles.ExecutionId;
            if (execution) sides = Layers(_actor, StrikeProfiles.EyeChargeId) / 10 * 2;

            int ammo = _skill.Id switch {
                885124 or 885127 when segment < 2 => 1,
                885128 when segment < 3 => 1,
                885129 when segment == 4 => Math.Min(5, Layers(_actor, StrikeProfiles.AccelBulletId)),
                _ => 0
            };
            bool consumed = ammo > 0 && Layers(_actor, StrikeProfiles.AccelBulletId) >= ammo;
            bool fired = consumed && await ValennaKit.SpendShot(_actor, ammo);
            int amount = sides > 0 ? UnityEngine.Random.Range(1, sides + 1) : 0;
            if (execution) amount++;
            if (Layers(_actor, StrikeProfiles.BreathId) >= 6) amount++;
            int conditions = Layers(_target, StrikeProfiles.BurnId) + ValennaTremor.CountLayers(_target);
            if (_skill.Id == 885127) amount += Math.Min(1, conditions / 6);
            if (_skill.Id == 885126 || _skill.Id == 885128 || execution) amount += Math.Min(2, conditions / 6);
            if (execution && segment == 4 && _focus)
            {
                amount += 2;
                await Finish();
            }
            if (execution && _target.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentHp) * 2L <=
                _target.Data.GetRoleExtraAttrMaxValue(ERoleExtraAttribute.CurrentHp)) amount *= 2;
            if (fired) amount = (11 * amount + 5) / 10;
            amount = ValennaKit.BoostBlade(_actor, _target, amount);

            DamageData damage = _skill.Config.CustomDamage?.Clone();
            if (damage == null) return;
            // 武器身份供原生词缀和命中效果识别。基础伤害使用本段骰，DB 系数固定为零。
            damage.Value = amount.ToString();
            damage.DB_Bonus = 0f;
            var weapon = _skill.CurrentWeapon ?? _actor.Data.Weapons?.Find(item =>
                item?.Config?.Weapon?.AttackType == EAttackType.Melee);
            var additional = new SegmentHit
            {
                SourceType = EDamageSourceType.BattleSkill,
                Weapon = weapon,
                IsWeaponDamage = weapon != null,
                DiceResult = Result,
                EnableChangeDamageValue = true
            };
            var source = new DamageSourceRecord
            {
                Source = _actor,
                SourceType = DamageSourceRecord.EDamageSourceType.BattleSkill,
                BattleSkill = _skill
            };
            var log = new BattleSkillEffectLogData
            {
                LogType = EBattleLogType.BattleSkill,
                EffectType = EBattleLogEffectType.Damage
            };
            int points = BattleHelper.CalculationDamage(damage, _actor, _target, additional);
            await _target.OnHit(_actor, Mathf.Max(points, 0), damage, additional, source, log);
            HitResolved = true;
            if (!StrikeSequence.CanHit(_target)) return;
            if (fired) await ValennaKit.ApplyShotHit(_actor, _target);
            await ApplyEffects(segment, consumed, additional.IsCritical || Result == EDiceResult.GreatSuccess);
        }

        private async Task ApplyEffects(int segment, bool consumed, bool critical)
        {
            int burn = (_skill.Id, segment) switch {
                (885121, 0) => 1,
                (885124 or 885127, 0) when consumed => 1,
                (885126, 0) => 2,
                (885128, 0 or 1) => 1,
                (885129, 1 or 2) => 1,
                _ => 0
            };
            int tremor = (_skill.Id, segment) switch {
                (885124 or 885127 or 885126, 1) => 2,
                (885128, 0 or 1) => 2,
                (885125 or 885129, 0) => 1,
                _ => 0
            };
            if (burn > 0) await Add(_target, StrikeProfiles.BurnId, burn);
            if (tremor > 0)
            {
                BuffData status = Singleton<ResManager>.Instance.BuffFactory.GetData(StrikeProfiles.TremorId);
                if (status?.Config != null)
                {
                    status.CurLayer = ValennaHeart.SkillLayers(_actor, tremor);
                    await _target.AddBuff(_actor, status);
                }
            }
            if ((_skill.Id, segment) is (885125, 1) or (885126, 2) or (885128, 2) or (885129, 3) &&
                ValennaTremor.CountLayers(_target) > 0)
                await _target.AddBuff(_actor, StrikeProfiles.ScorchTremorId);

            int bursts = (_skill.Id, segment) switch {
                (885125, 1) or (885127, 2) => 1,
                (885126, 2) => 2,
                (885128, 3) => critical ? 4 : 2,
                (885129, 4) => critical ? 5 : 3,
                _ => 0
            };
            if (bursts > 0) await ValennaTremor.BurstBatch(_actor, _target, bursts);

            if (_skill.Id == 885121 && segment == 1)
                await TriggerBurn();
        }

        private Task Add(BattleRole role, int id, int count) =>
            ValennaTremor.AddLayers(_actor, role, id, count, true, null);

        private static int Layers(BattleRole role, int id)
        {
            BuffData buff = role.GetBuff(id);
            return buff == null || buff.DisableBuff ? 0 : Math.Max(0, buff.CurLayer);
        }

        private async Task TriggerBurn()
        {
            BuffData burn = _target.GetBuff(StrikeProfiles.BurnId);
            if (burn == null || Layers(_target, StrikeProfiles.BurnId) == 0) return;
            await burn.TriggerBuff(_target, EBuffTriggerType.RoundStart);
        }
    }
}
