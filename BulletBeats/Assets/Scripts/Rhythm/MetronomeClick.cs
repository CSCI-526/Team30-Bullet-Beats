using System.Collections.Generic;
using UnityEngine;

public enum MetronomeMode
{
    /// <summary>Click on every quarter-note beat from <see cref="BeatClock"/>.</summary>
    Beats,
    /// <summary>Click when each chart note reaches its hit time (matches asteroids).</summary>
    ChartNotes,
    /// <summary>Quiet beat grid + louder chart-note accents.</summary>
    Both
}

/// <summary>
/// Metronome audio. For the DrumOnly chart, prefer <see cref="MetronomeMode.ChartNotes"/> or
/// <see cref="MetronomeMode.Both"/> — many MIDI hits sit on 8ths, so a quarter-only click
/// will look "off" next to asteroids even when the song clock is correct.
/// </summary>
public class MetronomeClick : MonoBehaviour
{
    public bool playOnStart = true;
    public MetronomeMode mode = MetronomeMode.Both;
    [Range(0f, 1f)] public float beatVolume = 0.18f;
    [Range(0f, 1f)] public float noteVolume = 0.45f;
    public float beatFrequency = 880f;
    public float noteFrequency = 1320f;
    public float clickSeconds = 0.03f;

    AudioSource source;
    AudioClip beatClick;
    AudioClip noteClick;
    BeatClock clock;
    bool subscribed;
    readonly List<float> noteTimes = new List<float>();
    int nextNote;

    void Awake()
    {
        var go = new GameObject("MetronomeAudio");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        beatClick = BuildClick(beatFrequency);
        noteClick = BuildClick(noteFrequency);
        enabled = playOnStart;
    }

    void OnEnable() => TrySubscribe();

    void OnDisable()
    {
        if (clock != null && subscribed)
        {
            clock.Beat -= OnBeat;
            subscribed = false;
        }
    }

    void Update()
    {
        if (!subscribed) TrySubscribe();
        if (!isActiveAndEnabled || clock == null) return;
        if (mode == MetronomeMode.Beats) return;

        float songTime = clock.SongTime;
        // Catch notes that crossed the hit time this frame (small look-back for frame gaps).
        while (nextNote < noteTimes.Count && noteTimes[nextNote] <= songTime + 0.0005f)
        {
            if (noteTimes[nextNote] > songTime - 0.08f)
                source.PlayOneShot(noteClick, noteVolume);
            nextNote++;
        }
    }

    public void SetChartNotes(IReadOnlyList<ChartNote> notes)
    {
        noteTimes.Clear();
        if (notes != null)
        {
            for (int i = 0; i < notes.Count; i++)
                noteTimes.Add(notes[i].time);
        }
        nextNote = 0;
    }

    public void RewindNotes()
    {
        nextNote = 0;
    }

    /// <summary>One quarter-note click for the pre-song count-in. Works while chart clicks are off.</summary>
    public void PlayCountInClick()
    {
        if (source == null || beatClick == null) return;
        source.PlayOneShot(beatClick, 0.6f);
    }

    void TrySubscribe()
    {
        if (subscribed) return;
        clock = BeatClock.Instance;
        if (clock == null) return;
        clock.Beat += OnBeat;
        subscribed = true;
    }

    void OnBeat(int beat)
    {
        if (!isActiveAndEnabled) return;
        if (mode == MetronomeMode.ChartNotes) return;
        source.PlayOneShot(beatClick, beatVolume);
    }

    AudioClip BuildClick(float frequency)
    {
        int sampleRate = 44100;
        int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * clickSeconds));
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float env = 1f - i / (float)samples;
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * env;
        }
        var clip = AudioClip.Create($"MetronomeClick_{frequency:0}", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
