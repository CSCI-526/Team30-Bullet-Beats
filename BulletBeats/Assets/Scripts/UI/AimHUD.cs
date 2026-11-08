using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crosshair that stays exactly on the mouse. Timing is shown by the ring on each chart target, not here.
/// After each shot at an asteroid it shows PERFECT / GOOD / MISS next to it, with a small line under it:
/// on the beat, slightly early/late, too early/late. A shot at empty space shows nothing.
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
    Text judgmentLabel, judgmentDetail;
    Color judgmentColor;
    float judgmentTimer;

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
            Color c = judgmentColor;
            c.a = Mathf.Clamp01(judgmentTimer / (judgmentShowTime * 0.5f));
            judgmentLabel.color = c;
            judgmentDetail.color = new Color(1f, 1f, 1f, c.a * 0.85f);
        }

        UpdateLockMarker();
    }

    void UpdateLockMarker()
    {
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
        judgmentDetail.text = Detail(result, shooter.LastShotOffset);
        judgmentColor = shooter.ColorFor(result);
        judgmentLabel.color = judgmentColor;
        judgmentTimer = judgmentShowTime;

        // Next to the asteroid, just above it.
        float scale = canvas.scaleFactor;
        if (shooter.LastShotTarget.TryGetScreenCircle(shooter.aimCamera, out Vector2 center, out float radius))
            judgment.position = center + Vector2.up * (radius + 34f * scale);
        else
            judgment.position = shooter.AimScreenPosition + Vector2.down * (tickGap + TickLength + 30f) * scale;
    }

    static string Detail(BeatJudgment result, float offset)
    {
        if (result == BeatJudgment.Perfect) return "on the beat";
        bool early = offset < 0f;
        if (result == BeatJudgment.Good) return early ? "slightly early" : "slightly late";
        return early ? "too early" : "too late";
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
        judgmentLabel = AddText("Result", judgment, 28, FontStyle.Bold, 0f);
        judgmentDetail = AddText("Detail", judgment, 18, FontStyle.Normal, -24f);
    }

    static Text AddText(string name, Transform parent, int size, FontStyle style, float y)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(300f, size * 1.4f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.text = "";
        return text;
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
