using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small pixel art drawn from code, so it needs no art files: the menu icons and the floating
/// card frame. Icons are white so an <see cref="Image"/> can tint them; '#' is the icon colour
/// and 'k' a dark outline. Each texture is point-filtered, so its pixels stay square at any size.
/// </summary>
public static class PixelArt
{
    public enum Icon { Gear, SoundOn, SoundOff, Clock, ArrowUp, ArrowDown, ArrowLeft, Enter, Star, Equalizer, Play }

    static readonly Dictionary<Icon, Sprite> icons = new Dictionary<Icon, Sprite>();
    static Sprite panelFrame;

    /// <summary>An icon as an Image under <paramref name="parent"/>, <paramref name="size"/> canvas units square.</summary>
    public static Image IconImage(Transform parent, Icon icon, float size, Color color)
    {
        var image = new GameObject(icon + " Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = new Vector2(size, size);
        image.sprite = IconSprite(icon);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    public static Sprite IconSprite(Icon icon)
    {
        if (icons.TryGetValue(icon, out Sprite sprite) && sprite != null) return sprite;
        sprite = FromRows(IconRows(icon), icon.ToString());
        icons[icon] = sprite;
        return sprite;
    }

    /// <summary>
    /// The floating card frame: dark translucent fill, a cyan pixel border with stepped corners,
    /// orange brackets at the top-left and bottom-right and small blue checkers at the other two
    /// corners. Use it Sliced; <see cref="PanelPixelScale"/> sets how big one art pixel is.
    /// </summary>
    public static Sprite PanelFrame
    {
        get
        {
            if (panelFrame != null) return panelFrame;
            const int size = 40, border = 12;
            var texture = NewTexture(size, size, "PanelFrame");
            Color clear = Color.clear;
            Color fill = UITheme.PanelFill;
            Color cyan = UITheme.ElectricCyan;
            Color cyanDim = new Color(cyan.r, cyan.g, cyan.b, 0.35f);
            Color orange = UITheme.ImpactOrange;
            Color blue = UITheme.ArcadeBlue;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance from each edge; corners are cut in three steps.
                int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
                int ring = Mathf.Min(dx, dy);
                bool corner = dx < 3 && dy < 3;
                Color c;
                if (corner && dx + dy < 3) c = clear;
                else if (corner && dx + dy == 3) c = cyan;
                else if (ring == 0) c = cyan;
                else if (ring == 1) c = cyanDim;
                else c = fill;
                pixels[y * size + x] = c;
            }

            // Orange brackets: top-left and bottom-right (y = 0 is the bottom row).
            for (int i = 3; i < 10; i++)
            {
                Set(pixels, size, i, size - 3, orange); Set(pixels, size, 3, size - 1 - i + 0, orange);
                Set(pixels, size, size - 1 - i, 2, orange); Set(pixels, size, size - 4, i - 1, orange);
            }
            Set(pixels, size, 11, size - 3, orange); Set(pixels, size, 13, size - 3, orange);
            Set(pixels, size, size - 12, 2, orange); Set(pixels, size, size - 14, 2, orange);

            // Blue checkers: top-right and bottom-left.
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                if ((x + y) % 2 != 0) continue;
                Set(pixels, size, size - 8 + x, size - 8 + y, blue);
                Set(pixels, size, 4 + x, 4 + y, blue);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            panelFrame = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            panelFrame.name = "PanelFrame";
            return panelFrame;
        }
    }

    /// <summary>Canvas units per art pixel of <see cref="PanelFrame"/>.</summary>
    public const float PanelPixelScale = 5f;

    static void Set(Color[] pixels, int size, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < size && y < size) pixels[y * size + x] = color;
    }

    static Sprite FromRows(string[] rows, string name)
    {
        int h = rows.Length, w = rows[0].Length;
        var texture = NewTexture(w, h, name);
        var pixels = new Color[w * h];
        Color dark = new Color(0.04f, 0.05f, 0.1f, 1f);
        for (int row = 0; row < h; row++)
        for (int x = 0; x < w; x++)
        {
            char ch = x < rows[row].Length ? rows[row][x] : '.';
            // Rows are written top-down; textures start at the bottom.
            pixels[(h - 1 - row) * w + x] = ch == '#' ? Color.white : ch == 'k' ? dark : Color.clear;
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        return sprite;
    }

    static Texture2D NewTexture(int w, int h, string name) => new Texture2D(w, h, TextureFormat.RGBA32, false)
    {
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp,
        name = name
    };

    static string[] IconRows(Icon icon) => icon switch
    {
        Icon.Gear => new[]
        {
            "......##......",
            "..##..##..##..",
            "..##########..",
            "...########...",
            "..####..####..",
            "####......####",
            "####......####",
            "..####..####..",
            "...########...",
            "..##########..",
            "..##..##..##..",
            "......##......",
        },
        Icon.SoundOn => new[]
        {
            "....#.........",
            "...##.....#...",
            "..###...#..#..",
            "####..#..#..#.",
            "####...#.#..#.",
            "####...#.#..#.",
            "####..#..#..#.",
            "..###...#..#..",
            "...##.....#...",
            "....#.........",
        },
        Icon.SoundOff => new[]
        {
            "....#.........",
            "...##.........",
            "..###..#...#..",
            "####....#.#...",
            "####.....#....",
            "####....#.#...",
            "####...#...#..",
            "..###.........",
            "...##.........",
            "....#.........",
        },
        Icon.Clock => new[]
        {
            "....######....",
            "..##......##..",
            ".#....##....#.",
            "#.....##.....#",
            "#.....##.....#",
            "#.....#####..#",
            "#............#",
            "#............#",
            ".#..........#.",
            "..##......##..",
            "....######....",
        },
        Icon.ArrowUp => new[]
        {
            "....##....",
            "...####...",
            "..######..",
            ".########.",
            "##########",
        },
        Icon.ArrowDown => new[]
        {
            "##########",
            ".########.",
            "..######..",
            "...####...",
            "....##....",
        },
        Icon.ArrowLeft => new[]
        {
            "....#",
            "...##",
            "..###",
            ".####",
            "#####",
            ".####",
            "..###",
            "...##",
            "....#",
        },
        Icon.Enter => new[]
        {
            "..........##",
            "..........##",
            "...#......##",
            "..##......##",
            ".############",
            "############",
            ".##.........",
            "..#.........",
        },
        Icon.Star => new[]
        {
            "......kk......",
            ".....k##k.....",
            ".....k##k.....",
            "kkkkk####kkkkk",
            "k############k",
            ".k##########k.",
            "..k########k..",
            "..k########k..",
            ".k####kk####k.",
            ".k###k..k###k.",
            "k##kk....kk##k",
            "kkk........kkk",
        },
        Icon.Equalizer => new[]
        {
            "......#.......",
            "......#...#...",
            "..#...#...#...",
            "..#.#.#.#.#...",
            "#.#.#.#.#.#.#.",
            "#.#.#.#.#.#.#.",
        },
        _ => new[]
        {
            "#.....",
            "###...",
            "#####.",
            "######",
            "#####.",
            "###...",
            "#.....",
        },
    };
}
