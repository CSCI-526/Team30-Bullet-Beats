using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Esc (or P) pauses a level and opens this menu: a short controls list and Resume / Restart /
/// Quit to menu. Esc again resumes. <see cref="GameFlow.SetPaused"/> decides when pausing is allowed
/// (not in the tutorial, where Esc skips). Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class PauseMenu : MonoBehaviour
{
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color backdropColor = new Color(0.02f, 0.01f, 0.06f, 0.82f);

    const string Controls = "W A S D   move\nClick   shoot on the beat\nMove onto a star   collect it";

    GameFlow flow;
    Font font;
    GameObject canvasObject;
    Button resume;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();
    }

    void OnEnable() => flow.PausedChanged += Show;
    void OnDisable() => flow.PausedChanged -= Show;

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame)
            flow.SetPaused(!flow.IsPaused);
    }

    void Show(bool paused)
    {
        canvasObject.SetActive(paused);
        if (paused && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resume.gameObject);
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("PauseMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        backdrop.rectTransform.SetParent(canvasObject.transform, false);
        backdrop.rectTransform.anchorMin = Vector2.zero;
        backdrop.rectTransform.anchorMax = Vector2.one;
        backdrop.rectTransform.offsetMin = backdrop.rectTransform.offsetMax = Vector2.zero;
        backdrop.color = backdropColor;

        AddText("PAUSED", 96, FontStyle.Bold, accentColor, 280f, 140f);
        AddText(Controls, 32, FontStyle.Normal, Color.white, 110f, 160f);

        resume = AddButton("RESUME", -60f, () => flow.SetPaused(false));
        AddButton("RESTART", -150f, flow.Retry);
        AddButton("QUIT TO MENU", -240f, flow.ShowTitle);
        canvasObject.SetActive(false);
    }

    void AddText(string text, int size, FontStyle style, Color color, float y, float height)
    {
        var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(canvasObject.transform, false);
        label.rectTransform.sizeDelta = new Vector2(1200f, height);
        label.rectTransform.anchoredPosition = new Vector2(0f, y);
        label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = text;
    }

    // Same look as the GameMenus buttons.
    Button AddButton(string text, float y, UnityEngine.Events.UnityAction onClick)
    {
        var image = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        image.rectTransform.SetParent(canvasObject.transform, false);
        image.rectTransform.sizeDelta = new Vector2(460f, 70f);
        image.rectTransform.anchoredPosition = new Vector2(0f, y);
        image.color = Color.white;

        var outline = image.GetComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        colors.highlightedColor = new Color(0.15f, 0.35f, 0.45f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accentColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(image.transform, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = font;
        label.fontSize = 30;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = text;
        return button;
    }
}
