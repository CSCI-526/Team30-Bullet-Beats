using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The on-screen cues the <see cref="Tutorial"/> draws next to its words: W/A/S/D key caps at the
/// spots (lit when that's the key to press), a glowing "go here" ring and an arrow between spots, a mouse
/// with a pulsing button for shooting, hit pips and labels on asteroids and stars (TURBO, SHOOT NOW),
/// short labels that float up, a heart row for the goal, and the check mark + NEXT pop-up after each step
/// with an arrow cursor, since the crosshair is off while it waits.
/// Everything is drawn from code on the tutorial's canvas and animates on unscaled time.
/// </summary>
public class TutorialCues
{
    public static readonly PlayerMovement.Spot[] Spots =
        { PlayerMovement.Spot.Center, PlayerMovement.Spot.Left, PlayerMovement.Spot.Bottom, PlayerMovement.Spot.Right };

    const float KeySize = 64f;
    const float GlowSize = 190f;

    readonly Canvas canvas;
    readonly Font font;
    readonly Color accent, good, warn;
    readonly PlayerMovement movement;

    readonly Dictionary<PlayerMovement.Spot, Image> keys = new Dictionary<PlayerMovement.Spot, Image>();
    readonly Dictionary<PlayerMovement.Spot, Image> glows = new Dictionary<PlayerMovement.Spot, Image>();
    readonly HashSet<PlayerMovement.Spot> lit = new HashSet<PlayerMovement.Spot>();
    readonly HashSet<PlayerMovement.Spot> glowing = new HashSet<PlayerMovement.Spot>();
    RectTransform arrow, arrowHead;
    PlayerMovement.Spot arrowFrom, arrowTo;
    bool arrowOn, keysOn;

    RectTransform mouse;
    Image mouseButton;

    ChartTarget marked;
    RectTransform pips, crossed;
    readonly List<Image> pipImages = new List<Image>();

    RectTransform hearts, progress;
    readonly List<Image> heartImages = new List<Image>();
    readonly List<Image> progressDots = new List<Image>();

    readonly List<(Text text, Vector2 from, float start)> floaters = new List<(Text, Vector2, float)>();

    GameObject popup;
    Button next;
    bool nextPressed;
    RectTransform cursor;

    // Labels that follow an asteroid or a star.
    Text nowLabel, tagLabel;
    ChartTarget nowTarget, tagTarget;
    readonly List<(Text text, Transform follow)> worldTags = new List<(Text, Transform)>();

    static Sprite ring, disc, heart, pointer;

    public TutorialCues(Canvas canvas, Font font, PlayerMovement movement, Color accent, Color good, Color warn)
    {
        this.canvas = canvas;
        this.font = font;
        this.movement = movement;
        this.accent = accent;
        this.good = good;
        this.warn = warn;
        Build();
    }

    /// <summary>True while the check mark pop-up waits for NEXT.</summary>
    public bool WaitingForNext => popup != null && popup.activeSelf;

    // ---------------------------------------------------------------- keys, glows, arrow

    /// <summary>Shows the four key caps at their spots.</summary>
    public void ShowKeys(bool show) => keysOn = show;

    public void LightKey(PlayerMovement.Spot spot, bool on)
    {
        if (on) lit.Add(spot); else lit.Remove(spot);
    }

    public void Glow(PlayerMovement.Spot spot, bool on)
    {
        if (on) glowing.Add(spot); else glowing.Remove(spot);
    }

    public void ShowArrow(PlayerMovement.Spot from, PlayerMovement.Spot to)
    {
        arrowFrom = from;
        arrowTo = to;
        arrowOn = true;
    }

    public void HideArrow() => arrowOn = false;

    /// <summary>Clears keys, glows, the arrow, the mouse and asteroid marks.</summary>
    public void ClearGuides()
    {
        keysOn = false;
        lit.Clear();
        glowing.Clear();
        arrowOn = false;
        ShowMouse(false);
        Mark(null, MarkKind.None);
        ClearTag();
        HideNow();
        ClearWorldTags();
    }

    // ---------------------------------------------------------------- mouse

    public void ShowMouse(bool show) => mouse.gameObject.SetActive(show);

    // ---------------------------------------------------------------- asteroid marks

    public enum MarkKind { None, Pips, Crossed }
    MarkKind markKind;

    /// <summary>Hit pips (one per hit left) or a crossed-out crosshair on <paramref name="target"/>.</summary>
    public void Mark(ChartTarget target, MarkKind kind)
    {
        marked = target;
        markKind = target != null ? kind : MarkKind.None;
    }

    // ---------------------------------------------------------------- labels that follow things

