using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Analysis
{
    public class WaveformData
    {
        public float[] MinPeaks { get; }

        public float[] MaxPeaks { get; }

        public float[] RmsValues { get; }

        public int ColumnCount { get; }

        public WaveformData(int columnCount)
        {
            ColumnCount = columnCount;
            MinPeaks = new float[columnCount];
            MaxPeaks = new float[columnCount];
            RmsValues = new float[columnCount];
        }
    }

    public static class WaveformGenerator
    {
        public static unsafe WaveformData Generate(AudioBuffer buffer, int channel, int columnCount)
        {
            var data = new WaveformData(columnCount);

            int totalFrames = buffer.FrameCount;
            if (totalFrames == 0 || columnCount == 0)
                return data;

            int channels = buffer.Channels;
            float* ptr = buffer.Ptr;
            double framesPerColumn = (double)totalFrames / columnCount;

            for (int col = 0; col < columnCount; col++)
            {
                int startFrame = (int)(col * framesPerColumn);
                int endFrame = (int)((col + 1) * framesPerColumn);
                endFrame = Math.Min(endFrame, totalFrames);

                if (startFrame >= endFrame)
                {
                    data.MinPeaks[col] = 0;
                    data.MaxPeaks[col] = 0;
                    data.RmsValues[col] = 0;
                    continue;
                }

                float min = float.MaxValue;
                float max = float.MinValue;
                double sumSquared = 0;
                int count = 0;

                for (int f = startFrame; f < endFrame; f++)
                {
                    float sample = ptr[f * channels + channel];
                    if (sample < min) min = sample;
                    if (sample > max) max = sample;
                    sumSquared += sample * sample;
                    count++;
                }
                data.MinPeaks[col] = min;
                data.MaxPeaks[col] = max;
                data.RmsValues[col] = count > 0
                    ? (float)Math.Sqrt(sumSquared / count)
                    : 0f;
            }
            return data;
        }
    }
}
