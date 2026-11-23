using System;

namespace ValennaBattle
{
    // System.Drawing 在构建脚本里负责 PNG 输入输出。这里用直通的 BGRA32 缓冲区处理遮罩。
    public sealed class SlashMask
    {
        private readonly float[] _pixels;
        public readonly int Width;
        public readonly int Height;

        public SlashMask(byte[] bgra, int width, int height, bool alphaOnly)
        {
            Width = width;
            Height = height;
            _pixels = new float[width * height];
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = bgra[i * 4 + 3] * (alphaOnly ? 255 : bgra[i * 4 + 2]) / 65025f;
        }

        public float Sample(float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
            float x = u * (Width - 1);
            float y = v * (Height - 1);
            int left = (int)x, top = (int)y;
            int right = Math.Min(left + 1, Width - 1), bottom = Math.Min(top + 1, Height - 1);
            float a = _pixels[top * Width + left];
            float b = _pixels[top * Width + right];
            float c = _pixels[bottom * Width + left];
            float d = _pixels[bottom * Width + right];
            return SlashFramePainter.Lerp(SlashFramePainter.Lerp(a, b, x - left),
                SlashFramePainter.Lerp(c, d, x - left), y - top);
        }

        public byte[] AsAlpha()
        {
            var result = new byte[_pixels.Length * 4];
            for (int pixel = 0; pixel < _pixels.Length; pixel++)
            {
                int i = pixel * 4;
                result[i] = result[i + 1] = result[i + 2] = 255;
                result[i + 3] = (byte)Math.Round(_pixels[pixel] * 255f);
            }
            return result;
        }
    }

    public sealed class SlashParticles
    {
        public SlashMask Wind { get; set; }
        public SlashMask Flash { get; set; }
        public SlashMask Star { get; set; }
        public SlashMask ThrustStreaks { get; set; }
        public SlashMask ImpactDebris { get; set; }
        public SlashMask SweepFragments { get; set; }
        public SlashMask SweepWake { get; set; }
        public SlashMask ThrustWake { get; set; }
    }

    public enum ThrustPattern
    {
        Twin,
        Spread,
        Long
    }

    public sealed class SlashFramePainter
    {
        private readonly SlashMask _blade;
        private readonly SlashMask _tail;
        private readonly SlashMask _grain;
        private readonly SlashMask _chip;
        private readonly SlashParticles _particles;
        private readonly int _size;
        private readonly float _span;
        private readonly float _interval;

        public SlashFramePainter(SlashMask blade, SlashMask tail, SlashMask grain, SlashMask chip, SlashParticles particles,
            int size, float span, float interval)
        {
            _blade = blade;
            _tail = tail;
            _grain = grain;
            _chip = chip;
            _particles = particles;
            _size = size;
            _span = span;
            _interval = interval;
        }

        public byte[] Draw(int frame, bool companion, bool execution = false, int extraLightFrames = 0, bool peak = false)
        {
            int size = _size;
            float time = frame * _interval + 0.004f;
            float extraLight = extraLightFrames * _interval;
            float peakLight = peak ? 1f - Smooth(0.174f, 0.29f, time) : 0f;
            var pixels = new byte[size * size * 4];
            if (!companion) SweepDebris(pixels, time);
            for (int row = 0; row < size; row++)
            {
                float y = (0.5f - (row + 0.5f) / size) * _span;
                for (int col = 0; col < size; col++)
                {
                    float x = ((col + 0.5f) / size - 0.5f) * _span - 0.11f;
                    float tiltedY = y / 0.77f;
                    float radius = (float)Math.Sqrt(x * x + tiltedY * tiltedY);
                    int index = (row * size + col) * 4;
                    var pixel = Pixel.Read(pixels, index);
                    if (!companion) PaintOuter(ref pixel, x, y, time);
                    if (radius >= 0.39f && radius <= 2.5f)
                    {
                        float angle = (float)(Math.Atan2(tiltedY, x) * 180.0 / Math.PI);
                        float u = (angle + 151f) / 292f;
                        if (u >= 0f && u <= 1f)
                        {
                            if (!companion) PaintWind(ref pixel, radius, u, time);
                            PaintBlade(ref pixel, radius, u, time, companion, execution, extraLight, peakLight);
                        }
                    }
                    pixel.Write(pixels, index);
                }
            }
            if (!companion)
            {
                Scatter(pixels, time);
                ScatterOuterDebris(pixels, time);
            }
            return pixels;
        }

        private const float ExecutionArcStart = -115f;
        private const float ExecutionArcLength = 360f;
        private const float ExecutionArcTravelTime = 0.139f;
        private const float ExecutionArcWidth = 0.83f;
        private const float ExecutionArcHeight = 0.92f;

        public byte[] DrawExecutionOpening(int frame, float tilt)
        {
            float time = (frame + 0.40f) * _interval;
            var pixels = new byte[_size * _size * 4];
            float units = _span / _size;
            for (int row = 0; row < _size; row++)
            {
                float y = (0.5f * _size - row - 0.5f) * units;
                for (int col = 0; col < _size; col++)
                {
                    float x = (col + 0.5f - 0.5f * _size) * units;
                    var pixel = new Pixel();
                    OuterArc(ref pixel, x + 0.10f, y - 0.07f, time - 0.052f,
                        0.99f, 0.14f, 1.15f, -125f, 207f, 29f, 1.47f, 0.40f, 0.125f, 0.75f, 0x9C3029, 0.91f);
                    OuterArc(ref pixel, x - 0.06f, y + 0.13f, time - 0.124f,
                        0.96f, -0.26f, 1.07f, -116f, 172f, 9f, 1.64f, 0.37f, 0.102f, 0.72f, 0x742420, 0.85f);
                    OuterArc(ref pixel, x + 0.21f, y + 0.06f, time - 0.174f,
                        0.97f, 0.24f, 0.97f, 82f, 148f, 27f, 1.28f, 0.32f, 0.067f, 0.64f, 0x8B2C27, 0.63f);
                    PaintExecutionBlade(ref pixel, x, y, time);
                    pixel.Write(pixels, (row * _size + col) * 4);
                }
            }
            ScatterExecutionOpening(pixels, time);
            FadeExecutionFootGap(pixels, -90f - tilt);
            return pixels;
        }

        private void FadeExecutionFootGap(byte[] pixels, float angle)
        {
            float units = _span / _size;
            for (int row = _size / 2; row < _size; row++)
            {
                float y = (0.5f * _size - row - 0.5f) * units;
                for (int col = 0; col < _size; col++)
                {
                    int alpha = (row * _size + col) * 4 + 3;
                    if (pixels[alpha] == 0) continue;
                    float x = (col + 0.5f - 0.5f * _size) * units;
                    float bearing = (float)(Math.Atan2(y, x) * 180.0 / Math.PI);
                    float open = Smooth(17f, 31f, Math.Abs(bearing - angle));
                    pixels[alpha] = (byte)Math.Round(pixels[alpha] * open);
                }
            }
        }

