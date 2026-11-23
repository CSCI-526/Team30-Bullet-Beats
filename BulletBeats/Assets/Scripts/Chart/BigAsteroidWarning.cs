using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A red pulsing "!" shown at the screen spot where a big asteroid is about to appear.
/// ChartTargetSpawner shows and hides it. Builds its own overlay canvas, so it needs no scene setup.
/// Hidden whenever the game isn't in the Playing state.
/// </summary>
public class BigAsteroidWarning : MonoBehaviour
{
    public Color color = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Blinks per second.")]
    public float blinkHz = 2.5f;
    public int fontSize = 140;

    class Marker
    {
        public RectTransform rect;
        public Text text;
        public Outline outline;
        public Vector2 viewport;
        public bool inUse;
    }

    readonly List<Marker> markers = new List<Marker>();
    GameObject canvasObject;
    Font font;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasObject = new GameObject("BigAsteroidWarning", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    /// <summary>Shows a blinking "!" at a camera viewport position (0-1). Returns an id for <see cref="Hide"/>.</summary>
    public int Show(Vector2 viewport)
    {
        int id = markers.FindIndex(m => !m.inUse);
        if (id < 0)
        {
            markers.Add(MakeMarker());
            id = markers.Count - 1;
        }
        Marker marker = markers[id];
        marker.inUse = true;
        marker.viewport = viewport;
        marker.rect.gameObject.SetActive(true);
        return id;
    }

    public void Hide(int id)
    {
        if (id < 0 || id >= markers.Count) return;
        markers[id].inUse = false;
        markers[id].rect.gameObject.SetActive(false);
    }

    /// <summary>Moves an already shown marker to a new viewport position (0-1).</summary>
    public void Move(int id, Vector2 viewport)
    {
        if (id < 0 || id >= markers.Count || !markers[id].inUse) return;
        markers[id].viewport = viewport;
    }

    public void Clear()
    {
        for (int i = 0; i < markers.Count; i++) Hide(i);
    }

    Marker MakeMarker()
    {
        var go = new GameObject("Warning", typeof(RectTransform), typeof(Text), typeof(Outline));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        rect.sizeDelta = new Vector2(120f, fontSize * 1.3f);
        var text = go.GetComponent<Text>();
        text.text = "!";
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.color = color;
        var outline = go.GetComponent<Outline>();
        outline.effectDistance = new Vector2(3f, -3f);
        go.SetActive(false);
        return new Marker { rect = rect, text = text, outline = outline };
    }

    void LateUpdate()
    {
        GameFlow flow = GameFlow.Instance;
        bool show = flow == null || (flow.State == GameState.Playing && !flow.IsPaused);
        canvasObject.SetActive(show);
        if (!show) return;

        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * blinkHz * Mathf.PI * 2f);
        foreach (Marker m in markers)
        {
            if (!m.inUse) continue;
            Vector3 screen = cam.ViewportToScreenPoint(new Vector3(m.viewport.x, m.viewport.y, 0f));
            m.rect.position = new Vector2(screen.x, screen.y);

            Color c = color;
            c.a = Mathf.Lerp(0.2f, 1f, blink);
            m.text.color = c;
            m.outline.effectColor = new Color(0f, 0f, 0f, c.a);
            m.rect.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.2f, blink);
        }
    }
}