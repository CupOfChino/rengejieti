// 心效果下拉框的“状态目录”。
//
// 照搬「自定义播放战斗背景和BGM」的做法：
//   · 游戏的状态表是按需加载的，BuffFactory.All 只有已经读进内存的一小部分；
//   · 必须再遍历 LoadFileDatas 逐个 GetConfig，才能把游戏本体和其它 mod 的状态都列出来；
//   · 排除运行时生成的状态（自定义心 / 自定义BGM 的 Comment 标记），避免自我引用。

using System;
using System.Collections;
using System.Collections.Generic;
using Game;
using MOD;

namespace XinEditor
{
    internal class HeartBuffChoice
    {
        public string Label;
        public int Value;

        public HeartBuffChoice(string label, int value)
        {
            Label = label;
            Value = value;
        }
    }

    internal static class HeartBuffCatalog
    {
        private const string CustomBattleBgCommentTag = "自定义播放战斗背景和BGM：";
        private const string CustomBattleBgGuid = "codex.depersonal.custombattlebg";

        // 预加载完成后把“普通状态”的选项缓存下来；打开编辑窗时只复制引用，不再重新扫表。
        private static List<HeartBuffChoice> _normalChoices;
        private static bool _cacheEnglish;

        /// <summary>自定义BGM 是否已启用（订阅但没启用时，不显示它的预设）。</summary>
        internal static bool HasCustomBattleBg()
        {
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos != null &&
                    BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(CustomBattleBgGuid))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }

