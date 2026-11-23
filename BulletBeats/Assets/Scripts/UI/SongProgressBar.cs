using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A thin bar that fills as the song plays, with a small 8-bit spaceship riding the fill edge, so the
/// player can see how far through the level they are. Two layouts to compare: a vertical bar on the
/// right edge, or a short horizontal bar under the clock in the top-right corner.
/// Builds its own overlay canvas and draws the ship in code, so it needs no scene setup or art.
/// Hidden outside play and in the tutorial (no song there), unless a preview is set.
/// </summary>
public class SongProgressBar : MonoBehaviour
{
    public enum Layout { VerticalRight, HorizontalTopRight }

    [Header("Layout (try both, keep the one that looks better)")]
    public Layout layout = Layout.VerticalRight;
    [Tooltip("Vertical bar: fill from the bottom up. Off: from the top down.")]
    public bool fillFromBottom = true;
    [Min(2f)] public float thickness = 8f;

    [Header("Vertical bar (pixels at 1080p)")]
    public float rightMargin = 14f;
    [Tooltip("Space kept clear above the bar, for the SOUND ON button and the clock under it.")]
    public float topClearance = ClockBottom + 24f; // room for half the ship icon, which rides past the bar's end
    public float bottomClearance = 60f;

    [Header("Horizontal bar (pixels at 1080p)")]
    [Tooltip("Matches the clock's right edge.")]
    public float horizontalRightMargin = 28f;
    [Tooltip("Distance from the top of the screen: just under the clock.")]
    public float horizontalTop = ClockBottom + 24f;
    public float horizontalLength = 170f;

    /// <summary>Bottom of the level clock, in pixels from the top at 1080p (it sits under the SOUND ON button).</summary>
    const float ClockBottom = LevelClock.Top + LevelClock.FontSize * 1.3f;

    [Header("Look")]
    public Color trackColor = new Color(1f, 1f, 1f, 0.18f);
    public Color fillColor = new Color(0.3f, 0.9f, 1f, 0.85f);

    [Header("Ship icon")]
    public bool showShip = true;
    [Tooltip("Screen pixels per sprite pixel at 1080p. 3 makes the ship 33 px.")]
    [Min(1)] public int shipPixelScale = 3;
    public Color shipColor = new Color(0.82f, 0.95f, 1f);
    public Color cockpitColor = new Color(0.3f, 0.9f, 1f);
    public Color flameColor = new Color(1f, 0.6f, 0.2f);
    [Tooltip("Seconds between the two flame flickers. 0 = steady flame.")]
    public float flameFlickerSeconds = 0.15f;

    /// <summary>A tutorial can show the bar at a fixed fill (0-1) with no song. Negative = off.</summary>
    public float previewFill = -1f;

    // 11 x 11, nose up. # = hull, o = cockpit, f = flame.
    static readonly string[] ShipRows =
    {
        ".....#.....",
        ".....#.....",
        "....###....",
        "....###....",
        "...##o##...",
        ".#.#ooo#.#.",
        ".#.#####.#.",
        "###########",
        "##.#####.##",
        "#..#####..#",
        "....f.f....",
    };

    GameObject canvasObject;
    RectTransform bar, fill, ship;
    Image shipImage;
    Sprite shipSprite, shipSpriteFlicker;
    Layout appliedLayout;
    bool appliedFromBottom;

    void Awake()
    {
        canvasObject = new GameObject("SongProgressBar", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 88;   // under the HUD, aim and warning canvases
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var track = new GameObject("Track", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        bar = track.rectTransform;
        bar.SetParent(canvasObject.transform, false);
        track.color = trackColor;
        track.raycastTarget = false;

        var fillImage = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        fill = fillImage.rectTransform;
        fill.SetParent(bar, false);
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;

        // Made last so the ship is drawn on top of the fill.
        shipSprite = MakeShipSprite(true);
        shipSpriteFlicker = MakeShipSprite(false);
        shipImage = new GameObject("Ship", typeof(RectTransform), typeof(Image), typeof(Outline)).GetComponent<Image>();
        ship = shipImage.rectTransform;
        ship.SetParent(bar, false);
        shipImage.sprite = shipSprite;
        shipImage.preserveAspect = true;
        shipImage.raycastTarget = false;
        float size = ShipRows.Length * shipPixelScale;
        ship.sizeDelta = new Vector2(size, size);
        shipImage.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.55f);
        shipImage.GetComponent<Outline>().effectDistance = new Vector2(shipPixelScale * 0.6f, -shipPixelScale * 0.6f);

        ApplyLayout();
        canvasObject.SetActive(false);
    }

    void OnDestroy()
    {
        DestroySprite(shipSprite);
        DestroySprite(shipSpriteFlicker);
        if (canvasObject != null) Destroy(canvasObject);
    }

    static void DestroySprite(Sprite sprite)
    {
        if (sprite == null) return;
        Destroy(sprite.texture);
        Destroy(sprite);
    }

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // F9 flips between the two layouts, for comparing them side by side.
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            layout = layout == Layout.VerticalRight ? Layout.HorizontalTopRight : Layout.VerticalRight;
#endif
        GameFlow flow = GameFlow.Instance;
        bool playing = flow != null && flow.State == GameState.Playing && !flow.IsTutorial;
        bool show = previewFill >= 0f || playing;
        if (canvasObject.activeSelf != show) canvasObject.SetActive(show);
        if (!show) return;

        if (layout != appliedLayout || fillFromBottom != appliedFromBottom) ApplyLayout();

        float amount = previewFill;
        if (amount < 0f)
        {
            BeatClock clock = BeatClock.Instance;
            float length = clock != null ? DurationOf(clock) : 0f;
            amount = length > 0.1f ? Mathf.Clamp01(clock.SongTime / length) : 0f;   // negative during the count-in
        }
        SetFill(amount);
        UpdateShip(amount);
    }

