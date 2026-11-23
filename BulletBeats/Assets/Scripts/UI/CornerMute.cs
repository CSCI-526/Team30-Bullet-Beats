using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global mute control, always visible in the top-right corner.
/// Each play session starts with sound on; the choice is kept across scene loads in that session.
/// </summary>
public class CornerMute : MonoBehaviour
{
    public static bool Muted { get; private set; }

    /// <summary>Button layout at 1080p: this far in from the top-right corner, and this size.
    /// Public so <see cref="LevelClock"/> can sit just under it.</summary>
    public const float ButtonMargin = 28f, ButtonWidth = 220f, ButtonHeight = 56f;

    Button button;
    Text label;
    RectTransform buttonRect;

    public static void Apply()
    {
        AudioListener.volume = Muted ? 0f : 1f;
    }

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
        Refresh();
    }

    void Toggle() => SetMuted(!Muted);

    /// <summary>Sets mute from anywhere (the Settings screen uses this).</summary>
    public static void SetMuted(bool muted)
    {
        Muted = muted;
        Apply();
        FindFirstObjectByType<CornerMute>()?.Refresh();
    }

    // The redesigned menus have Sound in Settings, so the corner button hides while one is open.
    void Update()
    {
        if (button == null) return;
        bool show = !UIScreen.AnyOpen;
        if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
    }

    void Refresh()
    {
        if (label == null) return;
        label.text = Muted ? "MUTED" : "SOUND ON";
    }

    void Build()
    {
        var canvasObject = new GameObject("CornerMuteCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var image = new GameObject("Mute", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        buttonRect = image.rectTransform;
        buttonRect.SetParent(canvasObject.transform, false);
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        buttonRect.anchoredPosition = new Vector2(-ButtonMargin, -ButtonMargin);
        image.color = Color.white;

        var outline = image.GetComponent<Outline>();
        outline.effectColor = new Color(0.95f, 0.78f, 0.35f);
        outline.effectDistance = new Vector2(2f, -2f);

        button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        colors.highlightedColor = new Color(0.16f, 0.13f, 0.05f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.95f, 0.78f, 0.35f, 1f);
        button.colors = colors;
        button.onClick.AddListener(Toggle);

        label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        RectTransform labelRect = label.rectTransform;
        labelRect.SetParent(image.transform, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.font = font;
        label.fontSize = 24;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
    }
}
