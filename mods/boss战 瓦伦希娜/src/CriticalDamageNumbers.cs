using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class CriticalDamageNumbers
    {
        private static readonly AsyncLocal<Scope> Active = new AsyncLocal<Scope>();
        private static readonly ConditionalWeakTable<UIHudNumElement, AppliedStyle> Styles =
            new ConditionalWeakTable<UIHudNumElement, AppliedStyle>();
        private static readonly Color32 CriticalGold = new Color32(254, 247, 131, 255);
        private static readonly Color32 CriticalRed = new Color32(211, 31, 31, 255);
        private const float DamageAtDoubleSize = 30f;
        [ThreadStatic] private static Scope _drawing;

        internal static Scope Current { get => Active.Value; set => Active.Value = value; }

        internal sealed class Scope
        {
            internal readonly HUDElement Hud;
            internal bool Used;
            internal Scope(HUDElement hud) { Hud = hud; }
        }

        private sealed class AppliedStyle
        {
            internal readonly Outline Outline;
            internal Vector3 OriginalScale;
            internal Color OriginalColor;
            internal bool Applied;

            internal AppliedStyle(Text text)
            {
                Outline = text.gameObject.AddComponent<Outline>();
                Outline.effectColor = CriticalRed;
                Outline.effectDistance = new Vector2(1.25f, -1.25f);
            }
        }

        [HarmonyPatch(typeof(HUDElement), nameof(HUDElement.ShowHudNum))]
        private static class SameHud
        {
            private static void Prefix(HUDElement __instance, bool __2, out Scope __state)
            {
                __state = _drawing;
                Scope hit = Current;
                _drawing = __2 && hit != null && !hit.Used && hit.Hud == __instance ? hit : null;
            }

            private static void Finalizer(Scope __state) { _drawing = __state; }
        }

        [HarmonyPatch(typeof(UIHudNumElement), nameof(UIHudNumElement.Reset))]
        private static class ResetPooledStyle
        {
            private static void Postfix(UIHudNumElement __instance)
            {
                var text = __instance.Text_Value;
                if (text == null || !Styles.TryGetValue(__instance, out AppliedStyle style) || !style.Applied)
                    return;

                text.rectTransform.localScale = style.OriginalScale;
                text.color = style.OriginalColor;
                style.Outline.enabled = false;
                style.Applied = false;
            }
        }

        [HarmonyPatch(typeof(UIHudNumElement), nameof(UIHudNumElement.SetElement),
            new[] { typeof(int), typeof(bool), typeof(UnityAction<UIHudNumElement>) })]
        private static class GoldCritical
        {
            private static void Postfix(UIHudNumElement __instance, int __0, bool __1)
            {
                if (!__1 || _drawing == null || _drawing.Used || __instance.Text_Value == null) return;

                var text = __instance.Text_Value;
                AppliedStyle style = Styles.GetValue(__instance, _ => new AppliedStyle(text));
                style.OriginalScale = text.rectTransform.localScale;
                style.OriginalColor = text.color;
                style.Applied = true;
                float scale = 1f + Mathf.Clamp01(Mathf.Abs((float)__0) / DamageAtDoubleSize);
                text.rectTransform.localScale = style.OriginalScale * scale;
                text.color = CriticalGold;
                style.Outline.enabled = true;
                _drawing.Used = true;
            }
        }
    }
}
