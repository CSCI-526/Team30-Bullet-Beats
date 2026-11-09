using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Time left in the level, top right ("0:41"). It counts down on the song clock, so it slows down
/// with Freeze. In the last <see cref="warningSeconds"/> it turns orange, then red, and pulses once
/// as each second ticks over (no faster, so it stays flash-safe). Hidden outside play and in the
/// tutorial, and while the <see cref="LevelIntroCard"/> is up. Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class LevelClock : MonoBehaviour
{
    public static LevelClock Instance { get; private set; }

    public Color normalColor = Color.white;
    public Color warningColor = new Color(1f, 0.6f, 0.15f);
    public Color finalColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Seconds left when the clock starts warning.")]
    public float warningSeconds = 5f;

    const float Margin = 28f;
    const int FontSize = 56;
    const float PulseScale = 1.2f;
    const float PulseSeconds = 0.25f;

    GameFlow flow;
    LevelIntroCard intro;
    RhythmSession session;
    GameObject canvasObject;
    Text label;

    /// <summary>The clock text, for effects that restyle it (e.g. Freeze).</summary>
    public Text Label => label;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        intro = GetComponent<LevelIntroCard>();
        session = FindFirstObjectByType<RhythmSession>();
        Instance = this;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (canvasObject != null) Destroy(canvasObject);
    }

    void LateUpdate()
    {
        float left = 0f;
        if (intro == null) intro = GetComponent<LevelIntroCard>();
        bool show = flow.State == GameState.Playing && !flow.IsTutorial && (intro == null || !intro.IsShowing)
                    && TryGetTimeLeft(out left);
        canvasObject.SetActive(show);
        if (!show) return;

        // 0:41 stays up until 40 s are left, like a countdown timer.
        int shown = Mathf.CeilToInt(left);
        label.text = $"{shown / 60}:{shown % 60:00}";

        if (left > warningSeconds)
        {
            label.color = normalColor;
            label.rectTransform.localScale = Vector3.one;
            return;
        }

        label.color = Color.Lerp(warningColor, finalColor, 1f - left / Mathf.Max(0.01f, warningSeconds));
        float sinceTick = shown - left;
        float pulse = Mathf.Clamp01(1f - sinceTick / PulseSeconds);
        label.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, PulseScale, pulse);
    }

    bool TryGetTimeLeft(out float left)
    {
        BeatClock clock = BeatClock.Instance;
        if (clock != null && clock.HasMusic && clock.SongLength > 0f)
        {
            left = Mathf.Max(0f, clock.SongLength - clock.SongTime);
            return true;
        }
        // No song in this scene at all: the level runs on GameFlow's fixed length.
        if (session == null && flow.levelLengthSeconds > 0f)
        {
            left = Mathf.Max(0f, flow.levelLengthSeconds - flow.TimePlayed);
            return true;
        }
        left = 0f;
        return false;
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("LevelClock", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        label = new GameObject("Clock", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        RectTransform rect = label.rectTransform;
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-Margin, -Margin);
        rect.sizeDelta = new Vector2(220f, FontSize * 1.3f);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = FontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.UpperRight;
        label.color = normalColor;
        label.raycastTarget = false;
        label.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        canvasObject.SetActive(false);
    }
}
