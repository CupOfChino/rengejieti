using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal sealed class StrikeSequence
    {
        internal static readonly IReadOnlyDictionary<int, StrikeShow> Clips =
            StrikeProfiles.Shows.ToDictionary(show => show.Id);

        private static readonly HashSet<StrikeSequence> Playing = new HashSet<StrikeSequence>();

        private readonly BattleFightContent _fight;
        private readonly BattleSkillData _skill;
        private readonly StrikeShow _show;
        private readonly Actor _source;
        private readonly Actor _target;
        private readonly Transform _view;
        private readonly Vector3 _forward;
        private readonly Vector3 _up;
        private readonly float _size;
        private readonly bool _originalFacing;

        private StrikeEffects _effects;
        private StrikeFootwork _footwork;
        private Sprite _displayedPose;
        private string _stepAction;
        private Task _pendingImpact = Task.CompletedTask;
        private bool _stopped;

        private StrikeSequence(BattleRole actor, BattleRole target, BattleSkillData skill, Camera camera,
            StrikeShow show)
        {
            _fight = BattleHelper.FightContent;
            _skill = skill;
            _show = show;
            _source = new Actor(actor);
            _target = new Actor(target);
            _view = camera.transform;
            Vector3 right = _view.right;
            _forward = right * (Vector3.Dot(_target.OriginalWorld - _source.OriginalWorld, right) < 0f ? -1f : 1f);
            _up = _view.up;
            _size = Mathf.Clamp(_source.Model.SpriteRender.bounds.size.y, 0.45f, 2.2f);
            _originalFacing = _source.Model.SpriteRender.flipX;
        }

        internal static bool CanHit(BattleRole role) =>
            role?.Data != null && !role.IsDeath && !role.IsEscape;

        internal static async Task Play(BattleRole actor, BattleRole target, BattleSkillData skill,
            StrikeShow show)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (!CanContinueBattle(fight) || !CanHit(actor) || !CanHit(target)) return;

            RoleModel sourceModel = actor.Model;
            RoleModel targetModel = target.Model;
            Camera camera = BattleHelper.BattleCam;
            if (camera == null) camera = Camera.main;
            bool busy = Playing.Any(clip => clip.UsesModel(sourceModel) || clip.UsesModel(targetModel));
            if (!HasSprite(sourceModel) || !HasSprite(targetModel) || camera == null || busy)
            {
                var sourceData = actor.Data;
                var targetData = target.Data;
                ValennaPlugin.Instance.Warn($"跳过出招演出。施放者画面 {HasSprite(sourceModel)}，目标画面 {HasSprite(targetModel)}，战斗相机 {camera != null}，模型占用 {busy}");
                foreach (StrikeCue cue in show.Cues)
                {
                    if (!CanContinueBattle(fight) || !CanHit(actor) || !CanHit(target) ||
                        !ReferenceEquals(sourceData, actor.Data) || !ReferenceEquals(targetData, target.Data)) break;
                    if (cue.Damage) await Hit(actor, target, skill);
                }
                return;
            }

            var sequence = new StrikeSequence(actor, target, skill, camera, show);
            Playing.Add(sequence);
            await sequence.PlayClip();
        }

        internal static void StopForRestart(BattleFightContent fight)
        {
            foreach (StrikeSequence sequence in Playing.Where(clip => ReferenceEquals(clip._fight, fight)).ToArray())
                sequence.StopPresentation();
        }

        // 原版换模型会停用 RoleModel 组件，SpriteAnim 仍从独立的组件播放。
        private static bool HasSprite(RoleModel model) =>
            model != null && model.SpriteRender != null && model.SpriteRender.gameObject.activeInHierarchy;

        private static bool IsCurrentBattle(BattleFightContent fight) =>
            fight != null && BattleHelper.IsInBattle && ReferenceEquals(BattleHelper.FightContent, fight);

        private static bool CanContinueBattle(BattleFightContent fight) =>
            IsCurrentBattle(fight) && !fight.IsRestartBattleBreak && !fight.IsGiveUpBattleBreak;

        private bool UsesModel(RoleModel model) =>
            IsCurrentBattle(_fight) && ((_source.Model == model && _source.IsCurrent) ||
                (_target.Model == model && _target.IsCurrent));

        private bool CanAnimate() =>
            !_stopped && CanContinueBattle(_fight) && _source.IsCurrent && _target.IsCurrent &&
            CanHit(_source.Role) && CanHit(_target.Role) && HasSprite(_source.Model) && HasSprite(_target.Model);

        private async Task PlayClip()
        {
            try
            {
                if (!CanAnimate()) return;

                var body = _source.Model.SpriteRender;
                float spacing = Mathf.Clamp(
                    (body.bounds.size.x + _target.Model.SpriteRender.bounds.size.x) * 0.38f,
                    _size * 0.42f, _size * 0.86f);
                if (_show.Id == 885121)
                {
                    float distance = Vector3.Dot(_target.OriginalWorld - _source.OriginalWorld, _forward);
                    spacing = Mathf.Max(spacing, Mathf.Min(_size * 1.12f, distance));
                }
                Vector3 close = _target.OriginalWorld - _forward * spacing;
                _effects = new StrikeEffects(_fight.Room?.RoomEntity?.SceneTipRoot, body, _forward, _view,
                    _size, _show.Id == 885121);
                body.flipX = Vector3.Dot(_forward, _view.right) > 0f;
                string style = _effects.HasTextured ? "贴图扫斩"
                    : _effects.HasRibbon ? "分节网格"
                    : (_effects.HasStrokeMaterial ? "线条着色器" : "序列帧");
                ValennaPlugin.Instance.Report($"瓦伦娜刀光 {style}，模型高度 {_size:F2}");
                _displayedPose = body.sprite;
                _effects.SampleAttackPose(body, TrailColor);
                _footwork = body.gameObject.AddComponent<StrikeFootwork>();
                _footwork.BeforeRender = UpdateDisplayedPose;
                StrikeOpenings opening = StrikeOpenings.For(_show);
                TryPose(_source.Model, StrikeProfiles.OpeningPose(_show), "valenna_draw", "battle_idle", "idle");
                _effects.Prime(body, opening);
                StrikeCue first = _show.Cues[0];
                float drawTime = opening.Duration;
                float approachTime = _show.Id == 885121 ? StrikeProfiles.CutApproachTime : StrikeProfiles.ApproachTime;
                if (_show.Charged)
                {
                    _effects.ExecutionOpening(_source.Center, opening);
                    _effects.LeadCue(first, () => _source.Center, drawTime);
                }
                else
                    _effects.ApproachWind(() => _source.Center, drawTime - StrikeProfiles.SweepPoseLead,
                        approachTime + StrikeProfiles.SweepPoseLead);

                Vector3? firstSweepCenter = null;
                float beforeFirst = first.SweepLead - first.Windup;
                void BeforeFirstSweep(float timeToPose)
                {
                    float age = beforeFirst - timeToPose;
                    if (beforeFirst <= 0f || age < 0f || firstSweepCenter.HasValue) return;

                    // 在即将近身的位置先画第一道刃，人物在本段结束时才进入出刀姿势。
                    Vector3 closeCenter = _source.Center + (close - _source.World);
                    firstSweepCenter = _effects.HasTextured ? closeCenter :
                        Vector3.Lerp(closeCenter, _target.Center, 0.56f) - _up * (_size * 0.04f);
                    _effects.BeginSweep(first, firstSweepCenter.Value, 60f, -26f, age, _show.Id == StrikeProfiles.ExecutionId);
                }
                if (!await Animate(drawTime, t =>
                {
                    DrawBlade(-32f + 12f * t, Ease(t), t);
                    if (_show.Charged) BeforeFirstSweep(drawTime * (1f - t));
                })) return;

                if (_show.Charged)
                {
                    // 处置先原地亮刃，第一刀才切到目标身前。
                    _source.Move(close);
                }
                else
                {
                    TryPose(_source.Model, StrikeProfiles.ApproachPose(_show), "move");
                    _effects.LeadCue(first, () => _source.Center, approachTime);
                    if (!await Animate(approachTime, t =>
                    {
                        _source.Move(Vector3.Lerp(_source.OriginalWorld, close, Ease(t)));
                        DrawBlade(-20f, 1f, 0.95f);
                        BeforeFirstSweep(approachTime * (1f - t));
                    })) return;
                }

                for (int index = 0; index < _show.Cues.Length; index++)
                {
                    if (!CanAnimate()) return;
                    StrikeCue cue = _show.Cues[index];
                    StrikeCue next = index + 1 < _show.Cues.Length ? _show.Cues[index + 1] : null;
                    bool elephantFinisher = _show.Id == 885128 && next == null;
                    if (!await PlayBeat(cue, next, elephantFinisher, index == 0 ? firstSweepCenter : null)) return;
                }
                if (!CanAnimate()) return;

                Vector3 fromSource = _source.World;
                Vector3 fromTarget = _target.World;
                _stepAction = null;
                TryPose(_source.Model, StrikeProfiles.ReturnPose(_show), "valenna_recover", "move", "walk");
                if (await Animate(StrikeProfiles.ReturnTime, t =>
                {
                    _source.Move(Vector3.Lerp(fromSource, _source.OriginalWorld, Ease(t)));
                    _target.Move(Vector3.Lerp(fromTarget, _target.OriginalWorld, Ease(t)));
                    DrawBlade(18f - 35f * t, 1f, 1f - Mathf.Clamp01(t * 2.5f));
                }) && _effects.HasTextured)
                    await Animate(StrikeProfiles.ReturnAfterglow);
            }
            finally
            {
                try { StopPresentation(); }
                finally { await _pendingImpact; }
            }
        }

        private void StopPresentation()
        {
            if (_stopped) return;
            _stopped = true;
            Playing.Remove(this);
            if (_footwork != null)
            {
                _footwork.BeforeRender = null;
                UnityEngine.Object.Destroy(_footwork);
            }

            if (_effects == null) return;
            _effects.Dispose();
            if (!IsCurrentBattle(_fight)) return;

            _source.Restore();
            _target.Restore();
            if (!_source.IsCurrent) return;
            var body = _source.Model.SpriteRender;
            if (body != null) body.flipX = _originalFacing;
            if (CanHit(_source.Role)) _source.Model.PlayIdle(isFight: true);
        }

        private async Task<bool> WaitForImpact()
        {
            // 命中仍在结算时继续推进余光。确认死亡后先释放画面，再等原版结算结束。
            while (!_pendingImpact.IsCompleted && CanAnimate())
            {
                await new WaitForEndOfFrame();
                if (CanAnimate()) _effects.Advance(FrameStep);
            }
            if (!CanAnimate()) return false;
            await _pendingImpact;
            return CanAnimate();
        }

        private async Task<bool> PlayBeat(StrikeCue cue, StrikeCue next, bool elephantFinisher, Vector3? sweepCenter)
        {
            _stepAction = null;
            StrikeBeat beat = cue.Beat;
            if (_show.Charged && beat == StrikeBeat.Volley) return await PlayExecutionFinish(cue);
            if (cue.PauseBeforePose != null)
            {
                TryPose(_source.Model, cue.PauseBeforePose);
                _effects.BraceWind(_source.Center);
                _effects.LeadCue(cue, () => _source.Center, StrikeProfiles.BeatPause);
                if (!await Animate(StrikeProfiles.BeatPause)) return false;
            }

            bool shot = StrikeEffects.IsStraight(beat);
            bool thrust = beat == StrikeBeat.Thrust;
            bool passing = cue.PassDistance > 0f;
            float fromAngle = beat == StrikeBeat.Rising ? -78f : 60f;
            float impactAngle = beat == StrikeBeat.Rising ? 37f : -26f;
            if (shot) fromAngle = impactAngle = 3f;

            Vector3 fromSource = _source.World;
            float gap = Vector3.Dot(_target.World - fromSource, _forward);
            float travel = _size * (thrust ? cue.ThrustTravel : 0.10f);
            if (passing) travel = Mathf.Max(0f, gap - _size * 0.19f);
            Vector3 finish = fromSource + _forward * travel;
            string legacyPose = beat switch
            {
                StrikeBeat.Crosscut => "valenna_crosscut",
                StrikeBeat.Rising or StrikeBeat.Orbit => "valenna_rise",
                StrikeBeat.Thrust => "valenna_dash",
                _ => "valenna_muzzle"
            };
            string action = StrikeProfiles.ActionPose(cue);
            if (!TryPose(_source.Model, action, legacyPose, "attack", "battle_attack"))
                _source.Model.PlayModelAnim("Attack");
            var animation = _source.Model.SpriteAnim;
            bool poseSteps = !passing && animation != null && animation.CurrentPlayAnimKey == action &&
                animation.CurrentAnimation?.SpriteDatas?.Count > 0 && !animation.CurrentAnimation.IsUseOldMode &&
                animation.CurrentAnimation.SpriteDatas[0]?.Sprite != null;
            if (poseSteps) _stepAction = action;
            _effects.TurningSweeps(cue, () => _source.Center);
            bool elephant = _show.Id == 885128;
            if (elephant && cue.Pose == "valenna_elephant_prepare")
                _effects.ElephantGather(() => Grip(_forward), cue.Windup + cue.Hold);
            bool shotStarted = false;
            float lightAt = cue.Windup - cue.SweepLead;
            float shotLead = cue.SplitThrust || cue.TwinThrust ? 3f * StrikeProfiles.SweepInterval
                : passing ? StrikeProfiles.PassingThrustLead : StrikeProfiles.SweepPoseLead;
            float shotAt = cue.Windup - shotLead;

            Vector3 ShotOrigin(Vector3 hit, Vector3 offset)
            {
                Vector3 point = Muzzle(_forward, cue.SplitThrust, cue.RaisedBlade) + offset;
                if (passing)
                    point -= _forward * Mathf.Max(0f, Vector3.Dot(point - hit, _forward) + _size * 0.13f);
                return point;
            }
            if (!await Animate(cue.Windup, t =>
            {
                float elapsed = cue.Windup * t;
                if (!poseSteps)
                {
                    float advance = Mathf.Clamp01((elapsed - cue.Preparation) / (cue.Windup - cue.Preparation));
                    float lean = cue.Preparation > 0f
                        ? Ease(Mathf.Clamp01(elapsed / cue.Preparation)) * (1f - advance) * _size * 0.13f
                        : 0f;
                    _source.Move(Vector3.Lerp(fromSource, finish, Ease(advance)) - _forward * lean);
                }
                DrawBlade(Mathf.Lerp(fromAngle, impactAngle, Ease(t)), 1f, 1f);
                float age = elapsed - lightAt;
                if (cue.Sweep && (!shot || thrust) && !sweepCenter.HasValue && age >= 0f)
                {
                    sweepCenter = _effects.HasTextured
                        ? _source.Center
                        : Vector3.Lerp(_source.Center, _target.Center, 0.56f) - _up * (_size * 0.04f);
                    _effects.BeginSweep(cue, sweepCenter.Value, fromAngle, impactAngle, age, _show.Id == StrikeProfiles.ExecutionId);
                }
                if (shot && !shotStarted && elapsed >= shotAt)
                {
                    shotStarted = true;
                    Vector3 hit = _target.Center;
                    Vector3 muzzle = ShotOrigin(hit, poseSteps ? Vector3.zero : finish - _source.World);
                    float shotAge = elapsed - shotAt;
                    if (cue.TwinThrust) _effects.TwinThrust(muzzle, hit, cue.Scale, shotAge);
                    else _effects.BeginStraight(cue, muzzle, hit, _forward, shotAge);
                    _effects.RepeatThrust(cue, muzzle, hit, _forward, shotAge, () => _source.World);
                    if (elephant && beat == StrikeBeat.Volley)
                        _effects.ElephantRay(muzzle, hit, cue.Scale, shotAge);
                }
            })) return false;

            Vector3 contact = _target.Center;
            Vector3 targetBeforeHit = _target.World;
            Vector3 sourceBeforeHit = _source.World;
            float recoil = _size * (shot ? 0.28f : 0.18f);
            float push = cue.Damage ? (elephantFinisher ? _size * StrikeProfiles.ElephantKnockback : recoil) : 0f;
            Vector3 origin = shot ? ShotOrigin(contact, default) : Grip(_forward);
            _effects.Impact(cue, origin, contact, fromAngle, impactAngle, sweepCenter, _forward, leaveScar: false);
            if (elephantFinisher) _effects.ElephantFinishWind(_source.Center, contact, cue.Scale);
            Task fallback = !_effects.HasTextured && !_effects.HasStrokeMaterial && !shot
                ? _target.Model.PlayFx("valenna_arc_preview", "idle", false, ERolePointType.Center)
                : Task.CompletedTask;
            _pendingImpact = cue.Damage ? Task.WhenAll(Hit(_source.Role, _target.Role, _skill), fallback) : fallback;
            if (!CanAnimate()) return false;

            float passTravel = passing
                ? Mathf.Max(0f, Vector3.Dot(targetBeforeHit - sourceBeforeHit, _forward) + push + _size * cue.PassDistance)
                : 0f;
            bool passWindStarted = false;
            float passMoveTime = cue.PassMoveTime > 0f ? Mathf.Min(cue.PassMoveTime, cue.Hold) : 0.73f * cue.Hold;
            float windAt = passMoveTime * (0.36f / 0.73f);
            float recoilTime = cue.RecoilTime > 0f ? Mathf.Min(cue.RecoilTime, cue.Hold) : cue.Hold;
            if (next != null)
                _effects.LeadCue(next, () => _source.Center,
                    cue.Hold + (cue.PauseAfter ? StrikeProfiles.BeatPause : 0f),
                    reverse: _show.Charged && next.Beat == StrikeBeat.Volley,
                    flash: next.PauseBeforePose == null);
            // 受击推退沿出招方向。逻辑上的阵位号始终由游戏管理。
            if (!await Animate(cue.Hold, t =>
            {
                float elapsed = t * cue.Hold;
                if (cue.Damage)
                {
                    float recoilPhase = Mathf.Clamp01(elapsed / recoilTime);
                    float progress = 1f - (1f - recoilPhase) * (1f - recoilPhase) * (1f - recoilPhase);
                    Vector3 lift = _up * (_size * 0.05f * Mathf.Sin(Mathf.PI * recoilPhase));
                    _target.Move(targetBeforeHit + _forward * (push * progress) + lift);
                    if (!passing && !poseSteps)
                        _source.Move(sourceBeforeHit + _forward * (recoil * progress * (shot ? -0.38f : 0.30f)));
                }
                if (passing)
                {
                    _source.Move(sourceBeforeHit + _forward * (passTravel * Ease(Mathf.Clamp01(elapsed / passMoveTime))));
                    if (cue.LongThrust && !passWindStarted && elapsed >= windAt)
                    {
                        passWindStarted = true;
                        _effects.LongThrustWind(_source.Center, cue.Scale, elapsed - windAt);
                    }
                }
                DrawBlade(impactAngle + (shot ? 12f : 21f) * t, 1f, 1f);
            })) return false;
            if (passing)
            {
                float side = Vector3.Dot(_source.World - _target.World, _forward);
                ValennaPlugin.Instance.Report($"{_show.Name}穿过目标，已越过目标 {side:F2}");
            }
            if (cue.PauseAfter && !await Animate(StrikeProfiles.BeatPause)) return false;
            return await WaitForImpact();
        }

        private async Task<bool> PlayExecutionFinish(StrikeCue cue)
        {
            SpriteRenderer body = _source.Model.SpriteRender;
            Vector3 direction = -_forward;
            _effects.BraceWind(_source.Center, reverse: true);
            if (!await Animate(StrikeProfiles.SweepPoseLead)) return false;
            body.flipX = Vector3.Dot(direction, _view.right) > 0f;
            TryPose(_source.Model, "valenna_finish_brace", "valenna_finish_volley");
            if (!await Animate(StrikeProfiles.BeatPause)) return false;

            Vector3 from = _source.World;
            Vector3 tip = ValennaAppearance.ExecutionBladeTip(body);
            Bounds targetBounds = _target.Model.SpriteRender.bounds;
            Vector3 center = _target.Center;
            float torso = Mathf.Clamp(targetBounds.extents.y * 0.40f, 0.04f, _size * 0.20f);
            Vector3 contact = center + _up * Mathf.Clamp(Vector3.Dot(tip - center, _up), -torso, torso);
            float depth = Mathf.Min(_size * 0.08f, targetBounds.extents.x * 0.27f);
            Vector3 correction = contact + direction * depth - tip;
            Vector3 finish = from + direction * Vector3.Dot(correction, direction) + _up * Vector3.Dot(correction, _up);

            TryPose(_source.Model, "valenna_finish_insert", "valenna_finish_volley");
            _effects.ExecutionInsert(_source.Center, contact, cue.Scale);
            if (!await Animate(cue.Windup, t =>
            {
                _source.Move(Vector3.Lerp(from, finish, Ease(t)));
                DrawBlade(0f, 1f, 1f, reverse: true);
            })) return false;

            TryPose(_source.Model, "valenna_finish_pin", "valenna_finish_volley");
            _effects.PinBlade(body, contact, cue.Scale);
            _effects.LeadCue(cue, () => _source.Center, StrikeProfiles.BeatPause, reverse: true);
            ValennaPlugin.Instance.Report($"处置从身后推进 {Vector3.Dot(finish - from, direction):F2}，刺入后蓄光");
            if (!await Animate(StrikeProfiles.BeatPause)) return false;

            Vector3 targetBeforeHit = _target.World;
            Vector3 origin = ValennaAppearance.GunPoint(body) ?? Muzzle(direction, true);
            TryPose(_source.Model, StrikeProfiles.ActionPose(cue), "valenna_muzzle", "attack");
            _effects.ReleaseMuzzleRing(body, cue.Scale);
            _effects.Impact(cue, origin, contact, 0f, 0f, null, direction, leaveScar: true);
            _pendingImpact = Hit(_source.Role, _target.Role, _skill);
            if (!await Animate(cue.Hold, t =>
            {
                float airborne = Mathf.Clamp01(t * cue.Hold / StrikeProfiles.ExecutionKnockbackTime);
                float distance = 1f - (1f - airborne) * (1f - airborne) * (1f - airborne);
                Vector3 lift = _up * (_size * 0.16f * Mathf.Sin(Mathf.PI * airborne));
                _target.Move(targetBeforeHit + direction * (_size * 0.76f * distance) + lift);
            })) return false;
            return await WaitForImpact();
        }

        private Color TrailColor => _show.Id == 885124
            ? new Color(1f, 0.77f, 0.42f)
            : new Color(0.97f, 0.50f, 0.47f);

        private Vector3 Grip(Vector3 forward) =>
            _source.Center + forward * (_size * 0.16f) - _up * (_size * 0.09f);

        private Vector3 Muzzle(Vector3 forward, bool finisher, bool raisedBlade = false)
        {
            Vector3? muzzle = ValennaAppearance.GunPoint(_source.Model.SpriteRender);
            if (muzzle.HasValue) return muzzle.Value;
            float reach = 0.56f;
            float height = StrikeProfiles.MuzzleHeight;
            if (finisher)
            {
                reach = 0.24f;
                height = StrikeProfiles.FinishMuzzleHeight;
            }
            else if (raisedBlade)
            {
                reach = 0.73f;
                height = StrikeProfiles.RaisedMuzzleHeight;
            }
            return _source.Center + (forward * reach + _up * height) * _size;
        }

        private void DrawBlade(float angle, float length, float alpha, bool reverse = false)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 forward = reverse ? -_forward : _forward;
            Vector3 direction = forward * Mathf.Cos(radians) + _up * Mathf.Sin(radians);
            _effects.DrawBlade(Grip(forward), direction, length, alpha);
        }

        private static float FrameStep => Mathf.Min(Time.deltaTime, 0.05f) * StrikeProfiles.PlaybackRate;

        private void UpdateDisplayedPose()
        {
            if (!CanAnimate()) return;
            SpriteRenderer body = _source.Model.SpriteRender;
            Sprite current = body.sprite;
            if (current == null) return;
            if (_stepAction != null && _displayedPose != current &&
                _source.Model.SpriteAnim?.CurrentPlayAnimKey == _stepAction)
            {
                float step = StrikeFootwork.Step(_stepAction, _displayedPose?.name, current.name) * _size;
                // 留出目标身前的站位，真正穿身的段落单独走自己的轨迹。
                float room = Vector3.Dot(_target.World - _source.World, _forward) - _size * 0.18f;
                step = Mathf.Min(step, Mathf.Max(0f, room));
                if (step != 0f) _source.Move(_source.World + _forward * step);
            }
            _displayedPose = current;
            _effects.SampleAttackPose(body, TrailColor);
        }

        private async Task<bool> Animate(float duration, Action<float> frame = null)
        {
            if (!CanAnimate()) return false;
            float elapsed = 0f;
            _effects.Advance(0f, frame);
            while (elapsed < duration)
            {
                await new WaitForEndOfFrame();
                if (!CanAnimate()) return false;
                float step = FrameStep;
                elapsed += step;
                _effects.Advance(step, frame, Mathf.Clamp01(elapsed / duration));
            }
            return true;
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        private static bool TryPose(RoleModel model, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!model.CheckSpriteAnimExist(key)) continue;
                float duration = model.PlaySpriteAnim(key, StrikeProfiles.PlaybackRate);
                PlaySpriteAnimation anim = model.SpriteAnim;
                SpriteAnimationData clip = anim?.CurrentAnimation;
                if (duration > 0f && clip != null && !clip.IsUseOldMode && clip.SpriteDatas?.Count > 1)
                {
                    // 原生 Update 先计时再显示。短起势至少留给当前帧率一次绘制机会。
                    anim.CurDuration = Mathf.Max(anim.CurDuration, FrameStep);
                }
                return true;
            }
            return false;
        }

        private static async Task Hit(BattleRole actor, BattleRole target, BattleSkillData skill)
        {
            DamageData damage = skill.Config.CustomDamage?.Clone();
            if (damage == null) return;

            var additional = new DamageAdditionalData
            {
                SourceType = EDamageSourceType.BattleSkill,
                DiceResult = EDiceResult.Success,
                EnableChangeDamageValue = true
            };
            var source = new DamageSourceRecord
            {
                Source = actor,
                SourceType = DamageSourceRecord.EDamageSourceType.BattleSkill,
                BattleSkill = skill
            };
            var log = new BattleSkillEffectLogData
            {
                LogType = EBattleLogType.BattleSkill,
                EffectType = EBattleLogEffectType.Damage
            };
            int points = BattleHelper.CalculationDamage(damage, actor, target, additional);
            await target.OnHit(actor, Mathf.Max(points, 0), damage, additional, source, log);
        }

        private sealed class Actor
        {
            internal readonly BattleRole Role;
            internal readonly RoleModel Model;
            internal readonly Vector3 OriginalWorld;
            private readonly Transform _parent;
            private readonly Vector3 _originalLocal;
            private readonly int _slot;
            private readonly RoleData _data;

            internal Actor(BattleRole role)
            {
                Role = role;
                _data = role.Data;
                Model = role.Model;
                Transform root = Model.transform;
                _parent = root.parent;
                _originalLocal = root.localPosition;
                _slot = role.PositionInBattle;
                OriginalWorld = root.position;
            }

            internal bool IsCurrent => Model != null && Role.Model == Model && ReferenceEquals(Role.Data, _data) &&
                Model.transform.parent == _parent && Role.PositionInBattle == _slot;
            internal Vector3 World => Model.transform.position;
            internal Vector3 Center => Model.Trans_Center != null ? Model.CenterPos : Model.SpriteRender.bounds.center;

            internal void Move(Vector3 position)
            {
                if (IsCurrent) Model.transform.position = position;
            }

            internal void Restore()
            {
                if (IsCurrent) Model.transform.localPosition = _originalLocal;
            }
        }
    }
}
