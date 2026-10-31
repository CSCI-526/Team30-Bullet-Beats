using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Global mute control. Hidden until the cursor is in the top-right corner.
/// Each play session starts muted; the choice is kept across scene loads in that session.
/// </summary>
public class CornerMute : MonoBehaviour
{
    public static bool Muted { get; private set; } = true;

    const float CornerWidth = 0.2f;
    const float CornerHeight = 0.18f;

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

    void Update()
    {
        Mouse mouse = Mouse.current;
        bool show = false;
        if (mouse != null)
        {
            Vector2 cursor = mouse.position.ReadValue();
            show = cursor.x >= Screen.width * (1f - CornerWidth)
                && cursor.y >= Screen.height * (1f - CornerHeight);
        }
        if (button != null && button.gameObject.activeSelf != show)
            button.gameObject.SetActive(show);
    }

    void Toggle()
    {
        Muted = !Muted;
        Apply();
        Refresh();
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
        buttonRect.sizeDelta = new Vector2(220f, 56f);
        buttonRect.anchoredPosition = new Vector2(-28f, -28f);
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
        button.gameObject.SetActive(false);

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
