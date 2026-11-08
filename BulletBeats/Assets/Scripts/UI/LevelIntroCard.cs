using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Card shown before each level for <see cref="showSeconds"/>: the level number, the song, the difficulty
/// as bars, and the goal in one line ("Survive the next 90 seconds", or "Survive the song" with sound on).
/// Then it counts 3, 2, 1 on the session's count-in beats and disappears when the song starts.
/// <see cref="GameFlow"/> waits for <see cref="Show"/> before it starts the count-in.
/// Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class LevelIntroCard : MonoBehaviour
{
    [Tooltip("Seconds the card stays up before the count-in starts.")]
    public float showSeconds = 2f;
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color panelColor = new Color(0.02f, 0.01f, 0.06f, 0.88f);

    const float GiveUpSeconds = 8f;

    GameFlow flow;
    RhythmSession session;
    Font font;
    GameObject canvasObject, card;
    Text levelLabel, songLabel, difficultyLabel, goalLabel, countLabel;
    readonly Image[] bars = new Image[3];
    float shownAt;

    /// <summary>True while the card or its countdown is on screen.</summary>
    public bool IsShowing => canvasObject != null && canvasObject.activeSelf;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        session = FindFirstObjectByType<RhythmSession>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();
    }

    void OnEnable()
    {
        if (session != null) session.CountInBeat += OnCountInBeat;
    }

    void OnDisable()
    {
        if (session != null) session.CountInBeat -= OnCountInBeat;
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    /// <summary>Shows the card for <paramref name="level"/>. Finishes when the count-in should start.</summary>
    public IEnumerator Show(GameFlow.Level level)
    {
        Fill(level);
        canvasObject.SetActive(true);
        card.SetActive(true);
        countLabel.gameObject.SetActive(false);
        shownAt = Time.time;
        // Scaled time, so the card waits too if the game is frozen.
        for (float t = 0f; t < showSeconds; t += Time.deltaTime) yield return null;
    }

    void Update()
    {
        if (!IsShowing) return;
        BeatClock clock = BeatClock.Instance;
        bool songStarted = clock != null && clock.IsMusicPlaying;
        // Gone once the song plays, or if the level ended or never started a count-in.
        if (songStarted || flow.State != GameState.Playing || Time.time - shownAt > showSeconds + GiveUpSeconds)
            canvasObject.SetActive(false);
    }

    void OnCountInBeat(int beatsLeft)
    {
        if (!IsShowing) return;
        card.SetActive(false);
        countLabel.gameObject.SetActive(true);
        countLabel.text = beatsLeft.ToString();
    }

    void Fill(GameFlow.Level level)
    {
        int index = level != null ? System.Array.IndexOf(flow.levels, level) : -1;
        levelLabel.text = index >= 0 ? $"LEVEL {index + 1}" : "YOUR SONG";
        songLabel.text = level != null ? level.displayName : "";

        Difficulty difficulty = ScoreKeeper.SelectedDifficulty;
        difficultyLabel.text = ScoreKeeper.DifficultyName(difficulty);
        for (int i = 0; i < bars.Length; i++)
            bars[i].color = i <= (int)difficulty ? accentColor : new Color(1f, 1f, 1f, 0.2f);

        goalLabel.text = Goal(level);
    }

    static string Goal(GameFlow.Level level)
    {
        if (!CornerMute.Muted) return "Survive the song";
        if (level != null && level.music != null) return $"Survive the next {Mathf.CeilToInt(level.music.length)} seconds";
        return "Survive till the end";
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("LevelIntroCard", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var panel = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline)).GetComponent<Image>();
        panel.rectTransform.SetParent(canvasObject.transform, false);
        panel.rectTransform.sizeDelta = new Vector2(760f, 380f);
        panel.color = panelColor;
        panel.raycastTarget = false;
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);
        card = panel.gameObject;

        levelLabel = AddText(card.transform, 64, FontStyle.Bold, accentColor, 120f);
        songLabel = AddText(card.transform, 34, FontStyle.Normal, Color.white, 50f);
        for (int i = 0; i < bars.Length; i++)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bar.rectTransform.SetParent(card.transform, false);
            bar.rectTransform.sizeDelta = new Vector2(44f, 14f);
            bar.rectTransform.anchoredPosition = new Vector2(-160f + i * 54f, -12f);
            bar.raycastTarget = false;
            bars[i] = bar;
        }
        difficultyLabel = AddText(card.transform, 26, FontStyle.Bold, Color.white, -12f);
        difficultyLabel.alignment = TextAnchor.MiddleLeft;
        difficultyLabel.rectTransform.sizeDelta = new Vector2(240f, 40f);
        difficultyLabel.rectTransform.anchoredPosition = new Vector2(110f, -12f);
        goalLabel = AddText(card.transform, 38, FontStyle.Bold, Color.white, -100f);

        countLabel = AddText(canvasObject.transform, 160, FontStyle.Bold, Color.white, 0f);
        countLabel.rectTransform.sizeDelta = new Vector2(400f, 240f);
        canvasObject.SetActive(false);
    }

    Text AddText(Transform parent, int size, FontStyle style, Color color, float y)
    {
        var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(720f, size * 1.4f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }
}
