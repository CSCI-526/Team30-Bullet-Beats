using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Base for the redesigned menu screens. Each screen builds its own overlay canvas (so screens
/// live in their own files and don't share one big panel script), shows and hides with the
/// shared <see cref="ScreenTransition"/> pop, and picks a button for keyboard focus.
/// While any screen is open, <see cref="AnyOpen"/> is true (the corner mute button hides then).
/// </summary>
public abstract class UIScreen : MonoBehaviour
{
    /// <summary>Number of screens currently open.</summary>
    static int openCount;
    public static bool AnyOpen => openCount > 0;

    protected RectTransform Root { get; private set; }
    protected RectTransform Content { get; private set; }
    public bool IsOpen { get; private set; }
    /// <summary>Frame the screen last opened or closed. A key press that opened or closed one screen
    /// must not also act on the screen underneath in the same frame.</summary>
    public int OpenedFrame { get; private set; } = -1;
    public int ClosedFrame { get; private set; } = -1;
    protected bool ChangedThisFrame => OpenedFrame == Time.frameCount || ClosedFrame == Time.frameCount;

    /// <summary>Draw order; later screens (Settings) sit above earlier ones (Main Menu).</summary>
    protected abstract int SortingOrder { get; }
    /// <summary>Builds the screen's contents under <see cref="Content"/>.</summary>
    protected abstract void Build();
    /// <summary>The button that gets keyboard focus when the screen opens.</summary>
    protected abstract Selectable FirstSelected { get; }

    protected virtual void Awake()
    {
        var canvasObject = new GameObject(GetType().Name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Root = (RectTransform)canvasObject.transform;

        Content = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        Content.SetParent(Root, false);
        Content.anchorMin = Vector2.zero;
        Content.anchorMax = Vector2.one;
        Content.offsetMin = Content.offsetMax = Vector2.zero;

        Build();
        Root.gameObject.SetActive(false);
    }

    protected virtual void OnDestroy()
    {
        if (IsOpen) openCount--;
    }

    public virtual void Open()
    {
        if (!IsOpen) openCount++;
        IsOpen = true;
        OpenedFrame = Time.frameCount;
        Root.gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(ScreenTransition.PopIn(Content));
        // No button starts highlighted, so the menu sits still until the mouse or keyboard is used;
        // the first arrow key, Tab or Enter gives focus to the first button (see Update).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public virtual void Close()
    {
        if (!IsOpen) return;
        openCount--;
        IsOpen = false;
        ClosedFrame = Time.frameCount;
        StopAllCoroutines();
        Root.gameObject.SetActive(false);
    }

    /// <summary>
    /// A mouse click on empty space clears the selected button, which leaves the arrow keys with
    /// nothing to move from. Give focus back on the next arrow key, Tab or Enter.
    /// </summary>
    protected virtual void Update()
    {
        if (!IsOpen || !IsTopmost() || EventSystem.current == null) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(Root)) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
            keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
            keyboard.tabKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            Focus();
    }

    /// <summary>True if no open screen is drawn above this one.</summary>
    bool IsTopmost()
    {
        foreach (UIScreen other in FindObjectsByType<UIScreen>(FindObjectsSortMode.None))
            if (other != this && other.IsOpen && other.SortingOrder > SortingOrder) return false;
        return true;
    }

    protected void Focus()
    {
        if (EventSystem.current != null && FirstSelected != null)
            EventSystem.current.SetSelectedGameObject(FirstSelected.gameObject);
    }

    /// <summary>A small framed button with an icon and a label, for the screen corners (Settings, Back).</summary>
    protected PixelButton CornerButton(string text, PixelArt.Icon icon, bool right, UnityEngine.Events.UnityAction onClick)
    {
        PixelButton button = PixelButton.Create(Content, text, onClick, UITheme.ButtonStyle.Secondary, 270f, 76f, 0f, 28f);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1f : 0f, 1f);
        rect.anchoredPosition = new Vector2(right ? -36f : 36f, -32f);

        // Icon on the left, text after it.
        Image image = PixelArt.IconImage(rect, icon, 34f, UITheme.White);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        image.rectTransform.anchoredPosition = new Vector2(64f, 0f);
        button.Label.rectTransform.offsetMin = new Vector2(76f, 0f);
        return button;
    }

    protected static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
