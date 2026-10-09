using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Minimal Type-0/1 MIDI reader for a single usable note track.
/// Maps pitch 36 → LeftUpper and 38 → RightUpper; other pitches are ignored.
/// </summary>
public static class MidiChartReader
{
    public const int PitchLeft = 36;  // kick
    public const int PitchRight = 38; // snare

    public static bool TryLoadStreamingChart(string fileName, out List<ChartNote> notes, out float bpm)
    {
        notes = new List<ChartNote>();
        bpm = 100f;
        string path = Path.Combine(Application.streamingAssetsPath, "Charts", fileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"MIDI chart not found: {path}");
            return false;
        }
        return TryParse(File.ReadAllBytes(path), notes, out bpm);
    }

    public static bool TryParse(byte[] data, List<ChartNote> notes, out float bpm)
    {
        notes.Clear();
        bpm = 100f;
        if (data == null || data.Length < 14 || data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
        {
            Debug.LogError("Not a MIDI file.");
            return false;
        }

        int i = 8;
        int format = ReadU16(data, ref i);
        int trackCount = ReadU16(data, ref i);
        int division = ReadU16(data, ref i);
        if ((division & 0x8000) != 0)
        {
            Debug.LogError("SMPTE MIDI timing is not supported.");
            return false;
        }

        var tempoMap = new List<(long tick, int usPerQuarter)>();
        var rawNotes = new List<(long tick, int pitch)>();

        for (int t = 0; t < trackCount; t++)
        {
            if (i + 8 > data.Length || data[i] != 'M' || data[i + 1] != 'T' || data[i + 2] != 'r' || data[i + 3] != 'k')
            {
                Debug.LogError($"Bad MIDI track header at {i}");
                return false;
            }
            i += 4;
            int length = ReadU32(data, ref i);
            int end = i + length;
            long tick = 0;
            int? running = null;

            while (i < end)
            {
                tick += ReadVar(data, ref i);
                int status = data[i];
                if ((status & 0x80) != 0)
                {
                    running = status;
                    i++;
                }
                else
                {
                    if (running == null) return false;
                    status = running.Value;
                }

                if (status == 0xFF)
                {
                    int type = data[i++];
                    int len = ReadVar(data, ref i);
                    if (type == 0x51 && len == 3)
                    {
                        int us = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                        tempoMap.Add((tick, us));
                    }
                    i += len;
                    if (type == 0x2F) break;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    int len = ReadVar(data, ref i);
                    i += len;
                }
                else
                {
                    int eventType = status & 0xF0;
                    if (eventType == 0xC0 || eventType == 0xD0)
                    {
                        i += 1;
                    }
                    else
                    {
                        int a = data[i++];
                        int b = data[i++];
                        if (eventType == 0x90 && b > 0)
                            rawNotes.Add((tick, a));
                    }
                }
            }
            i = end;
        }

        if (tempoMap.Count == 0)
            tempoMap.Add((0, 750_000)); // 80 BPM, matches DrumOnly
        tempoMap.Sort((x, y) => x.tick.CompareTo(y.tick));
        bpm = 60_000_000f / tempoMap[0].usPerQuarter;

        foreach (var (tick, pitch) in rawNotes)
        {
            if (!TryMapLane(pitch, out NoteLane lane)) continue;
            notes.Add(new ChartNote(TicksToSeconds(tick, division, tempoMap), pitch, lane));
        }

        notes.Sort((a, b) => a.time.CompareTo(b.time));
        Debug.Log($"MIDI parsed ({format}): {notes.Count} chart notes @ {bpm:0.##} BPM (ignored non 36/38).");
        return notes.Count > 0;
    }

    public static bool TryMapLane(int pitch, out NoteLane lane)
    {
        if (pitch == PitchLeft)
        {
            lane = NoteLane.LeftUpper;
            return true;
        }
        if (pitch == PitchRight)
        {
            lane = NoteLane.RightUpper;
            return true;
        }
        lane = default;
        return false;
    }

    static float TicksToSeconds(long tick, int division, List<(long tick, int usPerQuarter)> tempoMap)
    {
        double seconds = 0;
        long prev = 0;
        int us = tempoMap[0].usPerQuarter;
        int mapIndex = 0;

        while (mapIndex + 1 < tempoMap.Count && tempoMap[mapIndex + 1].tick <= tick)
        {
            long next = tempoMap[mapIndex + 1].tick;
            seconds += (next - prev) * (us / 1_000_000.0) / division;
            prev = next;
            mapIndex++;
            us = tempoMap[mapIndex].usPerQuarter;
        }

        seconds += (tick - prev) * (us / 1_000_000.0) / division;
        return (float)seconds;
    }

    static int ReadU16(byte[] data, ref int i)
    {
        int v = (data[i] << 8) | data[i + 1];
        i += 2;
        return v;
    }

    static int ReadU32(byte[] data, ref int i)
    {
        int v = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
        i += 4;
        return v;
    }

    static int ReadVar(byte[] data, ref int i)
    {
        int v = 0;
        while (true)
        {
            byte c = data[i++];
            v = (v << 7) | (c & 0x7F);
            if ((c & 0x80) == 0) return v;
        }
    }
}
