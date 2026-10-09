using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crosshair that follows the mouse and doubles as a metronome: its four ticks close in towards
/// the centre over each beat and spring back out on the beat, so click when they close.
/// Shows PERFECT / GOOD / MISS under the crosshair after each shot.
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(PlayerShooter))]
public class AimHUD : MonoBehaviour
{
    public Color crosshairColor = Color.white;
    [Tooltip("Gap between the centre and the ticks right before a beat, in pixels at 1080p.")]
    public float tickGap = 12f;
    [Tooltip("Extra gap right after a beat, in pixels at 1080p.")]
    public float beatSpread = 26f;
    [Tooltip("Seconds the PERFECT / GOOD / MISS label stays up.")]
    public float judgmentShowTime = 0.45f;
    public bool hideSystemCursor = true;

    static readonly Vector2[] TickDirections = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
    const float TickLength = 12f;
    const float TickWidth = 3f;

    PlayerShooter shooter;
    GameObject canvasObject;
    RectTransform crosshair;
    readonly RectTransform[] ticks = new RectTransform[4];
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

        float gap = tickGap;
        if (BeatClock.Instance != null)
        {
            float beat = BeatClock.Instance.BeatPosition;
            gap += beatSpread * (1f - (beat - Mathf.Floor(beat)));
        }
        for (int i = 0; i < ticks.Length; i++)
            ticks[i].anchoredPosition = TickDirections[i] * (gap + TickLength * 0.5f);

        if (judgmentTimer > 0f)
        {
            judgmentTimer -= Time.deltaTime;
            Color c = judgmentColor;
            c.a = Mathf.Clamp01(judgmentTimer / (judgmentShowTime * 0.5f));
            judgmentLabel.color = c;
        }
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
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        crosshair = new GameObject("Crosshair", typeof(RectTransform)).GetComponent<RectTransform>();
        crosshair.SetParent(canvasObject.transform, false);
        crosshair.sizeDelta = Vector2.zero;

        AddImage("Dot", new Vector2(4f, 4f));
        for (int i = 0; i < ticks.Length; i++)
        {
            bool vertical = TickDirections[i].x == 0f;
            ticks[i] = AddImage("Tick", vertical ? new Vector2(TickWidth, TickLength) : new Vector2(TickLength, TickWidth));
        }

        judgmentLabel = new GameObject("Judgment", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<Text>();
        var labelRect = judgmentLabel.rectTransform;
        labelRect.SetParent(crosshair, false);
        labelRect.sizeDelta = new Vector2(300f, 40f);
        labelRect.anchoredPosition = new Vector2(0f, -(tickGap + beatSpread + TickLength + 24f));
        judgmentLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        judgmentLabel.fontSize = 28;
        judgmentLabel.fontStyle = FontStyle.Bold;
        judgmentLabel.alignment = TextAnchor.MiddleCenter;
        judgmentLabel.raycastTarget = false;
        judgmentLabel.text = "";
    }

    RectTransform AddImage(string name, Vector2 size)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(crosshair, false);
        image.rectTransform.sizeDelta = size;
        image.color = crosshairColor;
        image.raycastTarget = false;
        return image.rectTransform;
    }
}
