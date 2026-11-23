using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValennaBattle
{
    internal static class StrikeArt
    {
        private static readonly List<Sprite> Loaded = new List<Sprite>();
        private static readonly Dictionary<Sprite, Sprite> Silhouettes = new Dictionary<Sprite, Sprite>();

        internal static Sprite[] Sweep { get; private set; }
        internal static Sprite[] ExecutionWave { get; private set; }
        internal static Sprite[] CuttingWind { get; private set; }
        internal static Sprite[] ElephantWind { get; private set; }
        internal static Sprite[] Companion { get; private set; }
        internal static Sprite[] ExecutionSweep { get; private set; }
        internal static Sprite[] ExecutionCompanion { get; private set; }
        internal static Sprite[] AttackSweep { get; private set; }
        internal static Sprite[] TwinThrustSweep { get; private set; }
        internal static Sprite[] PeakSweep { get; private set; }
        internal static Sprite[] AttackCompanion { get; private set; }
        internal static Sprite[] TwinThrust { get; private set; }
        internal static Sprite[] Eye { get; private set; }
        internal static Sprite[] Sheath { get; private set; }
        internal static Sprite[] Aim { get; private set; }
        internal static Sprite[] Wake { get; private set; }
        internal static Sprite[] Shot { get; private set; }
        internal static Sprite[] Pierce { get; private set; }
        internal static Sprite[] LongThrust { get; private set; }
        internal static Sprite[] Impact { get; private set; }
        internal static Sprite[] Parry { get; private set; }
        internal static Sprite ForeseenMark { get; private set; }
        internal static Sprite HuntSwords { get; private set; }
        internal static Sprite HuntGlow { get; private set; }
        internal static Sprite ExecutionFrame { get; private set; }
        internal static Sprite TremorBurstIcon { get; private set; }
        internal static Sprite ScorchBurstIcon { get; private set; }
        internal static Sprite Flare { get; private set; }
        internal static Sprite Streak { get; private set; }
        internal static Sprite HitLine { get; private set; }
        internal static Sprite Smoke { get; private set; }

        internal static void Load()
        {
            if (Sweep != null) return;
            try
            {
                Sweep = LoadFrames("slash_frame_", StrikeProfiles.SweepFrames, "D2");
                ExecutionWave = LoadFrames("execution_wave_", StrikeProfiles.ExecutionOpeningFrames, "D2");
                CuttingWind = LoadFrames("cutting_wind_", StrikeProfiles.CuttingWindFrames, "D2");
                ElephantWind = LoadFrames("elephant_wind_", StrikeProfiles.ElephantWindFrames, "D2");
                Companion = LoadFrames("companion_frame_", StrikeProfiles.CompanionFrames, "D2");
                ExecutionSweep = LoadBladeVariant("execution_slash_", Sweep, StrikeProfiles.ExecutionBrightFrames);
                ExecutionCompanion = LoadBladeVariant("execution_companion_", Companion, StrikeProfiles.ExecutionBrightFrames);
                AttackSweep = LoadBladeVariant("attack_slash_", Sweep, StrikeProfiles.AttackBrightFrames);
                // 原片切入首刺时外弧已经扫开，二、三轮直刺掠过时它已转红。
                const int twinStartFrame = 4;
                TwinThrustSweep = new Sprite[AttackSweep.Length - twinStartFrame];
                Array.Copy(AttackSweep, twinStartFrame, TwinThrustSweep, 0, TwinThrustSweep.Length);
                PeakSweep = LoadBladeVariant("peak_slash_", Sweep, StrikeProfiles.AttackBrightFrames);
                AttackCompanion = LoadBladeVariant("attack_companion_", Companion, StrikeProfiles.AttackBrightFrames);
                TwinThrust = LoadFrames("twin_thrust_frame_", StrikeProfiles.TwinThrustFrames, "D2");
                Eye = LoadFrames("eye_frame_", StrikeProfiles.EyeFrames, "D2");
                Sheath = LoadFrames("sheath_frame_", StrikeProfiles.SheathFrames, "D2");
                Aim = LoadFrames("aim_frame_", StrikeProfiles.AimFrames, "D2");
                Wake = LoadFrames("wake_frame_", StrikeProfiles.WakeFrames, "D2");
                Shot = LoadFrames("shot_frame_", StrikeProfiles.ShotFrames, "D2");
                Pierce = LoadFrames("pierce_frame_", StrikeProfiles.PierceFrames, "D2");
                LongThrust = LoadFrames("long_thrust_frame_", StrikeProfiles.LongThrustFrames, "D2");
                Impact = LoadFrames("impact_frame_", StrikeProfiles.ImpactFrames, "D2");
                Parry = LoadFrames("parry_frame_", StrikeProfiles.ParryFrames, "D2");
                ForeseenMark = LoadSprite("foreseen_mark_00");
                HuntSwords = LoadSprite("hunt_swords_00");
                HuntGlow = LoadSprite("hunt_glow_00");
                ExecutionFrame = LoadSprite("execution_frame");
                TremorBurstIcon = LoadSprite("tremor_burst_icon");
                ScorchBurstIcon = LoadSprite("scorch_burst_icon");
                Flare = LoadSprite("flare");
                Streak = LoadSprite("streak");
                HitLine = LoadSprite("hit_line");
                Smoke = LoadSprite("smoke");
            }
            catch (Exception error)
            {
                Clear();
                ValennaPlugin.Instance.Warn($"瓦伦娜演出贴图载入失败：{error.Message}");
            }
        }

        private static Sprite[] LoadFrames(string key, int count, string format)
        {
            var result = new Sprite[count];
            for (int i = 0; i < count; i++) result[i] = LoadSprite(key + i.ToString(format));
            return result;
        }

        internal static Sprite Silhouette(Sprite pose)
        {
            const string prefix = "valenna_pose_";
            if (pose == null || !pose.name.StartsWith(prefix, StringComparison.Ordinal)) return null;
            if (Silhouettes.TryGetValue(pose, out Sprite mask)) return mask;
            try { mask = LoadSprite("ghost_" + pose.name.Substring(prefix.Length), pose); }
            catch (Exception error) { ValennaPlugin.Instance.Warn($"姿势残影载入失败 {error.Message}"); }
            Silhouettes.Add(pose, mask);
            return mask;
        }

        private static Sprite[] LoadBladeVariant(string prefix, Sprite[] baseFrames, int brightFrames)
        {
            var sequence = (Sprite[])baseFrames.Clone();
            for (int i = 0; i < brightFrames; i++)
                sequence[i] = LoadSprite(prefix + i.ToString("D2"));
            return sequence;
        }

        private static Sprite LoadSprite(string key, Sprite pose = null)
        {
            string name = "valenna_" + key;
            using (Stream source = typeof(StrikeArt).Assembly.GetManifestResourceStream("ValennaBattle.Art." + name + ".png"))
            {
                if (source == null) throw new FileNotFoundException(name);
                using (var buffer = new MemoryStream())
                {
                    source.CopyTo(buffer);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = name,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!ImageConversion.LoadImage(texture, buffer.ToArray(), true))
                    {
                        Object.Destroy(texture);
                        throw new InvalidDataException(name);
                    }
                    var pivot = pose == null ? new Vector2(0.5f, 0.5f)
                        : new Vector2(pose.pivot.x / pose.rect.width, pose.pivot.y / pose.rect.height);
                    var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        pivot, pose == null ? texture.width : pose.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = name;
                    Loaded.Add(sprite);
                    return sprite;
                }
            }
        }

        internal static void Clear()
        {
            foreach (Sprite sprite in Loaded)
            {
                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }
            Loaded.Clear();
            Silhouettes.Clear();
            Sweep = ExecutionWave = CuttingWind = ElephantWind = Companion = TwinThrust = Eye = Sheath = Aim = Wake = Pierce = Impact = Parry = null;
            ForeseenMark = HuntSwords = HuntGlow = ExecutionFrame = TremorBurstIcon = ScorchBurstIcon = Flare = Streak = HitLine = Smoke = null;
            LongThrust = Shot = ExecutionSweep = ExecutionCompanion = AttackSweep = AttackCompanion = TwinThrustSweep = PeakSweep = null;
        }
    }
}
