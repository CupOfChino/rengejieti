using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class ValennaWarningArt
    {
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private const string SlotBadge = "ValennaSlotDanger";

        internal static Sprite GetSprite(string key)
        {
            if (!Sprites.TryGetValue(key, out var sprite) || sprite == null)
                Sprites[key] = sprite = StrikeArt.LoadSprite(key);
            return sprite;
        }

        internal static int Level(int skillId) => skillId switch
        {
            885126 or 885128 => 2,
            StrikeProfiles.ExecutionId or ValennaKit.PrescienceSkillId => 3,
            _ => 0
        };

        internal static Sprite Badge(int level) => GetSprite(level == 2 ? "danger_2" : "danger_3");

        internal static void SetSlot(RectTransform parent, int level)
        {
            if (parent == null) return;
            bool visible = level > 0;
            var badge = parent.Find(SlotBadge) as RectTransform;
            if (badge == null && !visible) return;
            if (badge == null)
            {
                badge = AddBadge(parent, SlotBadge, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f)).rectTransform;
                badge.anchoredPosition = Vector2.zero;
            }
            if (visible)
            {
                float edge = Mathf.Clamp(parent.rect.height * 0.57f, 16f, 34f);
                Image image = badge.GetComponent<Image>();
                Sprite sprite = Badge(level);
                if (image.sprite != sprite) image.sprite = sprite;
                Rect art = sprite.rect;
                var size = new Vector2(edge, edge * art.height / art.width);
                if (badge.sizeDelta != size) badge.sizeDelta = size;
            }
            if (badge.gameObject.activeSelf != visible) badge.gameObject.SetActive(visible);
        }

        internal static Image AddBadge(RectTransform parent, string name, Vector2 anchor, Vector2 pivot)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            var image = child.GetComponent<Image>();
            child.GetComponent<LayoutElement>().ignoreLayout = true;
            image.rectTransform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = anchor;
            image.rectTransform.pivot = pivot;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }
    }

    internal sealed class ValennaMenuWarning : MonoBehaviour
    {
        private Image _left, _right;
        private RawImage _strip;
        private RectTransform _rect;
        private float _tileWidth;

        internal void Bind(int level, bool masked)
        {
            if (_strip == null)
            {
                _rect = (RectTransform)transform;
                _left = ValennaWarningArt.AddBadge(_rect, "DangerLeft", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                _right = ValennaWarningArt.AddBadge(_rect, "DangerRight", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                var bar = new GameObject("WarningStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
                bar.GetComponent<LayoutElement>().ignoreLayout = true;
                _strip = bar.GetComponent<RawImage>();
                _strip.raycastTarget = false;
                _strip.rectTransform.SetParent(_rect, false);
                _strip.rectTransform.anchorMin = Vector2.up;
                _strip.rectTransform.anchorMax = Vector2.one;
                _strip.rectTransform.pivot = new Vector2(0.5f, 1f);
            }
            _left.sprite = _right.sprite = ValennaWarningArt.Badge(level);
            Texture texture = ValennaWarningArt.GetSprite(level == 2 ? "warning_2" : "warning_3").texture;
            texture.wrapMode = TextureWrapMode.Repeat;
            _strip.texture = texture;
            Color tint = new Color(1f, 1f, 1f, masked ? 0.57f : 1f);
            _left.color = _right.color = _strip.color = tint;
            _left.enabled = _right.enabled = _strip.enabled = enabled = true;
            Fit();
        }

        internal void Hide()
        {
            if (_strip != null) _left.enabled = _right.enabled = _strip.enabled = false;
            enabled = false;
        }

        private void OnRectTransformDimensionsChange() { if (_strip?.texture != null && enabled) Fit(); }

        private void Fit()
        {
            float edge = Mathf.Clamp(_rect.rect.height * 0.55f, 12f, 30f);
            _left.rectTransform.sizeDelta = _right.rectTransform.sizeDelta = new Vector2(edge, edge);
            _left.rectTransform.anchoredPosition = new Vector2(3f, 0f);
            _right.rectTransform.anchoredPosition = new Vector2(-3f, 0f);
            float height = Mathf.Clamp(_rect.rect.height * 0.21f, 5f, 12f);
            _strip.rectTransform.sizeDelta = new Vector2(-8f, height);
            _strip.rectTransform.anchoredPosition = Vector2.zero;
            _tileWidth = height * _strip.texture.width / _strip.texture.height;
            Scroll();
        }

        private void Update() => Scroll();

        private void Scroll()
        {
            if (_tileWidth <= 0f) return;
            float width = Mathf.Max(0f, _strip.rectTransform.rect.width);
            _strip.uvRect = new Rect(Mathf.Repeat(-Time.unscaledTime * 36f / _tileWidth, 1f), 0f, width / _tileWidth, 1f);
        }
    }
}
