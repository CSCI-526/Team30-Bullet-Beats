/// <summary>One hittable event from a chart (usually one MIDI note-on).</summary>
[System.Serializable]
public struct ChartNote
{
    /// <summary>Seconds from song start when the target should reach the hit plane.</summary>
    public float time;
    /// <summary>MIDI pitch (e.g. 36 kick, 38 snare).</summary>
    public int pitch;
    public NoteLane lane;

    public ChartNote(float time, int pitch, NoteLane lane)
    {
        this.time = time;
        this.pitch = pitch;
        this.lane = lane;
    }
}

/// <summary>Where a chart note spawns targets. DrumOnly maps kick→LeftUpper, snare→RightUpper.</summary>
public enum NoteLane
{
    LeftUpper,
    RightUpper
}
