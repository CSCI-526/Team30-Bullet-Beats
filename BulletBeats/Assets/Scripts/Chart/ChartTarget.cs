using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeholder asteroid: flies so it reaches <see cref="hitPoint"/> at <see cref="hitTime"/>
/// on the song clock. A physical bullet hit destroys it; the shrinking ring is a timing cue.
/// If it reaches its beat without being shot, or touches the ship, the ship takes a hit.
/// Replace with the real Asteroid prefab once that PR merges.
/// </summary>
public class ChartTarget : MonoBehaviour, IShootable
{
    static readonly List<ChartTarget> active = new List<ChartTarget>();

    public float hitTime;
    public Vector3 hitPoint;
    public Vector3 spawnPoint;
    public NoteLane lane;

    float spinSpeed;
    Vector3 spinAxis;
    float quarterSeconds = 0.75f;
    float approachSeconds = 6f;
    float spawnDistance = 160f;
    float hitDistance = 55f;
    Vector2 viewport;
    bool hasViewport;
    bool armed;

    [HideInInspector] public float shipHitRadius = 3f;
    Vector3 lastPosition;
    bool passed;
    int hitsLeft = 1;
    int healOnDestroy;
    bool dying;
    
    [Header("Lock-on brackets")]
    [Tooltip("Static corner ticks are this much bigger than the asteroid on screen.")]
    [Min(1f)] public float tickPadding = 1.1f;
    [Tooltip("Brackets start at this multiple of the tick size and close in to 1.")]
    [Min(1.05f)] public float bracketStartScale = 1.7f;
    [Tooltip("Beats before the hit that brackets show (capped by the flight time).")]
    [Min(0.25f)] public float bracketBeats = 1f;
    [Range(0.1f, 0.5f)] public float armLength = 0.28f;
    public float tickPixels = 2f;
    public float bracketPixels = 3.5f;
    public float glowPixels = 7f;
    public Color tickColor = new Color(1f, 1f, 1f, 0.8f);
    public Color bracketColor = new Color(0.36f, 0.88f, 1f, 1f);
    public Color glowColor = new Color(1f, 0.82f, 0.34f, 1f);
    [Tooltip("Brightness of the second asteroid's brackets when several arrive together.")]
    [Range(0f, 1f)] public float secondaryAlpha = 0.5f;

    Transform lockRoot;
    readonly LineRenderer[] tickLines = new LineRenderer[4];
    readonly LineRenderer[] bracketLines = new LineRenderer[4];
    static Material lineMaterial; 
    static PlayerShooter shooterRef;
    /// <summary>A locked rock keeps the lock this much longer, so it doesn't flicker at the edge or between two rocks.</summary>
    const float LockHoldFactor = 1.3f;
    /// <summary>A rock whose ring is closing counts as this much closer: it's the one to hit now.</summary>
    const float RingLockPreference = 0.6f;
    float baseRadius = 1f;
    float baseScale = 1f;
    bool isBig;
    bool isTurbo;
    Color bigWarnColor;
    /// <summary>Keeps a big asteroid solid red instead of pulsing to white (the tutorial uses this).</summary>
    [HideInInspector] public bool steadyWarning;
    Renderer[] bigRenderers;

    /// <summary>Indestructible: it has to be dodged.</summary>
    public bool IsBig => isBig;
    /// <summary>Shots still needed to break it (turbo asteroids start with more than one).</summary>
    public int HitsLeft => hitsLeft;

    /// <summary>How an asteroid's life ended. Each asteroid ends exactly one way.</summary>
    public enum Outcome { Perfect, Good, Dodged, Shielded, Bombed, Hit }

    /// <summary>Raised once per asteroid when it ends, for analytics.</summary>
    public static event System.Action<ChartTarget, Outcome> Ended;

