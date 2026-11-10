using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays the song slower for a few seconds. Asteroids, rings and timing all run on
/// <see cref="BeatClock.SongTime"/> (the music's playback position), so lowering the music's pitch
/// slows them together and they stay in sync. The ship, menus and <see cref="Time.timeScale"/> are
/// untouched (the pause menu and the tutorial use it). Without music it only shows the effect.
/// Shows a blue screen tint and a shrinking SLOW-MO bar while active. Granted by a <see cref="PowerUpPickup"/>.
/// </summary>
public class SlowMo : MonoBehaviour
{
    [Tooltip("Seconds of slow motion per pickup (real time, paused time doesn't count).")]
    public float duration = 6f;
    [Tooltip("Song speed while active: 0.5 = half speed.")]
    [Range(0.2f, 1f)] public float speed = 0.5f;
    [Tooltip("Seconds to ease in and out of slow motion.")]
    public float rampSeconds = 0.4f;
    [Tooltip("Real seconds to ease back to normal speed when it ends, so asteroids speed up gently.")]
    public float rampOutSeconds = 1.5f;
    public Color tintColor = new Color(0.4f, 0.75f, 1f, 0.12f);
    public Color barColor = new Color(0.45f, 0.85f, 1f);

    /// <summary>Raised when slow motion starts or ends, with the new state.</summary>
    public event Action<bool> Changed;

    public bool Active => remaining > 0f;
    /// <summary>Seconds left, 0 when inactive.</summary>
    public float Remaining => remaining;

    float remaining;
    float current = 1f;

    GameObject canvasObject;
    Image tint, bar;
    Text label;

    void Awake() => BuildUI();

    /// <summary>Starts slow motion, or refills the timer if it's already running.</summary>
    public void Grant()
    {
        bool was = Active;
        remaining = duration;
        if (!was) Changed?.Invoke(true);
    }

    void Update()
    {
        if (Active)
        {
            // Ends with the level, so nothing stays slowed (or on screen) over the menus.
            if (GameFlow.Instance != null && GameFlow.Instance.State != GameState.Playing) remaining = 0f;
            // Real seconds, but not while time is frozen.
            else if (Time.timeScale > 0f) remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
            if (!Active) Changed?.Invoke(false);
        }

        float target = Active ? speed : 1f;
        float ramp = Active ? rampSeconds : rampOutSeconds;
        current = Mathf.MoveTowards(current, target, (1f - speed) / Mathf.Max(0.01f, ramp) * Time.unscaledDeltaTime);
        Apply(current);
        RefreshUI();
    }

    void OnDisable()
    {
        remaining = 0f;
        current = 1f;
        Apply(1f);
        RefreshUI();
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    static void Apply(float value)
    {
        BeatClock clock = BeatClock.Instance;
        if (clock != null && clock.HasMusic) clock.music.pitch = value;
    }

    // ------------------------------------------------------------------ UI

    void RefreshUI()
    {
        if (canvasObject == null) return;
        float strength = Mathf.InverseLerp(1f, speed, current);
        bool show = strength > 0.001f;
        canvasObject.SetActive(show);
        if (!show) return;

        Color c = tintColor;
        c.a *= strength;
        tint.color = c;
        bar.fillAmount = duration > 0f ? remaining / duration : 0f;
        label.text = $"FREEZE  {Mathf.CeilToInt(remaining)}";
    }

    void BuildUI()
    {
        canvasObject = new GameObject("SlowMoUI", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        tint = NewImage("Tint", canvasObject.transform);
        tint.rectTransform.anchorMin = Vector2.zero;
        tint.rectTransform.anchorMax = Vector2.one;
        tint.rectTransform.offsetMin = tint.rectTransform.offsetMax = Vector2.zero;

        // Top left, under the hearts and shield lines (the top middle is where the ship sits on W):
        // a label, then a dim track with a bright fill that empties over time.
        Image track = NewImage("Track", canvasObject.transform);
        track.rectTransform.anchorMin = track.rectTransform.anchorMax = track.rectTransform.pivot = new Vector2(0f, 1f);
        track.rectTransform.anchoredPosition = new Vector2(32f, -200f);
        track.rectTransform.sizeDelta = new Vector2(300f, 12f);
        track.color = new Color(0f, 0f, 0f, 0.5f);

        bar = NewImage("Fill", track.transform);
        bar.rectTransform.anchorMin = Vector2.zero;
        bar.rectTransform.anchorMax = Vector2.one;
        bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
        bar.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;
        bar.color = barColor;

        label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(track.transform, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 0f);
        label.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        label.rectTransform.sizeDelta = new Vector2(300f, 36f);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 26;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = barColor;
        label.raycastTarget = false;

        canvasObject.SetActive(false);
    }

    static Image NewImage(string name, Transform parent)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.raycastTarget = false;
        return image;
    }
}
