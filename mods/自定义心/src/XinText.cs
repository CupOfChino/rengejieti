// 英文本地化。
//
// 中文/繁体：一切照旧——数据文件里本来就是中文，插件界面也是中文。
// 其它语言：插件启动后把"我们自己的"数据文案覆盖成英文（只改我们这几个编号，不碰游戏本体），
//          插件自己的界面与提示也走 T()。
//
// 心 = Shin（沿用《边狱巴士》英文版的写法）。
using System;
using System.Collections.Generic;
using MOD;
using UnityEngine;
using UnityEngine.UI;

namespace XinEditor
{
    internal static class XinText
    {
        // ---------------- 语言判断 ----------------

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
                        XinEditorPlugin.LogInfo("当前语言判定：" + note);
                    }
                    return !chinese;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static string _loggedLanguage;

        /// <summary>中文返回 zh，其它语言返回 en。</summary>
        internal static string T(string zh, string en)
        {
            return IsEnglish ? en : zh;
        }

        // ---------------- 数据文案（英文）----------------

        internal const string TraitName = "Shin";
        internal const string TraitDes = "FACE THE SIN, SAVE THE E.G.O";
        internal const string TraitWake = "Auto-activation";
        internal const string TraitSleepy = "Manual activation";

        internal const string ShinBuffName = "Shin";

        /// <summary>心的三条规则 + 风味，自定义心拼描述时复用。</summary>
        internal const string ShinRules =
            "At the start of each round, try to spend 1 SP to sustain Shin.\n" +
            "Removed when SP enters Weakness or Collapse; restoring 5 SP when removed.\n" +
            "Removed at the end of battle.\n\n" +
            "<i>A manifestation of the power of the mind.</i>";

        internal const string ShinBuffDes = "Damage +2, Speed +20, damage taken -1\n" + ShinRules;

        internal const string SpeedBuffName = "Shin: Initiative";
        internal const string SpeedBuffDes = "Speed +50 this round (to seize the initiative).";

        internal const string ArmorBuffName = "Poised";
        internal const string ArmorBuffDes = "Armor +4.\nRemoved at the start of the next round.";

        internal const string ActivateSkillName = "Activate Shin";
        internal const string ActivateSkillDes =
            "Re-ignite a dormant Shin.\n\n" +
            "Speed +50 this round (initiative); after the action, spend 5 SP to gain Shin and 4 Armor " +
            "(removed at the start of the next round).\n\n" +
            "Unusable while you already have Shin, or while your SP is in Weakness or Collapse.";

        internal const string EditVacatTitle = "Edit this investigator's Shin buff";
        internal const string EditVacatDes =
            "Adjust that investigator's own Shin buff: name suffix, speed, and up to three extra bonuses.";
        internal const string EditVacatEffect = "Open the Shin buff editor";

        internal const string AddVacatTitle = "Add Shin";
        internal const string AddVacatDes =
            "Give this investigator the Shin trait. The trait comes first - only then can they have a Shin buff of their own.";
        internal const string AddVacatEffect = "Gain the Shin trait";

        internal const string RemoveVacatTitle = "Remove Shin";
        internal const string RemoveVacatDes =
            "Remove this investigator's Shin trait and Shin buff; their own custom Shin is deleted as well.";
        internal const string RemoveVacatEffect = "Remove the Shin buff and trait";

        internal static string StatName(HeartStatType type)
        {
            if (!IsEnglish)
            {
                return HeartConstants.StatName(type);
            }
            switch (type)
            {
                case HeartStatType.DamageBonus: return "Damage Bonus";
                case HeartStatType.DamageReduce: return "Damage Reduction";
                case HeartStatType.Dodge: return "Dodge";
                case HeartStatType.Willpower: return "Willpower";
                case HeartStatType.Occultism: return "Occultism";
                case HeartStatType.Brawl: return "Brawl";
                case HeartStatType.Shooting: return "Shooting";
                case HeartStatType.Athletics: return "Athletics";
                case HeartStatType.PhysicalDamageReducePercent: return "Physical Damage Reduction %";
                case HeartStatType.MagicDamageReducePercent: return "Magic Damage Reduction %";
                case HeartStatType.PhysicalDamageBonusPercent: return "Physical Damage Bonus %";
                case HeartStatType.MagicDamageBonusPercent: return "Magic Damage Bonus %";
                default: return "Unknown";
            }
        }

