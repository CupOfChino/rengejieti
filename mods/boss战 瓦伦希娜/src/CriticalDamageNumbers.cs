using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace ValennaBattle
{
    internal static class CriticalDamageNumbers
    {
        private static readonly AsyncLocal<Scope> Active = new AsyncLocal<Scope>();
        private static readonly ConditionalWeakTable<UIHudNumElement, FontSize> Sizes =
            new ConditionalWeakTable<UIHudNumElement, FontSize>();
        [ThreadStatic] private static Scope _drawing;

        internal static Scope Current { get => Active.Value; set => Active.Value = value; }

        internal sealed class Scope
        {
            internal readonly HUDElement Hud;
            internal bool Used;
            internal Scope(HUDElement hud) { Hud = hud; }
        }

        private sealed class FontSize
        {
            internal readonly int Value;
            internal FontSize(int value) { Value = value; }
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

            private static void Postfix(Scope __state) { _drawing = __state; }
        }

        [HarmonyPatch(typeof(UIHudNumElement), nameof(UIHudNumElement.Reset))]
        private static class ResetPooledFont
        {
            private static void Postfix(UIHudNumElement __instance)
            {
                if (__instance.Text_Value != null && Sizes.TryGetValue(__instance, out FontSize size))
                    __instance.Text_Value.fontSize = size.Value;
            }
        }

        [HarmonyPatch(typeof(UIHudNumElement), nameof(UIHudNumElement.SetElement),
            new[] { typeof(int), typeof(bool), typeof(UnityAction<UIHudNumElement>) })]
        private static class GoldCritical
        {
            private static void Postfix(UIHudNumElement __instance, bool __1)
            {
                if (!__1 || _drawing == null || _drawing.Used || __instance.Text_Value == null) return;

                var text = __instance.Text_Value;
                FontSize size = Sizes.GetValue(__instance, _ => new FontSize(text.fontSize));
                text.fontSize = Mathf.RoundToInt(size.Value * 1.38f);
                text.color = new Color32(255, 207, 90, 255);
                _drawing.Used = true;
            }
        }
    }
}
