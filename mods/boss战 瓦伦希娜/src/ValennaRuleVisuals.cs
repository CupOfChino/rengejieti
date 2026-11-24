using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class ValennaRuleVisuals
    {
        private const string EyeMarkup = "<color=#8CD6EB>预知眼</color>";
        private static List<int> _shownBuffs;

        private static bool IsOurs(int id) => id >= 885101 && id <= 885155;

        private static void Style(Text label, string text, bool activeSkill = false)
        {
            if (label == null) return;
            var inline = label.GetComponent<ValennaInlineRules>();
            bool styled = !string.IsNullOrEmpty(text) && (activeSkill || ValennaInlineRules.HasRuleMarkup(text));
            if (styled && inline == null) inline = label.gameObject.AddComponent<ValennaInlineRules>();
            if (inline != null) inline.Set(label, styled, activeSkill);

            if (!styled || !text.Contains(EyeMarkup)) return;
            label.supportRichText = true;
            EnsureRuleLine(label, strike: false);
        }

        private static void EnsureRuleLine(Text label, bool strike)
        {
            ValennaRuleLine line = label.GetComponentInChildren<ValennaRuleLine>(true);
            if (line == null)
            {
                var child = new GameObject("ValennaRuleLine", typeof(RectTransform), typeof(ValennaRuleLine));
                child.transform.SetParent(label.transform, false);
                line = child.GetComponent<ValennaRuleLine>();
                line.Bind(label, strike);
            }
            line.enabled = true;
        }

        private static void StyleIntro(Text label, BattleRole role)
        {
            if (label == null) return;
            if (ValennaPlugin.IsValennaPartner(role) && ValennaRuleLine.HasIntroMarks(label.text))
            {
                EnsureRuleLine(label, strike: true);
                return;
            }

            var line = label.GetComponentInChildren<ValennaRuleLine>(true);
            if (line != null) line.enabled = false;
        }

        private static async Task AfterIntro(Task original, Action update)
        {
            await original;
            update();
        }

        [HarmonyPatch(typeof(UICharacterDetailPanel), "_UpdateCharacterBaseInfo")]
        private static class CharacterDetailIntro
        {
            private static void Postfix(UICharacterDetailPanel __instance, ref Task __result) =>
                __result = AfterIntro(__result, () => StyleIntro(__instance.Text_CharacterDesc, __instance.CurRole));
        }

        [HarmonyPatch(typeof(UINewCharacterInfoPanel), nameof(UINewCharacterInfoPanel.InitTitleView))]
        private static class CharacterCardIntro
        {
            private static void Postfix(UINewCharacterInfoPanel __instance, ref Task __result) =>
                __result = AfterIntro(__result, () =>
                {
                    StyleIntro(__instance.Text_Role_Des_withicon, __instance.CurRole);
                    StyleIntro(__instance.Text_Role_Des_withouticon, __instance.CurRole);
                });
        }

        [HarmonyPatch(typeof(UIMagicInfoPanel), "_UpdateRoleInfo")]
        private static class MagicInfoIntro
        {
            private static void Postfix(UIMagicInfoPanel __instance, ref Task __result) =>
                __result = AfterIntro(__result, () => StyleIntro(__instance.Text_Role_Des, __instance.Role));
        }

        private static void SetTitleIcon(Text text, LocalizationKeyData key, string plain)
        {
            if (text == null) return;
            TextureResourceReference icon = null;
            if (_shownBuffs != null)
            {
                var factory = Singleton<ResManager>.Instance.BuffFactory;
                foreach (int id in _shownBuffs)
                {
                    var data = factory.Find(id);
                    if (data?.Name != null &&
                        (ReferenceEquals(data.Name, key) || (plain != null && data.Name.GetValue() == plain)))
                    {
                        icon = data.IconPathReference;
                        break;
                    }
                }
            }
            ValennaRuleIcon view = text.GetComponentInChildren<ValennaRuleIcon>(true);
            if (icon == null && view == null) return;
            if (view == null)
            {
                var child = new GameObject("ValennaBuffIcon", typeof(RectTransform), typeof(Image), typeof(ValennaRuleIcon));
                child.transform.SetParent(text.transform, false);
                view = child.GetComponent<ValennaRuleIcon>();
            }
            view.Set(text, icon);
        }

        [HarmonyPatch(typeof(GameHelper), nameof(GameHelper.SetLocalization),
            new[] { typeof(Text), typeof(LocalizationKeyData), typeof(object[]) })]
        private static class LocalizedRule
        {
            private static void Postfix(Text __0, LocalizationKeyData __1) => Style(__0, __1?.InputText);
        }

        [HarmonyPatch(typeof(UITraitDesElement), nameof(UITraitDesElement.Fresh))]
        private static class TraitDescription
        {
            private static void Postfix(UITraitDesElement __instance, string __0) => Style(__instance.Text_Des, __0);
        }

        [HarmonyPatch(typeof(UIDetailedTipPanel), nameof(UIDetailedTipPanel.SetItemDescription))]
        private static class MainItemDescription
        {
            private static void Postfix(MOD_Dynamic_Item __0, List<Transform> ____itemDescElements)
            {
                if (__0?.Config == null || !IsOurs(__0.Config.ItemId) || ____itemDescElements == null) return;
                foreach (Transform item in ____itemDescElements)
                {
                    Text text = item != null ? item.GetComponent<Text>() : null;
                    if (text != null) Style(text, text.text);
                }
            }
        }

        [HarmonyPatch(typeof(UIDetailTermDesElement), nameof(UIDetailTermDesElement.Fresh))]
        private static class ItemDescription
        {
            private static void Postfix(UIDetailTermDesElement __instance, string __0) => Style(__instance.Text_Des, __0);
        }

        [HarmonyPatch(typeof(UIBattlePanel), "_ShowSelectSkillInfo")]
        private static class SkillScope
        {
            private static void Prefix(BattleSkillData __3, out List<int> __state)
            {
                __state = _shownBuffs;
                _shownBuffs = __3 != null && IsOurs(__3.Id) ? __3.Config?.BuffsLink : null;
            }

            private static void Postfix(UIBattlePanel __instance, BattleSkillData __3)
            {
                Text label = __instance.Text_SelectSkillInfo;
                if (label != null) Style(label, label.text, __3 != null && IsOurs(__3.Id));
            }

            private static void Finalizer(List<int> __state) => _shownBuffs = __state;
        }

        [HarmonyPatch(typeof(UISpecialityInfoElement), nameof(UISpecialityInfoElement.SetElement),
            new[] { typeof(MOD_Dynamic_Trait), typeof(bool), typeof(ScrollRect) })]
        private static class TraitScope
        {
            private static void Prefix(MOD_Dynamic_Trait __0, out List<int> __state)
            {
                __state = _shownBuffs;
                _shownBuffs = __0 != null && IsOurs(__0.Id) ? __0.Config?.BuffsLink : null;
            }

            private static void Postfix(UISpecialityInfoElement __instance, MOD_Dynamic_Trait __0)
            {
                bool active = __0?.Config != null && IsOurs(__0.Id);
                var layout = __instance.GetComponent<ValennaTraitDescriptionLayout>();
                if (layout == null)
                {
                    if (!active) return;
                    layout = __instance.gameObject.AddComponent<ValennaTraitDescriptionLayout>();
                }

                Text label = __instance.Text_Desc;
                Style(label, __0?.Config?.Localization_Des?.InputText, active);
                if (!layout.Set(__instance.Trans_NormalDesc, active) && !active) return;
                if (__instance.Trans_ContentScrollView is RectTransform scroll)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll);
                    ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
                    if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
                }
            }

            private static void Finalizer(List<int> __state) => _shownBuffs = __state;
        }

        [HarmonyPatch(typeof(UIDetailedTipPanel), "_ShowAffixInfo")]
        private static class ItemScope
        {
            private static void Prefix(MOD_Dynamic_Item __0, out List<int> __state)
            {
                __state = _shownBuffs;
                var config = __0?.Config;
                _shownBuffs = config != null && IsOurs(config.ItemId) ? config.BuffsLink : null;
            }

            private static void Finalizer(List<int> __state) => _shownBuffs = __state;
        }

        [HarmonyPatch(typeof(UIBattleTermTitleElement), nameof(UIBattleTermTitleElement.Fresh))]
        private static class SkillTitle
        {
            private static void Postfix(UIBattleTermTitleElement __instance, LocalizationKeyData __0) =>
                SetTitleIcon(__instance.Text_Title, __0, null);
        }

        [HarmonyPatch(typeof(UITraitTitleElement), nameof(UITraitTitleElement.Fresh))]
        private static class TraitTitle
        {
            private static void Postfix(UITraitTitleElement __instance, string __0) =>
                SetTitleIcon(__instance.Text_Title, null, __0);
        }

        [HarmonyPatch(typeof(UIDetailTermTitleElement), nameof(UIDetailTermTitleElement.Fresh))]
        private static class ItemTitle
        {
            private static void Postfix(UIDetailTermTitleElement __instance, string __0) =>
                SetTitleIcon(__instance.Text_Title, null, __0);
        }
    }

    internal sealed class ValennaTraitDescriptionLayout : MonoBehaviour
    {
        private LayoutGroup _group;
        private TextAnchor _originalAlignment;
        private bool _active;

        internal bool Set(Transform normalDescription, bool active)
        {
            if (_active == active) return false;
            if (active)
            {
                _group = normalDescription?.GetComponent<LayoutGroup>();
                if (_group == null) return false;
                _originalAlignment = _group.childAlignment;
                _group.childAlignment = TextAnchor.UpperCenter;
            }
            else if (_group != null) _group.childAlignment = _originalAlignment;
            _active = active;
            return true;
        }
    }

    internal sealed class ValennaInlineRules : MonoBehaviour
    {
        // Unity 的旧版 Text 没有 sprite 标签。透明 ASCII 字槽只在对应图标已加载后插入。
        private const string Slot = "<color=#FE02FD01>M</color>\u2060";
        private static readonly Color32 SlotColor = new Color32(254, 2, 253, 1);
        private static readonly Regex StyledWord = new Regex(
            "(?<overheat><color=#8CD6EB>预知眼</color><color=#E77167>·过热</color>)|" +
            "<color=#(?:E77167|F35D50|FFA15E|77D1DC|E76160|EABA6D|8CD6EB|EBC556|79CAD7|CBB1DD|D196C7|AAC4D2|EE827B|F9CA75)>(?<word>[^<]+)</color>",
            RegexOptions.Compiled);
        private static readonly Dictionary<string, int> Buffs = new Dictionary<string, int>
        {
            { "燃烧", StrikeProfiles.BurnId }, { "烧伤", StrikeProfiles.BurnId },
            { "震颤", StrikeProfiles.TremorId }, { "灼热·震颤", StrikeProfiles.ScorchTremorId },
            { "震颤·灼热", StrikeProfiles.ScorchTremorId }, { "震颤引爆", StrikeProfiles.TremorBurstId },
            { "呼吸法", StrikeProfiles.BreathId }, { "预知眼", StrikeProfiles.EyeChargeId },
            { "加速的未来", StrikeProfiles.AcceleratedFutureId },
            { "加速弹", StrikeProfiles.AccelBulletId }, { "狩猎目标", StrikeProfiles.HuntingPreyId },
            { "伤害强化", ValennaKit.DamageUpId }, { "醉酒", StrikeProfiles.DrunkId },
            { "恐惧", 128 }, { "「望」", StrikeProfiles.FocusId }
        };
        private static readonly Dictionary<int, Task<Sprite>> Icons = new Dictionary<int, Task<Sprite>>();

        private readonly List<Image> _images = new List<Image>();
        private readonly List<Sprite> _run = new List<Sprite>();
        private readonly TextGenerator _generator = new TextGenerator();
        private Text _label;
        private TextAnchor _alignment;
        private VerticalWrapMode _wrap;
        private bool _richText;
        private string _source;
        private string _rendered;
        private Vector2 _size;
        private float _pixels;
        private int _revision;
        private bool _needsLayout;

        internal static bool HasRuleMarkup(string text) => !string.IsNullOrEmpty(text) && StyledWord.IsMatch(text);

        private static bool TryBuff(Match match, out int id)
        {
            if (match.Groups["overheat"].Success)
            {
                id = StrikeProfiles.EyeOverheatId;
                return true;
            }
            return Buffs.TryGetValue(match.Groups["word"].Value, out id);
        }

        internal void Set(Text label, bool styled, bool forceLayout)
        {
            if (_label == null)
            {
                _label = label;
                _alignment = label.alignment;
                _wrap = label.verticalOverflow;
                _richText = label.supportRichText;
            }

            bool active = styled || forceLayout;
            _label.alignment = active ? TextAnchor.UpperLeft : _alignment;
            _label.verticalOverflow = active ? VerticalWrapMode.Overflow : _wrap;
            _label.supportRichText = active || _richText;
            if (!active)
            {
                ++_revision;
                _source = _rendered = null;
                HideFrom(0);
            }
            enabled = active;
            _needsLayout = true;
        }

        private void LateUpdate()
        {
            if (_label == null || !_label.isActiveAndEnabled) return;

            string text = _label.text;
            if (text != _source && text != _rendered)
            {
                // 同一个 Text 会被原版菜单池复用。原文换掉时清理上次的图标。
                _source = text;
                _rendered = text;
                _run.Clear();
                HideFrom(0);
                if (HasRuleMarkup(text) && _label.font != null && _label.font.HasCharacter('M'))
                    _ = SetIcons(text, ++_revision);
                else ++_revision;
            }

            Vector2 size = _label.rectTransform.rect.size;
            float pixels = _label.pixelsPerUnit;
            if (_needsLayout || size != _size || !Mathf.Approximately(pixels, _pixels))
            {
                _needsLayout = false;
                _size = size;
                _pixels = pixels;
                LayoutIcons();
            }
        }

        private async Task SetIcons(string text, int revision)
        {
            try
            {
                MatchCollection matches = StyledWord.Matches(text);
                foreach (Match match in matches)
                {
                    if (!TryBuff(match, out int id)) continue;
                    if (!Icons.TryGetValue(id, out Task<Sprite> load))
                        Icons.Add(id, load = LoadIcon(id));
                    await load;
                }
                if (this == null || _label == null || revision != _revision ||
                    !isActiveAndEnabled || _label.text != text) return;

                _run.Clear();
                _rendered = StyledWord.Replace(text, match =>
                {
                    if (!TryBuff(match, out int id)) return match.Value;
                    Sprite sprite = Icons[id].Result;
                    if (sprite == null) return match.Value;
                    _run.Add(sprite);
                    return Slot + match.Value;
                });
                if (_label.text != _rendered)
                {
                    _label.text = _rendered;
                    LayoutRebuilder.MarkLayoutForRebuild(_label.rectTransform);
                }
                _needsLayout = true;
            }
            catch (Exception error) { ValennaPlugin.Instance.Report(error); }
        }

        private static async Task<Sprite> LoadIcon(int id)
        {
            var reference = Singleton<ResManager>.Instance.BuffFactory.Find(id)?.IconPathReference;
            if (reference == null) return null;
            Sprite loaded = null;
            await reference.GetResLoad((Sprite icon) => loaded = icon);
            return loaded;
        }

        private void LayoutIcons()
        {
            int index = 0;
            if (_run.Count > 0 && _pixels > 0.001f &&
                _generator.Populate(_label.text, _label.GetGenerationSettings(_size)))
            {
                IList<UIVertex> verts = _generator.verts;
                Color32 multiplied = (Color)SlotColor * _label.color;
                for (int i = 0; i + 3 < verts.Count && index < _run.Count; i += 4)
                {
                    UIVertex a = verts[i], c = verts[i + 2];
                    if (a.color.a > 1 || !(Similar(a.color, SlotColor) || Similar(a.color, multiplied))) continue;
                    Vector2 min = Vector2.Min(a.position, c.position) / _pixels;
                    Vector2 max = Vector2.Max(a.position, c.position) / _pixels;
                    if (max.x - min.x < 1f || max.y - min.y < 1f) continue;
                    Image image = GetImage(index);
                    float edge = Mathf.Min(max.x - min.x, _label.fontSize * 0.9f);
                    image.rectTransform.anchoredPosition = (min + max) * 0.5f;
                    image.rectTransform.sizeDelta = new Vector2(edge, edge);
                    image.sprite = _run[index++];
                    image.enabled = true;
                }
            }
            if (index != _run.Count)
            {
                // 某些字体或旧渲染器不产生透明字槽的顶点。还原正文，避免空位。
                _run.Clear();
                _rendered = _source;
                _label.text = _source;
                LayoutRebuilder.MarkLayoutForRebuild(_label.rectTransform);
                index = 0;
            }
            HideFrom(index);
        }

        private static bool Similar(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) < 3 && Mathf.Abs(a.g - b.g) < 3 && Mathf.Abs(a.b - b.b) < 3;

        private Image GetImage(int index)
        {
            if (index < _images.Count) return _images[index];
            var child = new GameObject("ValennaInlineBuff", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(transform, false);
            Image image = child.GetComponent<Image>();
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = _label.rectTransform.pivot;
            image.preserveAspect = true;
            image.raycastTarget = false;
            _images.Add(image);
            return image;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _images.Count; i++) _images[i].enabled = false;
        }

        private void OnDisable()
        {
            ++_revision;
            if (_run.Count == 0) _source = _rendered = null;
            HideFrom(0);
        }

        private void OnEnable() => _needsLayout = true;
    }

    internal sealed class ValennaRuleIcon : MonoBehaviour
    {
        private Text _title;
        private Vector2 _offset;
        private Image _image;
        private int _revision;

        internal async void Set(Text title, TextureResourceReference reference)
        {
            if (_title == null)
            {
                _title = title;
                _offset = title.rectTransform.offsetMin;
                _image = GetComponent<Image>();
                _image.preserveAspect = true;
                _image.raycastTarget = false;
            }

            int revision = ++_revision;
            _image.enabled = false;
            _title.rectTransform.offsetMin = _offset;
            if (reference == null) return;
            try
            {
                await reference.GetResLoad((Sprite sprite) =>
                {
                    if (this == null || _title == null || _revision != revision || sprite == null) return;
                    float size = _title.fontSize;
                    RectTransform icon = _image.rectTransform;
                    icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                    icon.sizeDelta = new Vector2(size, size);
                    icon.anchoredPosition = new Vector2(-size * 0.5f - 3f, 0f);
                    _title.rectTransform.offsetMin = _offset + new Vector2(size + 6f, 0f);
                    _image.sprite = sprite;
                    _image.enabled = true;
                });
            }
            catch (Exception error) { ValennaPlugin.Instance.Report(error); }
        }
    }

    internal sealed class ValennaRuleLine : MaskableGraphic
    {
        private static readonly Color32 EyeColor = new Color32(140, 214, 235, 255);
        private static readonly Color32 IntroColor = new Color32(255, 64, 64, 255);
        private static readonly Regex RedIntroWords = new Regex(
            "<color=#FF4040>([^<]+)</color>", RegexOptions.IgnoreCase);
        private Text _label;
        private readonly TextGenerator _generator = new TextGenerator();
        private bool _strike;
        private bool _originalRichText;

        internal static bool HasIntroMarks(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            MatchCollection matches = RedIntroWords.Matches(text);
            if (matches.Count != 2) return false;
            string first = matches[0].Groups[1].Value;
            string second = matches[1].Groups[1].Value;
            return (first == "二老板" && second == "七大家族") ||
                (first == "七大家族" && second == "二老板");
        }

        internal void Bind(Text label, bool strike)
        {
            _label = label;
            _strike = strike;
            _originalRichText = label.supportRichText;
            if (_strike) label.supportRichText = true;
            raycastTarget = false;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = label.rectTransform.pivot;
            rectTransform.offsetMin = rectTransform.offsetMax = Vector2.zero;
            label.RegisterDirtyVerticesCallback(SetVerticesDirty);
            SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_strike && _label != null) _label.supportRichText = true;
        }

        protected override void OnDisable()
        {
            if (_strike && _label != null) _label.supportRichText = _originalRichText;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_label != null) _label.UnregisterDirtyVerticesCallback(SetVerticesDirty);
            base.OnDestroy();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_label == null || !_label.isActiveAndEnabled) return;
            if (_strike ? !HasIntroMarks(_label.text) : !_label.text.Contains("<color=#8CD6EB>")) return;

            Color32 ink = _strike ? IntroColor : EyeColor;

            TextGenerationSettings settings = _label.GetGenerationSettings(_label.rectTransform.rect.size);
            if (!_generator.Populate(_label.text, settings)) return;
            IList<UIVertex> vertices = _generator.verts;
            float scale = 1f / _label.pixelsPerUnit;
            Color32 tintedInk = (Color)ink * _label.color;
            float start = 0f, end = 0f, bottom = 0f, top = 0f;
            bool pending = false;
            for (int i = 0; i + 3 < vertices.Count; i += 4)
            {
                UIVertex a = vertices[i], c = vertices[i + 2];
                bool marked = IsNear(a.color, ink) || IsNear(a.color, tintedInk);
                float left = Mathf.Min(a.position.x, c.position.x) * scale;
                float right = Mathf.Max(a.position.x, c.position.x) * scale;
                float y = Mathf.Min(a.position.y, c.position.y) * scale;
                float upper = Mathf.Max(a.position.y, c.position.y) * scale;
                bool contiguous = marked && left >= start && left - end <= _label.fontSize * 0.45f &&
                    Mathf.Abs(y - bottom) < _label.fontSize * 0.45f;
                if (pending && !contiguous)
                {
                    AddLine(mesh, start, end, bottom, top, ink);
                    pending = false;
                }
                if (!marked || right <= left) continue;
                if (!pending)
                {
                    start = left;
                    end = right;
                    bottom = y;
                    top = upper;
                    pending = true;
                }
                else
                {
                    end = Mathf.Max(end, right);
                    bottom = Mathf.Min(bottom, y);
                    top = Mathf.Max(top, upper);
                }
            }
            if (pending) AddLine(mesh, start, end, bottom, top, ink);
        }

        private static bool IsNear(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) < 3 && Mathf.Abs(a.g - b.g) < 3 && Mathf.Abs(a.b - b.b) < 3;

        private void AddLine(VertexHelper mesh, float left, float right, float bottom, float top, Color32 ink)
        {
            int start = mesh.currentVertCount;
            float y = _strike ? Mathf.Lerp(bottom, top, 0.55f) : bottom - 2f;
            float width = _strike ? Mathf.Max(1.2f, _label.fontSize * 0.08f) : 1.2f;
            ink.a = (byte)Mathf.RoundToInt(255f * _label.color.a);
            mesh.AddVert(new Vector3(left, y - width), ink, Vector2.zero);
            mesh.AddVert(new Vector3(left, y), ink, Vector2.zero);
            mesh.AddVert(new Vector3(right, y), ink, Vector2.zero);
            mesh.AddVert(new Vector3(right, y - width), ink, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
