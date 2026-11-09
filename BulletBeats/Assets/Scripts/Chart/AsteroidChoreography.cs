using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns asteroids from a level's isolated stems (drums / other / vocals / bass).
/// Each stem plays near-silently ahead of the song by the spawner's lead time and is read with
/// <see cref="AudioSource.GetSpectrumData"/>. When a stem's energy jumps above its recent average,
/// an asteroid is spawned in that stem's screen section, timed to reach the ship when the song
/// gets to that moment. The screen is a 3 x 2 grid. Levels without stems use the normal chart.
/// </summary>
public class AsteroidChoreography : MonoBehaviour
{
    [Header("Sections (3 columns x 2 rows, row 0 = bottom)")]
    public Vector2Int drumsCell = new Vector2Int(1, 1);   // top center
    public Vector2Int otherCell = new Vector2Int(0, 1);   // top left
    public Vector2Int vocalsCell = new Vector2Int(2, 1);  // top right
    public Vector2Int bassCell = new Vector2Int(1, 0);    // bottom center
    [Range(0f, 0.15f)] public float sectionMargin = 0.04f;
    [Tooltip("On: asteroids aim at the exact center of their section. Off: a random point inside it.")]
    public bool spawnAtCenter = true;

    [Header("Detection")]
    [Tooltip("Spectrum samples per read. Power of two, 64-8192.")]
    public int spectrumSize = 512;
    [Tooltip("A stem triggers when its energy is this many times its running average.")]
    public float peakRatio = 1.6f;
    [Tooltip("Ignore energy below this (relative to the stem's loudest recent read).")]
    [Range(0f, 1f)] public float minRelativeEnergy = 0.25f;
    [Tooltip("How quickly the running average follows the energy (per second).")]
    public float averageSpeed = 4f;
    [Tooltip("Shortest time between two asteroids from the same stem.")]
    public float minGapSeconds = 0.6f;
    [Tooltip("Stem playback volume. Very low so they aren't heard; detection is relative so the level doesn't matter.")]
    [Range(0.0001f, 1f)] public float analysisVolume = 0.001f;
    [Tooltip("Resync the stems if they drift this far from the song.")]
    public float resyncSeconds = 0.05f;

    [Header("Busy stem")]
    [Tooltip("Seconds of history used to measure how busy each stem is.")]
    public float activityWindow = 2f;
    [Tooltip("Hits within the window for a stem to count as busy.")]
    [Min(1)] public int busyHits = 4;
    [Tooltip("Asteroids spawned per hit while a stem is busy.")]
    [Min(1)] public int burstCount = 2;
    [Tooltip("Seconds between the asteroids of one burst.")]
    public float burstSpacing = 0.12f;

    [Header("Priority")]
    [Tooltip("When drums and other are both active, drums spawn instead of other.")]
    public bool drumsOverOther = true;

    [Header("Other circle")]
    [Tooltip("While other is busy, its asteroids go one after another around a counterclockwise circle.")]
    [UnityEngine.Serialization.FormerlySerializedAs("bassCircle")]
    public bool otherCircle = true;

