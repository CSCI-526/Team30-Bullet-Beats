using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global mute (set from the Settings screen) and the pause button in the top-right corner during play.
/// The button is the pause_button art (Resources/UI/Art), as tall as one heart, and pauses the level like
/// Esc. It shows in levels only, not in the tutorial. Each play session starts with sound on; the choice is kept across scene
/// loads in that session.
/// </summary>
public class CornerMute : MonoBehaviour
{
    public static bool Muted { get; private set; }

    /// <summary>Button layout at 1080p: this far in from the top-right corner, and this size (one heart).
    /// Public so <see cref="LevelClock"/> can sit just under it.</summary>
    public const float ButtonMargin = 32f, ButtonWidth = 75f, ButtonHeight = 60f;

    Button button;
    RectTransform buttonRect;
    GameFlow flow;

    public static void Apply()
    {
        AudioListener.volume = Muted ? 0f : 1f;
    }

    /// <summary>True when <paramref name="screenPoint"/> is on the pause button, so a click there doesn't fire.</summary>
    public static bool Contains(Vector2 screenPoint)
    {
        CornerMute mute = FindFirstObjectByType<CornerMute>();
        if (mute == null || mute.buttonRect == null || !mute.button.gameObject.activeSelf) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(mute.buttonRect, screenPoint, null);
    }

    void Awake()
    {
        if (FindObjectsByType<CornerMute>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        Build();
        Apply();
    }

    /// <summary>Sets mute from anywhere (the Settings screen uses this).</summary>
    public static void SetMuted(bool muted)
    {
        Muted = muted;
        Apply();
    }

    void Pause()
    {
        // Drop focus so Space/Enter in play can't press the button again.
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        if (flow != null) flow.SetPaused(true);
    }

    // Only while a level is running and not paused or behind a menu.
    void Update()
    {
        if (button == null) return;
        if (flow == null) flow = FindFirstObjectByType<GameFlow>();
        bool show = flow != null && flow.State == GameState.Playing && !flow.IsTutorial && !flow.IsPaused && !UIScreen.AnyOpen;
        if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
    }

    void Build()
    {
        var canvasObject = new GameObject("PauseButtonCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var image = new GameObject("Pause", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        buttonRect = image.rectTransform;
        buttonRect.SetParent(canvasObject.transform, false);
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        buttonRect.anchoredPosition = new Vector2(-ButtonMargin, -ButtonMargin);
        image.sprite = UITheme.Art("pause_button");
        image.preserveAspect = true;

        button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.colorMultiplier = 1.25f;
        button.colors = colors;
        button.onClick.AddListener(Pause);

        button.gameObject.SetActive(false);
    }
}
