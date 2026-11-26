using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Game.SkillData;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class ValennaTremor
    {
        private const int LeiCount = 686122775;
        private const int LeiStrength = 68612299;
        private const int LeiScorch = 68614495;
        private const int LeiBurst = 68612256;
        private const int RetiredBurn = 885133;

        private static readonly AsyncLocal<BuffData> InternalGrant = new AsyncLocal<BuffData>();
        private static readonly ConditionalWeakTable<BattleFightContent, StunMeters> Meters =
            new ConditionalWeakTable<BattleFightContent, StunMeters>();

        private sealed class StunMeters
        {
            internal readonly Dictionary<BattleRole, StunMeter> Values = new Dictionary<BattleRole, StunMeter>();
            internal readonly Dictionary<BattleRole, (BuffData Status, int Round)> Stuns =
                new Dictionary<BattleRole, (BuffData Status, int Round)>();
            internal readonly ConditionalWeakTable<BuffData, BattleRole> LeiBursts =
                new ConditionalWeakTable<BuffData, BattleRole>();
            internal bool? UsesLeiHeng;
        }

        private sealed class StunMeter
        {
            internal int Value;
            internal int StartingWillpower;
            internal bool StunPending;
        }

        internal static async Task BurstBatch(BattleRole source, BattleRole target, int attempts)
        {
            for (int i = 0; i < attempts && StrikeSequence.CanHit(target); i++)
                await target.AddBuff(source, StrikeProfiles.TremorBurstId);
        }

        private static bool UsesLeiHeng(StunMeters fight)
        {
            if (!fight.UsesLeiHeng.HasValue)
            {
                var buffs = Singleton<ResManager>.Instance.BuffFactory;
                fight.UsesLeiHeng = buffs.GetConfig(LeiCount) != null && buffs.GetConfig(LeiStrength) != null &&
                    buffs.GetConfig(LeiScorch) != null && buffs.GetConfig(LeiBurst) != null;
            }
            return fight.UsesLeiHeng.Value;
        }

        internal static bool IsScorching(BattleRole role) =>
            role.GetBuff(StrikeProfiles.ScorchTremorId) != null || role.GetBuff(LeiScorch) != null;

        internal static int CountLayers(BattleRole role)
        {
            BuffData count = role.GetBuff(LeiCount) ?? role.GetBuff(StrikeProfiles.ScorchTremorId) ??
                role.GetBuff(StrikeProfiles.TremorId);
            return count == null || count.DisableBuff ? 0 : Math.Max(0, count.CurLayer);
        }

        internal static async Task RoundEnd(BattleRole role)
        {
            BuffData tremor = role.GetBuff(StrikeProfiles.ScorchTremorId) ?? role.GetBuff(StrikeProfiles.TremorId);
            if (tremor != null && tremor.CurLayer > 0)
                await tremor.ChangeLayer(role, -1);
            await ClearBurstStun(role, endOfBattle: false);
        }

        internal static async Task ClearBurstStun(BattleRole role, bool endOfBattle)
        {
            BattleFightContent battle = BattleHelper.FightContent;
            if (battle == null || !Meters.TryGetValue(battle, out StunMeters fight) ||
                !fight.Stuns.TryGetValue(role, out var stun) || (!endOfBattle && battle.Round < stun.Round))
                return;

            fight.Stuns.Remove(role);
            if (!role.IsDeath && ReferenceEquals(role.GetBuff(StrikeProfiles.StunnedId), stun.Status))
                await role.RemoveBuff(StrikeProfiles.StunnedId, showChangeTip: false);
        }

        internal static float StunProgress(BattleFightContent battle, BattleRole target)
        {
            if (!Meters.TryGetValue(battle, out StunMeters fight)) return 0f;
            if (fight.Stuns.TryGetValue(target, out var stun) &&
                ReferenceEquals(target.GetBuff(StrikeProfiles.StunnedId), stun.Status)) return 1f;
            if (!fight.Values.TryGetValue(target, out StunMeter meter)) return 0f;
            int threshold = meter.StartingWillpower > 0 ? meter.StartingWillpower :
                Math.Max(1, target.Data.GetAttributeValue(EHeroAttribute.POW));
            return Mathf.Clamp01((float)meter.Value / threshold);
        }

        private static int Accumulate(StunMeters fight, BattleRole target, int gain, int willpower,
            bool holdThreshold, out bool full)
        {
            full = false;
            if (gain <= 0 || target.IsDeath || target.GetBuff(StrikeProfiles.StunnedId) != null) return 0;
            if (!fight.Values.TryGetValue(target, out StunMeter meter))
            {
                if (willpower <= 0) return 0;
                meter = new StunMeter { StartingWillpower = holdThreshold ? willpower : 0 };
                fight.Values.Add(target, meter);
            }
            if (meter.StunPending) return 0;

            int threshold = meter.StartingWillpower > 0 ? meter.StartingWillpower : Math.Max(1, willpower);
            int applied = Math.Min(gain, Math.Max(0, threshold - meter.Value));
            meter.Value += applied;
            full = meter.Value >= threshold;
            meter.StunPending = full;
            return applied;
        }

        private static async Task ApplyStun(StunMeters fight, BattleRole source, BattleRole target,
            BattleFightContent battle)
        {
            try
            {
                if (target.IsDeath || target.GetBuff(StrikeProfiles.StunnedId) != null) return;
                BuffData stun = Singleton<ResManager>.Instance.BuffFactory.GetData(StrikeProfiles.StunnedId);
                if (stun?.Config == null) return;
                await target.AddBuff(source, stun);
                if (ReferenceEquals(target.GetBuff(StrikeProfiles.StunnedId), stun))
                    fight.Stuns[target] = (stun, battle.Round + 1);
            }
            finally { fight.Values.Remove(target); }
        }

        private static async Task Burst(BattleRole source, BattleRole target, StunMeters fight)
        {
            BuffData scorch = target.GetBuff(StrikeProfiles.ScorchTremorId);
            BuffData tremor = scorch ?? target.GetBuff(StrikeProfiles.TremorId);
            if (tremor == null || tremor.CurLayer <= 0 || tremor.DisableBuff || target.IsDeath) return;

            ValennaEventAudio.PlayBurst(target);
            int layers = tremor.CurLayer;
            BuffData burn = scorch == null ? null : target.GetBuff(StrikeProfiles.BurnId);
            int burning = Math.Max(0, burn?.CurLayer ?? 0);
            BattleFightContent battle = BattleHelper.FightContent;
            int threshold = Math.Max(0, target.Data.GetAttributeValue(EHeroAttribute.POW));
            int gain = threshold == 0 ? 0 : (int)Math.Min(((long)layers + 2) / 3, Math.Max(1, threshold / 3));
            int increase = Accumulate(fight, target, gain, threshold, holdThreshold: false, out bool full);
            TremorBurstDisplay.Show(target, scorch != null, increase);
            if (full) await ApplyStun(fight, source, target, battle);
            ValennaKit.RewardBurst(source);

            await tremor.ChangeLayer(target, -1 - (scorch == null ? 0 : layers / 3));
            if (burning >= 3 && ReferenceEquals(target.GetBuff(StrikeProfiles.BurnId), burn))
                await burn.ChangeLayer(target, -(burning / 3));

            if (scorch == null || target.IsDeath) return;

            int damage = (int)(((long)burning + layers) / 2);
            if (damage <= 0) return;
            var fire = new DamageData { DamageType = EDamageType.Explosion, Value = damage.ToString() };
            var additional = new DamageAdditionalData
            {
                SourceType = EDamageSourceType.Buff, EnableChangeDamageValue = true
            };
            int result = BattleHelper.CalculationDamage(fire, source, target, additional);
            var hit = new RoleHitData(fire, additional, result, 0, false) { Attacker = source, Target = target };
            await target.SetDamage((uint)Math.Max(0, result), hitData: hit);
        }

        [HarmonyPatch(typeof(Buff_RoleAttrOption), nameof(Buff_RoleAttrOption.OnAction))]
        private static class LeiBurstApplied
        {
            private static void Prefix(Buff_RoleAttrOption __instance, BuffData __0, BattleRole __1,
                out (BattleFightContent Battle, BattleRole Source, RoleData Data, int Willpower) __state)
            {
                __state = default;
                if (!BattleHelper.IsInBattle || __1?.Data == null || __0 == null || __0.CurLayer <= 0) return;

                // 雷横先施加引爆标记，直到伤害结算才执行强度或灼热的引爆属性变化。
                bool burst = __0.Id switch
                {
                    LeiStrength => __instance.SourceKey == "1470691050Buff_RoleAttrOption",
                    LeiScorch => __instance.SourceKey == "2129000652Buff_RoleAttrOption",
                    _ => false
                };
                BattleFightContent battle = BattleHelper.FightContent;
                if (!burst || battle == null || !Meters.TryGetValue(battle, out StunMeters fight)) return;
                BuffData mark = __1.GetBuff(LeiBurst);
                if (mark == null || !fight.LeiBursts.TryGetValue(mark, out BattleRole source)) return;

                ValennaEventAudio.PlayBurst(__1);
                __state = (battle, source, __1.Data, __1.Data.GetAttributeValue(EHeroAttribute.POW));
            }

            private static void Postfix(BuffData __0, BattleRole __1,
                (BattleFightContent Battle, BattleRole Source, RoleData Data, int Willpower) __state, ref Task __result)
            {
                if (__state.Data == null) return;
                __result = RecordAfter(__result, __state.Source, __1, __0.Id == LeiScorch, __state.Battle,
                    __state.Data, __state.Willpower);
            }

            private static async Task RecordAfter(Task original, BattleRole source, BattleRole target, bool scorching,
                BattleFightContent battle, RoleData data, int willpowerBefore)
            {
                await original;
                if (battle == null || !ReferenceEquals(BattleHelper.FightContent, battle) ||
                    !ReferenceEquals(target.Data, data)) return;
                // 雷横直接降低意志。读取这次结算的实际降幅，并以本轮积累前的意志为条的上限。
                int gain = Math.Max(0, willpowerBefore - data.GetAttributeValue(EHeroAttribute.POW));
                StunMeters fight = Meters.GetValue(battle, _ => new StunMeters());
                int increase = Accumulate(fight, target, gain, willpowerBefore, holdThreshold: true, out bool full);
                TremorBurstDisplay.Show(target, scorching, increase);
                if (full) await ApplyStun(fight, source, target, battle);
            }
        }

        private static async Task Apply(BattleRole source, BattleRole target, BuffData incoming,
            bool showChangeTip, object[] arguments)
        {
            int id = incoming.Id;
            int layers = id == StrikeProfiles.ScorchTremorId
                ? Math.Max(0, incoming.CurLayer) : Math.Max(1, incoming.CurLayer);
            if (id == RetiredBurn)
            {
                await AddLayers(source, target, StrikeProfiles.BurnId, layers, showChangeTip, arguments);
                return;
            }

            StunMeters fight = Meters.GetValue(BattleHelper.FightContent, _ => new StunMeters());
            if (UsesLeiHeng(fight))
            {
                if (id == StrikeProfiles.TremorBurstId)
                {
                    bool triggered = CountLayers(target) > 0 && !target.HaveImmunizationBuff(LeiBurst);
                    BuffData mark = Singleton<ResManager>.Instance.BuffFactory.GetData(LeiBurst);
                    if (mark == null) return;
                    // 这是雷横在后续伤害结算读取的原生标记。按具体实例绑定，防止把其他来源算入。
                    if (triggered && source != null) fight.LeiBursts.Add(mark, source);
                    await target.AddBuff(source, mark, showChangeTip, arguments);
                    if (triggered) ValennaKit.RewardBurst(source);
                    return;
                }

                bool scorching = id == StrikeProfiles.ScorchTremorId || target.GetBuff(LeiScorch) != null;
                if (layers > 0)
                {
                    await AddLayers(source, target, LeiCount, layers, showChangeTip, arguments);
                    await AddLayers(source, target, scorching ? LeiScorch : LeiStrength, layers, showChangeTip, arguments);
                    if (CountLayers(target) > 0) TremorBurstDisplay.Watch(target);
                }
                else if (target.GetBuff(LeiStrength) != null && target.GetBuff(LeiScorch) == null)
                    await target.AddBuff(source, LeiScorch, showChangeTip, arguments);
                return;
            }

            if (id == StrikeProfiles.TremorBurstId)
            {
                await Burst(source, target, fight);
                return;
            }

            if (id == StrikeProfiles.TremorId && target.GetBuff(StrikeProfiles.ScorchTremorId) != null)
                id = StrikeProfiles.ScorchTremorId;
            if (id == StrikeProfiles.ScorchTremorId && !target.HaveImmunizationBuff(id))
            {
                BuffData normal = target.GetBuff(StrikeProfiles.TremorId);
                if (normal != null)
                {
                    layers = (int)Math.Min(int.MaxValue, (long)layers + normal.CurLayer);
                    await target.RemoveBuff(StrikeProfiles.TremorId, showChangeTip: false);
                }
            }
            if (layers > 0)
            {
                await AddLayers(source, target, id, layers, showChangeTip, arguments);
                if (CountLayers(target) > 0) TremorBurstDisplay.Watch(target);
            }
        }

        internal static async Task AddLayers(BattleRole source, BattleRole target, int id, int count,
            bool showChangeTip, object[] arguments)
        {
            if (target.HaveImmunizationBuff(id) || target.IsDeath) return;
            BuffData existing = target.GetBuff(id);
            if (existing != null)
            {
                await existing.ChangeLayer(target, count);
                return;
            }

            BuffData buff = Singleton<ResManager>.Instance.BuffFactory.GetData(id);
            if (buff?.Config == null) return;
            buff.CurLayer = buff.MaxLayer > 0 ? Math.Min(count, buff.MaxLayer) : count;
            BuffData previous = InternalGrant.Value;
            Task installation;
            try
            {
                InternalGrant.Value = buff;
                installation = target.AddBuff(source, buff, showChangeTip, arguments);
            }
            finally { InternalGrant.Value = previous; }
            await installation;
        }

        [HarmonyPatch(typeof(BuffHelper), nameof(BuffHelper.AddBuff),
            new[] { typeof(BattleRole), typeof(BattleRole), typeof(BuffData), typeof(bool), typeof(object[]) })]
        private static class RouteTremor
        {
            private static bool Prefix(BattleRole __0, BattleRole __1, BuffData __2, bool __3, object[] __4,
                ref Task __result)
            {
                if (__0?.Data == null || __2 == null || ReferenceEquals(__2, InternalGrant.Value)) return true;
                int id = __2.Id;
                if (id != RetiredBurn && (!BattleHelper.IsInBattle || BattleHelper.FightContent == null ||
                    (id != StrikeProfiles.TremorId && id != StrikeProfiles.ScorchTremorId &&
                     id != StrikeProfiles.TremorBurstId))) return true;

                __result = Apply(__1, __0, __2, __3, __4);
                return false;
            }
        }

        [HarmonyPatch(typeof(BattleFightContent), nameof(BattleFightContent.Run))]
        private static class InitializeBattleMeters
        {
            private static void Prefix(BattleFightContent __instance, out bool __state)
            {
                __state = !__instance.IsRunning;
                if (__state) Meters.Remove(__instance);
            }

            private static void Postfix(BattleFightContent __instance, bool __state)
            {
                if (__state) TremorBurstDisplay.RefreshCombatants(__instance);
            }
        }

        [HarmonyPatch]
        private static class RefreshBattleMeters
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(BattleFightContent), nameof(BattleFightContent.UpdateCurrentWaveEnemies));
                yield return AccessTools.Method(typeof(BattleFightContent), nameof(BattleFightContent.TriggerEnemyAddedInBattle));
            }

            private static void Postfix(BattleFightContent __instance, ref Task __result) =>
                __result = RefreshAfter(__result, __instance);

            private static async Task RefreshAfter(Task original, BattleFightContent fight)
            {
                await original;
                TremorBurstDisplay.RefreshCombatants(fight);
            }
        }
    }

    internal sealed class TremorBurstDisplay : MonoBehaviour
    {
        private const float IconDuration = 0.50f;
        private const float GainDuration = 0.68f;
        private const float GainSpacing = 0.075f;
        private static Font _font;
        private readonly List<GainPopup> _gains = new List<GainPopup>();
        private BattleRole _target;
        private BattleFightContent _fight;
        private RoleModel _model;
        private static Sprite _gaugePixel;
        private RoleData _data;
        private SpriteRenderer _gaugeBorder;
        private SpriteRenderer _gaugeTrack;
        private SpriteRenderer _gaugeFill;
        private bool _scorching;
        private BattleRole _opposingValenna;

        private sealed class GainPopup
        {
            internal int Value;
            internal int Lane;
            internal float Starts;
            internal bool Scorching;
            internal SpriteRenderer Icon;
            internal Text Text;
            internal Outline Outline;
        }

        internal static void RefreshCombatants(BattleFightContent fight)
        {
            if (!BattleHelper.IsInBattle || !fight.IsRunning || !ReferenceEquals(fight, BattleHelper.FightContent)) return;

            BattleRole alliedValenna = fight.Allies.Find(IsValennaCombatant);
            BattleRole enemyValenna = fight.CurWaveEnemies.Find(IsValennaCombatant);
            foreach (BattleRole enemy in fight.CurWaveEnemies)
                WatchCombatant(fight, enemy, alliedValenna);
            foreach (BattleRole ally in fight.Allies)
                WatchCombatant(fight, ally, enemyValenna);
        }

        private static bool IsValennaCombatant(BattleRole role) => IsTargetable(role) &&
            (ValennaPlugin.IsValennaPartner(role) || ValennaPlugin.HasPalermo(role));

        private static bool IsTargetable(BattleRole role) => StrikeSequence.CanHit(role) && !role.IsRemoved;

        private static void WatchCombatant(BattleFightContent fight, BattleRole target, BattleRole opposingValenna)
        {
            if (!IsTargetable(target) || target.Model == null) return;
            TremorBurstDisplay display = opposingValenna != null ? GetDisplay(target) :
                target.Model.GetComponent<TremorBurstDisplay>();
            if (display != null && display.Matches(fight, target)) display._opposingValenna = opposingValenna;
        }

        internal static void Watch(BattleRole target) => GetDisplay(target);

        internal static void Show(BattleRole target, bool scorching, int increase)
        {
            TremorBurstDisplay burst = GetDisplay(target);
            if (burst == null) return;

            burst._scorching = scorching;
            float now = Time.unscaledTime;
            GainPopup previous = burst._gains.Count == 0 ? null : burst._gains[burst._gains.Count - 1];
            burst._gains.Add(new GainPopup
            {
                Value = increase,
                Scorching = scorching,
                Lane = previous == null ? 0 : (previous.Lane + 1) % 5,
                Starts = previous == null ? now : Mathf.Max(now, previous.Starts + GainSpacing)
            });
            if (burst.isActiveAndEnabled) burst.LateUpdate();
        }

        private static TremorBurstDisplay GetDisplay(BattleRole target)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (!BattleHelper.IsInBattle || fight == null || fight.IsRestartBattleBreak || fight.IsGiveUpBattleBreak) return null;
            RoleModel model = target.Model;
            if (model == null) return null;
            foreach (TremorBurstDisplay current in model.GetComponents<TremorBurstDisplay>())
            {
                if (!current.enabled) continue;
                if (current.Matches(fight, target)) return current;
                current.Close();
            }

            var display = model.gameObject.AddComponent<TremorBurstDisplay>();
            display._target = target;
            display._data = target.Data;
            display._model = model;
            display._fight = fight;
            return display;
        }

        private bool Matches(BattleFightContent fight, BattleRole target) =>
            ReferenceEquals(fight, _fight) && ReferenceEquals(target, _target) &&
            ReferenceEquals(target.Data, _data);

        private void LateUpdate()
        {
            if (!BattleHelper.IsInBattle || !Matches(BattleHelper.FightContent, _target) ||
                _fight.IsRestartBattleBreak || _fight.IsGiveUpBattleBreak || _target.Model != _model ||
                !IsTargetable(_target))
            {
                Close();
                return;
            }

            float progress = ValennaTremor.StunProgress(_fight, _target);
            bool showGauge = progress > 0f || ValennaTremor.CountLayers(_target) > 0 || IsTargetable(_opposingValenna);
            if (showGauge) UpdateGauge(progress);
            else HideGauge();
            UpdateGains();
            if (!showGauge && _gains.Count == 0) Close();
        }

        private void UpdateGains()
        {
            HUDElement hud = _model.Hud;
            RectTransform bar = hud?.Image_HPBG?.rectTransform;
            for (int index = _gains.Count - 1; index >= 0; index--)
            {
                GainPopup popup = _gains[index];
                float age = Time.unscaledTime - popup.Starts;
                if (age < 0f) continue;
                if (age >= GainDuration)
                {
                    if (popup.Icon != null) Destroy(popup.Icon.gameObject);
                    if (popup.Text != null) Destroy(popup.Text.gameObject);
                    _gains.RemoveAt(index);
                    continue;
                }
                if (popup.Text != null) popup.Text.enabled = true;
                int lane = (popup.Lane + 1) / 2 * (popup.Lane % 2 == 0 ? 1 : -1);
                if (age < IconDuration) UpdateIcon(popup, age, lane);
                else if (popup.Icon != null)
                {
                    Destroy(popup.Icon.gameObject);
                    popup.Icon = null;
                }
                if (bar == null || bar.parent == null) continue;
                if (popup.Text == null)
                {
                    var obj = new GameObject("ValennaTremorGain", typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(Text), typeof(Outline));
                    var rect = (RectTransform)obj.transform;
                    rect.SetParent(bar.parent, false);
                    rect.SetAsLastSibling();
                    rect.sizeDelta = new Vector2(96f, 56f);
                    popup.Text = obj.GetComponent<Text>();
                    if (_font == null && hud.Text_HP?.font == null)
                        _font = Font.CreateDynamicFontFromOSFont("Arial", 54);
                    popup.Text.font = hud.Text_HP?.font ?? _font;
                    popup.Text.fontSize = 44;
                    popup.Text.fontStyle = FontStyle.Bold;
                    popup.Text.alignment = TextAnchor.MiddleCenter;
                    popup.Text.raycastTarget = false;
                    popup.Text.text = popup.Value.ToString(CultureInfo.InvariantCulture);
                    popup.Outline = obj.GetComponent<Outline>();
                    popup.Outline.effectDistance = new Vector2(2f, -2f);
                }
                Rect health = bar.rect;
                float scale = Mathf.Clamp(health.width, 52f, 310f) / 150f;
                float progress = Mathf.Clamp01(age / GainDuration);
                popup.Text.rectTransform.position = bar.TransformPoint(new Vector3(
                    health.center.x + 43f * scale * lane,
                    health.yMax + scale * (30f + 34f * progress + 9f * (popup.Lane % 2))));
                float pop = Mathf.Lerp(1.18f, 0.9f, Mathf.Clamp01(age / 0.14f));
                popup.Text.rectTransform.localScale = Vector3.one * scale * pop;
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, GainDuration, age));
                popup.Text.color = new Color(0.06f, 0.04f, 0.02f, alpha);
                popup.Outline.useGraphicAlpha = false;
                popup.Outline.effectColor = new Color(1f, 0.81f, 0.20f, alpha);
            }
        }

        private void UpdateIcon(GainPopup popup, float elapsed, int lane)
        {
            SpriteRenderer body = _model.SpriteRender;
            Sprite art = popup.Scorching ? StrikeArt.ScorchBurstIcon : StrikeArt.TremorBurstIcon;
            if (body == null || body.sprite == null || art == null)
            {
                if (popup.Icon != null) popup.Icon.enabled = false;
                return;
            }

            if (popup.Icon == null) popup.Icon = new GameObject("ValennaTremorBurstIcon").AddComponent<SpriteRenderer>();
            SpriteRenderer icon = popup.Icon;
            if (icon.transform.parent != body.transform) icon.transform.SetParent(body.transform, false);
            icon.gameObject.layer = body.gameObject.layer;
            icon.sprite = art;
            icon.sortingLayerID = body.sortingLayerID;
            icon.sortingOrder = body.sortingOrder + 4 + popup.Lane;
            icon.enabled = true;

            Bounds bounds = body.bounds;
            float height = Mathf.Clamp(bounds.size.y, 0.45f, 2.45f);
            icon.transform.position = bounds.center + Vector3.right * (0.135f * lane * height);
            float pop = Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(elapsed / 0.14f));
            Vector3 artSize = art.bounds.size;
            float scale = height * 0.29f * pop /
                Mathf.Max(artSize.x, artSize.y);
            Vector3 parentScale = body.transform.lossyScale;
            icon.transform.localScale = new Vector3(scale / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1f);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, IconDuration, elapsed));
            icon.color = new Color(1f, 1f, 1f, alpha);
        }

        private void UpdateGauge(float progress)
        {
            SpriteRenderer body = _model.SpriteRender;
            Camera camera = BattleHelper.BattleCam ?? Camera.main;
            if (body == null || body.sprite == null || !body.enabled || !body.gameObject.activeInHierarchy ||
                body.color.a <= 0.01f || camera == null)
            {
                HideGauge();
                return;
            }

            Bounds bounds = body.bounds;
            Transform view = camera.transform;
            Vector3 right = view.right;
            Vector3 up = view.up;
            float height = 2f * ProjectedExtent(bounds, up);
            if (height < 0.001f)
            {
                HideGauge();
                return;
            }
            float scale = Mathf.Clamp(height, 0.45f, 2.45f);
            float gaugeHeight = scale * 0.52f;
            float gaugeWidth = scale * 0.087f;
            float border = scale * 0.012f;
            Vector3 center = bounds.center + right *
                (ProjectedExtent(bounds, right) + scale * 0.10f + gaugeWidth * 0.5f);
            if (_gaugeBorder == null) _gaugeBorder = NewGaugeLayer("ValennaStunBorder");
            if (_gaugeTrack == null) _gaugeTrack = NewGaugeLayer("ValennaStunTrack");
            if (_gaugeFill == null) _gaugeFill = NewGaugeLayer("ValennaStunFill");

            PlaceGaugeLayer(_gaugeBorder, body, view, center, gaugeWidth, gaugeHeight, 12,
                new Color32(67, 40, 25, 255));
            float innerWidth = gaugeWidth - 2f * border;
            float innerHeight = gaugeHeight - 2f * border;
            PlaceGaugeLayer(_gaugeTrack, body, view, center, innerWidth, innerHeight, 13,
                new Color32(27, 19, 23, 235));
            _gaugeFill.enabled = progress > 0f;
            if (progress <= 0f) return;

            bool scorching = ValennaTremor.IsScorching(_target) || _scorching;
            Color fill = progress >= 1f ? new Color32(255, 239, 165, 255) :
                scorching ? new Color32(245, 91, 55, 255) : new Color32(255, 197, 76, 255);
            PlaceGaugeLayer(_gaugeFill, body, view, center - up * (innerHeight * (1f - progress) * 0.5f),
                innerWidth, innerHeight * progress, 14, fill);
        }

        private static float ProjectedExtent(Bounds bounds, Vector3 axis) =>
            Mathf.Abs(axis.x) * bounds.extents.x + Mathf.Abs(axis.y) * bounds.extents.y +
            Mathf.Abs(axis.z) * bounds.extents.z;

        private SpriteRenderer NewGaugeLayer(string layerName)
        {
            if (_gaugePixel == null)
            {
                Texture2D pixel = Texture2D.whiteTexture;
                _gaugePixel = Sprite.Create(pixel, new Rect(0, 0, pixel.width, pixel.height),
                    new Vector2(0.5f, 0.5f), pixel.width);
            }

            var layer = new GameObject(layerName).AddComponent<SpriteRenderer>();
            layer.transform.SetParent(transform, false);
            layer.sprite = _gaugePixel;
            return layer;
        }

        private static void PlaceGaugeLayer(SpriteRenderer layer, SpriteRenderer body, Transform view,
            Vector3 center, float width, float height, int order, Color color)
        {
            layer.gameObject.layer = body.gameObject.layer;
            layer.sortingLayerID = body.sortingLayerID;
            layer.sortingOrder = body.sortingOrder + order;
            layer.color = color;
            Transform item = layer.transform;
            item.SetPositionAndRotation(center, view.rotation);
            item.localScale = Vector3.one;
            Vector3 inheritedScale = item.lossyScale;
            item.localScale = new Vector3(width / Mathf.Max(0.001f, Mathf.Abs(inheritedScale.x)),
                height / Mathf.Max(0.001f, Mathf.Abs(inheritedScale.y)), 1f);
            layer.enabled = true;
        }

        private void HideGauge()
        {
            if (_gaugeBorder != null) _gaugeBorder.enabled = false;
            if (_gaugeTrack != null) _gaugeTrack.enabled = false;
            if (_gaugeFill != null) _gaugeFill.enabled = false;
        }

        private void Close()
        {
            enabled = false;
            Destroy(this);
        }

        private void OnDisable()
        {
            HideGauge();
            foreach (GainPopup popup in _gains)
            {
                if (popup.Icon != null) popup.Icon.enabled = false;
                if (popup.Text != null) popup.Text.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_gaugeBorder != null) Destroy(_gaugeBorder.gameObject);
            if (_gaugeTrack != null) Destroy(_gaugeTrack.gameObject);
            if (_gaugeFill != null) Destroy(_gaugeFill.gameObject);
            foreach (GainPopup popup in _gains)
            {
                if (popup.Icon != null) Destroy(popup.Icon.gameObject);
                if (popup.Text != null) Destroy(popup.Text.gameObject);
            }
        }
    }
}
