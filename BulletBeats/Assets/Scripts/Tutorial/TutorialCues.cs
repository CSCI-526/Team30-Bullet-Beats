using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The on-screen cues the <see cref="Tutorial"/> draws next to its words: W/A/S/D key caps at the
/// spots (lit when that's the key to press), a glowing "go here" ring and an arrow between spots, a mouse
/// with a pulsing button for shooting, hit pips and labels on asteroids and stars (TURBO, SHOOT NOW),
/// short labels that float up, progress dots and callouts. The mouse and crosshair are never locked.
/// Everything is drawn from code on the tutorial's canvas and animates on unscaled time.
/// </summary>
public class TutorialCues
{
    public static readonly PlayerMovement.Spot[] Spots =
        { PlayerMovement.Spot.Center, PlayerMovement.Spot.Left, PlayerMovement.Spot.Bottom, PlayerMovement.Spot.Right };

    const float KeySize = 46f;
    /// <summary>How far below the object the key cap and the label sit, at 1080p.</summary>
    const float KeyDrop = 112f, LabelDrop = 62f;
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

    RectTransform progress;
    readonly List<Image> progressDots = new List<Image>();

    readonly List<(Text text, Vector2 from, float start)> floaters = new List<(Text, Vector2, float)>();

    RectTransform callout, calloutArrow;
    Text calloutText;
    CalloutArrow calloutArrowDir;

    // Labels that follow an asteroid or a star.
    Text nowLabel, tagLabel;
    ChartTarget nowTarget, tagTarget;
    readonly List<(Text text, Transform follow)> worldTags = new List<(Text, Transform)>();
    readonly List<(Image aura, Transform follow)> auras = new List<(Image, Transform)>();

    static Sprite ring, disc, triangle;

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
        HideCallout();
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

    // ---------------------------------------------------------------- callout

    public enum CalloutArrow { Left, Right, Up }

    /// <summary>
    /// A name with an arrow pointing at part of the screen, e.g. YOUR LIVES under the hearts.
    /// <paramref name="anchor"/> is the screen corner it hangs from, <paramref name="position"/> the offset from it,
    /// and <paramref name="arrow"/> which way the arrow points.
    /// </summary>
    public void ShowCallout(string text, Vector2 anchor, Vector2 position, CalloutArrow arrow)
    {
        callout.anchorMin = callout.anchorMax = callout.pivot = anchor;
        callout.anchoredPosition = position;
        calloutText.text = text;
        calloutArrowDir = arrow;
        bool up = arrow == CalloutArrow.Up;
        bool left = arrow == CalloutArrow.Left;
        callout.sizeDelta = up ? new Vector2(330f, 120f) : new Vector2(440f, 70f);
        calloutText.alignment = up ? TextAnchor.LowerCenter : left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        calloutText.rectTransform.offsetMin = new Vector2(left ? 64f : 0f, 0f);
        calloutText.rectTransform.offsetMax = new Vector2(left || up ? 0f : -64f, up ? -56f : 0f);
        calloutArrow.anchorMin = calloutArrow.anchorMax = up ? new Vector2(0.5f, 1f) : new Vector2(left ? 0f : 1f, 0.5f);
        calloutArrow.localRotation = Quaternion.Euler(0f, 0f, up ? 90f : left ? 180f : 0f);
        callout.gameObject.SetActive(true);
    }

    public void HideCallout() => callout.gameObject.SetActive(false);

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

    /// <summary>A small soft glow in <paramref name="color"/> around a collectible, gently pulsing and bobbing.
    /// Replaces the big yellow ring so the power-up itself is the main visual. Cleared with the tags.</summary>
    public void Aura(Transform follow, Color color)
    {
        Image aura = AddImage(canvas.transform, HaloSprite(), Vector2.one * 150f);
        aura.color = new Color(color.r, color.g, color.b, 0.35f);
        aura.transform.SetAsFirstSibling(); // behind the other cues
        auras.Add((aura, follow));
    }