        // ---------------- 覆盖我们自己的数据文案 ----------------

        /// <summary>把心的数据文案换成英文（只在我们自己的编号上动手）。</summary>
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

                // 特质
                TraitData trait = FindById(res.TraitFactory, HeartConstants.DefaultHeartTraitId, o => o.Id);
                if (trait != null)
                {
                    SetText(trait.Localization_Name, TraitName);
                    SetText(trait.Localization_Des, TraitDes);
                    if (trait.WakeEvent != null)
                    {
                        SetText(trait.WakeEvent.Localization_Functin, TraitWake);
                    }
                    if (trait.SleepyEvent != null)
                    {
                        SetText(trait.SleepyEvent.Localization_Functin, TraitSleepy);
                    }
                }

                // 三个状态：默认心、先手、蓄势
                BuffTableData buff = FindById(res.BuffFactory, HeartConstants.DefaultHeartBuffId, o => o.Id);
                if (buff != null)
                {
                    SetText(buff.Name, ShinBuffName);
                    SetText(buff.Des, ShinBuffDes);
                }
                buff = FindById(res.BuffFactory, HeartBattleSkill.SpeedBuffId, o => o.Id);
                if (buff != null)
                {
                    SetText(buff.Name, SpeedBuffName);
                    SetText(buff.Des, SpeedBuffDes);
                }
                buff = FindById(res.BuffFactory, HeartBattleSkill.ArmorBuffId, o => o.Id);
                if (buff != null)
                {
                    SetText(buff.Name, ArmorBuffName);
                    SetText(buff.Des, ArmorBuffDes);
                }

                // 技艺
                BattleSkillTableData skill = res.CharacterBattleSkillData.Find(
                    (BattleSkillTableData o) => o != null && o.Id == HeartBattleSkill.SkillId);
                if (skill != null)
                {
                    SetText(skill.Name, ActivateSkillName);
                    SetText(skill.Description, ActivateSkillDes);
                }

                // 幕间三条
                SetVacat(res, HeartMenu.EditHeartVacatId, EditVacatTitle, EditVacatDes, EditVacatEffect);
                SetVacat(res, HeartMenu.AddHeartVacatId, AddVacatTitle, AddVacatDes, AddVacatEffect);
                SetVacat(res, HeartMenu.RemoveHeartVacatId, RemoveVacatTitle, RemoveVacatDes, RemoveVacatEffect);

