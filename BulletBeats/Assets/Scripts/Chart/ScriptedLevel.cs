using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a hand-made chart from Resources/Charts: every asteroid, big asteroid and star happens at
/// the times in the file, the same on every playthrough. Each song has one chart per difficulty.
/// Each asteroid flies in one straight line at one speed, through its chart position on its beat
/// and on to the ship.
/// </summary>
public class ScriptedLevel : MonoBehaviour
{
    [Serializable] public class Note { public float t; public bool turbo; public float x, y; public float fly, cue; }
    [Serializable] public class Star { public float t; public string type; public string spot; }
    [Serializable] public class Move { public float t; public string kind; public string to; }
    [Serializable]
    public class Data
    {
        public float flyIn, cue;
        [Tooltip("Song second where the music starts fading out and the level then ends. 0 = play to the end of the audio.")]
        public float endTime, endFade = 1.5f;
        [Tooltip("Shifts the chart to line up with its audio. Missing = the shared default.")]
        public float offset = float.NaN;
        [Tooltip("Seconds the red edge warning starts before a big asteroid appears. Missing = the spawner's default. " +
                 "0 = no early glow at all: the edge only glows once the asteroid is locked on (the last cue seconds).")]
        public float bigWarn = float.NaN;
        public int healStreak, startLives;
        public bool canDie;
        public float perfectWindow, goodWindow;
        public Note[] notes;
        public float[] bigAsteroids;
        public Star[] stars;
        public Move[] moves;
    }

    public static ScriptedLevel Instance { get; private set; }
    /// <summary>Moves the whole chart later (+) to line up with the audio as played.</summary>
    public static float AudioOffsetSeconds = 0.05f;
    public static bool Verbose;

    public Data Chart { get; private set; }
    public int Dropped { get; private set; }

    struct Timed { public float at; public Action run; }
    class Flight { public ChartTarget target; public bool turned; }

    readonly List<Timed> events = new List<Timed>();
    readonly List<Flight> flights = new List<Flight>();
    int next;
    ChartTargetSpawner spawner;
    PowerUpSpawner powerUps;

    void Awake() => Instance = this;
    void OnDestroy()
    { 
        if (Instance == this) Instance = null; 
        if (spawner != null) spawner.ClearBigWarnings();
    }
    readonly List<int> bigWarnings = new List<int>();   // marker id per big asteroid, -1 if not shown

    public bool Load(string chartName, out List<ChartNote> shots)
    {
        shots = new List<ChartNote>();
        TextAsset file = Resources.Load<TextAsset>("Charts/" + chartName);
        if (file == null)
        {
            Debug.LogError($"ScriptedLevel: missing Resources/Charts/{chartName}.json", this);
            return false;
        }

        Chart = JsonUtility.FromJson<Data>(file.text);
        ApplyOffset(Chart, float.IsNaN(Chart.offset) ? AudioOffsetSeconds : Chart.offset);
        spawner = GetComponent<ChartTargetSpawner>();
        powerUps = FindFirstObjectByType<PowerUpSpawner>();
        if (powerUps != null) powerUps.enabled = false; // stars only come from the chart
        PlayerHealth health = FindFirstObjectByType<PlayerHealth>();
        if (health != null) health.UseStreakHealing(Chart.healStreak);
        if (health != null) health.SetChartLives(Chart.startLives, Chart.canDie);
        BeatClock clock = BeatClock.Instance;
        if (clock != null)
        {
            clock.perfectWindow = Chart.perfectWindow;
            clock.goodWindow = Chart.goodWindow;
        }

        foreach (Note n in Chart.notes)
        {
            Note note = n;
            shots.Add(new ChartNote(n.t, 0, NoteLane.Left));
            events.Add(new Timed { at = n.t - FlightOf(n), run = () => SpawnAsteroid(note) });
        }
        float warnLead = !float.IsNaN(Chart.bigWarn) ? Chart.bigWarn : spawner != null ? spawner.bigWarningSeconds : 3.5f;
        foreach (float big in Chart.bigAsteroids)
        {
            float time = big;
            int slot = bigWarnings.Count;
            bigWarnings.Add(-1);
            float appearAt = big - Chart.flyIn;
            float warnAt = Mathf.Max(0f, appearAt - warnLead);

            // Start the red edge warning a few seconds before the asteroid appears (never before the song starts)...
            if (spawner != null && warnLead > 0f && warnAt < appearAt)
                events.Add(new Timed { at = warnAt, run = () => ShowBigWarning(slot) });
            // ...and the asteroid hides it when it appears.
            events.Add(new Timed { at = appearAt, run = () => SpawnBig(time, slot) });
        }
        for (int i = 0; i < Chart.stars.Length; i++)
        {
            int index = i;
            events.Add(new Timed { at = Chart.stars[i].t - Chart.flyIn, run = () => SpawnStar(index) });
        }
        events.Sort((a, b) => a.at.CompareTo(b.at));
        return true;
    }

