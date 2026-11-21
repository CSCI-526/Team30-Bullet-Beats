using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End-of-level card over the blurred game. Shows only the results the game already tracks.
/// <para><b>LEVEL COMPLETED:</b> stars, song · difficulty, score, high score, Perfect %, best streak;
/// NEXT LEVEL (the next song, same difficulty) when there is one, then RETRY and SONG SELECT.</para>
/// <para><b>GAME OVER:</b> SHIP DESTROYED, the same numbers without stars; RETRY and SONG SELECT.</para>
/// </summary>
public class ResultCard : CardScreen
{
    GameFlow flow;
    TextMeshProUGUI heading, subline, score, highScore, perfect, streak, newBest;
    readonly Image[] stars = new Image[3];
    RectTransform starRow;
    PixelButton next, retry, songSelect;
    Selectable first;

    protected override int SortingOrder => 230;
    protected override Selectable FirstSelected => first;

    protected override void Awake()
    {
        flow = GetComponent<GameFlow>();
        base.Awake();
    }

    protected override void Build()
    {
        RectTransform card = PixelPanel.Create(Content, new Vector2(1060f, 560f), new Vector2(0f, 150f), "ResultCard");

        Image bars = PixelArt.IconImage(card, PixelArt.Icon.Equalizer, 90f, UITheme.ElectricCyan);
        bars.rectTransform.anchoredPosition = new Vector2(0f, 300f);

        heading = Label(card, 82f, UITheme.White, 200f, 980f);

        starRow = new GameObject("Stars", typeof(RectTransform)).GetComponent<RectTransform>();
        starRow.SetParent(card, false);
        starRow.anchoredPosition = new Vector2(0f, 95f);
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = PixelArt.IconImage(starRow, PixelArt.Icon.Star, 96f, UITheme.AccentYellow);
            stars[i].rectTransform.anchoredPosition = new Vector2((i - 1) * 120f, 0f);
        }

        subline = Label(card, 34f, UITheme.White, 10f, 980f);

        // Score in its own framed strip.
        RectTransform strip = PixelPanel.Create(card, new Vector2(560f, 96f), new Vector2(0f, -80f), "ScoreStrip");
        TextMeshProUGUI scoreTitle = UITheme.Text(strip, "SCORE", 30f, UITheme.White, align: TextAlignmentOptions.Right);
        scoreTitle.rectTransform.sizeDelta = new Vector2(180f, 60f);
        scoreTitle.rectTransform.anchoredPosition = new Vector2(-120f, 0f);
        score = UITheme.Text(strip, "", 52f, UITheme.White, align: TextAlignmentOptions.Left);
        score.rectTransform.sizeDelta = new Vector2(300f, 70f);
        score.rectTransform.anchoredPosition = new Vector2(150f, 0f);
        newBest = UITheme.Text(card, "NEW HIGH SCORE!", 24f, UITheme.AccentYellow);
        newBest.rectTransform.sizeDelta = new Vector2(400f, 30f);
        newBest.rectTransform.anchoredPosition = new Vector2(0f, -145f);

        highScore = Stat(card, "HIGH SCORE", -300f);
        perfect = Stat(card, "PERFECT", 0f);
        streak = Stat(card, "BEST STREAK", 300f);

