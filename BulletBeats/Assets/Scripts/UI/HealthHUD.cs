using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health hearts in the top-left corner, under the score card. Hearts use the pixel art in
/// Resources/UI/Art (heart_full / heart_empty). A heart that was just lost shows full, then empty.
/// The shield doesn't change the hearts; it shows on the ship.
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthHUD : MonoBehaviour
{
    [Tooltip("Full pip colour if the heart art is missing.")]
    public Color fullColor = new Color(0.87f, 0.1f, 0.24f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.15f);
    public Color lostColor = new Color(1f, 0.2f, 0.2f);
    [Tooltip("Seconds a pip glows after the ship heals.")]
    public float healGlowTime = 0.5f;
    [Tooltip("Seconds a lost pip flashes red before going empty.")]
    public float lostFlashTime = 0.6f;

    // 60 = 3 canvas units per art pixel (20-cell grid incl. glow), so the pixels stay even. The negative gap overlaps
    // the soft glows so the hearts themselves sit close together. Public so the tutorial's lives frame matches.
    public const float PipWidth = 60f;
    public const float PipHeight = 60f;
    public const float PipGap = -9f;
    public const float Margin = 32f;

    PlayerHealth health;
    GameObject canvasObject;
    RectTransform corner;
    static Sprite heart;
    Sprite fullHeart, emptyHeart;
    readonly System.Collections.Generic.List<Image> pips = new System.Collections.Generic.List<Image>();
    float healGlowTimer;
    float lostFlashTimer;
    int lostFrom, lostTo;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        fullHeart = UITheme.Art("heart_full");
        emptyHeart = UITheme.Art("heart_empty");
        BuildCanvas();
        if (GetComponent<ScoreHUD>() == null) gameObject.AddComponent<ScoreHUD>();
    }

    void OnEnable()
    {
        health.HealthChanged += OnHealthChanged;
        health.Healed += OnHealed;
        health.Damaged += OnDamaged;
        canvasObject.SetActive(true);
        RefreshPips();
    }

    void OnDisable()
    {
        health.HealthChanged -= OnHealthChanged;
        health.Healed -= OnHealed;
        health.Damaged -= OnDamaged;
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void LateUpdate()
    {
        if (healGlowTimer <= 0f && lostFlashTimer <= 0f) return;
        // Unscaled, so the flash still fades once the game freezes on the end screen.
        healGlowTimer -= Time.unscaledDeltaTime;
        lostFlashTimer -= Time.unscaledDeltaTime;
        RefreshPips();
    }

    void OnHealthChanged(int _) => RefreshPips();

    void OnHealed(int _)
    {
        healGlowTimer = healGlowTime;
        RefreshPips();
    }

    void OnDamaged(int amount)
    {
        lostFrom = health.Health;
        lostTo = health.Health + amount;
        lostFlashTimer = lostFlashTime;
        RefreshPips();
    }

    void RefreshPips()
    {
        float glow = healGlowTime > 0f ? Mathf.Clamp01(healGlowTimer / healGlowTime) : 0f;
        Color full = Color.Lerp(fullColor, Color.white, glow);
        while (pips.Count < Mathf.Max(health.maxHealth, health.Health))
            AddPip();
        float lost = lostFlashTime > 0f ? Mathf.Clamp01(lostFlashTimer / lostFlashTime) : 0f;
        Color justLost = Color.Lerp(emptyColor, lostColor, lost);
        bool art = fullHeart != null && emptyHeart != null;
        for (int i = 0; i < pips.Count; i++)
        {
            Image pip = pips[i];
            if (!art)
            {
                if (i < health.Health) pip.color = full;
                else if (lost > 0f && i >= lostFrom && i < lostTo) pip.color = justLost;
                else pip.color = emptyColor;
                continue;
            }
            if (i < health.Health)
            {
                pip.sprite = fullHeart;
                pip.color = Color.Lerp(Color.white, new Color(1.6f, 1.6f, 1.6f), glow); // brief brighten on heal
            }
            else if (lost > 0f && i >= lostFrom && i < lostTo)
            {
                pip.sprite = lost > 0.5f ? fullHeart : emptyHeart; // just lost: full, then empty
                pip.color = Color.white;
            }
            else
            {
                pip.sprite = emptyHeart;
                pip.color = Color.white;
            }
        }
    }

    void AddPip() => pips.Add(MakePip("Pip", pips.Count));

    Image MakePip(string name, int index)
    {
        var pip = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        RectTransform rect = pip.rectTransform;
        rect.SetParent(corner, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(PipWidth, PipHeight);
        rect.anchoredPosition = new Vector2(index * (PipWidth + PipGap), 0f);
        pip.sprite = fullHeart != null ? fullHeart : HeartSprite();
        pip.preserveAspect = true;
        pip.raycastTarget = false;
        return pip;
    }

    /// <summary>A white heart drawn in code (no image asset), tinted per pip.</summary>
    static Sprite HeartSprite()
    {
        if (heart != null) return heart;
        const int size = 64;
        const int samples = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            int inside = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                // Heart curve (x² + y² - 1)³ - x²y³ <= 0, fitted to the square.
                float x = ((px + (sx + 0.5f) / samples) / size - 0.5f) * 2.6f;
                float y = ((py + (sy + 0.5f) / samples) / size - 0.5f) * 2.6f + 0.15f;
                float a = x * x + y * y - 1f;
                if (a * a * a - x * x * y * y * y <= 0f) inside++;
            }
            pixels[py * size + px] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        heart = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return heart;
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("HealthHUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        corner = new GameObject("Corner", typeof(RectTransform)).GetComponent<RectTransform>();
        corner.SetParent(canvasObject.transform, false);
        corner.anchorMin = corner.anchorMax = corner.pivot = new Vector2(0f, 1f);
        corner.anchoredPosition = new Vector2(Margin, -(Margin + ScoreHUD.ShownHeight + 6f)); // under the score card
        corner.sizeDelta = Vector2.zero;

        for (int i = 0; i < health.maxHealth; i++)
            AddPip();
    }

}
