using System;
using UnityEngine;

namespace Fodinae.Scripts.Audio
{
    public static class WavUtility
    {
        public static AudioClip ToAudioClip(byte[] wavBytes, string clipName = "GeneratedWav")
        {
            if (wavBytes == null || wavBytes.Length < 44)
            {
                Debug.LogError("[WavUtility] Invalid WAV data: too short or null");
                return null;
            }

            if (wavBytes[0] != 'R' || wavBytes[1] != 'I' || wavBytes[2] != 'F' || wavBytes[3] != 'F' ||
                wavBytes[8] != 'W' || wavBytes[9] != 'A' || wavBytes[10] != 'V' || wavBytes[11] != 'E')
            {
                Debug.LogError("[WavUtility] Not a valid WAV file (missing RIFF/WAVE headers)");
                return null;
            }

            int channels = 1;
            int sampleRate = 44100;
            int bitsPerSample = 16;
            int dataOffset = 0;
            int dataSize = 0;

            int offset = 12;
            bool foundFmt = false;
            bool foundData = false;

            while (offset < wavBytes.Length - 8)
            {
                var chunkId = System.Text.Encoding.ASCII.GetString(wavBytes, offset, 4);
                int chunkSize = BitConverter.ToInt32(wavBytes, offset + 4);

                if (chunkId == "fmt ")
                {
                    foundFmt = true;
                    int audioFormat = BitConverter.ToInt16(wavBytes, offset + 8);
                    if (audioFormat != 1)
                    {
                        Debug.LogError($"[WavUtility] Unsupported audio format: {audioFormat} (only PCM=1 supported)");
                        return null;
                    }

                    channels = BitConverter.ToInt16(wavBytes, offset + 10);
                    sampleRate = BitConverter.ToInt32(wavBytes, offset + 12);
                    bitsPerSample = BitConverter.ToInt16(wavBytes, offset + 22);
                }
                else if (chunkId == "data")
                {
                    foundData = true;
                    dataOffset = offset + 8;
                    dataSize = chunkSize;
                }

                offset += 8 + chunkSize;
            }

            if (!foundFmt)
            {
                Debug.LogError("[WavUtility] No fmt chunk found in WAV");
                return null;
            }

            if (!foundData)
            {
                Debug.LogError("[WavUtility] No data chunk found in WAV");
                return null;
            }

            int bytesPerSample = bitsPerSample / 8;
            int totalSamples = dataSize / bytesPerSample;
            float[] samples = new float[totalSamples];

            if (bitsPerSample == 16)
            {
                int maxIndex = Math.Min(totalSamples, (wavBytes.Length - dataOffset) / 2);
                for (int i = 0; i < maxIndex; i++)
                {
                    short val = BitConverter.ToInt16(wavBytes, dataOffset + i * 2);
                    samples[i] = val / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                int maxIndex = Math.Min(totalSamples, wavBytes.Length - dataOffset);
                for (int i = 0; i < maxIndex; i++)
                {
                    samples[i] = (wavBytes[dataOffset + i] - 128) / 128f;
                }
            }
            else
            {
                Debug.LogError($"[WavUtility] Unsupported bits per sample: {bitsPerSample} (only 8/16 supported)");
                return null;
            }

            var clip = AudioClip.Create(clipName, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