        next = PixelButton.Create(Content, "NEXT LEVEL", null, UITheme.ButtonStyle.Primary, 560f, 92f, -205f, 42f);
        retry = PixelButton.Create(Content, "RETRY", () => flow.Retry(), UITheme.ButtonStyle.Secondary, 560f, 92f, -305f, 42f);
        songSelect = PixelButton.Create(Content, "SONG SELECT", () => flow.ShowLevelSelect(), UITheme.ButtonStyle.Secondary, 560f, 92f, -405f, 42f);
    }

    public override void Open()
    {
        Fill();
        base.Open();
    }

    void Fill()
    {
        bool won = flow.Won;
        ScoreKeeper keeper = GetComponent<ScoreKeeper>();
        if (keeper != null) keeper.RecordResult(); // safe to call twice; makes the high score current

        heading.text = won ? "LEVEL COMPLETED" : "GAME OVER";
        heading.color = won ? UITheme.White : UITheme.Danger;

        string song = flow.CurrentLevel?.displayName ?? "Level";
        string difficulty = DifficultyLabel(ScoreKeeper.SelectedDifficulty);
        subline.text = won ? $"{song}  ·  {difficulty}" : "SHIP DESTROYED";
        subline.color = won ? UITheme.White : UITheme.Danger;
        // Without stars, SHIP DESTROYED moves up into their place.
        subline.rectTransform.anchoredPosition = new Vector2(0f, won ? 10f : 70f);

        starRow.gameObject.SetActive(won);
        int earned = keeper != null ? keeper.Stars(won) : 0;
        for (int i = 0; i < stars.Length; i++)
            stars[i].color = i < earned ? UITheme.AccentYellow : new Color(1f, 1f, 1f, 0.18f);

        int points = keeper != null ? keeper.Score : 0;
        int best = keeper != null ? Mathf.Max(points, keeper.PreviousHighScore) : points;
        score.text = ScoreKeeper.FormatPoints(points);
        highScore.text = ScoreKeeper.FormatPoints(best);
        perfect.text = keeper != null ? $"{Mathf.RoundToInt(keeper.PerfectShare * 100f)}%" : "-";
        streak.text = (flow.PlayerHealth != null ? flow.PlayerHealth.BestStreak : 0).ToString();
        newBest.gameObject.SetActive(keeper != null && keeper.IsNewHighScore);

        // Buttons: Level Completed leads with NEXT LEVEL (when there is one); Game Over with RETRY.
        GameFlow.Level nextLevel = won ? NextLevel() : null;
        next.gameObject.SetActive(nextLevel != null);
        next.Button.onClick.RemoveAllListeners();
        if (nextLevel != null) next.Button.onClick.AddListener(() => flow.StartLevel(nextLevel));

        bool retryLeads = nextLevel == null;
        retry.SetStyle(retryLeads ? UITheme.ButtonStyle.Primary : UITheme.ButtonStyle.Secondary);
        float y = -205f;
        if (nextLevel != null) { Place(next, y); y -= 100f; }
        Place(retry, y); y -= 100f;
        Place(songSelect, y);
        first = nextLevel != null ? next.Button : retry.Button;
    }

    GameFlow.Level NextLevel()
    {
        GameFlow.Level current = flow.CurrentLevel;
        int i = current != null ? System.Array.IndexOf(flow.levels, current) : -1;
        if (i < 0 || i + 1 >= flow.levels.Length) return null;
        return GameFlow.CanLoad(flow.levels[i + 1]) ? flow.levels[i + 1] : null;
    }

    public static string DifficultyLabel(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "EASY",
        Difficulty.Difficult => "HARD",
        _ => "MEDIUM"
    };

    static void Place(PixelButton button, float y) =>
        ((RectTransform)button.transform).anchoredPosition = new Vector2(0f, y);

    static TextMeshProUGUI Label(RectTransform parent, float size, Color color, float y, float width)
    {
        TextMeshProUGUI label = UITheme.Text(parent, "", size, color);
        label.rectTransform.sizeDelta = new Vector2(width, size * 1.3f);
        label.rectTransform.anchoredPosition = new Vector2(0f, y);
        return label;
    }

    static TextMeshProUGUI Stat(RectTransform parent, string title, float x)
    {
        TextMeshProUGUI name = UITheme.Text(parent, title, 24f, UITheme.White);
        name.rectTransform.sizeDelta = new Vector2(280f, 34f);
        name.rectTransform.anchoredPosition = new Vector2(x, -195f);
        TextMeshProUGUI value = UITheme.Text(parent, "", 36f, UITheme.White);
        value.rectTransform.sizeDelta = new Vector2(280f, 48f);
        value.rectTransform.anchoredPosition = new Vector2(x, -235f);
        return value;
    }
}
