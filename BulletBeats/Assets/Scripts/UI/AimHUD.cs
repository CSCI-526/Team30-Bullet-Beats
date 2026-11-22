using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crosshair that stays exactly on the mouse. Timing is shown by the ring on each chart target, not here.
/// After each shot at an asteroid it shows PERFECT / GOOD / MISS just above it: small, widely spaced
/// letters in gold / cyan / coral. A shot at empty space shows nothing. The label is sleek and minimal
/// rather than pixel (gameplay effects are smooth; the menus stay pixel): it pops in with a soft glow
/// and fades.
/// Also draws lock-on brackets around
/// the rock the next shot goes to when aim assist has locked one (<see cref="PlayerShooter.LockedTarget"/>).
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerShooter))]
public class AimHUD : MonoBehaviour
{
    public Color crosshairColor = Color.white;
    public Color lockColor = Color.white;
    [Tooltip("Gap between the centre and the ticks, in pixels at 1080p.")]
    public float tickGap = 12f;
    [Tooltip("Seconds the PERFECT / GOOD / MISS label stays up.")]
    public float judgmentShowTime = 0.45f;
    public bool hideSystemCursor = true;

    static readonly Vector2[] TickDirections = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
    static readonly Vector2[] CornerDirections = { new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 1f) };
    const float TickLength = 12f;
    const float TickWidth = 3f;
    const float BracketLength = 16f;
    const float BracketPadding = 8f;
    const float MinBracketDistance = 24f;
    const float LockInSeconds = 0.1f;
    const float LockInScale = 1.6f;

    PlayerShooter shooter;
    GameObject canvasObject;
    Canvas canvas;
    RectTransform crosshair;
    RectTransform lockMarker;
    readonly RectTransform[] ticks = new RectTransform[4];
    readonly RectTransform[] corners = new RectTransform[4];
    ChartTarget markedTarget;
    float lockInTimer;
    RectTransform judgment;
    TextMeshProUGUI judgmentLabel;
    Color judgmentColor;
    float judgmentTimer;

    // Hit feedback colours: gold Perfect, cyan Good, muted coral Miss.
    static readonly Color PerfectColor = new Color(1f, 0.835f, 0.302f);
    static readonly Color GoodColor = new Color(0.043f, 0.898f, 1f);
    static readonly Color MissColor = new Color(1f, 0.47f, 0.43f);
    /// <summary>Font size at 1080p. Small on purpose: about the height of a rock's bracket arm.</summary>
    const float JudgmentSize = 16f;
    /// <summary>Extra space between letters (in hundredths of an em); the wide spacing is the look.</summary>
    const float JudgmentSpacing = 24f;
    const float PopSeconds = 0.08f;

    [Tooltip("Draw the white lock-on brackets here. Off: the timing brackets on the asteroid show the lock instead.")]
    public bool showLockBrackets = false;

    void Awake()
    {
        shooter = GetComponent<PlayerShooter>();
        BuildCanvas();
    }

    void OnEnable()
    {
        shooter.ShotFired += ShowJudgment;
        canvasObject.SetActive(true);
        if (hideSystemCursor) Cursor.visible = false;
    }

    void OnDisable()
    {
        shooter.ShotFired -= ShowJudgment;
        if (canvasObject != null) canvasObject.SetActive(false);
        Cursor.visible = true;
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void LateUpdate()
    {
        crosshair.position = shooter.AimScreenPosition;

        for (int i = 0; i < ticks.Length; i++)
            ticks[i].anchoredPosition = TickDirections[i] * (tickGap + TickLength * 0.5f);

        if (judgmentTimer > 0f)
        {
            judgmentTimer -= Time.deltaTime;
            float age = judgmentShowTime - judgmentTimer;
            float alpha = Mathf.Clamp01(judgmentTimer / (judgmentShowTime * 0.5f));

            // Pop in slightly larger, then settle. Where it sits is unchanged from before.
            float pop = Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(age / PopSeconds));
            judgment.localScale = new Vector3(pop, pop, 1f);

            Color c = judgmentColor;
            c.a = alpha;
            judgmentLabel.color = c;
        }
        else if (judgment.gameObject.activeSelf)
        {
            judgment.gameObject.SetActive(false);
        }

        UpdateLockMarker();
    }

    void UpdateLockMarker()
    {
        if (!showLockBrackets)
        {
            lockMarker.gameObject.SetActive(false);
            markedTarget = null;
            return;
        }
        ChartTarget target = shooter.LockedTarget;
        Vector2 center = default;
        float radius = 0f;
        bool show = target != null && target.TryGetScreenCircle(shooter.aimCamera, out center, out radius);
        lockMarker.gameObject.SetActive(show);
        if (!show)
        {
            markedTarget = null;
            return;
        }

        if (target != markedTarget)
        {
            markedTarget = target;
            lockInTimer = LockInSeconds;
        }

        // A new lock closes in from wider, so it reads as a lock instead of a marker sliding over.
        // Unscaled so it stays snappy in the tutorial's slow motion.
        lockInTimer = Mathf.Max(0f, lockInTimer - Time.unscaledDeltaTime);
        float scale = Mathf.Lerp(1f, LockInScale, lockInTimer / LockInSeconds);
        float distance = (Mathf.Max(radius / canvas.scaleFactor, MinBracketDistance) + BracketPadding) * scale;

        lockMarker.position = center;
        for (int i = 0; i < corners.Length; i++)
            corners[i].anchoredPosition = CornerDirections[i] * distance;
    }

    void ShowJudgment(BeatJudgment result)
    {
        // A MISS at empty space says nothing about timing, so it isn't shown.
        if (shooter.LastShotTarget == null) return;

        judgmentLabel.text = result.ToString().ToUpperInvariant();
        judgmentColor = result switch
        {
            BeatJudgment.Perfect => PerfectColor,
            BeatJudgment.Good => GoodColor,
            _ => MissColor
        };
        judgmentLabel.color = judgmentColor;
        SetGlow(judgmentLabel, judgmentColor);
        judgmentTimer = judgmentShowTime;
        judgment.gameObject.SetActive(true);

        // Next to the asteroid, just above it (same placement as before; only the look changed).
        float scale = canvas.scaleFactor;
        if (shooter.LastShotTarget.TryGetScreenCircle(shooter.aimCamera, out Vector2 center, out float radius))
            judgment.position = center + Vector2.up * (radius + 34f * scale);
        else
            judgment.position = shooter.AimScreenPosition + Vector2.down * (tickGap + TickLength + 30f) * scale;
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("AimHUD", typeof(Canvas), typeof(CanvasScaler));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        lockMarker = AddGroup("LockMarker", canvasObject.transform);
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = AddGroup("Corner", lockMarker);
            Vector2 inward = -CornerDirections[i] * BracketLength * 0.5f;
            AddImage("Arm", new Vector2(BracketLength, TickWidth), corners[i], lockColor).anchoredPosition = new Vector2(inward.x, 0f);
            AddImage("Arm", new Vector2(TickWidth, BracketLength), corners[i], lockColor).anchoredPosition = new Vector2(0f, inward.y);
        }
        lockMarker.gameObject.SetActive(false);

        crosshair = AddGroup("Crosshair", canvasObject.transform);

        AddImage("Dot", new Vector2(4f, 4f), crosshair, crosshairColor);
        for (int i = 0; i < ticks.Length; i++)
        {
            bool vertical = TickDirections[i].x == 0f;
            ticks[i] = AddImage("Tick", vertical ? new Vector2(TickWidth, TickLength) : new Vector2(TickLength, TickWidth), crosshair, crosshairColor);
        }

        judgment = AddGroup("Judgment", canvasObject.transform);
        judgmentLabel = AddText("Result", judgment, UITheme.HitFont, JudgmentSize, 0f);
        judgmentLabel.characterSpacing = JudgmentSpacing;
        judgmentLabel.fontStyle = FontStyles.Bold; // a touch heavier so the thin letters stay readable
        judgment.gameObject.SetActive(false);
    }

    static TextMeshProUGUI AddText(string name, Transform parent, TMP_FontAsset font, float size, float y)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(400f, size * 1.4f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    /// <summary>Soft glow in the label's own colour (a blurred copy drawn behind it), not a hard outline.</summary>
    static void SetGlow(TextMeshProUGUI text, Color color)
    {
        Material material = text.fontMaterial; // per-label copy, so other text keeps its look
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(color.r, color.g, color.b, 0.8f));
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.4f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
    }

    static RectTransform AddGroup(string name, Transform parent)
    {
        var group = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        group.SetParent(parent, false);
        group.sizeDelta = Vector2.zero;
        return group;
    }

    static RectTransform AddImage(string name, Vector2 size, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = size;
        image.color = color;
        image.raycastTarget = false;
        return image.rectTransform;
    }
}
