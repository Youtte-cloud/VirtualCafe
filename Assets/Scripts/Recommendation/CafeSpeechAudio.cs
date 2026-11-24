using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VirtualCafe.Recommendation
{
    // Azure short-audio input: mono PCM16 WAV at 16 kHz. Resample devices that ignore the requested rate.
    public static class CafeSpeechAudio
    {
        public static byte[] EncodeWav(float[] input, int frames, int channels, int frequency)
        {
            if (channels < 1 || frequency < 1 || frames < 1 || frames > input.Length / channels)
                throw new ArgumentException("Invalid microphone samples.");
            int count = (int)((long)frames * 16000 / frequency);
            if (count < 1) throw new ArgumentException("Recording is empty.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    double position = (double)i * frequency / 16000;
                    int a = Math.Min((int)position, frames - 1), b = Math.Min(a + 1, frames - 1);
                    float value = 0;
                    for (int c = 0; c < channels; c++)
                        value += Mathf.Lerp(input[a * channels + c], input[b * channels + c], (float)(position - a));
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(value / channels, -1, 1) * 32767));
                }
                return stream.ToArray();
            }
        }
        public static float[] DecodePcm16(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0) throw new InvalidDataException("Invalid PCM audio.");
            var samples = new float[bytes.Length / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)(bytes[i * 2] | bytes[i * 2 + 1] << 8) / 32768f;
            return samples;
        }
    }
}
