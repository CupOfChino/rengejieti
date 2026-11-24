using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class ValennaHunt
    {
        private static readonly ConditionalWeakTable<BattleFightContent, Hunt> Hunts =
            new ConditionalWeakTable<BattleFightContent, Hunt>();

        private sealed class Hunt
        {
            internal BattleRole Owner;
            internal BattleRole Prey;
            internal int RenewInRound;
            internal int Remainder;
        }

        private static Hunt Current
        {
            get
            {
                BattleFightContent fight = BattleHelper.FightContent;
                return BattleHelper.IsInBattle && fight != null && Hunts.TryGetValue(fight, out Hunt hunt)
                    ? hunt : null;
            }
        }

        internal static bool IsMarkedBy(BattleRole owner, BattleRole target)
        {
            Hunt hunt = Current;
            return hunt != null && hunt.Owner == owner && hunt.Prey == target && HasMark(target);
        }

        internal static bool CanExecute(BattleRole owner, BattleRole target) =>
            IsMarkedBy(owner, target) && target.CheckCanBeSelectedAsTarget(owner);

        private static bool HasMark(BattleRole prey) =>
            StrikeSequence.CanHit(prey) && !prey.IsRemoved && prey.GetBuff(StrikeProfiles.HuntingPreyId) != null;

        internal static BattleRole TargetFor(BattleRole owner)
        {
            BattleRole target = Current?.Prey;
            return CanExecute(owner, target) ? target : null;
        }

        internal static async Task BattleStart(BattleRole owner)
        {
            if (!StrikeSequence.CanHit(owner) || !ValennaPlugin.HasPalermo(owner) ||
                owner.GetTraitData(StrikeProfiles.HuntTraitId)?.Config == null ||
                !BattleHelper.IsInBattle || BattleHelper.FightContent == null) return;

            Hunt hunt = Hunts.GetValue(BattleHelper.FightContent, _ => new Hunt());
            if (hunt.Owner != null) return;
            hunt.Owner = owner;
            await Assign(hunt);
        }

        internal static async Task BeforeRound(BattleRole owner)
        {
            Hunt hunt = Current;
            if (hunt == null || hunt.Owner != owner || hunt.RenewInRound != BattleHelper.FightContent.Round) return;
            hunt.RenewInRound = 0;
            if (!HasMark(hunt.Prey) && StrikeSequence.CanHit(owner))
                await Assign(hunt);
        }

        internal static void BeginAttack(BattleRole owner)
        {
            Hunt hunt = Current;
            if (hunt != null && hunt.Owner == owner) hunt.Remainder = 0;
        }

        internal static int BoostDamage(BattleRole owner, BattleRole target, int damage)
        {
            Hunt hunt = Current;
            if (damage <= 0 || hunt?.Owner != owner || hunt.Prey != target || !HasMark(target)) return damage;

            long scaled = 20L * damage + hunt.Remainder;
            hunt.Remainder = (int)(scaled % 100);
            return (int)Math.Min(int.MaxValue, damage + scaled / 100);
        }

        internal static async Task PreyDefeated(BattleRole owner, BattleRole victim, bool execution)
        {
            Hunt hunt = Current;
            if (hunt?.Owner != owner || hunt.Prey != victim || !victim.IsDeath) return;

            hunt.Prey = null;
            await victim.RemoveBuff(StrikeProfiles.HuntingPreyId, showChangeTip: false);
            if (execution) hunt.RenewInRound = BattleHelper.FightContent.Round + 1;
            await TerrifyEnemies(owner);
        }

        internal static async Task FinishExecution(BattleRole owner, BattleRole target)
        {
            Hunt hunt = Current;
            if (hunt?.Owner != owner || hunt.Prey != target) return;
            if (target.IsDeath)
            {
                await PreyDefeated(owner, target, execution: true);
                return;
            }
            hunt.Prey = null;
            await target.RemoveBuff(StrikeProfiles.HuntingPreyId, showChangeTip: false);
        }

        private static async Task TerrifyEnemies(BattleRole owner)
        {
            const int fearId = 128;
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null || fight.IsFightOver) return;

            var enemies = new List<BattleRole>();
            if (owner.IsAlly) enemies.AddRange(fight.CurWaveEnemies);
            else enemies.AddRange(fight.Allies);
            var checks = new List<DiceResultData>();
            foreach (BattleRole enemy in enemies)
            {
                if (!StrikeSequence.CanHit(enemy) || !enemy.Data.IsHuman) continue;
                checks.Add(new DiceResultData
                {
                    Role = enemy, CheckType = EDiceValueType.Attr, CheckId = (int)EHeroAttribute.POW,
                    OriginAttr = EHeroAttribute.POW, CheckValue = enemy.Data.GetAttributeValue(EHeroAttribute.POW)
                });
            }
            if (checks.Count == 0) return;

            await BattleHelper.UpdateDiceResultData(checks);
            foreach (DiceResultData check in checks)
                if (check.Result != EDiceResult.None && check.Result.GetLevel() < EDiceResult.Success.GetLevel())
                    await check.Role.AddBuff(owner, fearId);
        }

        private static async Task Assign(Hunt hunt, BattleRole excluded = null)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            BattleRole owner = hunt.Owner;
            if (fight == null || fight.IsFightOver || !StrikeSequence.CanHit(owner)) return;

            var candidates = new List<BattleRole>();
            if (owner.IsAlly) candidates.AddRange(fight.CurWaveEnemies);
            else candidates.AddRange(fight.Allies);
            candidates.RemoveAll(role => role == excluded || role.IsAlly == owner.IsAlly ||
                !StrikeSequence.CanHit(role) || !role.CheckCanBeSelectedAsTarget(owner));
            if (candidates.Count == 0) return;

            BattleRole target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            hunt.Prey = target;
            hunt.Remainder = 0;
            await target.AddBuff(owner, StrikeProfiles.HuntingPreyId);
            if (HasMark(target))
            {
                HuntDisplay.Attach(target, fight);
                ValennaPlugin.Instance.Report($"狩猎目标：{target.Data.Name}，第 {fight.Round} 行动轮");
            }
        }

        [HarmonyPatch(typeof(BattleFightContent), nameof(BattleFightContent.EnterPhase))]
        private static class RoundEnd
        {
            private static void Prefix(BattleFightContent __instance, EFightPhase __0, out bool __state)
            {
                __state = __0 == EFightPhase.ResultStage && __instance.CurrentPhase != __0 &&
                    !__instance.IsFightOver && __instance.Round % 3 == 0;
            }

            private static void Postfix(BattleFightContent __instance, bool __state, ref Task __result)
            {
                if (__state) __result = PrepareNextRound(__result, __instance);
            }

            private static async Task PrepareNextRound(Task original, BattleFightContent fight)
            {
                await original;
                if (!fight.IsFightOver && Hunts.TryGetValue(fight, out Hunt hunt) && !HasMark(hunt.Prey))
                    hunt.RenewInRound = fight.Round + 1;
            }
        }

        [HarmonyPatch(typeof(RoleData), "get_Speed")]
        private static class PreySpeed
        {
            private static void Postfix(RoleData __instance, ref int __result)
            {
                if (Current?.Prey?.Data == __instance && HasMark(__instance.Role))
                    __result = Math.Min(__result, Math.Max(10, __result - 50));
            }
        }

        [HarmonyPatch(typeof(BattleSkillData), nameof(BattleSkillData.UpdateValidTargetSelectData))]
        private static class ExecutionTargets
        {
            private static void Postfix(BattleSkillData __instance, BattleRole __0)
            {
                if (__instance.Id != StrikeProfiles.ExecutionId || !BattleHelper.IsInBattle) return;

                __instance.TargetSelectCheckFilterRole.Clear();
                BattleRole target = TargetFor(__0);
                if (target != null) __instance.TargetSelectCheckFilterRole.Add(target);
            }
        }

        [HarmonyPatch(typeof(BattleSkillSelectElement), nameof(BattleSkillSelectElement.SetMasked))]
        private static class ExecutionButton
        {
            private static void Prefix(BattleSkillSelectElement __instance, ref bool __0)
            {
                if (__instance.SkillData?.Id == StrikeProfiles.ExecutionId)
                    __0 |= TargetFor(BattleHelper.BattleHeroInfo) == null;
            }

        }

        [HarmonyPatch(typeof(UIBattlePanel), "_OnClickBattleSkillSelectElement")]
        private static class ExecutionClick
        {
            private static bool Prefix(BattleSkillSelectElement __0, ref Task __result)
            {
                if (__0?.SkillData?.Id != StrikeProfiles.ExecutionId ||
                    TargetFor(BattleHelper.BattleHeroInfo) != null) return true;

                __0.SetMasked(true);
                __result = Task.CompletedTask;
                return false;
            }
        }

        private sealed class HuntDisplay : MonoBehaviour
        {
            private BattleRole _role;
            private BattleFightContent _fight;
            private SpriteRenderer _body;
            private SpriteRenderer _swords;
            private SpriteRenderer _glow;
            private float _time;

            internal static void Attach(BattleRole role, BattleFightContent fight)
            {
                SpriteRenderer body = role.Model?.SpriteRender;
                if (body == null || StrikeArt.HuntSwords == null || StrikeArt.HuntGlow == null) return;

                var mark = new GameObject("ValennaHuntingPrey").AddComponent<HuntDisplay>();
                mark.transform.SetParent(body.transform, false);
                mark._role = role;
                mark._fight = fight;
                mark._body = body;
                mark._swords = mark.AddLayer(StrikeArt.HuntSwords, 2);
                mark._glow = mark.AddLayer(StrikeArt.HuntGlow, 1);
                mark.LateUpdate();
            }

            private SpriteRenderer AddLayer(Sprite sprite, int order)
            {
                var layer = new GameObject(sprite.name).AddComponent<SpriteRenderer>();
                layer.transform.SetParent(transform, false);
                layer.sprite = sprite;
                layer.sortingLayerID = _body.sortingLayerID;
                layer.sortingOrder = _body.sortingOrder + order;
                return layer;
            }

            private void LateUpdate()
            {
                if (_body == null || !ReferenceEquals(BattleHelper.FightContent, _fight) ||
                    !BattleHelper.IsInBattle || !HasMark(_role))
                {
                    Destroy(gameObject);
                    return;
                }

                _time += Time.deltaTime;
                float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_time / 0.34f));
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5.8f);
                float height = Mathf.Clamp(_body.bounds.size.y, 0.45f, 2.45f);
                Vector3 center = _body.bounds.center;
                transform.position = center + Vector3.up * height * 0.04f;
                float displaySize = height * 1.5f * Mathf.Lerp(0.88f, 0.53f, settle);
                float modelScale = Mathf.Max(Mathf.Abs(_body.transform.lossyScale.x), 0.001f);
                transform.localScale = Vector3.one * (displaySize / modelScale);
                _swords.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.85f, settle));
                _glow.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.31f + pulse * 0.25f, settle));
            }
        }
    }
}
