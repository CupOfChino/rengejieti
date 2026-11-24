using System;
using System.Collections.Generic;
using Game;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AttackTargetVisualizer
{
    /// <summary>只在原武器命中节点播放剑气；瞬态特效与伤害、命中检定和音效各自结算。</summary>
    internal static class BattleBladeFx
    {
        private static readonly List<BladeBurst> Active = new List<BladeBurst>();
        private static BladeStrip _thrust;
        private static Sprite _gust;
        private static BladeStrip _slash;
        private static Sprite _petal;
        private static bool _thrustLoaded;
        private static bool _slashLoaded;

        internal static bool TryPuncture(BattleRole target, BattleRole source, float verticalOffset)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            Camera camera = BattleHelper.BattleCam;
            if (fight == null || camera == null || !TryMeasure(target, camera, out BladeAnchor anchor))
            {
                return false;
            }
            if (!_thrustLoaded)
            {
                _thrustLoaded = true;
                _thrust = BladeStrip.Load("jingji-mint-thrust.png");
                _gust = MoliAmbientFx.LoadSprite("moli-white-airflow.png", FilterMode.Trilinear);
            }
            if (_thrust == null) return false;

            Vector3 impact = anchor.Center + camera.transform.up * verticalOffset;
            var burst = new BladeBurst(fight, camera, anchor, impact, Direction(source, impact, camera),
                _thrust, _gust, true, false, anchor.Height * 1.72f);
            Active.Add(burst);
            return true;
        }

        internal static bool TrySlash(BattleRole source, List<BattleRole> targets, bool sweep)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            Camera camera = BattleHelper.BattleCam;
            if (fight == null || camera == null || targets == null || targets.Count == 0) return false;
            if (!_slashLoaded)
            {
                _slashLoaded = true;
                _slash = BladeStrip.Load("zanghua-mint-slash.png");
                _petal = MoliAmbientFx.LoadSprite("zanghua-pale-petal.png", FilterMode.Point);
            }
            if (_slash == null) return false;

            BladeAnchor first = default;
            int count = 0;
            Vector3 sum = Vector3.zero;
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            foreach (BattleRole target in targets)
            {
                if (!TryMeasure(target, camera, out BladeAnchor anchor)) continue;
                if (count++ == 0) first = anchor;
                if (!sweep)
                {
                    var burst = new BladeBurst(fight, camera, anchor, anchor.Center,
                        Direction(source, anchor.Center, camera), _slash, _petal, false, false, anchor.Height * 1.64f);
                    Active.Add(burst);
                    continue;
                }
                float x = Vector3.Dot(anchor.Center, camera.transform.right);
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                sum += anchor.Center;
            }
            if (count == 0) return false;
            if (sweep)
            {
                Vector3 center = sum / count;
                Vector3 axis = camera.transform.right;
                center += axis * ((left + right) * 0.5f - Vector3.Dot(center, axis));
                float width = Mathf.Min(right - left + first.Height * 2.35f, first.Height * 5.8f);
                var burst = new BladeBurst(fight, camera, first, center, Direction(source, center, camera),
                    _slash, _petal, false, true, width);
                Active.Add(burst);
            }
            return true;
        }

        private static float Direction(BattleRole source, Vector3 target, Camera camera)
        {
            SpriteRenderer sprite = source?.Model != null ? source.Model.SpriteRender : null;
            if (sprite == null) return 1f;
            float delta = Vector3.Dot(target - sprite.bounds.center, camera.transform.right);
            return delta < 0f ? -1f : 1f;
        }

        private static bool TryMeasure(BattleRole target, Camera camera, out BladeAnchor anchor)
        {
            anchor = default;
            RoleModel model = target?.Model;
            if (model == null || model.SpriteRender == null || !model.gameObject.activeInHierarchy) return false;
            var frame = new MoliAmbientFx.ParticleFrame(model, model.SpriteRender, camera.transform);
            anchor = new BladeAnchor
            {
                Height = frame.Height,
                Center = model.Trans_Center != null ? model.Trans_Center.position : frame.Base + frame.Up * frame.Height * 0.5f,
                Body = model.SpriteRender
            };
            return true;
        }

        private struct BladeAnchor
        {
            internal float Height;
            internal Vector3 Center;
            internal SpriteRenderer Body;
        }

        internal static void Tick()
        {
            if (Active.Count == 0) return;
            BattleFightContent fight = BattleHelper.IsInBattle ? BattleHelper.FightContent : null;
            float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                BladeBurst burst = Active[i];
                if (burst.Fight == fight && burst.Advance(step)) continue;
                burst.Dispose();
                Active.RemoveAt(i);
            }
        }

        internal static void Shutdown()
        {
            foreach (BladeBurst burst in Active) burst.Dispose();
            Active.Clear();
            _thrust?.Dispose();
            _slash?.Dispose();
            MoliAmbientFx.DestroySprite(_gust);
            MoliAmbientFx.DestroySprite(_petal);
            _thrust = _slash = null;
            _gust = _petal = null;
            _thrustLoaded = _slashLoaded = false;
        }

        // 每一格共用同一张图，只显示从剑气起点到刃尖的部分。
        private sealed class BladeStrip
        {
            private const int RevealFrames = 16;
            private readonly Sprite[] _frames = new Sprite[RevealFrames];
            private Sprite Full => _frames[RevealFrames - 1];

            private BladeStrip(Sprite sprite)
            {
                _frames[RevealFrames - 1] = sprite;
            }

            internal static BladeStrip Load(string file)
            {
                Sprite sprite = MoliAmbientFx.LoadSprite(file, FilterMode.Trilinear, keepReadable: true);
                if (sprite == null) return null;
                var strip = new BladeStrip(sprite);
                try
                {
                    Rect full = sprite.rect;
                    for (int i = 0; i < RevealFrames - 1; i++)
                    {
                        float width = Mathf.Floor(full.width * ((i + 1f) / RevealFrames));
                        Rect crop = new Rect(full.x, full.y, Mathf.Max(1f, width), full.height);
                        strip._frames[i] = Sprite.Create(sprite.texture, crop,
                            new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    }
                    sprite.texture.Apply(false, true);
                    return strip;
                }
                catch (Exception error)
                {
                    strip.Dispose();
                    AttackTargetPlugin.LogError("剑气展开贴图创建失败：" + error.Message);
                    return null;
                }
            }

            internal Sprite GetFrame(float progress, out float inset)
            {
                int index = Mathf.Clamp(Mathf.CeilToInt(progress * RevealFrames) - 1, 0, RevealFrames - 1);
                Sprite frame = _frames[index];
                inset = (frame.rect.width - Full.rect.width) / (2f * Full.pixelsPerUnit);
                return frame;
            }

            internal void Dispose()
            {
                for (int i = 0; i < RevealFrames - 1; i++)
                {
                    if (_frames[i] != null) Object.Destroy(_frames[i]);
                }
                MoliAmbientFx.DestroySprite(Full);
            }
        }

        private sealed class BladeBurst
        {
            private static readonly Color BluePetal = new Color(0.69f, 0.87f, 1f);
            private static readonly Color MintEcho = new Color(0.62f, 1f, 0.79f);
            internal readonly BattleFightContent Fight;
            private readonly Transform _root;
            private readonly SpriteRenderer _blade;
            private readonly SpriteRenderer _echo;
            private readonly BladeStrip _strip;
            private readonly SpriteRenderer[] _bits;
            private readonly float _height;
            private readonly float _width;
            private readonly float _direction;
            private readonly bool _pierce;
            private readonly bool _wide;
            private float _age;

            internal BladeBurst(BattleFightContent fight, Camera camera, BladeAnchor anchor, Vector3 center,
                float direction, BladeStrip blade, Sprite particles, bool pierce, bool wide, float width)
            {
                Fight = fight;
                _height = anchor.Height;
                _width = width;
                _direction = direction;
                _pierce = pierce;
                _wide = wide;
                _strip = blade;

                _root = new GameObject(pierce ? "JingJiMintThrust" : "ZangHuaMintSlash").transform;
                Transform scene = fight.Room?.RoomEntity?.SceneTipRoot;
                if (scene != null) _root.SetParent(scene, true);
                VfxPlacement layout = VfxPlacement.Current;
                VfxPlacement.Effect placement = pierce ? layout.thrust : wide ? layout.sweep : layout.slash;
                Vector3 offset = (camera.transform.right * placement.x + camera.transform.up * placement.y) * _height;
                _root.SetPositionAndRotation(center + offset,
                    camera.transform.rotation * Quaternion.Euler(0f, 0f, placement.angle));

                Sprite initial = blade.GetFrame(0f, out _);
                _blade = NewRenderer("Blade", initial, anchor.Body, 3);
                _echo = NewRenderer("BladeEcho", initial, anchor.Body, 2);
                _blade.flipX = _echo.flipX = _direction < 0f;
                _blade.flipY = _echo.flipY = !pierce;
                int count = particles == null ? 0 : pierce ? 4 : wide ? 12 : 8;
                _bits = new SpriteRenderer[count];
                for (int i = 0; i < count; i++)
                {
                    _bits[i] = NewRenderer(pierce ? "PressureWave" : "PalePetal", particles, anchor.Body, pierce ? 1 : 4);
                }
                Render(0f);
            }

            private SpriteRenderer NewRenderer(string name, Sprite sprite, SpriteRenderer body, int order)
            {
                var child = new GameObject(name);
                child.layer = body.gameObject.layer;
                child.transform.SetParent(_root, false);
                var renderer = child.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = body.sortingLayerID;
                renderer.sortingOrder = body.sortingOrder + order;
                return renderer;
            }

            internal bool Advance(float step)
            {
                if (_root == null) return false;
                _age += step;
                float progress = _age / (_pierce ? 0.34f : 0.66f);
                if (progress >= 1f) return false;
                Render(progress);
                return true;
            }

            private void Render(float t)
            {
                if (_pierce) RenderPuncture(t);
                else RenderSlash(t);
            }

            private void RenderPuncture(float t)
            {
                float reveal = Ease(t / 0.35f);
                float engage = Ease(t / 0.14f);
                float trail = Ease((t - 0.12f) / 0.32f);
                float x = _direction * _height * Mathf.Lerp(-0.42f, 0.23f, Ease(t));
                float length = _width * Mathf.Lerp(0.83f, 1.06f, reveal);
                DrawBlade(_blade, reveal, x, 0f, length, _width * 0.95f, 0f, Color.white,
                    engage * (1f - Ease((t - 0.44f) / 0.44f)));
                DrawBlade(_echo, trail, x - _direction * _height * 0.18f, _height * 0.02f,
                    length * 1.10f, _width * 1.38f, 0f, MintEcho,
                    trail * (1f - Ease((t - 0.48f) / 0.49f)) * 0.42f);

                for (int i = 0; i < _bits.Length; i++)
                {
                    bool late = i >= 2;
                    float wake = Mathf.InverseLerp(late ? 0.23f : 0.11f, 1f, t);
                    float side = i % 2 == 0 ? -1f : 1f;
                    float wave = _height * Mathf.Lerp(late ? 0.72f : 0.54f, late ? 1.45f : 1.16f, Ease(wake));
                    float dispersion = Mathf.Lerp(late ? 0.17f : 0.09f, late ? 0.47f : 0.35f, Ease(wake));
                    float alpha = Mathf.Sin(wake * Mathf.PI) * (late ? 0.38f : 0.61f);
                    Place(_bits[i], x - _direction * _height * (0.20f + wake * 0.23f), side * _height * dispersion,
                        wave, wave * 2.12f, side * _direction * (8f + wake * 12f), Color.white, alpha);
                }
            }

            private void RenderSlash(float t)
            {
                float reveal = Ease(t / 0.40f);
                float echo = Ease((t - 0.13f) / 0.36f);
                float growth = Mathf.Lerp(0.90f, 1.13f, Ease(t));
                float x = _direction * _height * Mathf.Lerp(-0.17f, 0.29f, Ease(t));
                float height = (_wide ? _height * 0.85f : _width * 0.91f) * growth;
                float angle = _direction * Mathf.Lerp(-12f, 13f, Ease(t));
                DrawBlade(_blade, reveal, x, 0f, _width * growth, height, angle, Color.white,
                    Ease(t / 0.12f) * (1f - Ease((t - 0.43f) / 0.31f)));
                DrawBlade(_echo, echo, x - _direction * _height * 0.13f, -_height * 0.10f,
                    _width * growth * 1.04f, height * 0.74f, angle - _direction * 9f, MintEcho,
                    echo * (1f - Ease((t - 0.55f) / 0.39f)) * 0.43f);

                for (int i = 0; i < _bits.Length; i++)
                {
                    float p = Mathf.InverseLerp(0.16f + (i % 4) * 0.042f, 1f, t);
                    float spread = (i + 0.5f) / _bits.Length * 2f - 1f;
                    float phase = i * 2.39f;
                    float f = Ease(p);
                    float petalX = spread * _width * (0.23f + f * 0.44f) + _direction * _height * p * 0.28f;
                    float petalY = _height * (Mathf.Sin(phase) * (0.09f + f * 0.46f) + p * 0.64f - p * p * 0.91f);
                    float size = _height * (0.075f + (i % 3) * 0.017f);
                    float flip = 0.61f + 0.39f * Mathf.Abs(Mathf.Cos(phase + p * 7f));
                    float alpha = Ease(p * 6f) * (1f - Ease((p - 0.55f) / 0.45f));
                    Place(_bits[i], petalX, petalY, size * flip, size, phase * 45f + _direction * 190f * p,
                        i % 2 == 0 ? Color.white : BluePetal, alpha);
                }
            }

            private void DrawBlade(SpriteRenderer renderer, float progress, float x, float y,
                float width, float height, float angle, Color tint, float alpha)
            {
                if (progress <= 0f || alpha <= 0f)
                {
                    renderer.enabled = false;
                    return;
                }

                renderer.sprite = _strip.GetFrame(progress, out float inset);
                float offset = inset * width * _direction;
                float radians = angle * Mathf.Deg2Rad;
                Place(renderer, x + offset * Mathf.Cos(radians), y + offset * Mathf.Sin(radians),
                    width, height, angle, tint, alpha);
            }

            private static float Ease(float value)
            {
                float t = Mathf.Clamp01(value);
                return t * t * (3f - 2f * t);
            }

            private static void Place(SpriteRenderer sprite, float x, float y, float width, float height,
                float angle, Color color, float alpha)
            {
                sprite.enabled = alpha > 0f;
                if (!sprite.enabled) return;
                color.a = alpha;
                sprite.color = color;
                sprite.transform.localPosition = new Vector3(x, y, 0f);
                sprite.transform.localScale = new Vector3(width, height, 1f);
                sprite.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            internal void Dispose()
            {
                if (_root != null)
                {
                    _root.gameObject.SetActive(false);
                    Object.Destroy(_root.gameObject);
                }
            }
        }
    }
}
