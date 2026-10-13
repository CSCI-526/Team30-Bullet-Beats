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
    bool passed;
    LineRenderer ring;
    const int RingSegments = 48;
    const float RingMinRadius = 3f;
    const float RingMaxRadius = 14f;

    void OnEnable() => active.Add(this);

    void OnDisable() => active.Remove(this);

    /// <summary>Read-only timing feedback; never claims, freezes, or destroys a target.</summary>
    public static BeatJudgment JudgeUnderCursor(Vector2 screenPoint, Camera cam, float songTime,
        float perfectWindow, float goodWindow)
    {
        if (cam == null) return BeatJudgment.Miss;

        float bestError = float.MaxValue;
        for (int i = 0; i < active.Count; i++)
        {
            ChartTarget candidate = active[i];
            if (!candidate.RingContains(screenPoint, cam, songTime)) continue;
            float error = Mathf.Abs(songTime - candidate.hitTime);
            if (error < bestError) bestError = error;
        }

        if (bestError <= perfectWindow) return BeatJudgment.Perfect;
        if (bestError <= goodWindow) return BeatJudgment.Good;
        return BeatJudgment.Miss;
    }

    public void OnShot(PlayerBullet bullet) => Destroy(gameObject);

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
        if (cam != null)
        {
            spawnDistance = Vector3.Distance(cam.transform.position, spawn);
            hitDistance = Vector3.Distance(cam.transform.position, hit);
        }
        armed = true;
        BuildRing();
    }

    /// <summary>Ring is visible for one quarter note. <paramref name="lead"/> is the full fly-in.</summary>
    public void SetLifetime(float quarter, float lead, Vector2 viewportPoint)
    {
        quarterSeconds = Mathf.Max(0.05f, quarter);
        approachSeconds = Mathf.Max(0.05f, lead);
        viewport = viewportPoint;
        hasViewport = true;
    }

    void OnDestroy()
    {
        if (hasViewport && ChartTargetSpawner.Instance != null)
            ChartTargetSpawner.Instance.ReleaseViewport(viewport);
        if (ring != null) Destroy(ring.gameObject);
    }

    bool RingContains(Vector2 screenPoint, Camera cam, float songTime)
    {
        if (!RingIsShowing(songTime, out float untilHit)) return false;

        Vector3 center = cam.WorldToScreenPoint(transform.position);
        if (center.z <= 0f) return false;

        Vector3 edge = cam.WorldToScreenPoint(transform.position + cam.transform.right * RingRadius(untilHit));
        float pixelRadius = Vector2.Distance(new Vector2(center.x, center.y), new Vector2(edge.x, edge.y));
        return Vector2.Distance(screenPoint, new Vector2(center.x, center.y)) <= pixelRadius;
    }

    bool RingIsShowing(float songTime, out float untilHit)
    {
        untilHit = hitTime - songTime;
        return armed && untilHit <= quarterSeconds && untilHit >= 0f;
    }

    float RingRadius(float untilHit)
    {
        float approach = Mathf.Clamp01(untilHit / quarterSeconds);
        return Mathf.Lerp(RingMinRadius, RingMaxRadius, approach);
    }

    void Update()
    {
        if (!armed) return;

        BeatClock clock = BeatClock.Instance;
        float songTime = clock != null ? clock.SongTime : Time.time;
        float travel = (songTime - (hitTime - approachSeconds)) / approachSeconds;
        transform.position = Vector3.LerpUnclamped(spawnPoint, hitPoint, Mathf.Max(0f, travel));
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        UpdateRing(songTime);

        if (songTime >= hitTime && !passed)
        {
            passed = true;
            PlayerHealth.Instance?.TakeDamage(1);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (ring != null) ring.enabled = false;
        }

        if (passed)
        {
            Camera cam = DisplayCamera.Find();
            if (cam == null) cam = Camera.main;
            if (cam == null || Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward) < -hitDistance)
                Destroy(gameObject);
        }
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

    void BuildRing()
    {
        var go = new GameObject("HitRing");
        ring = go.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = RingSegments;
        ring.widthMultiplier = 0.3f;
        ring.numCornerVertices = 2;
        ring.numCapVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) ring.material = new Material(shader);
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
    }

    void UpdateRing(float songTime)
    {
        if (ring == null) return;

        float untilHit = hitTime - songTime;
        bool visible = untilHit <= quarterSeconds && untilHit >= 0f;
        ring.enabled = visible;
        if (!visible) return;

        float radius = RingRadius(untilHit);

        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        Transform ringTransform = ring.transform;
        ringTransform.position = transform.position;
        if (cam != null)
            ringTransform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        ringTransform.localScale = Vector3.one * radius;

        Color color = RingColor(untilHit);
        ring.startColor = color;
        ring.endColor = color;
    }

    static Color RingColor(float untilHit)
    {
        BeatClock clock = BeatClock.Instance;
        float perfect = clock != null ? clock.perfectWindow : 0.07f;
        float good = clock != null ? clock.goodWindow : 0.15f;
        float abs = Mathf.Abs(untilHit);

        if (untilHit < -perfect) return new Color(1f, 0.25f, 0.25f);
        if (abs <= perfect) return new Color(1f, 0.92f, 0.35f);
        if (abs <= good) return new Color(1f, 0.55f, 0.15f);
        return new Color(0.35f, 0.85f, 1f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerHealth.Instance?.TakeDamage(1);
            Destroy(gameObject);
        }
    }
}