    /// <summary>"normal", "turbo" or "big".</summary>
    public string TypeName => isBig ? "big" : isTurbo ? "turbo" : "normal";
    /// <summary>Seconds from this asteroid's beat to the first shot at it (negative = early). NaN if never shot at.</summary>
    public float FirstShotOffset { get; private set; } = float.NaN;
    /// <summary>Shots fired at this asteroid.</summary>
    public int Tries { get; private set; }
    /// <summary>True if the first shot at it came during slow motion, when song-time offsets are stretched.</summary>
    public bool FirstShotInSlowMo { get; private set; }

    BeatJudgment lastJudgment = BeatJudgment.Miss;
    bool ended;

    /// <summary>Called by <see cref="PlayerShooter"/> for every shot judged against this asteroid.</summary>
    public void RecordShot(BeatJudgment judgment, float offset)
    {
        lastJudgment = judgment;
        Tries++;
        if (Tries > 1) return;
        FirstShotOffset = offset;
        PlayerHealth ship = PlayerHealth.Instance;
        FirstShotInSlowMo = ship != null && ship.TryGetComponent(out SlowMo slowMo) && slowMo.Active;
    }

    void End(Outcome outcome)
    {
        if (ended) return;
        ended = true;
        Ended?.Invoke(this, outcome);
    }

    /// <summary>Ends the asteroid after it touched the ship. <paramref name="landed"/> is TakeDamage's result.</summary>
    void EndOnShip(bool landed)
    {
        if (!landed) End(Outcome.Dodged);
        else End(PlayerHealth.Instance != null && PlayerHealth.Instance.LastHitAbsorbed ? Outcome.Shielded : Outcome.Hit);
    }

    /// <summary>Every asteroid in flight (read only).</summary>
    public static IReadOnlyList<ChartTarget> Active => active;

    void OnEnable() => active.Add(this);

    void OnDisable() => active.Remove(this);

    /// <summary>Removes the asteroid with no damage, judgment or turbo heal. Works on big ones too.</summary>
    /// <returns>False if it was already on its way out.</returns>
    public bool DestroyByBomb()
    {
        if (dying || !armed) return false;
        dying = true;
        End(Outcome.Bombed);
        Destroy(gameObject);
        return true;
    }

