using System;
using UnityEngine;

/// <summary>
/// Graybox beat clock. Counts beats at a fixed BPM from when the scene starts, or follows
/// <see cref="music"/>'s playback time while that is set and playing. The audio work can drive
/// or replace this later; gameplay only needs <see cref="BeatPosition"/>, <see cref="Judge"/>
/// and <see cref="Beat"/>.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BeatClock : MonoBehaviour
{
    public static BeatClock Instance { get; private set; }

    [Tooltip("Beats per minute.")]
    public float bpm = 100f;
    [Tooltip("Seconds from the start of the song (or scene) to the first beat.")]
    public float firstBeatOffset = 0f;
    [Tooltip("Optional. While this has a clip, SongTime follows its playback timeline.")]
    public AudioSource music;
    [Tooltip("If music is assigned, do not advance the clock until playback actually starts.")]
    public bool holdUntilMusicPlays = true;

    [Header("Timing windows (seconds either side of a beat)")]
    public float perfectWindow = 0.07f;
    public float goodWindow = 0.15f;

    /// <summary>Raised once per beat with the beat's index (0 = first beat).</summary>
    public event Action<int> Beat;

    public float SecondsPerBeat => 60f / Mathf.Max(1f, bpm);

    public bool HasMusic => music != null && music.clip != null;

    public bool IsMusicPlaying => HasMusic && music.isPlaying;

    /// <summary>
    /// Song timeline in seconds. Follows <see cref="music"/> once it has a clip; otherwise
    /// falls back to time since this clock awoke.
    /// </summary>
    public float SongTime
    {
        get
        {
            if (HasMusic)
            {
                if (holdUntilMusicPlays && !music.isPlaying && music.timeSamples == 0)
                    return 0f;
                return (float)music.timeSamples / Mathf.Max(1, music.clip.frequency);
            }
            return Time.time - startTime;
        }
    }

    /// <summary>Beats since the first beat; 12.25 is a quarter beat after beat 12.</summary>
    public float BeatPosition => (SongTime - firstBeatOffset) / SecondsPerBeat;

    float startTime;
    int lastBeat = int.MinValue;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("More than one BeatClock in the scene; using the newest one.", this);
        Instance = this;
        startTime = Time.time;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (HasMusic && holdUntilMusicPlays && !music.isPlaying && music.timeSamples == 0)
            return;

        int beat = Mathf.FloorToInt(BeatPosition);
        if (beat == lastBeat) return;
        lastBeat = beat;
        if (beat >= 0) Beat?.Invoke(beat);
    }

    /// <summary>Call when music is (re)started so beat events resync from the song timeline.</summary>
    public void ResetBeatTracking()
    {
        lastBeat = int.MinValue;
        startTime = Time.time;
    }

    /// <summary>Judges an input made this frame against the nearest beat.</summary>
    public BeatJudgment Judge(out int beat, out float offset)
    {
        float position = BeatPosition;
        beat = Mathf.RoundToInt(position);
        offset = (position - beat) * SecondsPerBeat;

        float error = Mathf.Abs(offset);
        if (error <= perfectWindow) return BeatJudgment.Perfect;
        if (error <= goodWindow) return BeatJudgment.Good;
        return BeatJudgment.Miss;
    }

    /// <summary>Judges an input against a chart note time on the song clock.</summary>
    public BeatJudgment JudgeAtSongTime(float noteTime, out float offset)
    {
        offset = SongTime - noteTime;
        float error = Mathf.Abs(offset);
        if (error <= perfectWindow) return BeatJudgment.Perfect;
        if (error <= goodWindow) return BeatJudgment.Good;
        return BeatJudgment.Miss;
    }
}
