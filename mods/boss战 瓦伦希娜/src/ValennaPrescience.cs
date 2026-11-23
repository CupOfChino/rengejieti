using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game;
using GamePlayEvent;
using Game.SkillData;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal static class ValennaPrescience
    {
        private static readonly Dictionary<BattleActiveBehaviorData, IncomingAttack> Attacks =
            new Dictionary<BattleActiveBehaviorData, IncomingAttack>();

        internal static async Task InstallEye(BattleRole role)
        {
            if (ValennaEyeEquipment.IsWorn(role) && role.Data.BuffList != null &&
                !role.IsDeath && role.GetBuff(StrikeProfiles.EyeChargeId) == null &&
                role.GetBuff(StrikeProfiles.EyeOverheatId) == null)
                await role.AddBuff(role, StrikeProfiles.EyeChargeId, showChangeTip: false);
        }

        internal static async Task RestoreEye(BattleRole role)
        {
            if (!ValennaEyeEquipment.IsWorn(role) || role.Data.BuffList == null) return;
            BuffData overheat = role.GetBuff(StrikeProfiles.EyeOverheatId);
            if (overheat != null)
            {
                await overheat.ChangeLayer(role, StrikeProfiles.OverheatRecovery);
                if (overheat.CurLayer >= StrikeProfiles.MaxEyeCharge)
                {
                    if (role.GetBuff(StrikeProfiles.EyeChargeId) == null)
                        await role.AddBuff(role, StrikeProfiles.EyeChargeId, showChangeTip: false);
                    if (role.GetBuff(StrikeProfiles.EyeChargeId) != null)
                        await role.RemoveBuff(StrikeProfiles.EyeOverheatId, showChangeTip: false);
                }
                return;
            }

            BuffData eye = role.GetBuff(StrikeProfiles.EyeChargeId);
            if (eye != null)
                await eye.ChangeLayer(role, StrikeProfiles.ChargeRecovery);
        }

        private static bool CanDefend(BattleRole role) => StrikeSequence.CanHit(role) && !role.IsUnableAct &&
            ValennaEyeEquipment.IsWorn(role);

        private static async Task SpendEye(BattleRole role, BuffData eye)
        {
            await eye.ChangeLayer(role, -Math.Min(StrikeProfiles.EyeUseCost, eye.CurLayer));
            if (eye.CurLayer <= 0 && role.GetBuff(StrikeProfiles.EyeOverheatId) == null)
                await role.AddBuff(role, StrikeProfiles.EyeOverheatId, showChangeTip: false);
        }

        private static bool IsDodge(DiceResultData dice) => dice != null && !dice.IsNoRoll &&
            dice.CheckType == EDiceValueType.ExtraAttr && dice.CheckId == (int)ERoleExtraAttribute.Dodge;

        private sealed class Defence
        {
            internal int Bonus;
            internal bool Evaded;
            internal bool Incomplete;
            internal int Contacts;
            internal DiceResultData PendingRoll;
            internal MarkLifetime Mark;

            internal void BreakChain()
            {
                Incomplete = true;
                Bonus = 0;
            }
        }

        private readonly struct MarkLifetime
        {
            internal readonly PrescienceDisplay Display;
            private readonly int _revision;

            internal MarkLifetime(PrescienceDisplay display)
            {
                Display = display;
                _revision = display.Revision;
            }

            internal void Fade()
            {
                if (Display != null) Display.StartFade(_revision);
            }
        }

        private sealed class IncomingAttack
        {
            internal readonly AcceleratedFuture.AttackUse Future;
            internal bool AttackExecuted;
            internal Task Resolution = Task.CompletedTask;
            internal readonly Dictionary<BattleRole, Defence> Defenders =
                new Dictionary<BattleRole, Defence>();
            internal readonly Dictionary<DiceResultData, EBattleDefenceType> OriginalDefence =
                new Dictionary<DiceResultData, EBattleDefenceType>();

            internal IncomingAttack(AcceleratedFuture.AttackUse future) { Future = future; }

            internal Defence For(BattleRole role)
            {
                if (!Defenders.TryGetValue(role, out Defence state))
                    Defenders.Add(role, state = new Defence());
                return state;
            }

            internal void RestoreDefence(DiceResultData dice)
            {
                if (!OriginalDefence.TryGetValue(dice, out EBattleDefenceType previous)) return;
                BattleDefenceData defence = dice.Role?.CurrentDefenceData;
                if (defence != null && ReferenceEquals(defence.RollDice, dice) &&
                    defence.DefenceType == EBattleDefenceType.DodgeOnly)
                    defence.DefenceType = previous;
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.Run))]
        private static class AttackWindow
        {
            private static void Prefix(BattleActiveBehaviorData __instance, out IncomingAttack __state)
            {
                __state = null;
                if (!BattleHelper.IsInBattle || Attacks.ContainsKey(__instance)) return;

                ValennaHunt.BeginAttack(__instance.Self);
                AcceleratedFuture.AttackUse future = AcceleratedFuture.BeginAttack(__instance);
                if (future == null && __instance.BattleSkillData?.Config?.NeedDamage != true &&
                    __instance.MagicData?.Config?.NeedDamage != true) return;

                __state = new IncomingAttack(future);
                Attacks.Add(__instance, __state);
            }

            private static void Postfix(BattleActiveBehaviorData __instance, IncomingAttack __state, ref Task __result)
            {
                if (__state != null) __result = Finish(__result, __instance, __state);
            }

            private static async Task Finish(Task original, BattleActiveBehaviorData action, IncomingAttack watch)
            {
                try
                {
                    await original;
                    // 原版的部分分组用回调计数推进，不会等待此处追加的效果任务。
                    await watch.Resolution;
                }
                finally
                {
                    Attacks.Remove(action);
                    foreach (DiceResultData dice in watch.OriginalDefence.Keys)
                        watch.RestoreDefence(dice);
                    foreach (Defence defence in watch.Defenders.Values)
                        defence.Mark.Fade();
                }

                if (!action.IsCompleted || BattleHelper.GameOptionIsBreak) return;
                try
                {
                    if (watch.AttackExecuted && watch.Future != null)
                        await watch.Future.Consume(action.Self);

                    foreach (var entry in watch.Defenders)
                    {
                        BattleRole defender = entry.Key;
                        Defence result = entry.Value;
                        BattleRole attacker = action.Self;
                        if (!result.Evaded || result.Incomplete || result.Bonus <= 0 || !CanDefend(defender) ||
                            !ValennaEyeEquipment.HasPrescience(defender) ||
                            !StrikeSequence.CanHit(attacker) || !BattleHelper.IsInBattle)
                            continue;

                        BattleSkillData skill = defender.Data.BattleSkills?.Find(item =>
                            item?.Id == StrikeProfiles.RabbitId && item.Config != null);
                        if (skill == null)
                        {
                            var definition = Singleton<ResManager>.Instance.CharacterBattleSkillData.Find(
                                item => item.Id == StrikeProfiles.RabbitId);
                            if (definition == null) continue;
                            skill = new BattleSkillData();
                            skill.Install(definition);
                        }

                        ValennaPlugin.Instance.Report("预知完整闪避，反击解兔");
                        ValennaHunt.BeginAttack(defender);
                        await StrikeSequence.Play(defender, attacker, skill, StrikeSequence.Clips[StrikeProfiles.RabbitId]);
                    }
                }
                catch (Exception error)
                {
                    ValennaPlugin.Instance.Report(error);
                }
            }
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), "_UpdateRollDiceTmpDiceCount")]
        private static class ApplyDodgeDice
        {
            private static void Postfix(BattleActiveBehaviorData __instance, List<DiceResultData> __0,
                bool __2, ref Task __result)
            {
                if (__2 && __0 != null && Attacks.TryGetValue(__instance, out IncomingAttack watch))
                    __result = AddDice(__result, __instance, watch, __0);
            }

            private static async Task AddDice(Task original, BattleActiveBehaviorData action,
                IncomingAttack watch, List<DiceResultData> diceList)
            {
                await original;
                if (BattleHelper.GameOptionIsBreak) return;

                foreach (DiceResultData dice in diceList)
                {
                    BattleRole defender = dice?.Role;
                    if (!IsDodge(dice) || dice.CheckValue <= 0 || dice.FixedResult != EDiceResult.None ||
                        !CanDefend(defender) || defender.IsAlly == action.Self.IsAlly ||
                        !action.Targets.Contains(defender)) continue;

                    BattleDefenceData defence = defender.CurrentDefenceData;
                    if (defence == null || !ReferenceEquals(defence.RollDice, dice)) continue;

                    Defence state = watch.For(defender);
                    if (state.PendingRoll == dice) continue;
                    state.PendingRoll = dice;
                    if (state.Bonus == 0)
                    {
                        if (defender.GetBuff(StrikeProfiles.EyeOverheatId) != null)
                        {
                            dice.TmpPunishDice++;
                        }
                        else
                        {
                            BuffData eye = defender.GetBuff(StrikeProfiles.EyeChargeId);
                            if (eye != null && eye.CurLayer >= StrikeProfiles.EyeUseCost)
                            {
                                int before = eye.CurLayer;
                                await SpendEye(defender, eye);
                                state.Bonus = before <= StrikeProfiles.LowEyeCharge ? 1 : 2;
                                ValennaPlugin.Instance.Report($"预知眼 {before}→{Math.Max(eye.CurLayer, 0)} 层，闪避 +{state.Bonus} 奖励骰");
                            }
                        }
                    }

                    if (state.Bonus > 0 && ValennaEyeEquipment.HasPrescience(defender))
                    {
                        if (!watch.OriginalDefence.ContainsKey(dice))
                            watch.OriginalDefence.Add(dice, defence.DefenceType);
                        defence.DefenceType = EBattleDefenceType.DodgeOnly;
                    }
                    dice.TmpRewardDice += state.Bonus;
                    PrescienceDisplay display = PrescienceDisplay.Show(defender, action.Self);
                    state.Mark = display == null ? default : new MarkLifetime(display);
                }
            }
        }

        private readonly struct ResolvedDefence
        {
            internal readonly BattleRole Role;
            internal readonly MarkLifetime Mark;
            private readonly Defence _state;
            private readonly int _contacts;
            private readonly bool _evaded;

            internal ResolvedDefence(BattleRole role, Defence state, bool evaded)
            {
                Role = role;
                Mark = state.Mark;
                _state = state;
                _contacts = state.Contacts;
                _evaded = evaded;
            }

            internal bool CanEarnFuture => _evaded && _state.Contacts == _contacts;
        }

        [HarmonyPatch(typeof(BattleActiveBehaviorData), nameof(BattleActiveBehaviorData.ActionEffectProcess))]
        private static class RecordSegment
        {
            private static void Prefix(BattleActiveBehaviorData __instance, EDiceResult __0,
                List<BattleRole> __1, out List<ResolvedDefence> __state)
            {
                __state = null;
                if (__0 == EDiceResult.None || __1 == null || __1.Count == 0 || BattleHelper.GameOptionIsBreak ||
                    !Attacks.TryGetValue(__instance, out IncomingAttack watch)) return;

                watch.AttackExecuted |= __instance.BattleSkillData?.Id != StrikeProfiles.ExecutionId ||
                    __1.Exists(role => ValennaHunt.CanExecute(__instance.Self, role));

                foreach (BattleRole defender in __1)
                {
                    if (!CanDefend(defender) || defender.IsAlly == __instance.Self.IsAlly) continue;
                    Defence state = watch.For(defender);
                    DiceResultData dodge = state.PendingRoll;
                    DiceResultData attack = __instance.RollDice;
                    int success = EDiceResult.Success.GetLevel();

                    // 一致检定会共用攻击结果，每名防守者仍按自己的防御骰判断。
                    bool dodged = __0.GetLevel() < success && dodge != null &&
                        dodge.Result.GetLevel() >= success &&
                        (attack == null || attack.CheckType == EDiceValueType.None ||
                         dodge.Result.GetLevel() >= attack.Result.GetLevel());
                    if (dodged)
                    {
                        state.Evaded = true;
                        if (state.Mark.Display != null) state.Mark.Display.Confirm();
                    }
                    else
                        state.BreakChain();
                    state.PendingRoll = null;
                    if (dodged || state.Mark.Display != null)
                    {
                        if (__state == null) __state = new List<ResolvedDefence>();
                        __state.Add(new ResolvedDefence(defender, state, dodged));
                    }
                }
            }

            private static void Postfix(BattleActiveBehaviorData __instance, List<ResolvedDefence> __state,
                ref Task __result)
            {
                if (__state == null || !Attacks.TryGetValue(__instance, out IncomingAttack watch)) return;
                __result = watch.Resolution = Resolve(__result, watch.Resolution, __state);
            }

            private static async Task Resolve(Task original, Task prior, List<ResolvedDefence> defences)
            {
                try { await original; }
                finally { foreach (ResolvedDefence defence in defences) defence.Mark.Fade(); }

                await prior;
                foreach (ResolvedDefence defence in defences)
                    if (defence.CanEarnFuture) await AcceleratedFuture.Grant(defence.Role);
            }
        }

        [HarmonyPatch(typeof(BattleDefenceData), nameof(BattleDefenceData.Run))]
        private static class HoldDuringDodge
        {
            private static void Postfix(BattleDefenceData __instance, ref Task __result)
            {
                BattleRole role = __instance.Self;
                if (!BattleHelper.IsInBattle || !ValennaEyeEquipment.IsWorn(role) ||
                    !CanDefend(role) || __instance.DefenceType == EBattleDefenceType.None ||
                    !IsDodge(__instance.RollDice)) return;

                PrescienceDisplay display = PrescienceDisplay.Show(role, BattleHelper.FightContent?.CurActionRole);
                if (display != null)
                    __result = FadeAfterDodge(__result, new MarkLifetime(display));
            }

            private static async Task FadeAfterDodge(Task original, MarkLifetime mark)
            {
                try { await original; }
                finally { mark.Fade(); }
            }
        }

        private sealed class HitContext
        {
            internal BattleRole Target;
            internal BattleRole Attacker;
            internal HitContext Previous;
            internal BuffData Breath;
            internal DamageAdditionalData AddData;
            internal bool HitPrey;
            internal bool Executing;
            internal bool CriticalApplied;
        }

        private static readonly AsyncLocal<HitContext> ActiveHit = new AsyncLocal<HitContext>();

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.OnHit))]
        private static class DrainOnAttack
        {
            private static void Prefix(BattleRole __instance, BattleRole __0, int __1,
                DamageAdditionalData __3, DamageSourceRecord __4, out HitContext __state)
            {
                __state = null;
                if (__0 == null || __0 == __instance) return;

                bool hitPrey = ValennaHunt.IsMarkedBy(__0, __instance);
                bool executing = hitPrey && __4?.BattleSkill?.Id == StrikeProfiles.ExecutionId;
                BuffData breath = ValennaBattleStatus.TryCritical(__0, __3, __4, __1);
                if (!hitPrey && breath == null && !ValennaEyeEquipment.IsWorn(__instance)) return;

                __state = new HitContext
                {
                    Target = __instance, Attacker = __0, Previous = ActiveHit.Value,
                    Breath = breath, HitPrey = hitPrey, Executing = executing, AddData = __3
                };
                ActiveHit.Value = __state;
            }

            private static void Postfix(BattleRole __instance, HitContext __state, ref Task __result)
            {
                if (__state == null) return;
                ActiveHit.Value = __state.Previous;
                __result = FinishHit(__result, __instance, __state);
            }
        }

        private static async Task FinishHit(Task original, BattleRole role, HitContext hit)
        {
            await original;
            if (hit.CriticalApplied && ReferenceEquals(hit.Attacker.GetBuff(StrikeProfiles.BreathId), hit.Breath))
                await hit.Breath.ChangeLayer(hit.Attacker, -1);
            if (hit.HitPrey && role.IsDeath)
                await ValennaHunt.PreyDefeated(hit.Attacker, role, hit.Executing);
            await DrainAfterHit(Task.CompletedTask, role);
        }

        private static async Task DrainAfterHit(Task original, BattleRole role)
        {
            await original;
            if (role.IsDeath || !ValennaEyeEquipment.IsWorn(role) ||
                role.GetBuff(StrikeProfiles.EyeOverheatId) != null) return;

            BuffData eye = role.GetBuff(StrikeProfiles.EyeChargeId);
            if (eye == null || eye.CurLayer <= 0) return;
            int before = eye.CurLayer;
            await SpendEye(role, eye);
            ValennaPlugin.Instance.Report($"预知眼受击 {before}→{Math.Max(0, eye.CurLayer)} 层");
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.SetDamage),
            new[] { typeof(uint), typeof(bool), typeof(bool), typeof(RoleHitData) })]
        private static class CheckContact
        {
            private static void Prefix(BattleRole __instance, ref uint __0, RoleHitData __3,
                out CriticalDamageNumbers.Scope __state)
            {
                __state = CriticalDamageNumbers.Current;
                BattleRole attacker = __3?.Attacker;
                if (attacker == null || attacker == __instance) return;

                foreach (var active in Attacks)
                    if (active.Key.Self == attacker &&
                        active.Value.Defenders.TryGetValue(__instance, out Defence state))
                    {
                        state.Contacts++;
                        state.BreakChain();
                    }

                if (__0 == 0) return;
                int damage = (int)Math.Min((uint)int.MaxValue, __0);
                BattleActiveBehaviorData action = attacker.CurrentBehaviourData;
                if (__3.AddData?.SourceType == EDamageSourceType.BattleSkill && action?.IsRunning == true &&
                    Attacks.TryGetValue(action, out IncomingAttack watch) && watch.AttackExecuted && watch.Future != null)
                    damage = watch.Future.Boost(damage);
                damage = ValennaHunt.BoostDamage(attacker, __instance, damage);

                HitContext hit = ActiveHit.Value;
                if (hit != null && hit.Target == __instance && hit.Attacker == attacker &&
                    hit.Breath != null && !hit.CriticalApplied && ReferenceEquals(hit.AddData, __3.AddData))
                {
                    damage = (int)Math.Min(int.MaxValue, (12L * damage + 5) / 10);
                    hit.CriticalApplied = true;
                    CriticalDamageNumbers.Current = new CriticalDamageNumbers.Scope(__instance.Model?.Hud);
                }
                __0 = (uint)damage;
                __3.Damage = damage;
            }

            private static void Postfix(BattleRole __instance, RoleHitData __3, CriticalDamageNumbers.Scope __state,
                ref Task __result)
            {
                CriticalDamageNumbers.Current = __state;
                BattleRole attacker = __3?.Attacker;
                if (attacker == null || attacker == __instance || !ValennaEyeEquipment.IsWorn(__instance)) return;
                for (HitContext hit = ActiveHit.Value; hit != null; hit = hit.Previous)
                    if (hit.Target == __instance && hit.Attacker == attacker) return;
                __result = DrainAfterHit(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.TriggerRoundStartEffectOnRoundChangeInMap))]
        private static class ExploreRecovery
        {
            private static void Postfix(BuffData __instance, ref Task __result)
            {
                if ((BattleHelper.IsInExplore && !BattleHelper.IsInBattle) &&
                    (__instance.Id == StrikeProfiles.EyeChargeId || __instance.Id == StrikeProfiles.EyeOverheatId))
                    __result = Recover(__result, __instance);
            }

            private static async Task Recover(Task original, BuffData status)
            {
                await original;
                BattleRole role = status.TargetRole;
                if (ValennaEyeEquipment.IsWorn(role) && !role.IsDeath && ReferenceEquals(role.GetBuff(status.Id), status))
                    await RestoreEye(role);
            }
        }

        [HarmonyPatch(typeof(BattleHelper), nameof(BattleHelper.GetDiceData),
            new[] { typeof(BattleRole), typeof(EExploreSkill), typeof(EHeroAttribute), typeof(ERoleExtraAttribute), typeof(bool) })]
        private static class ExploreOverheat
        {
            private static void Postfix(BattleRole __0, ERoleExtraAttribute __3, CheckDiceData __result)
            {
                if (BattleHelper.IsInBattle || __3 != ERoleExtraAttribute.Dodge || __result == null) return;
                BattleRole role = __0 ?? BattleHelper.SceneHeroInfo;
                if (ValennaEyeEquipment.IsWorn(role) && role.Data.BuffList != null &&
                    role.GetBuff(StrikeProfiles.EyeOverheatId) != null)
                    __result.PunishDiceCount++;
            }
        }
    }

    // 图标跟着模型实时挂点，持续到本次攻击的骰子和伤害都处理结束。
    internal sealed class PrescienceDisplay : MonoBehaviour
    {
        private BattleRole _role;
        private BattleFightContent _fight;
        private RoleModel _model;
        private SpriteRenderer _body;
        private StrikeEffects _effects;
        private bool _dodge;
        private float _remaining = 1f;
        internal bool IsHeld { get; private set; }
        internal int Revision { get; private set; }

        private static readonly Color Echo = new Color(0.97f, 0.52f, 0.49f);

        internal static PrescienceDisplay Show(BattleRole role, BattleRole attacker)
        {
            RoleModel model = role.Model;
            SpriteRenderer body = model?.SpriteRender;
            Camera camera = BattleHelper.BattleCam ?? Camera.main;
            if (body == null || camera == null || !model.isActiveAndEnabled) return null;

            PrescienceDisplay display = body.GetComponent<PrescienceDisplay>();
            if (display != null && display._effects != null &&
                ReferenceEquals(display._fight, BattleHelper.FightContent))
            {
                display.Refresh();
                return display;
            }

            display = body.gameObject.AddComponent<PrescienceDisplay>();
            display._role = role;
            display._fight = BattleHelper.FightContent;
            display._model = model;
            display._body = body;
            Vector3 right = camera.transform.right * (body.flipX ? 1f : -1f);
            if (attacker?.Model != null)
            {
                float side = Vector3.Dot(attacker.Model.transform.position - model.transform.position, camera.transform.right);
                if (Mathf.Abs(side) > 0.001f) right = camera.transform.right * Mathf.Sign(side);
            }
            float height = Mathf.Clamp(body.bounds.size.y, 0.45f, 2.2f);
            display._effects = new StrikeEffects(display._fight.Room?.RoomEntity?.SceneTipRoot,
                body, right, camera.transform, height, useRibbon: false);
            display._effects.Foretell(body);
            display.Refresh();
            return display;
        }

        private void LateUpdate()
        {
            if (!BattleHelper.IsInBattle || !ReferenceEquals(BattleHelper.FightContent, _fight) ||
                !StrikeSequence.CanHit(_role) || _role.Model != _model || _body == null)
            {
                Close();
                return;
            }

            float step = Mathf.Min(Time.deltaTime, 0.05f);
            if (!IsHeld)
            {
                _remaining -= Time.unscaledDeltaTime;
                if (_remaining <= 0f)
                {
                    Close();
                    return;
                }
            }

            _effects.SetForetellOpacity(Mathf.SmoothStep(0f, 1f, _remaining));
            _effects.Advance(step);
            if (IsHeld) _effects.SamplePose(_body, Echo);
        }

        internal void Confirm() => _effects?.ConfirmDodge(_body);

        internal void Refresh()
        {
            Revision++;
            IsHeld = true;
            _remaining = 1f;
            _effects.SetForetellOpacity(1f);
            _effects.SamplePose(_body, Echo, leaveEcho: false);
            _dodge = _model.CheckSpriteAnimExist("valenna_prescience_dodge");
            if (_dodge) _model.PlaySpriteAnim("valenna_prescience_dodge", 1f);
        }

        internal void StartFade(int revision)
        {
            if (revision != Revision || !IsHeld || _effects == null) return;
            IsHeld = false;
            ReturnToIdle();
        }

        private void ReturnToIdle()
        {
            if (_dodge && _body != null && _body.sprite != null &&
                _body.sprite.name == "valenna_pose_dodge" && _role.Model == _model && StrikeSequence.CanHit(_role))
                _model.PlayIdle(isFight: true);
            _dodge = false;
        }

        internal void Close()
        {
            if (_effects == null) return;
            enabled = false;
            ReturnToIdle();
            _effects.Dispose();
            _effects = null;
            Destroy(this);
        }

        private void OnDestroy() => _effects?.Dispose();
    }
}