    /// <summary>
    /// The rock a shot from <paramref name="cursor"/> goes to: the closest one within
    /// <paramref name="reachPixels"/> of it, or whose visible ring contains it. Null if there is none.
    /// Rocks that can't be destroyed or have flown past the ship are never picked.
    /// </summary>
    /// <param name="current">The rock locked last frame, which is harder to lose.</param>
    public static ChartTarget FindAimTarget(Vector2 cursor, Camera cam, float songTime, float reachPixels, ChartTarget current)
    {
        if (cam == null) return null;

        ChartTarget best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < active.Count; i++)
        {
            ChartTarget candidate = active[i];
            if (!candidate.CanBeLocked(cam) || !candidate.TryGetScreenCircle(cam, out Vector2 center, out float radius))
                continue;

            bool held = candidate == current;
            float hold = held ? LockHoldFactor : 1f;
            float distance = Vector2.Distance(cursor, center);
            float gap = Mathf.Max(0f, distance - radius);
            bool showing = candidate.RingIsShowing(songTime, out float untilHit);
            bool inRing = showing && distance <= candidate.ScreenPixels(cam, center, candidate.RingRadius(untilHit, cam)) * hold;
            if (gap > reachPixels * hold && !inRing) continue;

            float score = gap / hold * (showing ? RingLockPreference : 1f);
            if (score < bestScore || (held && score <= bestScore))
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Where the rock is on screen and how big it looks, in pixels.</summary>
    /// <returns>False if it is behind the camera.</returns>
    public bool TryGetScreenCircle(Camera cam, out Vector2 center, out float radius)
    {
        Vector3 point = cam.WorldToScreenPoint(transform.position);
        center = point;
        radius = 0f;
        if (point.z <= 0f) return false;
        radius = ScreenPixels(cam, center, baseRadius * transform.lossyScale.x / baseScale);
        return true;
    }

    float ScreenPixels(Camera cam, Vector2 center, float worldRadius)
    {
        Vector3 edge = cam.WorldToScreenPoint(transform.position + cam.transform.right * worldRadius);
        return Vector2.Distance(center, edge);
    }

    bool CanBeLocked(Camera cam)
    {
        if (!armed || isBig || dying) return false;
        PlayerHealth ship = PlayerHealth.Instance;
        return ship == null || Vector3.Dot(transform.position - ship.transform.position, cam.transform.forward) > 0f;
    }

    public void OnShot(PlayerBullet bullet)
    {
        if (isBig) return; // indestructible: force player to dodge
        if (dying) return;
        // A shot off the beat (outside the Good window) doesn't break it; it keeps flying.
        if (bullet != null && bullet.Judgment == BeatJudgment.Miss) return;

        hitsLeft--;
        if(hitsLeft > 0)
        {
            transform.localScale *= 0.9f;
            return;
        }

        dying = true;
        End(lastJudgment == BeatJudgment.Perfect ? Outcome.Perfect : Outcome.Good);
        if (healOnDestroy > 0) PlayerHealth.Instance?.Heal(healOnDestroy);
        Destroy(gameObject);
    }

    public void Arm(ChartNote note, Vector3 spawn, Vector3 hit)
    {
        hitTime = note.time;
        lane = note.lane;
        spawnPoint = spawn;
        hitPoint = hit;
        transform.position = spawn;
        spinAxis = Random.onUnitSphere;
        spinSpeed = Random.Range(40f, 120f);
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam != null && PlayerHealth.Instance != null)
        {
            Vector3 ship = PlayerHealth.Instance.transform.position;
            Vector3 fwd = cam.transform.forward;
            float spawnDepth = Vector3.Dot(spawn - ship, fwd);
            float hitDepth = Vector3.Dot(hit - ship, fwd);
            if (spawnDepth > 0.01f && hitDepth > 0f)
            {
                hitPoint = Vector3.Lerp(ship, spawn, hitDepth / spawnDepth);
                hit = hitPoint;
            }
        }
        if (cam != null)
        {
            spawnDistance = Vector3.Distance(cam.transform.position, spawn);
            hitDistance = Vector3.Distance(cam.transform.position, hit);
        }
        baseScale = Mathf.Max(0.01f, transform.lossyScale.x);
        baseRadius = 0.5f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            baseRadius = Mathf.Max(baseRadius, r.localBounds.extents.magnitude * r.transform.lossyScale.x);
        armed = true;
        BuildLockOn();
    }

    /// <summary>Ring is visible for one quarter note. <paramref name="lead"/> is the full fly-in.</summary>
    public void SetLifetime(float quarter, float lead, Vector2 viewportPoint)
    {
        quarterSeconds = Mathf.Max(0.05f, quarter);
        approachSeconds = Mathf.Max(0.05f, lead);
        viewport = viewportPoint;
        hasViewport = true;
    }

    public void MakeBig(float scale, float hitRadius, Color warnColor)
    {
        isBig = true;
        bigWarnColor = warnColor;
        shipHitRadius = hitRadius;
        transform.localScale *= scale;
        bigRenderers = GetComponentsInChildren<Renderer>();
    }
    public void MakeTurbo(int hits, int heal, float scale, Color tint)
    {
        isTurbo = true;
        hitsLeft = hits;
        healOnDestroy = heal;
        transform.localScale *= scale;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.material.color = tint;
    }

    void OnDestroy()
    {
        if (hasViewport && ChartTargetSpawner.Instance != null)
            ChartTargetSpawner.Instance.ReleaseViewport(viewport);
        if (lockRoot != null) Destroy(lockRoot.gameObject);
    }

    float ShowSeconds => Mathf.Min(quarterSeconds * bracketBeats, approachSeconds);
    float PerfectWindow { get { BeatClock c = BeatClock.Instance; return c != null ? c.perfectWindow : 0.12f; } }
    float GoodWindow { get { BeatClock c = BeatClock.Instance; return c != null ? c.goodWindow : 0.25f; } }

