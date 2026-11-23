using System;

namespace ValennaBattle
{
    public readonly struct BladeBand
    {
        public readonly float Offset;
        public readonly int Color;
        public readonly float Opacity;

        internal BladeBand(float offset, int color, float opacity)
        {
            Offset = offset;
            Color = color;
            Opacity = opacity;
        }
    }

    // 编辑器预览与游戏内主刀光使用同一条截面和伸展曲线。
    public static class BladeRibbonProfile
    {
        public const int SectionCount = 3;
        public const int Samples = 25;
        public const float SectionDelay = 0.026f;
        public const float Duration = 0.254f;
        public const float GlowWidth = 1.58f;
        public const float GlowStrength = 0.28f;

        public static readonly BladeBand[] Bands =
        {
            new BladeBand(-1.20f, 0xA33136, 0f),
            new BladeBand(-0.90f, 0xB14031, 0.25f),
            new BladeBand(-0.58f, 0xE55E24, 0.70f),
            new BladeBand(-0.18f, 0xF5912C, 0.90f),
            new BladeBand( 0.12f, 0xFFC05C, 0.98f),
            new BladeBand( 0.39f, 0xFFF2B5, 1f),
            new BladeBand( 0.57f, 0xFFF9DC, 0.91f),
            new BladeBand( 0.80f, 0xFF9F47, 0.35f),
            new BladeBand( 1.18f, 0xE7684A, 0f)
        };

        public static float Reveal(float time) => Math.Min(1f, 0.24f + time * 10.1f);

        public static float Expand(float phase) => 0.82f + 0.36f * (float)Math.Sqrt(phase);

        public static float Width(float along, float phase)
        {
            float middle = (float)Math.Sin(along * Math.PI);
            float taper = 0.035f + 0.965f * (float)Math.Pow(Math.Max(0f, middle), 0.72);
            float variation = 0.90f + 0.10f * (float)Math.Sin(along * 38f - phase * 9f);
            return taper * (0.78f + along * 0.22f) * variation * (1f - phase * 0.58f);
        }

        public static float Brightness(float along, float phase)
        {
            float light = 1f - (float)Math.Pow(phase, 1.55);
            return light * (0.83f + 0.17f * (float)Math.Cos(along * 31f - phase * 18f));
        }
    }
}
