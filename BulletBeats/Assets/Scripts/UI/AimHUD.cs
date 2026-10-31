using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crosshair that stays exactly on the mouse. Timing is shown by the ring on each chart target, not here.
/// Shows PERFECT / GOOD / MISS under the crosshair after each shot, and lock-on brackets around
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
    Text judgmentLabel;
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

    void ShowJudgment(BeatJudgment judgment)
    {
        judgmentLabel.text = judgment.ToString().ToUpperInvariant();
        judgmentColor = shooter.ColorFor(judgment);
        judgmentLabel.color = judgmentColor;
        judgmentTimer = judgmentShowTime;
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

        judgmentLabel = new GameObject("Judgment", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        var labelRect = judgmentLabel.rectTransform;
        labelRect.SetParent(crosshair, false);
        labelRect.sizeDelta = new Vector2(300f, 40f);
        labelRect.anchoredPosition = new Vector2(0f, -(tickGap + TickLength + 24f));
        judgmentLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        judgmentLabel.fontSize = 28;
        judgmentLabel.fontStyle = FontStyle.Bold;
        judgmentLabel.alignment = TextAnchor.MiddleCenter;
        judgmentLabel.raycastTarget = false;
        judgmentLabel.text = "";
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