    public void ClearWorldTags()
    {
        foreach ((Image aura, Transform _) in auras)
            if (aura != null) Object.Destroy(aura.gameObject);
        auras.Clear();
        foreach ((Text text, Transform _) in worldTags)
            if (text != null) Object.Destroy(text.gameObject);
        worldTags.Clear();
    }

    // ---------------------------------------------------------------- labels and progress

    /// <summary>A short label that floats up from a screen point and fades.</summary>
    public void Float(string text, Vector2 screenPoint, Color color)
    {
        Text label = AddText(canvas.transform, 34, FontStyle.Bold, color);
        label.text = text;
        label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        floaters.Add((label, screenPoint, Time.unscaledTime));
    }

    /// <summary>Dots under the prompt, <paramref name="done"/> of <paramref name="total"/> filled. A total of 0 hides them.</summary>
    public void ShowProgress(int done, int total)
    {
        progress.gameObject.SetActive(total > 0);
        while (progressDots.Count < total) progressDots.Add(AddImage(progress, PixelDiscSprite(), Vector2.one * 28f));
        for (int i = 0; i < progressDots.Count; i++)
        {
            progressDots[i].gameObject.SetActive(i < total);
            progressDots[i].rectTransform.anchoredPosition = new Vector2((i - (total - 1) * 0.5f) * 40f, 0f);
            progressDots[i].color = i < done ? good : new Color(1f, 1f, 1f, 0.25f);
        }
    }

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
            key.rectTransform.position = at + Vector2.down * KeyDrop * scale;
            bool on = lit.Contains(spot);
            // Small floating key cap: dark see-through fill, thin cyan pixel frame, no solid cyan block.
            key.color = new Color(0.04f, 0.06f, 0.12f, 0.75f);
            key.transform.GetChild(0).GetComponent<Image>().color = on ? Color.Lerp(accent, Color.white, pulse * 0.3f) : new Color(1f, 1f, 1f, 0.35f);
            key.transform.localScale = Vector3.one * (on ? Mathf.Lerp(1f, 1.06f, pulse) : 1f);
            key.GetComponentInChildren<Text>().color = on ? accent : new Color(1f, 1f, 1f, 0.55f);

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
        if (callout.gameObject.activeSelf)
            calloutArrow.anchoredPosition = calloutArrowDir == CalloutArrow.Up
                ? new Vector2(0f, -26f + 8f * pulse)
                : new Vector2((calloutArrowDir == CalloutArrow.Left ? 28f : -28f) + (calloutArrowDir == CalloutArrow.Left ? -1f : 1f) * 8f * pulse, 0f);
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

