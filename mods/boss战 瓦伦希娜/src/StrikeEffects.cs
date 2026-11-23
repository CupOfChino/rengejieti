using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ValennaBattle
{
    internal sealed partial class StrikeEffects : IDisposable
    {
        private static readonly Color Core = new Color(1f, 0.93f, 0.72f);
        private static readonly Color Heat = new Color(1f, 0.43f, 0.12f);
        private static readonly Color Ember = new Color(0.66f, 0.11f, 0.27f);
        private static readonly Color Echo = new Color(0.48f, 0.33f, 0.82f);

        private readonly GameObject _root;
        private readonly Material _strokeMaterial;
        private readonly Material _ribbonMaterial;
        private readonly Material _ribbonGlowMaterial;
        private readonly int _layer;
        private readonly int _sortLayer;
        private readonly int _sortOrder;
        private readonly Vector3 _right;
        private readonly Vector3 _up;
        private readonly Quaternion _viewRotation;
        private readonly float _facing;
        private readonly float _height;
        private readonly List<TimedEffect> _active = new List<TimedEffect>();
        private readonly List<BladeSurface> _surfaces = new List<BladeSurface>();
        private readonly BladeSurface _solidBlade;
        private readonly LineRenderer _bladeRim;
        private readonly LineRenderer _bladeCore;
        private readonly LineRenderer _guard;
        private PoseSnapshot _previous;
        private int _travelFrames;
        private Action<float> _drawForetell;
        private float _foretellAge;
        private float _foretellOpacity = 1f;

        internal bool HasStrokeMaterial => _strokeMaterial != null;
        internal bool HasRibbon => _ribbonMaterial != null;
        internal bool HasTextured => StrikeArt.Sweep != null;

        internal StrikeEffects(Transform scene, SpriteRenderer body, Vector3 right, Transform view,
            float height, bool useRibbon)
        {
            _root = new GameObject("ValennaStrike");
            _root.transform.SetParent(scene, false);
            _layer = body.gameObject.layer;
            _sortLayer = body.sortingLayerID;
            _sortOrder = body.sortingOrder;
            _right = right;
            _up = view.up;
            _viewRotation = view.rotation;
            _facing = Vector3.Dot(right, view.right) < 0f ? -1f : 1f;
            _height = height;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null || !shader.isSupported) return;

            _strokeMaterial = new Material(shader);
            if (HasTextured) return;
            if (useRibbon)
                _ribbonMaterial = CreateRibbonMaterial();
            if (_ribbonMaterial != null)
            {
                _ribbonGlowMaterial = new Material(_ribbonMaterial);
                _ribbonGlowMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
                _ribbonGlowMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
                _ribbonGlowMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
                _solidBlade = NewSurface("武器刀身", 4, 4);
            }
            else
            {
                _bladeRim = NewStroke("刀身外缘", 2, 2);
                _bladeCore = NewStroke("刀锋", 2, 3);
            }
            _guard = NewStroke("护手", 2, 4);
        }

        internal void DrawBlade(Vector3 grip, Vector3 direction, float extent, float opacity)
        {
            if (_strokeMaterial == null || HasTextured) return;
            float length = _height * extent;
            Vector3 normal = Vector3.Cross(Vector3.Cross(_right, _up), direction);
            if (_solidBlade != null)
                DrawSolidBlade(grip, direction, normal, length, opacity);
            else
            {
                Vector3 tip = grip + direction * (length * 0.94f);
                Segment(_bladeRim, grip, tip, _height * 0.084f, _height * 0.023f, Heat, opacity * 0.78f);
                Segment(_bladeCore, grip, tip, _height * 0.033f, _height * 0.007f, Core, opacity);
            }
            Segment(_guard, grip - normal * (length * 0.10f), grip + normal * (length * 0.10f),
                _height * 0.045f, _height * 0.025f, Ember, opacity);
        }

        private static readonly float[] WeaponStations = { 0f, 0.18f, 0.70f, 0.96f };
        private static readonly float[] WeaponWidths = { 0.45f, 1f, 0.61f, 0.01f };

        private void DrawSolidBlade(Vector3 grip, Vector3 direction, Vector3 normal, float length, float opacity)
        {
            Transform blade = _solidBlade.Node.transform;
            blade.position = grip;
            Vector3 localDirection = blade.InverseTransformVector(direction * length);
            Vector3 localWidth = blade.InverseTransformVector(normal * (Mathf.Min(_height, length) * 0.065f));
            for (int row = 0; row < WeaponStations.Length; row++)
            {
                for (int column = 0; column < BladeRibbonProfile.Bands.Length; column++)
                {
                    BladeBand band = BladeRibbonProfile.Bands[column];
                    Vector3 vertex = localDirection * WeaponStations[row] + localWidth * (WeaponWidths[row] * band.Offset);
                    _solidBlade.Set(row, column, vertex, BandColor(band, opacity));
                }
            }
            _solidBlade.Upload();
        }

        internal void SamplePose(SpriteRenderer body, Color tint, bool leaveEcho = true)
        {
            if (body == null || body.sprite == null) return;
            PoseSnapshot prior = _previous;
            _previous = new PoseSnapshot(body);
            if (leaveEcho && prior.Sprite != null && prior.Sprite != _previous.Sprite)
                LeavePoseEcho(prior, tint, false);
        }

        internal void SampleAttackPose(SpriteRenderer body, Color tint)
        {
            if (body == null || body.sprite == null) return;
            PoseSnapshot prior = _previous;
            _previous = new PoseSnapshot(body);
            if (prior.Sprite == null) return;
            bool changed = prior.Sprite != _previous.Sprite;
            float epsilon = _height * 0.0001f;
            bool inMotion = (_previous.Position - prior.Position).sqrMagnitude > epsilon * epsilon;
            _travelFrames = inMotion ? Mathf.Min(_travelFrames + 1, 5) : 0;
            if (!changed && _travelFrames < 5) return;

            // 换姿留刚离开的那一张，持续移动则定格当下这一张，两者每帧最多留一影。
            LeavePoseEcho(changed ? prior : _previous, tint, !changed, attack: true);
            _travelFrames = 0;
        }

        private void LeavePoseEcho(PoseSnapshot prior, Color tint, bool moving, bool attack = false)
        {
            if (ValennaPoses.IsIdleFrame(prior.Sprite.name) &&
                !(attack && moving && prior.Sprite.name == "valenna_pose_idle")) return;

            Sprite mask = StrikeArt.Silhouette(prior.Sprite);
            if (mask == null) return;
            SpriteRenderer image = NewSprite(moving ? "位移残影" : "上一姿势单色残影", mask, -1);
            Transform node = image.transform;
            node.SetPositionAndRotation(prior.Position, prior.Rotation);
            Transform parent = node.parent;
            node.localScale = new Vector3(
                parent.InverseTransformVector(prior.Rotation * Vector3.right * prior.Scale.x).magnitude,
                parent.InverseTransformVector(prior.Rotation * Vector3.up * prior.Scale.y).magnitude,
                1f);
            image.flipX = prior.FlipX;
            image.flipY = prior.FlipY;
            Add(image.gameObject, moving ? 0.30f : 0.13f, t =>
            {
                if (!attack) node.position = prior.Position - _right * (_height * 0.085f * t);
                float fade = 1f - t;
                image.color = Tint(tint, moving ? 0.36f * fade : 0.44f * fade * fade);
            });
        }

        private readonly struct PoseSnapshot
        {
            internal readonly Sprite Sprite;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly Vector3 Scale;
            internal readonly bool FlipX;
            internal readonly bool FlipY;

            internal PoseSnapshot(SpriteRenderer body)
            {
                Sprite = body.sprite;
                Position = body.transform.position;
                Rotation = body.transform.rotation;
                Scale = body.transform.lossyScale;
                FlipX = body.flipX;
                FlipY = body.flipY;
            }
        }

        internal void BeginSweep(StrikeCue cue, Vector3 center, float startAngle, float endAngle, float age, bool execution)
        {
            if (HasTextured)
            {
                if (cue.Pose == "valenna_cut_crosscut")
                {
                    Vector3 origin = center + (_right * 0.06f - _up * 0.06f) * _height;
                    float span = StrikeProfiles.SweepSpan * 1.07f;
                    PlayFrames("切割首斩外圈剑气", StrikeArt.CuttingWind, () => origin,
                        span, span, -11f, -age, 2, Color.white);
                }
                SweepShape shape = cue.SweepStyle ?? StrikeProfiles.Shape(cue.Beat);
                Sprite[] mainBlade = execution ? StrikeArt.ExecutionSweep : cue.Pose switch
                {
                    "valenna_elephant_prepare" or "valenna_bullet_recoil" => StrikeArt.Sweep,
                    "valenna_cut_crosscut" or "valenna_cut_rise" or "valenna_deer_open" or "valenna_deer_raise"
                        or "valenna_elephant_sweep" => StrikeArt.PeakSweep,
                    _ when cue.TwinThrust => StrikeArt.TwinThrustSweep,
                    _ => StrikeArt.AttackSweep
                };
                SweepSprite(SweepOrigin(center, shape), shape, cue.Scale, Color.white, -age,
                    tailExtension: cue.PauseAfter ? StrikeProfiles.BeatPause : 0f,
                    frames: mainBlade);
                if (cue.CompanionSweep is SweepShape companion)
                {
                    SweepSprite(SweepOrigin(center, companion), companion, cue.Scale,
                        new Color(1f, 1f, 1f, 0.93f), cue.CompanionDelay - age, true,
                        frames: execution ? StrikeArt.ExecutionCompanion : StrikeArt.AttackCompanion);
                }
                return;
            }
            if (!HasRibbon) return;
            float spread = SlashSpread(cue.Beat);
            for (int section = 0; section < BladeRibbonProfile.SectionCount; section++)
            {
                float start = SectionAngle(startAngle, endAngle, section);
                float end = SectionAngle(startAngle, endAngle, section + 0.88f);
                RibbonArc(center, start, end, spread, section * BladeRibbonProfile.SectionDelay);
            }
        }

        private Vector3 SweepOrigin(Vector3 body, SweepShape shape) =>
            body + (_right * shape.Forward + _up * shape.Elevation) * _height;

        private static float SectionAngle(float first, float last, float section) =>
            Mathf.Lerp(first, last, section / BladeRibbonProfile.SectionCount);

        private static float SlashSpread(StrikeBeat beat) => beat == StrikeBeat.Rising ? 1.10f : 1.20f;

        internal void Impact(StrikeCue cue, Vector3 origin, Vector3 contact, float startAngle, float endAngle,
            Vector3? sweepCenter, Vector3 direction, bool leaveScar)
        {
            if (HasTextured)
            {
                TexturedImpact(cue, origin, contact, direction, leaveScar);
                return;
            }
            if (_strokeMaterial == null) return;
            StrikeBeat beat = cue.Beat;
            if (IsStraight(beat))
            {
                Streak("枪刃闪光", origin + direction * (_height * 0.18f), contact + direction * (_height * 0.32f),
                    _height * 0.16f, Core, 0.14f, 5);
                Shockwave(contact + direction * (_height * 0.14f), Heat, 0.31f, 1.16f);
                Shockwave(contact, Echo, 0.24f, 0.81f, delay: 0.048f);
            }
            else
            {
                Vector3 center = sweepCenter ?? contact - _right * (_height * 0.24f);
                float spread = SlashSpread(beat);
                Color trailingColor = beat == StrikeBeat.Crosscut ? Echo : Ember;
                if (_ribbonMaterial == null)
                {
                    for (int section = 0; section < BladeRibbonProfile.SectionCount; section++)
                    {
                        float start = SectionAngle(startAngle, endAngle, section);
                        float end = SectionAngle(startAngle, endAngle, section + 0.88f);
                        float delay = section * 0.022f;
                        Arc(center, start, end, spread, Core, 0.086f, 0.17f, delay, 4);
                        Arc(center, start - 2f, end + 3f, spread * 0.97f, Heat, 0.225f, 0.27f, delay + 0.012f, 3);
                    }
                }
                Arc(center, startAngle - 14f, endAngle - 3f, spread * 0.86f,
                    trailingColor, 0.12f, 0.32f, 0.064f, 2);
                Arc(center - _right * (_height * 0.05f), startAngle + 5f, endAngle + 9f, spread * 1.02f,
                    Ember, 0.068f, 0.35f, 0.072f, 2);
                Arc(center - _right * (_height * 0.09f), Mathf.Lerp(startAngle, endAngle, 0.34f),
                    endAngle + 8f, spread * 1.12f, Heat, 0.041f, 0.33f, 0.096f, 1);
            }

            int count = beat == StrikeBeat.Muzzle ? 8 : 5;
            for (int i = 0; i < count; i++)
            {
                float height = (i - (count - 1) * 0.5f) * 0.14f;
                float speed = 0.26f + (i % 3) * 0.11f;
                Vector3 drift = (_right * speed + _up * height) * _height;
                Spark(contact + _up * (height * _height * 0.17f), drift, i % 2 == 0 ? Heat : Core,
                    0.028f + (i % 3) * 0.016f);
            }
        }

        internal static bool IsStraight(StrikeBeat beat) =>
            beat == StrikeBeat.Thrust || beat == StrikeBeat.Muzzle || beat == StrikeBeat.Volley;

        internal void Prime(SpriteRenderer body, StrikeOpenings opening)
        {
            if (!HasTextured) return;
            float eyeSpeed = StrikeArt.Eye.Length * StrikeProfiles.SweepInterval / opening.EyeDuration;
            PlayFrames("左眼红光", StrikeArt.Eye, () => ValennaAppearance.LightPoint(body, true),
                StrikeProfiles.EyeWidth, StrikeProfiles.EyeHeight, 0f, 0f, 8, Color.white, eyeSpeed);
            if (opening.SheathAt < 0f) return;
            float sheathDuration = StrikeArt.Sheath.Length * StrikeProfiles.SweepInterval;
            float sheathSpeed = sheathDuration / Mathf.Min(sheathDuration, opening.Duration - opening.SheathAt);
            PlayFrames("剑鞘口金光", StrikeArt.Sheath, () => ValennaAppearance.LightPoint(body, false),
                StrikeProfiles.SheathWidth, StrikeProfiles.SheathHeight, 0f, opening.SheathAt, 8, Color.white, sheathSpeed);
        }

        internal void ExecutionOpening(Vector3 center, StrikeOpenings opening)
        {
            if (!HasTextured) return;
            Vector3 origin = center + (_right * 0.025f + _up * 0.08f) * _height;
            PlayFrames("处置起手剑浪", StrikeArt.ExecutionWave, () => origin, StrikeProfiles.SweepSpan,
                StrikeProfiles.SweepSpan, StrikeProfiles.ExecutionWaveAngle, Mathf.Max(0f, opening.BackAt - 0.08f), 3, Color.white,
                tailExtension: StrikeProfiles.ExecutionOpeningTail);
            float duration = StrikeProfiles.ExecutionOpeningWindDuration;
            Vector3 outer = origin + _up * (_height * 0.04f);
            PlayFrames("处置停势外圈余风", StrikeArt.CuttingWind, () => outer, 4.55f, 4.03f, 154f,
                Mathf.Max(0f, opening.Duration - StrikeProfiles.ExecutionOpeningWindLead), 1,
                new Color(1f, 0.83f, 0.82f, 0.63f), StrikeArt.CuttingWind.Length * StrikeProfiles.SweepInterval / duration);
        }

        internal void ApproachWind(Func<Vector3> center, float delay, float duration)
        {
            if (!HasTextured) return;
            PlayFrames("移步浅风", StrikeArt.Wake, () => center() - _right * (_height * 0.29f),
                2.55f, 0.53f, 4f, delay, -1, new Color(1f, 0.78f, 0.76f, 0.49f),
                StrikeArt.Wake.Length * StrikeProfiles.SweepInterval / duration);
        }

        internal void LeadCue(StrikeCue cue, Func<Vector3> center, float untilPose, bool reverse = false, bool flash = true)
        {
            if (!HasTextured) return;
            bool straight = IsStraight(cue.Beat);
            float windLead = Mathf.Min(untilPose, StrikeProfiles.SweepPoseLead);
            Sprite[] wind = straight ? StrikeArt.Wake : StrikeArt.Companion;
            float scale = Mathf.Lerp(1f, cue.Scale, 0.52f);
            float facing = reverse ? -1f : 1f;
            Vector3 direction = _right * facing;
            float angle = straight ? 5f * facing : 0.48f * (cue.SweepStyle ?? StrikeProfiles.Shape(cue.Beat)).Angle;
            PlayFrames("换姿前浅风", wind, () => center() - direction * (_height * 0.13f) - _up * (_height * 0.06f),
                (straight ? 2.32f : 2.40f) * scale, (straight ? 0.38f : 1.55f) * scale,
                angle, untilPose - windLead, -1, new Color(1f, 0.72f, 0.67f, straight ? 0.46f : 0.34f),
                wind.Length * StrikeProfiles.SweepInterval / (windLead + 0.19f), reverse: reverse);

            if (!flash || cue.MotionFlashFrames <= 0) return;
            float flashLead = Mathf.Min(untilPose, cue.MotionFlashFrames * StrikeProfiles.SweepInterval);
            float delay = untilPose - flashLead;
            float speed = StrikeArt.Sheath.Length * StrikeProfiles.SweepInterval / (flashLead + 0.08f);
            Vector3 blade = direction * (_height * 0.31f) - _up * (_height * 0.10f);
            PlayFrames("刃口短金闪", StrikeArt.Sheath, () => center() + blade,
                1.32f * scale, 0.41f * scale, (straight ? 66f : -36f) * facing,
                delay, 6, new Color(1f, 0.96f, 0.84f, 0.93f), speed, reverse: reverse);
            if (cue.MotionFlashFrames >= 3)
                PlayFrames("刃前交错金线", StrikeArt.Sheath, () => center() + blade + _up * (_height * 0.075f),
                    0.92f * scale, 0.25f * scale, (straight ? 18f : 39f) * facing,
                    delay + 0.022f, 6, new Color(1f, 0.77f, 0.57f, 0.63f), speed * 1.19f, reverse: reverse);
        }

        internal void BraceWind(Vector3 center, bool reverse = false)
        {
            if (!HasTextured) return;
            var curl = new SweepShape(reverse ? 63f : -36f, 0.68f, 0.52f, elevation: -0.08f,
                mirrorX: !reverse, forward: reverse ? -0.11f : 0.06f, travel: reverse ? -0.16f : 0.12f,
                turn: reverse ? 15f : -12f, speed: 1.14f);
            SweepSprite(SweepOrigin(center, curl), curl, 1f, new Color(0.90f, 0.37f, 0.30f, 0.49f),
                reverse ? 0f : 0.018f, companion: true);
        }

        internal void ExecutionInsert(Vector3 body, Vector3 contact, float scale)
        {
            if (!HasTextured) return;
            Vector3 center = Vector3.Lerp(body, contact, 0.37f);
            var swing = new SweepShape(-46f, 0.94f, 0.68f, reverse: true, mirrorX: true, elevation: -0.06f,
                forward: -0.03f, travel: -0.36f, turn: 41f, motionTime: 0.16f, speed: 1.76f);
            SweepSprite(SweepOrigin(center, swing), swing, scale, new Color(1f, 0.88f, 0.83f, 0.93f),
                StrikeProfiles.ExecutionInsertPoseTime - StrikeProfiles.SweepPoseLead, frames: StrikeArt.ExecutionSweep);
            Vector3 wake = Vector3.Lerp(body, contact, 0.59f);
            PlayFrames("平剑前送余光", StrikeArt.Wake, () => wake, 3.15f * scale, 0.93f * scale,
                4f, StrikeProfiles.ExecutionInsertPoseTime - 0.02f, 3, new Color(1f, 0.66f, 0.62f, 0.81f), 1.39f, reverse: true);
        }

        internal void LongThrustWind(Vector3 center, float scale, float age)
        {
            if (!HasTextured) return;
            var wind = new SweepShape(52f, 1.02f, 0.79f, mirrorX: true, elevation: 0.12f,
                forward: -0.17f, travel: 0.39f, turn: 19f, motionTime: 0.17f, speed: 1.85f);
            SweepSprite(SweepOrigin(center, wind), wind, scale, new Color(1f, 0.66f, 0.60f, 0.86f), -age,
                tailExtension: StrikeProfiles.BeatPause);
        }

        internal void PinBlade(SpriteRenderer body, Vector3 contact, float scale)
        {
            if (!HasTextured) return;
            float speed = StrikeArt.Aim.Length * StrikeProfiles.SweepInterval / StrikeProfiles.BeatPause;
            PlayFrames("刺入后枪口蓄光", StrikeArt.Aim, () => ValennaAppearance.GunPoint(body),
                StrikeProfiles.MuzzleRingWidth * scale, StrikeProfiles.MuzzleRingHeight * scale, 0f, 0f, 8, Color.white, speed);
            Flash("剑尖刺入闪光", StrikeArt.Flare, contact, Vector3.zero, Core,
                0.54f * scale, 0.34f * scale, 0f, 0.21f, 0f, 8);
            Flash("剑尖微光", StrikeArt.HitLine, contact, Vector3.zero, new Color(1f, 0.59f, 0.41f, 0.73f),
                1.04f * scale, 0.044f * scale, 0f, StrikeProfiles.BeatPause, 0.03f, 7);
        }

        internal void ReleaseMuzzleRing(SpriteRenderer body, float scale)
        {
            if (!HasTextured) return;
            var ring = NewSprite("开火后枪口金环", StrikeArt.Aim[StrikeArt.Aim.Length - 1], 7);
            Add(ring.gameObject, StrikeProfiles.ExecutionKnockbackTime + StrikeProfiles.ExecutionStandTime, t =>
            {
                Vector3? muzzle = ValennaAppearance.GunPoint(body);
                ring.enabled = muzzle.HasValue;
                if (!muzzle.HasValue) return;
                Place(ring, muzzle.Value, StrikeProfiles.MuzzleRingWidth * scale, StrikeProfiles.MuzzleRingHeight * scale, 0f);
                ring.color = Tint(Color.white, 1f - Mathf.SmoothStep(0f, 1f, (t - 0.26f) / 0.74f));
            });
        }

        internal void SetForetellOpacity(float opacity) => _foretellOpacity = opacity;

        internal void Foretell(SpriteRenderer body)
        {
            if (!HasTextured || StrikeArt.ForeseenMark == null) return;
            SpriteRenderer mark = NewSprite("浮空的预知眼", StrikeArt.ForeseenMark, 9);
            SpriteRenderer halo = NewSprite("预知眼红晕", StrikeArt.Flare, 8);
            SpriteRenderer glare = NewSprite("左眼预知红线", StrikeArt.Eye[0], 8);
            _drawForetell = age =>
            {
                Vector3 eye = ValennaAppearance.LightPoint(body, true) ??
                    body.bounds.center + _up * (0.27f * _height);
                float grow = Mathf.SmoothStep(0f, 1f, age / 0.085f);
                Vector3 facing = (_viewRotation * Vector3.right) * (body.flipX ? 1f : -1f);
                Vector3 point = eye + (facing * 0.55f + _up * 0.10f) * _height;
                const float size = StrikeProfiles.ForeseenMarkSize;
                mark.flipX = body.flipX;
                Place(mark, point, size, size, 0f);
                mark.color = Tint(Color.white, _foretellOpacity);
                Place(halo, point, size * 1.29f, size * 1.29f, 0f);
                halo.color = Tint(new Color(1f, 0.23f, 0.23f), 0.24f * _foretellOpacity);
                int index = Mathf.Min((int)(age / StrikeProfiles.SweepInterval), 5);
                glare.sprite = StrikeArt.Eye[index];
                Place(glare, eye, 2.20f, 0.30f, 0f);
                glare.color = Tint(Color.white, 0.90f * grow * _foretellOpacity);
            };
            _drawForetell(0f);
        }

        internal void ConfirmDodge(SpriteRenderer body)
        {
            if (body == null || !HasTextured) return;
            Vector3 center = body.bounds.center + (_right * 0.23f + _up * 0.14f) * _height;
            PlayFrames("预知闪避星芒", StrikeArt.Parry, () => center,
                1.25f, 1.25f, 0f, 0f, 8, Color.white, 1.62f);
        }

        internal void ElephantGather(Func<Vector3> grip, float duration)
        {
            if (!HasTextured) return;
            for (int i = 0; i < 7; i++)
            {
                float delay = 0.009f + i * 0.017f;
                float angle = -119f + i * 43f;
                float radians = angle * Mathf.Deg2Rad;
                float radius = _height * (0.64f + 0.14f * (i % 3));
                Vector3 outward = (_right * Mathf.Cos(radians) + _up * Mathf.Sin(radians)) * radius;
                float width = 0.43f + 0.14f * (i % 4);
                Color tint = i % 3 == 0 ? Core : new Color(1f, 0.63f, 0.28f);
                SpriteRenderer shard = NewSprite("解象蓄刺汇光", StrikeArt.Streak, 6);
                Add(shard.gameObject, duration - delay, t =>
                {
                    float age = delay + (duration - delay) * t;
                    float drawIn = Mathf.SmoothStep(0f, 1f, (age - 0.10f) / (duration - 0.10f));
                    float glow = Mathf.SmoothStep(0f, 1f, (age - delay) / 0.043f);
                    glow *= 1f - Mathf.SmoothStep(0f, 1f, (age - duration + 0.030f) / 0.030f);
                    Vector3 point = grip() + outward * (1f - 0.93f * drawIn);
                    Place(shard, point, Mathf.Lerp(width, 0.94f, drawIn), Mathf.Lerp(0.084f, 0.046f, drawIn),
                        Mathf.Lerp(angle + 30f, 2f, drawIn));
                    shard.color = Tint(tint, glow * 0.85f);
                }, delay);
            }
        }

        internal void ElephantRay(Vector3 origin, Vector3 contact, float scale, float age)
        {
            if (!HasTextured) return;
            Vector3 travel = contact - origin;
            float angle = Mathf.Atan2(Vector3.Dot(travel, _up), Mathf.Max(0.28f * _height, Vector3.Dot(travel, _right)))
                * Mathf.Rad2Deg;
            Vector3 center = Vector3.Lerp(origin, contact, 0.57f) + _right * (_height * 0.11f);
            Flash("解象直向橙刃", StrikeArt.Streak, center, _right * (_height * 0.26f),
                new Color(1f, 0.50f, 0.10f), 3.70f * scale, 0.23f * scale, angle, 0.24f, -age, 7);
            Flash("解象直刃亮沿", StrikeArt.Streak, center + _up * (_height * 0.033f), _right * (_height * 0.19f),
                new Color(1f, 0.78f, 0.46f), 3.25f * scale, 0.094f * scale, angle, 0.18f, 0.011f - age, 8);
        }

        internal void ElephantFinishWind(Vector3 body, Vector3 contact, float scale)
        {
            if (!HasTextured) return;
            Vector3 center = Vector3.Lerp(body, contact, 0.45f) - _up * (_height * 0.12f);
            var edge = new SweepShape(3f, 1.26f, 0.67f, reverse: true, mirrorX: true, elevation: -0.09f,
                forward: 0.04f, travel: 0.37f, lift: -0.08f, turn: 24f, motionTime: 0.12f, speed: 1.53f);
            SweepSprite(SweepOrigin(center, edge), edge, scale, new Color(1f, 0.68f, 0.32f, 0.95f), 0.005f,
                companion: true, frames: StrikeArt.AttackCompanion);
            float span = StrikeProfiles.SweepSpan * scale;
            float speed = StrikeArt.ElephantWind.Length * StrikeProfiles.SweepInterval / StrikeProfiles.ElephantWindDuration;
            PlayFrames("解象末斩逐层消散的剑风", StrikeArt.ElephantWind, () => center, span * 1.13f, span * 0.88f,
                11f, StrikeProfiles.ElephantWindDelay, 2, Color.white, speed);
        }

        internal void TwinThrust(Vector3 origin, Vector3 hit, float scale, float age)
        {
            if (!HasTextured) return;
            Vector3 center = Vector3.Lerp(origin, hit, 0.58f) + _right * (_height * 0.42f);
            PlayFrames("一长一短双刺", StrikeArt.TwinThrust, () => center,
                StrikeProfiles.TwinWidth * scale, StrikeProfiles.TwinHeight * scale,
                2f, -age, 8, Color.white);
        }

        internal void BeginStraight(StrikeCue cue, Vector3 origin, Vector3 hit, Vector3 direction, float age)
        {
            if (!HasTextured) return;
            float scale = cue.Scale;
            if (cue.SplitThrust)
            {
                Vector3 center = origin + direction * (_height * 0.12f);
                PlayFrames("处置三道突刺", StrikeArt.Pierce, () => center, 3.80f * scale, 1.45f * scale,
                    3f, -age, 8, Color.white);
            }
            else if (cue.LongThrust)
            {
                Vector3 center = Vector3.Lerp(origin, hit, 0.50f) + direction * (_height * 0.48f);
                PlayFrames("末次直刺", StrikeArt.LongThrust, () => center,
                    StrikeProfiles.LongThrustWidth * scale, StrikeProfiles.LongThrustHeight * scale,
                    -3f, -age, 8, Color.white);
            }
            else
                FiringLine(origin, hit, direction, scale, -age, cue.Beat != StrikeBeat.Thrust);
        }

        private void TexturedImpact(StrikeCue cue, Vector3 origin, Vector3 hit, Vector3 direction, bool leaveScar)
        {
            float scale = cue.Scale;
            float speed = cue.SweepStyle?.Speed ?? 1f;
            bool straight = IsStraight(cue.Beat);
            if (straight)
            {
                if (cue.SplitThrust)
                    Flash("突刺亮点", StrikeArt.Flare, hit, Vector3.zero, Core,
                        1.13f * scale, 0.60f * scale, 0f, 0.18f, 0.013f, 8);
                if (leaveScar) FiringLine(origin, hit, direction, scale, 0f, true);
                Vector3 wake = Vector3.Lerp(origin, hit, 0.37f) + direction * (_height * 0.04f);
                bool reverse = Vector3.Dot(direction, _right) < 0f;
                float wakeAngle = cue.Beat == StrikeBeat.Thrust ? -8f : -3f;
                PlayFrames("枪刃余光", StrikeArt.Wake, () => wake,
                    StrikeProfiles.WakeWidth * scale, StrikeProfiles.WakeHeight * scale,
                    reverse ? -wakeAngle : wakeAngle, StrikeProfiles.WakeDelay, 3, new Color(1f, 1f, 1f, 0.86f),
                    tailExtension: leaveScar ? StrikeProfiles.ExecutionStandTime
                        : cue.PauseAfter ? StrikeProfiles.BeatPause : 0f, reverse: reverse);
                if (cue.Beat == StrikeBeat.Volley && cue.Pose != "valenna_elephant_volley")
                {
                    FiringLine(origin + _up * (_height * 0.07f), hit + _up * (_height * 0.04f), direction, scale, 0.095f, true);
                    FiringLine(origin - _up * (_height * 0.06f), hit, direction, scale * 1.13f, 0.20f, true);
                    if (leaveScar)
                    {
                        Scar(hit, scale, -6f);
                        Scar(hit, scale * 0.85f, 79f);
                        ExecutionBlastWind(hit, scale);
                    }
                }
            }

            if (cue.Ring)
            {
                TargetRing(hit, new Color(1f, 0.77f, 0.15f), 0.42f / speed, 0.82f * scale, 0.030f);
                TargetRing(hit, Heat, 0.34f / speed, 0.49f * scale, 0.077f);
            }

            // 处置首刀有接触红线，举刀时再闪一次星点，伤害仍在后续段结算。
            bool openingCut = cue.Pose == "valenna_finish_turn";
            if (!cue.Damage && !cue.Ring && !openingCut) return;
            float tilt = cue.Beat == StrikeBeat.Orbit ? 21f : straight ? 5f : -4f;
            if (cue.Pose == "valenna_finish_turn_slash" || cue.Pose == "valenna_finish_spin") tilt = 33f;
            if (openingCut) tilt = 0f;
            if (cue.Damage || cue.PassDistance > 0f || openingCut)
            {
                float lineTime = openingCut ? 16f * StrikeProfiles.SweepInterval : 0.35f;
                HitStroke(hit, tilt, 2.28f * scale, scale, 0f, lineTime);
                HitStroke(hit, tilt + 87f, 1.77f * scale, scale * 0.86f, 0.018f, lineTime);
            }
            float strength = !cue.Damage ? 0.72f : 1f;
            PlayFrames("命中星点与散落碎屑", StrikeArt.Impact, () => hit, 2.36f * scale, 1.77f * scale,
                tilt, 0f, 8, Tint(Color.white, strength), speed, reverse: Vector3.Dot(direction, _right) < 0f);
            if (openingCut)
                PlayFrames("处置立剑星点", StrikeArt.Impact, () => hit, 1.84f * scale, 1.38f * scale,
                    12f, 7f * StrikeProfiles.SweepInterval, 8, Tint(Color.white, 0.68f), reverse: Vector3.Dot(direction, _right) < 0f);
        }

        private void ExecutionBlastWind(Vector3 contact, float scale)
        {
            var upper = new SweepShape(63f, 0.96f, 0.55f, mirrorX: true, elevation: 0.18f,
                forward: -0.18f, travel: -0.23f, lift: 0.14f, turn: -14f, speed: 1.10f);
            var lower = new SweepShape(-61f, 0.91f, 0.49f, reverse: true, elevation: -0.11f,
                forward: -0.25f, travel: -0.31f, lift: -0.11f, turn: 13f, speed: 1.07f);
            SweepSprite(SweepOrigin(contact, upper), upper, scale, new Color(1f, 0.91f, 0.70f, 0.86f), 0.096f,
                companion: true, tailExtension: StrikeProfiles.BeatPause);
            SweepSprite(SweepOrigin(contact, lower), lower, scale, new Color(0.82f, 0.26f, 0.24f, 0.48f), 0.17f,
                companion: true, tailExtension: StrikeProfiles.BeatPause);
        }

        private void HitStroke(Vector3 hit, float angle, float length, float scale, float delay, float duration)
        {
            SpriteRenderer line = NewSprite("靶点红色切痕", StrikeArt.HitLine, 7);
            Add(line.gameObject, duration, t =>
            {
                float reach = Mathf.Lerp(0.59f, 1f, Mathf.SmoothStep(0f, 1f, t * 5.3f));
                Place(line, hit, length * reach, scale * Mathf.Lerp(0.048f, 0.012f, t), angle);
                line.color = Tint(Color.white, 1f - Mathf.SmoothStep(0f, 1f, (t - 0.43f) / 0.57f));
            }, delay);
        }

        private void FiringLine(Vector3 origin, Vector3 hit, Vector3 direction, float scale, float delay, bool gunshot)
        {
            Vector3 travel = hit - origin;
            float horizontal = Vector3.Dot(travel, direction);
            float vertical = Vector3.Dot(travel, _up);
            float sign = Vector3.Dot(direction, _right) < 0f ? -1f : 1f;
            float angle = Mathf.Atan2(vertical, Mathf.Max(0.22f * _height, horizontal)) * Mathf.Rad2Deg * sign;
            Vector3 mid = Vector3.Lerp(origin, hit, 0.60f) + direction * (0.26f * _height);
            float width = Mathf.Max(1.70f, travel.magnitude / _height + 1.77f) * scale;
            PlayFrames(gunshot ? "铳剑开火" : "剑尖刺线", StrikeArt.Shot, () => mid,
                width * (gunshot ? 1.30f : 1.06f), (gunshot ? 0.70f : 0.43f) * scale,
                angle, delay, 6, Color.white, gunshot ? 1f : 1.26f, reverse: sign < 0f);
            if (gunshot)
                Flash("枪口星芒", StrikeArt.Flare, origin, Vector3.zero, Core, 0.46f * scale, 0.40f * scale,
                    angle, 0.12f, delay, 7);
        }

        private void Scar(Vector3 hit, float scale, float angle)
        {
            var scar = NewSprite("交叉刀痕", StrikeArt.HitLine, 6);
            Add(scar.gameObject, 0.62f, t =>
            {
                Place(scar, hit, (1.98f + 0.21f * t) * scale, (0.042f - 0.025f * t) * scale, angle);
                scar.color = Tint(new Color(0.86f, 0.58f, 0.49f), 0.88f * (1f - t * t));
            }, 0.22f);
        }

        private void SweepSprite(Vector3 center, SweepShape shape, float scale, Color color, float delay,
            bool companion = false, float tailExtension = 0f, Sprite[] frames = null, Func<Vector3> startAt = null)
        {
            frames ??= companion ? StrikeArt.Companion : StrikeArt.Sweep;
            SpriteRenderer image = NewSprite(companion ? "错时副刃" : "流动刀光与余弧", frames[0], companion ? 4 : 3);
            image.flipX = image.flipX != shape.MirrorX;
            image.flipY = shape.Reverse;
            image.color = color;
            float span = StrikeProfiles.SweepSpan * scale;
            float duration = frames.Length * StrikeProfiles.SweepInterval / shape.Speed;
            Vector3 drift = (_right * shape.Travel + _up * shape.Lift) * _height;
            Place(image, center, span * shape.Width, span * shape.Height, shape.Angle);
            Add(image.gameObject, duration + tailExtension, t =>
            {
                if (startAt != null)
                {
                    center = startAt();
                    startAt = null;
                }
                image.sprite = frames[FrameIndex(t, frames.Length, duration, tailExtension)];
                float eased = Mathf.SmoothStep(0f, 1f, t * (duration + tailExtension) / shape.MotionTime);
                Place(image, center + drift * eased, span * shape.Width, span * shape.Height, shape.Angle + shape.Turn * eased);
            }, delay);
        }

        private void PlayFrames(string name, Sprite[] frames, Func<Vector3?> point,
            float width, float height, float angle, float delay, int order, Color color, float speed = 1f,
            float tailExtension = 0f, bool reverse = false)
        {
            var image = NewSprite(name, frames[0], order);
            image.color = color;
            if (reverse) image.flipX = !image.flipX;
            float duration = frames.Length * StrikeProfiles.SweepInterval / speed;
            Add(image.gameObject, duration + tailExtension, t =>
            {
                Vector3? position = point();
                image.enabled = position.HasValue;
                if (!position.HasValue) return;
                image.sprite = frames[FrameIndex(t, frames.Length, duration, tailExtension)];
                Place(image, position.Value, width, height, angle);
            }, delay);
        }

        private static int FrameIndex(float progress, int count, float duration, float tailExtension)
        {
            if (tailExtension <= 0f) return Mathf.Min((int)(progress * count), count - 1);
            float age = progress * (duration + tailExtension);
            // 亮刃按原速扫过，延长后半的红色余风。
            float tailStart = duration * 0.56f;
            float tail = duration - tailStart;
            if (age > tailStart) age = tailStart + (age - tailStart) * tail / (tail + tailExtension);
            return Mathf.Min((int)(age / duration * count), count - 1);
        }

        private void Flash(string name, Sprite sprite, Vector3 center, Vector3 drift, Color color,
            float width, float height, float angle, float duration, float delay, int order)
        {
            SpriteRenderer image = NewSprite(name, sprite, order);
            Add(image.gameObject, duration, t =>
            {
                float grow = 0.73f + Mathf.Sqrt(t) * 0.43f;
                Place(image, center + drift * t, width * grow, height * grow, angle);
                image.color = Tint(color, color.a * (1f - t) * (1f - t));
            }, delay);
        }

        private SpriteRenderer NewSprite(string name, Sprite art, int order)
        {
            var sprite = NewObject(name).AddComponent<SpriteRenderer>();
            sprite.sprite = art;
            sprite.flipX = _facing < 0f;
            sprite.sortingLayerID = _sortLayer;
            sprite.sortingOrder = _sortOrder + order;
            if (_strokeMaterial != null) sprite.sharedMaterial = _strokeMaterial;
            return sprite;
        }

        private void Place(SpriteRenderer image, Vector3 position, float width, float height, float angle)
        {
            Transform node = image.transform;
            Quaternion rotation = _viewRotation * Quaternion.AngleAxis(angle * _facing, Vector3.forward);
            node.SetPositionAndRotation(position, rotation);
            Vector3 x = node.parent.InverseTransformVector(rotation * Vector3.right);
            Vector3 y = node.parent.InverseTransformVector(rotation * Vector3.up);
            node.localScale = new Vector3(x.magnitude * _height * width,
                y.magnitude * _height * height / image.sprite.bounds.size.y, 1f);
        }

        private void TargetRing(Vector3 center, Color color, float duration, float spread, float delay)
        {
            if (!HasStrokeMaterial) return;
            const int segments = 31;
            LineRenderer ring = NewStroke("金色环形气浪", segments, 5);
            Add(ring.gameObject, duration, t =>
            {
                float radius = _height * spread * Mathf.Lerp(0.24f, 0.65f, Mathf.Sqrt(t));
                for (int i = 0; i < segments; i++)
                {
                    float angle = Mathf.PI * 2f * i / (segments - 1);
                    ring.SetPosition(i, center + (_right * (Mathf.Cos(angle) * 0.76f) + _up * Mathf.Sin(angle)) * radius);
                }
                float width = _height * Mathf.Lerp(0.032f, 0.009f, t);
                SetStyle(ring, width, width, color, (1f - t) * 0.85f);
            }, delay);
        }

        private void RibbonArc(Vector3 center, float startAngle, float endAngle, float radius, float delay)
        {
            BladeSurface surface = NewSurface("分节主刀光", BladeRibbonProfile.Samples, 4);
            BladeSurface glow = NewSurface("刀光外缘柔光", BladeRibbonProfile.Samples, 5, _ribbonGlowMaterial);
            Transform node = surface.Node.transform;
            node.position = center;
            glow.Node.transform.SetParent(node, false);
            Vector3 horizontal = node.InverseTransformVector(_right * 0.84f);
            Vector3 vertical = node.InverseTransformVector(_up * 0.65f);
            Add(surface.Node, BladeRibbonProfile.Duration, phase =>
            {
                float reveal = BladeRibbonProfile.Reveal(phase * BladeRibbonProfile.Duration);
                float expanded = _height * radius * BladeRibbonProfile.Expand(phase);
                for (int row = 0; row < BladeRibbonProfile.Samples; row++)
                {
                    float along = row / (BladeRibbonProfile.Samples - 1f);
                    float angle = Mathf.Lerp(startAngle, endAngle, along * reveal) * Mathf.Deg2Rad;
                    Vector3 radial = horizontal * Mathf.Cos(angle) + vertical * Mathf.Sin(angle);
                    float width = _height * 0.27f * BladeRibbonProfile.Width(along, phase);
                    float alpha = BladeRibbonProfile.Brightness(along, phase);
                    for (int column = 0; column < BladeRibbonProfile.Bands.Length; column++)
                    {
                        BladeBand band = BladeRibbonProfile.Bands[column];
                        surface.Set(row, column, radial * (expanded + width * band.Offset), BandColor(band, alpha));
                        float outer = expanded + width * (band.Offset * BladeRibbonProfile.GlowWidth + 0.20f);
                        glow.Set(row, column, radial * outer, BandColor(band, alpha * BladeRibbonProfile.GlowStrength));
                    }
                }
                surface.Upload();
                glow.Upload();
            }, delay);
        }

        private void Arc(Vector3 center, float startAngle, float endAngle, float radius, Color color,
            float width, float duration, float delay, int order)
        {
            const int segments = 13;
            LineRenderer line = NewStroke("斩击轨迹", segments, order);
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.09f), new Keyframe(0.32f, 0.82f),
                new Keyframe(0.65f, 1f), new Keyframe(1f, 0.05f));
            Add(line.gameObject, duration, t =>
            {
                float reach = Mathf.Lerp(0.80f, 1.23f, Mathf.Sqrt(t));
                float growth = Mathf.Clamp01(t * 5f + 0.27f);
                float scale = _height * radius * reach;
                for (int j = 0; j < segments; j++)
                {
                    float s = j / (segments - 1f);
                    float angle = Mathf.Lerp(startAngle, Mathf.Lerp(startAngle, endAngle, growth), s) * Mathf.Deg2Rad;
                    line.SetPosition(j, center + (_right * (Mathf.Cos(angle) * 0.84f) +
                        _up * (Mathf.Sin(angle) * 0.65f)) * scale);
                }
                line.widthMultiplier = _height * width * (1f - t * 0.78f);
                Fade(line, color, (1f - t) * 0.90f);
            }, delay);
        }

        private void Shockwave(Vector3 center, Color color, float duration, float spread, float delay = 0f)
        {
            const int segments = 23;
            LineRenderer ring = NewStroke("冲击波", segments, 1);
            Add(ring.gameObject, duration, t =>
            {
                float radius = _height * spread * Mathf.Lerp(0.14f, 0.65f, Mathf.Sqrt(t));
                for (int j = 0; j < segments; j++)
                {
                    float angle = j / (segments - 1f) * Mathf.PI * 2f;
                    ring.SetPosition(j, center + _right * (t * _height * 0.18f) +
                        (_right * (Mathf.Cos(angle) * 0.70f) + _up * Mathf.Sin(angle)) * radius);
                }
                float width = _height * Mathf.Lerp(0.082f, 0.014f, t);
                SetStyle(ring, width, width, color, (1f - t) * 0.68f);
            }, delay);
        }

        private void Streak(string name, Vector3 a, Vector3 b, float width, Color color, float duration, int order)
        {
            LineRenderer line = NewStroke(name, 2, order);
            Add(line.gameObject, duration, t => Segment(line, a, b, (1f - t) * width,
                (1f - t) * width * 0.24f, color, 1f - t));
        }

        private void Spark(Vector3 initial, Vector3 drift, Color color, float delay)
        {
            LineRenderer spark = NewStroke("火星", 2, 4);
            Add(spark.gameObject, 0.28f, t =>
            {
                Vector3 position = initial + drift * (0.08f + t) - _up * (_height * 0.15f * t * t);
                Segment(spark, position, position + drift * (0.20f * (1f - t)), _height * 0.028f,
                    0f, color, 1f - t);
            }, delay);
        }

        internal void Advance(float delta, Action<float> drawPose = null, float poseProgress = 0f)
        {
            int existing = _active.Count;
            drawPose?.Invoke(poseProgress);
            _foretellAge += delta;
            _drawForetell?.Invoke(_foretellAge);
            // 本帧生成的刀光已带起扫时的年龄。这里推进已在场的挂件，让它们读取移动后的锚点。
            for (int i = existing - 1; i >= 0; i--)
            {
                TimedEffect effect = _active[i];
                effect.Age += delta;
                if (effect.Age < 0f) continue;
                if (effect.Age >= effect.Duration)
                {
                    effect.Object.SetActive(false);
                    Object.Destroy(effect.Object);
                    _active.RemoveAt(i);
                }
                else
                {
                    effect.Draw(effect.Age / effect.Duration);
                    if (!effect.Object.activeSelf) effect.Object.SetActive(true);
                }
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                _root.SetActive(false);
                Object.Destroy(_root);
            }
            foreach (BladeSurface surface in _surfaces) surface.Dispose();
            if (_strokeMaterial != null) Object.Destroy(_strokeMaterial);
            if (_ribbonMaterial != null) Object.Destroy(_ribbonMaterial);
            if (_ribbonGlowMaterial != null) Object.Destroy(_ribbonGlowMaterial);
        }

        private BladeSurface NewSurface(string name, int rows, int order, Material material = null)
        {
            GameObject node = NewObject(name);
            var surface = new BladeSurface(node, rows);
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material ?? _ribbonMaterial;
            renderer.sortingLayerID = _sortLayer;
            renderer.sortingOrder = _sortOrder + order;
            _surfaces.Add(surface);
            return surface;
        }

        private static Material CreateRibbonMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader)
            {
                mainTexture = Texture2D.whiteTexture,
                color = Color.white,
                renderQueue = (int)RenderQueue.Transparent
            };
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private static Color BandColor(BladeBand band, float brightness) =>
            new Color32((byte)(band.Color >> 16), (byte)(band.Color >> 8), (byte)band.Color,
                (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(brightness * band.Opacity)));

        private GameObject NewObject(string name)
        {
            var node = new GameObject(name) { layer = _layer };
            node.transform.SetParent(_root.transform, false);
            return node;
        }

        private LineRenderer NewStroke(string name, int points, int order)
        {
            var line = NewObject(name).AddComponent<LineRenderer>();
            line.sharedMaterial = _strokeMaterial;
            line.positionCount = points;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.sortingLayerID = _sortLayer;
            line.sortingOrder = _sortOrder + order;
            return line;
        }

        private void Add(GameObject node, float duration, Action<float> draw, float delay = 0f)
        {
            _active.Add(new TimedEffect(node, duration, draw) { Age = -delay });
            if (delay > 0f) node.SetActive(false);
            else draw(Mathf.Clamp01(-delay / duration));
        }

        private static void Segment(LineRenderer line, Vector3 from, Vector3 to,
            float start, float end, Color color, float opacity)
        {
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            SetStyle(line, start, end, color, opacity);
        }

        private static void SetStyle(LineRenderer line, float start, float end, Color color, float opacity)
        {
            line.startWidth = start;
            line.endWidth = end;
            Fade(line, color, opacity);
        }

        private static void Fade(LineRenderer line, Color color, float opacity)
        {
            line.startColor = Tint(color, opacity);
            line.endColor = Tint(color, opacity * 0.72f);
        }

        private static Color Tint(Color color, float opacity)
        {
            color.a = opacity;
            return color;
        }

        private sealed class BladeSurface : IDisposable
        {
            internal readonly GameObject Node;
            private readonly Mesh _mesh;
            private readonly Vector3[] _vertices;
            private readonly Color[] _colors;

            internal BladeSurface(GameObject node, int rows)
            {
                Node = node;
                int bands = BladeRibbonProfile.Bands.Length;
                _vertices = new Vector3[rows * bands];
                _colors = new Color[_vertices.Length];
                var uv = new Vector2[_vertices.Length];
                var indices = new int[(rows - 1) * (bands - 1) * 6];
                int cursor = 0;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < bands; column++)
                    {
                        int index = row * bands + column;
                        uv[index] = new Vector2(row / (rows - 1f), column / (bands - 1f));
                        if (row == rows - 1 || column == bands - 1) continue;
                        indices[cursor++] = index;
                        indices[cursor++] = index + bands;
                        indices[cursor++] = index + 1;
                        indices[cursor++] = index + 1;
                        indices[cursor++] = index + bands;
                        indices[cursor++] = index + bands + 1;
                    }
                }
                _mesh = new Mesh { name = node.name, vertices = _vertices, uv = uv, triangles = indices };
                _mesh.MarkDynamic();
                node.AddComponent<MeshFilter>().sharedMesh = _mesh;
            }

            internal void Set(int row, int column, Vector3 position, Color color)
            {
                int index = row * BladeRibbonProfile.Bands.Length + column;
                _vertices[index] = position;
                _colors[index] = color;
            }

            internal void Upload()
            {
                _mesh.vertices = _vertices;
                _mesh.colors = _colors;
                _mesh.RecalculateBounds();
            }

            public void Dispose() => Object.Destroy(_mesh);
        }

        private sealed class TimedEffect
        {
            internal readonly GameObject Object;
            internal readonly float Duration;
            internal readonly Action<float> Draw;
            internal float Age;

            internal TimedEffect(GameObject node, float duration, Action<float> draw)
            {
                Object = node;
                Duration = duration;
                Draw = draw;
            }
        }
    }
}
