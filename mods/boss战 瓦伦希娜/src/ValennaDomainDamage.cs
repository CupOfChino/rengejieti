using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CustomBattleBg;
using Game;
using HarmonyLib;
using MOD;

namespace ValennaBattle
{
    internal static class ValennaDomainDamage
    {
        // 领域的公开展开接口会同时替换当前领域。这里只向它的状态构建器取得友方预设。
        private static readonly Type Builder = typeof(DomainApi).Assembly.GetType("CustomBattleBg.DomainBuffBuilder", true);
        private static readonly Func<DomainProfile, int, int, BuffTableData> BuildPreset =
            (Func<DomainProfile, int, int, BuffTableData>)Delegate.CreateDelegate(
                typeof(Func<DomainProfile, int, int, BuffTableData>),
                AccessTools.DeclaredMethod(Builder, "Build", new[] { typeof(DomainProfile), typeof(int), typeof(int) }));
        private static readonly int AllyKind = (int)AccessTools.DeclaredField(Builder, "KindAlly").GetRawConstantValue();
        private static readonly ConditionalWeakTable<LocalizationKeyData, DamageTotal> Totals =
            new ConditionalWeakTable<LocalizationKeyData, DamageTotal>();
        private sealed class DamageTotal { internal int Percent; }
        private static readonly LocalizationKeyData TotalText =
            ValennaText.Key("damageUp.total", "当前普通伤害加成：+{0}%。");

        internal static BuffTableData Create(int layers)
        {
            var profile = new DomainProfile
            {
                AllyTarget = 1,
                AllyEffectType = DomainEffectPreset.PhysicalDamageBonus,
                AllyEffectValue = layers
            };
            BuffTableData config = BuildPreset(profile, AllyKind, ValennaKit.DamageUpId);
            var definition = Singleton<ResManager>.Instance.BuffFactory.GetConfig(ValennaKit.DamageUpId);
            var rules = definition?.Des;
            if (!string.IsNullOrEmpty(rules?.InputText))
            {
                config.Name = definition.Name;
                config.Des = new LocalizationBuffKeyData { TarKey = rules.TargetKey, InputText = rules.InputText };
                Totals.Add(config.Des, new DamageTotal { Percent = layers * 10 });
            }
            // 领域每轮重新发放；铳剑的层数则留到战斗结束。每个角色持有独立的配置实例。
            config.Events.RemoveAll(effect => effect.EBuffTrigger == EBuffTriggerType.RoundEnd);
            return config;
        }

        [HarmonyPatch(typeof(LocalizationKeyData), nameof(LocalizationKeyData.GetValueByChinese))]
        private static class ShowDamageTotal
        {
            private static void Postfix(LocalizationKeyData __instance, ref string __result)
            {
                if (__instance.TarKey == "valenna.Buff.885153.Des" && Totals.TryGetValue(__instance, out var total))
                    __result += "\n" + string.Format(TotalText.GetValue(), total.Percent);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.AddBuff))]
        private static class InstallPreset
        {
            private static void Prefix(BuffData __instance)
            {
                if (__instance.Id == ValennaKit.DamageUpId)
                    __instance.Config = Create(__instance.CurLayer);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.ChangeLayer))]
        private static class UpdateLayers
        {
            private static void Postfix(BuffData __instance, BattleRole __0, ref Task __result)
            {
                if (__instance.Id == ValennaKit.DamageUpId)
                    __result = RefreshAfter(__result, __instance, __0);
            }
        }

        [HarmonyPatch(typeof(BuffData), nameof(BuffData.Reload))]
        private static class RestorePreset
        {
            private static void Postfix(BuffData __instance, BattleRole __0, ref Task __result)
            {
                if (__instance.Id == ValennaKit.DamageUpId)
                    __result = RefreshAfter(__result, __instance, __0);
            }
        }

        private static async Task RefreshAfter(Task original, BuffData buff, BattleRole role)
        {
            await original;
            if (role?.Data?.BuffList?.Contains(buff) != true) return;
            buff.Config = Create(buff.CurLayer);
            // 同一来源先撤旧值，保留别的领域与装备来源；禁用时只更新描述，等待原生启用事件。
            foreach (var effect in buff.Config.Events)
                if (effect.EBuffTrigger == EBuffTriggerType.Stable)
                    foreach (var option in effect.Funcs)
                    {
                        await option.OnRemove(buff, role);
                        if (!buff.DisableBuff && buff.CurLayer > 0) await option.OnAction(buff, role);
                    }
        }
    }
}
