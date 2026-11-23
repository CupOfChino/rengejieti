using System;

namespace ValennaBattle
{
    public static class HuntingMarkArt
    {
        public const int Size = 352;
        public const int FrameWidth = 480;
        public const int FrameHeight = 104;

        public static byte[] Extract(byte[] comparison, int stride, int height)
        {
            if (height < 320 || stride < 640 * 4)
                throw new ArgumentException("狩猎目标对比图的尺寸有误。");

            var swords = new byte[Size * Size * 4];
            for (int y = 0; y < 320; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    int source = y * stride + x * 4;
                    int target = ((y + 16) * Size + x + 16) * 4;
                    int difference = Math.Max(Math.Abs(comparison[source] - comparison[0]),
                        Math.Max(Math.Abs(comparison[source + 1] - comparison[1]),
                            Math.Abs(comparison[source + 2] - comparison[2])));
                    if (difference <= 2) continue;

                    swords[target] = comparison[source];
                    swords[target + 1] = comparison[source + 1];
                    swords[target + 2] = comparison[source + 2];
                    swords[target + 3] = (byte)Math.Min(255, (difference - 2) * 23);
                }
            }
            return swords;
        }

        public static byte[] Glow(byte[] swords)
        {
            var alpha = new float[Size * Size];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = swords[i * 4 + 3] / 255f;
            float[] edge = Blur(Blur(alpha, 3), 3);
            float[] halo = Blur(Blur(alpha, 9), 9);
            var glow = new byte[swords.Length];
            for (int i = 0; i < alpha.Length; i++)
            {
                float hot = Math.Min(1f, edge[i] * 2.1f);
                float opacity = Math.Min(1f, hot * 0.72f + halo[i] * 1.5f);
                glow[i * 4] = (byte)(19f + hot * 35f);
                glow[i * 4 + 1] = (byte)(11f + hot * 45f);
                glow[i * 4 + 2] = (byte)(173f + hot * 82f);
                glow[i * 4 + 3] = (byte)(255f * opacity);
            }
            return glow;
        }

        private static float[] Blur(float[] alpha, int radius)
        {
            var across = new float[alpha.Length];
            var result = new float[alpha.Length];
            int window = radius * 2 + 1;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float sum = 0f;
                    for (int k = Math.Max(0, x - radius); k <= Math.Min(Size - 1, x + radius); k++)
                        sum += alpha[y * Size + k];
                    across[y * Size + x] = sum / window;
                }
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    float sum = 0f;
                    for (int k = Math.Max(0, y - radius); k <= Math.Min(Size - 1, y + radius); k++)
                        sum += across[k * Size + x];
                    result[y * Size + x] = sum / window;
                }
            return result;
        }

        public static byte[] Frame()
        {
            var pixels = new byte[FrameWidth * FrameHeight * 4];
            for (int y = 0; y < FrameHeight; y++)
                for (int x = 0; x < FrameWidth; x++)
                {
                    int side = Math.Min(x, FrameWidth - 1 - x);
                    int top = Math.Min(y, FrameHeight - 1 - y);
                    if (side + top < 14) continue;
                    int line = Math.Min(Math.Min(side, top), side + top - 14);
                    int corner = Math.Min(side, top * 3);
                    int i = (y * FrameWidth + x) * 4;
                    if (line <= 2) Color(pixels, i, 94, 22, 30, 245);
                    else if (line <= 4) Color(pixels, i, 247, 179, 85, (byte)(corner < 40 ? 240 : 185));
                    else if (line == 5) Color(pixels, i, 166, 61, 39, 208);
                    else if (line == 9 && (side < 54 || top < 10)) Color(pixels, i, 151, 87, 53, 154);
                    else if (side < 29 && Math.Abs(y - FrameHeight / 2) * 2 + Math.Abs(side - 16) < 9)
                        Color(pixels, i, 249, 166, 97, 205);
                    else if (line < 14) Color(pixels, i, 108, 15, 26, (byte)(62 - line * 4));
                }
            return pixels;
        }

        private static void Color(byte[] pixels, int i, byte red, byte green, byte blue, byte alpha)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = alpha;
        }
    }
}