    public enum OtherPattern { X, Circle, Figure8 }
    [Tooltip("Shape traced while other is busy. X: two diagonals corner to corner. Circle: around the screen.")]
    public OtherPattern otherPattern = OtherPattern.X;
    [Tooltip("Mix both shapes instead of only otherPattern: circlesPerX circles, then one X.")]
    public bool mixShapes = true;
    [Min(0)] public int circlesPerX = 3;
    OtherPattern activeShape = OtherPattern.Circle;
    int circlesSinceX;
    [Tooltip("Busy vocals trace a sideways figure 8 through the screen center.")]
    public bool vocalsFigure8 = true;
    [Min(4)] public int figure8Steps = 16;
    [Tooltip("Horizontal inset from the left/right edges (viewport).")]
    [Range(0f, 0.4f)] public float figure8EdgeMargin = 0.06f;
    [Tooltip("Vertical reach above/below center (viewport). Small keeps it near the middle.")]
    [Range(0.02f, 0.5f)] public float figure8Height = 0.18f;
    [Tooltip("X: asteroids along each diagonal (corner to corner, inclusive).")]
    [Min(2)] public int xStepsPerLine = 5;
    [Tooltip("X: gap kept between the corners and the screen edges (viewport 0-0.5).")]
    [Range(0.02f, 0.3f)] public float xEdgeMargin = 0.12f;
    [Tooltip("X: the first X starts from the top left. Each X after that switches side.")]
    public bool xStartLeft = true;
    [Tooltip("On: the circle goes around the whole screen (center and radius below are ignored). Off: uses Circle Center and Circle Radius.")]
    public bool circleFullScreen = true;
    [Tooltip("Full-screen circle: gap kept between the circle and the screen edges (viewport 0-0.5).")]
    [Range(0.02f, 0.3f)] public float circleEdgeMargin = 0.12f;
    [Tooltip("Points around the circle.")]
    [Min(3)] public int circleSteps = 8;
    [Tooltip("Circle radius as a fraction of the screen height.")]
    [Range(0.05f, 0.4f)] public float circleRadius = 0.15f;
    [Tooltip("Circle center on screen (viewport 0-1). Default is the center of the top-left section.")]
    public Vector2 circleCenter = new Vector2(1f / 6f, 0.75f);
    [Tooltip("Angle of the first point, in degrees (0 = right, 90 = top).")]
    public float circleStartAngle = 270f;
    [Tooltip("Asteroids per bass hit while circling.")]
    [Min(1)] public int circlePerHit = 2;
    [Tooltip("Hits by other within the activity window that start the circle.")]
    [Min(1)] public int circleBusyHits = 3;
    [Tooltip("Seconds between consecutive asteroids on the circle.")]
    public float circleSpacing = 0.35f;
    [Tooltip("Log to the console when the circle starts and stops.")]
    public bool logCircle = true;
    [Tooltip("Seconds after a shape finishes before another can start.")]
    [Min(0f)] public float shapeCooldown = 4f;
    [Tooltip("Shape asteroids ignore the spawner's on-screen limit so the shape always forms fully.")]
    public bool shapesIgnoreCap = true;

    [Header("Easy difficulty")]
    [Tooltip("Min Gap Seconds is multiplied by this on Easy.")]
    [Min(1f)] public float easyGapMultiplier = 2.5f;
    [Tooltip("Circle Spacing is multiplied by this on Easy.")]
    [Min(1f)] public float easyCircleSpacingMultiplier = 3f;
    [Tooltip("On Easy, at least this many seconds between any two asteroids.")]
    [Min(0f)] public float easyMinSpawnInterval = 1f;
    [Tooltip("Asteroids per busy-stem hit on Easy.")]
    [Min(1)] public int easyBurstCount = 1;
    [Tooltip("Most asteroids on screen at once on Easy.")]
    [Min(1)] public int easyMaxLiveAsteroids = 2;
    [Tooltip("Turn off big (unshootable) asteroids on Easy.")]
    public bool easyNoBigAsteroids = true;

    [Header("Difficult")]
    [Tooltip("Min Gap Seconds is multiplied by this on Difficult (below 1 = more often).")]
    [Range(0.1f, 1f)] public float hardGapMultiplier = 0.5f;
    [Tooltip("Circle Spacing is multiplied by this on Difficult (below 1 = faster circle).")]
    [Range(0.1f, 1f)] public float hardCircleSpacingMultiplier = 0.6f;
    [Tooltip("Asteroids per busy-stem hit on Difficult.")]
    [Min(1)] public int hardBurstCount = 3;
    [Tooltip("Most asteroids on screen at once on Difficult.")]
    [Min(1)] public int hardMaxLiveAsteroids = 5;
    [Tooltip("Every Nth asteroid is big (unshootable) on Difficult. 0 = keep the spawner's value.")]
    [Min(0)] public int hardBigEveryN = 8;
    [Tooltip("Every Nth asteroid is turbo (2 hits) on Difficult. 0 = keep the spawner's value.")]
    [Min(0)] public int hardTurboEveryN = 4;
    [Tooltip("Peak Ratio is multiplied by this on Difficult (below 1 = more hits detected).")]
    [Range(0.5f, 1f)] public float hardPeakRatioMultiplier = 0.8f;

