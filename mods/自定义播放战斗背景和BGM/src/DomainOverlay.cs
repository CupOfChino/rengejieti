// 领域展开用的角色头顶文字 + 画面边缘黑色暗角。
//
// 黑色椭圆底框在原版 UI 下无法稳定渲染，已移除；现在只绘制文字和全屏暗角。
// 文字长度变化时按 Text 的 preferred size 重新计算排版区域。
// 定位优先级：
//   1) 技能牌 / 编辑行动牌存在时，放在行动框上边框中点上方；
//   2) 没有行动框时，放在 RoleModel.Trans_Head（模型顶边）上方。
// 每帧跟随角色移动；容器 pivot 在底边，底边与锚点保留少量间距。
//
// 原版参考：
//   · ShowBlackScreenOption -> UIScreenTipPanel.AutoShowTip：全屏黑屏，不是本效果；
//   · ShowHUDTextOption：原版 Boss 的头顶短文字；
//   · RoleModel.ShowDialog：角色头像对话框，不是本效果。
// 均记录在 docs\AI协作文档.md，后续做剧情/秒杀演出时可以复用。

using System;
using System.Collections;
using Game;
using UnityEngine;
using UnityEngine.UI;

namespace CustomBattleBg
{
    internal class DomainTextBoxOverlay : MonoBehaviour
    {
        private static DomainTextBoxOverlay _instance;

        private CanvasGroup _group;
        private Canvas _canvas;
        private RectTransform _canvasRt;
        private RectTransform _boxRt;
        private Text _text;
        private Transform _anchor;
        private BattleRole _caster;
        private Coroutine _showCo;

