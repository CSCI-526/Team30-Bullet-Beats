using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The difficulty step of level select: Easy / Medium / Hard are three plain circles and the player shoots
/// one from a small ship at the bottom. The shot circle breaks into squares that fly together and start the
/// level. Aim with the mouse and click, or use A / D (arrows) and Space. Esc goes back. Everything is drawn
/// from code, so it needs no art. Added to the difficulty panel by <see cref="GameMenus"/>.
/// </summary>
public class LevelSelectOrbs : MonoBehaviour
{
    enum Phase { Difficulty, Launching }

    class Orb
    {
        public RectTransform root;
        public Image body, ring;
        public Vector2 home, pos;
        public float bobPhase, radius, hover, scale, targetScale = 1f;
        public Color color;
        public GameFlow.Level level;
        public Difficulty difficulty;
        public bool selectable = true;
    }

    class Bullet
    {
        public RectTransform rt;
        public Vector2 from, to;
        public float t, duration;
        public Orb target;
    }

    class Shard
    {
        public RectTransform rt;
        public Image image;
        public Vector2 start, burst;
        public float size, spin, delay;
    }

        const float DifficultyDiameter = 200f;
    const float BulletSpeed = 2600f;
    const float FireCooldown = 0.12f;
    const int MaxBullets = 6;
    static readonly Vector2 ShipPos = new Vector2(0f, -350f);

    static readonly string[] DifficultyNames = { "EASY", "MEDIUM", "HARD" };
    static readonly Color[] DifficultyColors =
    {
        new Color(0.35f, 1f, 0.55f), new Color(1f, 0.85f, 0.3f), new Color(1f, 0.35f, 0.35f)
    };

    static Sprite circleSprite, ringSprite, shipSprite;

    GameFlow flow;
    Font font;
    Color accent;
    Action onBack;
    Action<GameFlow.Level, Difficulty> onStart;
    bool built;

    RectTransform stage, ship, backRect;
    Text songTitle;
    Image flash;
    Phase phase;
    GameFlow.Level chosenLevel;
    Color chosenColor;

    readonly List<Orb> orbs = new List<Orb>();
    readonly List<Bullet> bullets = new List<Bullet>();
    readonly List<Shard> shards = new List<Shard>();
    readonly List<GameObject> extras = new List<GameObject>();

    bool useMouse = true;
    int keyIndex;
    float nextFire, recoil, shipAngle;
    float launchTime = -1f;

    /// <summary>The Back button, so the menu can give it keyboard focus.</summary>
    public Button BackButton { get; private set; }

    public void Init(GameFlow gameFlow, Font uiFont, Color accentColor, Action back, Action<GameFlow.Level, Difficulty> start)
    {
        flow = gameFlow;
        font = uiFont;
        accent = accentColor;
        onBack = back;
        onStart = start;

        stage = NewRect("Stage", transform);
        stage.sizeDelta = Vector2.zero;

        flash = NewImage("Flash", stage, null, new Color(1f, 1f, 1f, 0f), Vector2.zero);
        flash.rectTransform.anchorMin = Vector2.zero;
        flash.rectTransform.anchorMax = Vector2.one;
        flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;
        flash.transform.SetAsLastSibling();

        songTitle = NewText("SongTitle", stage, "", 64, FontStyle.Bold, Color.white, new Vector2(1200f, 90f));
        songTitle.rectTransform.anchoredPosition = new Vector2(0f, 330f);

        ship = NewImage("Ship", stage, ShipSprite(), accent, new Vector2(64f, 76f)).rectTransform;
        ship.anchoredPosition = ShipPos;

        Text hint = NewText("Hint", stage, "SHOOT TO PICK", 26, FontStyle.Bold, new Color(1f, 1f, 1f, 0.55f), new Vector2(600f, 40f));
        hint.rectTransform.anchoredPosition = new Vector2(0f, -455f);

        BuildBackButton();
        built = true;
    }

    void OnEnable()
    {
        if (built) Clear();
    }

    // ------------------------------------------------------------------ build

    void BuildBackButton()
    {
        Image image = NewImage("Back", stage, null, new Color(0.04f, 0.05f, 0.08f, 0.95f), new Vector2(200f, 64f));
        image.raycastTarget = true;
        backRect = image.rectTransform;
        backRect.anchoredPosition = new Vector2(-620f, -470f);
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(2f, -2f);

        var button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.6f, 0.9f, 1f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accent;
        button.colors = colors;
        button.targetGraphic = image;
        button.onClick.AddListener(GoBack);
        BackButton = button;

        Text label = NewText("Label", backRect, "BACK", 30, FontStyle.Bold, Color.white, new Vector2(200f, 64f));
        label.rectTransform.anchoredPosition = Vector2.zero;
    }