    public enum CircleAlternate { EveryLap, EveryCircle, Never }
    [Tooltip("When the circle switches between counterclockwise and clockwise. EveryLap: after each full trip around. EveryCircle: each time other gets busy again.")]
    public CircleAlternate alternateDirection = CircleAlternate.EveryLap;
    [Tooltip("Direction of the very first circle.")]
    public bool startCounterclockwise = true;

    class Stem
    {
        public string name;
        public AudioClip clip;
        public Vector2Int cell;
        public AudioSource source;
        public float energy;
        public float average;
        public float peak;
        public float lastSpawn = float.NegativeInfinity;
        public readonly System.Collections.Generic.Queue<float> hits = new System.Collections.Generic.Queue<float>();
    }

    Stem[] stems;
    float[] spectrum;
    AudioSource music;
    ChartTargetSpawner spawner;
    float lead;
    int circleIndex;
    bool circling;
    float nextCircleTime;
    bool counterclockwise = true;
    bool firstCircle = true;
    bool xLeftFirst = true;
    bool firstX = true;
    int shapeLength;
    float shapeReadyAt = float.NegativeInfinity;

    // Settings for the current run, after the difficulty is applied.
    float gapScale = 1f, circleScale = 1f, minSpawnInterval, peakScale = 1f;
    int runBurstCount;
    float lastAnySpawn = float.NegativeInfinity;
    bool savedSpawnerSettings;
    int baseMaxLive, baseBigEveryN, baseTurboEveryN;

    /// <summary>True once stems are loaded for the current level.</summary>
    public bool HasLoadedStems => stems != null && stems.Length > 0;

    public static bool HasStems(GameFlow.Level level) =>
        level != null && (level.drumsStem != null || level.otherStem != null || level.vocalsStem != null || level.bassStem != null);

    /// <summary>Creates a near-silent source per stem and loads its audio. Call before the song starts.</summary>
    public IEnumerator Load(GameFlow.Level level)
    {
        Stop();
        if (!HasStems(level)) yield break;

        Stem[] all =
        {
            new Stem { name = "drums", clip = level.drumsStem, cell = drumsCell },
            new Stem { name = "other", clip = level.otherStem, cell = otherCell },
            new Stem { name = "vocals", clip = level.vocalsStem, cell = vocalsCell },
            new Stem { name = "bass", clip = level.bassStem, cell = bassCell }
        };
        Stem[] found = System.Array.FindAll(all, s => s.clip != null);

        foreach (Stem stem in found)
        {
            if (stem.clip.loadState != AudioDataLoadState.Loaded) stem.clip.LoadAudioData();
            while (stem.clip.loadState == AudioDataLoadState.Loading) yield return null;

            var go = new GameObject($"Stem_{stem.name}");
            go.transform.SetParent(transform, false);
            stem.source = go.AddComponent<AudioSource>();
            stem.source.clip = stem.clip;
            stem.source.playOnAwake = false;
            stem.source.loop = false;
            stem.source.spatialBlend = 0f;
            stem.source.volume = analysisVolume;
        }
        stems = found;
    }

    /// <summary>Starts the stems <paramref name="leadSeconds"/> ahead of <paramref name="song"/>, which has just started playing.</summary>
    public void Play(AudioSource song, ChartTargetSpawner targetSpawner, float leadSeconds)
    {
        if (!HasLoadedStems || song == null) return;
        music = song;
        spawner = targetSpawner;
        lead = Mathf.Max(0f, leadSeconds);
        spectrum = new float[Mathf.ClosestPowerOfTwo(Mathf.Clamp(spectrumSize, 64, 8192))];
        circleIndex = 0;
        circling = false;
        counterclockwise = startCounterclockwise;
        firstCircle = true;
        xLeftFirst = xStartLeft;
        firstX = true;
        shapeLength = 0;
        shapeReadyAt = float.NegativeInfinity;
        ApplyDifficulty(ScoreKeeper.SelectedDifficulty);

        foreach (Stem stem in stems)
        {
            stem.average = 0f;
            stem.peak = 0f;
            stem.lastSpawn = float.NegativeInfinity;
            stem.hits.Clear();
            if (lead >= stem.clip.length) continue;
            stem.source.time = lead;
            stem.source.Play();
        }
    }

