using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small colored tabs at the top centre naming the power-ups in effect: SHIELD stays until it takes
/// its hit, FREEZE drains with its timer, BOMB shows briefly. Two at once sit side by side. It only
/// reads the power-ups' state, so they need no knowledge of it. Builds its own canvas.
/// </summary>
public class PowerUpBanners : MonoBehaviour
{
    public Color shieldColor = new Color(0.62f, 0.36f, 1f);
    public Color freezeColor = new Color(0.25f, 0.65f, 1f);
    public Color bombColor = new Color(1f, 0.42f, 0.15f);
    [Tooltip("Seconds the BOMB tab shows after the bomb is collected.")]
    public float bombSeconds = 1.5f;

    const float TabWidth = 190f, TabHeight = 44f, Gap = 12f, Top = 14f;
    const float PopSeconds = 0.3f, LeaveSeconds = 0.22f;

    class Tab
    {
        public RectTransform rect;
        public CanvasGroup group;
        public RectTransform drain;
        public bool wanted, shown;
        public float changedAt;
        public float x;
    }

    Tab shield, freeze, bomb;
    Tab[] tabs;
    GameObject canvasObject;
    GameFlow flow;
    bool bombWas;
    float bombUntil = float.NegativeInfinity;

    void Start()
    {
        flow = GetComponent<GameFlow>();
        BuildCanvas();
        shield = AddTab("SHIELD", shieldColor, false);
        freeze = AddTab("FREEZE", freezeColor, true);
        bomb = AddTab("BOMB", bombColor, false);
        tabs = new[] { shield, freeze, bomb };
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (tabs == null) return;
        ReadPowerUps();

        int count = 0;
        foreach (Tab t in tabs)
            if (t.wanted) count++;

        float now = Time.unscaledTime;
        int index = 0;
        foreach (Tab t in tabs)
        {
            if (t.wanted != t.shown && (t.wanted || t.rect.gameObject.activeSelf))
            {
                t.shown = t.wanted;
                t.changedAt = now;
                if (t.wanted)
                {
                    t.rect.gameObject.SetActive(true);
                    t.x = (index - (count - 1) * 0.5f) * (TabWidth + Gap);
                }
            }
            if (t.wanted)
            {
                t.x = Mathf.Lerp(t.x, (index - (count - 1) * 0.5f) * (TabWidth + Gap), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                index++;
            }
            Animate(t, now);
        }
    }

    void ReadPowerUps()
    {
        PlayerHealth ship = PlayerHealth.Instance;
        bool playing = flow != null && flow.State == GameState.Playing && ship != null;

        BasicShield basicShield = ship != null ? ship.GetComponent<BasicShield>() : null;
        shield.wanted = playing && basicShield != null && basicShield.Active;

        SlowMo slowMo = ship != null ? ship.GetComponent<SlowMo>() : null;
        freeze.wanted = playing && slowMo != null && slowMo.Active;
        if (freeze.wanted)
            freeze.drain.anchorMax = new Vector2(Mathf.Clamp01(slowMo.Remaining / Mathf.Max(0.01f, slowMo.duration)), 1f);

        Bomb bombPower = ship != null ? ship.GetComponent<Bomb>() : null;
        bool active = bombPower != null && bombPower.Active;
        if (active && !bombWas) bombUntil = Time.unscaledTime + bombSeconds;
        bombWas = active;
        bomb.wanted = playing && Time.unscaledTime < bombUntil;
    }

    void Animate(Tab t, float now)
    {
        if (!t.rect.gameObject.activeSelf) return;
        float age = now - t.changedAt;
        float y = -Top, scale = 1f, alpha = 1f;
        if (t.shown)
        {
            float p = Mathf.Clamp01(age / PopSeconds);
            // Pops past full size, then settles.
            scale = p < 0.6f ? Mathf.Lerp(0.6f, 1.12f, p / 0.6f) : Mathf.Lerp(1.12f, 1f, (p - 0.6f) / 0.4f);
            alpha = Mathf.Clamp01(p * 3f);
        }
        else
        {
            float p = Mathf.Clamp01(age / LeaveSeconds);
            y += p * (TabHeight + Top);
            alpha = 1f - p;
            if (p >= 1f) t.rect.gameObject.SetActive(false);
        }
        t.rect.anchoredPosition = new Vector2(t.x, y);
        t.rect.localScale = Vector3.one * scale;
        t.group.alpha = alpha;
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("PowerUpBanners", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 92;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
    }

    Tab AddTab(string title, Color color, bool drains)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(TabWidth, TabHeight);
        var back = go.GetComponent<Image>();
        back.color = drains ? new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.92f) : color;
        back.raycastTarget = false;
        go.GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.55f);

        RectTransform drain = null;
        if (drains)
        {
            var fill = new GameObject("Drain", typeof(RectTransform), typeof(Image));
            drain = fill.GetComponent<RectTransform>();
            drain.SetParent(rect, false);
            drain.anchorMin = Vector2.zero;
            drain.anchorMax = Vector2.one;
            drain.offsetMin = drain.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = title;
        label.raycastTarget = false;

        go.SetActive(false);
        return new Tab { rect = rect, group = go.GetComponent<CanvasGroup>(), drain = drain };
    }
}
