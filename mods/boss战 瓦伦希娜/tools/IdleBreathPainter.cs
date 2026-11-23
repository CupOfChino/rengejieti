using System;

namespace ValennaBattle
{
    public static class IdleBreathPainter
    {
        public static byte[] Draw(byte[] pixels, int width, int height, int frame)
        {
            var shift = ValennaPoses.IdleBreathShift(frame);
            var output = (byte[])pixels.Clone();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float weight = ValennaPoses.IdleBreathWeight(x, y);
                    if (weight <= 0f) continue;
                    float sourceX = x - shift.X * weight;
                    float sourceY = y - shift.Y * weight;
                    // 逆向采样两次修正，使图像和根据同一变形计算的锚点对齐。
                    for (int estimate = 0; estimate < 2; estimate++)
                    {
                        weight = ValennaPoses.IdleBreathWeight(sourceX, sourceY);
                        sourceX = x - shift.X * weight;
                        sourceY = y - shift.Y * weight;
                    }
                    Sample(pixels, width, height, sourceX, sourceY, output, (y * width + x) * 4);
                }
            }
            return output;
        }

        private static void Sample(byte[] pixels, int width, int height, float x, float y, byte[] output, int index)
        {
            int left = (int)Math.Floor(x);
            int top = (int)Math.Floor(y);
            float horizontal = x - left;
            float vertical = y - top;
            // 按预乘 Alpha 混合，保住帽沿、发梢等半透明边缘的色彩。
            var sample = new Pixel();
            sample.Add(pixels, width, height, left, top, (1f - horizontal) * (1f - vertical));
            sample.Add(pixels, width, height, left + 1, top, horizontal * (1f - vertical));
            sample.Add(pixels, width, height, left, top + 1, (1f - horizontal) * vertical);
            sample.Add(pixels, width, height, left + 1, top + 1, horizontal * vertical);
            sample.Write(output, index);
        }

        private struct Pixel
        {
            private float _blue, _green, _red, _alpha;

            internal void Add(byte[] pixels, int width, int height, int x, int y, float weight)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                int index = (y * width + x) * 4;
                float alpha = pixels[index + 3] * weight;
                _blue += pixels[index] * alpha;
                _green += pixels[index + 1] * alpha;
                _red += pixels[index + 2] * alpha;
                _alpha += alpha;
            }

            internal void Write(byte[] pixels, int index)
            {
                byte alpha = (byte)Math.Round(_alpha);
                pixels[index] = alpha == 0 ? (byte)0 : (byte)Math.Round(_blue / _alpha);
                pixels[index + 1] = alpha == 0 ? (byte)0 : (byte)Math.Round(_green / _alpha);
                pixels[index + 2] = alpha == 0 ? (byte)0 : (byte)Math.Round(_red / _alpha);
                pixels[index + 3] = alpha;
            }
        }
    }
}
