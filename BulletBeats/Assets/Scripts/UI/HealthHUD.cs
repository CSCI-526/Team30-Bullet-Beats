using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health pips in the top-left corner and the current streak under them, with how many more notes
/// in a row heal the ship. A pip that was just lost flashes red before going empty.
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthHUD : MonoBehaviour
{
    public Color fullColor = new Color(0.3f, 0.9f, 1f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.15f);
    public Color streakColor = Color.white;
    public Color lostColor = new Color(1f, 0.2f, 0.2f);
    [Tooltip("Seconds a pip glows after the ship heals.")]
    public float healGlowTime = 0.5f;
    [Tooltip("Seconds a lost pip flashes red before going empty.")]
    public float lostFlashTime = 0.6f;

    const float PipWidth = 34f;
    const float PipHeight = 14f;
    const float PipGap = 6f;
    const float Margin = 32f;

    PlayerHealth health;
    GameObject canvasObject;
    Image[] pips;
    Text streakLabel;
    float healGlowTimer;
    float lostFlashTimer;
    int lostFrom, lostTo;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        BuildCanvas();
    }

    void OnEnable()
    {
        health.HealthChanged += OnHealthChanged;
        health.StreakChanged += OnStreakChanged;
        health.Healed += OnHealed;
        health.Damaged += OnDamaged;
        canvasObject.SetActive(true);
        Refresh();
    }

    void OnDisable()
    {
        health.HealthChanged -= OnHealthChanged;
        health.StreakChanged -= OnStreakChanged;
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
        healGlowTimer -= Time.deltaTime;
        lostFlashTimer -= Time.deltaTime;
        RefreshPips();
    }

    void OnHealthChanged(int _) => RefreshPips();
    void OnStreakChanged(int _) => RefreshStreak();

    void OnHealed(int _)
    {
        healGlowTimer = healGlowTime;
        RefreshPips();
    }

    void OnDamaged(int amount)
    {
        lostFrom = health.Health;
        lostTo = Mathf.Min(health.maxHealth, health.Health + amount);
        lostFlashTimer = lostFlashTime;
        RefreshPips();
    }

    void Refresh()
    {
        RefreshPips();
        RefreshStreak();
    }

    void RefreshPips()
    {
        float glow = healGlowTime > 0f ? Mathf.Clamp01(healGlowTimer / healGlowTime) : 0f;
        Color full = Color.Lerp(fullColor, Color.white, glow);
        float lost = lostFlashTime > 0f ? Mathf.Clamp01(lostFlashTimer / lostFlashTime) : 0f;
        Color justLost = Color.Lerp(emptyColor, lostColor, lost);
        for (int i = 0; i < pips.Length; i++)
        {
            if (i < health.Health) pips[i].color = full;
            else if (lost > 0f && i >= lostFrom && i < lostTo) pips[i].color = justLost;
            else pips[i].color = emptyColor;
        }
    }

    void RefreshStreak()
    {
        streakLabel.text = health.Streak > 0
            ? $"STREAK {health.Streak}   heal in {health.NotesUntilHeal}"
            : $"Hit {health.healStreak} notes in a row to heal";
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

        var corner = new GameObject("Corner", typeof(RectTransform)).GetComponent<RectTransform>();
        corner.SetParent(canvasObject.transform, false);
        corner.anchorMin = corner.anchorMax = corner.pivot = new Vector2(0f, 1f);
        corner.anchoredPosition = new Vector2(Margin, -Margin);
        corner.sizeDelta = Vector2.zero;

        pips = new Image[health.maxHealth];
        for (int i = 0; i < pips.Length; i++)
        {
            var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            RectTransform rect = pip.rectTransform;
            rect.SetParent(corner, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(PipWidth, PipHeight);
            rect.anchoredPosition = new Vector2(i * (PipWidth + PipGap), 0f);
            pip.raycastTarget = false;
            pips[i] = pip;
        }

        streakLabel = new GameObject("Streak", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        RectTransform labelRect = streakLabel.rectTransform;
        labelRect.SetParent(corner, false);
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 1f);
        labelRect.sizeDelta = new Vector2(600f, 32f);
        labelRect.anchoredPosition = new Vector2(0f, -(PipHeight + 10f));
        streakLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        streakLabel.fontSize = 22;
        streakLabel.fontStyle = FontStyle.Bold;
        streakLabel.color = streakColor;
        streakLabel.alignment = TextAnchor.UpperLeft;
        streakLabel.raycastTarget = false;
    }
}