            // 兜底：有些加载顺序下 PluginInfos 还没填好，直接看程序集。
            try
            {
                System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    if (assemblies[i] != null && assemblies[i].GetName().Name == "CustomBattleBg")
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>构造下拉框选项：0=无，随后是预设（仅在自定义BGM启用时），最后是全部状态。</summary>
        internal static List<HeartBuffChoice> BuildOptions()
        {
            List<HeartBuffChoice> list = new List<HeartBuffChoice>();
            list.Add(new HeartBuffChoice(XinText.T("（无）", "(None)"), 0));

            if (HasCustomBattleBg())
            {
                for (int i = 0; i < HeartEffectPreset.Supported.Length; i++)
                {
                    int preset = HeartEffectPreset.Supported[i];
                    list.Add(new HeartBuffChoice(HeartEffectPreset.DisplayName(preset), preset));
                }
            }

            if (_normalChoices == null || _normalChoices.Count == 0 ||
                _cacheEnglish != XinText.IsEnglish)
            {
                _normalChoices = BuildNormalChoices();
                _cacheEnglish = XinText.IsEnglish;
            }
            if (_normalChoices != null)
            {
                list.AddRange(_normalChoices);
            }

            XinEditorPlugin.LogInfo("心效果列表：普通状态 " +
                (_normalChoices != null ? _normalChoices.Count : 0) +
                " 条，加预设/无后合计 " + list.Count + " 项");
            return list;
        }

        /// <summary>收集并缓存全部普通状态选项。失败时返回 null，下次打开再试。</summary>
        private static List<HeartBuffChoice> BuildNormalChoices()
        {
            List<HeartBuffChoice> result = new List<HeartBuffChoice>();
            int total = 0;
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return null;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return null;
                }

                List<BuffTableData> all = CollectAllBuffs(factory);
                total = all.Count;
                all.Sort(delegate(BuffTableData a, BuffTableData b)
                {
                    int ia = a != null ? a.Id : 0;
                    int ib = b != null ? b.Id : 0;
                    return ia.CompareTo(ib);
                });
                for (int i = 0; i < all.Count; i++)
                {
                    BuffTableData cfg = all[i];
                    if (!IsUsableStatic(cfg))
                    {
                        continue;
                    }
                    string name = ResolveBuffName(cfg);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    result.Add(new HeartBuffChoice(
                        XinText.T("【状态】", "[Buff] ") + name,
                        HeartConstants.EffectBuffOffset + cfg.Id));
                }
                XinEditorPlugin.LogInfo("心效果状态缓存：状态表共 " + total +
                    " 条，可用 " + result.Count + " 条");
                _cacheEnglish = XinText.IsEnglish;
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("构建心效果状态缓存失败：" + e.Message);
                return null;
            }
            return result;
        }

        /// <summary>具体状态是否存在且允许被心效果引用。</summary>
        internal static bool ValidateBuff(int buffId)
        {
            BuffTableData cfg;
            return TryGetBuff(buffId, out cfg) && IsUsableStatic(cfg);
        }

        /// <summary>
        /// 同步把待加载清单读进来。只在“存档里已有普通状态效果、但注册时查不到”时兜底调用，
        /// 避免手改配置或加载顺序导致效果被误判为失效。
        /// </summary>
        internal static void LoadAllBuffsNow()
        {
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return;
                }
                List<ResloadFileData> pending = factory.LoadFileDatas;
                if (pending == null)
                {
                    return;
                }
                for (int i = 0; i < pending.Count; i++)
                {
                    ResloadFileData item = pending[i];
                    if (item == null || string.IsNullOrEmpty(item.FileName))
                    {
                        continue;
                    }
                    try
                    {
                        factory.GetConfig(item.FileName);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("补读状态表失败：" + e.Message);
            }
        }

        internal static bool TryGetBuff(int buffId, out BuffTableData cfg)
        {
            cfg = null;
            if (buffId <= 0)
            {
                return false;
            }
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    return false;
                }
                BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                if (factory == null)
                {
                    return false;
                }
                cfg = factory.GetConfig(buffId);
                return cfg != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>给描述/下拉框用的显示名。</summary>
        internal static string BuffDisplayName(int effectType)
        {
            if (effectType == 0)
            {
                return XinText.T("（无）", "(None)");
            }
            if (HeartEffectPreset.IsSupported(effectType))
            {
                return HeartEffectPreset.DisplayName(effectType);
            }
            if (effectType >= HeartConstants.EffectBuffOffset)
            {
                int buffId = effectType - HeartConstants.EffectBuffOffset;
                BuffTableData cfg;
                if (TryGetBuff(buffId, out cfg))
                {
                    string name = ResolveBuffName(cfg);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
                return XinText.T("状态 " + buffId, "Buff " + buffId);
            }
            return XinText.T("未知效果", "Unknown effect");
        }

        private static bool IsUsableStatic(BuffTableData cfg)
        {
            if (cfg == null || cfg.Id <= 0)
            {
                return false;
            }
            string comment = cfg.Comment;
            if (!string.IsNullOrEmpty(comment) &&
                (comment.StartsWith(HeartConstants.CommentTag) ||
                 comment.StartsWith(HeartConstants.EffectCommentTag) ||
                 comment.StartsWith(CustomBattleBgCommentTag)))
            {
                return false;
            }
            return true;
        }

        private static string ResolveBuffName(BuffTableData cfg)
        {
            try
            {
                if (cfg.Name != null)
                {
                    if (!string.IsNullOrEmpty(cfg.Name.InputText))
                    {
                        return cfg.Name.InputText;
                    }
                    string value = cfg.Name.GetValue();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
            catch (Exception)
            {
            }
            return cfg.Id > 0 ? XinText.T("状态 " + cfg.Id, "Buff " + cfg.Id) : "";
        }

        /// <summary>已加载的状态 + 按清单触发的按需加载，按 id 去重。</summary>
        private static List<BuffTableData> CollectAllBuffs(BuffResFactory factory)
        {
            List<BuffTableData> result = new List<BuffTableData>();
            HashSet<int> seen = new HashSet<int>();

            List<BuffTableData> loaded = new List<BuffTableData>(factory.All);
            for (int i = 0; i < loaded.Count; i++)
            {
                AddUnique(result, seen, loaded[i]);
            }

            List<ResloadFileData> pending = factory.LoadFileDatas;
            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    ResloadFileData item = pending[i];
                    if (item == null || string.IsNullOrEmpty(item.FileName))
                    {
                        continue;
                    }
                    try
                    {
                        AddUnique(result, seen, factory.GetConfig(item.FileName));
                    }
                    catch (Exception)
                    {
                        // 单条读不出来就跳过，不影响其它状态。
                    }
                }
            }
            return result;
        }

        private static void AddUnique(List<BuffTableData> list, HashSet<int> seen, BuffTableData cfg)
        {
            if (cfg == null || cfg.Id <= 0 || seen.Contains(cfg.Id))
            {
                return;
            }
            seen.Add(cfg.Id);
            list.Add(cfg);
        }

        /// <summary>后台预加载状态表，避免第一次打开编辑窗卡两秒。</summary>
        internal static IEnumerator PreloadAllBuffsCo()
        {
            BuffResFactory factory = null;
            try
            {
                if (!Singleton<ResManager>.HasInstance)
                {
                    yield break;
                }
                factory = Singleton<ResManager>.Instance.BuffFactory;
            }
            catch (Exception)
            {
            }
            if (factory == null)
            {
                yield break;
            }
            List<ResloadFileData> pending = factory.LoadFileDatas;
            if (pending == null || pending.Count == 0)
            {
                yield break;
            }

            XinEditorPlugin.LogInfo("心效果状态表预加载开始：待读取 " + pending.Count + " 个文件");
            long start = DateTime.Now.Ticks;
            int loaded = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                ResloadFileData item = pending[i];
                if (item != null && !string.IsNullOrEmpty(item.FileName))
                {
                    try
                    {
                        if (factory.GetConfig(item.FileName) != null)
                        {
                            loaded++;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                if ((i & 3) == 3)
                {
                    yield return null;
                }
            }
            long ms = (DateTime.Now.Ticks - start) / TimeSpan.TicksPerMillisecond;
            XinEditorPlugin.LogInfo("心效果状态表预加载完成：共 " + pending.Count + " 个文件、读取 " +
                loaded + " 条，耗时 " + ms + " 毫秒");
            // 数据都进内存后顺手把名称/编号缓存好，打开编辑窗时不再逐条查表。
            List<HeartBuffChoice> choices = BuildNormalChoices();
            if (choices != null)
            {
                _normalChoices = choices;
                _cacheEnglish = XinText.IsEnglish;
            }
        }
    }
}
