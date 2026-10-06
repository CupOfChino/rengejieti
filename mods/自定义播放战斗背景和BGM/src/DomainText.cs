// 本地化（中/英）。
//
// 中文/繁体：一切照旧——数据文件里本来就是中文，插件界面也是中文。
// 其它语言：插件启动后把"我们自己的"数据文案覆盖成英文（只改我们这几个编号，不碰游戏本体），
//          插件自己的界面与提示也走 L()。

using System;
using System.Collections.Generic;
using MOD;
using UnityEngine;

namespace CustomBattleBg
{
    internal static class DomainText
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

        internal static string BuildDisplayName(string roleName, string suffix)
        {
            return roleName + "——" + suffix;
        }

        /// <summary>按编号取状态（buff）的名字；取不到就返回"状态 编号"。</summary>
        internal static string BuffName(int buffId)
        {
            try
            {
                if (Singleton<ResManager>.HasInstance)
                {
                    BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                    if (factory != null)
                    {
                        BuffTableData cfg = factory.GetCache(buffId.ToString());
                        if (cfg == null)
                        {
                            List<BuffTableData> all = factory.All;
                            for (int i = 0; i < all.Count; i++)
                            {
                                if (all[i] != null && all[i].Id == buffId)
                                {
                                    cfg = all[i];
                                    break;
                                }
                            }
                        }
                        if (cfg != null && cfg.Name != null && !string.IsNullOrEmpty(cfg.Name.InputText))
                        {
                            return cfg.Name.InputText;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return "状态 " + buffId;
        }

        // ---------------- 数据文案（英文）----------------

        internal const string TraitName = "Domain";
        internal const string TraitDes =
            "Materialize a battle domain of your own.\n\n" +
            "Auto-expands when the trait is awake (on entering battle): switches the battle background " +
            "and plays your custom battle BGM.\n" +
            "You can also re-expand it in battle with the skill \"Domain Expansion\".\n\n" +
            "The background and BGM are restored when the battle ends.";
        internal const string TraitWake = "Auto-expand";
        internal const string TraitSleepy = "No expansion";

        internal const string SkillName = "Domain Expansion";
        internal const string SkillDes =
            "Materialize a battle domain of your own.\n\n" +
            "Switches the battle background to your image/video and plays your battle BGM.\n\n" +
            "Can be used repeatedly in one battle.";

        internal const string EditVacatTitle = "Edit custom battle background & BGM";
        internal const string EditVacatDes =
            "Set this investigator's own battle background and battle BGM: an image/video background, " +
            "an ogg/mp3 BGM, and whether the BGM loops.";
        internal const string EditVacatEffect = "Open the Domain editor";

        internal const string AddVacatTitle = "Add Domain";
        internal const string AddVacatDes =
            "Give this investigator the Domain trait. Only with the trait can they expand their own domain in battle.";
        internal const string AddVacatEffect = "Gain the Domain trait";

        internal const string RemoveVacatTitle = "Remove Domain";
        internal const string RemoveVacatDes =
            "Remove this investigator's Domain trait; their own background/BGM settings are deleted as well.";
        internal const string RemoveVacatEffect = "Remove the Domain trait and settings";

        // ---------------- 覆盖我们自己的数据文案 ----------------

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
                TraitData trait = FindById(res.TraitFactory, DomainConstants.TraitId, o => o.Id);
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

                // 技艺
                BattleSkillTableData skill = res.CharacterBattleSkillData.Find(
                    (BattleSkillTableData o) => o != null && o.Id == DomainConstants.SkillId);
                if (skill != null)
                {
                    SetText(skill.Name, SkillName);
                    SetText(skill.Description, SkillDes);
                }

                // 幕间三条
                SetVacat(res, DomainConstants.EditVacatId, EditVacatTitle, EditVacatDes, EditVacatEffect);
                SetVacat(res, DomainConstants.AddVacatId, AddVacatTitle, AddVacatDes, AddVacatEffect);
                SetVacat(res, DomainConstants.RemoveVacatId, RemoveVacatTitle, RemoveVacatDes, RemoveVacatEffect);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("英文文案覆盖失败：" + e.Message);
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
                CustomBattleBgPlugin.LogError("幕间文案覆盖失败（" + id + "）：" + e.Message);
            }
        }

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

        // ---------------- 插件界面文案 ----------------

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
            { "编辑「领域」", "Edit Domain" },
            { "名称", "Name" },
            { "后缀（必填）", "Suffix (required)" },
            { "战斗背景", "Battle background" },
            { "战斗BGM", "Battle BGM" },
            { "循环播放", "Loop" },
            { "只占上半屏（仅视频生效，下方保留原来的背景）",
              "Top half only (video only; keeps the original background below)" },
            { "选择文件", "Choose file" },
            { "清除", "Clear" },
            { "未选择", "Not set" },
            { "确认", "Confirm" },
            { "取消", "Cancel" },
            { "关闭", "Close" },
            { "当前已有的领域", "Existing domains" },
            { "删除", "Delete" },
            { "确认删除", "Confirm" },
            { "调查员", "Investigator" },
            { "还没有人设置过领域。", "Nobody has set up a domain yet." },
            { "这个角色还没有自己的领域配置，填好后点确认就会生成一条。",
              "This investigator has no domain yet; fill the form and hit Confirm." },
            { "已保存：", "Saved: " },
            { "\n改动在下一场战斗生效。", "\nChanges apply from the next battle." },
            { "后缀不能空着，得给自己的领域起个名字。", "The suffix cannot be empty." },
            { "已经有一条同名的领域了，换个后缀。", "A domain with that name already exists." },
            { "保存出错，看日志。", "Save failed - check the log." },
            { "删除出错，看日志。", "Delete failed - check the log." },
            { "资源重名：目标文件夹里已经有同名文件，换一个名字或先删掉旧的。",
              "Duplicate resource: a file with the same name already exists. Rename it or remove the old one." },
            { "这个文件格式不对：背景只支持 mp4 / png / jpg。",
              "Unsupported format: background allows mp4 / png / jpg only." },
            { "这个文件格式不对：BGM 只支持 ogg / mp3。",
              "Unsupported format: BGM allows ogg / mp3 only." },
            { "复制资源失败，看日志。", "Failed to copy the resource - check the log." },
            { "把这个角色自己的战斗背景和BGM删掉吗？", "Delete this investigator's background/BGM settings?" },
            { "没有选择背景。", "No background selected." },
            { "没有选择BGM。", "No BGM selected." },
            { "已清除背景选择（文件不存在或被别的配置用着，没有删）。",
              "Background selection cleared (file missing or in use by another profile; not deleted)." },
            { "已清除BGM选择（文件不存在或被别的配置用着，没有删）。",
              "BGM selection cleared (file missing or in use by another profile; not deleted)." },
            { "已取消覆盖，资源库里原来的文件没有动。", "Overwrite cancelled; the existing file was kept." },
            { "打开资源库", "Open asset folder" },
            { "资源库", "Library" },
            { "从资源库选择战斗背景", "Choose battle background from the library" },
            { "从资源库选择战斗BGM", "Choose battle BGM from the library" },
            { "已从资源库选择背景：", "Background chosen from library: " },
            { "已从资源库选择BGM：", "BGM chosen from library: " },
            { "打开资源库文件夹", "Open asset folder" },
            { "资源库里还没有这个类型的文件。\n先用「选择文件」从电脑导入一个吧。",
              "No files of this type in the library yet.\nUse \"Choose file\" to import one from your computer first." },
            { "打开资源库失败，看日志。", "Failed to open the asset folder - check the log." },
        };

        private static readonly string[] Fragments = new string[]
        {
            "编辑「领域」—— ", "Edit Domain - ",
            "领域（", "Domain (",
            "）——", ") - ",
            "（还有 ", "(",
            " 条没显示）", " more hidden)",
            "已删除「", "Deleted ",
            "」，这个角色以后不再展开领域。", "; this investigator no longer expands a domain.",
            "已保存：", "Saved: ",
            "」获得了【领域】特质。", " gained the Domain trait.",
            "」已经有【领域】特质了。", " already has the Domain trait.",
            "」还没有【领域】特质，先用「添加「领域」」。", " has no Domain trait yet; use \"Add Domain\" first.",
            "」没有【领域】特质，不用移除。", " has no Domain trait.",
            "添加【领域】特质失败，看日志。", "Failed to add the Domain trait - check the log.",
            "移除【领域】失败，看日志。", "Failed to remove the Domain - check the log.",
            "」的「领域」特质与配置已移除。", "'s Domain trait and settings have been removed.",
            "清除会把这张背景文件一起删掉：", "Clearing will also delete this background file: ",
            "清除会把这首BGM文件一起删掉：", "Clearing will also delete this BGM file: ",
            "\n\n（现在没有资源库，删掉后要重新导入才能再用）",
            "\n\n(There is no resource library yet; import it again to use it)",
            "已清除并删除背景文件：", "Cleared and deleted background file: ",
            "已清除并删除BGM文件：", "Cleared and deleted BGM file: ",
            "资源库里已经有同名文件：\n", "A file with the same name already exists in the asset folder:\n",
            "\n\n要覆盖它吗？（引用这个文件的配置会换成新内容）",
            "\n\nOverwrite it? (Profiles referencing this file will switch to the new content)",
            "已打开资源库：", "Opened asset folder: ",
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
                UnityEngine.UI.Text[] texts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
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
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("界面文案本地化失败：" + e.Message);
            }
        }
    }
}