    public void Stop()
    {
        music = null;
        if (stems == null) return;
        foreach (Stem stem in stems)
            if (stem.source != null) Destroy(stem.source.gameObject);
        stems = null;
    }

    void OnDestroy() => Stop();

    void Update()
    {
        if (music == null || !HasLoadedStems || spawner == null) return;
        BeatClock clock = BeatClock.Instance;
        if (clock == null || !music.isPlaying) return;

        float noteTime = clock.SongTime + lead;
        float dt = Time.unscaledDeltaTime;

        // Read every stem first, then only the loudest one may spawn this frame.
        Stem loudest = null;
        foreach (Stem stem in stems)
        {
            stem.energy = 0f;
            AudioSource source = stem.source;
            if (source == null || !source.isPlaying) continue;

            // Follow the song: same pitch (slow-mo) and stay lead seconds ahead.
            source.pitch = music.pitch;
            float wanted = music.time + lead;
            if (wanted < stem.clip.length && Mathf.Abs(source.time - wanted) > resyncSeconds)
                source.time = wanted;

            source.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
            for (int i = 0; i < spectrum.Length; i++) stem.energy += spectrum[i];
            stem.peak = Mathf.Max(stem.peak * (1f - 0.05f * dt), stem.energy);

            if (loudest == null || stem.energy > loudest.energy) loudest = stem;
        }

        // Count hits on every stem (even ones that don't spawn) to see which is busiest.
        Stem busiest = null;
        foreach (Stem stem in stems)
        {
            if (stem.energy > 0f && stem.energy >= stem.peak * minRelativeEnergy
                && stem.energy > stem.average * peakRatio * peakScale
                && (stem.hits.Count == 0 || noteTime - LastHit(stem) >= minGapSeconds))
                stem.hits.Enqueue(noteTime);
            while (stem.hits.Count > 0 && noteTime - stem.hits.Peek() > activityWindow)
                stem.hits.Dequeue();
            if (stem.hits.Count >= busyHits && (busiest == null || stem.hits.Count > busiest.hits.Count))
                busiest = stem;
        }

        // A busy stem takes over: only it spawns, several asteroids per hit, all in its section.
        Stem spawning = busiest ?? loudest;

        // Drums beat other: if other would spawn but drums are hitting (or busy) too, use drums.
        if (drumsOverOther && spawning != null && spawning.name == "other")
        {
            Stem drums = System.Array.Find(stems, s => s.name == "drums");
            if (drums != null && (IsHit(drums) || drums.hits.Count >= busyHits))
            {
                if (busiest == spawning) busiest = drums.hits.Count >= busyHits ? drums : null;
                spawning = drums;
            }
        }
        int count = busiest != null && busiest == spawning ? runBurstCount : 1;

        // Busy other starts a shape (X or circle). Once started it always finishes: one asteroid
        // every circleSpacing seconds until every point is placed, then a cooldown before the next.
        Stem other = System.Array.Find(stems, s => s.name == "other");
        bool otherBusy = otherCircle && other != null && other.hits.Count >= circleBusyHits;
        Stem vocals = System.Array.Find(stems, s => s.name == "vocals");
        bool vocalsBusy = vocalsFigure8 && vocals != null && vocals.hits.Count >= circleBusyHits;
        if (!circling && (otherBusy || vocalsBusy) && noteTime >= shapeReadyAt)
        {
            circling = true;
            circleIndex = 0;
            nextCircleTime = noteTime;
            if (!otherBusy)
                activeShape = OtherPattern.Figure8;
            else if (mixShapes)
            {
                activeShape = circlesSinceX >= circlesPerX ? OtherPattern.X : OtherPattern.Circle;
                circlesSinceX = activeShape == OtherPattern.X ? 0 : circlesSinceX + 1;
            }
            else activeShape = otherPattern;
            if (activeShape == OtherPattern.X)
            {
                if (!firstX) xLeftFirst = !xLeftFirst;
                firstX = false;
                shapeLength = 2 * Mathf.Max(2, xStepsPerLine);
            }
            else if (activeShape == OtherPattern.Figure8)
            {
                shapeLength = figure8Steps;
            }
            else
            {
                if (alternateDirection == CircleAlternate.EveryCircle && !firstCircle) counterclockwise = !counterclockwise;
                firstCircle = false;
                shapeLength = alternateDirection == CircleAlternate.EveryLap ? circleSteps * 2 : circleSteps;
            }
            if (logCircle) Debug.Log($"AsteroidChoreography: {activeShape} start at {noteTime:0.00}s ({shapeLength} asteroids, {(activeShape == OtherPattern.X ? (xLeftFirst ? "from left" : "from right") : (counterclockwise ? "counterclockwise" : "clockwise"))}).", this);
        }

        if (circling)
        {
            if (noteTime >= nextCircleTime)
            {
                Vector2 point;
                if (activeShape == OtherPattern.X)
                    point = XPoint(circleIndex);
                else if (activeShape == OtherPattern.Figure8)
                    point = Figure8Point(circleIndex);
                else
                {
                    // A full lap is done: turn around and head back the other way from the same point.
                    if (alternateDirection == CircleAlternate.EveryLap && circleIndex > 0 && circleIndex % circleSteps == 0)
                        counterclockwise = !counterclockwise;
                    point = CirclePoint(circleIndex);
                }
                circleIndex++;
                spawner.SpawnInArea(noteTime, new Rect(point.x, point.y, 0f, 0f), true, shapesIgnoreCap);
                lastAnySpawn = noteTime;
                nextCircleTime = noteTime + Mathf.Max(0.05f, circleSpacing * circleScale);

                if (circleIndex >= shapeLength)
                {
                    circling = false;
                    shapeReadyAt = noteTime + shapeCooldown;
                    if (logCircle) Debug.Log($"AsteroidChoreography: {activeShape} done at {noteTime:0.00}s.", this);
                }
            }
        }
        else if (spawning != null)
        {
            bool rested = noteTime - spawning.lastSpawn >= minGapSeconds * gapScale;
            if (IsHit(spawning) && rested)
            {
                Rect area = Section(spawning.cell);
                for (int i = 0; i < count; i++)
                    if (TrySpawn(noteTime + i * burstSpacing, area, spawnAtCenter))
                        spawning.lastSpawn = noteTime;
            }
        }

        foreach (Stem stem in stems)
            stem.average = Mathf.Lerp(stem.average, stem.energy, Mathf.Clamp01(dt * averageSpeed));
    }

