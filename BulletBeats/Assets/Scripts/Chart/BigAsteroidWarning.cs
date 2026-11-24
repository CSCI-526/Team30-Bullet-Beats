using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Warns the player that a big asteroid is coming, in two stages. Early: a slow red pulse on the screen
/// edges (driven by Show/Hide, which ScriptedLevel calls a few seconds ahead). Locked: once a big asteroid
/// is within lockSeconds of hitting, the pulse speeds up and the spot it will hit glows red.
/// Reads the live big asteroids, so it works on scripted and non-scripted levels. Builds its own overlay
/// canvas (below the HUD) and draws its sprites in code, so it needs no scene setup or art.
/// Hidden when paused, outside play and in the tutorial.
/// </summary>
public class BigAsteroidWarning : MonoBehaviour
{
    public enum CueMode { Both, EdgeOnly, SpotOnly }

    [Header("Which cues (F10 cycles them in the Editor and development builds)")]
    public CueMode mode = CueMode.Both;

    [Header("Timing")]
    [Tooltip("Seconds before impact when a big asteroid counts as locked on. Scripted levels use their chart's cue instead.")]
    public float lockSeconds = 0.8f;

    [Header("Look")]
    public Color color = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Edge pulse opacity before the lock and once locked on. Kept low so it never washes out the screen.")]
    [Range(0f, 1f)] public float earlyAlpha = 0.16f;
    [Range(0f, 1f)] public float lockAlpha = 0.34f;
    [Tooltip("Pulses per second. Keep at 3 or below.")]
    [Range(0.2f, 3f)] public float earlyHz = 0.8f;
    [Range(0.2f, 3f)] public float lockHz = 2f;
    [Tooltip("Size of the danger-spot glow, in pixels at 1080p.")]
    public float spotSize = 170f;

    static readonly PlayerMovement.Spot[] Spots =
        { PlayerMovement.Spot.Center, PlayerMovement.Spot.Left, PlayerMovement.Spot.Bottom, PlayerMovement.Spot.Right };

    readonly List<bool> shown = new List<bool>();
    readonly HashSet<PlayerMovement.Spot> danger = new HashSet<PlayerMovement.Spot>();
    readonly Dictionary<PlayerMovement.Spot, Image> rings = new Dictionary<PlayerMovement.Spot, Image>();
    GameObject canvasObject;
    Image edge;
    Sprite vignetteSprite, ringSprite;
    PlayerMovement movement;
    float level;   // 0 = off, 0.5 = early, 1 = locked

