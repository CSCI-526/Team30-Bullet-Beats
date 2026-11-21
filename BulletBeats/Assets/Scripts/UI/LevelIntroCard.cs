using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating card shown before each level over the blurred, dimmed game, for <see cref="showSeconds"/>:
/// the song name, the difficulty, how long the level lasts and the goal ("SURVIVE THE SONG").
/// Then the card goes and a big 3, 2, 1 counts on the session's count-in beats, with GO! as the song
/// starts; the blur clears and play begins. <see cref="GameFlow"/> waits for <see cref="Show"/> before
/// it starts the count-in. Builds its own overlay canvas, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class LevelIntroCard : MonoBehaviour
{
    [Tooltip("Seconds the card stays up before the count-in starts.")]
    public float showSeconds = 2f;
    [Tooltip("Seconds GO! stays on screen once the song starts.")]
    public float goSeconds = 0.6f;

    const float GiveUpSeconds = 8f;

    GameFlow flow;
    RhythmSession session;
    RectTransform root, card;
    BlurBackdrop backdrop;
    TextMeshProUGUI songLabel, difficultyLabel, lengthLabel, goalLabel, countLabel;
    GameObject lengthGroup;
    float shownAt, goUntil = -1f;
    bool showing;

    /// <summary>True while the card or its countdown is on screen.</summary>
    public bool IsShowing => showing;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        session = FindFirstObjectByType<RhythmSession>();
        Build();
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
        if (root != null) Destroy(root.gameObject);
    }

    /// <summary>Shows the card for <paramref name="level"/>. Finishes when the count-in should start.</summary>
    public IEnumerator Show(GameFlow.Level level)
    {
        Fill(level);
        showing = true;
        goUntil = -1f;
        shownAt = Time.time;
        card.gameObject.SetActive(false);
        countLabel.gameObject.SetActive(false);
        root.gameObject.SetActive(true);
        backdrop.Show(this, () =>
        {
            if (!showing) return;
            StartCoroutine(ScreenTransition.PopIn(card));
        });
        // Scaled time, so the card waits too if the game is frozen.
        for (float t = 0f; t < showSeconds; t += Time.deltaTime) yield return null;
    }

    void Update()
    {
        if (!showing) return;
        BeatClock clock = BeatClock.Instance;
        bool songStarted = clock != null && clock.IsMusicPlaying;

        if (songStarted && goUntil < 0f)
        {
            // The song is on: GO!, clear the blur, then hide.
            goUntil = Time.unscaledTime + goSeconds;
            card.gameObject.SetActive(false);
            backdrop.Hide();
            SetCount("GO!", UITheme.ImpactOrange);
        }
        bool goDone = goUntil >= 0f && Time.unscaledTime >= goUntil;
        // Gone after GO!, or if the level ended or never started a count-in.
        if (goDone || flow.State != GameState.Playing || Time.time - shownAt > showSeconds + GiveUpSeconds)
            Hide();

        if (countLabel.gameObject.activeSelf)
        {
            // Each number lands big and settles, in a few pixel-style steps.
            float age = Mathf.Clamp01((Time.unscaledTime - countShownAt) / 0.2f);
            float step = Mathf.Ceil(age * 4f) / 4f;
            countLabel.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, step);
        }
    }

    float countShownAt;

    void OnCountInBeat(int beatsLeft)
    {
        if (!showing || goUntil >= 0f) return;
        card.gameObject.SetActive(false);
        SetCount(beatsLeft.ToString(), UITheme.White);
    }

    void SetCount(string text, Color color)
    {
        countLabel.gameObject.SetActive(true);
        countLabel.text = text;
        countLabel.color = color;
        countShownAt = Time.unscaledTime;
    }

    void Hide()
    {
        showing = false;
        backdrop.Hide();
        root.gameObject.SetActive(false);
    }

    void Fill(GameFlow.Level level)
    {
        songLabel.text = level != null ? level.displayName : "YOUR SONG";
        difficultyLabel.text = ResultCard.DifficultyLabel(ScoreKeeper.SelectedDifficulty);
        int seconds = LengthSeconds(level);
        lengthGroup.SetActive(seconds > 0);
        lengthLabel.text = $"{seconds} SECONDS";
        goalLabel.text = Goal(level, seconds);
    }

    /// <summary>Length of this level: the hand-made chart's end for the chosen difficulty, else the song.</summary>
    static int LengthSeconds(GameFlow.Level level)
    {
        if (level == null) return 0;
        if (!string.IsNullOrEmpty(level.chartJson))
        {
            var file = Resources.Load<TextAsset>("Charts/" + ScriptedLevel.ChartName(level.chartJson, ScoreKeeper.SelectedDifficulty));
            if (file != null)
            {
                var data = JsonUtility.FromJson<ScriptedLevel.Data>(file.text);
                if (data != null && data.endTime > 0f) return Mathf.CeilToInt(data.endTime);
            }
        }
        return level.music != null ? Mathf.CeilToInt(level.music.length) : 0;
    }

    static string Goal(GameFlow.Level level, int seconds)
    {
        if (!CornerMute.Muted) return "SURVIVE THE SONG";
        if (seconds > 0) return $"SURVIVE {seconds} SECONDS";
        return "SURVIVE TILL THE END";
    }

    void Build()
    {
        var canvasObject = new GameObject("LevelIntroCard", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)canvasObject.transform;
        backdrop = BlurBackdrop.Create(root);

        card = PixelPanel.Create(root, new Vector2(1000f, 460f), Vector2.zero, "IntroCard");

        songLabel = UITheme.Text(card, "", 84f, UITheme.White);
        songLabel.rectTransform.sizeDelta = new Vector2(920f, 110f);
        songLabel.rectTransform.anchoredPosition = new Vector2(0f, 125f);
        songLabel.enableAutoSizing = true;
        songLabel.fontSizeMin = 40f;
        songLabel.fontSizeMax = 84f;

        // Divider: two thin cyan lines with equalizer bars in the middle.
        for (int side = -1; side <= 1; side += 2)
        {
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            line.rectTransform.SetParent(card, false);
            line.rectTransform.sizeDelta = new Vector2(330f, 4f);
            line.rectTransform.anchoredPosition = new Vector2(side * 230f, 45f);
            line.color = new Color(UITheme.ElectricCyan.r, UITheme.ElectricCyan.g, UITheme.ElectricCyan.b, 0.6f);
            line.raycastTarget = false;
        }
        PixelArt.IconImage(card, PixelArt.Icon.Equalizer, 70f, UITheme.ElectricCyan).rectTransform.anchoredPosition = new Vector2(0f, 45f);

        // Difficulty pill | clock + length.
        RectTransform pill = Frame(card, "btn_blue_normal", new Vector2(240f, 84f), new Vector2(-190f, -40f));
        difficultyLabel = UITheme.Text(pill, "", 36f, UITheme.ElectricCyan);
        difficultyLabel.rectTransform.sizeDelta = new Vector2(220f, 60f);

        var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        divider.rectTransform.SetParent(card, false);
        divider.rectTransform.sizeDelta = new Vector2(4f, 60f);
        divider.rectTransform.anchoredPosition = new Vector2(-40f, -40f);
        divider.color = new Color(UITheme.ElectricCyan.r, UITheme.ElectricCyan.g, UITheme.ElectricCyan.b, 0.6f);
        divider.raycastTarget = false;

        lengthGroup = new GameObject("Length", typeof(RectTransform)).gameObject;
        var lengthRect = (RectTransform)lengthGroup.transform;
        lengthRect.SetParent(card, false);
        lengthRect.anchoredPosition = new Vector2(150f, -40f);
        PixelArt.IconImage(lengthRect, PixelArt.Icon.Clock, 54f, UITheme.ImpactOrange).rectTransform.anchoredPosition = new Vector2(-150f, 0f);
        lengthLabel = UITheme.Text(lengthRect, "", 40f, UITheme.White, align: TextAlignmentOptions.Left);
        lengthLabel.rectTransform.sizeDelta = new Vector2(320f, 60f);
        lengthLabel.rectTransform.anchoredPosition = new Vector2(70f, 0f);

        // Goal in an orange outline frame.
        RectTransform goal = Frame(card, "btn_outline_normal", new Vector2(780f, 104f), new Vector2(0f, -150f));
        goalLabel = UITheme.Text(goal, "", 46f, UITheme.White);
        goalLabel.rectTransform.sizeDelta = new Vector2(740f, 70f);

        countLabel = UITheme.Text(root, "", 220f, UITheme.White);
        countLabel.rectTransform.sizeDelta = new Vector2(900f, 300f);
        countLabel.outlineWidth = 0.15f;
        countLabel.outlineColor = new Color32(11, 15, 26, 255);

        canvasObject.SetActive(false);
    }

    static RectTransform Frame(RectTransform parent, string art, Vector2 size, Vector2 position)
    {
        var image = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = size;
        image.rectTransform.anchoredPosition = position;
        image.sprite = UITheme.Art(art);
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 256f / size.y;
        image.raycastTarget = false;
        return image.rectTransform;
    }
}
