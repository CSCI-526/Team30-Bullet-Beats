using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A menu button in the theme's look: a 9-sliced pixel frame that swaps to its glowing frame on
/// hover or keyboard focus and to its pressed frame on click, with white pixel text on top.
/// The selected button also pulses gently. Animations use unscaled time, because the game is
/// paused (time scale 0) while menus are up. Build one with <see cref="Create"/>.
/// </summary>
[RequireComponent(typeof(Button))]
public class PixelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public const float DefaultWidth = 520f;
    public const float DefaultHeight = 92f;

    [Tooltip("How much the button grows while hovered or selected.")]
    public float hoverScale = 1.04f;
    [Tooltip("Pulses per second of the selected button.")]
    public float pulseSpeed = 1.6f;

    public Button Button { get; private set; }
    public TextMeshProUGUI Label { get; private set; }

    bool hovered, selected;
    float scale = 1f;

    /// <summary>Makes a themed button under <paramref name="parent"/>, centred, at <paramref name="y"/>.</summary>
    public static PixelButton Create(Transform parent, string text, UnityAction onClick,
        UITheme.ButtonStyle style = UITheme.ButtonStyle.Primary,
        float width = DefaultWidth, float height = DefaultHeight, float y = 0f, float fontSize = 40f)
    {
        var go = new GameObject(text + " Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(PixelButton));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(0f, y);

        string art = UITheme.ButtonArt(style);
        var image = go.GetComponent<Image>();
        image.sprite = UITheme.Art(art + "_normal");
        image.type = Image.Type.Sliced;
        // Source frames are 256 px tall; keep the pixel border thickness in proportion to the button.
        image.pixelsPerUnitMultiplier = 256f / height;

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = UITheme.Art(art + "_hover"),
            selectedSprite = UITheme.Art(art + "_hover"),
            pressedSprite = UITheme.Art(art + "_pressed"),
            disabledSprite = UITheme.Art(art + "_normal")
        };
        if (onClick != null) button.onClick.AddListener(onClick);

        var pixelButton = go.GetComponent<PixelButton>();
        pixelButton.Button = button;
        pixelButton.Label = UITheme.Text(rect, text, fontSize, UITheme.White);
        Stretch(pixelButton.Label.rectTransform);
        return pixelButton;
    }

    void Awake()
    {
        if (Button == null) Button = GetComponent<Button>();
    }

    void OnDisable()
    {
        hovered = selected = false;
        scale = 1f;
        transform.localScale = Vector3.one;
    }

    void Update()
    {
        bool active = (hovered || selected) && Button.interactable;
        float target = active ? hoverScale : 1f;
        if (selected && Button.interactable)
            target += 0.012f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);
        scale = Mathf.MoveTowards(scale, target, Time.unscaledDeltaTime * 0.6f);
        transform.localScale = new Vector3(scale, scale, 1f);

        if (Label != null)
            Label.alpha = Button.interactable ? 1f : 0.45f;
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;
    public void OnPointerExit(PointerEventData eventData) => hovered = false;
    public void OnSelect(BaseEventData eventData) => selected = true;
    public void OnDeselect(BaseEventData eventData) => selected = false;

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