        private void PaintExecutionBlade(ref Pixel pixel, float x, float y, float time)
        {
            x /= ExecutionArcWidth;
            float vertical = y / ExecutionArcHeight;
            float radius = (float)Math.Sqrt(x * x + vertical * vertical);
            if (radius < 0.42f || radius > 2.21f) return;
            float u = Fraction(((float)(Math.Atan2(vertical, x) * 180.0 / Math.PI) - ExecutionArcStart) / ExecutionArcLength);
            float drift = Smooth(0.15f, 0.57f, time);
            float reach = ExecutionBladeRadius(u) + 0.26f * drift;
            float rimDistance = radius - reach + 0.038f;
            float rim = Smooth(0.030f, 0.102f, time) * (1f - Smooth(0.41f, 0.68f, time));
            float goldRim = rim * (1f - Smooth(0.152f, 0.29f, time));
            pixel.Blend(0xA43D30, rim * Bell(rimDistance, 0.076f) * 0.81f);
            pixel.Blend(0xFFA143, goldRim * Bell(rimDistance, 0.112f) * 0.93f);
            pixel.Blend(0xFFF0AD, goldRim * Bell(rimDistance, 0.036f) * 1.15f);

            float age = time - ExecutionArcTravelTime * u;
            if (age <= 0f || age >= 0.68f) return;

            float taper = Smooth(0f, 0.19f, u) * (1f - Smooth(0.84f, 1f, u));
            float grain = _grain.Sample(Fraction(u * 3.74f - time * 0.48f), Fraction(radius * 2.25f + time * 0.37f));
            float crest = taper * Lerp(0.30f, 1f, Smooth(0.08f, 0.44f, u));
            float width = Lerp(0.11f, 0.54f, crest) * Lerp(1f, 0.24f, drift);
            float band = (radius - reach) / width + (grain - 0.5f) * 0.10f;
            float face = Smooth(-1.33f, -0.48f, band) * (1f - Smooth(-0.02f, 0.065f, band));
            float red = Smooth(0.039f, 0.125f, age) * (1f - Smooth(0.20f, 0.56f, age)) * taper;
            float texture = _tail.Sample(0.11f + drift * 0.71f, 0.13f - band * 0.73f);
            pixel.Blend(0x6C2224, red * Bell(band + 0.17f, 0.97f) * 0.48f);
            pixel.Blend(0xBF252B, red * face * (0.81f + 0.18f * grain));
            pixel.Blend(0xFF4446, red * face * (0.32f + 0.92f * texture) * (1f - drift * 0.63f));
            pixel.Blend(0x651E20, red * Bell(band + 0.018f, 0.108f) * 0.94f);

            // 齿纹沿内缘斜切，亮边离开后依然留在红色刃面。
            float tooth = 1f - Smooth(0.09f, 0.27f, Fraction(u * 35f + band * 1.46f));
            pixel.Blend(0x671D22, red * face * tooth * Bell(band + 0.73f, 0.28f) * 0.88f);
            float light = Smooth(0f, 0.026f, age) * (1f - Smooth(0.129f, 0.251f, age)) * taper;
            float edge = radius - reach + 0.042f;
            float afterglow = Smooth(0f, 0.03f, age) * (1f - Smooth(0.16f, 0.47f, age)) * taper;
            pixel.Blend(0xF87647, afterglow * Bell(edge - 0.062f, 0.064f) * 0.90f);
            float warmFace = light * face * Bell(band + 0.33f, 0.81f);
            float core = Smooth(-0.63f, -0.24f, band) * (1f - Smooth(0.025f, 0.18f, band));
            pixel.Blend(0xFFAB50, light * Bell(edge + 0.03f, 0.34f) * 0.66f);
            pixel.Blend(0xFFAC3C, warmFace * (0.72f + grain * 0.97f));
            pixel.Blend(0xFFEA8D, warmFace * (0.39f + texture * 0.88f));
            pixel.Blend(0xFFCB51, light * core * (1.34f + grain * 0.25f));
            pixel.Blend(0xFFFFB4, light * core * (1.18f - tooth * 0.20f));
            pixel.Blend(0xFFFBE2, light * Bell(edge + 0.028f, 0.115f) * 1.62f);
        }

        private static float ExecutionBladeRadius(float u)
        {
            float turn = u * (float)(2.0 * Math.PI);
            return 1.19f + (float)Math.Cos(turn - 0.7f) * 0.09f + (float)Math.Sin(2f * turn + 0.4f) * 0.048f;
        }

        private void ScatterExecutionOpening(byte[] pixels, float time)
        {
            float scale = _size / _span;
            for (int i = 0; i < 52; i++)
            {
                bool onBlade = i < 10;
                bool spark = i < 25;
                bool star = spark && (onBlade ? i % 3 == 0 : i % 2 == 0);
                float u = (i * 19 % 53 + 0.49f) / 53f;
                float delay = spark ? 0.010f + u * (onBlade ? 0.092f : 0.061f) + (i % 4) * 0.008f
                    : 0.079f + u * 0.096f + (i % 5) * 0.016f;
                float age = time - delay;
                float life = spark ? 0.135f + (i % 4) * 0.025f : 0.34f + (i % 5) * 0.064f;
                if (age <= 0f || age >= life) continue;
                float path = u + age * (0.052f + (i % 5) * 0.071f);
                float angle = (ExecutionArcStart + path * ExecutionArcLength) * (float)(Math.PI / 180.0);
                float reach = ExecutionBladeRadius(path) + 0.26f * Smooth(0.15f, 0.57f, time);
                if (onBlade) reach -= 0.037f - (i % 3 - 1) * 0.028f;
                else reach += 0.13f + (i % 4) * 0.067f + age * (spark ? 0.95f : 1.48f);
                float x = _size * 0.50f + (float)Math.Cos(angle) * reach * scale * ExecutionArcWidth;
                float drop = spark ? 0f : age * age * 0.62f;
                float y = _size * 0.50f - ((float)Math.Sin(angle) * reach * ExecutionArcHeight - drop) * scale;
                float width = (onBlade ? 0.16f + i % 5 * 0.038f : spark ? 0.035f + i % 5 * 0.014f : 0.29f + i % 5 * 0.091f) * scale;
                float height = width * (star ? 1f : spark ? 0.23f : 0.67f + i % 3 * 0.115f);
                float alpha = Smooth(0f, spark ? 0.018f : 0.041f, age) * (1f - Smooth(life * 0.37f, life, age));
                float rotation = spark ? -angle : -angle + i * 0.81f + age * 4.6f;
                SlashMask shape = star ? _particles.Star : spark ? _particles.Flash
                    : i % 4 == 1 ? _particles.SweepFragments : i % 4 == 2 ? _particles.SweepWake : _chip;
                if (spark)
                {
                    if (onBlade)
                        Stamp(pixels, _size, _size, shape, x, y, width * 1.55f, height * 1.58f, rotation, 0xFFA439, alpha * 0.68f);
                    Stamp(pixels, _size, _size, shape, x, y, width, height, rotation,
                        0xFFFFF4, alpha * (onBlade ? 1.53f : 0.88f));
                    continue;
                }
                Stamp(pixels, _size, _size, shape, x, y, width, height, rotation,
                    0x7E2729, alpha * 0.94f);
                Stamp(pixels, _size, _size, shape, x + width * 0.068f, y - height * 0.075f,
                    width * 0.83f, height * 0.78f, rotation, 0xFF514A, alpha * 1.13f);
            }
        }

        public byte[] DrawCuttingWind(int frame)
        {
            float time = (frame + 0.92f) * _interval;
            var pixels = new byte[_size * _size * 4];
            float units = _span / _size;
            for (int row = 0; row < _size; row++)
            {
                float y = (0.5f * _size - row - 0.5f) * units;
                for (int col = 0; col < _size; col++)
                {
                    float x = (col + 0.5f - 0.5f * _size) * units;
                    var pixel = new Pixel();
                    PaintCrosswind(ref pixel, x + 0.10f, y - 0.09f, time - 0.032f,
                        0.84f, 0.543f, 0.38f, -166f, 153f, 1.21f, 0.68f);
                    OuterArc(ref pixel, x + 0.04f, y + 0.08f, time - 0.055f,
                        0.89f, 0.454f, 0.84f, 105f, 142f, 83f, 1.39f, 0.27f, 0.108f, 0.57f,
                        0xA14537, 0.78f, 0.026f);
                    OuterArc(ref pixel, x + 0.01f, y + 0.11f, time - 0.094f,
                        0.89f, 0.454f, 0.84f, 122f, 101f, 82f, 1.51f, 0.24f, 0.047f, 0.52f,
                        0x844137, 0.51f, 0.016f);
                    OuterArc(ref pixel, x - 0.15f, y + 0.15f, time - 0.079f,
                        0.95f, -0.312f, 0.73f, -122f, 147f, 62f, 1.46f, 0.34f, 0.086f, 0.59f,
                        0x884039, 0.75f, 0.031f);
                    pixel.Write(pixels, (row * _size + col) * 4);
                }
            }
            return pixels;
        }

        public byte[] DrawElephantWind(float phase, float duration)
        {
            float time = phase * duration;
            float remain = time < 0.50f ? Lerp(1f, 2f / 3f, Smooth(0f, 0.50f, time))
                : time < 0.80f ? Lerp(2f / 3f, 1f / 3f, Smooth(0.50f, 0.80f, time))
                : Lerp(1f / 3f, 0f, Smooth(0.80f, 0.88f, time));
            float arcAge = 0.158f + 0.19f * Math.Min(1f, time / 0.88f);
            float near = remain * (1f - 0.30f * Smooth(0.71f, 0.88f, time));
            float thin = Lerp(1f, 0.91f, Smooth(0.58f, 0.88f, time));
            float branches = remain * Smooth(0.11f, 0.38f, time) * (1f - Smooth(0.57f, 0.82f, time));
            float lastWind = 0.24f * Smooth(0.71f, 0.88f, time) * (1f - Smooth(0.91f, duration, time));
            float lastWidth = 0.12f * thin * Lerp(1f, 0.46f, Smooth(0.80f, 0.97f, time));
            var pixels = new byte[_size * _size * 4];
            float units = _span / _size;
            for (int row = 0; row < _size; row++)
            {
                float y = (0.5f * _size - row - 0.5f) * units;
                for (int col = 0; col < _size; col++)
                {
                    float x = (col + 0.5f - 0.5f * _size) * units;
                    var pixel = new Pixel();
                    OuterArc(ref pixel, x + 0.19f, y - 0.08f, arcAge,
                        0.97f, -0.24f, 0.71f, 21f, 182f, 86f, 1.20f, 0.35f, 0.31f * thin, 0.92f,
                        0xEE5C4E, 0.92f * near, 0.18f);
                    OuterArc(ref pixel, x - 0.04f, y + 0.04f, arcAge,
                        0.96f, 0.28f, 0.61f, -110f, 231f, 68f, 1.45f, 0.31f, 0.169f * thin, 0.92f,
                        0xA8342F, remain * 1.12f, 0.045f);
                    if (branches > 0f)
                    {
                        OuterArc(ref pixel, x + 0.03f, y + 0.22f, arcAge - 0.03f,
                            0.81f, 0.584f, 0.67f, -163f, 153f, 31f, 1.16f, 0.28f, 0.111f, 0.91f,
                            0xC6473F, branches * 0.79f, 0.057f);
                        OuterArc(ref pixel, x + 0.43f, y - 0.14f, arcAge - 0.05f,
                            0.84f, -0.543f, 0.74f, 131f, 142f, -18f, 1.51f, 0.23f, 0.089f, 0.94f,
                            0x96342D, branches * 0.84f, 0.035f);
                    }
                    OuterArc(ref pixel, x + 0.17f, y + 0.12f, arcAge - 0.014f,
                        0.93f, -0.35f, 0.78f, 94f, 196f, -38f, 1.63f, 0.31f, lastWidth, 0.96f,
                        0x953230, Math.Max(remain * 0.95f, lastWind), 0.035f);
                    pixel.Write(pixels, (row * _size + col) * 4);
                }
            }
            return pixels;
        }

