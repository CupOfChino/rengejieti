using System.Collections.Generic;
using Game;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal sealed class ValennaSkillFrame : MaskableGraphic
    {
        private const string OverlayName = "ValennaSkillFrame";
        private static readonly Color32 Blue = new Color32(80, 176, 250, 255);
        private static readonly Color32 Yellow = new Color32(246, 216, 85, 255);
        private static readonly Color32 Orange = new Color32(255, 151, 58, 255);
        private static readonly Color32 Red = new Color32(249, 49, 64, 255);
        private static readonly int[] ExclamationRows = { 7, 7, 2, 2, 0, 2 };
        private Color _accent;
        private bool _warning;
        private bool _selected;
        private bool _masked;
        private float _phase;
        private float _nextPaint;

        internal static bool TryColor(int skillId, out Color32 accent)
        {
            accent = skillId switch
            {
                885121 or 885126 => Blue,
                885124 or 885127 => Yellow,
                StrikeProfiles.RabbitId or 885128 => Orange,
                StrikeProfiles.ExecutionId => Red,
                _ => default
            };
            return accent.a != 0;
        }

        private static void SetMenuFrame(BattleSkillSelectElement element)
        {
            if (element.ImageSwitch_Bg == null) return;
            int skillId = element.SkillData?.Id ?? 0;
            if (!TryColor(skillId, out Color32 accent) || !ValennaPlugin.HasPalermo(BattleHelper.BattleHeroInfo))
            {
                Hide(element.ImageSwitch_Bg.transform);
                return;
            }
            bool selected = element.Image_ChooseFrame != null && element.Image_ChooseFrame.gameObject.activeSelf;
            Show(element.ImageSwitch_Bg.transform, accent, skillId == StrikeProfiles.ExecutionId, selected, element.Masked);
        }

        private static void Show(Transform parent, Color accent, bool warning, bool selected, bool masked)
        {
            Transform existing = parent.Find(OverlayName);
            ValennaSkillFrame overlay;
            if (existing == null)
            {
                var item = new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(LayoutElement), typeof(ValennaSkillFrame));
                var rect = (RectTransform)item.transform;
                rect.SetParent(parent, false);
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                item.GetComponent<LayoutElement>().ignoreLayout = true;
                overlay = item.GetComponent<ValennaSkillFrame>();
                overlay.raycastTarget = false;
            }
            else overlay = existing.GetComponent<ValennaSkillFrame>();

            overlay._accent = accent;
            overlay._warning = warning;
            overlay._selected = selected;
            overlay._masked = masked;
            overlay.gameObject.SetActive(true);
            overlay.SetVerticesDirty();
        }

        private static void Hide(Transform parent)
        {
            Transform existing = parent?.Find(OverlayName);
            if (existing != null) existing.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(BattleSkillSelectElement), nameof(BattleSkillSelectElement.SetSkillElement))]
        private static class BindMenu
        {
            // 原生异步方法先同步完成 Reset 和 SkillData 绑定，再等待添加按钮监听。
            private static void Postfix(BattleSkillSelectElement __instance) => SetMenuFrame(__instance);
        }

        [HarmonyPatch(typeof(BattleSkillSelectElement), nameof(BattleSkillSelectElement.SetSelectState))]
        private static class RefreshSelection
        {
            private static void Postfix(BattleSkillSelectElement __instance) => SetMenuFrame(__instance);
        }

        [HarmonyPatch(typeof(BattleSkillSelectElement), nameof(BattleSkillSelectElement.SetMasked))]
        private static class RefreshMask
        {
            private static void Postfix(BattleSkillSelectElement __instance) => SetMenuFrame(__instance);
        }

        [HarmonyPatch(typeof(BattleSkillSelectElement), nameof(BattleSkillSelectElement.Reset))]
        private static class ResetMenu
        {
            private static void Prefix(BattleSkillSelectElement __instance) => Hide(__instance.ImageSwitch_Bg?.transform);
        }

        [HarmonyPatch(typeof(UIBattlePanel), "_ClearBattleSkillSelectElement")]
        private static class RecycleMenu
        {
            private static void Prefix(List<BattleSkillSelectElement> ____skillSelectElement)
            {
                if (____skillSelectElement == null) return;
                foreach (BattleSkillSelectElement element in ____skillSelectElement)
                    if (element != null) Hide(element.ImageSwitch_Bg?.transform);
            }
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.ShowSkillIcon))]
        private static class BindActionIcon
        {
            private static void Postfix(HUDElement __instance, LocalizationKeyData __1, bool __3)
            {
                if (__instance.Image_SkillIcon == null) return;
                Transform parent = __instance.Image_SkillIcon.transform;
                BattleRole actor = __instance.Model?.Role;
                var skill = actor?.CurrentBehaviourData?.BattleSkillData;
                // 切换行动种类也会显示一个通用图标；只装饰已绑定的实际技能。
                if (__3 && ValennaPlugin.HasPalermo(actor) && skill?.IsItemSkill == false &&
                    __1 != null && ReferenceEquals(__1, skill.NameKey) && TryColor(skill.Id, out Color32 accent))
                    Show(parent, accent, warning: false, selected: false, masked: false);
                else
                    Hide(parent);
            }
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.CloseSkillIcon))]
        private static class ResetActionIcon
        {
            private static void Prefix(HUDElement __instance) => Hide(__instance.Image_SkillIcon?.transform);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (!_warning || now < _nextPaint) return;
            _nextPaint = now + 1f / 25f;
            _phase = now * 45f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect box = GetPixelAdjustedRect();
            if (box.width < 10f || box.height < 5f) return;
            float width = Mathf.Clamp(box.height * 0.054f, 1.5f, 3f);
            float chamfer = _warning ? Mathf.Min(box.height * 0.22f, 8f) : 1.5f;
            Color border = _masked ? Color.Lerp(_accent, Color.gray, 0.69f) :
                _selected ? Color.Lerp(_accent, Color.white, 0.32f) : _accent;
            border.a = _masked ? 0.64f : 0.94f;
            Stroke(mesh, box, chamfer, width, border);
            Rect inset = Rect.MinMaxRect(box.xMin + width, box.yMin + width, box.xMax - width, box.yMax - width);
            border.a *= 0.26f;
            Stroke(mesh, inset, Mathf.Max(0f, chamfer - width * 0.5f), 1f, border);
            if (_warning) PaintWarning(mesh, box);
        }

        private void PaintWarning(VertexHelper mesh, Rect box)
        {
            float height = Mathf.Clamp(box.height * 0.31f, 8f, 15f);
            Rect strip = Rect.MinMaxRect(box.xMin + 7f, box.yMax - height * 0.38f,
                box.xMax - 7f, box.yMax + height * 0.62f);
            if (strip.width < 10f) return;
            Color baseColor = new Color(0.12f, 0.018f, 0.025f, 0.94f);
            Box(mesh, strip, baseColor);
            Color rail = new Color(0.66f, 0.079f, 0.12f, _masked ? 0.46f : 0.92f);
            Stroke(mesh, strip, 1.5f, 0.7f, rail);

            float cell = (height - 1.6f) / ExclamationRows.Length;
            float pitch = Mathf.Max(30f, cell * 20f);
            float start = strip.xMin + 1f, end = strip.xMax - 1f;
            float offset = Mathf.Repeat(_phase, pitch);
            Color light = _masked ? new Color(0.85f, 0.18f, 0.24f, 0.60f) : new Color(1f, 0.20f, 0.28f, 1f);
            Color halo = new Color(1f, 0.06f, 0.14f, _masked ? 0.14f : 0.3f);
            for (float x = start + offset - pitch; x < end; x += pitch)
            {
                for (int row = 0; row < ExclamationRows.Length; row++)
                {
                    float y = strip.yMax - 0.8f - (row + 1) * cell;
                    for (int column = 0; column < 3; column++)
                    {
                        if ((ExclamationRows[row] & (1 << column)) == 0) continue;
                        float left = x + column * cell;
                        LedDot(mesh, left, y, cell, start, end, halo);
                        LedDot(mesh, left + cell * 0.16f, y + cell * 0.16f, cell * 0.68f, start, end, light);
                    }
                }
            }
        }

        private static void LedDot(VertexHelper mesh, float x, float y, float size, float start, float end, Color color)
        {
            float left = Mathf.Max(start, x), right = Mathf.Min(end, x + size);
            if (left < right) Box(mesh, Rect.MinMaxRect(left, y, right, y + size), color);
        }

        private static void Stroke(VertexHelper mesh, Rect outer, float chamfer, float line, Color color)
        {
            Rect inner = Rect.MinMaxRect(outer.xMin + line, outer.yMin + line, outer.xMax - line, outer.yMax - line);
            if (inner.width <= 0f || inner.height <= 0f) return;
            float innerChamfer = Mathf.Max(0f, chamfer - line * 0.65f);
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                Quad(mesh, Corner(outer, chamfer, i), Corner(outer, chamfer, next),
                    Corner(inner, innerChamfer, next), Corner(inner, innerChamfer, i), color);
            }
        }

        private static Vector2 Corner(Rect rect, float cut, int index) => index switch
        {
            0 => new Vector2(rect.xMin + cut, rect.yMax),
            1 => new Vector2(rect.xMax - cut, rect.yMax),
            2 => new Vector2(rect.xMax, rect.yMax - cut),
            3 => new Vector2(rect.xMax, rect.yMin + cut),
            4 => new Vector2(rect.xMax - cut, rect.yMin),
            5 => new Vector2(rect.xMin + cut, rect.yMin),
            6 => new Vector2(rect.xMin, rect.yMin + cut),
            _ => new Vector2(rect.xMin, rect.yMax - cut)
        };

        private static void Box(VertexHelper mesh, Rect rect, Color color) =>
            Quad(mesh, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMin, rect.yMin), color);

        private static void Quad(VertexHelper mesh, Vector2 topLeft, Vector2 topRight,
            Vector2 bottomRight, Vector2 bottomLeft, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(topLeft, color, Vector2.zero);
            mesh.AddVert(topRight, color, Vector2.zero);
            mesh.AddVert(bottomRight, color, Vector2.zero);
            mesh.AddVert(bottomLeft, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
