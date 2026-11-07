using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rates the chart a player actually faces, from 1 to 10.
/// Average note density is stretched from the sparsest chart in the set (level 1)
/// to the densest (level 10). A chart whose busiest <see cref="PeakWindowSeconds"/>
/// run at least <see cref="PeakBumpRatio"/> times its own average gains one level.
/// On the current songs, Pink Panther at Easy is the sparse end of that scale.
/// </summary>
public static class ChartRating
{
    public const float PeakWindowSeconds = 5f;
    /// <summary>Busiest 5 seconds at least this many times the chart's own average density.</summary>
    public const float PeakBumpRatio = 2f;

    public readonly struct Load
    {
        public readonly int noteCount;
        public readonly float notesPerSecond;
        public readonly float peakNotesPerSecond;

        public Load(int noteCount, float notesPerSecond, float peakNotesPerSecond)
        {
            this.noteCount = noteCount;
            this.notesPerSecond = notesPerSecond;
            this.peakNotesPerSecond = peakNotesPerSecond;
        }

        public bool HasDensePeak =>
            notesPerSecond > 0f && peakNotesPerSecond >= notesPerSecond * PeakBumpRatio;
    }

    /// <summary>Density of the notes the player faces. <paramref name="songSeconds"/> is the length of the file.</summary>
    public static Load Measure(IReadOnlyList<ChartNote> facedNotes, float songSeconds)
    {
        if (facedNotes == null || facedNotes.Count == 0)
            return new Load(0, 0f, 0f);

        var times = new float[facedNotes.Count];
        for (int i = 0; i < facedNotes.Count; i++)
            times[i] = facedNotes[i].time;
        System.Array.Sort(times);

        float length = songSeconds > 0.01f ? songSeconds : Mathf.Max(0.01f, times[times.Length - 1]);
        float notesPerSecond = times.Length / length;

        int peakCount = 1;
        int end = 0;
        for (int start = 0; start < times.Length; start++)
        {
            while (end < times.Length && times[end] - times[start] <= PeakWindowSeconds)
                end++;
            peakCount = Mathf.Max(peakCount, end - start);
        }

        return new Load(times.Length, notesPerSecond, peakCount / PeakWindowSeconds);
    }

    /// <summary>
    /// Same thinning as <see cref="ChartTargetSpawner"/>: keep every Nth note in time order, then measure that.
    /// </summary>
    public static Load Measure(IReadOnlyList<ChartNote> notes, float songSeconds, int noteStride)
    {
        return Measure(Thin(notes, noteStride), songSeconds);
    }

    /// <summary>
    /// 1 is <paramref name="sparsestNotesPerSecond"/>, 10 is <paramref name="densestNotesPerSecond"/>.
    /// A dense peak adds one. The result stays inside 1..10.
    /// </summary>
    public static int Level(Load load, float sparsestNotesPerSecond, float densestNotesPerSecond)
    {
        float span = densestNotesPerSecond - sparsestNotesPerSecond;
        float t = span <= 0.0001f
            ? 0f
            : Mathf.InverseLerp(sparsestNotesPerSecond, densestNotesPerSecond, load.notesPerSecond);
        int level = Mathf.Clamp(Mathf.RoundToInt(1f + 9f * t), 1, 10);
        if (load.HasDensePeak)
            level = Mathf.Min(10, level + 1);
        return level;
    }

    /// <summary>Rates every chart on one scale. The sparsest density in <paramref name="charts"/> is level 1.</summary>
    public static int[] Levels(IReadOnlyList<Load> charts)
    {
        var levels = new int[charts != null ? charts.Count : 0];
        if (charts == null || charts.Count == 0)
            return levels;

        float sparsest = charts[0].notesPerSecond;
        float densest = sparsest;
        for (int i = 1; i < charts.Count; i++)
        {
            sparsest = Mathf.Min(sparsest, charts[i].notesPerSecond);
            densest = Mathf.Max(densest, charts[i].notesPerSecond);
        }

        for (int i = 0; i < charts.Count; i++)
            levels[i] = Level(charts[i], sparsest, densest);
        return levels;
    }

    static List<ChartNote> Thin(IReadOnlyList<ChartNote> notes, int noteStride)
    {
        var sorted = new List<ChartNote>(notes != null ? notes.Count : 0);
        if (notes != null)
        {
            for (int i = 0; i < notes.Count; i++)
                sorted.Add(notes[i]);
        }
        sorted.Sort((a, b) => a.time.CompareTo(b.time));

        int stride = Mathf.Max(1, noteStride);
        if (stride == 1)
            return sorted;

        var kept = new List<ChartNote>((sorted.Count + stride - 1) / stride);
        for (int i = 0; i < sorted.Count; i += stride)
            kept.Add(sorted[i]);
        return kept;
    }
}
