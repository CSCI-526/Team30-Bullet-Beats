using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Frost in the four screen corners while Freeze (<see cref="SlowMo"/>) is on: a soft icy glow and a few
/// detailed snowflakes that pop in one after another, twinkle gently, and fade out when Freeze ends. Sits
/// on top of SlowMo's blue screen tint. Looks only: it just reads SlowMo. Builds its own canvas and draws
/// the snowflake in code, so it needs no art. Added by <see cref="PowerUpBanners"/>.
/// </summary>
public class FreezeFrost : MonoBehaviour
{
    public Color frostColor = new Color(0.75f, 0.93f, 1f, 0.9f);
    public Color glowColor = new Color(0.55f, 0.85f, 1f, 0.32f);
    [Tooltip("Seconds between one snowflake popping in and the next.")]
    public float popStagger = 0.08f;
    [Tooltip("Seconds the frost takes to fade when Freeze ends.")]
    public float fadeSeconds = 0.4f;

    [Tooltip("Snowflakes per corner. They are thick at the corner and thin out toward the middle.")]
    public int flakesPerCorner = 26;
    [Tooltip("How far from its corner a snowflake can reach (pixels at 1080p).")]
    public float reach = 620f;

    static Sprite flakeSprite, glowSprite;

    GameObject canvasObject;
    CanvasGroup group;
    RectTransform[] flakes;
    float[] phase, delay;
    bool wasActive;
    float startedAt, endedAt = float.NegativeInfinity;

    void Start() => Build();

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (canvasObject == null) return;
        PlayerHealth ship = PlayerHealth.Instance;
        SlowMo slowMo = ship != null ? ship.GetComponent<SlowMo>() : null;
        bool active = slowMo != null && slowMo.Active;
        float now = Time.unscaledTime;

        if (active && !wasActive) startedAt = now;
        if (!active && wasActive) endedAt = now;
        wasActive = active;

        float alpha = active ? 1f : 1f - Mathf.Clamp01((now - endedAt) / Mathf.Max(0.01f, fadeSeconds));
        bool show = alpha > 0f;
        if (canvasObject.activeSelf != show) canvasObject.SetActive(show);
        if (!show) return;
        group.alpha = alpha;

        for (int i = 0; i < flakes.Length; i++)
        {
            // Pop in one after another (overshoot, then settle), then a slow twinkle.
            float p = Mathf.Clamp01((now - startedAt - delay[i]) / 0.25f);
            float pop = p < 0.7f ? Mathf.Lerp(0f, 1.2f, p / 0.7f) : Mathf.Lerp(1.2f, 1f, (p - 0.7f) / 0.3f);
            float twinkle = 1f + 0.06f * Mathf.Sin(now * 3f + phase[i]);
            flakes[i].localScale = Vector3.one * pop * twinkle;
        }
    }

    void Build()
    {
        canvasObject = new GameObject("FreezeFrost", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 87; // under the HUD and the song bar
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        group = canvasObject.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;

        Vector2[] corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        flakes = new RectTransform[corners.Length * flakesPerCorner];
        phase = new float[flakes.Length];
        delay = new float[flakes.Length];
        var random = new System.Random(526); // same pattern every time
        int n = 0;
        foreach (Vector2 corner in corners)
        {
            Vector2 inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);

            Image glow = NewImage("Glow", GlowSprite(), glowColor);
            RectTransform g = glow.rectTransform;
            g.anchorMin = g.anchorMax = corner;
            g.sizeDelta = new Vector2(reach * 1.6f, reach * 1.6f);
            g.anchoredPosition = Vector2.zero; // centred on the corner, so a quarter shows

            for (int k = 0; k < flakesPerCorner; k++)
            {
                // Distance biased toward the corner (u^2), so flakes crowd there and thin out with gaps.
                float u = (float)random.NextDouble();
                float d = 20f + reach * u * u;
                float angle = (float)random.NextDouble() * Mathf.PI * 0.5f;
                float size = Mathf.Lerp(58f, 20f, d / reach) * (0.8f + 0.4f * (float)random.NextDouble());

                Image flake = NewImage("Snowflake", FlakeSprite(), frostColor);
                RectTransform r = flake.rectTransform;
                r.anchorMin = r.anchorMax = corner;
                r.sizeDelta = new Vector2(size, size);
                r.anchoredPosition = new Vector2(Mathf.Cos(angle) * d * inward.x, Mathf.Sin(angle) * d * inward.y);
                r.localScale = Vector3.zero;
                flake.color = new Color(frostColor.r, frostColor.g, frostColor.b, frostColor.a * Mathf.Lerp(1f, 0.55f, d / reach));
                phase[n] = n * 1.7f;
                delay[n] = d / reach * popStagger * 8f; // frost spreads out from the corner
                flakes[n++] = r;
            }
        }
        canvasObject.SetActive(false);
    }

    Image NewImage(string name, Sprite sprite, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(canvasObject.transform, false);
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>A detailed six-armed ice crystal (128 px, smooth edges and a soft glow): each arm has two
    /// pairs of side branches and a small fork at the tip, around a little hexagon in the middle.</summary>
    static Sprite FlakeSprite()
    {
        if (flakeSprite != null) return flakeSprite;
        const int size = 128;
        const float R = 58f;
        var segments = new System.Collections.Generic.List<(Vector2 a, Vector2 b, float w)>();
        for (int arm = 0; arm < 6; arm++)
        {
            float angle = arm * Mathf.PI / 3f + Mathf.PI / 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            segments.Add((Vector2.zero, dir * R, 3.2f));                       // main arm
            foreach ((float at, float len) in new[] { (0.42f, 0.30f), (0.68f, 0.22f), (0.9f, 0.1f) })
            {
                Vector2 root = dir * (R * at);
                foreach (float side in new[] { -1f, 1f })
                {
                    float b = angle + side * Mathf.PI / 4f;                    // branches at 45 degrees
                    segments.Add((root, root + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * R * len, 2.2f));
                }
            }
            // Hexagon edge around the centre.
            float next = angle + Mathf.PI / 3f;
            segments.Add((dir * 13f, new Vector2(Mathf.Cos(next), Mathf.Sin(next)) * 13f, 2f));
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, name = "Snowflake" };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
            float edge = float.MaxValue;
            foreach (var seg in segments)
            {
                Vector2 ab = seg.b - seg.a;
                float t = Mathf.Clamp01(Vector2.Dot(p - seg.a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                edge = Mathf.Min(edge, Vector2.Distance(p, seg.a + ab * t) - seg.w * 0.5f);
            }
            float core = Mathf.Clamp01(0.5f - edge);              // crisp, anti-aliased lines
            float glow = Mathf.Exp(-Mathf.Max(0f, edge) / 3.5f) * 0.45f;
            float alpha = Mathf.Clamp01(core + glow * (1f - core));
            byte white = (byte)(200 + 55 * core);                  // lines whiter than the glow
            pixels[y * size + x] = new Color32(white, white, 255, (byte)(255 * alpha));
        }
        tex.SetPixels32(pixels);
        tex.Apply(true);
        flakeSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return flakeSprite;
    }

    static Sprite GlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FrostGlow" };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return glowSprite;
    }
}
