// 私货特质的英文本地化（2026-09-22 从「自定义心」的 XinText.cs 拆过来）。
//
// 中文/繁体不动：数据文件里本来就是中文。
// 其它语言：启动后把私货这 4 个特质 + 7 个状态的文案覆盖成英文（只碰我们自己的编号）。
//
// 注意：这一份和「自定义心」里的 XinText.cs 是**两份独立代码**，各管各的编号，互不影响。

using System;
using System.Collections.Generic;
using MOD;

namespace AttackTargetVisualizer
{
    internal static class SecretText
    {
        // ---------------- 语言判断 ----------------

        private static string _loggedLanguage;

        internal static bool IsEnglish
        {
            get
            {
                try
                {
                    if (!Singleton<LocalizationHandler>.HasInstance)
                    {
                        return false;
                    }
                    LocalizationHandler handler = Singleton<LocalizationHandler>.Instance;
                    if (handler == null)
                    {
                        return false;
                    }

                    string lang = handler.CurrentLanguage != null ? handler.CurrentLanguage : "";
                    string code = handler.CurrentLanguageCode != null ? handler.CurrentLanguageCode : "";

                    // 语言代码最可靠：zh / zhTW 是中文，其它一律按英文走（我们只做了英文这一份）
                    // 注意：不能用 IsChineseLanguage——语言表还没加载好时它一律返回"是中文"
                    bool chinese;
                    if (!string.IsNullOrEmpty(code))
                    {
                        chinese = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        chinese = lang.StartsWith("Chinese") || lang.StartsWith("SimpleChinese") ||
                                  lang.StartsWith("TraditionalChinese") || lang.Contains("中文");
                    }

                    string note = lang + " / " + code + (chinese ? " -> 中文" : " -> English");
                    if (_loggedLanguage != note)
                    {
                        _loggedLanguage = note;
                        AttackTargetPlugin.LogInfo("当前语言判定：" + note);
                    }
                    return !chinese;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // ---------------- 私货文案 ----------------

        internal const string LilyTraitName = "Lily Flower";
        internal const string LilyTraitDes =
            "A beautiful lily - lacking in resistance, yet overwhelmingly alluring.";
        internal const string LilyEffect =
            "Willpower -25.\n" +
            "Enemy charm actions against you gain 1 bonus die.\n" +
            "Your opposing roll against charm actions gains 1 penalty die.\n" +
            "Enemy actions that charm will target the holder first.\n" +
            "While charmed, your Willpower checks and opposed rolls gain 1 penalty die.\n" +
            "On entering a module, automatically equips the \"Lily Flower\" accessory (cannot be unequipped): " +
            "1 death immunity per battle, and 1 spell-damage immunity every 2 rounds.";

        internal const string WhiteHairTraitName = "White-Haired Girl";
        internal const string WhiteHairTraitDes = "A frail white-haired girl.";
        internal const string WhiteHairEffect =
            "Enemies without sanity gain 2 stacks of Exposed Weakness.\n" +
            "Persuade and Psychology checks gain 1 bonus die.";

        internal const string LoneShadowTraitName = "Lone Shadow";
        internal const string LoneShadowDes =
            "A legendary investigator who always walks alone; because of her white hair, the world calls her: the Lone Shadow.";
        internal const string LoneShadowEffect =
            "After an attack action (throw and magic excluded), roll Willpower: " +
            "a hard success spends 2 Sanity to strike once more, an extreme success does so for free; " +
            "a regular success or a failure grants nothing.\n" +
            "Never triggers with less than 2 Sanity, or while Weakened or Collapsed.\n" +
            "While alone: Dodge +20, Speed +20, Physical damage +50%.\n" +
            "Always: Dexterity +20.";

        internal const string MemoryTraitName = "Twin Swords of Memory";
        internal const string MemoryTraitDes = "Memories carved into the bone are never forgotten.";
        internal const string MemoryEffect =
            "Melee damage +3.\n" +
            "Melee hits apply 1 stack of Bleed.\n" +
            "Melee damage you take is reduced by 1.\n" +
            "When an attack misses you while you have a melee weapon equipped, strike back immediately " +
            "(one hit only, never counts as a pursuit attack).\n" +
            "Your off-hand melee weapon suffers no penalty die.\n" +
            "Two-handed melee weapons take up only one weapon slot for you.";

        internal const string WhiteHairWeakName = "Frail";
        internal const string WhiteHairWeakDes = "Strength -10.";

        internal const string UndyingName = "The Lily Unwithered";
        internal const string UndyingDes = "Where the petals fell, new buds have risen.";

        internal const string LoneBonusName = "Lone Shadow: Solitude";
        internal const string LoneBonusDes =
            "With no other teammate able to act: Dodge +20, Speed +20, Physical damage +50%.";

        internal const string DamageImmuneName = "Damage Immunity";
        internal const string DamageImmuneDes = "Immune to damage from enemies for 1 round.";

        // ---------------- 覆盖我们自己的数据文案 ----------------

        /// <summary>把私货的数据文案换成英文（只在私货编号上动手）。</summary>
        internal static void ApplyDataOverlay()
        {
            if (!IsEnglish)
            {
                return;
            }
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return;
                }
                ResManager res = Singleton<ResManager>.Instance;

                ApplyTraitText(res, SecretIds.LilyTrait, LilyTraitName, LilyTraitDes, LilyEffect, "Charm no longer lasts longer");
                ApplyTraitText(res, SecretIds.WhiteHairTrait, WhiteHairTraitName, WhiteHairTraitDes, WhiteHairEffect, "No longer charms enemies");
                ApplyTraitText(res, SecretIds.LoneShadowTrait, LoneShadowTraitName, LoneShadowDes, LoneShadowEffect, "Lose the Lone Shadow status");
                ApplyTraitText(res, SecretIds.MemoryTrait, MemoryTraitName, MemoryTraitDes, MemoryEffect, "Lose Twin Swords of Memory");

                ApplyBuffText(res, SecretIds.LilyBuff, LilyTraitName, LilyEffect);
                ApplyBuffText(res, SecretIds.LoneShadowBuff, LoneShadowTraitName, LoneShadowDes);
                ApplyBuffText(res, SecretIds.UndyingBuff, UndyingName, UndyingDes);
                ApplyBuffText(res, SecretIds.LoneBonusBuff, LoneBonusName, LoneBonusDes);
                ApplyBuffText(res, SecretIds.DamageImmuneBuff, DamageImmuneName, DamageImmuneDes);
                ApplyBuffText(res, SecretIds.MemoryBuff, MemoryTraitName, MemoryEffect);
                ApplyBuffText(res, SecretIds.WhiteHairWeakBuff, WhiteHairWeakName, WhiteHairWeakDes);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货英文文案覆盖失败：" + e.Message);
            }
        }

        private static void ApplyTraitText(ResManager res, int id, string name, string des, string wake, string sleepy)
        {
            TraitData trait = FindById(res.TraitFactory, id, o => o.Id);
            if (trait == null)
            {
                return;
            }
            SetText(trait.Localization_Name, name);
            SetText(trait.Localization_Des, des);
            if (trait.WakeEvent != null)
            {
                SetText(trait.WakeEvent.Localization_Functin, wake);
            }
            if (trait.SleepyEvent != null)
            {
                SetText(trait.SleepyEvent.Localization_Functin, sleepy);
            }
        }

        private static void ApplyBuffText(ResManager res, int id, string name, string des)
        {
            BuffTableData buff = FindById(res.BuffFactory, id, o => o.Id);
            if (buff == null)
            {
                return;
            }
            SetText(buff.Name, name);
            SetText(buff.Des, des);
        }

        private static void SetText(LocalizationKeyData text, string value)
        {
            if (text == null || string.IsNullOrEmpty(value))
            {
                return;
            }
            text.InputText = value;
        }

        /// <summary>按编号在工厂里找一条数据：先按 key 查缓存，查不到再遍历一遍。</summary>
        private static T FindById<T>(BaseFactory<T> factory, int id, Func<T, int> getId) where T : BaseSheetData
        {
            if (factory == null)
            {
                return null;
            }
            try
            {
                T byKey = factory.GetCache(id.ToString());
                if (byKey != null)
                {
                    return byKey;
                }
            }
            catch (Exception)
            {
            }
            try
            {
                List<T> all = factory.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && getId(all[i]) == id)
                    {
                        return all[i];
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