    /// <summary>A big "SHOOT NOW!" under <paramref name="target"/>: the brackets are gold, so this is the moment.</summary>
    public void ShowNow(ChartTarget target) => nowTarget = target;

    public void HideNow() => nowTarget = null;

    /// <summary>A name above <paramref name="target"/>, e.g. TURBO.</summary>
    public void Tag(ChartTarget target, string text, Color color)
    {
        tagTarget = target;
        tagLabel.text = text;
        tagLabel.color = color;
    }

    public void ClearTag() => tagTarget = null;

    /// <summary>A short line above something in the world, e.g. a power-up star. Stays until <see cref="ClearWorldTags"/>.</summary>
    public void TagWorld(Transform follow, string text, Color color)
    {
        Text label = AddText(canvas.transform, 30, FontStyle.Bold, color);
        label.text = text;
        label.rectTransform.sizeDelta = new Vector2(700f, 50f);
        label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        worldTags.Add((label, follow));
    }

    public void ClearWorldTags()
    {
        foreach ((Text text, Transform _) in worldTags)
            if (text != null) Object.Destroy(text.gameObject);
        worldTags.Clear();
    }

    // ---------------------------------------------------------------- labels and hearts

    /// <summary>A short label that floats up from a screen point and fades.</summary>
    public void Float(string text, Vector2 screenPoint, Color color)
    {
        Text label = AddText(canvas.transform, 34, FontStyle.Bold, color);
        label.text = text;
        label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        floaters.Add((label, screenPoint, Time.unscaledTime));
    }

