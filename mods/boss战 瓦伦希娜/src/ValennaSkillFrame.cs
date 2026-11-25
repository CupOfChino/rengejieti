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
        private Color _accent;
        private bool _warning;
        private bool _selected;
        private bool _masked;

        internal static bool TryColor(int skillId, out Color32 accent)
        {
            accent = skillId switch
            {
                885121 or 885126 => Blue,
                885124 or 885127 => Yellow,
                StrikeProfiles.RabbitId or 885128 => Orange,
                StrikeProfiles.ExecutionId or ValennaKit.PrescienceSkillId => Red,
                _ => default
            };
            return accent.a != 0;
        }

        private static void SetMenuFrame(BattleSkillSelectElement element)
        {
            if (element.ImageSwitch_Bg == null) return;
            int skillId = element.SkillData?.Id ?? 0;
            int level = ValennaWarningArt.Level(skillId);
            if (!TryColor(skillId, out Color32 accent) ||
                (level == 0 && !ValennaPlugin.HasPalermo(BattleHelper.BattleHeroInfo)))
            {
                Hide(element.ImageSwitch_Bg.transform);
                return;
            }
            bool selected = element.Image_ChooseFrame != null && element.Image_ChooseFrame.gameObject.activeSelf;
            Show(element.ImageSwitch_Bg.transform, accent, level, selected, element.Masked);
        }

        private static void Show(Transform parent, Color accent, int level, bool selected, bool masked)
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
            overlay._warning = level > 0;
            overlay._selected = selected;
            overlay._masked = masked;
            overlay.gameObject.SetActive(true);
            var art = overlay.GetComponent<ValennaMenuWarning>();
            if (level > 0)
            {
                overlay.rectTransform.SetAsLastSibling();
                if (art == null) art = overlay.gameObject.AddComponent<ValennaMenuWarning>();
                art.Bind(level, masked);
            }
            else
            {
                if (art != null) art.Hide();
                overlay.rectTransform.SetAsFirstSibling();
            }
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
                    Show(parent, accent, level: 0, selected: false, masked: false);
                else
                    Hide(parent);
            }
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.CloseSkillIcon))]
        private static class ResetActionIcon
        {
            private static void Prefix(HUDElement __instance) => Hide(__instance.Image_SkillIcon?.transform);
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
