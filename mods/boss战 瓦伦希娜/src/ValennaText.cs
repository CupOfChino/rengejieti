using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;

namespace ValennaBattle
{
    internal static class ValennaText
    {
        private sealed class Translation
        {
            public string zh = null;
            public string en = null;
        }

        internal static LocalizationKeyData Key(string suffix, string chinese) =>
            new LocalizationKeyData { TarKey = "valenna." + suffix, InputText = chinese };

        internal static bool IsEnglish
        {
            get
            {
                if (!Singleton<LocalizationHandler>.HasInstance) return false;
                var handler = Singleton<LocalizationHandler>.Instance;
                return handler.CurrentLanguage == "English" ||
                    string.Equals(handler.CurrentLanguageCode, "en", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static void Register(LocalizationHandler handler)
        {
            var data = handler?.Data;
            if (data?.LocalizationMaps == null || data.LocalizationMaps.ContainsKey("valenna.profession")) return;
            using var stream = typeof(ValennaText).Assembly.GetManifestResourceStream("ValennaBattle.Localization.json");
            using var reader = new StreamReader(stream);
            var entries = JsonConvert.DeserializeObject<Dictionary<string, Translation>>(reader.ReadToEnd());
            var sheet = new List<LocalizationSheet>(entries.Count);
            foreach (var entry in entries)
                sheet.Add(new LocalizationSheet { Key = entry.Key, Chinese = entry.Value.zh, English = entry.Value.en });
            data.MergerSheet("Valenna", EModFolderMode.Other, sheet);
        }

        // 原版在启动、载入工坊及编辑器重读语言表时都会刷新语言列表。
        [HarmonyPatch(typeof(LocalizationHandler), nameof(LocalizationHandler.FreshLanguageType))]
        private static class LoadTranslations
        {
            private static void Prefix(LocalizationHandler __instance) => Register(__instance);
        }
    }
}
