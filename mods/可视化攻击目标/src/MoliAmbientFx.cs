using System;
using System.Collections.Generic;
using System.IO;
using Game;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AttackTargetVisualizer
{
    /// <summary>根据场景角色状态显示花香、灰暗孤影和目标锁定。</summary>
    internal static class MoliAmbientFx
    {
        private const string ResourcePrefix = "AttackTargetVisualizer.Particles.";
        private const float RefreshInterval = 0.15f;
        private const int PetalCount = 8;
        private const int MistCount = 2;
        private static readonly Color MistTint = new Color(1f, 0.60f, 0.78f);
        private static readonly string[] PetalFiles =
            { "moli-pink-petal.png", "moli-pink-petal-rounded.png", "moli-pink-petal-curled.png" };
        private const int WindCount = 2;
        private static readonly System.Random VisualRandom = new System.Random();
        private static readonly Dictionary<RoleModel, RoleParticles> Active = new Dictionary<RoleModel, RoleParticles>();
        private static readonly List<RoleModel> Expired = new List<RoleModel>();
        private static float _nextRefresh;
        private static int _refresh;
        private static readonly List<Sprite> PetalSprites = new List<Sprite>();
        private static Sprite _mistSprite;
        private static Sprite _windSprite;
        private static Sprite _lockSprite;
        private static bool _flowerAssetsLoaded;
        private static bool _windAssetLoaded;
        private static bool _lockAssetLoaded;
        private static bool _disabled;

        internal static void Tick()
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                float now = Time.unscaledTime;
                if (now >= _nextRefresh)
                {
                    _nextRefresh = now + RefreshInterval;
                    RefreshVisibleRoles();
                }

                Camera camera = null;
                if (Active.Count > 0)
                {
                    camera = BattleHelper.IsInBattle ? BattleHelper.BattleCam : BattleHelper.MapCam;
                    if (camera == null) camera = Camera.main;
                }
                float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                foreach (RoleParticles particles in Active.Values)
                {
                    particles.Update(camera, step);
                }
            }
            catch (Exception error)
            {
                _disabled = true;
                Shutdown();
                AttackTargetPlugin.LogError("茉莉循环粒子停用：" + error);
            }
        }

        private static void RefreshVisibleRoles()
        {
            _refresh++;
            if (BattleHelper.IsInBattle && BattleHelper.FightContent is BattleFightContent fight)
            {
                ObserveRoles(fight.Allies);
                ObserveRoles(fight.CurWaveEnemies);
            }
            else if (BattleHelper.IsInModuleExplore)
            {
                ObserveRoles(BattleHelper.GetHeroTeamRoleList());
            }

            foreach (var entry in Active)
            {
                if (entry.Value.LastRefresh != _refresh)
                {
                    entry.Value.Dispose();
                    Expired.Add(entry.Key);
                }
            }
            foreach (RoleModel model in Expired)
            {
                Active.Remove(model);
            }
            Expired.Clear();
        }

        private static void ObserveRoles<T>(List<T> roles) where T : BattleRole
        {
            if (roles == null)
            {
                return;
            }

            foreach (T role in roles)
            {
                RoleModel model = role?.Model;
                if (role?.Data == null || role.IsDeath || model == null || model.ShowModelInUI
                    || model.IsCharactrerInfoModel || model.SpriteRender == null
                    || !model.SpriteRender.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool petals = role.GetBuff(SecretIds.FlowerScentBuff)?.CurLayer > 0;
                bool wind = role.GetBuff(SecretIds.LoneShadowBuff) != null;
                bool locked = role.GetBuff(JingJi.TargetLockBuffId) != null;
                if (!petals && !wind && !locked)
                {
                    continue;
                }
                if (petals && !_flowerAssetsLoaded)
                {
                    _flowerAssetsLoaded = true;
                    foreach (string file in PetalFiles)
                    {
                        Sprite sprite = LoadSprite(file, FilterMode.Point);
                        if (sprite != null) PetalSprites.Add(sprite);
                    }
                    _mistSprite = LoadSprite("moli-pink-floral-mist.png", FilterMode.Trilinear);
                }
                if (wind && !_windAssetLoaded)
                {
                    _windAssetLoaded = true;
                    _windSprite = LoadSprite("moli-white-airflow.png", FilterMode.Trilinear);
                }

                if (locked && !_lockAssetLoaded)
                {
                    _lockAssetLoaded = true;
                    _lockSprite = LoadSprite("jingji-target-lock.png", FilterMode.Point);
                }

                if (!Active.TryGetValue(model, out RoleParticles particles))
                {
                    particles = new RoleParticles(model);
                    Active.Add(model, particles);
                }
                particles.LastRefresh = _refresh;
                particles.SetEffects(petals, wind, locked);
            }
        }

        internal static Sprite LoadSprite(string name, FilterMode filter, bool keepReadable = false)
        {
            Texture2D texture = null;
            try
            {
                using (Stream asset = typeof(MoliAmbientFx).Assembly.GetManifestResourceStream(ResourcePrefix + name))
                {
                    if (asset == null)
                    {
                        throw new FileNotFoundException("DLL 缺少嵌入贴图", name);
                    }
                    using (var buffer = new MemoryStream())
                    {
                        asset.CopyTo(buffer);
                        bool mipmaps = filter != FilterMode.Point;
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps);
                        if (!ImageConversion.LoadImage(texture, buffer.ToArray()))
                        {
                            throw new InvalidDataException("不是有效的 PNG");
                        }
                    }
                }

                texture.name = name;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = filter;
                Rect bounds = VisibleRect(texture, filter == FilterMode.Point ? 96 : 24);
                Sprite sprite = Sprite.Create(texture, bounds, new Vector2(0.5f, 0.5f),
                    Mathf.Max(bounds.width, bounds.height), 0, SpriteMeshType.FullRect);
                sprite.name = name;
                texture.Apply(filter != FilterMode.Point, !keepReadable);
                return sprite;
            }
            catch (Exception error)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
                AttackTargetPlugin.LogError("粒子贴图载入失败 " + name + "，" + error.Message);
                return null;
            }
        }

        private static Rect VisibleRect(Texture2D texture, int alphaThreshold)
        {
            Color32[] pixels = texture.GetPixels32();
            int left = texture.width;
            int right = -1;
            int bottom = texture.height;
            int top = -1;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a < alphaThreshold) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < bottom) bottom = y;
                    if (y > top) top = y;
                }
            }
            if (right < left || top < bottom)
            {
                throw new InvalidDataException("贴图没有可见像素");
            }

            const int padding = 6;
            left = Mathf.Max(0, left - padding);
            bottom = Mathf.Max(0, bottom - padding);
            right = Mathf.Min(texture.width, right + padding + 1);
            top = Mathf.Min(texture.height, top + padding + 1);
            return Rect.MinMaxRect(left, bottom, right, top);
        }

        internal static void Shutdown()
        {
            foreach (RoleParticles particles in Active.Values)
            {
                particles.Dispose();
            }
            Active.Clear();
            Expired.Clear();
            foreach (Sprite petal in PetalSprites)
            {
                DestroySprite(petal);
            }
            PetalSprites.Clear();
            DestroySprite(_mistSprite);
            DestroySprite(_windSprite);
            DestroySprite(_lockSprite);
            _lockSprite = null;
            _lockAssetLoaded = false;
            _mistSprite = null;
            _windSprite = null;
            _flowerAssetsLoaded = false;
            _windAssetLoaded = false;
        }

        internal static void DestroySprite(Sprite sprite)
        {
            if (sprite == null) return;
            Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }

        private static float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)VisualRandom.NextDouble());
        }

        private sealed class RoleParticles
        {
            internal int LastRefresh;
            private readonly RoleModel _model;
            private readonly SpriteRenderer _body;
            private readonly Transform _root;
            private readonly Petal[] _petals = new Petal[PetalCount];
            private readonly SpriteRenderer[] _wind = new SpriteRenderer[WindCount];
            private readonly SpriteRenderer[] _mist = new SpriteRenderer[MistCount];
            private float _nextPetal;
            private float _mistPhase;
            private int _nextPetalSprite;
            private bool _playMist;
            private bool _playLock;
            private SpriteRenderer _lockMark;
            private float _lockAge;
            private float _windPhase;
            private int _side = 1;
            private bool _playPetals;
            private bool _playWind;

            internal RoleParticles(RoleModel model)
            {
                _model = model;
                _body = model.SpriteRender;
                var root = new GameObject("MoliAmbientParticles");
                _root = root.transform;
                _root.SetParent(_body.transform, false);
            }

            internal void SetEffects(bool petals, bool wind, bool locked)
            {
                _playPetals = petals && PetalSprites.Count > 0;
                _playMist = petals && _mistSprite != null;
                _playWind = wind && _windSprite != null;
                _playLock = locked && _lockSprite != null;
            }

            internal void Update(Camera camera, float step)
            {
                if (_model == null || _root == null || _body == null)
                {
                    return;
                }

                bool visible = camera != null && _body.enabled && _body.gameObject.activeInHierarchy && _body.color.a > 0f;
                if (_root.gameObject.activeSelf != visible)
                {
                    _root.gameObject.SetActive(visible);
                }
                if (!visible)
                {
                    return;
                }

                var frame = new ParticleFrame(_model, _body, camera.transform);
                UpdateMist(frame, step);
                UpdatePetals(frame, step);
                UpdateWind(frame, step);
                UpdateLockMark(frame, step);
            }

            private void UpdatePetals(ParticleFrame frame, float step)
            {
                if (!_playPetals)
                {
                    _nextPetal = 0f;
                    foreach (Petal petal in _petals)
                    {
                        if (petal == null) continue;
                        petal.Age = petal.Lifetime;
                        petal.Renderer.enabled = false;
                    }
                    return;
                }

                VfxPlacement.Effect placement = VfxPlacement.Current.flower;
                _nextPetal -= step;
                for (int index = 0; index < _petals.Length; index++)
                {
                    Petal petal = _petals[index];
                    if (petal != null)
                    {
                        petal.Age += step;
                    }
                    if (_nextPetal <= 0f && (petal == null || petal.Age >= petal.Lifetime))
                    {
                        Sprite sprite = PetalSprites[_nextPetalSprite];
                        _nextPetalSprite = (_nextPetalSprite + 1) % PetalSprites.Count;
                        if (petal == null)
                        {
                            petal = new Petal(CreateParticle("Petal", sprite));
                            _petals[index] = petal;
                        }
                        else
                        {
                            petal.Renderer.sprite = sprite;
                        }
                        petal.Restart(_side);
                        _side = -_side;
                        _nextPetal = Range(0.29f, 0.38f);
                    }
                    if (petal == null) continue;
                    if (petal.Age >= petal.Lifetime)
                    {
                        petal.Renderer.enabled = false;
                        continue;
                    }

                    float progress = petal.Age / petal.Lifetime;
                    float wobble = Mathf.Sin(progress * 9f + petal.Phase);
                    float drift = petal.Side * (frame.HalfWidth + frame.Height * (0.04f + 0.11f * progress))
                        + frame.Height * 0.05f * wobble;
                    float x = frame.BodyCenterX + drift * placement.spread;
                    float y = frame.Height * Mathf.Lerp(petal.StartHeight, -0.015f, progress);
                    float fade = Mathf.Clamp01(progress * 7f) * Mathf.Clamp01((1f - progress) * 5f);
                    float size = frame.Height * petal.Size;
                    float flip = 0.67f + 0.33f * Mathf.Abs(Mathf.Cos(progress * 7f + petal.Phase));
                    Place(petal.Renderer, frame, x, y, size * flip, size,
                        petal.Rotation + petal.Side * progress * 140f + wobble * 22f, fade * 0.83f, petal.Order,
                        placement: placement);
                }
            }

            private void UpdateMist(ParticleFrame frame, float step)
            {
                if (!_playMist)
                {
                    foreach (SpriteRenderer wisp in _mist)
                    {
                        if (wisp != null) wisp.enabled = false;
                    }
                    _mistPhase = 0f;
                    return;
                }

                VfxPlacement.Effect placement = VfxPlacement.Current.flower;
                _mistPhase = Mathf.Repeat(_mistPhase + step * 0.30f, 1f);
                for (int index = 0; index < _mist.Length; index++)
                {
                    if (_mist[index] == null)
                    {
                        _mist[index] = CreateParticle("FlowerMist", _mistSprite);
                    }
                    float progress = Mathf.Repeat(_mistPhase + index * 0.5f, 1f);
                    float side = index == 0 ? -1f : 1f;
                    float x = frame.BodyCenterX + side * (frame.HalfWidth + frame.Height * (0.02f + 0.14f * progress))
                        * placement.spread;
                    float y = frame.Height * Mathf.Lerp(0.13f, 0.49f, progress);
                    float width = frame.Height * Mathf.Lerp(0.27f, 0.41f, progress);
                    float alpha = 0.32f * Mathf.Sin(progress * Mathf.PI);
                    Place(_mist[index], frame, x, y, width, width, side * (15f + 16f * progress), alpha, -2,
                        MistTint, placement);
                }
            }

            private void UpdateWind(ParticleFrame frame, float step)
            {
                if (!_playWind)
                {
                    foreach (SpriteRenderer line in _wind)
                    {
                        if (line != null) line.enabled = false;
                    }
                    return;
                }
                _windPhase = Mathf.Repeat(_windPhase + step * 1.8f, Mathf.PI * 2f);
                VfxPlacement.Effect placement = VfxPlacement.Current.wind;
                float radius = (frame.HalfWidth + frame.Height * 0.15f) * placement.spread;
                float rise = frame.Height * 0.105f;
                for (int index = 0; index < _wind.Length; index++)
                {
                    if (_wind[index] == null)
                    {
                        _wind[index] = CreateParticle("Wind", _windSprite);
                    }
                    float phase = _windPhase + index * Mathf.PI;
                    float across = Mathf.Cos(phase);
                    float depth = Mathf.Sin(phase);
                    float y = frame.Height * (0.16f + index * 0.085f) + rise * depth;
                    float angle = Mathf.Atan2(rise * across, -radius * depth) * Mathf.Rad2Deg;
                    float size = frame.Height * (index == 0 ? 0.43f : 0.33f);
                    float alpha = Mathf.Lerp(0.48f, 0.70f, Mathf.Max(0f, -depth));
                    Place(_wind[index], frame, frame.BodyCenterX + radius * across, y, size, size * 1.75f, angle, alpha,
                        depth > 0 ? -1 : 1, placement: placement);
                }
            }

            private void UpdateLockMark(ParticleFrame frame, float step)
            {
                if (!_playLock)
                {
                    if (_lockMark != null) _lockMark.enabled = false;
                    _lockAge = 0f;
                    return;
                }

                if (_lockMark == null) _lockMark = CreateParticle("TargetLock", _lockSprite);
                const float settleTime = 0.20f;
                _lockAge = Mathf.Min(_lockAge + step, settleTime);
                float remaining = 1f - _lockAge / settleTime;
                float settle = 1f + 0.18f * remaining * remaining;
                float outerWidth = Mathf.Max(frame.BodyHalfWidth * 2f + frame.Height * 0.28f, frame.Height * 0.75f);
                float outerHeight = frame.Height * 1.32f;
                Sprite mark = _lockMark.sprite;
                float width = outerWidth * mark.pixelsPerUnit / mark.rect.width;
                float height = outerHeight * mark.pixelsPerUnit / mark.rect.height;
                Place(_lockMark, frame, frame.BodyCenterX, frame.Height * 0.50f,
                    width * settle, height * settle, 0f, 0.86f + 0.14f * remaining, 3,
                    placement: VfxPlacement.Current.lockMark);
            }

            private SpriteRenderer CreateParticle(string name, Sprite sprite)
            {
                var item = new GameObject(name);
                item.layer = _body.gameObject.layer;
                item.transform.SetParent(_root, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.enabled = false;
                return renderer;
            }

            private void Place(SpriteRenderer renderer, ParticleFrame frame, float x, float y, float width,
                float height, float angle, float alpha, int order, Color? tint = null,
                VfxPlacement.Effect placement = null)
            {
                if (placement != null)
                {
                    x += frame.Height * placement.x;
                    y += frame.Height * placement.y;
                    angle += placement.angle;
                }
                renderer.enabled = true;
                Color color = tint ?? Color.white;
                color.a *= alpha * _body.color.a;
                renderer.color = color;
                renderer.sortingLayerID = _body.sortingLayerID;
                renderer.sortingOrder = _body.sortingOrder + order;
                renderer.transform.SetPositionAndRotation(frame.Base + frame.Right * x + frame.Up * y,
                    frame.Rotation * Quaternion.Euler(0f, 0f, angle));
                Vector3 scale = _root.lossyScale;
                renderer.transform.localScale = new Vector3(
                    width / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                    height / Mathf.Max(0.001f, Mathf.Abs(scale.y)), 1f);
            }

            internal void Dispose()
            {
                if (_root == null) return;
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }
        }

        private sealed class Petal
        {
            internal readonly SpriteRenderer Renderer;
            internal float Age;
            internal float Lifetime;
            internal float Phase;
            internal float Rotation;
            internal float StartHeight;
            internal float Size;
            internal int Side;
            internal int Order;

            internal Petal(SpriteRenderer renderer)
            {
                Renderer = renderer;
            }

            internal void Restart(int side)
            {
                Age = 0f;
                Lifetime = Range(2.1f, 2.5f);
                Phase = Range(0f, Mathf.PI * 2f);
                Rotation = Range(-65f, 65f);
                StartHeight = Range(0.62f, 0.78f);
                Size = Range(0.060f, 0.090f);
                Side = side;
                Order = VisualRandom.Next(3) == 0 ? -1 : 1;
            }
        }

        internal readonly struct ParticleFrame
        {
            internal readonly Vector3 Base;
            internal readonly Vector3 Right;
            internal readonly Vector3 Up;
            internal readonly Quaternion Rotation;
            internal readonly float Height;
            internal readonly float HalfWidth;
            internal readonly float BodyHalfWidth;
            internal readonly float BodyCenterX;

            internal ParticleFrame(RoleModel model, SpriteRenderer body, Transform camera)
            {
                Right = camera.right;
                Up = camera.up;
                Rotation = camera.rotation;
                Bounds bounds = body.bounds;
                float halfHeight = ExtentAlong(bounds.extents, Up);
                Base = model.Trans_Base != null ? model.Trans_Base.position : bounds.center - Up * halfHeight;
                Vector3 top = model.Trans_Head != null ? model.Trans_Head.position : bounds.center + Up * halfHeight;
                float anchorHeight = Vector3.Dot(top - Base, Up);
                Height = Mathf.Max(0.12f, anchorHeight > 0.12f ? anchorHeight : 2f * halfHeight);
                BodyHalfWidth = ExtentAlong(bounds.extents, Right);
                BodyCenterX = Vector3.Dot(bounds.center - Base, Right);
                HalfWidth = Mathf.Clamp(BodyHalfWidth, Height * 0.17f, Height * 0.40f);
            }

            private static float ExtentAlong(Vector3 extents, Vector3 axis)
            {
                return extents.x * Mathf.Abs(axis.x) + extents.y * Mathf.Abs(axis.y) + extents.z * Mathf.Abs(axis.z);
            }
        }
    }
}