                // 已经注册过的自定义心：名字和描述也跟着语言走
                if (XinEditorPlugin.Store != null)
                {
                    for (int i = 0; i < XinEditorPlugin.Store.Hearts.Count; i++)
                    {
                        HeartDefinition def = XinEditorPlugin.Store.Hearts[i];
                        if (def == null || def.BuffId < HeartConstants.CustomBuffIdMin ||
                            def.BuffId > HeartConstants.CustomBuffIdMax)
                        {
                            continue;
                        }
                        BuffTableData custom = FindById(res.BuffFactory, def.BuffId, o => o.Id);
                        if (custom != null)
                        {
                            SetText(custom.Name, def.DisplayName);
                            SetText(custom.Des, def.BuildDescription());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("英文文案覆盖失败：" + e.Message);
            }
        }

        private static void SetVacat(ResManager res, int id, string title, string des, string effect)
        {
            try
            {
                InterludeVacationConfig cfg = FindById(res.InterludeVacationFactory, id, o => o.VacatId);
                if (cfg == null)
                {
                    return;
                }
                SetText(cfg.TitleTxt, title);
                SetText(cfg.DecTxt, des);
                SetText(cfg.EffectTxt, effect);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("幕间文案覆盖失败（" + id + "）：" + e.Message);
            }
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

        private static void SetText(LocalizationKeyData text, string value)
        {
            if (text == null || string.IsNullOrEmpty(value))
            {
                return;
            }
            text.InputText = value;
        }

        // ---------------- 插件界面 ----------------

/// <summary>界面文案：整串匹配优先，匹配不到再按片段替换（对应"拼出来的句子"）。</summary>
        internal static string L(string zh)
        {
            if (!IsEnglish || string.IsNullOrEmpty(zh))
            {
                return zh;
            }
            string exact;
            if (ExactMap.TryGetValue(zh, out exact))
            {
                return exact;
            }
            string s = zh;
            for (int i = 0; i < Fragments.Length; i += 2)
            {
                s = s.Replace(Fragments[i], Fragments[i + 1]);
            }
            return s;
        }

        private static readonly Dictionary<string, string> ExactMap = new Dictionary<string, string>
        {
            { "修改「心」buff", "Edit Shin buff" },
            { "名称", "Name" },
            { "心（某某）——", "Shin (someone) - " },
            { "后缀（必填）", "Suffix (required)" },
            { "速度", "Speed" },
            { "可填 1 ~ 100（固定项，必填）", "1 ~ 100 (fixed, required)" },
            { "保存", "Save" },
            { "重置回默认", "Reset to default" },
            { "关闭", "Close" },
            { "当前已有的心", "Existing Shin" },
            { "调查员", "Investigator" },
            { "不选", "None" },
            { "删除", "Delete" },
            { "确认删除", "Confirm" },
            { "这个角色还没有自定义心，填好后点保存就会生成一颗。",
              "This investigator has no custom Shin yet; fill the form and hit Save." },
            { "还没有人给自己的心做过设定。", "Nobody has set up a Shin yet." },
            { "还没有人给自己的「心」做过设定。", "Nobody has set up a Shin yet." },
            { "速度要填 1 到 100 之间的整数（速度是固定项，必填）。",
              "Speed must be an integer between 1 and 100 (fixed value, required)." },
            { "后缀不能空着，得给这颗心起个名字。", "The suffix cannot be empty." },
            { "同一项不能选两次。", "The same bonus cannot be picked twice." },
            { "已经有一颗同名的心了，换个后缀。", "A Shin with that name already exists." },
            { "保存出错，看日志。", "Save failed - check the log." },
            { "删除出错，看日志。", "Delete failed - check the log." },
            { "已填回默认心的数值（伤害+2、速度+20、减伤1）。名字不动，点保存才生效。",
              "Restored the default values (Damage +2, Speed +20, damage taken -1). The name is untouched; hit Save to apply." },
        };

        private static readonly string[] Fragments = new string[]
        {
            "修改「心」buff —— ", "Edit Shin buff - ",
            "心（", "Shin (",
            "）——", ") - ",
            " 要填一个大于 0 的数；不想加就把左边改回「不选」。", " must be greater than 0; set it back to None to skip.",
            " 最多 ", " max ",
            "可填 0 ~ ", "0 ~ ",
            "已保存：", "Saved: ",
            "\n改动在下一场战斗生效。", "\nChanges apply from the next battle.",
            "已删掉「", "Deleted ",
            "」，这个角色以后走默认心。", "; this investigator now uses the default Shin.",
            "（还有 ", "(",
            " 颗没显示）", " more hidden)",
            "」的「心」buff 已移除。", "'s Shin buff has been removed.",
            "」获得了【心】特质。", " gained the Shin trait.",
            "」已经有【心】特质了。", " already has the Shin trait.",
            "」还没有【心】特质，先用「添加「心」」。", " has no Shin trait yet; use \"Add Shin\" first.",
            "」没有【心】特质，不用移除。", " has no Shin trait.",
            "添加【心】特质失败，看日志。", "Failed to add the Shin trait - check the log.",
            "移除「心」失败，看日志。", "Failed to remove Shin - check the log.",
            "。", ".",
        };

        internal static void LocalizeTree(Transform root)
        {
            if (!IsEnglish || root == null)
            {
                return;
            }
            try
            {
                Text[] texts = root.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null)
                    {
                        continue;
                    }
                    string value;
                    if (ExactMap.TryGetValue(texts[i].text, out value))
                    {
                        texts[i].text = value;
                    }
                    else if (texts[i].text != null && texts[i].text.StartsWith("修改「心」buff —— "))
                    {
                        texts[i].text = "Edit Shin buff - " + texts[i].text.Substring("修改「心」buff —— ".Length);
                    }
                    else if (texts[i].text != null && texts[i].text.StartsWith("心（") && texts[i].text.EndsWith("）——"))
                    {
                        string inner = texts[i].text.Substring(2, texts[i].text.Length - 5);
                        texts[i].text = "Shin (" + inner + ") - ";
                    }
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("界面文案本地化失败：" + e.Message);
            }
        }
    }
}
