using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns chart targets from MIDI notes. LeftUpper / RightUpper map to the top-left and
/// top-right approach lanes. Targets are spawned early so they reach the hit plane on time.
/// </summary>
public class ChartTargetSpawner : MonoBehaviour
{
    public static ChartTargetSpawner Instance { get; private set; }
    public static float SharedLeadTime { get; private set; } = 2f;

    [Header("Chart")]
    public string midiFileName = "Tetris.mid";
    public bool loadOnStart = true;
    [Tooltip("Keep only every Nth chart note (2 = half as many notes).")]
    [Min(1)] public int noteStride = 2;


    [Header("Timing")]
    [Tooltip("How long before the note the planet appears. The ring still waits until one quarter note.")]
    public float planetLeadSeconds = 0f;
    [Tooltip("Extra shift applied on top of RhythmSession chart offset (usually leave 0).")]
    public float chartOffsetSeconds = 0f;
    [Tooltip("Distance from the camera when the planet reaches its hit point.")]
    public float distanceFromCamera = 55f;
    [Tooltip("Distance from the camera when the planet first appears, farther than the hit point.")]

    public float spawnDistanceFromCamera = 60f;
    [Tooltip("Multiplier applied to the spawn distance.")]
    public float spawnDistanceMultiplier = 1f;
    [Tooltip("How close a planet must get to the ship to deal damage.")]
    public float shipHitRadius = 3f;

    [Header("Screen quadrants")]
    [Tooltip("Inset so targets stay inside the quarter and off the center cross.")]
    public float quadrantMargin = 0.08f;
    [Tooltip("Minimum viewport distance between two live targets.")]
    public float minViewportSeparation = 0.14f;

    [Header("Prefab")]
    [Tooltip("Optional. Empty = spawn a simple cube placeholder.")]
    public ChartTarget targetPrefab;
    public float targetScale = 4.4f;
    public Color leftColor = new Color(0.3f, 0.85f, 1f);
    public Color rightColor = new Color(1f, 0.75f, 0.25f);

    [Header("Turbo asteroid")]
    [Tooltip("Every Nth spawned target is a turbo asteroid. 0 = off")]
    [Min(0)] public int turboEveryN = 8;
    [Tooltip("Shots needed to destroy a turbo asteroid.")]
    [Min(2)] public int turboHits = 2;
    [Tooltip("Life gained when a turbo asteroid is destroyed.")]
    [Min(1)] public int turboHeal = 2;
    public float turboScale = 1.5f;
    public Color turboColor = new Color(1f, 0.45f, 0.1f);
    int spawnCount;

    readonly List<ChartNote> notes = new List<ChartNote>();
    readonly List<Vector2> liveViewports = new List<Vector2>();
    int nextIndex;

    public IReadOnlyList<ChartNote> Notes => notes;

    /// <summary>Raised for every target right after it spawns (and after it is made turbo).</summary>
    public event System.Action<ChartTarget> Spawned;

    public void SetNotes(List<ChartNote> chartNotes)
    {
        notes.Clear();
        if (chartNotes != null)
        {
            for (int i = 0; i < chartNotes.Count; i++)
            {
                ChartNote n = chartNotes[i];
                n.time += chartOffsetSeconds;
                notes.Add(n);
            }
        }
        notes.Sort((a, b) => a.time.CompareTo(b.time));
        int stride = Mathf.Max(1, noteStride);
        if (stride > 1)
        {
            var thinned = new List<ChartNote>();
            for (int i = 0; i < notes.Count; i += stride)
                thinned.Add(notes[i]);
            notes.Clear();
            notes.AddRange(thinned);
        }
        nextIndex = 0;
    }

    /// <summary>Seconds into the current note window for HUD (0 = just after note, 1 = next note).</summary>
    public bool TryGetNotePulse(float songTime, out float pulse01, out float secondsToNext)
    {
        pulse01 = 0f;
        secondsToNext = 0f;
        if (notes.Count == 0) return false;

        int next = 0;
        while (next < notes.Count && notes[next].time <= songTime)
            next++;

        float prevTime = next > 0 ? notes[next - 1].time : notes[0].time - 0.6f;
        float nextTime = next < notes.Count ? notes[next].time : notes[notes.Count - 1].time + 0.6f;
        float span = Mathf.Max(0.05f, nextTime - prevTime);
        secondsToNext = nextTime - songTime;
        pulse01 = Mathf.Clamp01((songTime - prevTime) / span);
        return true;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (loadOnStart && notes.Count == 0)
        {
            if (MidiChartReader.TryLoadStreamingChart(midiFileName, out var loaded, out float bpm))
            {
                SetNotes(loaded);
                if (BeatClock.Instance != null) BeatClock.Instance.bpm = bpm;
            }
        }
    }