        for (int i = auras.Count - 1; i >= 0; i--)
        {
            (Image aura, Transform follow) = auras[i];
            if (aura == null) { auras.RemoveAt(i); continue; }
            aura.gameObject.SetActive(follow != null);
            if (follow == null) continue;
            // Slow breathing glow that floats up and down a little.
            float t = Time.unscaledTime * 2.2f + i;
            aura.rectTransform.position = (Vector2)cam.WorldToScreenPoint(follow.position) + Vector2.up * Mathf.Sin(t) * 6f * scale;
            aura.transform.localScale = Vector3.one * (0.95f + 0.07f * Mathf.Sin(t * 1.3f));
            Color c = aura.color;
            c.a = 0.28f + 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f));
            aura.color = c;
        }

        for (int i = worldTags.Count - 1; i >= 0; i--)
        {
            (Text text, Transform follow) = worldTags[i];
            if (text == null) { worldTags.RemoveAt(i); continue; }
            bool alive = follow != null;
            text.gameObject.SetActive(alive);
            // Stacked, so two stars flying in from the same point don't print over each other.
            if (!alive) continue;
            Vector2 at = (Vector2)cam.WorldToScreenPoint(follow.position) + Vector2.down * (LabelDrop + 34f * i) * scale;
            // Keep the whole label on screen, e.g. above a ship parked at the right edge.
            float half = text.preferredWidth * 0.5f * scale + 14f * scale;
            at.x = Mathf.Clamp(at.x, half, Mathf.Max(half, Screen.width - half));
            text.rectTransform.position = at;
        }
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
            Image frame = AddImage(key.transform, PixelArt.HudFrame, Vector2.zero);
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 0.5f; // same pixel size as the HUD buttons: notched pixel corners
            frame.rectTransform.anchorMin = Vector2.zero;
            frame.rectTransform.anchorMax = Vector2.one;
            frame.rectTransform.offsetMin = frame.rectTransform.offsetMax = Vector2.zero;
            Text letter = AddText(key.transform, 26, FontStyle.Bold, Color.white);
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

        progress = new GameObject("Progress", typeof(RectTransform)).GetComponent<RectTransform>();
        progress.SetParent(canvas.transform, false);
        progress.anchorMin = progress.anchorMax = new Vector2(0.5f, 1f);
        progress.anchoredPosition = new Vector2(0f, -152f);
        progress.gameObject.SetActive(false);

        nowLabel = AddText(canvas.transform, 56, FontStyle.Bold, good); // yellow, like NICE!
        nowLabel.text = "SHOOT NOW!";
        nowLabel.rectTransform.sizeDelta = new Vector2(700f, 90f);
        nowLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        nowLabel.gameObject.SetActive(false);

        tagLabel = AddText(canvas.transform, 38, FontStyle.Bold, warn);
        tagLabel.rectTransform.sizeDelta = new Vector2(500f, 60f);
        tagLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        tagLabel.gameObject.SetActive(false);

        callout = new GameObject("Callout", typeof(RectTransform)).GetComponent<RectTransform>();
        callout.SetParent(canvas.transform, false);
        callout.sizeDelta = new Vector2(440f, 70f);
        calloutText = AddText(callout, 44, FontStyle.Bold, accent);
        calloutText.rectTransform.anchorMin = Vector2.zero;
        calloutText.rectTransform.anchorMax = Vector2.one;
        calloutText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        calloutArrow = AddImage(callout, TriangleSprite(), Vector2.one * 44f).rectTransform;
        calloutArrow.GetComponent<Image>().color = accent;
        callout.gameObject.SetActive(false);
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

    static Sprite halo;
    /// <summary>A soft glow ring: clear in the middle (so the object shows through), fading out at the edge.</summary>
    static Sprite HaloSprite()
    {
        if (halo != null) return halo;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.55f) / 0.45f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return halo = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    static Sprite RingSprite() => ring != null ? ring : ring = Shape(128, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); return d <= 1f && d >= 0.8f; });

    /// <summary>A triangle pointing right.</summary>
    static Sprite TriangleSprite()
    {
        if (triangle != null) return triangle;
        var tri = new[] { new Vector2(-0.6f, 0.85f), new Vector2(0.85f, 0f), new Vector2(-0.6f, -0.85f) };
        return triangle = Shape(64, (x, y) =>
        {
            bool inside = false;
            for (int i = 0, j = tri.Length - 1; i < tri.Length; j = i++)
                if ((tri[i].y > y) != (tri[j].y > y) &&
                    x < (tri[j].x - tri[i].x) * (y - tri[i].y) / (tri[j].y - tri[i].y) + tri[i].x)
                    inside = !inside;
            return inside;
        });
    }

    static Sprite pixelDisc;
    /// <summary>A chunky pixel circle (7 x 7 art pixels, hard edges) for the progress dots.</summary>
    static Sprite PixelDiscSprite()
    {
        if (pixelDisc != null) return pixelDisc;
        string[] rows = { "..###..", ".#####.", "#######", "#######", "#######", ".#####.", "..###.." };
        var tex = new Texture2D(7, 7, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
                tex.SetPixel(x, y, rows[y][x] == '#' ? Color.white : Color.clear);
        tex.Apply();
        return pixelDisc = Sprite.Create(tex, new Rect(0, 0, 7, 7), new Vector2(0.5f, 0.5f), 7f);
    }

    static Sprite DiscSprite() => disc != null ? disc : disc = Shape(64, (x, y) => x * x + y * y <= 1f);

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
