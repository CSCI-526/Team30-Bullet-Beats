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
    [Tooltip("Highest point on screen (0-1) an asteroid aims for, e.g. to keep them under a banner. 1 = no limit.")]
    [Range(0.5f, 1f)] public float maxViewportY = 1f;
    [Tooltip("Minimum viewport distance between two live targets.")]
    public float minViewportSeparation = 0.14f;
    [Tooltip("No asteroid aims inside this viewport radius around the ship. 0 = off.")]
    [Min(0f)] public float shipKeepOutRadius = 0.12f;
    [Tooltip("Hard-ban aim points under the ship (same X band, lower on screen) so rocks can't hide below it.")]
    public bool banBelowShip = true;
    [Tooltip("How wide the below-ship ban is, as a fraction of screen width (each side of the ship).")]
    [Range(0.05f, 0.5f)] public float belowShipHalfWidth = 0.28f;
    [Tooltip("Extra viewport lift above the ship before an aim point is allowed.")]
    [Min(0f)] public float belowShipPadding = 0.04f;
    [Tooltip("Most asteroids allowed on screen at once. Notes beyond this are skipped. 0 = no limit.")]
    [Min(0)] public int maxLiveAsteroids = 4;

    [Header("Shape bursts")]
    [Tooltip("When this many (or more) chart notes share nearly the same time, place them on a shape instead of random quadrants. Uses that count only — no extras.")]
    public bool shapeBursts = true;
    [Tooltip("Minimum notes in one time cluster to trigger a shape.")]
    [Min(3)] public int shapeMinCount = 3;
    [Tooltip("Notes within this many seconds of the first note count as the same burst.")]
    [Min(0f)] public float shapeClusterWindow = 0.08f;
    [Tooltip("Shape asteroids ignore the on-screen cap so the figure always completes.")]
    public bool shapeIgnoreCap = true;
    [Tooltip("Gap from screen edges for circle / X (viewport 0-0.5).")]
    [Range(0.02f, 0.3f)] public float shapeEdgeMargin = 0.12f;
    [Tooltip("Figure-8 vertical reach above/below center.")]
    [Range(0.02f, 0.5f)] public float shapeFigure8Height = 0.18f;
    public enum BurstShape { Circle, X, Figure8 }
    [Tooltip("Preferred shape. With Mix Shapes on, cycles Circle → X → Figure8.")]
    public BurstShape preferredShape = BurstShape.Circle;
    [Tooltip("Cycle shapes instead of always using Preferred Shape.")]
    public bool mixShapes = true;
    int shapeCycle;

    [Tooltip("Stem-driven spawn path. Empty = found or added on Awake. Only runs when the level has stems.")]
    public AsteroidChoreography choreography;

    [Header("Prefab")]
    [Tooltip("Optional. Empty = spawn a simple cube placeholder.")]
    public ChartTarget targetPrefab;
    public float targetScale = 4.4f;
    [Tooltip("Colour of normal asteroids (turbo asteroids use turboColor).")]
    public Color asteroidColor = new Color(0.62f, 0.66f, 0.74f);
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

    [Header("Big asteroid")]
    [Tooltip("Every Nth spawned target is a big, indestructible asteroid. 0 = off.")]
    [Min(0)] public int bigEveryN = 16;
    public float bigScale = 3f;
    [Tooltip("How close the asteroid must pass to hit the ship.")]
    public float bigHitRadius = 7f;
    public Color bigColor = new Color(1f, 0.15f, 0.15f);

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
        shapeCycle = 0;
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
        if (choreography == null) choreography = GetComponent<AsteroidChoreography>();
        if (choreography == null) choreography = gameObject.AddComponent<AsteroidChoreography>();
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
        float planetLead = LeadTime;
        SharedLeadTime = planetLead;

        // Stems are spawning the asteroids (AsteroidChoreography), so the chart is not used.
        if (StemsDriving) return;

        // Only spawn when that planet's fly-in actually begins. Notes whose flight
        // would have started before the song are skipped, so the count-in does not
        // dump the first several seconds of planets into the air at once.
        const float spawnSlack = 0.08f;
        float quarter = clock.SecondsPerBeat;
        while (nextIndex < notes.Count && notes[nextIndex].time - planetLead <= songTime)
        {
            float spawnAt = notes[nextIndex].time - planetLead;
            bool onTime = songTime - spawnAt <= spawnSlack && songTime <= notes[nextIndex].time;
            if (!onTime)
            {
                nextIndex++;
                continue;
            }

            // Peek ahead: notes that hit at nearly the same time form one burst.
            float clusterTime = notes[nextIndex].time;
            int end = nextIndex + 1;
            while (end < notes.Count && notes[end].time - clusterTime <= shapeClusterWindow)
                end++;
            int count = end - nextIndex;

            if (shapeBursts && count >= shapeMinCount)
            {
                BurstShape shape = NextBurstShape();
                for (int i = 0; i < count; i++)
                    Spawn(notes[nextIndex + i], quarter, planetLead, ShapePoint(shape, i, count), shapeIgnoreCap);
                nextIndex = end;
            }
            else
            {
                Spawn(notes[nextIndex], quarter, planetLead);
                nextIndex++;
            }
        }
    }

    /// <summary>Seconds from spawn to the hit point: at least one quarter note.</summary>
    public float LeadTime
    {
        get
        {
            BeatClock clock = BeatClock.Instance;
            float quarter = clock != null ? clock.SecondsPerBeat : 0.5f;
            return Mathf.Max(planetLeadSeconds, quarter);
        }
    }

    bool StemsDriving => choreography != null && choreography.enabled && choreography.HasLoadedStems;

    /// <summary>
    /// Spawns an asteroid that reaches the ship at noteTime, aimed at a point inside
    /// area (viewport 0-1). Used by AsteroidChoreography.
    /// </summary>
    public bool SpawnInArea(float noteTime, Rect area, bool centered = false, bool ignoreCap = false)
    {
        if (!ignoreCap && maxLiveAsteroids > 0 && liveViewports.Count >= maxLiveAsteroids) return false;
        BeatClock clock = BeatClock.Instance;
        float quarter = clock != null ? clock.SecondsPerBeat : 0.5f;
        var note = new ChartNote(noteTime, 0, NoteLane.LeftUpper);
        Vector2 viewport = centered
            ? new Vector2(area.center.x, Mathf.Min(area.center.y, maxViewportY))
            : PickInArea(area.xMin, area.xMax, area.yMin, area.yMax);
        SpawnAt(note, quarter, LeadTime, viewport);
        return true;
    }

    void Spawn(ChartNote note, float quarter, float lead) => Spawn(note, quarter, lead, PickViewport(note.lane), false);

    void Spawn(ChartNote note, float quarter, float lead, Vector2 viewport, bool ignoreCap = false)
    {
        if (!ignoreCap && maxLiveAsteroids > 0 && liveViewports.Count >= maxLiveAsteroids) return;
        SpawnAt(note, quarter, lead, viewport);
    }

    BurstShape NextBurstShape()
    {
        if (!mixShapes) return preferredShape;
        BurstShape shape = (BurstShape)(shapeCycle % 3);
        shapeCycle++;
        return shape;
    }

    /// <summary>
    /// Viewport point <paramref name="index"/> of <paramref name="count"/> along a shape.
    /// Count is the burst size — no padding to a fixed step count.
    /// </summary>
    Vector2 ShapePoint(BurstShape shape, int index, int count)
    {
        count = Mathf.Max(1, count);
        index = ((index % count) + count) % count;
        float margin = Mathf.Clamp(shapeEdgeMargin, 0.02f, 0.3f);
        Vector2 point;
        switch (shape)
        {
            case BurstShape.X:
                point = XPoint(index, count, margin);
                break;
            case BurstShape.Figure8:
                point = Figure8Point(index, count, margin);
                break;
            default:
                point = CirclePoint(index, count, margin);
                break;
        }
        point.y = Mathf.Min(point.y, maxViewportY);
        return point;
    }

    static Vector2 CirclePoint(int index, int count, float margin)
    {
        float angle = (270f + index * 360f / count) * Mathf.Deg2Rad;
        float r = 0.5f - margin;
        return new Vector2(0.5f + Mathf.Cos(angle) * r, 0.5f + Mathf.Sin(angle) * r);
    }

    static Vector2 XPoint(int index, int count, float margin)
    {
        // Split the burst across the two diagonals (no extra points).
        int firstLine = (count + 1) / 2;
        bool line0 = index < firstLine;
        int lineCount = line0 ? firstLine : count - firstLine;
        int lineIndex = line0 ? index : index - firstLine;
        float t = lineCount <= 1 ? 0.5f : lineIndex / (float)(lineCount - 1);
        float lo = margin, hi = 1f - margin;
        Vector2 start = line0 ? new Vector2(lo, hi) : new Vector2(hi, hi);
        Vector2 end = line0 ? new Vector2(hi, lo) : new Vector2(lo, lo);
        return Vector2.Lerp(start, end, t);
    }

    Vector2 Figure8Point(int index, int count, float margin)
    {
        float t = index / (float)count * 2f * Mathf.PI;
        float halfWidth = 0.5f - margin;
        float y = Mathf.Clamp(shapeFigure8Height, 0.02f, 0.5f);
        return new Vector2(0.5f + Mathf.Sin(t) * halfWidth, 0.5f + Mathf.Sin(2f * t) * y);
    }

    void SpawnAt(ChartNote note, float quarter, float lead, Vector2 viewport)
    {
        viewport = AwayFromShip(viewport);
        viewport.y = Mathf.Min(viewport.y, maxViewportY);

        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
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
            if (renderer != null)
            {
                Material material = NewAsteroidMaterial();
                if (material != null) renderer.material = material;
            }
        }

        target.Arm(note, spawn, hit);
        target.shipHitRadius = shipHitRadius;
        target.SetLifetime(quarter, lead, viewport);

        spawnCount++;
        if (bigEveryN > 0 && spawnCount % bigEveryN == 0)
            target.MakeBig(bigScale, bigHitRadius, bigColor);
        else if(turboEveryN > 0 && spawnCount % turboEveryN == 0)
            target.MakeTurbo(turboHits, turboHeal, turboScale, turboColor);

        Spawned?.Invoke(target);
    }

    // A primitive made from code gets the built-in default material, whose shader the URP web build
    // can't draw (magenta). Copy the ship's material instead: it's in the build and renders correctly.
    Material NewAsteroidMaterial()
    {
        Material template = null;
        if (PlayerHealth.Instance != null)
        {
            Renderer ship = PlayerHealth.Instance.GetComponentInChildren<Renderer>();
            if (ship != null) template = ship.sharedMaterial;
        }

        Material material;
        if (template != null) material = new Material(template);
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) return null;
            material = new Material(shader);
        }

        material.name = "Asteroid";
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", asteroidColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", asteroidColor);
        return material;
    }

    Vector2 PickViewport(NoteLane lane)
    {
        bool left = lane == NoteLane.LeftUpper || lane == NoteLane.LeftLower;
        bool upper = lane == NoteLane.LeftUpper || lane == NoteLane.RightUpper;
        float xMin = left ? quadrantMargin : 0.5f + quadrantMargin;
        float xMax = left ? 0.5f - quadrantMargin : 1f - quadrantMargin;
        float yMin = upper ? 0.5f + quadrantMargin : quadrantMargin;
        float yMax = upper ? 1f - quadrantMargin : 0.5f - quadrantMargin;
        return PickInArea(xMin, xMax, yMin, yMax);
    }

    Vector2 PickInArea(float xMin, float xMax, float yMin, float yMax)
    {
        yMax = Mathf.Min(yMax, maxViewportY);
        yMin = Mathf.Min(yMin, yMax);

        Vector2 best = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        float bestDist = -1f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 candidate = new Vector2(Random.Range(xMin, xMax), Random.Range(yMin, yMax));
            if (BlockedByShip(candidate)) continue;
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
        return AwayFromShip(best);
    }

    bool BlockedByShip(Vector2 viewport)
    {
        if (!TryGetShipViewport(out Vector2 ship)) return false;
        if (banBelowShip && IsBelowShip(viewport, ship)) return true;
        return shipKeepOutRadius > 0f && Vector2.Distance(viewport, ship) < shipKeepOutRadius;
    }

    bool IsBelowShip(Vector2 viewport, Vector2 ship)
    {
        if (viewport.y >= ship.y + belowShipPadding) return false;
        return Mathf.Abs(viewport.x - ship.x) <= belowShipHalfWidth;
    }

    /// <summary>
    /// Lifts points out from under the ship, then pushes them off the keep-out ring.
    /// Never leaves an aim point in the below-ship ban.
    /// </summary>
    Vector2 AwayFromShip(Vector2 viewport)
    {
        if (!TryGetShipViewport(out Vector2 ship)) return viewport;

        if (banBelowShip && IsBelowShip(viewport, ship))
        {
            viewport.y = Mathf.Min(maxViewportY, ship.y + Mathf.Max(belowShipPadding, shipKeepOutRadius));
            // If still inside the X band and keep-out, nudge sideways too.
            if (shipKeepOutRadius > 0f && Vector2.Distance(viewport, ship) < shipKeepOutRadius)
            {
                float side = viewport.x >= ship.x ? 1f : -1f;
                viewport.x = Mathf.Clamp01(ship.x + side * shipKeepOutRadius);
            }
        }

        if (shipKeepOutRadius > 0f)
        {
            Vector2 delta = viewport - ship;
            float dist = delta.magnitude;
            if (dist < shipKeepOutRadius)
            {
                if (dist < 0.0001f) delta = Vector2.up;
                else delta /= dist;
                // Prefer not to push further under the ship.
                if (banBelowShip && delta.y < 0f) delta.y = Mathf.Abs(delta.y);
                if (delta.sqrMagnitude < 0.0001f) delta = Vector2.up;
                else delta.Normalize();
                viewport = ship + delta * shipKeepOutRadius;
            }
        }

        viewport.x = Mathf.Clamp01(viewport.x);
        viewport.y = Mathf.Clamp(viewport.y, 0f, maxViewportY);
        if (banBelowShip && IsBelowShip(viewport, ship))
            viewport.y = Mathf.Min(maxViewportY, ship.y + Mathf.Max(belowShipPadding, 0.02f));
        return viewport;
    }

    static bool TryGetShipViewport(out Vector2 viewport)
    {
        viewport = default;
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Transform ship = null;
        if (PlayerHealth.Instance != null) ship = PlayerHealth.Instance.transform;
        else
        {
            PlayerMovement move = FindFirstObjectByType<PlayerMovement>();
            if (move != null) ship = move.transform;
        }
        if (ship == null) return false;

        Vector3 point = cam.WorldToViewportPoint(ship.position);
        if (point.z <= 0f) return false;
        viewport = new Vector2(point.x, point.y);
        return true;
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
