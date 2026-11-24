using System;
using System.Collections.Generic;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal sealed class StrikeVoiceCaption : MonoBehaviour, IDisposable
    {
        private static readonly Dictionary<BattleRole, StrikeVoiceCaption> Active =
            new Dictionary<BattleRole, StrikeVoiceCaption>();
        private static Font _font;

        private BattleRole _speaker;
        private BattleFightContent _fight;
        private RectTransform _canvasRect;
        private Text _label;
        private float _expiresAt;
        private bool _closed;

        internal static IDisposable Show(BattleRole speaker, string text, float duration)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (speaker?.Model == null || fight == null || !BattleHelper.IsInBattle ||
                string.IsNullOrWhiteSpace(text) || duration <= 0f ||
                float.IsNaN(duration) || float.IsInfinity(duration)) return null;

            Font font = GetFont();
            if (font == null) return null;
            if (Active.TryGetValue(speaker, out StrikeVoiceCaption previous)) previous.Dispose();

            var root = new GameObject("ValennaVoiceCaption", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            StrikeVoiceCaption caption = root.AddComponent<StrikeVoiceCaption>();
            caption._speaker = speaker;
            caption._fight = fight;
            caption._expiresAt = Time.unscaledTime + duration;
            Active.Add(speaker, caption);
            try
            {
                caption.Build(text, font);
                caption.LateUpdate();
                return caption;
            }
            catch
            {
                caption.Dispose();
                throw;
            }
        }

        private static Font GetFont()
        {
            if (_font != null) return _font;
            var fonts = MODToolConfig.Instance?.LocalizationFonts;
            if (fonts != null)
            {
                foreach (Font candidate in fonts)
                    if (candidate != null && candidate.HasCharacter('瓦') &&
                        candidate.HasCharacter('伦') && candidate.HasCharacter('娜'))
                        return _font = candidate;
            }
            return _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "SimSun" }, 32);
        }

        private void Build(string text, Font font)
        {
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingLayerName = "UI";
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = GetComponent<RectTransform>();

            var textObject = new GameObject("CaptionText", typeof(RectTransform), typeof(Text), typeof(Outline));
            textObject.transform.SetParent(transform, false);
            _label = textObject.GetComponent<Text>();
            _label.text = text;
            _label.font = font;
            _label.fontSize = 32;
            _label.fontStyle = FontStyle.Bold;
            _label.color = new Color32(255, 213, 139, 255);
            _label.alignment = TextAnchor.LowerCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.supportRichText = false;
            _label.raycastTarget = false;

            RectTransform rect = _label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(Mathf.Clamp(_label.preferredWidth + 8f, 40f, 550f), 1f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _label.preferredHeight + 4f);

            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = new Color32(40, 24, 16, 245);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private void LateUpdate()
        {
            if (_closed) return;
            if (Time.unscaledTime >= _expiresAt || !BattleHelper.IsInBattle ||
                !ReferenceEquals(_fight, BattleHelper.FightContent))
            {
                Dispose();
                return;
            }

            RoleModel model = _speaker.Model;
            Camera camera = BattleHelper.BattleCam ?? Camera.main;
            if (model == null || !model.gameObject.activeInHierarchy || camera == null)
            {
                _label.enabled = false;
                return;
            }

            SpriteRenderer body = model.SpriteRender;
            Vector3 head;
            if (model.Trans_Head != null) head = model.HeadPos;
            else if (body != null) head = new Vector3(body.bounds.center.x, body.bounds.max.y, body.bounds.center.z);
            else head = model.transform.position;

            Vector3 screen = camera.WorldToScreenPoint(head);
            Vector2 local = default;
            _label.enabled = screen.z > 0f &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local);
            if (!_label.enabled) return;

            RectTransform rect = _label.rectTransform;
            float halfWidth = rect.rect.width * 0.5f + 8f;
            Rect canvas = _canvasRect.rect;
            rect.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x, canvas.xMin + halfWidth, canvas.xMax - halfWidth),
                Mathf.Min(local.y + 18f, canvas.yMax - rect.rect.height - 8f));
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            Forget();
            if (this == null) return;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDestroy() => Forget();

        private void Forget()
        {
            if (_speaker != null && Active.TryGetValue(_speaker, out StrikeVoiceCaption current) &&
                ReferenceEquals(current, this))
                Active.Remove(_speaker);
        }

        [HarmonyPatch(typeof(BattleFightContent), nameof(BattleFightContent.UninstallOnRestart))]
        private static class Restart
        {
            private static void Prefix(BattleFightContent __instance)
            {
                foreach (StrikeVoiceCaption caption in new List<StrikeVoiceCaption>(Active.Values))
                    if (ReferenceEquals(caption._fight, __instance)) caption.Dispose();
            }
        }
    }
}