        internal static void Show(string content, string prefix, int startChar, int charCount,
            BattleRole caster, float duration)
        {
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }
                DomainTextBoxOverlay overlay = Ensure();
                if (overlay == null)
                {
                    return;
                }
                overlay.ShowInternal(content, prefix, startChar, charCount, caster,
                    Mathf.Clamp(duration, 0.5f, 30f));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("显示头顶文字框失败：" + e);
            }
        }

        internal static void Hide()
        {
            if (_instance == null)
            {
                return;
            }
            if (_instance._showCo != null)
            {
                _instance.StopCoroutine(_instance._showCo);
                _instance._showCo = null;
            }
            if (_instance._group != null)
            {
                _instance._group.alpha = 0f;
            }
            _instance._anchor = null;
            _instance._caster = null;
            _instance.gameObject.SetActive(false);
        }

        private static DomainTextBoxOverlay Ensure()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("CustomBattleBgHeadTextBox",
                    typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<DomainTextBoxOverlay>();
                _instance.Build();
            }
            return _instance;
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            _canvas = canvas;
            // 先恢复能稳定显示文字/暗角的 Overlay；Camera 模式在这套 UI 下会让整个 Canvas 消失。
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingLayerName = "UI";
            canvas.sortingOrder = 30000;   // 战斗开始行动槽 HUD 还在时也压在上面

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRt = gameObject.GetComponent<RectTransform>();
            _canvasRt.anchorMin = Vector2.zero;
            _canvasRt.anchorMax = Vector2.one;
            _canvasRt.offsetMin = Vector2.zero;
            _canvasRt.offsetMax = Vector2.zero;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 画面边缘暗角：中心透明、边缘黑色。
            GameObject vignetteGo = new GameObject("Vignette", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(EdgeVignetteGraphic));
            vignetteGo.transform.SetParent(transform, false);
            EdgeVignetteGraphic vignette = vignetteGo.GetComponent<EdgeVignetteGraphic>();
            vignette.raycastTarget = false;
            UIFactory.Stretch(vignetteGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            // 文字容器；不绘制黑色底框，只保留文字和边缘暗角。
            GameObject bubbleGo = new GameObject("Bubble", typeof(RectTransform),
                typeof(CanvasRenderer));
            bubbleGo.transform.SetParent(transform, false);
            _boxRt = bubbleGo.GetComponent<RectTransform>();
            _boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            _boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            _boxRt.pivot = new Vector2(0.5f, 0f);   // 底边对齐到头顶
            _boxRt.sizeDelta = new Vector2(360f, 92f);

            _text = UIFactory.Label(bubbleGo.transform, "Text", "", 30, TextAnchor.MiddleCenter);
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Stretch(_text.rectTransform, 28f, 28f, 16f, 16f);

            CustomBattleBgPlugin.LogInfo("头顶文字显示已创建：文字 + 边缘暗角（不绘制黑底框）");
            gameObject.SetActive(false);
        }

        private void ShowInternal(string content, string prefix, int startChar, int charCount,
            BattleRole caster, float duration)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();

            _anchor = null;
            _caster = caster;
            try
            {
                if (caster != null && caster.Model != null)
                {
                    _anchor = caster.Model.Trans_Head != null
                        ? caster.Model.Trans_Head
                        : caster.Model.Trans_Center;
                }
            }
            catch (Exception)
            {
            }

            _text.text = content;
            ResizeToText(content);
            UpdatePosition();
            try
            {
                EdgeVignetteGraphic vignette = GetComponentInChildren<EdgeVignetteGraphic>(true);
                if (vignette != null)
                {
                    vignette.SetAllDirty();
                }
            }
            catch (Exception)
            {
            }
            try
            {
                CustomBattleBgPlugin.LogInfo("头顶文字框状态：rect=" + (_boxRt != null ? _boxRt.rect.ToString() : "null") +
                    "，canvasRect=" + (_canvasRt != null ? _canvasRt.rect.ToString() : "null") +
                    "，mode=" + (HasHudAnchor() ? "行动框" : "模型") +
                    "，pos=" + (_boxRt != null ? _boxRt.anchoredPosition.ToString() : "null") +
                    "，active=" + (_boxRt != null && _boxRt.gameObject.activeInHierarchy));
            }
            catch (Exception)
            {
            }

            DomainDialogFloatEffect fx = _text.GetComponent<DomainDialogFloatEffect>();
            if (charCount > 0 && !string.IsNullOrEmpty(prefix))
            {
                if (fx == null)
                {
                    fx = _text.gameObject.AddComponent<DomainDialogFloatEffect>();
                }
                fx.Configure(_text, prefix, startChar, charCount, duration + 2f);
            }
            else if (fx != null)
            {
                // 外部 API 的普通文字不启用逐字浮动；把旧配置清掉，避免复用上一次的区间。
                fx.Configure(_text, "", 0, 0, duration + 2f);
            }

            if (_showCo != null)
            {
                StopCoroutine(_showCo);
            }
            _showCo = StartCoroutine(ShowCo(duration));
        }

        private void ResizeToText(string content)
        {
            try
            {
                float maxWidth = 720f;
                TextGenerationSettings settings = _text.GetGenerationSettings(new Vector2(maxWidth, 0f));
                settings.horizontalOverflow = HorizontalWrapMode.Wrap;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                TextGenerator generator = new TextGenerator();
                float width = generator.GetPreferredWidth(content, settings) / Mathf.Max(_text.pixelsPerUnit, 1f);
                float height = generator.GetPreferredHeight(content, settings) / Mathf.Max(_text.pixelsPerUnit, 1f);
                width = Mathf.Clamp(width + 70f, 260f, maxWidth + 70f);
                height = Mathf.Clamp(height + 42f, 80f, 320f);
                _boxRt.sizeDelta = new Vector2(width, height);
                CustomBattleBgPlugin.LogInfo("头顶文字框尺寸：" + width + "×" + height +
                    "（文字 " + content.Length + " 字符）");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("计算头顶文字框大小失败：" + e.Message);
                _boxRt.sizeDelta = new Vector2(360f, 92f);
            }
        }

        private void UpdatePosition()
        {
            if (_canvasRt == null || _boxRt == null)
            {
                return;
            }
            try
            {
                Vector2 screen;
                bool aboveHud;
                if (!TryGetAnchorScreen(out screen, out aboveHud))
                {
                    return;
                }
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, screen,
                    null, out local))
                {
                    return;
                }

                Vector2 size = _boxRt.sizeDelta;
                Rect rect = _canvasRt.rect;
                // 有行动框时贴在行动框上方，没有行动框时贴在角色模型顶边上方。
                local.y += aboveHud ? 16f : 28f;
                local.x = Mathf.Clamp(local.x,
                    rect.xMin + size.x * 0.5f + 12f,
                    rect.xMax - size.x * 0.5f - 12f);
                // 优先保证底边高于角色头顶；角色靠屏幕顶端时允许框顶部略微超出。
                _boxRt.anchoredPosition = local;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("更新头顶文字框位置失败：" + e.Message);
            }
        }

        private bool TryGetAnchorScreen(out Vector2 screen, out bool aboveHud)
        {
            screen = Vector2.zero;
            aboveHud = false;
            try
            {
                if (_caster != null && _caster.Model != null)
                {
                    HUDElement hud = _caster.Model.Hud;
                    if (hud != null)
                    {
                        RectTransform slotRect = null;
                        if (hud.Trans_SkillShow != null &&
                            hud.Trans_SkillShow.gameObject.activeInHierarchy)
                        {
                            // 和「可视化攻击目标」一致：先取整块技能牌的实际矩形，
                            // 再退回技能图标/容器，避免用到左上角先攻数字牌的边角。
                            if (hud.Anim_SkillShow != null &&
                                hud.Anim_SkillShow.gameObject.activeInHierarchy)
                            {
                                slotRect = hud.Anim_SkillShow.transform as RectTransform;
                            }
                            if (slotRect == null && hud.Button_SkillIcon != null &&
                                hud.Button_SkillIcon.gameObject.activeInHierarchy)
                            {
                                slotRect = hud.Button_SkillIcon.transform as RectTransform;
                            }
                            if (slotRect == null)
                            {
                                slotRect = hud.Trans_SkillShow as RectTransform;
                            }
                            if (slotRect == null)
                            {
                                slotRect = hud.Trans_SkillShow.GetComponent<RectTransform>();
                            }
                        }
                        if (slotRect == null && hud.Trans_EditorAction != null &&
                            hud.Trans_EditorAction.gameObject.activeInHierarchy)
                        {
                            slotRect = hud.Trans_EditorAction as RectTransform;
                            if (slotRect == null)
                            {
                                slotRect = hud.Trans_EditorAction.GetComponent<RectTransform>();
                            }
                        }
                        if (slotRect != null)
                        {
                            // HUD 属于它自己的 Canvas；Screen Space - Overlay 时
                            // worldCamera 为 null，此时换算结果就是屏幕坐标。
                            Canvas hudCanvas = hud.GetComponent<Canvas>();
                            Camera hudCam = hudCanvas != null ? hudCanvas.worldCamera : null;
                            Vector3[] corners = new Vector3[4];
                            slotRect.GetWorldCorners(corners);
                            // GetWorldCorners: 0=左下 1=左上 2=右上 3=右下
                            Vector3 world = (corners[1] + corners[2]) * 0.5f;
                            Vector3 sp = RectTransformUtility.WorldToScreenPoint(hudCam, world);
                            screen = new Vector2(sp.x, sp.y);
                            aboveHud = true;
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            if (_anchor != null)
            {
                Camera cam = GetCamera();
                if (cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(_anchor.position);
                    if (sp.z > 0f)
                    {
                        screen = new Vector2(sp.x, sp.y);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool HasHudAnchor()
        {
            try
            {
                if (_caster == null || _caster.Model == null)
                {
                    return false;
                }
                HUDElement hud = _caster.Model.Hud;
                if (hud == null)
                {
                    return false;
                }
                return (hud.Trans_SkillShow != null &&
                        hud.Trans_SkillShow.gameObject.activeInHierarchy) ||
                       (hud.Trans_EditorAction != null &&
                        hud.Trans_EditorAction.gameObject.activeInHierarchy);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Camera GetCamera()
        {
            try
            {
                if (BattleHelper.FightContent != null && BattleHelper.FightContent.Room != null &&
                    BattleHelper.FightContent.Room.RoomEntity != null)
                {
                    return BattleHelper.FightContent.Room.RoomEntity.Cam;
                }
            }
            catch (Exception)
            {
            }
            try
            {
                if (PrefabSingleton<UIRoot>.HasInstance)
                {
                    return PrefabSingleton<UIRoot>.Instance.CurrentCamera;
                }
            }
            catch (Exception)
            {
            }
            return Camera.main;
        }

        private void LateUpdate()
        {
            if (gameObject.activeInHierarchy)
            {
                UpdatePosition();
            }
        }

        private IEnumerator ShowCo(float duration)
        {
            _group.alpha = 0f;
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Clamp01(t / 0.18f);
                yield return null;
            }
            _group.alpha = 1f;

            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, duration - 0.18f));

            t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(t / 0.25f);
                yield return null;
            }
            _group.alpha = 0f;
            _showCo = null;
            _anchor = null;
            _caster = null;
            gameObject.SetActive(false);
        }

    }

    /// <summary>纯网格全屏暗角：中心透明，边缘和四角渐黑，不依赖贴图。</summary>
    internal class EdgeVignetteGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0.01f || rect.height <= 0.01f)
            {
                return;
            }

            const int columns = 16;
            const int rows = 16;
            float halfW = rect.width * 0.5f;
            float halfH = rect.height * 0.5f;
            for (int y = 0; y <= rows; y++)
            {
                float py = Mathf.Lerp(rect.yMin, rect.yMax, y / (float)rows);
                float ny = Mathf.Abs(py) / Mathf.Max(halfH, 0.01f);
                for (int x = 0; x <= columns; x++)
                {
                    float px = Mathf.Lerp(rect.xMin, rect.xMax, x / (float)columns);
                    float nx = Mathf.Abs(px) / Mathf.Max(halfW, 0.01f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float k = Mathf.Clamp01((d - 0.30f) / 0.75f);
                    k = k * k * 0.86f;
                    vh.AddVert(new Vector3(px, py, 0f),
                        new Color32(0, 0, 0, (byte)(Mathf.Clamp01(k) * 255f)), Vector2.zero);
                }
            }

            int stride = columns + 1;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int a = y * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int dIndex = c + 1;
                    vh.AddTriangle(a, c, b);
                    vh.AddTriangle(b, c, dIndex);
                }
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }
    }
}
