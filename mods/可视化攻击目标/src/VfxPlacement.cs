using System;
using System.IO;
using UnityEngine;

namespace AttackTargetVisualizer
{
    [Serializable]
    internal sealed class VfxPlacement
    {
        private const string ResourceName = "AttackTargetVisualizer.VfxPlacement.defaults.json";
        internal const string OverrideFile = "VfxPlacement.json";

        internal static VfxPlacement Current { get; private set; } = new VfxPlacement();

        public Effect flower = new Effect { spread = 0.62f };
        public Effect wind = new Effect { spread = 0.54f };
        public Effect lockMark = new Effect();
        public Effect thrust = new Effect();
        public Effect slash = new Effect();
        public Effect sweep = new Effect();

        [Serializable]
        internal sealed class Effect
        {
            public float x = 0f;
            public float y = 0f;
            public float angle = 0f;
            public float spread = 1f;

            internal static Effect Validate(Effect effect, Effect defaults)
            {
                if (effect == null) return defaults;
                effect.x = Limit(effect.x, defaults.x, -1f, 1f);
                effect.y = Limit(effect.y, defaults.y, -1f, 1f);
                effect.angle = Limit(effect.angle, defaults.angle, -180f, 180f);
                effect.spread = Limit(effect.spread, defaults.spread, 0.25f, 1.2f);
                return effect;
            }

            private static float Limit(float value, float fallback, float min, float max)
            {
                return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
            }
        }

        internal static void Load(string pluginFile)
        {
            string defaultsJson;
            try
            {
                using (Stream stream = typeof(VfxPlacement).Assembly.GetManifestResourceStream(ResourceName))
                using (var reader = new StreamReader(stream))
                {
                    defaultsJson = reader.ReadToEnd();
                }
                Current = Read(defaultsJson);
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("特效默认参数读取失败：" + error.Message);
                Current = new VfxPlacement();
                return;
            }

            string path = Path.Combine(Path.GetDirectoryName(pluginFile), OverrideFile);
            if (!File.Exists(path)) return;

            try
            {
                if (new FileInfo(path).Length > 16384) throw new InvalidDataException("参数文件超过 16 KB");
                VfxPlacement settings = Read(defaultsJson);
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), settings);
                settings.flower = Effect.Validate(settings.flower, Current.flower);
                settings.wind = Effect.Validate(settings.wind, Current.wind);
                settings.lockMark = Effect.Validate(settings.lockMark, Current.lockMark);
                settings.thrust = Effect.Validate(settings.thrust, Current.thrust);
                settings.slash = Effect.Validate(settings.slash, Current.slash);
                settings.sweep = Effect.Validate(settings.sweep, Current.sweep);
                Current = settings;
                AttackTargetPlugin.LogInfo("已读取特效位置参数 " + path);
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("特效位置参数读取失败，使用默认值：" + error.Message);
            }
        }

        private static VfxPlacement Read(string json)
        {
            var layout = new VfxPlacement();
            JsonUtility.FromJsonOverwrite(json, layout);
            return layout;
        }
    }
}