    void Update()
    {
        BeatClock clock = BeatClock.Instance;
        if (clock == null || notes.Count == 0) return;

        // With a song, wait for it to play; without one (e.g. the tutorial) follow the scene clock.
        if (clock.HasMusic && !clock.IsMusicPlaying) return;

        float songTime = clock.SongTime;
        float quarter = clock.SecondsPerBeat;
        float planetLead = Mathf.Max(planetLeadSeconds, quarter);
        SharedLeadTime = planetLead;

        // Only spawn when that planet's fly-in actually begins. Notes whose flight
        // would have started before the song are skipped, so the count-in does not
        // dump the first several seconds of planets into the air at once.
        const float spawnSlack = 0.08f;
        while (nextIndex < notes.Count && notes[nextIndex].time - planetLead <= songTime)
        {
            float spawnAt = notes[nextIndex].time - planetLead;
            bool onTime = songTime - spawnAt <= spawnSlack && songTime <= notes[nextIndex].time;
            if (onTime)
                Spawn(notes[nextIndex], quarter, planetLead);
            nextIndex++;
        }
    }

    void Spawn(ChartNote note, float quarter, float lead)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        Vector2 viewport = PickViewport(note.lane);
        float spawnDistance = Mathf.Max(spawnDistanceFromCamera * Mathf.Max(1f, spawnDistanceMultiplier), distanceFromCamera + 1f);
        if (cam != null && cam.farClipPlane < spawnDistance + 50f)
            cam.farClipPlane = spawnDistance + 50f;
        Vector3 hit = cam != null
            ? cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, distanceFromCamera))
            : new Vector3(note.lane == NoteLane.LeftUpper ? -8f : 8f, 8f, 0f);
        Vector3 spawn = cam != null
            ? cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, spawnDistance))
            : hit + Vector3.forward * 80f;
        liveViewports.Add(viewport);

        ChartTarget target;
        if (targetPrefab != null)
        {
            target = Instantiate(targetPrefab, spawn, Quaternion.identity);
        }
        else
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"ChartTarget_{note.lane}_{note.time:0.00}";
            go.transform.localScale = Vector3.one * targetScale;
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            target = go.AddComponent<ChartTarget>();
            var renderer = go.GetComponent<MeshRenderer>();
            //if (renderer != null)
            //{
            //    renderer.material = new Material(renderer.sharedMaterial)
            //    {
            //        color = note.lane == NoteLane.LeftUpper ? leftColor : rightColor
            //    };
            //}
        }

        target.Arm(note, spawn, hit);
        target.shipHitRadius = shipHitRadius;
        target.SetLifetime(quarter, lead, viewport);

        spawnCount++;
        if(turboEveryN > 0 && spawnCount % turboEveryN == 0)
            target.MakeTurbo(turboHits, turboHeal, turboScale, turboColor);

        Spawned?.Invoke(target);
    }

    Vector2 PickViewport(NoteLane lane)
    {
        bool left = lane == NoteLane.LeftUpper;
        float xMin = left ? quadrantMargin : 0.5f + quadrantMargin;
        float xMax = left ? 0.5f - quadrantMargin : 1f - quadrantMargin;
        float yMin = 0.5f + quadrantMargin;
        float yMax = 1f - quadrantMargin;

        Vector2 best = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        float bestDist = -1f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 candidate = new Vector2(Random.Range(xMin, xMax), Random.Range(yMin, yMax));
            float nearest = float.MaxValue;
            for (int i = 0; i < liveViewports.Count; i++)
                nearest = Mathf.Min(nearest, Vector2.Distance(candidate, liveViewports[i]));
            if (nearest >= minViewportSeparation) return candidate;
            if (nearest > bestDist)
            {
                bestDist = nearest;
                best = candidate;
            }
        }
        return best;
    }

    public void ReleaseViewport(Vector2 viewport)
    {
        for (int i = 0; i < liveViewports.Count; i++)
        {
            if ((liveViewports[i] - viewport).sqrMagnitude < 0.0001f)
            {
                liveViewports.RemoveAt(i);
                return;
            }
        }
    }
}
