// 领域配置的存读。
//
// 和「自定义心」一样：游戏存档是加密的 ES3，插件塞不进去，所以配置放在
// <mod 根目录>\CustomBattleAssets\profiles.cfg，
// 用调查员的 RoleLibraryKey（建角色时生成的 GUID）当钥匙，换存档不会串号。

// 自定义播放战斗背景和BGM：配置与资源的存读。
//
// 【重要】资源库放在**游戏存档目录**（Application.persistentDataPath）下，不放 mod 目录：
//   Steam 更新工坊 mod 时会把 mod 目录整个重置（本项目实测踩过，见 docs\AI协作文档.md 第 4 节坑 7），
//   玩家的背景/BGM 和配置放那里会被清掉。LocalLow 目录 Steam 不会碰，更新 mod 再多次也不丢。
//
//   %USERPROFILE%\AppData\LocalLow\MeowNature\Depersonalization-Release\CustomBattleBg\
//   ├─ Background\   战斗背景（mp4 / png / jpg）
//   ├─ Bgm\          战斗BGM（ogg / mp3）
//   └─ profiles.cfg  每个调查员的配置
//
// 编辑窗底部的"打开资源库"按钮会直接打开这个目录，玩家不需要自己找路径。
// 配置用调查员的 RoleLibraryKey（建角色时生成的 GUID）当钥匙，换存档不会串号。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace CustomBattleBg
{
    internal class DomainStore
    {
        private const string SectionPrefix = "domain_";

        private ConfigFile _file;
        private readonly List<DomainProfile> _profiles = new List<DomainProfile>();

        public List<DomainProfile> Profiles
        {
            get { return _profiles; }
        }

        public string FilePath { get; private set; }

        /// <summary>资源库根目录（游戏存档目录下，和 Steam 工坊更新无关）。</summary>
        public string AssetsRoot { get; private set; }

        /// <summary>mod 根目录（插件 DLL 往上两级）；只用于日志显示，资源不存这里。</summary>
        public string ModRoot { get; private set; }

        public string BackgroundDir
        {
            get { return Path.Combine(AssetsRoot, DomainConstants.BackgroundFolderName); }
        }

        public string BgmDir
        {
            get { return Path.Combine(AssetsRoot, DomainConstants.BgmFolderName); }
        }

        public void Load()
        {
            ModRoot = ResolveModRoot();
            // 资源库放游戏存档目录：Steam 更新工坊 mod 时会重置 mod 目录，放那里会把玩家的东西清掉
            AssetsRoot = Path.Combine(Application.persistentDataPath, DomainConstants.AssetsFolderName);
            FilePath = Path.Combine(AssetsRoot, DomainConstants.StoreFileName);
            if (!Directory.Exists(AssetsRoot))
            {
                Directory.CreateDirectory(AssetsRoot);
            }
            if (!Directory.Exists(BackgroundDir))
            {
                Directory.CreateDirectory(BackgroundDir);
            }
            if (!Directory.Exists(BgmDir))
            {
                Directory.CreateDirectory(BgmDir);
            }

            _file = new ConfigFile(FilePath, true);
            _profiles.Clear();

            // 注意：BepInEx 的 ConfigFile.Keys 只包含"本次会话里 Bind 过"的条目，
            // 刚启动时文件里的内容还在"孤儿条目"里，所以必须自己扫一遍小节点名，
            // 否则重启后一条都读不出来（自定义心踩过）。
            List<string> sections = new List<string>();
            if (File.Exists(FilePath))
            {
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length > 2 && line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string name = line.Substring(1, line.Length - 2).Trim();
                        if (name.StartsWith(SectionPrefix) && !sections.Contains(name))
                        {
                            sections.Add(name);
                        }
                    }
                }
            }
            sections.Sort();

            for (int i = 0; i < sections.Count; i++)
            {
                _profiles.Add(ReadSection(sections[i]));
            }
        }

        // 插件 DLL 在 <mod>\plugins\xxx.dll，往上两级就是 mod 根目录
        private static string ResolveModRoot()
        {
            try
            {
                string dll = typeof(DomainStore).Assembly.Location;
                if (!string.IsNullOrEmpty(dll))
                {
                    string pluginsDir = Path.GetDirectoryName(dll);
                    if (!string.IsNullOrEmpty(pluginsDir))
                    {
                        string parent = Path.GetDirectoryName(pluginsDir);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            return parent;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            // 兜底：游戏存档目录（至少能读写）
            return Application.persistentDataPath;
        }

        private DomainProfile ReadSection(string section)
        {
            DomainProfile p = new DomainProfile();
            p.Section = section;
            p.RoleKey = _file.Bind(section, "RoleKey", "", "调查员在存档里的稳定编号").Value;
            p.RoleName = _file.Bind(section, "RoleName", "", "调查员名字").Value;
            p.Suffix = _file.Bind(section, "Suffix", "", "领域的后缀名").Value;
            p.BgFile = _file.Bind(section, "BgFile", "", "战斗背景文件名（CustomBattleAssets\\Background 下）").Value;
            p.BgmFile = _file.Bind(section, "BgmFile", "", "战斗BGM文件名（CustomBattleAssets\\Bgm 下）").Value;
            p.BgmLoop = _file.Bind(section, "BgmLoop", true, "BGM 是否循环播放").Value;
            p.HalfScreen = _file.Bind(section, "HalfScreen", false,
                "背景只占上半屏（true=上方显示自定义背景、下方保留原背景）").Value;
            p.MountType = _file.Bind(section, "MountType", 0,
                "挂载行动类型（0=不挂载 1=战斗技能 2=任意法术 3=具体法术）").Value;
            p.MountId = _file.Bind(section, "MountId", 0, "挂载行动的 id（技能或法术）").Value;
            p.MountName = _file.Bind(section, "MountName", "", "挂载行动的显示名").Value;
            return p;
        }

        // 把一条配置写回文件（新建和覆盖都走这里）
        public void Save(DomainProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.Section))
            {
                return;
            }
            _file.Bind(p.Section, "RoleKey", "", "调查员在存档里的稳定编号").Value = p.RoleKey;
            _file.Bind(p.Section, "RoleName", "", "调查员名字").Value = p.RoleName;
            _file.Bind(p.Section, "Suffix", "", "领域的后缀名").Value = p.Suffix;
            _file.Bind(p.Section, "BgFile", "", "战斗背景文件名（CustomBattleAssets\\Background 下）").Value = p.BgFile;
            _file.Bind(p.Section, "BgmFile", "", "战斗BGM文件名（CustomBattleAssets\\Bgm 下）").Value = p.BgmFile;
            _file.Bind(p.Section, "BgmLoop", true, "BGM 是否循环播放").Value = p.BgmLoop;
            _file.Bind(p.Section, "HalfScreen", false,
                "背景只占上半屏（true=上方显示自定义背景、下方保留原背景）").Value = p.HalfScreen;
            _file.Bind(p.Section, "MountType", 0,
                "挂载行动类型（0=不挂载 1=战斗技能 2=任意法术 3=具体法术）").Value = p.MountType;
            _file.Bind(p.Section, "MountId", 0, "挂载行动的 id（技能或法术）").Value = p.MountId;
            _file.Bind(p.Section, "MountName", "", "挂载行动的显示名").Value = p.MountName;
            _file.Save();

            if (!_profiles.Contains(p))
            {
                _profiles.Add(p);
            }
        }

        public void Delete(DomainProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.Section))
            {
                return;
            }
            string[] keys = new string[]
            {
                "RoleKey", "RoleName", "Suffix", "BgFile", "BgmFile", "BgmLoop", "HalfScreen",
                "MountType", "MountId", "MountName"
            };
            for (int i = 0; i < keys.Length; i++)
            {
                _file.Remove(new ConfigDefinition(p.Section, keys[i]));
            }
            _file.Save();
            _profiles.Remove(p);
        }

        /// <summary>
        /// 删掉一条配置，并把它引用的背景/BGM 文件一起删掉（用户口径：
        /// "删除库中自定义的特质时也会删除对应资源"）。
        /// 若有别的配置在引用同一个文件（正常流程下不会），文件保留不动。
        /// </summary>
        public void DeleteWithAssets(DomainProfile p)
        {
            if (p == null)
            {
                return;
            }
            string bg = p.BgFile;
            string bgm = p.BgmFile;
            Delete(p);
            if (!string.IsNullOrEmpty(bg))
            {
                TryDeleteBackgroundFile(bg, p);
            }
            if (!string.IsNullOrEmpty(bgm))
            {
                TryDeleteBgmFile(bgm, p);
            }
        }

        /// <summary>删除一张背景文件（若没有别的配置在用）。返回是否真的删了。</summary>
        public bool TryDeleteBackgroundFile(string fileName, DomainProfile except)
        {
            return TryDeleteFile(Path.Combine(BackgroundDir, fileName), fileName, except, true);
        }

        /// <summary>删除一首 BGM 文件（若没有别的配置在用）。返回是否真的删了。</summary>
        public bool TryDeleteBgmFile(string fileName, DomainProfile except)
        {
            return TryDeleteFile(Path.Combine(BgmDir, fileName), fileName, except, false);
        }

        private bool TryDeleteFile(string fullPath, string fileName, DomainProfile except, bool isBackground)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(fullPath))
                {
                    return false;
                }
                // 还有别的配置在用同一个文件？那就别删（正常流程下一条文件只归一条配置，这里只是防御）
                for (int i = 0; i < _profiles.Count; i++)
                {
                    DomainProfile other = _profiles[i];
                    if (other == null || other == except)
                    {
                        continue;
                    }
                    string used = isBackground ? other.BgFile : other.BgmFile;
                    if (!string.IsNullOrEmpty(used) &&
                        string.Equals(used, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        CustomBattleBgPlugin.LogInfo("文件还有别的配置在用，保留不删：" + fileName);
                        return false;
                    }
                }
                if (!File.Exists(fullPath))
                {
                    return false;
                }
                File.Delete(fullPath);
                CustomBattleBgPlugin.LogInfo("已删除资源文件：" + fullPath);
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("删除资源文件失败：" + fullPath + " —— " + e.Message);
                return false;
            }
        }

        // 给新建的配置找一个没人用的小节名
        public string NextSection()
        {
            for (int i = 1; i < 1000; i++)
            {
                string s = SectionPrefix + i.ToString("D3", CultureInfo.InvariantCulture);
                bool used = false;
                for (int j = 0; j < _profiles.Count; j++)
                {
                    if (_profiles[j].Section == s)
                    {
                        used = true;
                        break;
                    }
                }
                if (!used)
                {
                    return s;
                }
            }
            return SectionPrefix + "999";
        }

        // 按调查员找他的配置：先认编号，编号对不上再按名字兜底
        public DomainProfile Find(RoleData role)
        {
            if (role == null)
            {
                return null;
            }
            string key = role.RoleLibraryKey;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i].RoleKey == key)
                    {
                        return _profiles[i];
                    }
                }
            }

            string name = SafeRoleName(role);
            if (!string.IsNullOrEmpty(name))
            {
                DomainProfile byName = null;
                int sameName = 0;
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (_profiles[i].RoleName != name)
                    {
                        continue;
                    }
                    sameName++;
                    byName = _profiles[i];
                    if (string.IsNullOrEmpty(_profiles[i].RoleKey))
                    {
                        return _profiles[i];
                    }
                }
                // 编号对不上时按名字兜底；同名只有一个才认，免得张冠李戴
                if (sameName == 1)
                {
                    return byName;
                }
            }
            return null;
        }

        /// <summary>按名字去大厅的角色库里查稳定编号（保存时补上空的编号）。</summary>
        public static string LookupRoleKey(string roleName)
        {
            try
            {
                if (string.IsNullOrEmpty(roleName) || !Singleton<HallWorld>.HasInstance)
                {
                    return "";
                }
                HallWorld hall = Singleton<HallWorld>.Instance;
                if (hall == null || hall.HallData == null)
                {
                    return "";
                }
                List<RoleLibraryData> roles = hall.HallData.HallLibrary.LibraryRoles;
                for (int i = 0; i < roles.Count; i++)
                {
                    HeroRoleData baseData = roles[i].BaseData;
                    if (baseData != null && SafeRoleName(baseData) == roleName)
                    {
                        return roles[i].Key;
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        // 查重：比的是完整名字（调查员名字 + 后缀）
        public bool IsNameTaken(string displayName, DomainProfile except)
        {
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i] == except)
                {
                    continue;
                }
                if (_profiles[i].DisplayName == displayName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>资源文件是否真的在目标目录里。</summary>
        public bool BackgroundExists(DomainProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.BgFile))
            {
                return false;
            }
            return File.Exists(Path.Combine(BackgroundDir, p.BgFile));
        }

        public bool BgmExists(DomainProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.BgmFile))
            {
                return false;
            }
            return File.Exists(Path.Combine(BgmDir, p.BgmFile));
        }

        internal static string SafeRoleName(RoleData role)
        {
            try
            {
                return role.Name;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
