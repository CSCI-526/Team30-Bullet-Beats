using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health hearts in the top-left corner and the current streak under them. Full pips take the ship's
/// health colour (<see cref="ShipDamageFlash.ColorFor"/>). A pip that was just lost flashes red before going empty.
/// An extra pip after the health shows the <see cref="BasicShield"/>, and a line under the streak
/// shows the <see cref="DirectionalShield"/>'s blocks left.
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthHUD : MonoBehaviour
{
    [Tooltip("Full pip colour when the ship has no ShipDamageFlash.")]
    public Color fullColor = new Color(0.3f, 0.9f, 1f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.15f);
    public Color streakColor = Color.white;
    public Color lostColor = new Color(1f, 0.2f, 0.2f);
    public Color shieldColor = new Color(0.75f, 0.6f, 1f);
    [Tooltip("Seconds a pip glows after the ship heals.")]
    public float healGlowTime = 0.5f;
    [Tooltip("Seconds a lost pip flashes red before going empty.")]
    public float lostFlashTime = 0.6f;

    const float PipWidth = 42f;
    const float PipHeight = 38f;
    const float PipGap = 6f;
    const float Margin = 32f;

    PlayerHealth health;
    ShipDamageFlash shipColor;
    BasicShield basicShield;
    DirectionalShield movingShield;
    GameObject canvasObject;
    RectTransform corner;
    static Sprite heart;
    readonly System.Collections.Generic.List<Image> pips = new System.Collections.Generic.List<Image>();
    Image shieldPip;
    Text streakLabel;
    Text movingShieldLabel;
    float healGlowTimer;
    float lostFlashTimer;
    int lostFrom, lostTo;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        basicShield = GetComponent<BasicShield>();
        movingShield = GetComponent<DirectionalShield>();
        BuildCanvas();
    }

    void OnEnable()
    {
        health.HealthChanged += OnHealthChanged;
        health.StreakChanged += OnStreakChanged;
        health.Healed += OnHealed;
        health.Damaged += OnDamaged;
        if (basicShield != null) basicShield.Changed += OnBasicShieldChanged;
        if (movingShield != null) movingShield.Changed += RefreshMovingShield;
        canvasObject.SetActive(true);
        Refresh();
    }

    void OnDisable()
    {
        health.HealthChanged -= OnHealthChanged;
        health.StreakChanged -= OnStreakChanged;
        health.Healed -= OnHealed;
        health.Damaged -= OnDamaged;
        if (basicShield != null) basicShield.Changed -= OnBasicShieldChanged;
        if (movingShield != null) movingShield.Changed -= RefreshMovingShield;
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    // The ship colour component may be added after this one, so colour the pips again once both exist.
    void Start() => RefreshPips();

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

    void OnHealthChanged(int _)
    {
        RefreshPips();
        RefreshStreak();
    }
    void OnStreakChanged(int _) => RefreshStreak();
    void OnBasicShieldChanged(bool _) => RefreshPips();

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

    void Refresh()
    {
        RefreshPips();
        RefreshStreak();
        RefreshMovingShield();
    }

    void RefreshPips()
    {
        float glow = healGlowTime > 0f ? Mathf.Clamp01(healGlowTimer / healGlowTime) : 0f;
        if (shipColor == null) shipColor = GetComponent<ShipDamageFlash>();
        Color baseColor = shipColor != null ? shipColor.ColorFor(health.Health) : fullColor;
        Color full = Color.Lerp(baseColor, Color.white, glow);
        while (pips.Count < Mathf.Max(health.maxHealth, health.Health))
            AddPip();
        float lost = lostFlashTime > 0f ? Mathf.Clamp01(lostFlashTimer / lostFlashTime) : 0f;
        Color justLost = Color.Lerp(emptyColor, lostColor, lost);
        for (int i = 0; i < pips.Count; i++)
        {
            if (i < health.Health) pips[i].color = full;
            else if (lost > 0f && i >= lostFrom && i < lostTo) pips[i].color = justLost;
            else pips[i].color = emptyColor;
        }

        bool shielded = basicShield != null && basicShield.Active;
        shieldPip.gameObject.SetActive(shielded);
        shieldPip.rectTransform.anchoredPosition = new Vector2(pips.Count * (PipWidth + PipGap), 0f);
        shieldPip.color = shieldColor;
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
        pip.sprite = HeartSprite();
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

    void RefreshStreak()
    {
        streakLabel.text = health.Streak > 0 ? $"STREAK {health.Streak}"
            : health.Health < health.maxHealth ? "Hit a Perfect note to gain a life"
            : "";
    }

    void RefreshMovingShield()
    {
        bool up = movingShield != null && movingShield.Active;
        movingShieldLabel.gameObject.SetActive(up);
        if (up) movingShieldLabel.text = $"ARC SHIELD x{movingShield.BlocksLeft}";
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
        corner.anchoredPosition = new Vector2(Margin, -Margin);
        corner.sizeDelta = Vector2.zero;

        for (int i = 0; i < health.maxHealth; i++)
            AddPip();
        shieldPip = MakePip("ShieldPip", health.maxHealth);

        streakLabel = MakeLabel("Streak", -(PipHeight + 10f), streakColor);
        movingShieldLabel = MakeLabel("ArcShield", -(PipHeight + 10f + 32f), shieldColor);
    }

    Text MakeLabel(string name, float y, Color color)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        RectTransform labelRect = label.rectTransform;
        labelRect.SetParent(corner, false);
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 1f);
        labelRect.sizeDelta = new Vector2(600f, 32f);
        labelRect.anchoredPosition = new Vector2(0f, y);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.color = color;
        label.alignment = TextAnchor.UpperLeft;
        label.raycastTarget = false;
        return label;
    }
}