        public byte[] DrawPrime(int width, int height, float time, bool eye)
        {
            var pixels = new byte[width * height * 4];
            float red = eye ? Smooth(0.029f, 0.093f, time) * (1f - Smooth(0.218f, 0.345f, time))
                : Smooth(0.142f, 0.218f, time) * (1f - Smooth(0.235f, 0.305f, time)) * 0.49f;
            float warm = Smooth(0f, eye ? 0.012f : 0.035f, time) * (eye
                ? (1f - Smooth(0.047f, 0.12f, time))
                : (1f - Smooth(0.103f, 0.235f, time)));
            float reach = Lerp(eye ? 0.38f : 0.45f, 0.99f, Smooth(0f, eye ? 0.127f : 0.092f, time));
            for (int row = 0; row < height; row++)
            {
                float y = (row + 0.5f) * 2f / height - 1f;
                for (int col = 0; col < width; col++)
                {
                    float x = (col + 0.5f) * 2f / width - 1f;
                    float taper = 1f - Smooth(0.28f, 1f, Math.Abs(x) / reach);
                    float line = taper * Bell(y, 0.054f);
                    float bloom = Bell(x, 0.60f) * Bell(y, 0.29f);
                    float center = Bell(x, eye ? 0.17f : 0.21f) * Bell(y, eye ? 0.32f : 0.49f);
                    var pixel = new Pixel();
                    pixel.Blend(0xEB4A4C, red * bloom * 0.91f);
                    pixel.Blend(0xF95B55, red * line * 0.97f);
                    pixel.Blend(0xFF695D, red * center * (eye ? 1.39f : 0.55f));
                    pixel.Blend(0xFFC6A9, eye ? red * Bell(x, 0.118f) * Bell(y, 0.12f) * 0.62f : 0f);
                    pixel.Blend(0xFDB951, warm * bloom * 0.69f);
                    pixel.Blend(0xFFF0C9, warm * (line * 1.05f + center));
                    float flares = Bell(x, 0.083f) * Bell(y - x * 3.6f, eye ? 0.26f : 0.41f);
                    pixel.Blend(0xFFFBE7, warm * (Bell(x, 0.14f) * Bell(y, 0.09f) * 1.62f + flares * 0.63f));
                    pixel.Write(pixels, (row * width + col) * 4);
                }
            }
            return pixels;
        }

        public byte[] DrawAim(int width, int height, float phase)
        {
            // 取自「自定义心」xin_glow 帧内的金黄外沿、亮黄和高光。
            const int rimGlow = 0xFED281;
            const int rimYellow = 0xFEF783;
            const int rimLight = 0xFFF9A9;
            const int rimFlash = 0xFDFEF8;
            var pixels = new byte[width * height * 4];
            float live = Smooth(0f, 0.20f, phase);
            float gather = Smooth(0.02f, 0.57f, phase);
            float radiusY = Lerp(0.68f, 0.52f, gather);
            float radiusX = Lerp(0.17f, 0.109f, gather);
            float echo = Smooth(0f, 0.17f, phase) * (1f - Smooth(0.27f, 0.64f, phase));
            float flash = Smooth(0.66f, 0.83f, phase) * (1f - Smooth(0.84f, 1f, phase));
            for (int row = 0; row < height; row++)
            {
                float y = 2f * (row + 0.5f) / height - 1f;
                for (int col = 0; col < width; col++)
                {
                    float x = 2f * (col + 0.5f) / width - 1f;
                    float rx = x / radiusX;
                    float ry = y / radiusY;
                    float rim = (float)Math.Sqrt(rx * rx + ry * ry) - 1f;
                    float rimStrength = 0.70f + 0.29f * Smooth(-0.85f, 0.55f, -rx - ry * 0.38f);
                    float outerX = (x + 0.077f * (1f - gather)) / (radiusX * 1.50f);
                    float outerY = y / (radiusY * 1.29f);
                    float outer = (float)Math.Sqrt(outerX * outerX + outerY * outerY) - 1f;
                    var pixel = new Pixel();
                    pixel.Blend(rimGlow, echo * Bell(outer, 0.17f) * Smooth(-0.50f, 0.72f, -ry + rx * 0.45f) * 0.69f);
                    pixel.Blend(rimGlow, live * Bell(rim, 0.46f) * 0.36f);
                    pixel.Blend(rimYellow, live * rimStrength * (1f - Smooth(0.085f, 0.25f, Math.Abs(rim))));
                    pixel.Blend(rimLight, live * rimStrength * Bell(rim + 0.041f, 0.18f) * 0.94f);
                    pixel.Blend(rimFlash, flash * (Bell(x, 0.40f) * Bell(y, 0.026f) * 1.24f
                        + Bell(x + radiusX, 0.072f) * Bell(y + 0.22f, 0.16f)));
                    pixel.Write(pixels, (row * width + col) * 4);
                }
            }
            return pixels;
        }

        public byte[] DrawShot(int width, int height, float time, SlashMask shape)
        {
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                float y = 2f * (row + 0.5f) / height - 1f;
                for (int col = 0; col < width; col++)
                {
                    float x = (col + 0.5f) / width;
                    var pixel = new Pixel();
                    PierceBeam(ref pixel, shape, x, y, time - 0.013f, -0.30f,
                        0.238f, 0.76f, 0.13f, 0.058f, 0.060f, 0xE85637, 0.79f,
                        extension: 0.012f, tilt: -0.008f, coreTint: 0xFFAE67);
                    PierceBeam(ref pixel, shape, x, y, time - 0.032f, 0.23f,
                        0.207f, 0.75f, 0.19f, 0.040f, 0.091f, 0xF96A3B, 0.82f,
                        extension: 0.016f, tilt: 0.036f, coreTint: 0xFFD891);
                    PierceBeam(ref pixel, shape, x, y, time, 0.00f,
                        0.266f, 0.92f, 0.043f, 0.030f, 0.149f, 0xFF701D, 1.13f,
                        extension: 0.017f, tilt: -0.017f, coreTint: 0xFFFFED,
                        hotSide: 1f, hotLife: 0.047f);
                    pixel.Write(pixels, (row * width + col) * 4);
                }
            }
            return pixels;
        }