    /// <summary>A row of hearts under the banner, e.g. the lives you take into the level. 0 hides it.</summary>
    public void ShowHearts(int count, int filled, Color color)
    {
        hearts.gameObject.SetActive(count > 0);
        while (heartImages.Count < count)
        {
            var image = AddImage(hearts, HeartSprite(), Vector2.one * 56f);
            heartImages.Add(image);
        }
        for (int i = 0; i < heartImages.Count; i++)
        {
            heartImages[i].gameObject.SetActive(i < count);
            heartImages[i].rectTransform.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * 66f, 0f);
            heartImages[i].color = i < filled ? color : new Color(1f, 1f, 1f, 0.2f);
        }
    }

    /// <summary>Dots under the prompt, <paramref name="done"/> of <paramref name="total"/> filled. A total of 0 hides them.</summary>
    public void ShowProgress(int done, int total)
    {
        progress.gameObject.SetActive(total > 0);
        while (progressDots.Count < total) progressDots.Add(AddImage(progress, DiscSprite(), Vector2.one * 28f));
        for (int i = 0; i < progressDots.Count; i++)
        {
            progressDots[i].gameObject.SetActive(i < total);
            progressDots[i].rectTransform.anchoredPosition = new Vector2((i - (total - 1) * 0.5f) * 40f, 0f);
            progressDots[i].color = i < done ? good : new Color(1f, 1f, 1f, 0.25f);
        }
    }

    // ---------------------------------------------------------------- pop-up

    /// <summary>Shows the check mark with NEXT. <see cref="NextPressed"/> turns true on the button, Space or Enter.</summary>
    public void ShowPopup()
    {
        nextPressed = false;
        popup.SetActive(true);
    }

    public bool NextPressed => nextPressed;

    public void HidePopup() => popup.SetActive(false);

    // ---------------------------------------------------------------- per frame

    public void Update(Camera cam)
    {
        if (cam == null) return;
        float scale = canvas.scaleFactor;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);

        foreach (PlayerMovement.Spot spot in Spots)
        {
            Vector2 at = SpotOnScreen(cam, spot);
            Image key = keys[spot];
            key.gameObject.SetActive(keysOn);
            key.rectTransform.position = at + Vector2.down * 115f * scale;
            bool on = lit.Contains(spot);
            key.color = on ? Color.Lerp(accent, Color.white, pulse * 0.35f) : new Color(0.08f, 0.08f, 0.16f, 0.85f);
            key.transform.localScale = Vector3.one * (on ? Mathf.Lerp(1f, 1.12f, pulse) : 1f);
            key.GetComponentInChildren<Text>().color = on ? new Color(0.02f, 0.02f, 0.08f) : new Color(1f, 1f, 1f, 0.6f);

            Image glow = glows[spot];
            glow.gameObject.SetActive(glowing.Contains(spot));
            glow.rectTransform.position = at;
            glow.color = new Color(good.r, good.g, good.b, Mathf.Lerp(0.45f, 0.95f, pulse));
            glow.transform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.08f, pulse);
        }

        arrow.gameObject.SetActive(arrowOn);
        if (arrowOn)
        {
            Vector2 from = SpotOnScreen(cam, arrowFrom), to = SpotOnScreen(cam, arrowTo);
            Vector2 d = to - from;
            float length = d.magnitude;
            Vector2 dir = length > 0.01f ? d / length : Vector2.right;
            // Start and stop outside the glow ring.
            float gap = GlowSize * 0.55f * scale;
            Vector2 a = from + dir * gap, b = to - dir * gap;
            arrow.position = (a + b) * 0.5f;
            arrow.sizeDelta = new Vector2(Mathf.Max(0f, (b - a).magnitude / scale - 26f), 12f);
            arrow.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            arrowHead.position = b;
            arrowHead.rotation = arrow.rotation * Quaternion.Euler(0f, 0f, 45f);
            arrow.GetComponent<Image>().color = new Color(good.r, good.g, good.b, Mathf.Lerp(0.6f, 1f, pulse));
            arrowHead.GetComponent<Image>().color = arrow.GetComponent<Image>().color;
        }

        if (mouse.gameObject.activeSelf)
        {
            mouse.position = SpotOnScreen(cam, movement.Current) + new Vector2(150f, 40f) * scale;
            mouseButton.color = Color.Lerp(new Color(1f, 1f, 1f, 0.25f), good, Mathf.Repeat(Time.unscaledTime * 1.5f, 1f) < 0.5f ? 1f : 0f);
        }

        UpdateMark(cam, scale);
        UpdateFloaters(scale);
        UpdateLabels(cam, scale, pulse);
        UpdateCursor();

        if (popup.activeSelf)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) nextPressed = true;
        }
    }

    void UpdateLabels(Camera cam, float scale, float pulse)
    {
        bool now = nowTarget != null && nowTarget.TryGetScreenCircle(cam, out _, out _);
        nowLabel.gameObject.SetActive(now);
        if (now)
        {
            nowTarget.TryGetScreenCircle(cam, out Vector2 center, out float radius);
            nowLabel.rectTransform.position = center + Vector2.down * (radius + 62f * scale);
            nowLabel.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.12f, pulse);
        }

        bool tag = tagTarget != null && tagTarget.TryGetScreenCircle(cam, out _, out _);
        tagLabel.gameObject.SetActive(tag);
        if (tag)
        {
            tagTarget.TryGetScreenCircle(cam, out Vector2 center, out float radius);
            tagLabel.rectTransform.position = center + Vector2.up * (radius + 90f * scale);
        }

        for (int i = worldTags.Count - 1; i >= 0; i--)
        {
            (Text text, Transform follow) = worldTags[i];
            if (text == null) { worldTags.RemoveAt(i); continue; }
            bool alive = follow != null;
            text.gameObject.SetActive(alive);
            // Stacked, so two stars flying in from the same point don't print over each other.
            if (alive) text.rectTransform.position = (Vector2)cam.WorldToScreenPoint(follow.position) + Vector2.up * (110f + 48f * i) * scale;
        }
    }

    /// <summary>While NEXT waits, the crosshair is off and the system cursor is hidden, so draw an arrow at the mouse.</summary>
    void UpdateCursor()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool show = popup.activeSelf && mouse != null;
        cursor.gameObject.SetActive(show);
        if (show) cursor.position = mouse.position.ReadValue();
    }

    void UpdateMark(Camera cam, float scale)
    {
        bool has = marked != null && markKind != MarkKind.None && marked.TryGetScreenCircle(cam, out _, out _);
        pips.gameObject.SetActive(has && markKind == MarkKind.Pips);
        crossed.gameObject.SetActive(has && markKind == MarkKind.Crossed);
        if (!has) return;

        marked.TryGetScreenCircle(cam, out Vector2 center, out float radius);
        if (markKind == MarkKind.Pips)
        {
            int left = marked.HitsLeft;
            while (pipImages.Count < left) pipImages.Add(AddImage(pips, DiscSprite(), Vector2.one * 26f));
            for (int i = 0; i < pipImages.Count; i++)
            {
                pipImages[i].gameObject.SetActive(i < left);
                pipImages[i].rectTransform.anchoredPosition = new Vector2((i - (left - 1) * 0.5f) * 36f, 0f);
                pipImages[i].color = warn;
            }
            pips.position = center + Vector2.up * (radius + 30f * scale);
        }
        else
        {
            crossed.position = center;
            crossed.sizeDelta = Vector2.one * Mathf.Max(90f, radius * 2.2f / scale);
        }
    }

    void UpdateFloaters(float scale)
    {
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            (Text text, Vector2 from, float start) = floaters[i];
            float t = (Time.unscaledTime - start) / 1.4f;
            if (t >= 1f || text == null)
            {
                if (text != null) Object.Destroy(text.gameObject);
                floaters.RemoveAt(i);
                continue;
            }
            text.rectTransform.position = from + Vector2.up * (40f + 60f * t) * scale;
            Color c = text.color;
            c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            text.color = c;
        }
    }

    /// <summary>Screen point of the ship's body at <paramref name="spot"/>.</summary>
    public Vector2 SpotOnScreen(Camera cam, PlayerMovement.Spot spot)
    {
        Vector3 bodyOffset = BodyCenter() - movement.transform.position;
        return cam.WorldToScreenPoint(movement.WorldPositionOf(spot) + bodyOffset);
    }

    Vector3 BodyCenter()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (MeshRenderer r in movement.GetComponentsInChildren<MeshRenderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        return found ? bounds.center : movement.transform.position;
    }

    // ---------------------------------------------------------------- building

    void Build()
    {
        foreach (PlayerMovement.Spot spot in Spots)
        {
            Image glow = AddImage(canvas.transform, RingSprite(), Vector2.one * GlowSize);
            glow.gameObject.SetActive(false);
            glows[spot] = glow;

            Image key = AddImage(canvas.transform, null, Vector2.one * KeySize);
            key.gameObject.AddComponent<Outline>().effectColor = accent;
            Text letter = AddText(key.transform, 36, FontStyle.Bold, Color.white);
            letter.rectTransform.sizeDelta = Vector2.one * KeySize;
            letter.text = KeyFor(spot);
            key.gameObject.SetActive(false);
            keys[spot] = key;
        }

        arrow = AddImage(canvas.transform, null, new Vector2(100f, 12f)).rectTransform;
        arrowHead = AddImage(canvas.transform, null, new Vector2(30f, 30f)).rectTransform;
        arrowHead.SetParent(arrow, true);
        arrow.gameObject.SetActive(false);

        // A mouse: a tall body with the left button lit.
        Image body = AddImage(canvas.transform, null, new Vector2(56f, 84f));
        body.color = new Color(0.08f, 0.08f, 0.16f, 0.9f);
        body.gameObject.AddComponent<Outline>().effectColor = Color.white;
        mouse = body.rectTransform;
        mouseButton = AddImage(mouse, null, new Vector2(24f, 34f));
        mouseButton.rectTransform.anchoredPosition = new Vector2(-12f, 22f);
        mouse.gameObject.SetActive(false);

        pips = new GameObject("HitPips", typeof(RectTransform)).GetComponent<RectTransform>();
        pips.SetParent(canvas.transform, false);
        pips.gameObject.SetActive(false);

        // A crosshair ring with a bar through it: "can't shoot this".
        Image cross = AddImage(canvas.transform, RingSprite(), Vector2.one * 120f);
        cross.color = warn;
        crossed = cross.rectTransform;
        Image bar = AddImage(crossed, null, new Vector2(10f, 10f));
        bar.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        bar.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        bar.rectTransform.sizeDelta = new Vector2(12f, 0f);
        bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        bar.color = warn;
        crossed.gameObject.SetActive(false);

        hearts = new GameObject("Hearts", typeof(RectTransform)).GetComponent<RectTransform>();
        hearts.SetParent(canvas.transform, false);
        hearts.anchorMin = hearts.anchorMax = new Vector2(0.5f, 1f);
        hearts.anchoredPosition = new Vector2(0f, -236f);
        hearts.gameObject.SetActive(false);

        progress = new GameObject("Progress", typeof(RectTransform)).GetComponent<RectTransform>();
        progress.SetParent(canvas.transform, false);
        progress.anchorMin = progress.anchorMax = new Vector2(0.5f, 1f);
        progress.anchoredPosition = new Vector2(0f, -214f);
        progress.gameObject.SetActive(false);

        nowLabel = AddText(canvas.transform, 56, FontStyle.Bold, good);
        nowLabel.text = "SHOOT NOW!";
        nowLabel.rectTransform.sizeDelta = new Vector2(700f, 90f);
        nowLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        nowLabel.gameObject.SetActive(false);

        tagLabel = AddText(canvas.transform, 38, FontStyle.Bold, warn);
        tagLabel.rectTransform.sizeDelta = new Vector2(500f, 60f);
        tagLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        tagLabel.gameObject.SetActive(false);

        BuildPopup();

        // Drawn last so it sits over everything, with its tip on the mouse.
        Image arrowImage = AddImage(canvas.transform, PointerSprite(), Vector2.one * 56f);
        arrowImage.rectTransform.pivot = new Vector2(0.15f, 0.95f);
        arrowImage.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        cursor = arrowImage.rectTransform;
        cursor.gameObject.SetActive(false);
    }

    void BuildPopup()
    {
        var panel = AddImage(canvas.transform, null, new Vector2(380f, 300f));
        panel.color = new Color(0.02f, 0.01f, 0.06f, 1f); // opaque, or the gold outline shows through
        panel.gameObject.AddComponent<Outline>().effectColor = good;
        panel.raycastTarget = true;
        popup = panel.gameObject;

        // Check mark from two bars.
        Image shortArm = AddImage(panel.transform, null, new Vector2(16f, 50f));
        shortArm.rectTransform.anchoredPosition = new Vector2(-28f, 78f);
        shortArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        shortArm.color = good;
        Image longArm = AddImage(panel.transform, null, new Vector2(16f, 96f));
        longArm.rectTransform.anchoredPosition = new Vector2(10f, 92f);
        longArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
        longArm.color = good;

        var image = new GameObject("NEXT", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        image.rectTransform.SetParent(panel.transform, false);
        image.rectTransform.sizeDelta = new Vector2(260f, 70f);
        image.rectTransform.anchoredPosition = new Vector2(0f, -64f);
        image.GetComponent<Outline>().effectColor = accent;
        next = image.GetComponent<Button>();
        ColorBlock colors = next.colors;
        colors.normalColor = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        colors.highlightedColor = new Color(0.15f, 0.35f, 0.45f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accent;
        next.colors = colors;
        next.onClick.AddListener(() => nextPressed = true);
        Text label = AddText(image.transform, 32, FontStyle.Bold, Color.white);
        label.rectTransform.sizeDelta = image.rectTransform.sizeDelta;
        label.text = "NEXT";

        Text hint = AddText(panel.transform, 24, FontStyle.Normal, new Color(1f, 1f, 1f, 0.75f));
        hint.rectTransform.anchoredPosition = new Vector2(0f, -122f);
        hint.rectTransform.sizeDelta = new Vector2(340f, 36f);
        hint.text = "or press SPACE";

        popup.SetActive(false);
    }

    static string KeyFor(PlayerMovement.Spot spot) => spot switch
    {
        PlayerMovement.Spot.Center => "W",
        PlayerMovement.Spot.Left => "A",
        PlayerMovement.Spot.Right => "D",
        _ => "S"
    };

    Image AddImage(Transform parent, Sprite sprite, Vector2 size)
    {
        var image = new GameObject("Cue", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = size;
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    Text AddText(Transform parent, int size, FontStyle style, Color color)
    {
        var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(400f, size * 1.5f);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    // ---------------------------------------------------------------- sprites drawn in code

    static Sprite RingSprite() => ring != null ? ring : ring = Shape(128, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); return d <= 1f && d >= 0.8f; });

    /// <summary>A mouse pointer (tip at the top left), drawn white.</summary>
    static Sprite PointerSprite()
    {
        if (pointer != null) return pointer;
        var outline = new[]
        {
            new Vector2(-0.7f, 0.9f), new Vector2(-0.7f, -0.65f), new Vector2(-0.3f, -0.3f), new Vector2(-0.05f, -0.9f),
            new Vector2(0.2f, -0.8f), new Vector2(-0.02f, -0.22f), new Vector2(0.45f, -0.22f)
        };
        return pointer = Shape(64, (x, y) =>
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                if ((outline[i].y > y) != (outline[j].y > y) &&
                    x < (outline[j].x - outline[i].x) * (y - outline[i].y) / (outline[j].y - outline[i].y) + outline[i].x)
                    inside = !inside;
            return inside;
        });
    }

    static Sprite DiscSprite() => disc != null ? disc : disc = Shape(64, (x, y) => x * x + y * y <= 1f);

    static Sprite HeartSprite() => heart != null ? heart : heart = Shape(64, (x, y) =>
    {
        // Heart curve (x² + y² - 1)³ - x²y³ <= 0, fitted to the square (same as the HUD's).
        x *= 1.3f;
        y = y * 1.3f + 0.15f;
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y <= 0f;
    });

    /// <summary>A white anti-aliased shape; <paramref name="inside"/> gets points in -1..1.</summary>
    static Sprite Shape(int size, System.Func<float, float, bool> inside)
    {
        const int samples = 4;
        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            int hits = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                float x = ((px + (sx + 0.5f) / samples) / size) * 2f - 1f;
                float y = ((py + (sy + 0.5f) / samples) / size) * 2f - 1f;
                if (inside(x, y)) hits++;
            }
            pixels[py * size + px] = new Color32(255, 255, 255, (byte)(255 * hits / (samples * samples)));
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