    void Awake()
    {
        canvasObject = new GameObject("BigAsteroidWarning", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;   // below the progress bar (88) and the health HUD (90)
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        vignetteSprite = BuildSprite(128, (x, y) =>
        {
            float m = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, m));
            return t * t;
        });
        ringSprite = BuildSprite(128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float outer = Mathf.Clamp01((1f - d) * 24f);
            float inner = Mathf.Clamp01((d - 0.78f) * 24f);
            return Mathf.Max(Mathf.Min(outer, inner), outer * 0.12f);
        });

        edge = new GameObject("EdgePulse", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        edge.rectTransform.SetParent(canvasObject.transform, false);
        edge.rectTransform.anchorMin = Vector2.zero;
        edge.rectTransform.anchorMax = Vector2.one;
        edge.rectTransform.offsetMin = edge.rectTransform.offsetMax = Vector2.zero;
        edge.sprite = vignetteSprite;
        edge.raycastTarget = false;
        edge.enabled = false;

        foreach (PlayerMovement.Spot spot in Spots)
        {
            var ring = new GameObject("DangerSpot", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            ring.rectTransform.SetParent(canvasObject.transform, false);
            ring.rectTransform.sizeDelta = Vector2.one * spotSize;
            ring.sprite = ringSprite;
            ring.raycastTarget = false;
            ring.gameObject.SetActive(false);
            rings[spot] = ring;
        }
    }

    void OnDestroy()
    {
        DestroySprite(vignetteSprite);
        DestroySprite(ringSprite);
        if (canvasObject != null) Destroy(canvasObject);
    }

    static void DestroySprite(Sprite sprite)
    {
        if (sprite == null) return;
        Destroy(sprite.texture);
        Destroy(sprite);
    }

    // ---------------------------------------------------------------- early stage (called by the spawner and ScriptedLevel)

    /// <summary>An early warning is on (a big asteroid is about to appear). The position is no longer used. Returns an id for <see cref="Hide"/>.</summary>
    public int Show(Vector2 viewport)
    {
        int id = shown.IndexOf(false);
        if (id < 0)
        {
            shown.Add(true);
            return shown.Count - 1;
        }
        shown[id] = true;
        return id;
    }

    /// <summary>Kept so existing callers still work; the new cues need no position.</summary>
    public void Move(int id, Vector2 viewport) { }

    public void Hide(int id)
    {
        if (id >= 0 && id < shown.Count) shown[id] = false;
    }

    public void Clear()
    {
        for (int i = 0; i < shown.Count; i++) shown[i] = false;
    }

    // ---------------------------------------------------------------- per frame

    void LateUpdate()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
        {
            mode = (CueMode)(((int)mode + 1) % 3);
            Debug.Log("Big asteroid cue: " + mode);
        }
#endif
        GameFlow flow = GameFlow.Instance;
        bool on = flow == null || (flow.State == GameState.Playing && !flow.IsPaused && !flow.IsTutorial);
        canvasObject.SetActive(on);
        if (!on)
        {
            level = 0f;
            return;
        }

        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (movement == null) movement = FindFirstObjectByType<PlayerMovement>();

        BeatClock clock = BeatClock.Instance;
        float songTime = clock != null ? clock.SongTime : Time.time;
        float lockAt = LockWindow();

        bool early = shown.Contains(true);
        bool locked = false;
        danger.Clear();
        foreach (ChartTarget t in ChartTarget.Active)
        {
            if (t == null || !t.IsBig) continue;
            float untilHit = t.hitTime - songTime;
            if (untilHit < -0.1f) continue;
            if (untilHit <= lockAt)
            {
                locked = true;
                if (movement != null) danger.Add(SpotAimedAt(t, cam));
            }
            else early = true;   // spawned but not locked on yet: keep the slow pulse going
        }

        UpdateEdge(early, locked);
        UpdateSpots(cam, locked);
    }

    float LockWindow()
    {
        ScriptedLevel scripted = ScriptedLevel.Instance;
        return scripted != null && scripted.Chart != null && scripted.Chart.cue > 0f ? scripted.Chart.cue : lockSeconds;
    }

    void UpdateEdge(bool early, bool locked)
    {
        bool useEdge = mode != CueMode.SpotOnly;
        float target = !useEdge ? 0f : locked ? 1f : early ? 0.5f : 0f;
        level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * 3f);

        float alphaMax = level <= 0.5f
            ? earlyAlpha * (level / 0.5f)
            : Mathf.Lerp(earlyAlpha, lockAlpha, (level - 0.5f) / 0.5f);
        float hz = Mathf.Lerp(earlyHz, lockHz, Mathf.InverseLerp(0.5f, 1f, level));
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * hz * Mathf.PI * 2f);

        Color c = color;
        c.a = alphaMax * Mathf.Lerp(0.35f, 1f, pulse);
        edge.color = c;
        edge.enabled = c.a > 0.003f;
    }

    void UpdateSpots(Camera cam, bool locked)
    {
        bool useSpot = mode != CueMode.EdgeOnly && locked && movement != null;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * lockHz * Mathf.PI * 2f);
        Vector3 bodyOffset = useSpot ? BodyCenter() - movement.transform.position : Vector3.zero;

        foreach (PlayerMovement.Spot spot in Spots)
        {
            Image ring = rings[spot];
            bool show = useSpot && danger.Contains(spot);
            ring.gameObject.SetActive(show);
            if (!show) continue;

            Vector3 p = cam.WorldToScreenPoint(movement.WorldPositionOf(spot) + bodyOffset);
            ring.rectTransform.position = new Vector2(p.x, p.y);
            Color c = color;
            c.a = Mathf.Lerp(0.5f, 1f, pulse);
            ring.color = c;
            ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.08f, pulse);
        }
    }

    /// <summary>The spot an asteroid will hit: where its flight line crosses the ship's depth, then the nearest spot.</summary>
    PlayerMovement.Spot SpotAimedAt(ChartTarget t, Camera cam)
    {
        Vector3 ship = movement.transform.position;
        Vector3 fwd = cam.transform.forward;
        float spawnDepth = Vector3.Dot(t.spawnPoint - ship, fwd);
        float hitDepth = Vector3.Dot(t.hitPoint - ship, fwd);

        Vector3 aimed = t.hitPoint;
        if (Mathf.Abs(hitDepth - spawnDepth) > 0.01f)
            aimed = t.spawnPoint + (t.hitPoint - t.spawnPoint) * (-spawnDepth / (hitDepth - spawnDepth));

        PlayerMovement.Spot best = PlayerMovement.Spot.Bottom;
        float bestDistance = float.MaxValue;
        foreach (PlayerMovement.Spot spot in Spots)
        {
            float d = (movement.WorldPositionOf(spot) - aimed).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = spot;
            }
        }
        return best;
    }

    /// <summary>Centre of the ship's visible body, which sits off the ship's origin.</summary>
    Vector3 BodyCenter()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (MeshRenderer r in movement.GetComponentsInChildren<MeshRenderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        return found ? bounds.center : movement.transform.position;
    }

    /// <summary>A white sprite whose opacity comes from <paramref name="alphaAt"/> (points in -1..1).</summary>
    static Sprite BuildSprite(int size, System.Func<float, float, float> alphaAt)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x + 0.5f) / size * 2f - 1f;
            float ny = (y + 0.5f) / size * 2f - 1f;
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(alphaAt(nx, ny))));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}