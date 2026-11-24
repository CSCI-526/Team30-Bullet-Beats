using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays the song slower for a few seconds. Asteroids, rings and timing all run on
/// <see cref="BeatClock.SongTime"/> (the music's playback position), so lowering the music's pitch
/// slows them together and they stay in sync. The ship, menus and <see cref="Time.timeScale"/> are
/// untouched (the pause menu and the tutorial use it). Without music it only shows the effect.
/// Shows a blue screen tint while active (the FREEZE banner in PowerUpBanners shows the time left). Granted by a <see cref="PowerUpPickup"/>.
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

    /// <summary>Raised when slow motion starts or ends, with the new state.</summary>
    public event Action<bool> Changed;

    public bool Active => remaining > 0f;
    /// <summary>Seconds left, 0 when inactive.</summary>
    public float Remaining => remaining;

    float remaining;
    float current = 1f;

    GameObject canvasObject;
    Image tint;

    void Awake() => BuildUI();

    /// <summary>Starts slow motion, or refills the timer if it's already running.</summary>
    public void Grant()
    {
        bool was = Active;
        remaining = duration;
        if (!was) Changed?.Invoke(true);
    }

    /// <summary>Ends slow motion now. The speed eases back up as usual.</summary>
    public void Cancel()
    {
        if (!Active) return;
        remaining = 0f;
        Changed?.Invoke(false);
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
