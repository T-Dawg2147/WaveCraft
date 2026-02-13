using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaveCraft.Core.Audio;

namespace WaveCraft.Core.Analysis
{
    /// <summary>
    /// FFT-based frequency spectrum analyser.
    ///
    /// PROFESSIONAL CONCEPT: The Fast Fourier Transform converts audio
    /// from the time domain (amplitude vs time) to the frequency domain
    /// (amplitude vs frequency). This is how spectrum analysers, EQ displays,
    /// and tuners work. This implementation uses the Cooley-Tukey radix-2
    /// algorithm — one of the most important algorithms in computing.
    /// </summary>
    public static class SpectrumAnalyzer
    {
        public static unsafe float[] ComputeSpectrum(AudioBuffer buffer, int channel, int fftSize = 1024)
        {
            if ((fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a power of 2.");

            int channels = buffer.Channels;
            int frames = Math.Min(buffer.FrameCount, fftSize);

            double[] real = new double[fftSize];
            double[] imag = new double[fftSize];

            float* ptr = buffer.Ptr;
            for (int i = 0; i < frames; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (frames - 1)));

                real[i] = ptr[i * channels + channel] * window;
                imag[i] = 0;
            }
            FFT(real, imag, fftSize);

            int outputSize = fftSize / 2;
            float[] magnitudes = new float[outputSize];

            for (int i = 0; i < outputSize; i++)
            {
                double magnitude = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);

                magnitudes[i] = magnitude > 0.000001
                    ? (float)(20.0 * Math.Log10(magnitude))
                    : -100f;
            }
            return magnitudes;
        }

        private static void FFT(double[] real, double[] imag, int n)
        {
            // Bit-reversal permutation
            int bits = (int)Math.Log2(n);
            for (int i = 0; i < n; i++)
            {
                int j = BitReverse(i, bits);
                if (j > i)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
            }

            // Butterfly operations
            for (int size = 2; size <= n; size *= 2)
            {
                int halfSize = size / 2;
                double angle = -2.0 * Math.PI / size;

                double wReal = Math.Cos(angle);
                double wImag = Math.Sin(angle);

                for (int start = 0; start < n; start += size)
                {
                    double curReal = 1.0, curImag = 0.0;

                    for (int k = 0; k < halfSize; k++)
                    {
                        int even = start + k;
                        int odd = start + k + halfSize;

                        // Complex multiplication: twiddle × odd
                        double tReal = curReal * real[odd] - curImag * imag[odd];
                        double tImag = curReal * imag[odd] + curImag * real[odd];

                        // Butterfly
                        real[odd] = real[even] - tReal;
                        imag[odd] = imag[even] - tImag;
                        real[even] += tReal;
                        imag[even] += tImag;

                        // Rotate twiddle factor
                        double newReal = curReal * wReal - curImag * wImag;
                        curImag = curReal * wImag + curImag * wReal;
                        curReal = newReal;
                    }
                }
            }
        }

        private static int BitReverse(int value, int bits)
        {
            int result = 0;
            for (int i = 0; i < bits; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            return result;
        }
    }
}
