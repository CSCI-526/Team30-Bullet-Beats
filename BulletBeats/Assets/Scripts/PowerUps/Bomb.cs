using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Clears every asteroid on screen. <see cref="Grant"/> arms it; it goes off on the next beat, so it
/// hits with the music. A ring spreads out from the ship to the screen edges, and each asteroid pops
/// as the ring reaches it on screen, with a small "+N" floating up where it was. Each one scores
/// <see cref="ScoreKeeper.bombPoints"/>, without touching the streak or giving a turbo's lives.
/// Granted by a <see cref="PowerUpPickup"/>. Builds its ring and labels from code, so it needs no art.
/// </summary>
public class Bomb : MonoBehaviour
{
    public Color color = new Color(1f, 0.42f, 0.15f);
    [Tooltip("Seconds the ring takes to reach the farthest screen corner.")]
    public float sweepSeconds = 0.4f;
    [Tooltip("Ring thickness on screen, in pixels.")]
    public float ringPixels = 10f;
    [Tooltip("Seconds a +N label floats before it's gone.")]
    public float labelSeconds = 0.8f;
    [Tooltip("How far a +N label floats up, in reference pixels (1080p).")]
    public float labelRise = 70f;

    [Tooltip("Goes off the moment it is collected instead of on the next beat. The tutorial turns this on.")]
    public bool goOffImmediately;

    /// <summary>True from pickup until the ring has finished.</summary>
    public bool Active => armed || sweeping;

    bool armed, sweeping;
    float goOffTime, sweepAge, maxRadius;
    readonly List<ChartTarget> toPop = new List<ChartTarget>();
    readonly List<(RectTransform rect, Text text, Vector2 start, float age)> labels = new List<(RectTransform, Text, Vector2, float)>();

    LineRenderer ring;
    GameObject canvasObject;
    RectTransform canvasRect;
    ScoreKeeper score;
    Font font;

    BeatClock Clock => BeatClock.Instance;
    float SongTime => Clock != null ? Clock.SongTime : Time.time;

    void Start()
    {
        score = FindFirstObjectByType<ScoreKeeper>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ring = BuildRing();
        BuildCanvas();
        FindShipCenter();
    }

    Vector3 localCenter;

    /// <summary>The ring starts at the ship's visible body; its origin sits off to the side of the mesh.</summary>
    void FindShipCenter()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        localCenter = found ? transform.InverseTransformPoint(bounds.center) : Vector3.zero;
    }

    void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
        if (canvasObject != null) Destroy(canvasObject);
    }

    /// <summary>Arms the bomb to go off on the next beat. Does nothing if one is already going.</summary>
    public void Grant()
    {
        if (Active) return;
        armed = true;
        goOffTime = SongTime;
        if (Clock != null && !goOffImmediately)
        {
            float beat = Clock.SecondsPerBeat;
            goOffTime = Clock.firstBeatOffset + Mathf.Ceil((SongTime - Clock.firstBeatOffset) / beat) * beat;
        }
    }

    void Update()
    {
        // Ends with the level, so nothing pops over the end screen.
        if (GameFlow.Instance != null && GameFlow.Instance.State != GameState.Playing) Cancel();

        if (armed && SongTime >= goOffTime) GoOff();
        if (sweeping) Sweep();
        UpdateLabels();
    }

    void GoOff()
    {
        armed = false;
        Camera cam = DisplayCamera.Find();
        if (cam == null) return;

        sweeping = true;
        sweepAge = 0f;
        // Far enough to reach every corner from wherever the ship is.
        maxRadius = Mathf.Sqrt(cam.pixelWidth * cam.pixelWidth + cam.pixelHeight * cam.pixelHeight);
        toPop.Clear();
        toPop.AddRange(ChartTarget.Active);
        ring.enabled = true;
    }

    void Sweep()
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) { EndSweep(); return; }

        sweepAge += Time.deltaTime; // stops with the pause menu
        float t = Mathf.Clamp01(sweepAge / Mathf.Max(0.01f, sweepSeconds));
        float radius = t * maxRadius;
        Vector3 center = transform.TransformPoint(localCenter);
        Vector2 centerScreen = cam.WorldToScreenPoint(center);

        // Pop each asteroid the ring has passed on screen (or that is behind the camera).
        for (int i = toPop.Count - 1; i >= 0; i--)
        {
            ChartTarget rock = toPop[i];
            if (rock == null) { toPop.RemoveAt(i); continue; }
            Vector3 screen = cam.WorldToScreenPoint(rock.transform.position);
            if (screen.z > 0f && Vector2.Distance(centerScreen, screen) > radius) continue;
            toPop.RemoveAt(i);
            if (!rock.DestroyByBomb()) continue;
            int points = score != null ? score.AddBombClear(rock) : 0;
            if (points > 0 && screen.z > 0f) AddLabel($"+{points}", screen);
        }

        DrawRing(cam, center, radius, 1f - t * t);
        if (t >= 1f) EndSweep();
    }

    void EndSweep()
    {
        sweeping = false;
        toPop.Clear();
        if (ring != null) ring.enabled = false;
    }

    void Cancel()
    {
        armed = false;
        if (sweeping) EndSweep();
    }

    // ------------------------------------------------------------------ ring

    /// <summary>A circle facing the camera at the ship's depth, sized so it's <paramref name="pixels"/> wide on screen.</summary>
    void DrawRing(Camera cam, Vector3 center, float pixels, float alpha)
    {
        float depth = Mathf.Max(0.1f, Vector3.Dot(center - cam.transform.position, cam.transform.forward));
        float worldPerPixel = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
        ring.transform.position = center;
        ring.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        ring.transform.localScale = Vector3.one * Mathf.Max(0.001f, pixels * worldPerPixel);
        ring.widthMultiplier = ringPixels * worldPerPixel;
        Color c = color;
        c.a = alpha;
        ring.startColor = ring.endColor = c;
    }

    LineRenderer BuildRing()
    {
        var go = new GameObject("BombRing");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        const int segments = 96;
        line.positionCount = segments;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        // Same unlit shader as the star and asteroid rings, which is known to be in the web build.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) line.material = new Material(shader);
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        line.enabled = false;
        return line;
    }

    // ------------------------------------------------------------------ +N labels

    void AddLabel(string message, Vector2 screen)
    {
        if (canvasRect == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        var text = new GameObject("BombPoints", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        RectTransform rect = text.rectTransform;
        rect.SetParent(canvasRect, false);
        rect.sizeDelta = new Vector2(160f, 50f);
        rect.anchoredPosition = local;
        text.font = font;
        text.fontSize = 34;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.85f, 0.3f);
        text.raycastTarget = false;
        text.text = message;
        labels.Add((rect, text, local, 0f));
    }

    void UpdateLabels()
    {
        for (int i = labels.Count - 1; i >= 0; i--)
        {
            var (rect, text, start, age) = labels[i];
            age += Time.unscaledDeltaTime;
            if (rect == null || age >= labelSeconds)
            {
                if (rect != null) Destroy(rect.gameObject);
                labels.RemoveAt(i);
                continue;
            }
            float t = age / labelSeconds;
            rect.anchoredPosition = start + Vector2.up * labelRise * t;
            Color c = text.color;
            c.a = 1f - t * t;
            text.color = c;
            labels[i] = (rect, text, start, age);
        }
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("BombUI", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        canvasRect = canvasObject.GetComponent<RectTransform>();
    }
}
