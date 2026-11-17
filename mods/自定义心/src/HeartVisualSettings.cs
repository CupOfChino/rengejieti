// 心外观独立于数值编辑草稿；按调查员稳定编号保存。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace XinEditor
{
    public sealed class HeartVisualSettings
    {
        public float X = 0.05f;
        public float Y = 0.05f;
        public float ScaleX = 0.30f;
        public float ScaleY = 0.30f;

        public HeartVisualSettings Copy()
        {
            return new HeartVisualSettings { X = X, Y = Y, ScaleX = ScaleX, ScaleY = ScaleY };
        }

        public static bool Valid(HeartVisualSettings value)
        {
            return value != null && FiniteRange(value.X, -10f, 10f) && FiniteRange(value.Y, -10f, 10f)
                && FiniteRange(value.ScaleX, 0.01f, 10f) && FiniteRange(value.ScaleY, 0.01f, 10f);
        }

        private static bool FiniteRange(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }

        public static bool TryParse(string x, string y, string sx, string sy, out HeartVisualSettings value)
        {
            value = new HeartVisualSettings();
            return float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out value.X)
                && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out value.Y)
                && float.TryParse(sx, NumberStyles.Float, CultureInfo.InvariantCulture, out value.ScaleX)
                && float.TryParse(sy, NumberStyles.Float, CultureInfo.InvariantCulture, out value.ScaleY)
                && Valid(value);
        }
    }

    internal sealed class HeartVisualStore
    {
        private readonly string _path;
        private ConfigFile _file;
        private readonly Dictionary<string, HeartVisualSettings> _values = new Dictionary<string, HeartVisualSettings>();
        internal int Revision { get; private set; }

        internal HeartVisualStore(string path) { _path = path; }

        internal void Load()
        {
            string dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _file = new ConfigFile(_path, false) { SaveOnConfigSet = false };
            _values.Clear();
            Revision++;
            if (!File.Exists(_path)) return;
            foreach (string line in File.ReadAllLines(_path, Encoding.UTF8))
            {
                string text = line.Trim();
                if (!text.StartsWith("[role_", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal)) continue;
                string section = text.Substring(1, text.Length - 2);
                HeartVisualSettings settings = Read(section);
                if (HeartVisualSettings.Valid(settings)) _values[section.Substring(5)] = settings;
            }
        }

        private HeartVisualSettings Read(string section)
        {
            return new HeartVisualSettings {
                X = _file.Bind(section, "X", 0.05f, "相对特效原始挂点(0,0)的水平位置").Value,
                Y = _file.Bind(section, "Y", 0.05f, "相对特效原始挂点(0,0)的垂直位置").Value,
                ScaleX = _file.Bind(section, "ScaleX", 0.30f, "宽度缩放").Value,
                ScaleY = _file.Bind(section, "ScaleY", 0.30f, "高度缩放").Value
            };
        }

        internal HeartVisualSettings Get(string roleKey)
        {
            HeartVisualSettings value;
            return !string.IsNullOrEmpty(roleKey) && _values.TryGetValue(roleKey, out value)
                ? value.Copy() : new HeartVisualSettings();
        }

        internal void Save(string roleKey, HeartVisualSettings value)
        {
            if (string.IsNullOrEmpty(roleKey) || roleKey.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0)
                throw new ArgumentException("调查员编号无效");
            if (!HeartVisualSettings.Valid(value)) throw new ArgumentException("特效参数无效");
            if (_file == null) throw new InvalidOperationException("外观配置未加载");
            string section = "role_" + roleKey;
            _file.Bind(section, "X", 0.05f, "相对特效原始挂点(0,0)的水平位置").Value = value.X;
            _file.Bind(section, "Y", 0.05f, "相对特效原始挂点(0,0)的垂直位置").Value = value.Y;
            _file.Bind(section, "ScaleX", 0.30f, "宽度缩放").Value = value.ScaleX;
            _file.Bind(section, "ScaleY", 0.30f, "高度缩放").Value = value.ScaleY;
            _file.Save();
            _values[roleKey] = value.Copy();
            Revision++;
        }
    }
}
