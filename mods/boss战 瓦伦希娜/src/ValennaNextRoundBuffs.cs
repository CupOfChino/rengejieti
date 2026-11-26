using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    public static class ValennaNextRoundBuffs
    {
        private sealed class Grant
        {
            internal int Round;
            internal BuffTableData Config;
            internal BattleRole Source;
            internal int Applications;
            internal bool ShowChangeTip;
            internal object[] Arguments;
        }

        private sealed class Preview : BuffData
        {
            internal string LayerText;
        }

        private static readonly ConditionalWeakTable<BattleFightContent, Dictionary<BattleRole, List<Grant>>> Pending =
            new ConditionalWeakTable<BattleFightContent, Dictionary<BattleRole, List<Grant>>>();
        private static readonly ConditionalWeakTable<HUDBuffElement, Image> Shades =
            new ConditionalWeakTable<HUDBuffElement, Image>();
        private static readonly LocalizationKeyData TimingText =
            ValennaText.Key("buff.nextRound", "下回合开始时生效。");

        // applications 对应原生 AddBuff 的调用次数，每种状态仍按自己的叠层和替换规则结算。
        public static bool Queue(BattleRole source, BattleRole target, int buffId, int applications = 1,
            bool showChangeTip = true, object[] arguments = null)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (!BattleHelper.IsInBattle || fight == null || fight.IsFightOver || applications <= 0 || !IsPresent(target))
                return false;

            BuffTableData config = Singleton<ResManager>.Instance.BuffFactory.GetConfig(buffId);
            if (config == null) return false;
            var roles = Pending.GetOrCreateValue(fight);
            if (!roles.TryGetValue(target, out var grants)) roles.Add(target, grants = new List<Grant>());
            grants.Add(new Grant
            {
                Round = fight.Round + 1,
                Config = config,
                Source = source,
                Applications = applications,
                ShowChangeTip = showChangeTip,
                Arguments = arguments == null ? null : (object[])arguments.Clone()
            });
            if (config.IsShowUI) RefreshHud(target);
            return true;
        }

        private static bool IsPresent(BattleRole role) => StrikeSequence.CanHit(role) && !role.IsRemoved;

        private static List<Grant> FindGrants(BattleFightContent fight, BattleRole role) =>
            fight != null && role != null && Pending.TryGetValue(fight, out var roles) &&
            roles.TryGetValue(role, out var grants) ? grants : null;

        private static void RefreshHud(BattleRole role)
        {
            if (role.Model != null && role.Model.Hud != null) _ = role.Model.Hud.ShowRoleBuff(role);
        }

        private static async Task Activate(BattleFightContent fight, BattleRole role, List<Grant> grants)
        {
            var due = grants.FindAll(grant => grant.Round <= fight.Round);
            if (due.Count == 0) return;
            // 先摘除到期项。Buff 的附加事件可以另行排入下一轮。
            grants.RemoveAll(grant => grant.Round <= fight.Round);
            if (grants.Count == 0 && Pending.TryGetValue(fight, out var roles)) roles.Remove(role);
            foreach (Grant grant in due)
            {
                for (int i = 0; i < grant.Applications; i++)
                {
                    if (!ReferenceEquals(fight, BattleHelper.FightContent) || fight.IsFightOver ||
                        !Pending.TryGetValue(fight, out _) || !IsPresent(role)) return;
                    await role.AddBuff(grant.Source, grant.Config.Id, grant.ShowChangeTip, grant.Arguments);
                }
            }
            if (due.Exists(grant => grant.Config.IsShowUI)) RefreshHud(role);
        }

        private static async Task BeforeRoleRound(BattleRole role, EBuffTriggerType trigger, object[] arguments)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (trigger == EBuffTriggerType.BeforeRoundStart && FindGrants(fight, role) is List<Grant> grants)
                await Activate(fight, role, grants);
            await role.TriggerBuffs(trigger, arguments);
        }

        private static List<BuffData> HudBuffs(RoleData data)
        {
            BattleRole role = data.Role;
            var pending = FindGrants(BattleHelper.FightContent, role);
            if (pending == null || !IsPresent(role)) return data.BuffList;
            var visible = new List<BuffData>();
            if (data.BuffList != null) visible.AddRange(data.BuffList);
            foreach (var group in pending.Where(grant => grant.Config.IsShowUI).GroupBy(grant => grant.Config.Id))
            {
                BuffTableData config = group.First().Config;
                long applications = group.Sum(grant => (long)grant.Applications);
                visible.Add(new Preview
                {
                    Id = config.Id,
                    Config = new BuffTableData
                    {
                        Id = config.Id, Name = config.Name, IconPathReference = config.IconPathReference,
                        BuffEffectType = config.BuffEffectType, OverlayType = config.OverlayType,
                        IsShowUI = true, Duration = config.Duration,
                        Des = new LocalizationBuffKeyData
                        {
                            InputText = TimingText.GetValue() + "\n" + config.Des?.GetValue()
                        }
                    },
                    LayerText = FormatLayers(config, applications)
                });
            }
            return visible;
        }

        private static string FormatLayers(BuffTableData config, long applications)
        {
            if (config.OverlayType != EBuffOverlyingType.OverlyLayer && config.OverlayType != EBuffOverlyingType.Multiple)
                return null;
            bool fixedCount = int.TryParse(config.LayerCount?.Value, out int count);
            if (!fixedCount && (!config.OverrideInitLayer || applications > 1 || config.MaxLayer > 0)) return "?";

            int first = config.OverrideInitLayer ? config.InitLayer : count;
            long layers = config.OverlayType == EBuffOverlyingType.Multiple
                ? (long)first * applications : first + (applications - 1) * count;
            if (config.MaxLayer > 0 && config.OverlayType == EBuffOverlyingType.OverlyLayer)
                layers = Math.Min(layers, config.MaxLayer);
            return Math.Max(0, layers).ToString();
        }

        private static Image MakeShade(HUDBuffElement element)
        {
            var obj = new GameObject("ValennaNextRoundShade", typeof(RectTransform), typeof(Image));
            var shade = obj.GetComponent<Image>();
            RectTransform rect = shade.rectTransform;
            rect.SetParent(element.Image_BuffIcon.rectTransform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            shade.raycastTarget = false;
            shade.color = new Color(0.12f, 0.12f, 0.12f, 0.72f);
            return shade;
        }

        private static MethodInfo AsyncBody(Type type, string method) => AccessTools.Method(
            AccessTools.Method(type, method).GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType,
            nameof(IAsyncStateMachine.MoveNext));

        [HarmonyPatch]
        private static class RoundStart
        {
            private static MethodBase TargetMethod() => AsyncBody(typeof(BattleFightContent), nameof(BattleFightContent.EnterPhase));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                instructions.MethodReplacer(AccessTools.Method(typeof(BattleRole), nameof(BattleRole.TriggerBuffs)),
                    AccessTools.Method(typeof(ValennaNextRoundBuffs), nameof(BeforeRoleRound)));
        }

        [HarmonyPatch]
        private static class ShowPending
        {
            private static MethodBase TargetMethod() => AsyncBody(typeof(HUDElement), nameof(HUDElement.ShowRoleBuff));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo buffList = AccessTools.Field(typeof(RoleData), nameof(RoleData.BuffList));
                MethodInfo hudBuffs = AccessTools.Method(typeof(ValennaNextRoundBuffs), nameof(HudBuffs));
                foreach (CodeInstruction instruction in instructions)
                {
                    // 仅替换 HUD 读取的列表，待生效项不进角色的实际状态列表。
                    if (instruction.LoadsField(buffList))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = hudBuffs;
                    }
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(UIBattlePanel), nameof(UIBattlePanel.UpdateRoleBuffShow))]
        private static class ShowWithEmptyBuffList
        {
            private static bool Prefix(BattleRole __0)
            {
                if ((__0?.Data?.BuffList?.Count ?? 0) != 0 || FindGrants(BattleHelper.FightContent, __0) == null)
                    return true;
                RefreshHud(__0);
                return false;
            }
        }

        [HarmonyPatch(typeof(HUDBuffElement), nameof(HUDBuffElement.UpdateBuffLayerShow))]
        private static class ShadePending
        {
            private static void Postfix(HUDBuffElement __instance)
            {
                if (__instance.Buff is Preview preview)
                {
                    if (preview.LayerText != null) __instance.Text_Round.text = preview.LayerText;
                    Image icon = __instance.Image_BuffIcon;
                    Image shade = Shades.GetValue(__instance, MakeShade);
                    shade.sprite = icon.sprite;
                    shade.type = icon.type;
                    shade.preserveAspect = icon.preserveAspect;
                    shade.gameObject.SetActive(icon.sprite != null);
                }
                else ClearShade(__instance);
            }
        }

        [HarmonyPatch(typeof(HUDBuffElement), nameof(HUDBuffElement.Reset))]
        private static class ResetShade
        {
            private static void Postfix(HUDBuffElement __instance) => ClearShade(__instance);
        }

        private static void ClearShade(HUDBuffElement __instance)
        {
            if (Shades.TryGetValue(__instance, out Image shade) && shade != null) shade.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(BattleRole), nameof(BattleRole.TriggerBuffs))]
        private static class ClearRole
        {
            private static void Prefix(BattleRole __instance, EBuffTriggerType __0)
            {
                if ((__0 == EBuffTriggerType.Death || __0 == EBuffTriggerType.BattleEnd) &&
                    BattleHelper.FightContent is BattleFightContent fight && Pending.TryGetValue(fight, out var roles) &&
                    roles.Remove(__instance)) RefreshHud(__instance);
            }
        }

        [HarmonyPatch]
        private static class ClearFight
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(BattleFightContent), nameof(BattleFightContent.UnInstall));
                yield return AccessTools.Method(typeof(BattleFightContent), nameof(BattleFightContent.UninstallOnRestart));
            }

            private static void Prefix(BattleFightContent __instance) => Pending.Remove(__instance);
        }
    }
}
