using System.Collections;
using UnityEngine;

/// <summary>
/// Boots a synced play session: load DrumOnly MIDI chart, play matching audio into
/// <see cref="BeatClock"/>, keep the metronome on, and drive <see cref="ChartTargetSpawner"/>.
/// </summary>
[DefaultExecutionOrder(-50)]
public class RhythmSession : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Optional. Empty = Resources/Audio/<MIDI name> (or <name>Audio for <name>Midi files); if none, a silent track the length of the chart.")]
    public AudioClip musicClip;
    public bool muteMusicForDemo;
    [Range(0f, 1f)] public float musicVolume = 0.8f;
    public bool loopMusic;
    [Tooltip("Shift the whole chart later (+) or earlier (-) to line up with the audible track.")]
    public float chartOffsetSeconds = 0f;
    [Tooltip("DSP schedule delay so Play and the first beat share one clock edge.")]
    //public float scheduleDelay = 0.05f;
    public float scheduleDelay = 0.0f;
    [Tooltip("Quarter-note clicks before the song and chart begin.")]
    public int countInBeats = 0;
    [Tooltip("Tempo for the count-in and the song clock. DrumOnly is 80.")]
    public float bpm = 80f;

    [Header("MIDI")]
    [Tooltip("Optional click on each chart note. The shrinking ring is the timing cue.")]
    public bool metronomeOn;
    public MetronomeMode metronomeMode = MetronomeMode.ChartNotes;

    [Header("Wiring")]
    public BeatClock beatClock;
    public ChartTargetSpawner spawner;
    public MetronomeClick metronome;

    AudioSource musicSource;

    void Awake()
    {
        if (beatClock == null) beatClock = BeatClock.Instance != null ? BeatClock.Instance : FindFirstObjectByType<BeatClock>();
        if (spawner == null) spawner = GetComponent<ChartTargetSpawner>() ?? gameObject.AddComponent<ChartTargetSpawner>();
        if (metronome == null) metronome = GetComponent<MetronomeClick>() ?? gameObject.AddComponent<MetronomeClick>();

        musicSource = GetComponent<AudioSource>();
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = loopMusic;
        musicSource.spatialBlend = 0f;
    }

    void Start()
    {
        if (beatClock == null)
        {
            Debug.LogError("RhythmSession: no BeatClock in scene.", this);
            return;
        }

        string midiFileName = spawner.midiFileName;
        spawner.loadOnStart = false;
        spawner.chartOffsetSeconds = chartOffsetSeconds;

        float chartEnd = 0f;
        beatClock.bpm = bpm;
        if (MidiChartReader.TryLoadStreamingChart(midiFileName, out var notes, out float fileBpm))
        {
            if (fileBpm > 1f) bpm = fileBpm;
            beatClock.bpm = bpm;
            spawner.SetNotes(notes);
            metronome.SetChartNotes(spawner.Notes);
            if (notes.Count > 0)
                chartEnd = notes[notes.Count - 1].time + chartOffsetSeconds + 2f;
        }
        beatClock.songLength = chartEnd;

        if (musicClip == null)
            musicClip = FindSongAudio(midiFileName);
        if (musicClip == null && chartEnd > 0f)
        {
            Debug.Log($"RhythmSession: no audio for {midiFileName}; using a silent track.", this);
            musicClip = CreateSilentClip(chartEnd);
        }

        if (musicClip == null)
            Debug.LogWarning("RhythmSession: no music clip — BeatClock will use scene time.", this);
        else
        {
            musicSource.clip = musicClip;
            musicSource.volume = muteMusicForDemo ? 0f : musicVolume;
            beatClock.music = musicSource;
            beatClock.holdUntilMusicPlays = true;
        }

        metronome.mode = metronomeMode;
        metronome.playOnStart = metronomeOn;
        metronome.enabled = metronomeOn || countInBeats > 0;

        StartCoroutine(CountInThenPlay());
    }

    static AudioClip FindSongAudio(string midiFileName)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(midiFileName);
        if (string.IsNullOrEmpty(name)) return null;
        AudioClip clip = Resources.Load<AudioClip>("Audio/" + name);
        if (clip == null && name.EndsWith("Midi"))
            clip = Resources.Load<AudioClip>("Audio/" + name.Substring(0, name.Length - 4) + "Audio");
        return clip;
    }

    static AudioClip CreateSilentClip(float seconds)
    {
        const int rate = 44100;
        int samples = Mathf.Max(1, Mathf.CeilToInt(seconds * rate));
        return AudioClip.Create("Silence", samples, 1, rate, true, data => System.Array.Clear(data, 0, data.Length));
    }

    IEnumerator CountInThenPlay()
    {
        float quarter = beatClock.SecondsPerBeat;
        int beats = Mathf.Max(0, countInBeats);
        if (scheduleDelay > 0f)
            yield return new WaitForSeconds(scheduleDelay);

        for (int i = 0; i < beats; i++)
        {
            metronome.PlayCountInClick();
            double next = AudioSettings.dspTime + quarter;
            while (AudioSettings.dspTime < next)
                yield return null;
        }

        if (musicSource.clip != null)
        {
            beatClock.ResetBeatTracking();
            metronome.RewindNotes();
            musicSource.Play();
        }
    }
}
