// 本地化（中 / 英）。
//
// 中文（含繁体）：一切照旧——数据文件里写的就是中文，提示也是中文。
// 其它语言：启动后把"我们的编号"的数据文案覆盖成英文（只碰 882001~882003，不碰游戏本体），
//          插件自己的提示与按钮文案走 T(zh, en)。
//
// 语言判断写法照抄「自定义播放战斗背景和BGM」的 DomainText（不要用 IsChineseLanguage：
// 语言表没加载好时它一律返回"是中文"）。

using System;
using System.Collections.Generic;
using MOD;

namespace DuoActionSlot
{
    internal static class SlotText
    {
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
                        DuoActionSlotPlugin.LogInfo("当前语言判定：" + note);
                    }
                    return !chinese;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>中文返回 zh，其它语言返回 en。</summary>
        internal static string T(string zh, string en)
        {
            return IsEnglish ? en : zh;
        }

        // ---------------- 英文文案 ----------------

        internal const string TraitName = "Swift as the Wind";
        internal const string TraitDes = "Gain extra action slots.";
        internal const string TraitEffectWake =
            "When entering battle and at the start of every round:\n" +
            "Speed >= 100: gain 1 extra action slot\n" +
            "Speed >= 300: gain 1 more extra action slot (up to 3)";
        internal const string TraitEffectSleep = "No effect";

        internal const string AddVacatTitle = "Add \"Swift as the Wind\"";
        internal const string AddVacatDesc =
            "Give this investigator the \"Swift as the Wind\" trait. With it, reaching the Speed thresholds grants extra action slots in battle.";
        internal const string AddVacatEffect = "Gain the \"Swift as the Wind\" trait";

        internal const string RemoveVacatTitle = "Remove \"Swift as the Wind\"";
        internal const string RemoveVacatDesc =
            "Remove the \"Swift as the Wind\" trait from this investigator. Without it, no extra action slots are gained in battle.";
        internal const string RemoveVacatEffect = "Remove the \"Swift as the Wind\" trait";

        internal const string AlreadyHasTrait = " already has the \"Swift as the Wind\" trait.";
        internal const string NoTraitToRemove = " does not have the \"Swift as the Wind\" trait.";
        internal const string AddTraitFailed = "Failed to add the trait, check the log.";
        internal const string RemoveTraitFailed = "Failed to remove the trait, check the log.";
        internal const string AddTraitOk = " gained the \"Swift as the Wind\" trait.";
        internal const string RemoveTraitOk = " lost the \"Swift as the Wind\" trait.";
        internal const string ConfirmAction = "Confirm action {0}/{1}";

        /// <summary>非中文环境下把我们的数据文案覆盖成英文；只动我们自己的编号。</summary>
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
                ApplyTraitText(res, SlotConstants.TraitId, TraitName, TraitDes, TraitEffectWake, TraitEffectSleep);
                ApplyVacatText(res, SlotConstants.AddVacatId, AddVacatTitle, AddVacatDesc, AddVacatEffect);
                ApplyVacatText(res, SlotConstants.RemoveVacatId, RemoveVacatTitle, RemoveVacatDesc, RemoveVacatEffect);
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("英文文案覆盖失败：" + e.Message);
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

        private static void ApplyVacatText(ResManager res, int id, string title, string desc, string effect)
        {
            InterludeVacationConfig config = FindById(res.InterludeVacationFactory, id, o => o.VacatId);
            if (config == null)
            {
                return;
            }
            SetText(config.TitleTxt, title);
            SetText(config.DecTxt, desc);
            SetText(config.EffectTxt, effect);
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