    void Clear()
    {
        ClearDynamic();
        songTitle.text = "";
        flash.color = new Color(1f, 1f, 1f, 0f);
        launchTime = -1f;
    }

    /// <summary>Shows the three difficulty circles for <paramref name="level"/>.</summary>
    public void Open(GameFlow.Level level)
    {
        Clear();
        phase = Phase.Difficulty;
        chosenLevel = level;
        chosenColor = SongColor(level);
        songTitle.text = level.displayName;
        songTitle.color = chosenColor;
        keyIndex = Mathf.Clamp((int)ScoreKeeper.SelectedDifficulty, 0, 2);

        for (int i = 0; i < 3; i++)
        {
            var difficulty = (Difficulty)i;
            var home = new Vector2((i - 1) * 400f, 20f);
            Orb orb = NewOrb(DifficultyNames[i], home, DifficultyDiameter, DifficultyColors[i], i);
            orb.level = chosenLevel;
            orb.difficulty = difficulty;
            orb.scale = 1f;

            // Three pips: one filled square per step of difficulty.
            for (int p = 0; p < 3; p++)
            {
                Image pip = NewImage("Pip", orb.root, null,
                    p <= i ? DifficultyColors[i] : new Color(1f, 1f, 1f, 0.2f), new Vector2(14f, 14f));
                pip.rectTransform.anchoredPosition = new Vector2((p - 1) * 24f, -36f);
            }

            int best = ScoreKeeper.GetHighScore(chosenLevel, difficulty);
            if (best >= 0)
            {
                Text score = NewText("Best", orb.root, ScoreKeeper.FormatPoints(best), 26, FontStyle.Normal,
                    new Color(1f, 1f, 1f, 0.75f), new Vector2(260f, 36f));
                score.rectTransform.anchoredPosition = new Vector2(0f, -DifficultyDiameter / 2f - 34f);
            }
        }
    }

    Orb NewOrb(string title, Vector2 home, float diameter, Color color, int index)
    {
        var orb = new Orb { home = home, pos = home, radius = diameter / 2f, color = color, bobPhase = index * 1.7f, scale = 1f };
        orb.root = NewRect(title, stage);
        orb.root.anchoredPosition = home;

        orb.body = NewImage("Body", orb.root, CircleSprite(), new Color(color.r * 0.16f, color.g * 0.16f, color.b * 0.16f, 0.92f), Vector2.one * diameter);
        orb.ring = NewImage("Ring", orb.root, RingSprite(), color, Vector2.one * diameter);

        Text label = NewText("Name", orb.root, title, diameter > 220f ? 36 : 34, FontStyle.Bold, Color.white, new Vector2(diameter - 36f, 60f));
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = label.fontSize;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.rectTransform.anchoredPosition = Vector2.zero;
        if (diameter < 220f) label.rectTransform.anchoredPosition = new Vector2(0f, 12f);

        orbs.Add(orb);
        return orb;
    }

    static Color SongColor(GameFlow.Level level)
    {
        switch (level.id)
        {
            case "pink-panther": return new Color(1f, 0.45f, 0.78f);
            case "cheat-codes": return new Color(0.35f, 1f, 0.6f);
            case "flames": return new Color(1f, 0.5f, 0.15f);
            case "far-out": return new Color(0.68f, 0.52f, 1f);
            case "boogie-shoes": return new Color(1f, 0.86f, 0.28f);
            default: return new Color(0.3f, 0.9f, 1f);
        }
    }

    void ClearDynamic()
    {
        foreach (Orb o in orbs) if (o.root != null) Destroy(o.root.gameObject);
        foreach (Bullet b in bullets) if (b.rt != null) Destroy(b.rt.gameObject);
        foreach (Shard s in shards) if (s.rt != null) Destroy(s.rt.gameObject);
        foreach (GameObject g in extras) if (g != null) Destroy(g);
        orbs.Clear();
        bullets.Clear();
        shards.Clear();
        extras.Clear();
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float time = Time.unscaledTime;

        Vector2 aimPoint = ShipPos + Vector2.up * 400f;
        Orb hovered = phase == Phase.Launching ? null : HandleInput(out aimPoint);

        foreach (Orb orb in orbs)
        {
            if (orb.root == null) continue;
            orb.pos = orb.home + new Vector2(Mathf.Sin(time * 0.7f + orb.bobPhase) * 8f, Mathf.Sin(time * 1.1f + orb.bobPhase) * 14f);
            orb.root.anchoredPosition = orb.pos;

            orb.hover = Mathf.MoveTowards(orb.hover, orb == hovered ? 1f : 0f, dt * 6f);
            orb.scale = Mathf.MoveTowards(orb.scale, orb.targetScale, dt * 4f);
            float pop = orb.scale >= 1f ? 1f : EaseOutBack(Mathf.Clamp01(orb.scale));
            orb.root.localScale = Vector3.one * (pop * (1f + 0.07f * orb.hover));

            orb.ring.color = Color.Lerp(orb.color * 0.85f, Color.white, orb.hover * 0.4f);
        }

        UpdateShip(dt, aimPoint);
        UpdateBullets(dt);
        UpdateShards(time);
    }

