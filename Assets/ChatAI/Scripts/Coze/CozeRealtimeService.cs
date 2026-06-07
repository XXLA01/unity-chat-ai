using UnityEngine;
using System;
using System.IO;
using System.Text;

namespace ChatAI.Coze
{
    /// <summary>
    /// 音频工具类：Unity 麦克风录音 → WAV 格式转换
    /// 用于将录音数据转换为 Coze 文件上传 API 所需的 WAV 格式
    /// </summary>
    public static class CozeAudioHelper
    {
        /// <summary>
        /// 将 Unity AudioClip 的 float[] 采样数据转换为 WAV 格式字节数组
        /// </summary>
        /// <param name="samples">float[] PCM 采样数据 (-1.0 ~ 1.0)</param>
        /// <param name="sampleRate">采样率 (Hz)</param>
        /// <param name="channels">声道数</param>
        /// <returns>WAV 格式字节数组</returns>
        public static byte[] FloatSamplesToWav(float[] samples, int sampleRate, int channels = 1)
        {
            if (samples == null || samples.Length == 0) return null;

            int pcmLength = samples.Length * 2; // 16-bit = 2 bytes per sample
            int wavLength = 44 + pcmLength;     // 44-byte header + PCM data
            byte[] wav = new byte[wavLength];

            using (var ms = new MemoryStream(wav))
            using (var bw = new BinaryWriter(ms))
            {
                int byteRate = sampleRate * channels * 2;
                short blockAlign = (short)(channels * 2);

                // RIFF header
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(wavLength - 8);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt sub-chunk
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);               // Sub-chunk size
                bw.Write((short)1);         // PCM format
                bw.Write((short)channels);  // Channels
                bw.Write(sampleRate);       // Sample rate
                bw.Write(byteRate);         // Byte rate
                bw.Write(blockAlign);       // Block align
                bw.Write((short)16);        // Bits per sample

                // data sub-chunk
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(pcmLength);

                // PCM data (16-bit little-endian)
                for (int i = 0; i < samples.Length; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    short sample = (short)(clamped * short.MaxValue);
                    bw.Write(sample);
                }
            }

            return wav;
        }

        /// <summary>
        /// 从 Microphone 录制的 AudioClip 中提取数据并转换为 WAV
        /// 自动处理实际采样率与期望采样率的差异
        /// </summary>
        /// <param name="clip">录制的 AudioClip</param>
        /// <param name="position">录音位置（Microphone.GetPosition 返回值）</param>
        /// <param name="targetSampleRate">目标采样率 (默认 16000)</param>
        /// <returns>WAV 格式字节数组</returns>
        public static byte[] AudioClipToWav(AudioClip clip, int position, int targetSampleRate = 16000)
        {
            if (clip == null || position <= 0) return null;

            int channels = clip.channels;
            int actualSampleRate = clip.frequency;
            int totalSamples = position * channels;

            float[] rawSamples = new float[totalSamples];
            clip.GetData(rawSamples, 0);

            // 如果实际采样率与目标不同，做简单重采样
            float[] samples;
            if (actualSampleRate != targetSampleRate)
            {
                samples = Resample(rawSamples, actualSampleRate, targetSampleRate, channels);
                Debug.Log($"[AudioHelper] 重采样: {actualSampleRate}Hz → {targetSampleRate}Hz " +
                          $"({rawSamples.Length} → {samples.Length} samples)");
            }
            else
            {
                samples = rawSamples;
            }

            return FloatSamplesToWav(samples, targetSampleRate, channels);
        }

        /// <summary>
        /// 简单线性插值重采样
        /// </summary>
        private static float[] Resample(float[] input, int srcRate, int dstRate, int channels)
        {
            if (srcRate == dstRate) return input;

            float ratio = (float)srcRate / dstRate;
            int srcFrames = input.Length / channels;
            int dstFrames = Mathf.CeilToInt(srcFrames / ratio);
            float[] output = new float[dstFrames * channels];

            for (int i = 0; i < dstFrames; i++)
            {
                float srcPos = i * ratio;
                int srcIdx = Mathf.FloorToInt(srcPos);
                float frac = srcPos - srcIdx;

                for (int ch = 0; ch < channels; ch++)
                {
                    int s0 = srcIdx * channels + ch;
                    int s1 = Math.Min(srcIdx + 1, srcFrames - 1) * channels + ch;

                    if (s0 < input.Length && s1 < input.Length)
                        output[i * channels + ch] = Mathf.Lerp(input[s0], input[s1], frac);
                }
            }

            return output;
        }

        /// <summary>
        /// 获取 WAV 数据的时长（秒）
        /// </summary>
        public static float GetWavDuration(byte[] wavData, int sampleRate = 16000, int channels = 1)
        {
            if (wavData == null || wavData.Length <= 44) return 0;
            int pcmBytes = wavData.Length - 44;
            int samples = pcmBytes / 2; // 16-bit
            return (float)samples / (sampleRate * channels);
        }
    }
}
