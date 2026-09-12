// 自定义心的存读。
// 游戏存档是加密的 ES3 格式，插件塞不进去，所以数据放在
// %LocalLow%\MeowNature\Depersonalization-Release\XinEditor\hearts.cfg，
// 用调查员的 RoleLibraryKey（建角色时生成的 GUID）当钥匙，换存档不会串号。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace XinEditor
{
    internal class HeartStore
    {
        private const string SectionPrefix = "heart_";

        private ConfigFile _file;
        private readonly List<HeartDefinition> _hearts = new List<HeartDefinition>();

        public List<HeartDefinition> Hearts
        {
            get { return _hearts; }
        }

        public string FilePath { get; private set; }

        public void Load()
        {
            FilePath = Path.Combine(Application.persistentDataPath, "XinEditor", "hearts.cfg");
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _file = new ConfigFile(FilePath, true);
            _hearts.Clear();

            List<string> sections = new List<string>();
            foreach (ConfigDefinition def in _file.Keys)
            {
                if (def.Section != null && def.Section.StartsWith(SectionPrefix) && !sections.Contains(def.Section))
                {
                    sections.Add(def.Section);
                }
            }
            sections.Sort();

            for (int i = 0; i < sections.Count; i++)
            {
                _hearts.Add(ReadSection(sections[i]));
            }
        }

        private HeartDefinition ReadSection(string section)
        {
            HeartDefinition h = new HeartDefinition();
            h.Section = section;
            h.RoleKey = _file.Bind(section, "RoleKey", "", "调查员在存档里的稳定编号").Value;
            h.RoleName = _file.Bind(section, "RoleName", "", "调查员名字").Value;
            h.Suffix = _file.Bind(section, "Suffix", "", "心的后缀名").Value;
            h.Speed = _file.Bind(section, "Speed", 20, "速度加成，固定项").Value;
            h.BuffId = _file.Bind(section, "BuffId", 0, "分配给这颗心的状态编号，插件自动填写").Value;
            string raw = _file.Bind(section, "Stats", "", "除速度外的加成项，格式 类型编号:数值").Value;
            h.Stats = ParseStats(raw);
            return h;
        }

        private static List<HeartStat> ParseStats(string raw)
        {
            List<HeartStat> list = new List<HeartStat>();
            if (string.IsNullOrEmpty(raw))
            {
                return list;
            }
            char[] separators = new char[] { ',', ';' };
            string[] groups = raw.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < groups.Length; i++)
            {
                string[] kv = groups[i].Split(':');
                if (kv.Length != 2)
                {
                    continue;
                }
                int typeNum;
                int value;
                if (!int.TryParse(kv[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out typeNum))
                {
                    continue;
                }
                if (!int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                if (value == 0)
                {
                    continue;
                }
                list.Add(new HeartStat((HeartStatType)typeNum, value));
            }
            return list;
        }

        private static string FormatStats(List<HeartStat> stats)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < stats.Count; i++)
            {
                parts.Add(((int)stats[i].Type).ToString(CultureInfo.InvariantCulture) + ":" +
                          stats[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", parts.ToArray());
        }

        // 把一个心的当前内容写回文件（新建和覆盖都走这里）
        public void Save(HeartDefinition h)
        {
            if (h == null || string.IsNullOrEmpty(h.Section))
            {
                return;
            }
            _file.Bind(h.Section, "RoleKey", "", "调查员在存档里的稳定编号").Value = h.RoleKey;
            _file.Bind(h.Section, "RoleName", "", "调查员名字").Value = h.RoleName;
            _file.Bind(h.Section, "Suffix", "", "心的后缀名").Value = h.Suffix;
            _file.Bind(h.Section, "Speed", 20, "速度加成，固定项").Value = h.Speed;
            _file.Bind(h.Section, "BuffId", 0, "分配给这颗心的状态编号，插件自动填写").Value = h.BuffId;
            _file.Bind(h.Section, "Stats", "", "除速度外的加成项，格式 类型编号:数值").Value = FormatStats(h.Stats);
            _file.Save();

            if (!_hearts.Contains(h))
            {
                _hearts.Add(h);
            }
        }

        public void Delete(HeartDefinition h)
        {
            if (h == null || string.IsNullOrEmpty(h.Section))
            {
                return;
            }
            string[] keys = new string[] { "RoleKey", "RoleName", "Suffix", "Speed", "BuffId", "Stats" };
            for (int i = 0; i < keys.Length; i++)
            {
                _file.Remove(new ConfigDefinition(h.Section, keys[i]));
            }
            _file.Save();
            _hearts.Remove(h);
        }

        // 给新建的心找一个没人用的小节名
        public string NextSection()
        {
            for (int i = 1; i < 1000; i++)
            {
                string s = SectionPrefix + i.ToString("D3", CultureInfo.InvariantCulture);
                bool used = false;
                for (int j = 0; j < _hearts.Count; j++)
                {
                    if (_hearts[j].Section == s)
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

        // 找一个没人占用的 buff 编号
        public int AllocateBuffId()
        {
            for (int id = HeartConstants.CustomBuffIdMin; id <= HeartConstants.CustomBuffIdMax; id++)
            {
                if (!IsBuffIdUsed(id))
                {
                    return id;
                }
            }
            return 0;
        }

        private bool IsBuffIdUsed(int id)
        {
            for (int i = 0; i < _hearts.Count; i++)
            {
                if (_hearts[i].BuffId == id)
                {
                    return true;
                }
            }
            return false;
        }

        // 按调查员找他的心：先认编号，编号对不上再按名字兜底
        public HeartDefinition Find(RoleData role)
        {
            if (role == null)
            {
                return null;
            }
            string key = role.RoleLibraryKey;
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < _hearts.Count; i++)
                {
                    if (_hearts[i].RoleKey == key)
                    {
                        return _hearts[i];
                    }
                }
            }

            string name = SafeRoleName(role);
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < _hearts.Count; i++)
                {
                    if (string.IsNullOrEmpty(_hearts[i].RoleKey) && _hearts[i].RoleName == name)
                    {
                        return _hearts[i];
                    }
                }
            }
            return null;
        }

        // 查重：比的是完整名字（前缀 + 后缀）
        public bool IsNameTaken(string displayName, HeartDefinition except)
        {
            for (int i = 0; i < _hearts.Count; i++)
            {
                if (_hearts[i] == except)
                {
                    continue;
                }
                if (_hearts[i].DisplayName == displayName)
                {
                    return true;
                }
            }
            return false;
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