    /// <summary>Seconds this asteroid flies. The first few of a chart can be slower.</summary>
    /// <summary>False when the chart wants the red edge glow only during the lock-on, not while a big asteroid approaches.</summary>
    public bool EarlyBigGlow => Chart == null || float.IsNaN(Chart.bigWarn) || Chart.bigWarn > 0f;

    float FlightOf(Note n) => (n.fly > 0f ? n.fly : Chart.flyIn) * (n.turbo && spawner != null ? spawner.turboSlow : 1f);

    static void ApplyOffset(Data chart, float offset)
    {
        chart.endTime = chart.endTime > 0f ? chart.endTime + offset : 0f;
        foreach (Note n in chart.notes) n.t += offset;
        for (int i = 0; i < chart.bigAsteroids.Length; i++) chart.bigAsteroids[i] += offset;
        foreach (Star s in chart.stars) s.t += offset;
        foreach (Move m in chart.moves) m.t += offset;
    }

    void Update()
    {
        BeatClock clock = BeatClock.Instance;
        if (Chart == null || clock == null || !clock.IsMusicPlaying) return;

        float now = clock.SongTime;
        if (UpdateEnding(clock, now)) return;
        // Nothing is skipped when a frame is slow: a late spawn just starts a little way into its flight.
        while (next < events.Count && events[next].at <= now)
            events[next++].run();
        UpdateFlights(now);
        FollowShip();
    }

    bool ending;
    float fadeElapsed, startVolume;

