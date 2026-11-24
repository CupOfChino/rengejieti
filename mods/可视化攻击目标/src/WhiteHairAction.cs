using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BattleSkillEvent;
using Game;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace AttackTargetVisualizer
{
    internal static class WhiteHairAction
    {
        internal const int SkillId = 880071;
        private const string SourceKey = "attacktarget_whitehair_meow";
        private static readonly Dictionary<BattleRole, SlotCue> Cues = new Dictionary<BattleRole, SlotCue>();
        private static readonly List<BattleRole> Expired = new List<BattleRole>();

        internal static void EnsureSkill(string configRoot)
        {
            if (!Singleton<ResManager>.HasInstance)
            {
                return;
            }
            var factory = Singleton<ResManager>.Instance.Data.CharacterBattleSkillData;
            string id = SkillId.ToString();
            BattleSkillTableData skill = factory.GetCache(id);
            if (skill == null && configRoot != null)
            {
                string file = Path.Combine(configRoot, "BattleSkill", id + ".txt");
                if (File.Exists(file))
                {
                    skill = GameHelper.ESLoad<BattleSkillTableData>("Data", file);
                    if (skill != null && skill.Id == SkillId)
                    {
                        factory.UGC.Add(skill);
                        factory.ClearCachData();
                    }
                }
            }
            if (skill == null || skill.Id != SkillId)
            {
                return;
            }
            BattleSkillEffectData equip = skill.EffectList.Single(effect => effect.Trigger == EBattleSkillTrigger.EquipSkill);
            if (equip.EffectOption.Any(option => option is WhiteHairPose))
            {
                return;
            }
            BattleSkillEffectData success = skill.EffectList.Single(effect => effect.Trigger == EBattleSkillTrigger.DiceSuccess);
            success.EffectOption.Add(new WhiteHairMeow());
            equip.EffectOption.Add(new WhiteHairPose());
            AttackTargetPlugin.LogInfo("白毛少女：喵喵叫卖萌已载入");
        }

        internal static async Task OnBattleStart(BattleRole self)
        {
            if (!SecretTraits.HasTraitWake(self, SecretIds.WhiteHairTrait))
            {
                return;
            }
            SecretRuntime.ConfigureWhiteHairAction();
            await self.AddBattleSkill(SkillId, false, SourceKey);
            await ApplyToEnemies(self, showCharmEffect: false);
        }

        private static async Task ApplyToEnemies(BattleRole self, bool showCharmEffect)
        {
            var fight = BattleHelper.FightContent;
            if (fight == null)
            {
                return;
            }

            List<BattleRole> targets = new List<BattleRole>();
            if (self.IsAlly)
            {
                targets.AddRange(fight.CurWaveEnemies);
            }
            else
            {
                targets.AddRange(fight.Allies);
            }
            foreach (BattleRole target in targets)
            {
                if (target?.Data == null || target.IsDeath || target.IsAlly == self.IsAlly)
                {
                    continue;
                }
                int maxSan = target.Data.GetRoleExtraAttrMaxValue(ERoleExtraAttribute.CurrentSan);
                int san = target.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentSan);
                if (maxSan <= 0 && san <= 0)
                {
                    await target.AddBuff(self, SecretIds.VanillaWeakPointBuff);
                    await target.AddBuff(self, SecretIds.VanillaWeakPointBuff);
                }
                else if (target.Data.IsHuman)
                {
                    if (showCharmEffect && target.Model != null)
                    {
                        _ = target.Model.PlayFx("Effect/Prefabs/FX_Skill_Charm", false, ERolePointType.Center);
                    }
                    await target.AddBuff(self, SecretIds.VanillaCharmBuff);
                }
            }
        }

        internal static void UpdateSlotCues()
        {
            if (Cues.Count == 0)
            {
                return;
            }
            BattleFightContent fight = BattleHelper.IsInBattle ? BattleHelper.FightContent : null;
            float now = Time.unscaledTime;
            foreach (var entry in Cues)
            {
                if (entry.Value.IsCurrent(entry.Key, fight, now))
                {
                    entry.Value.Refresh(entry.Key.Model?.Hud);
                }
                else
                {
                    entry.Value.ClearLabel();
                    Expired.Add(entry.Key);
                }
            }
            foreach (BattleRole role in Expired)
            {
                Cues.Remove(role);
            }
            Expired.Clear();
        }

        internal static void ClearSlotCues()
        {
            foreach (SlotCue cue in Cues.Values)
            {
                cue.ClearLabel();
            }
            Cues.Clear();
        }

        private static void ShowAboveAction(BattleActiveBehaviorData action, bool success)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null)
            {
                return;
            }
            BattleRole self = action.Self;
            Cues.TryGetValue(self, out SlotCue cue);
            if (!success || cue == null || cue.Action != action || cue.Fight != fight)
            {
                cue?.ClearLabel();
                cue = new SlotCue(action, fight);
                Cues[self] = cue;
            }
            if (success)
            {
                cue.MeowUntil = Time.unscaledTime + 2f;
            }
        }

        private sealed class SlotCue
        {
            internal readonly BattleActiveBehaviorData Action;
            internal readonly BattleFightContent Fight;
            internal float MeowUntil;

            private HUDElement _hud;
            private Text _label;
            private Vector3? _slotTop;

            internal SlotCue(BattleActiveBehaviorData action, BattleFightContent fight)
            {
                Action = action;
                Fight = fight;
            }

            internal bool IsCurrent(BattleRole role, BattleFightContent fight, float now)
            {
                if (Fight != fight || role.IsDeath || role.IsEscape) return false;
                if (MeowUntil > 0f) return now < MeowUntil;
                return !Action.ActionFinish && Action.BattleSkillData?.Id == SkillId &&
                    (Action.IsRunning || role.CurrentBehaviourData == Action);
            }

            internal void Refresh(HUDElement hud)
            {
                if (_hud != hud)
                {
                    ClearLabel();
                    _hud = hud;
                    _slotTop = null;
                }
                if (hud == null || hud.Trans_Battle == null || hud.Text_MagicName == null) return;
                Transform parent = hud.Trans_Battle;

                bool meowing = MeowUntil > 0f;
                // Run 会先收起并缩放槽牌，再投骰。成功文字用收牌前的上沿位置。
                RectTransform slot = !Action.IsRunning && !meowing
                    ? ArrowLayer.GetSkillSlotRect(hud)
                    : meowing && !_slotTop.HasValue ? ArrowLayer.GetSkillSlotRect(hud, includeHidden: true) : null;
                if (slot != null)
                {
                    Rect bounds = slot.rect;
                    Vector3 world = slot.TransformPoint(new Vector3(bounds.center.x, bounds.yMax, 0f));
                    _slotTop = parent.InverseTransformPoint(world);
                }

                bool show = _slotTop.HasValue && (meowing || (!Action.IsRunning && slot != null));
                if (!show)
                {
                    if (_label != null) _label.gameObject.SetActive(false);
                    return;
                }
                if (_label == null)
                {
                    _label = UnityEngine.Object.Instantiate(hud.Text_MagicName, parent, false);
                    _label.name = "WhiteHairMeowText";
                    _label.enabled = true;
                    _label.color = new Color32(255, 154, 208, 255);
                    _label.alignment = TextAnchor.LowerCenter;
                    _label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    _label.verticalOverflow = VerticalWrapMode.Overflow;
                    _label.raycastTarget = false;
                    _label.rectTransform.pivot = new Vector2(0.5f, 0f);
                    _label.rectTransform.sizeDelta = new Vector2(360f, 46f);
                    _label.rectTransform.localScale = Vector3.one;
                    _label.rectTransform.localRotation = Quaternion.identity;
                }
                string expression = meowing ? "喵~ =￣ω￣= ....." : "o(*////▽////*)q";
                if (_label.text != expression) _label.text = expression;
                _label.rectTransform.localPosition = _slotTop.Value + new Vector3(0f, 4f, 0f);
                _label.gameObject.SetActive(true);
            }

            internal void ClearLabel()
            {
                if (_label != null)
                {
                    _label.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(_label.gameObject);
                }
                _label = null;
            }
        }

        private sealed class WhiteHairPose : BattleSkillBaseOption
        {
            public override Task Run(BattleActiveBehaviorData data, ParamTransmitionData param)
            {
                if (SecretTraits.HasTraitWake(data.Self, SecretIds.WhiteHairTrait))
                {
                    ShowAboveAction(data, success: false);
                }
                return Task.CompletedTask;
            }
        }

        private sealed class WhiteHairMeow : BattleSkillBaseOption
        {
            public override async Task Run(BattleActiveBehaviorData data, ParamTransmitionData param)
            {
                if (SecretTraits.HasTraitWake(data.Self, SecretIds.WhiteHairTrait))
                {
                    ShowAboveAction(data, success: true);
                    await ApplyToEnemies(data.Self, showCharmEffect: true);
                }
            }
        }
    }
}
