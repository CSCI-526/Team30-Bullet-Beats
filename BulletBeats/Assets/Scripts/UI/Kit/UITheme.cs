using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// One place for the menu look (Cosmic Riot, Pixel Arcade): colours, fonts and the art files.
/// Every screen reads from here, so a colour or font changes everywhere at once, and new art
/// only has to be dropped into Resources/UI/Art with the same file name.
/// </summary>
public static class UITheme
{
    // Colour system from the UI rulebook.
    public static readonly Color SpaceBlack = Hex("0B0F1A");
    public static readonly Color ElectricCyan = Hex("0BE5FF");
    public static readonly Color ArcadeBlue = Hex("2B6BFF");
    public static readonly Color ImpactOrange = Hex("FF8A00");
    public static readonly Color AccentYellow = Hex("FFD54D");
    public static readonly Color White = Color.white;
    public static readonly Color MutedGrey = Hex("777777");
    public static readonly Color HotPink = Hex("FF3D9A");
    /// <summary>Restrained red/coral, only for Game Over and damage.</summary>
    public static readonly Color Danger = Hex("FF5A5A");
    /// <summary>Fill of floating cards and panels.</summary>
    public static readonly Color PanelFill = new Color(0.06f, 0.10f, 0.18f, 0.92f);

    /// <summary>Darkens the menu background on screens with a lot of information.</summary>
    public const float BusyScreenDim = 0.45f;

    // Art file names in Resources/UI/Art.
    public const string Logo = "logo";
    public const string MenuBackground = "menu_background";
    public const string MenuShip = "menu_ship";
    public const string IPod = "ipod";

    /// <summary>Button looks. Primary = orange outline with orange glow; Secondary = blue/cyan outline.
    /// SolidOrange is kept in case a solid button is wanted later.</summary>
    public enum ButtonStyle { Primary, Secondary, SolidOrange }

    public static string ButtonArt(ButtonStyle style) => style switch
    {
        ButtonStyle.Secondary => "btn_blue",
        ButtonStyle.SolidOrange => "btn_orange",
        _ => "btn_outline"
    };

    public static Color GlowColor(ButtonStyle style) =>
        style == ButtonStyle.Secondary ? ElectricCyan : ImpactOrange;

    // Fonts in Resources/UI/Fonts.
    const string DisplayFontFile = "Silkscreen-Bold";
    const string BodyFontFile = "PixelifySans";
    // Gameplay effects are smooth and futuristic rather than pixel (the menus stay pixel).
    const string HitFontFile = "Michroma-Regular";

    static TMP_FontAsset displayFont, bodyFont, hitFont;
    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    /// <summary>Chunky pixel font for titles, buttons and big labels.</summary>
    public static TMP_FontAsset DisplayFont => displayFont != null ? displayFont : displayFont = LoadFont(DisplayFontFile);
    /// <summary>Readable pixel font for small text.</summary>
    public static TMP_FontAsset BodyFont => bodyFont != null ? bodyFont : bodyFont = LoadFont(BodyFontFile);
    /// <summary>Small, wide, futuristic font for in-game hit feedback (PERFECT / GOOD / MISS).</summary>
    public static TMP_FontAsset HitFont => hitFont != null ? hitFont : hitFont = LoadFont(HitFontFile);

    /// <summary>A sprite from Resources/UI/Art, or null (with a warning) if the file is missing.</summary>
    public static Sprite Art(string name)
    {
        if (sprites.TryGetValue(name, out Sprite cached) && cached != null) return cached;
        Sprite sprite = Resources.Load<Sprite>("UI/Art/" + name);
        if (sprite == null) Debug.LogWarning($"UITheme: missing art Resources/UI/Art/{name}.png");
        sprites[name] = sprite;
        return sprite;
    }

    /// <summary>Text in the theme's look. Display text is upper case like the mockups.</summary>
    public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
        bool display = true, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(parent, false);
        label.font = display ? DisplayFont : BodyFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        if (display) label.fontStyle = FontStyles.UpperCase;
        label.text = text;
        return label;
    }

    static TMP_FontAsset LoadFont(string file)
    {
        Font font = Resources.Load<Font>("UI/Fonts/" + file);
        if (font == null)
        {
            Debug.LogWarning($"UITheme: missing font Resources/UI/Fonts/{file}");
            return TMP_Settings.defaultFontAsset;
        }
        // Dynamic, so any character the game needs is added to the atlas when first used.
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
        asset.name = file;
        return asset;
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
