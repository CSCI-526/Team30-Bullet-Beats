using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Score card in the top-left corner during play: SCORE and the points on the left, the multiplier
/// (x1 to x4) and five pips toward the next step on the right. Reads <see cref="ScoreKeeper"/> each
/// frame, so it changes no scoring. <see cref="HealthHUD"/> adds it and puts the hearts under it.
/// </summary>
public class ScoreHUD : MonoBehaviour
{
    public const float Width = 480f;
    public const float Height = 110f;
    /// <summary>Whole card is drawn at this scale; layout numbers below are at full size.</summary>
    public const float Scale = 0.78f;
    /// <summary>On-screen height, for placing the hearts under the card.</summary>
    public const float ShownHeight = Height * Scale;
    const float Margin = 32f;
    const int PipCount = 5;
    const float PipSize = 16f;
    const float PipGap = 6f;
    const float DividerX = 300f;
    [Tooltip("Seconds the shown score takes to catch up with the real one.")]
    public float countUpTime = 0.25f;
    [Tooltip("Seconds the multiplier pops when it goes up.")]
    public float popTime = 0.2f;

    static readonly Color PipEmpty = new Color(0.04f, 0.35f, 0.42f, 0.9f);
    static Sprite frame;

    ScoreKeeper keeper;
    GameFlow flow;
    GameObject canvasObject;
    RectTransform card;
    TextMeshProUGUI scoreText, multText;
    readonly Image[] pips = new Image[PipCount];
    float shownScore;
    int lastScore = -1, lastMult = -1, lastFilled = -1;
    float popTimer;

    void Awake() => Build();

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void OnDisable()
    {
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (keeper == null)
        {
            keeper = FindFirstObjectByType<ScoreKeeper>();
            if (keeper != null) flow = keeper.GetComponent<GameFlow>();
        }
        bool show = keeper != null && (flow == null || flow.State == GameState.Playing);
        if (canvasObject.activeSelf != show) canvasObject.SetActive(show);
        if (!show) return;

        // Score rolls up quickly instead of jumping.
        int score = keeper.Score;
        if (score < shownScore) shownScore = score; // new run
        float step = Mathf.Max(1f, Mathf.Abs(score - shownScore)) * Time.unscaledDeltaTime / Mathf.Max(0.01f, countUpTime);
        shownScore = Mathf.MoveTowards(shownScore, score, Mathf.Max(step, 400f * Time.unscaledDeltaTime));
        int shown = Mathf.RoundToInt(shownScore);
        if (shown != lastScore)
        {
            lastScore = shown;
            scoreText.text = Mathf.Min(shown, 999999).ToString("D6");
        }

        int mult = keeper.Multiplier;
        if (mult != lastMult)
        {
            if (lastMult > 0 && mult > lastMult) popTimer = popTime;
            lastMult = mult;
            multText.text = "x" + mult;
        }
        if (popTimer > 0f)
        {
            popTimer -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(popTimer / popTime);
            multText.rectTransform.localScale = Vector3.one * (1f + 0.3f * t); // relative to the card
        }
        else multText.rectTransform.localScale = Vector3.one;

        // Pips: progress toward the next multiplier; all lit (yellow) once it is maxed.
        bool maxed = mult >= keeper.maxMultiplier;
        int filled = maxed ? PipCount
            : (keeper.Streak % keeper.streakPerStep) * PipCount / keeper.streakPerStep;
        int key = maxed ? 100 : filled;
        if (key != lastFilled)
        {
            lastFilled = key;
            for (int i = 0; i < PipCount; i++)
                pips[i].color = i < filled ? (maxed ? UITheme.AccentYellow : UITheme.ElectricCyan) : PipEmpty;
        }
    }

    void Build()
    {
        canvasObject = new GameObject("ScoreHUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var image = new GameObject("Card", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        card = image.rectTransform;
        card.SetParent(canvasObject.transform, false);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(Margin, -Margin);
        card.sizeDelta = new Vector2(Width, Height);
        card.localScale = Vector3.one * Scale;
        image.sprite = FrameSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 0.25f; // one art pixel = 4 canvas units
        image.raycastTarget = false;

        Place(UITheme.Text(card, "SCORE", 26, UITheme.ElectricCyan, true, TextAlignmentOptions.TopLeft),
            new Vector2(28f, -14f), new Vector2(260f, 30f));
        scoreText = UITheme.Text(card, "000000", 54, Color.white, true, TextAlignmentOptions.TopLeft);
        Place(scoreText, new Vector2(26f, -40f), new Vector2(270f, 60f));

        var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        divider.color = new Color(UITheme.ElectricCyan.r, UITheme.ElectricCyan.g, UITheme.ElectricCyan.b, 0.75f);
        divider.raycastTarget = false;
        RectTransform d = divider.rectTransform;
        d.SetParent(card, false);
        d.anchorMin = d.anchorMax = d.pivot = new Vector2(0f, 1f);
        d.anchoredPosition = new Vector2(DividerX, -18f);
        d.sizeDelta = new Vector2(4f, Height - 36f);

        float right = (DividerX + Width) * 0.5f;
        multText = UITheme.Text(card, "x1", 46, UITheme.AccentYellow, false, TextAlignmentOptions.Center);
        multText.font = UITheme.DisplayFont; // lower-case x, so not the upper-case display style
        Place(multText, new Vector2(right - 80f, -10f), new Vector2(160f, 56f));

        float pipsWidth = PipCount * PipSize + (PipCount - 1) * PipGap;
        for (int i = 0; i < PipCount; i++)
        {
            var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            pip.raycastTarget = false;
            pip.color = PipEmpty;
            RectTransform r = pip.rectTransform;
            r.SetParent(card, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(PipSize, PipSize);
            r.anchoredPosition = new Vector2(right - pipsWidth * 0.5f + i * (PipSize + PipGap), -74f);
            pips[i] = pip;
        }
        canvasObject.SetActive(false);
    }

    static void Place(TextMeshProUGUI label, Vector2 pos, Vector2 size)
    {
        RectTransform r = label.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    /// <summary>12x12 pixel frame: dark translucent fill, cyan border with stepped corners. Used Sliced.</summary>
    static Sprite FrameSprite()
    {
        if (frame != null) return frame;
        const int n = 12;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Color32 edge = UITheme.ElectricCyan;
        var fill = new Color32(8, 14, 30, 200);
        var clear = new Color32(0, 0, 0, 0);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int ex = Mathf.Min(x, n - 1 - x), ey = Mathf.Min(y, n - 1 - y);
            Color32 c;
            if (ex + ey < 2) c = clear;               // cut corner
            else if (ex + ey == 2 || ex == 0 || ey == 0) c = edge; // border incl. the step
            else c = fill;
            px[y * n + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        frame = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        return frame;
    }
}