    /// <summary>At the chart's end time the music fades out, then the level is won.</summary>
    /// <returns>True while fading, when nothing else should spawn.</returns>
    bool UpdateEnding(BeatClock clock, float now)
    {
        if (Chart.endTime <= 0f || clock.music == null) return false;
        if (!ending)
        {
            if (now < Chart.endTime) return false;
            ending = true;
            startVolume = clock.music.volume;
        }

        if (Time.timeScale > 0f) fadeElapsed += Time.unscaledDeltaTime;
        float fade = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.05f, Chart.endFade));
        clock.music.volume = startVolume * (1f - fade);
        if (fade >= 1f && GameFlow.Instance != null) GameFlow.Instance.EndLevel(true);
        return true;
    }

    void UpdateFlights(float now)
    {
        PlayerHealth ship = PlayerHealth.Instance;
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            Flight f = flights[i];
            if (f.target == null) { flights.RemoveAt(i); continue; }
            if (f.turned) continue;

            if (ship != null && now >= f.target.hitTime - Chart.cue)
            {
                // Heads for wherever the ship is when its warning starts, keeping its current position.
                f.turned = true;
                float done = Mathf.Clamp(1f - (f.target.hitTime - now) / f.target.ApproachSeconds, 0f, 0.95f);
                f.target.hitPoint = ship.transform.position;
                f.target.spawnPoint = (f.target.transform.position - done * f.target.hitPoint) / (1f - done);
            }
        }
    }

    /// <summary>x is measured in screen heights from the middle, so spacing is the same in any window shape.</summary>
    static Vector2 ViewportOf(Note n, Camera cam)
    {
        float x = 0.5f + n.x / Mathf.Max(0.1f, cam.aspect);
        return new Vector2(Mathf.Clamp(x, 0.06f, 0.94f), n.y);
    }

    /// <summary>The point at this camera depth on the straight line from the ship through <paramref name="hit"/>.</summary>
    static Vector3 OnLineFromShip(Camera cam, Vector3 hit, float depth)
    {
        Vector3 forward = cam.transform.forward, origin = cam.transform.position;
        Vector3 ship = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform.position : origin;
        float shipDepth = Vector3.Dot(ship - origin, forward);
        float hitDepth = Vector3.Dot(hit - origin, forward);
        if (hitDepth <= shipDepth + 0.01f) return hit + forward * (depth - hitDepth);
        return ship + (hit - ship) * ((depth - shipDepth) / (hitDepth - shipDepth));
    }

    void SpawnAsteroid(Note n)
    {
        Camera cam = DisplayCamera.Find();
        float now = BeatClock.Instance.SongTime;
        if (cam == null || spawner == null || now >= n.t) { Dropped++; return; }

        Vector2 v = ViewportOf(n, cam);
        Vector3 hit = cam.ViewportToWorldPoint(new Vector3(v.x, v.y, spawner.distanceFromCamera));
        Vector3 from = OnLineFromShip(cam, hit, spawner.PrepareSpawnDistance(cam));
        ChartNote note = new ChartNote(n.t, 0, v.x < 0.5f ? NoteLane.Left : NoteLane.Right);
        var kind = n.turbo ? ChartTargetSpawner.ScriptedKind.Turbo : ChartTargetSpawner.ScriptedKind.Normal;
        ChartTarget target = spawner.SpawnScripted(note, from, hit, FlightOf(n), n.cue > 0f ? n.cue : Chart.cue, kind);
        if (Verbose) Debug.Log($"UI L1 spawn shot t={n.t:F3} expected={n.t - FlightOf(n):F3} actual={now:F3} late={now - (n.t - FlightOf(n)):F3}");
    }

    void SpawnBig(float time, int slot)
    {
        if (slot >= 0 && slot < bigWarnings.Count && bigWarnings[slot] >= 0)
        {
            spawner.HideBigWarning(bigWarnings[slot]);
            bigWarnings[slot] = -1;
        }
        Camera cam = DisplayCamera.Find();
        PlayerHealth ship = PlayerHealth.Instance;
        float now = BeatClock.Instance.SongTime;
        if (cam == null || ship == null || spawner == null || now >= time) { Dropped++; return; }

        Vector3 hit = ship.transform.position;
        Vector3 v = cam.WorldToViewportPoint(hit);
        v.z = spawner.PrepareSpawnDistance(cam);
        ChartNote note = new ChartNote(time, 0, NoteLane.FarLeft);
        ChartTarget target = spawner.SpawnScripted(note, cam.ViewportToWorldPoint(v), hit, Chart.flyIn, Chart.cue, ChartTargetSpawner.ScriptedKind.Big);
        flights.Add(new Flight { target = target });
        if (Verbose) Debug.Log($"UI L1 spawn big t={time:F3} expected={time - Chart.flyIn:F3} actual={now:F3}");
    }

    void ShowBigWarning(int slot)
    {
        Camera cam = DisplayCamera.Find();
        PlayerHealth ship = PlayerHealth.Instance;
        if (cam == null || ship == null || spawner == null) return;

        // Same spot SpawnBig uses: where the ship is on screen right now.
        Vector3 v = cam.WorldToViewportPoint(ship.transform.position);
        bigWarnings[slot] = spawner.ShowBigWarning(new Vector2(v.x, v.y));
    }
    /// <summary>Keeps every showing "!" on the ship's spot, since that's where the big asteroid will appear.</summary>
    void FollowShip()
    {
        Camera cam = DisplayCamera.Find();
        PlayerHealth ship = PlayerHealth.Instance;
        if (cam == null || ship == null || spawner == null) return;

        Vector3 v = cam.WorldToViewportPoint(ship.transform.position);
        var viewport = new Vector2(v.x, v.y);
        foreach (int id in bigWarnings)
            if (id >= 0) spawner.MoveBigWarning(id, viewport);
    }

    void SpawnStar(int index)
    {
        Star s = Chart.stars[index];
        PlayerHealth ship = PlayerHealth.Instance;
        if (ship == null || powerUps == null) { Dropped++; return; }

        PowerUpPickup pickup = SpawnPowerUp(s.type, ship, SpotOf(s.spot));
        PickupStar star = pickup != null ? pickup.GetComponent<PickupStar>() : null;
        if (star != null) star.LandAt(s.t);
        if (Verbose) Debug.Log($"UI L1 spawn star t={s.t:F3} spot={s.spot} kind={(pickup != null ? pickup.kind.ToString() : "none")}");
    }

    PowerUpPickup SpawnPowerUp(string type, PlayerHealth ship, PlayerMovement.Spot at) => type switch
    {
        "slowmo" => powerUps.SpawnSlowMo(ship, at),
        "bomb" => powerUps.SpawnBomb(ship, at),
        _ => powerUps.SpawnShield(ship, at)
    };

    /// <summary>Resources/Charts name of a song's chart for this difficulty.</summary>
    public static string ChartName(string song, Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => song + "_easy",
        Difficulty.Difficult => song + "_hard",
        _ => song + "_medium"
    };

    public static PlayerMovement.Spot SpotOf(string key) => key switch
    {
        "W" => PlayerMovement.Spot.Center,
        "A" => PlayerMovement.Spot.Left,
        "D" => PlayerMovement.Spot.Right,
        _ => PlayerMovement.Spot.Bottom
    };
}
