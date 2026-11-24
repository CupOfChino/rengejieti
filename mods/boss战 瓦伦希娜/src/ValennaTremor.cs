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
        private static readonly AsyncLocal<BurstAudioBatch> ActiveBurstAudio = new AsyncLocal<BurstAudioBatch>();
        private static readonly ConditionalWeakTable<BattleFightContent, StunMeters> Meters =
            new ConditionalWeakTable<BattleFightContent, StunMeters>();

        private sealed class StunMeters
        {
            internal readonly Dictionary<BattleRole, int> Values = new Dictionary<BattleRole, int>();
            internal readonly Dictionary<BattleRole, (BuffData Status, int Round)> Stuns =
                new Dictionary<BattleRole, (BuffData Status, int Round)>();
            internal bool? UsesLeiHeng;
        }

        private sealed class BurstAudioBatch
        {
            internal readonly BattleRole Target;
            internal int Count;

            internal BurstAudioBatch(BattleRole target) => Target = target;
        }

        internal static async Task BurstBatch(BattleRole source, BattleRole target, int attempts)
        {
            if (attempts <= 0 || !StrikeSequence.CanHit(target)) return;
            BurstAudioBatch previous = ActiveBurstAudio.Value;
            var batch = new BurstAudioBatch(target);
            ActiveBurstAudio.Value = batch;
            try
            {
                for (int i = 0; i < attempts && StrikeSequence.CanHit(target); i++)
                    await target.AddBuff(source, StrikeProfiles.TremorBurstId);
            }
            finally
            {
                ActiveBurstAudio.Value = previous;
                if (batch.Count > 0) ValennaEventAudio.PlayBursts(target, batch.Count);
            }
        }

        private static void RecordBurst(BattleRole target)
        {
            BurstAudioBatch batch = ActiveBurstAudio.Value;
            if (batch?.Target == target) batch.Count++;
            else ValennaEventAudio.PlayBursts(target, 1);
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

        internal static bool TryGetStunValue(BattleFightContent battle, BattleRole target, out int value)
        {
            value = 0;
            if (battle == null || !Meters.TryGetValue(battle, out StunMeters fight)) return false;
            fight.Values.TryGetValue(target, out value);
            return true;
        }

        private static async Task Burst(BattleRole source, BattleRole target, StunMeters fight)
        {
            BuffData scorch = target.GetBuff(StrikeProfiles.ScorchTremorId);
            BuffData tremor = scorch ?? target.GetBuff(StrikeProfiles.TremorId);
            if (tremor == null || tremor.CurLayer <= 0 || tremor.DisableBuff || target.IsDeath) return;

            int layers = tremor.CurLayer;
            BuffData burn = scorch == null ? null : target.GetBuff(StrikeProfiles.BurnId);
            int burning = Math.Max(0, burn?.CurLayer ?? 0);
            int threshold = Math.Max(0, target.Data.GetAttributeValue(EHeroAttribute.POW));
            int gain = (int)Math.Min(((long)layers + 2) / 3, threshold / 3);
            fight.Values.TryGetValue(target, out int stunValue);
            bool stun = false;
            int increase = 0;
            if (gain > 0 && target.GetBuff(StrikeProfiles.StunnedId) == null)
            {
                increase = Math.Max(0, Math.Min(gain, threshold - stunValue));
                long total = (long)stunValue + gain;
                stun = total >= threshold;
                stunValue = stun ? 0 : (int)total;
                if (stun) fight.Values.Remove(target);
                else fight.Values[target] = stunValue;
            }
            TremorBurstDisplay.Show(target, scorch != null, increase);
            RecordBurst(target);
            await ValennaKit.RewardBurst(source);

            await tremor.ChangeLayer(target, -1 - (scorch == null ? 0 : layers / 3));
            if (burning >= 3 && ReferenceEquals(target.GetBuff(StrikeProfiles.BurnId), burn))
                await burn.ChangeLayer(target, -(burning / 3));
            if (stun)
            {
                await target.AddBuff(source, StrikeProfiles.StunnedId);
                BuffData state = target.GetBuff(StrikeProfiles.StunnedId);
                if (state != null && BattleHelper.FightContent != null)
                    fight.Stuns[target] = (state, BattleHelper.FightContent.Round + 1);
            }

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
                out (RoleData Data, int Willpower) __state)
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
                if (burst) __state = (__1.Data, __1.Data.GetAttributeValue(EHeroAttribute.POW));
            }

            private static void Postfix(BuffData __0, BattleRole __1,
                (RoleData Data, int Willpower) __state, ref Task __result)
            {
                if (__state.Data == null) return;
                __result = RecordAfter(__result, __1, __0.Id == LeiScorch, __state.Data, __state.Willpower);
            }

            private static async Task RecordAfter(Task original, BattleRole target, bool scorching,
                RoleData data, int willpowerBefore)
            {
                await original;
                // 领域把混乱结算直接写入原生意志和大失败边界，无独立的累计计量。
                if (!ReferenceEquals(target.Data, data)) return;
                int increase = Math.Max(0, willpowerBefore - data.GetAttributeValue(EHeroAttribute.POW));
                TremorBurstDisplay.Show(target, scorching, increase);
                RecordBurst(target);
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
                    await target.AddBuff(source, LeiBurst, showChangeTip, arguments);
                    if (triggered) await ValennaKit.RewardBurst(source);
                    return;
                }

                bool scorching = id == StrikeProfiles.ScorchTremorId || target.GetBuff(LeiScorch) != null;
                if (layers > 0)
                {
                    await AddLayers(source, target, LeiCount, layers, showChangeTip, arguments);
                    await AddLayers(source, target, scorching ? LeiScorch : LeiStrength, layers, showChangeTip, arguments);
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
            if (layers > 0) await AddLayers(source, target, id, layers, showChangeTip, arguments);
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
            private static void Prefix(BattleFightContent __instance)
            {
                if (!__instance.IsRunning) Meters.Remove(__instance);
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
        private Text _number;
        private Outline _outline;
        private int _stunValue;
        private bool _scorching;

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

        internal static void Show(BattleRole target, bool scorching, int increase)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (!BattleHelper.IsInBattle || fight == null || fight.IsRestartBattleBreak || fight.IsGiveUpBattleBreak) return;
            RoleModel model = target.Model;
            if (model == null) return;
            TremorBurstDisplay burst = model.GetComponent<TremorBurstDisplay>();
            if (burst == null || !burst.enabled)
                burst = model.gameObject.AddComponent<TremorBurstDisplay>();

            burst._target = target;
            burst._model = model;
            burst._fight = fight;
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
            if (burst.isActiveAndEnabled)
            {
                burst.LateUpdate();
                if (burst._number != null) burst.UpdateNumberText();
            }
        }

        private void LateUpdate()
        {
            if (!BattleHelper.IsInBattle || !ReferenceEquals(BattleHelper.FightContent, _fight) ||
                _fight.IsRestartBattleBreak || _fight.IsGiveUpBattleBreak || _target.Model != _model)
            {
                Close();
                return;
            }

            ValennaTremor.TryGetStunValue(_fight, _target, out int stunValue);
            UpdateGains();

            if (_stunValue != stunValue)
            {
                _stunValue = stunValue;
                if (_number != null && stunValue > 0) UpdateNumberText();
            }
            if (stunValue > 0 && !_target.IsDeath) UpdateNumber();
            else RemoveNumber();
            if ((stunValue == 0 || _target.IsDeath) && _gains.Count == 0) Close();
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

        private void UpdateNumber()
        {
            HUDElement hud = _model.Hud;
            RectTransform bar = hud?.Image_HPBG?.rectTransform;
            if (bar == null || bar.parent == null)
            {
                RemoveNumber();
                return;
            }

            if (_number != null && _number.transform.parent != bar.parent) RemoveNumber();
            if (_number == null)
            {
                var obj = new GameObject("ValennaStunValue", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Text), typeof(Outline));
                var rect = (RectTransform)obj.transform;
                rect.SetParent(bar.parent, false);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(202f, 58f);
                _number = obj.GetComponent<Text>();
                if (_font == null && hud.Text_HP?.font == null)
                    _font = Font.CreateDynamicFontFromOSFont("Arial", 54);
                _number.font = hud.Text_HP?.font ?? _font;
                _number.fontSize = 48;
                _number.fontStyle = FontStyle.Bold;
                _number.alignment = TextAnchor.MiddleCenter;
                _number.raycastTarget = false;
                _outline = obj.GetComponent<Outline>();
                _outline.effectDistance = new Vector2(2f, -2f);
                UpdateNumberText();
            }

            Rect health = bar.rect;
            float scale = Mathf.Clamp(health.width, 52f, 310f) * 0.83f / _number.rectTransform.rect.width;
            _number.rectTransform.position = bar.TransformPoint(new Vector3(health.center.x, health.yMin - 8f * scale));
            _number.rectTransform.localScale = Vector3.one * scale;
            _number.enabled = true;
        }

        private void UpdateNumberText()
        {
            _number.text = _stunValue.ToString(CultureInfo.InvariantCulture);
            _number.color = _scorching ? new Color32(246, 43, 37, 255) : new Color32(255, 218, 93, 255);
            _outline.effectColor = _scorching ? new Color32(255, 214, 72, 255) : new Color32(50, 19, 8, 245);
        }

        private void RemoveNumber()
        {
            if (_number == null) return;
            _number.enabled = false;
            Destroy(_number.gameObject);
            _number = null;
            _outline = null;
        }

        private void Close()
        {
            enabled = false;
            Destroy(this);
        }

        private void OnDisable()
        {
            if (_number != null) _number.enabled = false;
            foreach (GainPopup popup in _gains)
            {
                if (popup.Icon != null) popup.Icon.enabled = false;
                if (popup.Text != null) popup.Text.enabled = false;
            }
        }

        private void OnDestroy()
        {
            RemoveNumber();
            foreach (GainPopup popup in _gains)
            {
                if (popup.Icon != null) Destroy(popup.Icon.gameObject);
                if (popup.Text != null) Destroy(popup.Text.gameObject);
            }
        }
    }
}
