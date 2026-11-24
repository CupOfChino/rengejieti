using System;
using System.Threading;

namespace ValennaBattle
{
    // 波形相似度叠加。所有声道共用同一个匹配位置，以保持立体声的相位关系。
    internal static class VoiceTempo
    {
        internal static float[] Change(float[] input, int channels, int rate, float speed, CancellationToken cancellation)
        {
            int sourceFrames = input.Length / channels;
            int outputFrames = Math.Max(1, (int)Math.Round(sourceFrames / (double)speed));
            int window = Math.Min(sourceFrames, Math.Min(Math.Max(1, outputFrames / 2), Math.Max(2, rate * 30 / 1000)));
            var output = new float[checked(outputFrames * channels)];
            if (window == 0) return output;
            Array.Copy(input, output, window * channels);
            if (outputFrames == window) return output;

            int hop = Math.Max(1, window / 2);
            int search = Math.Max(1, rate * 8 / 1000);
            int stride = Math.Max(1, rate / 12000);
            int sourceEnd = sourceFrames - window;
            int outputEnd = outputFrames - window;
            int written = window;
            for (int destination = Math.Min(hop, outputEnd); ; destination = Math.Min(destination + hop, outputEnd))
            {
                cancellation.ThrowIfCancellationRequested();
                int expected = (int)Math.Round(destination * (double)sourceEnd / outputEnd);
                int overlap = written - destination;
                int source = expected;
                if (destination != outputEnd)
                    source = Match(input, output, channels, destination, overlap, expected, sourceEnd, search, stride);
                for (int i = 0; i < overlap; i++)
                {
                    float mix = (i + 1f) / (overlap + 1f);
                    int from = (source + i) * channels, to = (destination + i) * channels;
                    for (int channel = 0; channel < channels; channel++)
                        output[to + channel] += mix * (input[from + channel] - output[to + channel]);
                }
                Array.Copy(input, (source + overlap) * channels, output, (destination + overlap) * channels,
                    (window - overlap) * channels);
                written = destination + window;
                if (destination == outputEnd) return output;
            }
        }

        private static int Match(float[] input, float[] output, int channels, int destination, int overlap,
            int expected, int sourceEnd, int radius, int stride)
        {
            int first = Math.Max(0, expected - radius), last = Math.Min(sourceEnd, expected + radius);
            int best = expected;
            double bestScore = Score(expected);
            for (int candidate = first; candidate <= last; candidate += stride)
                Consider(candidate);
            int coarse = best;
            for (int candidate = Math.Max(first, coarse - stride + 1); candidate <= Math.Min(last, coarse + stride - 1); candidate++)
                Consider(candidate);
            return best;

            void Consider(int candidate)
            {
                double score = Score(candidate);
                if (score <= bestScore) return;
                best = candidate;
                bestScore = score;
            }

            double Score(int candidate)
            {
                double cross = 0, referencePower = 0, candidatePower = 0;
                for (int frame = 0; frame < overlap; frame += stride)
                {
                    int from = (candidate + frame) * channels, to = (destination + frame) * channels;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float a = input[from + channel], b = output[to + channel];
                        cross += a * b;
                        referencePower += b * b;
                        candidatePower += a * a;
                    }
                }
                double power = Math.Sqrt(referencePower * candidatePower);
                double offset = (candidate - expected) / (double)radius;
                return (power > 1e-12 ? cross / power : 0) - 0.03 * offset * offset;
            }
        }
    }
}
