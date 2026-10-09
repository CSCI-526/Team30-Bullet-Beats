using UnityEngine;

/// <summary>
/// Placeholder asteroid: flies so it reaches <see cref="hitPoint"/> at <see cref="hitTime"/>
/// on the song clock. Replace with the real Asteroid prefab once that PR merges.
/// </summary>
public class ChartTarget : MonoBehaviour, IShootable
{
    public float hitTime;
    public Vector3 hitPoint;
    public Vector3 spawnPoint;
    public int health = 3;
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

    LineRenderer ring;
    const int RingSegments = 48;
    const float RingMinRadius = 3f;
    const float RingMaxRadius = 14f;

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

    void Update()
    {
        if (!armed) return;

        BeatClock clock = BeatClock.Instance;
        float songTime = clock != null ? clock.SongTime : Time.time;
        float travel = Mathf.Clamp01((songTime - (hitTime - approachSeconds)) / approachSeconds);
        transform.position = PositionAlongApproach(travel);
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        UpdateRing(songTime);

        if (songTime >= hitTime)
            Destroy(gameObject);
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

        float approach = Mathf.Clamp01(untilHit / quarterSeconds);
        float radius = Mathf.Lerp(RingMinRadius, RingMaxRadius, approach);

        Camera cam = Camera.main;
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

    public void OnShot(PlayerBullet bullet)
    {
        health -= bullet.Damage;
        if (health <= 0) Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            Destroy(gameObject);
    }
}
