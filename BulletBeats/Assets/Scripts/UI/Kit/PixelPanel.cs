using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The floating card used for the level-start card, Level Completed, Game Over, Pause and
/// Settings: <see cref="PixelArt.PanelFrame"/> stretched to any size, with a CanvasGroup so
/// <see cref="ScreenTransition"/> can fade it. Children go under <see cref="Content"/>.
/// </summary>
public static class PixelPanel
{
    /// <summary>A centred card of the given size. Returns the card's RectTransform.</summary>
    public static RectTransform Create(Transform parent, Vector2 size, Vector2 position = default, string name = "Panel")
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        var image = go.GetComponent<Image>();
        image.sprite = PixelArt.PanelFrame;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f / PixelArt.PanelPixelScale;
        return rect;
    }
}