    /// <summary>
    /// Viewport point <paramref name="index"/> along the X: first diagonal from the top corner on the
    /// starting side down to the opposite bottom corner, then the other diagonal the same way.
    /// </summary>
    Vector2 XPoint(int index)
    {
        int steps = Mathf.Max(2, xStepsPerLine);
        int line = (index / steps) % 2;
        float t = (index % steps) / (float)(steps - 1);
        float lo = xEdgeMargin, hi = 1f - xEdgeMargin;
        // Line 0 starts on the starting side, line 1 on the other side; both go top to bottom.
        bool fromLeft = line == 0 ? xLeftFirst : !xLeftFirst;
        Vector2 start = new Vector2(fromLeft ? lo : hi, hi);
        Vector2 end = new Vector2(fromLeft ? hi : lo, lo);
        return Vector2.Lerp(start, end, t);
    }

    /// <summary>Viewport point on a sideways figure 8 (lemniscate) starting at the screen center.</summary>
    Vector2 Figure8Point(int index)
    {
        float t = (index % figure8Steps) / (float)figure8Steps * 2f * Mathf.PI;
        float halfWidth = 0.5f - figure8EdgeMargin;
        return new Vector2(0.5f + Mathf.Sin(t) * halfWidth, 0.5f + Mathf.Sin(2f * t) * figure8Height);
    }