    /// <summary>Song length in seconds, matching when the level actually ends.</summary>
    static float DurationOf(BeatClock clock)
    {
        // Hand-made charts fade out and end at their own end time.
        ScriptedLevel scripted = ScriptedLevel.Instance;
        if (scripted != null && scripted.Chart != null && scripted.Chart.endTime > 0f) return scripted.Chart.endTime;
        if (clock.music != null && clock.music.clip != null) return clock.music.clip.length;
        return clock.songLength;
    }

    void ApplyLayout()
    {
        appliedLayout = layout;
        appliedFromBottom = fillFromBottom;

        if (layout == Layout.VerticalRight)
        {
            // Full height of the screen minus the clearances, stuck to the right edge.
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(thickness, -(topClearance + bottomClearance));
            bar.anchoredPosition = new Vector2(-rightMargin, (bottomClearance - topClearance) * 0.5f);
        }
        else
        {
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(horizontalLength, thickness);
            bar.anchoredPosition = new Vector2(-horizontalRightMargin, -horizontalTop);
        }
    }

    void SetFill(float amount)
    {
        if (layout == Layout.VerticalRight)
        {
            fill.anchorMin = new Vector2(0f, fillFromBottom ? 0f : 1f - amount);
            fill.anchorMax = new Vector2(1f, fillFromBottom ? amount : 1f);
        }
        else
        {
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(amount, 1f);
        }
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }

    /// <summary>Puts the ship on the leading edge of the fill, nose pointing the way the bar is filling.</summary>
    void UpdateShip(float amount)
    {
        shipImage.enabled = showShip;
        if (!showShip) return;

        if (layout == Layout.VerticalRight)
        {
            float y = fillFromBottom ? amount : 1f - amount;
            ship.anchorMin = ship.anchorMax = new Vector2(0.5f, y);
            ship.localRotation = Quaternion.Euler(0f, 0f, fillFromBottom ? 0f : 180f);   // nose up, or down
        }
        else
        {
            ship.anchorMin = ship.anchorMax = new Vector2(amount, 0.5f);
            ship.localRotation = Quaternion.Euler(0f, 0f, -90f);                         // nose right
        }
        ship.anchoredPosition = Vector2.zero;

        // Flickering flame: two frames, swapped on a timer (unscaled, so it keeps going while paused).
        bool alternate = flameFlickerSeconds > 0f && Mathf.FloorToInt(Time.unscaledTime / flameFlickerSeconds) % 2 == 1;
        shipImage.sprite = alternate ? shipSpriteFlicker : shipSprite;
    }

    /// <summary>Draws the ship into a tiny texture, crisp (no smoothing). Two frames differ only in the flame.</summary>
    Sprite MakeShipSprite(bool flameOn)
    {
        int h = ShipRows.Length, w = ShipRows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "SongProgressShip"
        };
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int row = 0; row < h; row++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = ShipRows[row][x];
                Color color = c == '#' ? shipColor
                            : c == 'o' ? cockpitColor
                            : c == 'f' ? (flameOn ? flameColor : clear)
                            : clear;
                tex.SetPixel(x, h - 1 - row, color);   // the texture's origin is the bottom left
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>The bar's rectangle in screen pixels, so the tutorial can point at it.</summary>
    public bool TryGetScreenRect(out Rect rect)
    {
        rect = default;
        if (bar == null) return false;
        var corners = new Vector3[4];
        bar.GetWorldCorners(corners);   // an overlay canvas is in screen pixels
        rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        return true;
    }
}