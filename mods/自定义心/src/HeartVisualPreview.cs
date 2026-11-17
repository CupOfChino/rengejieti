// 用实际模型/特效的精灵帧绘制UI预览；不把运行中的角色搬入界面，也不改共享动画配置。
using System;
using System.Threading.Tasks;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace XinEditor
{
    internal sealed class HeartVisualPreview : MonoBehaviour
    {
        private const float PixelsPerUnit = 135f;
        private Image _body;
        private Image _flame;
        private Text _message;
        private GameObject _modelObject;
        private GameObject _effectObject;
        private RoleModel _model;
        private RTECustomAnimConfig _effect;
        private SpriteAnimationData _idle;
        private Sprite _fallbackSprite;
        private Vector3 _bodyOrigin;
        private Vector2 _bodyScale = Vector2.one;
        private HeartVisualSettings _settings = new HeartVisualSettings();
        private int _bodyIndex, _flameIndex, _generation;
        private float _bodyTime, _flameTime, _bodySpeed = 1f;
        private float _started;
        private bool _loading;
        internal bool Ready { get; private set; }

        internal static HeartVisualPreview Create(Transform parent, float width, float height)
        {
            Image background = UIFactory.Panel(parent, "VisualPreview", new Color(0.055f, 0.065f, 0.08f, 1f));
            background.gameObject.AddComponent<RectMask2D>();
            HeartVisualPreview preview = background.gameObject.AddComponent<HeartVisualPreview>();
            preview._flame = SpriteImage(background.transform, "Shin");
            preview._body = SpriteImage(background.transform, "IdleModel");
            preview.BuildAxes(background.transform, width, height);
            preview._message = UIFactory.Label(background.transform, "PreviewMessage", "", 20, TextAnchor.UpperLeft);
            UIFactory.Place(preview._message.rectTransform, 12f, 8f, width - 24f, 28f);
            return preview;
        }

        private static Image SpriteImage(Transform parent, string name)
        {
            Image image = UIFactory.Panel(parent, name, Color.white);
            image.raycastTarget = false;
            image.enabled = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            return image;
        }

        private void BuildAxes(Transform parent, float width, float height)
        {
            // 所有线条坐标都相对固定原点，不参与火焰位移或缩放。
            for (int tick = -10; tick <= 10; tick++)
            {
                float p = tick * 0.25f * PixelsPerUnit;
                if (Math.Abs(p) < width * 0.5f)
                    Line(parent, "GridX" + tick, new Vector2(p, 0f), new Vector2(1f, height), new Color(1f, 1f, 1f, 0.055f));
                if (Math.Abs(p) < height * 0.5f)
                    Line(parent, "GridY" + tick, new Vector2(0f, p), new Vector2(width, 1f), new Color(1f, 1f, 1f, 0.055f));
            }
            Line(parent, "XAxis", Vector2.zero, new Vector2(width, 2f), new Color(1f, 0.45f, 0.4f, 0.8f));
            Line(parent, "YAxis", Vector2.zero, new Vector2(2f, height), new Color(0.45f, 0.9f, 0.55f, 0.8f));
            Line(parent, "Origin", Vector2.zero, new Vector2(6f, 6f), Color.white);
            Text x = UIFactory.Label(parent, "XLabel", "+X →", 18, TextAnchor.MiddleRight);
            UIFactory.Place(x.rectTransform, width - 94f, height * 0.5f - 28f, 80f, 26f);
            Text y = UIFactory.Label(parent, "YLabel", "+Y ↑", 18, TextAnchor.MiddleLeft);
            UIFactory.Place(y.rectTransform, width * 0.5f + 8f, 34f, 80f, 24f);
            Text zero = UIFactory.Label(parent, "OriginLabel", "(0, 0)", 17, TextAnchor.MiddleLeft);
            UIFactory.Place(zero.rectTransform, width * 0.5f + 7f, height * 0.5f + 5f, 90f, 22f);
        }

        private static void Line(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            Image line = UIFactory.Panel(parent, name, color);
            line.raycastTarget = false;
            line.rectTransform.anchorMin = line.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.rectTransform.anchoredPosition = position;
            line.rectTransform.sizeDelta = size;
        }

        internal void Apply(HeartVisualSettings settings) { _settings = settings.Copy(); }

        internal async void Load(HeroRoleData role)
        {
            Release();
            int generation = _generation;
            _loading = true;
            _started = Time.unscaledTime;
            _message.text = XinText.T("正在加载预览…", "Loading preview...");
            GameObject loaded = null;
            try
            {
                if (!string.IsNullOrEmpty(role.Custom_RoleModelName))
                    await MODHelper.LoadRoleModel(role.Custom_RoleModelName, o => { loaded = o; if (o != null) o.SetActive(false); });
                if (loaded == null && role.ModelRes != null && !string.IsNullOrEmpty(role.ModelRes.Key))
                    await role.ModelRes.GetResLoad(true, o => { loaded = o; if (o != null) o.SetActive(false); });
                if (loaded == null && role.Role != null && role.Role.Model != null)
                {
                    loaded = UnityEngine.Object.Instantiate(role.Role.Model.gameObject);
                    loaded.SetActive(false);
                }
                if (generation != _generation || !isActiveAndEnabled)
                {
                    if (loaded != null) UnityEngine.Object.Destroy(loaded);
                    return;
                }
                _modelObject = loaded;
                if (loaded == null || (_model = loaded.GetComponent<RoleModel>()) == null)
                    throw new InvalidOperationException("未能读取当前角色模型");
                loaded.transform.SetParent(transform, false);
                loaded.transform.localPosition = Vector3.zero;
                PlaySpriteAnimation animation = _model.SpriteAnim;
                if (animation == null) animation = loaded.GetComponentInChildren<PlaySpriteAnimation>(true);
                if (animation != null && animation.AnimationList != null)
                {
                    _idle = animation.AnimationList.Find(a => a != null && a.Key == "idle")
                        ?? animation.AnimationList.Find(a => a != null && a.Key == animation.DefaultName);
                    _bodySpeed = Math.Abs(animation.AnimSpeed);
                    if (_bodySpeed < 0.0001f) _bodySpeed = 1f;
                }
                SpriteRenderer render = _model.SpriteRender;
                if (render == null && animation != null) render = animation.Render;
                if (render == null) throw new InvalidOperationException("模型没有主体图片");
                _fallbackSprite = render.sprite;
                Vector3 anchor = GetAnchor(_model);
                _bodyOrigin = loaded.transform.InverseTransformPoint(render.transform.position) - anchor;
                _bodyScale = new Vector2(
                    loaded.transform.InverseTransformVector(render.transform.TransformVector(Vector3.right)).x,
                    loaded.transform.InverseTransformVector(render.transform.TransformVector(Vector3.up)).y);
                _effectObject = MODHelper.CreatFrameEffect(XinEditorPlugin.FrameEffectName, XinEditorPlugin.FrameEffectAnim);
                if (_effectObject == null) throw new InvalidOperationException("未能创建心焰预览");
                _effectObject.SetActive(false);
                _effectObject.transform.SetParent(transform, false);
                _effect = _effectObject.GetComponent<RTECustomAnimConfig>();
                if (_effect == null || _effect.CurPlayData == null) throw new InvalidOperationException("未能读取心焰帧动画");
            }
            catch (Exception e)
            {
                if (generation != _generation) return;
                _loading = false;
                _message.text = XinText.T("预览加载失败，请查看日志后重新打开。", "Preview failed. Check the log and reopen.");
                XinEditorPlugin.LogError("心外观预览加载失败：" + e);
            }
        }

        private static Vector3 GetAnchor(RoleModel model)
        {
            int point = XinEditorPlugin.FrameEffectPoint;
            Transform anchor = point == 1 ? model.Trans_Head : point == 3 ? model.Trans_Base
                : point == 4 ? model.Trans_Front : point == 5 ? model.Trans_Back : model.Trans_Center;
            // 战斗Install会按idle挂点覆盖顶层默认位置，预览采用同一份配置。
            HangingPointData idle = model.PointPosDatas != null
                ? model.PointPosDatas.Find(p => p != null && p.AnimKeys != null && p.AnimKeys.Contains("idle")) : null;
            if (idle != null)
            {
                if (point == 1) return idle.HeadPos;
                if (point == 3) return idle.BasePos;
                if (point != 4 && point != 5) return idle.CenterPos;
            }
            return anchor != null ? model.transform.InverseTransformPoint(anchor.position) : Vector3.zero;
        }

        private Sprite BodyFrame(int index)
        {
            if (_idle != null)
            {
                if (_idle.IsUseOldMode && _idle.Sprites != null && _idle.Sprites.Count > 0)
                    return _idle.Sprites[index % _idle.Sprites.Count];
                if (_idle.SpriteDatas != null && _idle.SpriteDatas.Count > 0)
                    return _idle.SpriteDatas[index % _idle.SpriteDatas.Count].Sprite;
            }
            return _fallbackSprite;
        }

        private void Update()
        {
            if (!_loading || _model == null || _effect == null) return;
            try
            {
                Sprite body = BodyFrame(_bodyIndex);
                EffectSpriteConfigData data = _effect.CurPlayData;
                if (data == null || data.ExtraAnims == null || data.ExtraAnims.Count == 0) return;
                Sprite flame = _effect.GetTarLoadSprite(data.ExtraAnims[_flameIndex % data.ExtraAnims.Count]);
                if (body == null || flame == null)
                {
                    if (Time.unscaledTime - _started > 20f)
                    {
                        _message.text = XinText.T("部分预览资源尚未加载，请关闭后重试。", "Some preview assets are unavailable. Close and retry.");
                        _loading = false;
                    }
                    return;
                }
                if (!Ready)
                    XinEditorPlugin.LogInfo("心外观预览已就绪：模型=" + _model.gameObject.name +
                        "，固定原点=特效播放挂点(0,0)，挂点类型=" + XinEditorPlugin.FrameEffectPoint);
                Ready = true;
                _message.text = "";
                ShowSprite(_body, body, new Vector2(_bodyOrigin.x, _bodyOrigin.y), _bodyScale);
                ShowSprite(_flame, flame, new Vector2(_settings.X, _settings.Y), new Vector2(_settings.ScaleX, _settings.ScaleY));
                float delta = Time.unscaledDeltaTime;
                _bodyTime += delta * _bodySpeed;
                float duration = _idle != null && _idle.IsUseOldMode ? _idle.IntervalTime
                    : _idle != null && _idle.SpriteDatas != null && _idle.SpriteDatas.Count > 0
                    ? _idle.SpriteDatas[_bodyIndex % _idle.SpriteDatas.Count].ShowCount * 0.1f : 0.1f;
                if (_bodyTime >= Math.Max(0.01f, duration))
                {
                    _bodyTime = 0f;
                    int count = _idle == null ? 1 : _idle.IsUseOldMode ? _idle.Sprites.Count : _idle.SpriteDatas.Count;
                    _bodyIndex = (_bodyIndex + 1) % Math.Max(1, count);
                }
                _flameTime += delta * Math.Abs(_effect.ConfigData.AnimSpeed);
                if (_flameTime >= Math.Max(0.001f, data.ExtraAnims[_flameIndex].ShowCount * 0.1f))
                {
                    _flameTime = 0f;
                    _flameIndex = (_flameIndex + 1) % data.ExtraAnims.Count;
                }
            }
            catch (Exception e)
            {
                _loading = false;
                Ready = false;
                _message.text = XinText.T("预览失败，请重新打开。", "Preview failed. Please reopen.");
                XinEditorPlugin.LogError("更新心外观预览失败：" + e);
            }
        }

        private static void ShowSprite(Image image, Sprite sprite, Vector2 position, Vector2 scale)
        {
            image.enabled = true;
            image.sprite = sprite;
            Rect rect = sprite.rect;
            image.rectTransform.pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
            image.rectTransform.anchoredPosition = position * PixelsPerUnit;
            image.rectTransform.sizeDelta = rect.size / sprite.pixelsPerUnit * PixelsPerUnit;
            image.rectTransform.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        internal void Release()
        {
            _generation++;
            _loading = false;
            Ready = false;
            _idle = null;
            _fallbackSprite = null;
            _model = null;
            _effect = null;
            _bodyIndex = _flameIndex = 0;
            _bodyTime = _flameTime = 0f;
            if (_body != null) { _body.sprite = null; _body.enabled = false; }
            if (_flame != null) { _flame.sprite = null; _flame.enabled = false; }
            if (_modelObject != null) UnityEngine.Object.Destroy(_modelObject);
            if (_effectObject != null) UnityEngine.Object.Destroy(_effectObject);
            _modelObject = _effectObject = null;
        }

        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }
    }
}
