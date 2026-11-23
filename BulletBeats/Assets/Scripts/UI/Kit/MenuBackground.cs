using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shared galaxy backdrop for the main menu, song select and settings. It fills the screen
/// (cropping, never stretching) and hides the 3D scene behind the menus. <see cref="Dim"/>
/// darkens it on screens with a lot of information, as the rulebook asks.
/// </summary>
public class MenuBackground : MonoBehaviour
{
    Image shade;

    /// <summary>0 = full art (main menu), up to 1 = black.</summary>
    public float Dim
    {
        get => shade != null ? shade.color.a : 0f;
        set { if (shade != null) shade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(value)); }
    }

    /// <summary>Adds the backdrop as the first (back-most) child of <paramref name="canvas"/>.</summary>
    public static MenuBackground Create(Transform canvas, float dim = 0f)
    {
        var root = new GameObject("MenuBackground", typeof(RectTransform), typeof(MenuBackground));
        var rect = (RectTransform)root.transform;
        rect.SetParent(canvas, false);
        rect.SetAsFirstSibling();
        Stretch(rect);

        // Solid space black under the art, in case the art is missing or the window is an odd shape.
        var black = root.AddComponent<Image>();
        black.color = UITheme.SpaceBlack;

        var art = new GameObject("Art", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter)).GetComponent<Image>();
        art.rectTransform.SetParent(rect, false);
        art.sprite = UITheme.Art(UITheme.MenuBackground);
        art.raycastTarget = false;
        var fitter = art.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = art.sprite != null ? art.sprite.rect.width / art.sprite.rect.height : 16f / 9f;

        var background = root.GetComponent<MenuBackground>();
        background.shade = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        background.shade.rectTransform.SetParent(rect, false);
        Stretch(background.shade.rectTransform);
        background.shade.raycastTarget = false;
        background.Dim = dim;
        return background;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