    bool RingIsShowing(float songTime, out float untilHit)
    {
        untilHit = hitTime - songTime;
        return armed && !isBig && untilHit <= ShowSeconds && untilHit >= -GoodWindow;
    }

    /// <summary> Half-size of the static corner ticks in world units. Scaled by distance so they stay the same size on screen. </summary>
    float TickHalfWorld (Camera cam)
    {
        float dist = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
        float atHit = baseRadius * (transform.lossyScale.x / baseScale) * tickPadding;
        return atHit * dist / Mathf.Max(0.01f, hitDistance);
    }

    float RingRadius(float untilHit, Camera cam)
    {
        float closing = Mathf.Clamp01(untilHit / Mathf.Max(0.01f, ShowSeconds));
        return TickHalfWorld(cam) * Mathf.Lerp(1f, bracketStartScale, closing);
    }

    void Update()
    {
        if (!armed) return;

        BeatClock clock = BeatClock.Instance;
        float songTime = clock != null ? clock.SongTime : Time.time;
        float travel = (songTime - (hitTime - approachSeconds)) / approachSeconds;

        lastPosition = transform.position;
        transform.position = Vector3.LerpUnclamped(spawnPoint, hitPoint, Mathf.Max(0f, travel));
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        UpdateLockOn(songTime);
        if (isBig) PulseWarning(songTime);

        //<<<<<<< Updated upstream
        //if (songTime >= hitTime && !passed)
        //{
        //    passed = true;
        //    PlayerHealth.Instance?.TakeDamage(1);
        //    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        //    if (ring != null) ring.enabled = false;
        //}

        //if (passed)
        //{
        //    Camera cam = DisplayCamera.Find();
        //    if (cam == null) cam = Camera.main;
        //    if (cam == null || Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward) < -hitDistance)
        //        Destroy(gameObject);
        ////=======
        //        // Damage only when the planet actually reaches the ship.
        // A BasicShield on the ship absorbs this hit inside TakeDamage.
        PlayerHealth ship = PlayerHealth.Instance;
        if (ship != null && DistanceToSegment(ship.transform.position, lastPosition, transform.position) <= shipHitRadius * PlayerMovement.SizeScale)
        {
            EndOnShip(ship.TakeDamage(1));
            Destroy(gameObject);
            return;
//>>>>>>> Stashed changes
        }

        if (songTime >= hitTime && !passed)
        {
            passed = true;
        }

        if (passed)
        {
            Camera cam = DisplayCamera.Find();
            if (cam == null) cam = Camera.main;
            if (cam == null || Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward) < -hitDistance)
            {
                End(Outcome.Dodged);
                Destroy(gameObject);
            }
        }
    }

    void PulseWarning(float songTime)
    {
        BeatClock clock = BeatClock.Instance;
        float beat = clock != null ? clock.SecondsPerBeat : 0.5f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(songTime / Mathf.Max(0.05f, beat) * Mathf.PI * 2f);
        Color c = steadyWarning ? bigWarnColor : Color.Lerp(bigWarnColor, Color.white, pulse);
        foreach (Renderer r in bigRenderers)
             if (r != null) r.material.color = c;
    }
    public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 0.0001f) return Vector3.Distance(point, a);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
        return Vector3.Distance(point, a + ab * t);
    }

    Vector3 PositionAlongApproach(float travel)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null || spawnDistance <= hitDistance)
            return Vector3.LerpUnclamped(spawnPoint, hitPoint, travel);

        // Linear in apparent size, so the planet creeps closer from the first frame
        // instead of sitting far away and rushing in at the end.
        float inv = Mathf.Lerp(1f / spawnDistance, 1f / Mathf.Max(0.01f, hitDistance), travel);
        Vector3 dir = hitPoint - cam.transform.position;
        if (dir.sqrMagnitude < 0.0001f) dir = cam.transform.forward;
        return cam.transform.position + dir.normalized / inv;
    }

    static Material LineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        lineMaterial = new Material(shader);
        return lineMaterial;
    }

    static float WorldPerPixel(Camera cam, float dist)
    {
        float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return h / Mathf.Max(1, cam.pixelHeight);
    }

    void BuildLockOn()
    {
        lockRoot = new GameObject("LockOn").transform;
        for (int i = 0; i < 4; i++)
        {
            tickLines[i] = MakeCorner(i);
            bracketLines[i] = MakeCorner(i);
        }
        lockRoot.gameObject.SetActive(false);
    }

    LineRenderer MakeCorner(int index)
    {
        var go = new GameObject("Corner" + index);
        go.transform.SetParent(lockRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 3;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = LineMaterial();
        return lr;
    }

    int LockOnRank(float songTime, float good)
    {
        int rank = 0;
        for (int i = 0; i < active.Count; i++)
        {
            ChartTarget other = active[i];
            if (other == this || !other.armed || other.isBig) continue;
            if (other.hitTime >= hitTime) continue;
            if (other.hitTime - songTime < -good) continue;
            rank++;
        }
        return rank;
    }

    void UpdateLockOn(float songTime)
    {
        if (lockRoot == null) return;

        bool showing = RingIsShowing(songTime, out float untilHit);
        lockRoot.gameObject.SetActive(showing);
        if(!showing) return;

        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        float dist = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
        if (dist <= 0.01f) { lockRoot.gameObject.SetActive(false); return; }

        lockRoot.position = transform.position;
        lockRoot.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        float perfect = PerfectWindow, good = GoodWindow;
        bool landed = Mathf.Abs(untilHit) <= perfect;

        if (shooterRef == null) shooterRef = FindFirstObjectByType<PlayerShooter>();
        ChartTarget locked = shooterRef != null ? shooterRef.LockedTarget : null;
        int rank = locked == this ? 0 : Mathf.Max(locked != null ? 1 : 0, LockOnRank(songTime, good));
        float bracketFade = rank == 0 ? 1f : rank == 1 ? secondaryAlpha : 0f;
        float tickFade = rank <= 1 ? 1f : 0.35f;

        float show = ShowSeconds;
        float fadeIn = Mathf.Clamp01((show - untilHit) / (show * 0.2f));
        float fadeOut = untilHit < 0f ? Mathf.Clamp01(1f + untilHit / good) : 1f;
        float a = fadeIn * fadeOut;

        float px = WorldPerPixel(cam, dist);
        SetCorners(tickLines, TickHalfWorld(cam), landed ? glowPixels : tickPixels, px, landed ? glowColor : tickColor, a * tickFade);
        SetCorners(bracketLines, RingRadius(untilHit, cam), landed ? glowPixels : bracketPixels, px, landed ? glowColor : bracketColor, a * bracketFade);
    }

    void SetCorners(LineRenderer[] lines, float half, float pixels, float WorldPerPixel, Color color, float alpha)
    {
        color.a *= alpha;
        float arm = armLength * 2f * half;
        for (int i = 0; i < lines.Length; i++)
        {
            float sx = (i == 0 || i == 3) ? -1f : 1f;
            float sy = i < 2 ? 1f : -1f;
            LineRenderer lr = lines[i];
            lr.enabled = color.a > 0.01f;
            lr.widthMultiplier = pixels * WorldPerPixel;
            lr.startColor = color;
            lr.endColor = color;
            lr.SetPosition(0, new Vector3(sx * half - sx * arm, sy * half, 0f));
            lr.SetPosition(1, new Vector3(sx * half, sy * half, 0f));
            lr.SetPosition(2, new Vector3(sx * half, sy * half - sy * arm, 0f));
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (PlayerHealth.Instance != null) EndOnShip(PlayerHealth.Instance.TakeDamage(1));
            Destroy(gameObject);
        }
    }
}
