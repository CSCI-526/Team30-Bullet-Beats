using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health pips in the top-left corner and the current streak under them, with how many more notes
/// in a row heal the ship. Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class HealthHUD : MonoBehaviour
{
    public Color fullColor = new Color(0.3f, 0.9f, 1f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.15f);
    public Color streakColor = Color.white;
    [Tooltip("Seconds a pip glows after the ship heals.")]
    public float healGlowTime = 0.5f;

    const float PipWidth = 34f;
    const float PipHeight = 14f;
    const float PipGap = 6f;
    const float Margin = 32f;

    PlayerHealth health;
    GameObject canvasObject;
    RectTransform corner;
    readonly System.Collections.Generic.List<Image> pips = new System.Collections.Generic.List<Image>();
    Text streakLabel;
    float healGlowTimer;

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
        canvasObject.SetActive(true);
        Refresh();
    }

    void OnDisable()
    {
        health.HealthChanged -= OnHealthChanged;
        health.StreakChanged -= OnStreakChanged;
        health.Healed -= OnHealed;
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void LateUpdate()
    {
        if (healGlowTimer <= 0f) return;
        healGlowTimer -= Time.deltaTime;
        RefreshPips();
    }

    void OnHealthChanged(int _) => RefreshPips();
    void OnStreakChanged(int _) => RefreshStreak();

    void OnHealed(int _)
    {
        healGlowTimer = healGlowTime;
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
        while (pips.Count < Mathf.Max(health.maxHealth, health.Health))
            AddPip();
        for (int i = 0; i < pips.Count; i++)
            pips[i].color = i < health.Health ? full : emptyColor;
    }

    void AddPip()
    {
        int i = pips.Count;
        var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        RectTransform rect = pip.rectTransform;
        rect.SetParent(corner, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(PipWidth, PipHeight);
        rect.anchoredPosition = new Vector2(i * (PipWidth + PipGap), 0f);
        pip.raycastTarget = false;
        pips.Add(pip);
    }

    void RefreshStreak()
    {
        streakLabel.text = health.Streak > 0
            ? $"STREAK {health.Streak}"
            : "Hit a Perfect note to gain a life";
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
