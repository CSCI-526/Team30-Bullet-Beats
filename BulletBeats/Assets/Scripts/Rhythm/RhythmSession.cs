using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("Ignore MIDI and build the chart by Fourier-transforming Music Clip. Drag an EDM wav here and turn this on.")]
    public bool chartFromAudio;

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
        // Before ChartTargetSpawner.Start, so a MIDI file is not loaded and then thrown away.
        if (spawner != null) spawner.loadOnStart = false;
        UseOneListener();
    }

    /// <summary>
    /// Main Camera and player_start_cam both ship with a listener. Two at once make Unity drop the mix.
    /// </summary>
    public static void UseOneListener()
    {
        Camera display = DisplayCamera.Find();
        AudioListener keep = display != null ? display.GetComponent<AudioListener>() : null;
        AudioListener[] all = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (keep == null && all.Length > 0) keep = all[0];
        for (int i = 0; i < all.Length; i++)
            all[i].enabled = all[i] == keep;
    }

    bool begun;

    /// <summary>
    /// Loads <paramref name="level"/>'s clip and chart, then starts the count-in.
    /// A level BPM above 0 is kept. 0 estimates the tempo from the clip.
    /// </summary>
    public void Begin(GameFlow.Level level)
    {
        if (begun) return;
        if (beatClock == null)
        {
            Debug.LogError("RhythmSession: no BeatClock in scene.", this);
            return;
        }
        begun = true;

        bool keepBpm = false;
        bool useMidi = level != null && !string.IsNullOrEmpty(level.midiFileName);
        if (level != null)
        {
            if (useMidi)
            {
                chartFromAudio = false;
                if (spawner != null) spawner.midiFileName = level.midiFileName;
                if (level.music == null) musicClip = null;
            }
            else if (level.id == GameFlow.CustomLevelId)
            {
                chartFromAudio = true;
                musicClip = level.music;
            }
            else if (level.music != null)
            {
                musicClip = level.music;
                chartFromAudio = true;
            }
            if (level.bpm > 1f)
            {
                bpm = level.bpm;
                keepBpm = true;
            }
            beatClock.firstBeatOffset = level.firstBeatOffset;
        }

        if (level != null && level.music == null && !useMidi && level.id != GameFlow.CustomLevelId)
            Debug.LogWarning($"RhythmSession: level '{level.displayName}' has no music clip.", this);

        string midiFileName = spawner != null ? spawner.midiFileName : null;
        if (spawner != null) spawner.chartOffsetSeconds = chartOffsetSeconds;

        if (musicClip == null && !chartFromAudio)
            musicClip = FindSongAudio(midiFileName);

        StartCoroutine(LoadThenPlay(keepBpm, level, midiFileName));
    }

    IEnumerator LoadThenPlay(bool keepBpm, GameFlow.Level level, string midiFileName)
    {
        if (musicClip == null && level != null && level.id == GameFlow.CustomLevelId && !string.IsNullOrEmpty(GameFlow.CustomSongPath))
        {
            AudioClip loaded = null;
            yield return SongFile.Load(GameFlow.CustomSongPath, clip => loaded = clip);
            musicClip = loaded;
            level.music = loaded;
        }

        if (musicClip != null && musicClip.loadState != AudioDataLoadState.Loaded)
        {
            musicClip.LoadAudioData();
            while (musicClip.loadState == AudioDataLoadState.Loading)
                yield return null;
        }

        float chartEnd = 0f;
        beatClock.bpm = bpm;
        List<ChartNote> notes = null;
        if (chartFromAudio)
        {
            if (musicClip == null)
                Debug.LogError("RhythmSession: Chart From Audio is on, but Music Clip is empty.", this);
            else
            {
                notes = AudioSpectrumChart.Build(musicClip, out float heardBpm);
                if (!keepBpm && heardBpm > 1f) bpm = heardBpm;
                beatClock.bpm = bpm;
                string source = keepBpm ? "level" : "estimated";
                Debug.Log($"RhythmSession: {level?.displayName ?? musicClip.name} — {notes.Count} notes @ {bpm:0.#} BPM ({source}).", this);
            }
        }
        else if (MidiChartReader.TryLoadStreamingChart(midiFileName, out notes, out float fileBpm))
        {
            if (!keepBpm && fileBpm > 1f) bpm = fileBpm;
            beatClock.bpm = bpm;
        }

        if (notes != null && notes.Count > 0 && spawner != null)
        {
            spawner.SetNotes(notes);
            if (metronome != null) metronome.SetChartNotes(spawner.Notes);
            chartEnd = notes[notes.Count - 1].time + chartOffsetSeconds + 2f;
        }
        beatClock.songLength = chartEnd;
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

        if (metronome != null)
        {
            metronome.mode = metronomeMode;
            metronome.playOnStart = metronomeOn;
            metronome.enabled = metronomeOn || countInBeats > 0;
        }

        yield return StartCoroutine(CountInThenPlay());
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
            yield return WaitDsp(scheduleDelay);

        for (int i = 0; i < beats; i++)
        {
            if (metronome != null) metronome.PlayCountInClick();
            yield return WaitDsp(quarter);
        }

        if (musicSource.clip != null)
        {
            UseOneListener();
            AudioListener.pause = false;
            CornerMute.Apply();
            musicSource.spatialBlend = 0f;
            musicSource.mute = false;
            musicSource.volume = muteMusicForDemo ? 0f : musicVolume;
            beatClock.ResetBeatTracking();
            if (metronome != null) metronome.RewindNotes();
            musicSource.Play();
            Debug.Log($"RhythmSession: playing {musicSource.clip.name} (listeners {CountEnabledListeners()}, pause {AudioListener.pause}).", this);
        }
    }

    static int CountEnabledListeners()
    {
        int count = 0;
        AudioListener[] all = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
            if (all[i].enabled) count++;
        return count;
    }

    static IEnumerator WaitDsp(float seconds)
    {
        double end = AudioSettings.dspTime + Mathf.Max(0f, seconds);
        while (AudioSettings.dspTime < end)
            yield return null;
    }
}