        public byte[] DrawWake(int width, int height, float phase)
        {
            var pixels = new byte[width * height * 4];
            float innerFade = 1f - Smooth(0.59f, 0.96f, phase);
            float dust = Smooth(0.02f, 0.21f, phase) * (1f - Smooth(0.36f, 0.94f, phase));
            Stamp(pixels, width, height, _particles.ThrustWake, width * (0.51f + phase * 0.34f),
                height * (0.39f - phase * 0.20f), width * 0.65f, height * 0.55f,
                -0.24f - phase * 0.19f, 0xD15B58, dust * 0.41f);
            float dustEcho = Smooth(0.15f, 0.39f, phase) * (1f - Smooth(0.55f, 1f, phase)) * innerFade;
            Stamp(pixels, width, height, _particles.ThrustWake, width * (0.39f + phase * 0.45f),
                height * (0.57f + phase * 0.25f), width * 0.61f, height * 0.62f,
                0.21f + phase * 0.18f, 0xA8433D, dustEcho * 0.44f);
            Stamp(pixels, width, height, _particles.ThrustStreaks, width * (0.50f + phase * 0.36f),
                height * 0.44f, width * (0.69f + phase * 0.24f), height * 0.68f,
                -0.04f, 0x8F3836, dust * 0.41f);
            float outerAge = Smooth(0.17f, 0.94f, phase);
            float outerFade = Smooth(0.16f, 0.36f, phase) * (1f - Smooth(0.63f, 0.98f, phase));
            for (int row = 0; row < height; row++)
            {
                float y = 2f * (row + 0.5f) / height - 1f;
                for (int col = 0; col < width; col++)
                {
                    float u = (col + 0.5f) / width;
                    float ends = Smooth(0.02f, 0.22f, u) * (1f - Smooth(0.78f, 0.998f, u));
                    int index = (row * width + col) * 4;
                    var pixel = Pixel.Read(pixels, index);
                    // 最薄的风边在直闪过去后才向上下两边卷开。
                    float bow = 2f * (float)Math.Sqrt(u * (1f - u)) * (0.45f + outerAge * 0.34f);
                    float tilt = (u - 0.51f) * (0.12f + 0.16f * outerAge);
                    float upper = Bell(y + bow - tilt, 0.022f) * Smooth(0.26f, 0.69f, u) * ends;
                    float lower = Bell(y - bow * 0.94f - tilt, 0.019f) * (1f - Smooth(0.47f, 0.93f, u)) * ends;
                    pixel.Blend(0x793537, outerFade * (upper * 0.44f + lower * 0.38f));
                    for (int ribbon = 0; ribbon < 3; ribbon++)
                    {
                        float age = phase - (1f - u) * 0.15f - ribbon * 0.049f;
                        if (age < 0f) continue;
                        float strength = Smooth(0f, 0.115f, age) * (1f - Smooth(0.21f, 0.79f, age)) * innerFade;
                        float flow = (float)Math.Sin(u * 4.65f + 0.93f + ribbon * 1.88f - phase * 1.50f);
                        float axis = (ribbon - 1f) * (0.17f + age * 0.29f) + flow * (0.33f + 0.18f * age);
                        float grain = _grain.Sample(Fraction(u * 2.0f - phase * 0.8f),
                            Fraction(y * 1.51f + ribbon * 0.27f));
                        float widthAt = Lerp(0.124f, 0.037f, Smooth(0.12f, 0.77f, age))
                            * Lerp(0.26f, 1f, (float)Math.Sin(u * Math.PI));
                        float cross = y - axis;
                        float band = Bell(cross, widthAt * (0.82f + grain * 0.48f));
                        pixel.Blend(ribbon == 0 ? 0xE2A5A5 : 0xBD6769, ends * strength * band * 0.75f);
                        float filament = Bell(cross + widthAt * 0.44f, 0.022f)
                            + Bell(cross - widthAt * 0.34f, 0.013f) * 0.72f;
                        pixel.Blend(ribbon == 0 ? 0xFFCBCA : 0xA6494B,
                            ends * strength * filament * (0.39f + grain * 0.38f));
                    }

                    for (int spark = 0; spark < 11; spark++)
                    {
                        float age = phase - 0.07f - (spark % 4) * 0.062f;
                        if (age < 0f || age >= 0.56f) continue;
                        float x = 0.17f + (spark % 7) * 0.088f + age * (0.10f + (spark % 3) * 0.11f);
                        float v = ((spark * 11 % 17) - 8) * (0.038f + age * 0.112f);
                        float opacity = Smooth(0f, 0.07f, age) * (1f - Smooth(0.18f, 0.56f, age));
                        float mask = _chip.Sample(0.5f + (u - x) * 19f, 0.5f + (y - v) * 20f);
                        pixel.Blend(spark % 3 == 0 ? 0xE75041 : 0xFFF0C8, opacity * mask * 0.96f);
                    }
                    pixel.Write(pixels, index);
                }
            }
            for (int spark = 0; spark < 7; spark++)
            {
                float age = phase - (spark % 4) * 0.038f;
                if (age < 0f || age >= 0.24f) continue;
                float alpha = Smooth(0f, 0.044f, age) * (1f - Smooth(0.085f, 0.24f, age));
                float x = width * (0.24f + spark * 0.093f + age * 0.21f);
                float y = height * (0.48f + ((spark * 7 % 13) - 6) * (0.038f + age * 0.092f));
                float size = width * (0.088f + spark % 3 * 0.028f) * (1f + age * 0.7f);
                Stamp(pixels, width, height, _particles.Star, x, y, size, size * 0.78f,
                    -0.72f + spark * 0.29f, spark % 3 == 0 ? 0xFFE09A : 0xFFF5DD, alpha);
            }
            return pixels;
        }