    /// <summary>Viewport point <paramref name="index"/> steps counterclockwise around the circle.</summary>
    Vector2 CirclePoint(int index)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        float aspect = cam != null && cam.aspect > 0f ? cam.aspect : 16f / 9f;
        // Viewport y points up, so a growing angle turns counterclockwise on screen.
        // Counting each lap from 0 means a reversed lap starts where the last one ended.
        int step = index % circleSteps;
        float sign = counterclockwise ? 1f : -1f;
        float angle = (circleStartAngle + sign * step * 360f / circleSteps) * Mathf.Deg2Rad;
        if (circleFullScreen)
        {
            // Around the whole screen: an ellipse filling the viewport, inset by the edge margin.
            float r = 0.5f - circleEdgeMargin;
            return new Vector2(0.5f + Mathf.Cos(angle) * r, 0.5f + Mathf.Sin(angle) * r);
        }
        Vector2 point = circleCenter + new Vector2(Mathf.Cos(angle) * circleRadius / aspect, Mathf.Sin(angle) * circleRadius);
        return new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
    }

    /// <summary>Sets this run's spawn limits from <paramref name="difficulty"/>. Easy spawns far fewer asteroids.</summary>
    void ApplyDifficulty(Difficulty difficulty)
    {
        if (!savedSpawnerSettings)
        {
            baseMaxLive = spawner.maxLiveAsteroids;
            baseBigEveryN = spawner.bigEveryN;
            baseTurboEveryN = spawner.turboEveryN;
            savedSpawnerSettings = true;
        }

        bool easy = difficulty == Difficulty.Easy;
        bool hard = difficulty == Difficulty.Difficult;
        gapScale = easy ? easyGapMultiplier : hard ? hardGapMultiplier : 1f;
        circleScale = easy ? easyCircleSpacingMultiplier : hard ? hardCircleSpacingMultiplier : 1f;
        peakScale = hard ? hardPeakRatioMultiplier : 1f;
        minSpawnInterval = easy ? easyMinSpawnInterval : 0f;
        runBurstCount = easy ? easyBurstCount : hard ? hardBurstCount : burstCount;
        spawner.maxLiveAsteroids = easy ? easyMaxLiveAsteroids : hard ? hardMaxLiveAsteroids : baseMaxLive;
        spawner.bigEveryN = easy && easyNoBigAsteroids ? 0 : hard && hardBigEveryN > 0 ? hardBigEveryN : baseBigEveryN;
        spawner.turboEveryN = hard && hardTurboEveryN > 0 ? hardTurboEveryN : baseTurboEveryN;
        lastAnySpawn = float.NegativeInfinity;
    }

    /// <summary>Spawns unless the last asteroid was less than the run's minimum interval ago.</summary>
    bool TrySpawn(float time, Rect area, bool centered)
    {
        if (time - lastAnySpawn < minSpawnInterval) return false;
        if (!spawner.SpawnInArea(time, area, centered)) return false;
        lastAnySpawn = time;
        return true;
    }

    /// <summary>True when the stem's energy this frame is loud and jumps above its recent average.</summary>
    bool IsHit(Stem stem) =>
        stem.energy > 0f && stem.energy >= stem.peak * minRelativeEnergy && stem.energy > stem.average * peakRatio * peakScale;

    static float LastHit(Stem stem)
    {
        float last = float.NegativeInfinity;
        foreach (float t in stem.hits) last = t;
        return last;
    }

    /// <summary>Viewport rectangle (0-1) of a cell in the 3 x 2 grid.</summary>
    public Rect Section(Vector2Int cell)
    {
        const int columns = 3, rows = 2;
        int column = Mathf.Clamp(cell.x, 0, columns - 1);
        int row = Mathf.Clamp(cell.y, 0, rows - 1);
        float width = 1f / columns;
        float height = 1f / rows;
        return Rect.MinMaxRect(
            column * width + sectionMargin,
            row * height + sectionMargin,
            (column + 1) * width - sectionMargin,
            (row + 1) * height - sectionMargin);
    }
}