    /// <summary>Reads the mouse and keyboard; returns the orb being aimed at.</summary>
    Orb HandleInput(out Vector2 aimPoint)
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        aimPoint = ShipPos + Vector2.up * 400f;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) GoBack();

        var selectable = orbs.FindAll(o => o.selectable && o.targetScale > 0f);
        if (selectable.Count == 0) return null;

        if (keyboard != null)
        {
            int step = (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame ? -1 : 0)
                     + (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ? 1 : 0);
            if (step != 0)
            {
                useMouse = false;
                keyIndex = (keyIndex + step + selectable.Count) % selectable.Count;
            }
        }

        Vector2 pointer = Vector2.zero;
        bool overBack = false;
        if (mouse != null)
        {
            Vector2 screen = mouse.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, screen, null, out pointer);
            overBack = RectTransformUtility.RectangleContainsScreenPoint(backRect, screen, null);
            if (mouse.delta.ReadValue().sqrMagnitude > 0.25f) useMouse = true;
        }

        Orb hovered = null;
        if (useMouse && mouse != null)
        {
            aimPoint = pointer;
            foreach (Orb o in selectable)
                if (Vector2.Distance(pointer, o.pos) <= o.radius) { hovered = o; break; }
        }
        else
        {
            hovered = selectable[Mathf.Clamp(keyIndex, 0, selectable.Count - 1)];
            aimPoint = hovered.pos;
        }
        if (hovered != null) aimPoint = hovered.pos;

        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame && !overBack;
        bool key = keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame);
        if (click) useMouse = true;
        if ((click || key) && Time.unscaledTime >= nextFire && bullets.Count < MaxBullets)
            Fire(hovered, hovered != null ? hovered.pos : (click ? pointer : aimPoint));

        return hovered;
    }

    void Fire(Orb target, Vector2 at)
    {
        nextFire = Time.unscaledTime + FireCooldown;
        recoil = 1f;

        Vector2 from = ShipPos + Vector2.up * 44f;
        Vector2 dir = (at - from);
        float distance = dir.magnitude;
        if (distance < 1f) return;

        Image image = NewImage("Bullet", stage, null, Color.white, new Vector2(10f, 38f));
        image.rectTransform.anchoredPosition = from;
        image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        bullets.Add(new Bullet
        {
            rt = image.rectTransform, from = from, to = at, target = target,
            duration = Mathf.Max(0.1f, distance / BulletSpeed)
        });
    }

    void UpdateShip(float dt, Vector2 aimPoint)
    {
        Vector2 dir = aimPoint - ShipPos;
        float angle = Mathf.Clamp(Mathf.DeltaAngle(0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f), -75f, 75f);
        shipAngle = Mathf.LerpAngle(shipAngle, angle, 1f - Mathf.Exp(-18f * dt));
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 8f);
        ship.localRotation = Quaternion.Euler(0f, 0f, shipAngle);
        ship.anchoredPosition = ShipPos - dir.normalized * (recoil * 8f);
        ship.localScale = Vector3.one * (1f + 0.12f * recoil);
    }

    void UpdateBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = bullets[i];
            b.t += dt / b.duration;
            if (b.target != null && b.target.root != null) b.to = b.target.pos; // follow the floating orb
            b.rt.anchoredPosition = Vector2.Lerp(b.from, b.to, Mathf.Clamp01(b.t));
            if (b.t < 1f) continue;

            Orb target = b.target;
            Destroy(b.rt.gameObject);
            bullets.RemoveAt(i);
            if (target != null && target.selectable && phase != Phase.Launching) OnOrbShot(target);
        }
    }

    // ------------------------------------------------------------------ picking

    void OnOrbShot(Orb orb)
    {
        orb.selectable = false;
        if (phase == Phase.Difficulty) StartCoroutine(Launch(orb));
    }

    IEnumerator Launch(Orb picked)
    {
        phase = Phase.Launching;
        picked.root.gameObject.SetActive(false);
        foreach (Orb o in orbs) { o.selectable = false; if (o != picked) o.targetScale = 0f; }
        songTitle.text = "";
        SpawnShards(picked, 64, true);
        launchTime = Time.unscaledTime;

        // Squares burst out, then fly together to the centre, then a flash and the level starts.
        yield return new WaitForSecondsRealtime(LaunchSeconds);
        onStart?.Invoke(picked.level, picked.difficulty);
    }

    const float BurstSeconds = 0.4f;
    const float GatherSeconds = 0.6f;
    const float LaunchSeconds = BurstSeconds + GatherSeconds + 0.15f;

    void SpawnShards(Orb orb, int count, bool gather)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            Vector2 start = orb.pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(0f, orb.radius * 0.8f);
            Vector2 burst = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(gather ? 140f : 100f, gather ? 380f : 220f);
            float size = UnityEngine.Random.Range(10f, 26f);
            Color color = Color.Lerp(orb.color, Color.white, UnityEngine.Random.Range(0f, 0.6f));
            Image image = NewImage("Shard", stage, null, color, new Vector2(size, size));
            image.rectTransform.anchoredPosition = start;
            flash.transform.SetAsLastSibling();
            shards.Add(new Shard
            {
                rt = image.rectTransform, image = image, start = start, burst = burst, size = size,
                spin = UnityEngine.Random.Range(-540f, 540f), delay = gather ? UnityEngine.Random.Range(0f, 0.08f) : 0f
            });
            if (!gather) shards[shards.Count - 1].delay = -1f; // -1 = burst and fade, no gathering
        }
        shardStart = Time.unscaledTime;
    }

    float shardStart;

    void UpdateShards(float time)
    {
        float age = time - shardStart;
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            Shard s = shards[i];
            if (s.rt == null) { shards.RemoveAt(i); continue; }

            if (s.delay < 0f) // song pick: burst out and fade
            {
                float u = Mathf.Clamp01(age / 0.4f);
                s.rt.anchoredPosition = Vector2.Lerp(s.start, s.burst, 1f - (1f - u) * (1f - u));
                s.rt.localRotation = Quaternion.Euler(0f, 0f, s.spin * age);
                s.image.color = new Color(s.image.color.r, s.image.color.g, s.image.color.b, 1f - u);
                if (u >= 1f) { Destroy(s.rt.gameObject); shards.RemoveAt(i); }
                continue;
            }

            float t = age - s.delay;
            Vector2 pos;
            float scale = 1f;
            if (t < BurstSeconds)
            {
                float u = Mathf.Clamp01(t / BurstSeconds);
                pos = Vector2.Lerp(s.start, s.burst, 1f - (1f - u) * (1f - u));
            }
            else
            {
                float u = Mathf.Clamp01((t - BurstSeconds) / GatherSeconds);
                float e = u * u * (3f - 2f * u);
                pos = Vector2.Lerp(s.burst, Vector2.zero, e);
                scale = Mathf.Lerp(1f, 1.6f, e);
            }
            s.rt.anchoredPosition = pos;
            s.rt.localScale = Vector3.one * scale;
            s.rt.localRotation = Quaternion.Euler(0f, 0f, s.spin * t);
        }

        if (launchTime >= 0f)
        {
            float t = time - launchTime - BurstSeconds - GatherSeconds;
            float a = Mathf.Clamp01(t / 0.15f);
            flash.color = new Color(1f, 1f, 1f, a);
            flash.transform.SetAsLastSibling();
        }
    }

    void GoBack()
    {
        if (phase == Phase.Launching) return;
        onBack?.Invoke();
    }

    // ------------------------------------------------------------------ drawing helpers

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = size;
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text NewText(string name, Transform parent, string text, int size, FontStyle style, Color color, Vector2 box)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(parent, false);
        label.rectTransform.sizeDelta = box;
        label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    static Sprite CircleSprite() => circleSprite != null ? circleSprite : circleSprite =
        MakeSprite(128, (x, y) => Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 64f));

    static Sprite RingSprite() => ringSprite != null ? ringSprite : ringSprite =
        MakeSprite(128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Min(Mathf.Clamp01((1f - r) * 64f), Mathf.Clamp01((r - 0.93f) * 64f));
        });

    /// <summary>A small arrow-head ship pointing up, with a notch in its tail.</summary>
    static Sprite ShipSprite() => shipSprite != null ? shipSprite : shipSprite =
        MakeSprite(128, (x, y) =>
        {
            float halfWidth = 0.85f * (1f - y) / 2f;
            float edge = Mathf.Clamp01((halfWidth - Mathf.Abs(x)) * 40f);
            float notch = y < -0.35f ? Mathf.Clamp01((Mathf.Abs(x) - (-0.35f - y) * 0.45f) * 40f) : 1f;
            return Mathf.Min(edge, notch) * Mathf.Clamp01((y + 1f) * 40f);
        });

    static Sprite MakeSprite(int size, Func<float, float, float> alpha)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float x = (px + 0.5f) / size * 2f - 1f;
                float y = (py + 0.5f) / size * 2f - 1f;
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