        public static byte[] DrawHitLine(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                float along = 1f - Math.Abs(2f * (x + 0.5f) / width - 1f);
                float taper = (float)Math.Pow(along, 0.72);
                float halfThickness = height * 0.46f * taper;
                for (int y = 0; y < height; y++)
                {
                    float distance = Math.Abs(y + 0.5f - height * 0.5f);
                    float edge = Math.Max(0f, Math.Min(1f, halfThickness - distance + 0.25f));
                    float core = Math.Max(0f, Math.Min(1f, halfThickness * 0.38f - distance + 0.5f));
                    int index = (y * width + x) * 4;
                    pixels[index] = (byte)(16f + 20f * core);
                    pixels[index + 1] = (byte)(7f + 44f * core);
                    pixels[index + 2] = (byte)(112f + 143f * core);
                    pixels[index + 3] = (byte)(255f * edge * (0.38f + 0.62f * taper));
                }
            }
            return pixels;
        }

        public byte[] DrawImpact(int width, int height, float time)
        {
            var pixels = new byte[width * height * 4];
            float debris = Smooth(0.022f, 0.115f, time) * (1f - Smooth(0.17f, 0.53f, time));
            float spread = Smooth(0f, 0.40f, time);
            Stamp(pixels, width, height, _particles.ImpactDebris, width * (0.51f + spread * 0.05f),
                height * (0.52f - spread * 0.07f), width * Lerp(0.27f, 0.49f, spread),
                height * Lerp(0.31f, 0.56f, spread), -0.31f + spread * 0.45f, 0xE35147, debris * 0.52f);
            float spray = Smooth(0.044f, 0.188f, time) * (1f - Smooth(0.24f, 0.57f, time));
            Stamp(pixels, width, height, _particles.ImpactDebris, width * (0.54f + spread * 0.34f),
                height * (0.47f - spread * 0.28f), width * 0.31f, height * 0.26f,
                -0.73f + spread * 0.62f, 0xDB4B45, spray * 0.78f);
            Stamp(pixels, width, height, _particles.ImpactDebris, width * (0.42f - spread * 0.28f),
                height * (0.54f + spread * 0.25f), width * 0.29f, height * 0.30f,
                0.62f + spread * 0.86f, 0xAD3634, spray * 0.67f);

            // 原片 735、957、982：靶点的白金闪花有近有远，红色切痕由运行时按招式方向绘制。
            // 先亮交点，外侧的四角星沿着入射方向接力，再留下较慢的红屑。
            for (int star = 0; star < 8; star++)
            {
                float age = time - (star == 0 ? 0f : 0.014f + (star % 4) * 0.025f);
                float life = star == 0 ? 0.108f : 0.095f + (star % 3) * 0.031f;
                if (age < 0f || age >= life) continue;
                float intensity = Smooth(0f, 0.014f, age) * (1f - Smooth(life * 0.36f, life, age));
                float a = star * 2.40f + 0.60f;
                float distance = star == 0 ? 0f : 0.115f + (star % 4) * 0.064f + age * 0.29f;
                float x = width * (0.5f + (float)Math.Cos(a) * distance);
                float y = height * (0.5f + (float)Math.Sin(a) * distance * 1.16f);
                float size = width * (star == 0 ? 0.25f : 0.10f + (star % 3) * 0.052f);
                float turn = (star % 3 - 1) * 0.17f;
                Stamp(pixels, width, height, _particles.Star, x, y, size * 1.36f, size * 1.51f,
                    turn, 0xFFA957, intensity * 0.58f);
                Stamp(pixels, width, height, _particles.Star, x, y, size, size * 1.18f,
                    turn, 0xFFFFEF, intensity * 1.85f);
            }

            for (int particle = 0; particle < 31; particle++)
            {
                bool glint = particle % 2 == 0;
                float age = time - (glint ? 0.003f : 0.025f) - (particle % 5) * 0.014f;
                float life = glint ? 0.114f + (particle % 5) * 0.023f : 0.29f + (particle % 4) * 0.048f;
                if (age < 0f || age >= life) continue;
                float angle = particle * 2.39996f;
                float distance = (0.16f + (particle % 4) * 0.047f) + age * (0.66f + (particle % 5) * 0.34f);
                float x = width * (0.49f + (float)Math.Cos(angle) * distance + age * 0.15f);
                float y = height * (0.51f + (float)Math.Sin(angle) * distance * 0.74f + age * age * 0.54f);
                float size = width * (0.036f + (particle % 4) * 0.018f) * (1f - age / life * 0.67f);
                float alpha = Smooth(0f, 0.021f, age) * (1f - Smooth(life * 0.31f, life, age));
                Stamp(pixels, width, height, glint ? _particles.Flash : _chip, x, y, size,
                    size * (glint ? 0.24f : 0.53f), angle + age * (glint ? 0.7f : 5f),
                    glint ? 0xFFF3DF : (particle % 4 == 1 ? 0xFF6960 : 0xAA302C), alpha * 0.96f);
            }
            return pixels;
        }

        public byte[] DrawParry(int size, float time)
        {
            var pixels = new byte[size * size * 4];
            float center = size * 0.5f;
            float flash = Smooth(0f, 0.012f, time) * (1f - Smooth(0.024f, 0.168f, time));
            Stamp(pixels, size, size, _particles.Star, center, center, size * 0.61f, size * 0.56f,
                0.42f, 0xFFC464, flash * 0.94f);
            for (int ray = 0; ray < 18; ray++)
            {
                float age = time - (ray % 4) * 0.011f;
                if (age <= 0f || age >= 0.28f) continue;
                float strength = Smooth(0f, 0.023f, age) * (1f - Smooth(0.067f, 0.27f, age));
                float angle = ray * (float)(Math.PI * 2.0 / 18.0) + 0.35f * age;
                float distance = size * (0.12f + ray % 3 * 0.040f + age * (0.53f + ray % 4 * 0.34f));
                float x = center + (float)Math.Cos(angle) * distance;
                float y = center + (float)Math.Sin(angle) * distance * 0.92f;
                float length = size * (0.20f + (ray % 3) * 0.077f) * (1f - age * 1.8f);
                Stamp(pixels, size, size, _particles.Star, x, y, length, length * 0.23f,
                    angle, ray % 3 == 0 ? 0xF24B30 : 0xFFD78C, strength);
                Stamp(pixels, size, size, _particles.Flash, x, y, length * 0.30f, length * 0.10f,
                    angle, 0xFFF8DD, strength * 0.93f);
            }
            return pixels;
        }

        public byte[] DrawPierce(int width, int height, float time, SlashMask shape, ThrustPattern pattern, int extraLightFrames = 0)
        {
            var pixels = new byte[width * height * 4];
            float extraLight = extraLightFrames * _interval;
            for (int row = 0; row < height; row++)
            {
                float y = 2f * (row + 0.5f) / height - 1f;
                for (int col = 0; col < width; col++)
                {
                    float x = (col + 0.5f) / width;
                    var pixel = new Pixel();
                    if (pattern == ThrustPattern.Twin)
                    {
                        PierceBeam(ref pixel, shape, x, y, time, -0.22f,
                            0.184f, 0.88f, 0.092f, 0.03f, 0.185f, 0xFF9F2B, 1.10f,
                            extension: 0.027f, tilt: -0.12f, coreTint: 0xFFF0A0, hotSide: 1f, hotLife: 0.075f,
                            extraLight: extraLight);
                        PierceBeam(ref pixel, shape, x, y, time - 0.026f, 0.40f,
                            0.176f, 0.61f, 0.16f, 0.18f, 0.109f, 0xFF9C55, 0.94f,
                            extension: 0.024f, tilt: 0.11f, coreTint: 0xFFF3BE, extraLight: extraLight);
                    }
                    else if (pattern == ThrustPattern.Spread)
                    {
                        PierceBeam(ref pixel, shape, x, y, time - 0.007f, -0.29f,
                            0.139f, 0.79f, 0.19f, 0.10f, 0.081f, 0xFF744F, 0.86f,
                            extension: 0.020f, tilt: -0.035f, coreTint: 0xFFC0A4, hotSide: 1f);
                        PierceBeam(ref pixel, shape, x, y, time - 0.012f, 0.38f,
                            0.117f, 0.67f, 0.28f, 0.15f, 0.052f, 0xEF4B47, 0.84f,
                            extension: 0.018f, tilt: 0.09f, coreTint: 0xFF8A77);
                        float mainTilt = Lerp(-0.46f, -0.12f, Smooth(0.04f, 0.14f, time));
                        PierceBeam(ref pixel, shape, x, y, time, 0.05f,
                            0.222f, 0.88f, 0.10f, 0.043f, 0.162f, 0xFFB92E, 1.16f,
                            extension: 0.023f, tilt: mainTilt, coreTint: 0xFFFFF3, hotSide: 1f, hotLife: 0.086f);
                    }
                    else
                    {
                        // 末次直刺从刀尖向前拉出一条细亮线，红色风压沿上下两侧张开。
                        PierceBeam(ref pixel, shape, x, y, time - 0.011f, 0.02f,
                            0.24f, 0.866f, 0.10f, 0.047f, 0.144f, 0xBD443C, 0.63f,
                            extension: 0.029f, tilt: 0.12f, coreTint: 0xE56A5D, hotSide: -1f);
                        PierceBeam(ref pixel, shape, x, y, time, -0.09f,
                            0.196f, 0.90f, 0.082f, 0.035f, 0.091f, 0xFF8C37, 1.09f,
                            extension: 0.025f, tilt: -0.055f, coreTint: 0xFFFBE8, hotSide: 1f, hotLife: 0.059f,
                            extraLight: extraLight);
                    }
                    pixel.Write(pixels, (row * width + col) * 4);
                }
            }
            if (pattern != ThrustPattern.Twin) ScatterPierce(pixels, width, height, time);
            return pixels;
        }

        private static void PierceBeam(ref Pixel pixel, SlashMask mask, float x, float y, float time,
            float lane, float life, float length, float travel, float left, float thickness, int tint, float opacity,
            float extension, float tilt = 0f, int coreTint = 0xFFF8E2, float hotSide = 0.62f, float hotLife = 0.065f,
            float extraLight = 0f)
        {
            if (time < 0f || time >= Math.Max(life, hotLife + extraLight)) return;
            float age = Math.Min(1f, time / life);
            float run = length * Lerp(0.21f, 1f, Smooth(0f, extension, time));
            float u = (x - left - age * travel) / run;
            if (u < 0f || u > 1f) return;
            float shift = y - lane - (x - 0.5f) * tilt;
            float shoulders = Smooth(0f, 0.55f, u) * (1f - Smooth(0.84f, 1f, u));
            float band = thickness * Lerp(0.082f, 1f, shoulders) * (1f - age * 0.44f);
            float face = shift / band;
            if (Math.Abs(face) > 2.45f) return;

            float ignite = Smooth(0f, 0.011f, time);
            float survive = 1f - Smooth(0.29f, 1f, age);
            float hot = ignite * (1f - Smooth(hotLife * 0.34f + extraLight, hotLife + extraLight, time));
            float ends = Smooth(0f, 0.055f, u) * (1f - Smooth(0.94f, 1f, u));
            float fade = ignite * survive * ends * opacity;
            float grain = mask.Sample(0.10f + u * 0.82f, 0.52f - shift / (thickness * 3.3f));
            float tooth = Fraction(u * 22f + Math.Abs(face + 0.17f) * 0.38f - age * 1.9f);
            float grooves = Bell(tooth - 0.45f, 0.20f) * Smooth(0.15f, 0.68f, u) * (0.51f + grain * 0.49f);
            float brokenEdge = face + (grain - 0.47f) * (0.15f + age * 0.48f);
            float cuttingSide = face - hotSide * 0.54f;
            float body = 1f - Smooth(0.72f, 1.08f, Math.Abs(brokenEdge));
            pixel.Blend(0x8B282B, fade * Bell(brokenEdge, 2.40f) * (0.43f + grain * 0.21f));
            pixel.Blend(0x4D2024, fade * body * (0.79f + grain * 0.21f));
            pixel.Blend(0xC94637, fade * body * (0.34f + grain * 0.25f));
            pixel.Blend(0x421B20, fade * grooves * Bell(brokenEdge * 0.74f, 1.1f) * 0.61f);
            pixel.Blend(0xEC5441, fade * Bell(cuttingSide, 0.69f) * (0.33f + grain * 0.40f));
            float lightFade = extraLight > 0f
                ? ignite * (1f - Smooth(0.29f, 1f, (time - extraLight) / life)) * ends * opacity
                : fade;
            pixel.Blend(tint, hot * lightFade * Bell(cuttingSide, 1.47f) * 0.36f);
            pixel.Blend(tint, hot * lightFade * (1f - Smooth(0.19f, 0.58f, Math.Abs(cuttingSide))) * 1.24f);
            pixel.Blend(coreTint, hot * lightFade * Bell(cuttingSide + 0.07f, 0.36f) * (0.76f + grain * 0.56f));
        }

        private void ScatterPierce(byte[] pixels, int width, int height, float time)
        {
            for (int spark = 0; spark < 18; spark++)
            {
                float age = time - (spark % 4) * 0.012f;
                float life = 0.16f + (spark % 5) * 0.022f;
                if (age < 0f || age >= life) continue;
                float path = ((spark * 11 % 19) - 9) / 9f;
                float x = width * (0.27f + (spark * 7 % 19) * 0.026f + age * (0.56f + (spark % 3) * 0.44f));
                float y = height * (0.50f + path * (0.23f + age * 0.55f));
                float fade = Smooth(0f, 0.022f, age) * (1f - Smooth(0.06f, life, age));
                bool ember = spark % 3 == 0;
                int rx = ember ? 10 + (spark % 5) * 3 : 5 + (spark % 5) * 3;
                int ry = ember ? 5 + spark % 4 : 2 + spark % 3;
                SlashMask fragment = ember ? _particles.ImpactDebris : _chip;
                for (int row = Math.Max(0, (int)y - ry); row < Math.Min(height, (int)y + ry + 1); row++)
                {
                    for (int col = Math.Max(0, (int)x - rx); col < Math.Min(width, (int)x + rx + 1); col++)
                    {
                        float u = 0.5f + (col - x) / (2f * rx);
                        float v = 0.5f + (row - y - path * (col - x) * 0.15f) / (2f * ry);
                        float mask = fragment.Sample(u, v);
                        int index = (row * width + col) * 4;
                        Pixel pixel = Pixel.Read(pixels, index);
                        pixel.Blend(ember ? 0xF45C43 : 0xFFF2CD, fade * mask * (ember ? 0.82f : 0.93f));
                        pixel.Write(pixels, index);
                    }
                }
            }
        }

        public byte[] DrawGroundFlame(int width, int height, float time, SlashMask fire, SlashMask smoke)
        {
            var pixels = new byte[width * height * 4];
            float rise = Smooth(0f, 0.57f, time);
            float heat = Smooth(0f, 0.085f, time) * (1f - Smooth(0.27f, 0.55f, time));
            float ash = Smooth(0.045f, 0.20f, time) * (1f - Smooth(0.44f, 0.64f, time));
            float flicker = (float)Math.Sin(time * 46f);
            Stamp(pixels, width, height, smoke, width * (0.54f + flicker * 0.05f),
                height * (0.57f - rise * 0.28f), width * (0.83f + rise * 0.13f), height * 0.51f,
                -0.16f + rise * 0.35f, 0x382C2B, 0.78f * ash);
            Stamp(pixels, width, height, smoke, width * (0.47f - rise * 0.15f),
                height * (0.71f - rise * 0.36f), width * 0.57f, height * 0.38f,
                0.49f, 0x281E25, 0.43f * ash);
            for (int row = (int)(height * 0.24f); row < height; row++)
            {
                float v = (row + 0.5f) / height;
                for (int col = 0; col < width; col++)
                {
                    float u = (col + 0.5f) / width;
                    float noise = _grain.Sample(Fraction(u * 2.1f + time * 0.28f), Fraction(v * 1.4f - time * 1.12f));
                    float lean = (0.17f + flicker * 0.21f) * (0.85f - v) + (noise - 0.5f) * 0.11f;
                    float x = u - 0.50f - lean;
                    float red = fire.Sample(0.5f + x / 0.79f, 0.5f + (v - 0.62f) / 0.67f);
                    float orange = fire.Sample(0.5f + (x + 0.035f) / 0.56f, 0.5f + (v - 0.72f) / 0.51f);
                    float core = fire.Sample(0.5f + x / 0.32f, 0.5f + (v - 0.81f) / 0.34f);
                    int index = (row * width + col) * 4;
                    var pixel = Pixel.Read(pixels, index);
                    pixel.Blend(0xAA301B, heat * red * (0.54f + noise * 0.44f));
                    pixel.Blend(0xEF5223, heat * orange * (0.56f + noise * 0.47f));
                    pixel.Blend(0xFF9C43, heat * core * (0.67f + noise * 0.46f));
                    pixel.Write(pixels, index);
                }
            }
            for (int ember = 0; ember < 11; ember++)
            {
                float age = time - 0.032f - ember * 0.031f;
                float life = 0.16f + (ember % 4) * 0.032f;
                if (age < 0f || age >= life) continue;
                float sign = ((ember * 5 % 11) - 5) / 5f;
                float x = width * (0.49f + sign * (0.16f + age * 0.99f));
                float y = height * (0.88f - age * (1.85f + ember % 3 * 0.51f));
                float opacity = Smooth(0f, 0.025f, age) * (1f - Smooth(life * 0.32f, life, age)) * heat;
                Stamp(pixels, width, height, _chip, x, y, width * 0.089f, width * 0.087f,
                    age * 6f + ember, ember % 3 == 0 ? 0xFFD187 : 0xF65931, opacity);
            }
            return pixels;
        }

        private void PaintOuter(ref Pixel pixel, float x, float y, float time)
        {
            // 淡风先擦过角色身前，褐红外弧各自向两侧展开。
            PaintCrosswind(ref pixel, x - 0.16f, y + 0.04f, time - 0.052f,
                0.80f, -0.60f, 0.42f, -163f, 172f, 1.18f, 0.93f);
            PaintCrosswind(ref pixel, x + 0.11f, y - 0.08f, time - 0.068f,
                0.88f, 0.475f, 0.44f, -176f, 162f, 1.16f, 0.82f);
            OuterArc(ref pixel, x - 0.07f, y + 0.16f, time - 0.046f,
                0.892f, 0.454f, 0.70f, -151f, 113f, 20f, 1.28f, 0.34f, 0.079f, 0.54f, 0x9B3D34, 0.74f, 0.020f);
            OuterArc(ref pixel, x + 0.13f - time * 0.19f, y - 0.02f, time - 0.091f,
                0.974f, -0.225f, 0.82f, -46f, 111f, 94f, 1.46f, 0.36f, 0.112f, 0.67f, 0x934039, 0.79f, 0.017f);
            OuterArc(ref pixel, x + 0.25f + time * 0.14f, y + 0.03f, time - 0.128f,
                0.927f, 0.375f, 0.73f, 115f, 101f, 54f, 1.49f, 0.32f, 0.101f, 0.66f, 0x8B3D34, 0.82f, 0.022f);
        }

        private void PaintCrosswind(ref Pixel pixel, float x, float y, float time,
            float cosine, float sine, float ellipse, float start, float length, float reach, float opacity)
        {
            if (time <= 0f || time >= 0.36f) return;
            float a = cosine * x + sine * y;
            float b = (cosine * y - sine * x) / ellipse;
            float radius = (float)Math.Sqrt(a * a + b * b);
            if (radius < reach - 0.29f || radius > reach + 0.43f) return;
            float turn = (float)(Math.Atan2(b, a) * 180.0 / Math.PI);
            float u = ((turn - start - time * 62f + 720f) % 360f) / length;
            if (u > 1f) return;
            float age = time - u * 0.068f;
            float alpha = Smooth(0f, 0.039f, age) * (1f - Smooth(0.085f, 0.29f, age)) * opacity;
            if (alpha <= 0f) return;

            float tip = Smooth(0.01f, 0.21f, u) * (1f - Smooth(0.64f, 1f, u));
            float distance = radius - reach - u * 0.19f - age * 0.36f;
            float width = 0.027f + tip * Lerp(0.14f, 0.047f, Smooth(0.06f, 0.27f, age));
            float ribbon = (distance + 0.025f * (float)Math.Sin(u * 7.6f)) / width;
            float inner = ribbon + 0.38f + 1.49f * Smooth(0.31f, 0.91f, u);
            float strand = ribbon - 0.15f - 1.05f * Smooth(0.43f, 0.96f, u);
            pixel.Blend(0xE99591, alpha * tip * Bell(ribbon, 1.60f) * 0.44f);
            pixel.Blend(0xFFD9D1, alpha * tip * Bell(ribbon, 0.69f) * 0.85f);
            pixel.Blend(0xFFB1AC, alpha * tip * Bell(inner, 0.80f) * 0.69f);
            pixel.Blend(0xFFE3DD, alpha * tip * Bell(strand, 0.28f) * 0.71f);
        }

        private void SweepDebris(byte[] pixels, float time)
        {
            float spread = Smooth(0.075f, 0.62f, time);
            float alpha = Smooth(0.039f, 0.17f, time) * (1f - Smooth(0.24f, 0.63f, time));
            Stamp(pixels, _size, _size, _particles.SweepFragments, _size * (0.59f + spread * 0.20f),
                _size * (0.46f - spread * 0.13f), _size * Lerp(0.49f, 0.78f, spread),
                _size * Lerp(0.37f, 0.61f, spread), -0.24f + spread * 0.44f, 0xD64039, alpha * 0.35f);
            float wake = Smooth(0.082f, 0.22f, time) * (1f - Smooth(0.27f, 0.68f, time));
            Stamp(pixels, _size, _size, _particles.SweepWake, _size * (0.44f - spread * 0.24f),
                _size * (0.60f + spread * 0.15f), _size * Lerp(0.46f, 0.65f, spread),
                _size * Lerp(0.30f, 0.44f, spread), 0.12f + spread * 0.49f, 0xB63C34, wake * 0.43f);
        }

        private void ScatterOuterDebris(byte[] output, float time)
        {
            float pixelsPerUnit = _size / _span;
            for (int index = 0; index < 9; index++)
            {
                float u = (index + 0.68f + (index % 3) * 0.14f) / 10f;
                float age = time - 0.064f - u * 0.123f - (index % 4) * 0.027f;
                float life = 0.39f + (index % 3) * 0.075f;
                if (age <= 0f || age >= life) continue;
                float angle = (-156f + u * 312f + age * (45f + index % 4 * 24f)) * (float)(Math.PI / 180.0);
                float reach = BladeRadius(u) + 0.27f + index % 3 * 0.16f + age * (1.78f + index % 4 * 0.37f);
                float x = _size * 0.5f + ((float)Math.Cos(angle) * reach + 0.07f) * pixelsPerUnit;
                float y = _size * 0.5f - ((float)Math.Sin(angle) * reach * 0.83f - age * age * 0.72f) * pixelsPerUnit;
                float size = pixelsPerUnit * (0.34f + index % 4 * 0.079f) * (1f - age / life * 0.42f);
                float alpha = Smooth(0f, 0.041f, age) * (1f - Smooth(life * 0.30f, life, age));
                SlashMask part = index % 3 == 0 ? _particles.ImpactDebris : _particles.SweepWake;
                Stamp(output, _size, _size, part, x, y, size, size * (0.73f + index % 2 * 0.19f),
                    angle + index * 0.62f + age * 4.8f, index % 3 == 0 ? 0xAB392F : 0xF25C55, alpha);
            }
        }

        private void PaintWind(ref Pixel pixel, float radius, float u, float time)
        {
            float age = time - u * 0.13f - 0.083f;
            if (age <= 0f || age >= 0.40f) return;
            float arc = Smooth(0.04f, 0.21f, u) * (1f - Smooth(0.78f, 0.98f, u));
            float progress = Smooth(0f, 0.40f, age);
            float path = BladeRadius(u) + 0.20f + progress * 0.31f
                + (float)Math.Sin(u * 9.5f + 1.48f) * 0.10f;
            float band = (radius - path) / Lerp(0.24f, 0.11f, progress);
            if (Math.Abs(band) > 1.1f) return;
            float texture = _particles.Wind.Sample(Fraction(0.10f + u * 0.91f - age * 0.53f), 0.53f - band * 0.47f);
            float alpha = Smooth(0f, 0.046f, age) * (1f - Smooth(0.12f, 0.40f, age)) * arc;
            pixel.Blend(0xE1978F, alpha * texture * 0.47f);
            pixel.Blend(0xFFE0D6, alpha * Bell(band - 0.12f, 0.35f) * (0.19f + texture * 0.33f));
        }

        private void OuterArc(ref Pixel pixel, float x, float y, float time,
            float cos, float sin, float ellipse, float start, float degrees, float curl, float radius,
            float spread, float width, float duration, int color, float opacity, float innerFeather = 0.009f)
        {
            if (time < 0f || time >= duration + 0.11f) return;
            float a = cos * x + sin * y;
            float b = (cos * y - sin * x) / ellipse;
            float distance = (float)Math.Sqrt(a * a + b * b);
            if (distance < radius - 0.30f || distance > radius + spread + 0.34f) return;
            float turn = (float)(Math.Atan2(b, a) * 180.0 / Math.PI);
            float along = (turn - start - time * curl + 720f) % 360f;
            if (along > degrees) return;
            float u = along / degrees;
            float age = time - u * 0.085f;
            if (age < 0f || age >= duration) return;
            float progress = Smooth(0f, duration, age);
            float envelope = Smooth(0f, 0.38f, u) * (1f - Smooth(0.56f, 1f, u));
            float wave = (u - 0.5f) * 0.36f + (float)Math.Sin(u * 8.7f) * 0.056f;
            float reach = radius + wave + spread * (0.12f + 0.88f * progress);
            float taper = width * (0.034f + 0.966f * envelope) * (1f - 0.72f * progress);
            float edge = distance - reach;
            const float feather = 0.009f;
            float alpha = Smooth(0f, 0.049f, age) * (1f - Smooth(duration * 0.30f, duration, age)) * opacity;
            float tip = Smooth(0f, 0.044f, u) * (1f - Smooth(0.962f, 1f, u));
            float blade = Smooth(-taper - innerFeather, -taper + innerFeather, edge) *
                (1f - Smooth(-feather, feather, edge)) * tip;
            if (blade > 0f)
            {
                float grain = _grain.Sample(Fraction(u * 3.8f + progress * 0.51f), Fraction(distance * 3f));
                pixel.Blend(0x531F1F, blade * alpha * (0.84f + grain * 0.16f));
                float inner = Smooth(0.08f, 0.58f, -edge / taper);
                pixel.Blend(color, blade * alpha * inner * (0.62f + grain * 0.37f));
            }
            float filament = Bell(edge + 0.13f + progress * 0.077f + u * 0.055f, 0.018f);
            pixel.Blend(0x71302B, filament * alpha * envelope * 0.74f);
        }

        private void PaintBlade(ref Pixel pixel, float radius, float u, float time, bool companion, bool execution, float extraLightTime, float peakLight)
        {
            float age = time - u * (companion ? 0.116f : 0.142f);
            // 强斩的切镜首帧已见半轮金光，逐段退红仍沿用原刃的时钟。
            float lightAge = peakLight > 0f ? time + 0.047f - u * 0.089f : age;
            float tailLife = companion ? 0.34f : 0.56f;
            if (lightAge < 0f || age >= tailLife + 0.041f) return;
            age = Math.Max(age, 0f);
            float tip = Smooth(0f, 0.098f, u) * (1f - Smooth(0.90f, 1f, u));
            float path = BladeRadius(u) + (companion ? (u - 0.55f) * 0.13f : 0f);
            float reach = radius - path;
            // 强闪沿刀锋逐段传开，亮晕限在刃面附近，随后露回原有齿纹和暗红风。
            float pulse = 0f;
            if (peakLight > 0f)
            {
                pulse = peakLight * tip * Smooth(0f, 0.030f, lightAge) * (1f - Smooth(0.095f, 0.191f, age));
                float halo = pulse * Bell(reach + 0.075f, 0.44f) * Smooth(0.39f, 0.64f, radius);
                pixel.Blend(0xFFBD6E, halo * 0.47f);
                pixel.Blend(0xFFDF86, halo * Bell(reach + 0.035f, 0.25f) * 0.55f);
            }
            if (reach < -0.52f - 0.18f * pulse || reach > 0.52f) return;
            float noise = _grain.Sample(Fraction(u * 2.8f - time * 0.51f),
                Fraction(radius * 2.4f + time * 0.65f));

            // 白金沿外缘扫出，内侧较宽的暖金刃面刻出向后弯折的齿纹。
            float thickness = (0.06f + 0.37f * tip) * (companion ? 0.84f : 1f);
            float tailAge = age - 0.041f;
            float red = Smooth(0f, 0.061f, tailAge) * (1f - Smooth(tailLife * 0.36f, tailLife, tailAge)) * tip;
            if (red > 0f)
            {
                float spread = Smooth(0f, tailLife, tailAge);
                float edge = reach - 0.041f - spread * 0.20f;
                float dissolve = Smooth(0.14f, 0.74f, spread);
                float shear = (float)Math.Sin(u * 14.2f + spread * 3.6f);
                float ragged = (noise - 0.50f) * (0.18f + dissolve * 0.63f);
                float lag = Lerp(1f, 0.70f + 0.27f * shear, dissolve);
                float depth = -edge / (thickness * Lerp(1.12f, 0.23f, spread) * lag);
                float streak = _tail.Sample(0.13f + spread * 0.49f, 0.14f + depth * 0.67f + ragged);
                float fan = Smooth(-0.045f, 0.12f, depth) * (1f - Smooth(0.60f, 1.04f, depth + ragged));
                float breakUp = Lerp(0.81f + streak * 0.19f,
                    Smooth(0.35f, 0.77f, noise + streak * 0.24f + shear * 0.20f), dissolve);
                pixel.Blend(0xF25956, red * Bell(edge + 0.08f, 0.27f) * (1f - spread) * 0.38f);
                pixel.Blend(0xFF5654, red * fan * breakUp * (1.12f + 0.28f * streak));
                pixel.Blend(0xFF9383, red * fan * Bell(depth - 0.21f, 0.55f) * (1f - spread) * 0.70f);
                pixel.Blend(0xA53634, red * fan * BladeNotch(u, depth) * (1f - dissolve) * 0.50f);
                pixel.Blend(0x71302E, red * Bell(edge + 0.007f, 0.052f) * breakUp * (0.23f + spread * 0.81f));
                pixel.Blend(0xF3373A, red * Bell(edge - 0.048f, 0.021f) * (1f - spread) * 0.73f);
            }

            float warmHold = execution ? 0.105f : 0.046f;
            float warmEnd = companion ? 0.149f : 0.162f;
            if (execution) warmEnd += 0.081f;
            float warm = 1f - Smooth(warmHold + extraLightTime, warmEnd + extraLightTime, age);
            float flash = Smooth(0f, 0.019f, lightAge) * warm * tip;
            if (flash > 0f)
            {
                float edge = reach - 0.028f - age * 0.44f;
                float width = thickness * Lerp(0.42f, 1f, Smooth(0f, 0.051f, lightAge));
                if (execution) width *= 1.21f;
                else if (extraLightTime > 0f) width *= 1.12f;
                if (pulse > 0f)
                {
                    edge -= pulse * 0.092f;
                    width *= 1f + 0.57f * pulse;
                }
                float depth = -edge / width;
                float mask = _blade.Sample(0.06f + age * 2.2f, 0.13f + depth * 0.75f + (noise - 0.5f) * 0.10f);
                float notch = BladeNotch(u, depth);
                float face = Smooth(-0.13f, 0.07f, depth) * (1f - Smooth(0.74f, 1.09f, depth));
                float core = Smooth(-0.09f, 0.06f, depth) *
                    (1f - Smooth(execution ? 0.39f : 0.29f, execution ? 0.73f : 0.61f, depth));
                float leadingEdge = Bell(edge - 0.092f - age * 0.38f - u * 0.046f, 0.026f);
                pixel.Blend(0xFF9D59, flash * Bell(edge + 0.035f, 0.49f) * 0.55f);
                if (execution)
                    pixel.Blend(0xFFBE60, flash * Bell(edge + 0.075f, 0.35f) * 0.48f);
                pixel.Blend(0xFF9B55, flash * face * (1.02f + mask * 0.26f));
                pixel.Blend(0xFFCF70, flash * face * (1f - Smooth(0.38f, 0.96f, depth)) * (0.88f + mask * 0.22f));
                pixel.Blend(0xFFF1BD, flash * core * (execution ? 1.58f : 1.35f));
                pixel.Blend(0xFFFFF1, flash * core * core * ((execution ? 1.56f : 1.28f) - notch * 0.54f));
                if (pulse > 0f)
                {
                    pixel.Blend(0xFFF09A, pulse * face * (1f - Smooth(0.66f, 0.97f, depth)) * 0.95f);
                    pixel.Blend(0xFFFFE9, pulse * face * (1f - Smooth(0.45f, 0.77f, depth)) * 1.20f);
                }
                pixel.Blend(0xA95733, flash * face * notch * (0.29f + mask * 0.24f));
                pixel.Blend(0xFFB449, flash * leadingEdge * 0.79f);
                pixel.Blend(0xFFE5A4, flash * Bell(edge + 0.014f, 0.019f) * 0.85f);
            }
        }

        private static float BladeNotch(float u, float depth)
        {
            float bend = depth * 0.9f + 1.72f * Math.Max(0f, depth - 0.59f);
            float tooth = Fraction(u * 53f - bend + (float)Math.Sin(u * 13f) * 0.16f) - 0.50f;
            return (1f - Smooth(0.043f, 0.128f, Math.Abs(tooth))) *
                Smooth(0.28f, 0.49f, depth) * (1f - Smooth(0.88f, 1.08f, depth));
        }

        private static float BladeRadius(float u) => 0.79f + u * 0.33f + (float)Math.Sin(u * 6.283f) * 0.08f;

        private void Scatter(byte[] output, float time)
        {
            const int count = 40;
            float pixelsPerUnit = _size / _span;
            for (int i = 0; i < count; i++)
            {
                float u = (i + 0.24f + (i % 3) * 0.24f) / count;
                bool spark = i % 2 == 0;
                float age = time - (spark ? 0.006f : 0.048f) - u * 0.122f - (i % 5) * 0.009f;
                float life = spark ? 0.126f + (i % 5) * 0.019f : 0.27f + (i % 4) * 0.061f;
                if (age < 0f || age >= life) continue;
                float progress = age / life;
                float angle = ((-151f + u * 292f) + age * (38f + (i % 3) * 34f)) * (float)(Math.PI / 180.0);
                float radius = BladeRadius(u) + 0.13f + (i % 3) * 0.073f
                    + age * (spark ? Lerp(1.65f, 3.60f, u) : Lerp(0.94f, 1.82f, u));
                float x = _size * 0.5f + (0.11f + (float)Math.Cos(angle) * radius) * pixelsPerUnit;
                float y = _size * 0.5f - (float)Math.Sin(angle) * radius * 0.77f * pixelsPerUnit;
                float width = pixelsPerUnit * (0.12f + (i % 5) * 0.051f) * (1f - progress * 0.54f);
                float height = width * (spark ? 0.29f : 0.39f + (i % 3) * 0.13f);
                float rotation = spark ? angle + (i % 5 - 2) * 0.30f : angle + i * 0.51f + age * 5f;
                float opacity = Smooth(0f, spark ? 0.017f : 0.029f, age) * (1f - Smooth(0.32f, 1f, progress));
                int color = spark ? 0xFFDBA6 : i % 3 == 0 ? 0x9F3028 : 0xFA5747;
                Stamp(output, _size, _size, spark ? _particles.Flash : _chip,
                    x, y, width, height, rotation, color, opacity);
                if (spark)
                    Stamp(output, _size, _size, _particles.Flash, x, y, width * 0.72f, height * 0.61f,
                        rotation, 0xFFFFF6, opacity * 1.41f);
            }
        }

        private static void Stamp(byte[] output, int canvasWidth, int canvasHeight, SlashMask mask,
            float x, float y, float width, float height, float angle, int color, float opacity)
        {
            if (opacity < 0.004f) return;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            int rangeX = (int)Math.Ceiling((Math.Abs(cos) * width + Math.Abs(sin) * height) * 0.5f);
            int rangeY = (int)Math.Ceiling((Math.Abs(sin) * width + Math.Abs(cos) * height) * 0.5f);
            int left = Math.Max(0, (int)x - rangeX), right = Math.Min(canvasWidth - 1, (int)x + rangeX);
            int top = Math.Max(0, (int)y - rangeY), bottom = Math.Min(canvasHeight - 1, (int)y + rangeY);
            for (int row = top; row <= bottom; row++)
            {
                for (int col = left; col <= right; col++)
                {
                    float dx = col + 0.5f - x, dy = row + 0.5f - y;
                    float u = 0.5f + (cos * dx + sin * dy) / width;
                    float v = 0.5f + (cos * dy - sin * dx) / height;
                    float alpha = opacity * mask.Sample(u, v);
                    if (alpha < 0.004f) continue;
                    int index = (row * canvasWidth + col) * 4;
                    Pixel pixel = Pixel.Read(output, index);
                    pixel.Blend(color, alpha);
                    pixel.Write(output, index);
                }
            }
        }

        private static float Bell(float x, float width)
        {
            float t = 1f - Math.Min(1f, Math.Abs(x) / width);
            return t * t;
        }

        private static float Smooth(float low, float high, float value)
        {
            float t = Math.Max(0f, Math.Min(1f, (value - low) / (high - low)));
            return t * t * (3f - 2f * t);
        }

        internal static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Fraction(float value) => value - (float)Math.Floor(value);

        private struct Pixel
        {
            // 叠加时使用预乘通道，写 PNG 时还原为直通 Alpha。
            private float _r, _g, _b, _a;

            internal static Pixel Read(byte[] source, int index)
            {
                float a = source[index + 3] / 255f;
                return new Pixel { _a = a, _b = source[index] * a, _g = source[index + 1] * a,
                    _r = source[index + 2] * a };
            }

            internal void Blend(int tint, float alpha)
            {
                alpha = Math.Min(1f, alpha);
                float remain = 1f - alpha;
                _r = _r * remain + (tint >> 16) * alpha;
                _g = _g * remain + ((tint >> 8) & 255) * alpha;
                _b = _b * remain + (tint & 255) * alpha;
                _a = _a * remain + alpha;
            }

            internal void Write(byte[] output, int index)
            {
                int alpha = (int)Math.Round(_a * 255f);
                if (alpha == 0) return;
                output[index] = (byte)Math.Round(_b / _a);
                output[index + 1] = (byte)Math.Round(_g / _a);
                output[index + 2] = (byte)Math.Round(_r / _a);
                output[index + 3] = (byte)alpha;
            }
        }
    }
}
