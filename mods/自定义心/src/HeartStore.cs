// 自定义心的存读。
// 游戏存档是加密的 ES3 格式，插件塞不进去，所以数据放在
// %LocalLow%\MeowNature\Depersonalization-Release\XinEditor\hearts.cfg，
// 用调查员的 RoleLibraryKey（建角色时生成的 GUID）当钥匙，换存档不会串号。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

            // 注意：BepInEx 的 ConfigFile.Keys 只包含"本次会话里 Bind 过"的条目，
            // 刚启动时文件里的内容还在它的"孤儿条目"里，所以必须自己扫一遍小节点名，
            // 否则重启后一条都读不出来（踩过）。
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

            // 两条附加效果。旧配置没有这些键时全部走默认值（未配置）。
            h.Effect1 = ReadEffect(section, 1);
            h.Effect2 = ReadEffect(section, 2);
            h.Effect1BuffId = _file.Bind(section, "Effect1BuffId", 0, "预设效果 1 的隐藏状态编号").Value;
            h.Effect2BuffId = _file.Bind(section, "Effect2BuffId", 0, "预设效果 2 的隐藏状态编号").Value;
            return h;
        }

        private HeartEffect ReadEffect(string section, int index)
        {
            string prefix = "Effect" + index.ToString(CultureInfo.InvariantCulture);
            int target = _file.Bind(section, prefix + "Target", 0, "目标：0自己 1所有友方 2仅限友方 3单个敌人 4所有敌人").Value;
            int trigger = _file.Bind(section, prefix + "Trigger", 0, "触发：0行动轮开始 1行动轮结束 2攻击命中后 3行动前 4行动后").Value;
            int type = _file.Bind(section, prefix + "Type", 0, "效果类型：0无 / 预设编号 / 100000+状态编号").Value;
            int layers = _file.Bind(section, prefix + "Layers", 0, "施加/获得的层数，不支持负数").Value;

            if (target < 0 || target > (int)HeartEffectTarget.AllEnemies)
            {
                target = 0;
            }
            if (trigger < 0 || trigger > (int)HeartEffectTrigger.ActionEnd)
            {
                trigger = 0;
            }
            if (layers < 0)
            {
                layers = 0;
            }
            if (layers > HeartConstants.MaxEffectLayers)
            {
                layers = HeartConstants.MaxEffectLayers;
            }
            return new HeartEffect((HeartEffectTarget)target, (HeartEffectTrigger)trigger, type, layers);
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
            WriteEffect(h.Section, 1, h.Effect1, h.Effect1BuffId);
            WriteEffect(h.Section, 2, h.Effect2, h.Effect2BuffId);
            _file.Save();

            if (!_hearts.Contains(h))
            {
                _hearts.Add(h);
            }
        }

        private void WriteEffect(string section, int index, HeartEffect effect, int buffId)
        {
            string prefix = "Effect" + index.ToString(CultureInfo.InvariantCulture);
            if (effect == null)
            {
                effect = new HeartEffect();
            }
            _file.Bind(section, prefix + "Target", 0, "目标：0自己 1所有友方 2仅限友方 3单个敌人 4所有敌人").Value = (int)effect.Target;
            _file.Bind(section, prefix + "Trigger", 0, "触发：0行动轮开始 1行动轮结束 2攻击命中后 3行动前 4行动后").Value = (int)effect.Trigger;
            _file.Bind(section, prefix + "Type", 0, "效果类型：0无 / 预设编号 / 100000+状态编号").Value = effect.EffectType;
            _file.Bind(section, prefix + "Layers", 0, "施加/获得的层数，不支持负数").Value = effect.Layers;
            _file.Bind(section, prefix + "BuffId", 0, "预设效果的隐藏状态编号，插件自动填写").Value = buffId;
        }

        public void Delete(HeartDefinition h)
        {
            if (h == null || string.IsNullOrEmpty(h.Section))
            {
                return;
            }
            string[] keys = new string[]
            {
                "RoleKey", "RoleName", "Suffix", "Speed", "BuffId", "Stats",
                "Effect1Target", "Effect1Trigger", "Effect1Type", "Effect1Layers", "Effect1BuffId",
                "Effect2Target", "Effect2Trigger", "Effect2Type", "Effect2Layers", "Effect2BuffId"
            };
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
        public int AllocateBuffId(HeartDefinition owner = null)
        {
            for (int id = HeartConstants.CustomBuffIdMin; id <= HeartConstants.CustomBuffIdMax; id++)
            {
                bool usedByOwner = owner != null &&
                    (owner.BuffId == id || owner.Effect1BuffId == id || owner.Effect2BuffId == id);
                if (!usedByOwner && !IsBuffIdUsed(id))
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
                HeartDefinition h = _hearts[i];
                if (h.BuffId == id || h.Effect1BuffId == id || h.Effect2BuffId == id)
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
                HeartDefinition byName = null;
                int sameName = 0;
                for (int i = 0; i < _hearts.Count; i++)
                {
                    if (_hearts[i].RoleName != name)
                    {
                        continue;
                    }
                    sameName++;
                    byName = _hearts[i];
                    if (string.IsNullOrEmpty(_hearts[i].RoleKey))
                    {
                        return _hearts[i];
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
