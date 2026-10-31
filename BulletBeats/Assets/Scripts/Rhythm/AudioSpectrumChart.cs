using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds chart notes from an audio clip. Four frequency bands become the four screen quadrants.
/// Call this when a clip is dragged onto <see cref="RhythmSession"/>; there is no MIDI file.
/// </summary>
public static class AudioSpectrumChart
{
    const int FftSize = 1024;
    const int Hop = 512;
    const float MinGapSeconds = 0.22f;

    struct Band
    {
        public float minHz;
        public float maxHz;
        public int pitch;
        public NoteLane lane;
    }

    static readonly Band[] Bands =
    {
        new Band { minHz = 40f, maxHz = 150f, pitch = 36, lane = NoteLane.LeftLower },
        new Band { minHz = 150f, maxHz = 400f, pitch = 43, lane = NoteLane.RightLower },
        new Band { minHz = 400f, maxHz = 1600f, pitch = 55, lane = NoteLane.LeftUpper },
        new Band { minHz = 1600f, maxHz = 5000f, pitch = 67, lane = NoteLane.RightUpper },
    };

    public static List<ChartNote> Build(AudioClip clip, out float bpm)
    {
        bpm = 120f;
        var notes = new List<ChartNote>();
        if (clip == null || clip.samples < FftSize || clip.frequency < 1000)
            return notes;

        int channels = Mathf.Max(1, clip.channels);
        int step = Mathf.Max(1, clip.frequency / 11025);
        float rate = clip.frequency / (float)step;
        float[] mono = Downmix(clip, channels, step);
        if (mono.Length < FftSize) return notes;

        int frames = 1 + (mono.Length - FftSize) / Hop;
        var flux = new float[Bands.Length][];
        for (int b = 0; b < Bands.Length; b++)
            flux[b] = new float[frames];

        var re = new float[FftSize];
        var im = new float[FftSize];
        var prev = new float[Bands.Length];
        for (int frame = 0; frame < frames; frame++)
        {
            int offset = frame * Hop;
            for (int i = 0; i < FftSize; i++)
            {
                float window = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (FftSize - 1)));
                re[i] = mono[offset + i] * window;
                im[i] = 0f;
            }
            Fft(re, im);
            for (int b = 0; b < Bands.Length; b++)
            {
                float energy = BandEnergy(re, im, rate, Bands[b].minHz, Bands[b].maxHz);
                flux[b][frame] = Mathf.Max(0f, energy - prev[b]);
                prev[b] = energy;
            }
        }

        float frameSeconds = Hop / rate;
        for (int b = 0; b < Bands.Length; b++)
        {
            float threshold = Threshold(flux[b]);
            float last = -MinGapSeconds;
            for (int frame = 1; frame < frames - 1; frame++)
            {
                float value = flux[b][frame];
                if (value < threshold) continue;
                if (value < flux[b][frame - 1] || value < flux[b][frame + 1]) continue;
                float time = frame * frameSeconds;
                if (time - last < MinGapSeconds) continue;
                last = time;
                notes.Add(new ChartNote(time, Bands[b].pitch, Bands[b].lane));
            }
        }

        notes.Sort((a, c) => a.time.CompareTo(c.time));
        bpm = EstimateBpm(flux[0], frameSeconds);
        return notes;
    }

    static float[] Downmix(AudioClip clip, int channels, int step)
    {
        int frames = clip.samples;
        var mono = new List<float>(frames / step);
        const int chunkFrames = 48000;
        var chunk = new float[chunkFrames * channels];
        for (int offset = 0; offset < frames; offset += chunkFrames)
        {
            int count = Mathf.Min(chunkFrames, frames - offset);
            if (chunk.Length != count * channels)
                chunk = new float[count * channels];
            if (!clip.GetData(chunk, offset))
            {
                Debug.LogWarning($"AudioSpectrumChart: {clip.name} has no loaded samples at {offset}. Chart notes will be missing.");
                return mono.ToArray();
            }
            for (int frame = 0; frame < count; frame += step)
            {
                float sample = 0f;
                for (int c = 0; c < channels; c++)
                    sample += chunk[frame * channels + c];
                mono.Add(sample / channels);
            }
        }
        return mono.ToArray();
    }

    static float BandEnergy(float[] re, float[] im, float rate, float minHz, float maxHz)
    {
        int minBin = Mathf.Clamp(Mathf.CeilToInt(minHz * FftSize / rate), 1, FftSize / 2 - 1);
        int maxBin = Mathf.Clamp(Mathf.FloorToInt(maxHz * FftSize / rate), minBin, FftSize / 2 - 1);
        float energy = 0f;
        for (int bin = minBin; bin <= maxBin; bin++)
            energy += Mathf.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);
        return energy;
    }

    static float Threshold(float[] flux)
    {
        double sum = 0;
        for (int i = 0; i < flux.Length; i++) sum += flux[i];
        float mean = (float)(sum / Mathf.Max(1, flux.Length));
        double var = 0;
        for (int i = 0; i < flux.Length; i++)
        {
            float d = flux[i] - mean;
            var += d * d;
        }
        float deviation = Mathf.Sqrt((float)(var / Mathf.Max(1, flux.Length)));
        return mean + deviation * 1.6f;
    }

    static float EstimateBpm(float[] bassFlux, float frameSeconds)
    {
        float best = 0f;
        float bpm = 120f;
        int minLag = Mathf.Max(1, Mathf.RoundToInt(60f / 170f / frameSeconds));
        int maxLag = Mathf.Min(bassFlux.Length / 2, Mathf.RoundToInt(60f / 80f / frameSeconds));
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float score = 0f;
            for (int i = 0; i + lag < bassFlux.Length; i++)
                score += bassFlux[i] * bassFlux[i + lag];
            if (score > best)
            {
                best = score;
                bpm = 60f / (lag * frameSeconds);
            }
        }
        return Mathf.Clamp(bpm, 80f, 170f);
    }

    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                float tr = re[i]; re[i] = re[j]; re[j] = tr;
                float ti = im[i]; im[i] = im[j]; im[j] = ti;
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            float angle = -2f * Mathf.PI / len;
            float wLenRe = Mathf.Cos(angle);
            float wLenIm = Mathf.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                float wRe = 1f;
                float wIm = 0f;
                int half = len >> 1;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k;
                    int b = a + half;
                    float vRe = re[b] * wRe - im[b] * wIm;
                    float vIm = re[b] * wIm + im[b] * wRe;
                    re[b] = re[a] - vRe;
                    im[b] = im[a] - vIm;
                    re[a] += vRe;
                    im[a] += vIm;
                    float nextRe = wRe * wLenRe - wIm * wLenIm;
                    wIm = wRe * wLenIm + wIm * wLenRe;
                    wRe = nextRe;
                }
            }
        }
    }
}
