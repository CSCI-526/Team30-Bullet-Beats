using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Title, level select, difficulty and end-of-level screens for <see cref="GameFlow"/>. Builds its own
/// overlay canvas (and an EventSystem if the scene has none), so it needs no scene setup.
/// Scores and high scores come from <see cref="ScoreKeeper"/>.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class GameMenus : MonoBehaviour
{
    public string gameTitle = "BULLET BEATS";
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color winColor = new Color(1f, 0.85f, 0.2f);
    public Color loseColor = new Color(1f, 0.4f, 0.4f);
    public Color backdropColor = new Color(0.02f, 0.01f, 0.06f, 0.82f);

    const float WideButton = 860f;

    GameFlow flow;
    Font font;
    GameObject canvasObject;
    GameObject titlePanel, levelPanel, difficultyPanel, endPanel;
    Button titleFirst, levelFirst, endFirst;
    Text endHeading, endStats, difficultySong, yourSongLabel;
    Button yourSongButton;
    readonly Button[] difficultyButtons = new Button[3];
    GameFlow.Level difficultyLevel;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildCanvas();
        Show(flow.State);
    }

    void OnEnable() => flow.StateChanged += Show;
    void OnDisable() => flow.StateChanged -= Show;

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Show(GameState state)
    {
        canvasObject.SetActive(state != GameState.Playing);
        titlePanel.SetActive(state == GameState.Title);
        levelPanel.SetActive(state == GameState.LevelSelect);
        difficultyPanel.SetActive(false);
        endPanel.SetActive(state == GameState.Ended);

        if (state == GameState.Ended) FillEndScreen();

        Button first = state switch
        {
            GameState.Title => titleFirst,
            GameState.LevelSelect => levelFirst,
            GameState.Ended => endFirst,
            _ => null
        };
        // Select a button so the menus also work with the keyboard (arrows + Enter).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    void FillEndScreen()
    {
        endHeading.text = flow.Won ? "LEVEL COMPLETE" : "GAME OVER";
        endHeading.color = flow.Won ? winColor : loseColor;

        int shots = flow.PerfectCount + flow.GoodCount + flow.MissCount;
        int onBeat = flow.PerfectCount + flow.GoodCount;
        string accuracy = shots > 0 ? $"{Mathf.RoundToInt(100f * onBeat / shots)}%" : "-";
        int seconds = Mathf.FloorToInt(flow.TimePlayed);
        int bestStreak = flow.PlayerHealth != null ? flow.PlayerHealth.BestStreak : 0;

        string scoreLines = "";
        ScoreKeeper keeper = GetComponent<ScoreKeeper>();
        if (keeper != null)
        {
            keeper.RecordResult();
            string best = keeper.IsNewHighScore
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(winColor)}>NEW HIGH SCORE!</color>"
                : $"High score  {ScoreKeeper.FormatPoints(Mathf.Max(keeper.Score, keeper.PreviousHighScore))}";
            scoreLines = $"Score  {ScoreKeeper.FormatPoints(keeper.Score)}\n{best}\n";
        }

        endStats.text =
            $"{flow.CurrentLevel?.displayName ?? "Level"}  -  {ScoreKeeper.DifficultyName(ScoreKeeper.SelectedDifficulty)}\n\n" +
            scoreLines +
            $"Time  {seconds / 60}:{seconds % 60:00}\n" +
            $"Perfect  {flow.PerfectCount}     Good  {flow.GoodCount}     Miss  {flow.MissCount}\n" +
            $"On-beat accuracy  {accuracy}\n" +
            $"Best streak  {bestStreak}";
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
        // The project uses the Input System only, so the UI needs its input module.
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    void BuildCanvas()
    {
        canvasObject = new GameObject("GameMenus", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(backdrop.rectTransform, canvasObject.transform);
        backdrop.color = backdropColor;

        BuildTitlePanel();
        BuildLevelPanel();
        BuildDifficultyPanel();
        BuildEndPanel();
    }

    void BuildTitlePanel()
    {
        titlePanel = NewPanel("Title");
        AddText(titlePanel, gameTitle, 120, accentColor, 220f, FontStyle.Bold);

        titleFirst = AddButton(titlePanel, "PLAY", -10f, flow.Play);
        AddButton(titlePanel, "TUTORIAL", -100f, flow.StartTutorial);
        AddButton(titlePanel, "LEVEL SELECT", -190f, flow.ShowLevelSelect);
#if !UNITY_WEBGL
        AddButton(titlePanel, "QUIT", -280f, flow.Quit);
#endif
        BuildAimAssistSlider();
        BuildHoldToFireToggle();
    }

    void BuildAimAssistSlider()
    {
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        float initial = shooter != null ? shooter.aimAssist : 1f;
        Text label = AddText(titlePanel, AimAssistLabel(initial), 26, Color.white, -370f);

        var sliderRect = new GameObject("AimAssist", typeof(RectTransform), typeof(Slider)).GetComponent<RectTransform>();
        sliderRect.SetParent(titlePanel.transform, false);
        sliderRect.sizeDelta = new Vector2(460f, 28f);
        sliderRect.anchoredPosition = new Vector2(0f, -420f);

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(background.rectTransform, sliderRect);
        background.color = new Color(0.08f, 0.08f, 0.16f, 0.95f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        Stretch(fillArea, sliderRect);
        fillArea.offsetMin = new Vector2(8f, 6f);
        fillArea.offsetMax = new Vector2(-8f, -6f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(fill.rectTransform, fillArea);
        fill.color = accentColor;

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        handle.rectTransform.SetParent(sliderRect, false);
        handle.rectTransform.sizeDelta = new Vector2(18f, 36f);
        handle.color = Color.white;

        var slider = sliderRect.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(value =>
        {
            if (shooter == null) shooter = FindFirstObjectByType<PlayerShooter>();
            shooter?.SetAimAssist(value);
            label.text = AimAssistLabel(value);
        });
    }

    static string AimAssistLabel(float value) => $"AIM ASSIST  {Mathf.RoundToInt(value * 100f)}%";

    void BuildHoldToFireToggle()
    {
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        bool on = PlayerShooter.HoldToFireEnabled;
        if (shooter != null) shooter.SetHoldToFire(on);

        var row = new GameObject("HoldToFire", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(titlePanel.transform, false);
        row.sizeDelta = new Vector2(460f, 56f);
        row.anchoredPosition = new Vector2(0f, -500f);

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(row, false);
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = new Vector2(-120f, 0f);
        label.font = font;
        label.fontSize = 26;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        label.text = "HOLD TO FIRE";

        const float trackW = 96f, trackH = 40f, knobSize = 32f;
        var switchRoot = new GameObject("Switch", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
        switchRoot.SetParent(row, false);
        switchRoot.anchorMin = switchRoot.anchorMax = new Vector2(1f, 0.5f);
        switchRoot.pivot = new Vector2(1f, 0.5f);
        switchRoot.sizeDelta = new Vector2(trackW, trackH);
        switchRoot.anchoredPosition = Vector2.zero;

        var hit = switchRoot.GetComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0.001f);
        hit.raycastTarget = true;

        var track = new GameObject("Track", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(track.rectTransform, switchRoot);
        track.raycastTarget = false;

        var state = new GameObject("State", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        Stretch(state.rectTransform, switchRoot);
        state.font = font;
        state.fontSize = 16;
        state.fontStyle = FontStyle.Bold;
        state.alignment = TextAnchor.MiddleCenter;
        state.raycastTarget = false;

        var knob = new GameObject("Knob", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        knob.rectTransform.SetParent(switchRoot, false);
        knob.rectTransform.sizeDelta = new Vector2(knobSize, knobSize);
        knob.color = Color.white;
        knob.raycastTarget = false;

        void Paint(bool enabled)
        {
            track.color = enabled
                ? accentColor
                : new Color(0.18f, 0.18f, 0.28f, 0.95f);
            state.text = enabled ? "ON" : "OFF";
            state.color = enabled
                ? new Color(0.05f, 0.08f, 0.12f, 0.9f)
                : new Color(0.75f, 0.78f, 0.85f, 0.9f);
            float x = enabled ? (trackW - knobSize) * 0.5f - 4f : -(trackW - knobSize) * 0.5f + 4f;
            knob.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }

        Paint(on);

        var button = switchRoot.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.75f, 0.85f, 0.95f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            bool next = !PlayerShooter.HoldToFireEnabled;
            if (shooter == null) shooter = FindFirstObjectByType<PlayerShooter>();
            if (shooter != null) shooter.SetHoldToFire(next);
            else PlayerShooter.HoldToFireEnabled = next;
            Paint(next);
        });
    }

    void BuildLevelPanel()
    {
        levelPanel = NewPanel("LevelSelect");
        AddText(levelPanel, "SELECT LEVEL", 72, accentColor, 460f, FontStyle.Bold);

        var viewport = new GameObject("SongList", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.SetParent(levelPanel.transform, false);
        viewportRect.sizeDelta = new Vector2(920f, 560f);
        viewportRect.anchoredPosition = new Vector2(0f, 16f);
        AddRankHeader(levelPanel, 324f);
        var viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportImage.raycastTarget = true;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.SetParent(viewportRect, false);
        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(WideButton, 0f);

        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 16f;
        layout.padding = new RectOffset(0, 0, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewportRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        foreach (GameFlow.Level level in flow.levels)
        {
            bool playable = GameFlow.CanLoad(level);
            Button button = AddSongButton(content, level, playable, () => ShowDifficultySelect(level));
            button.interactable = playable;
            if (playable && levelFirst == null) levelFirst = button;
        }

        AddText(levelPanel, "OR LOAD A FILE", 22, new Color(0.95f, 0.78f, 0.35f), -340f);
        yourSongButton = AddFilePicker(levelPanel, -406f);
        Button back = AddButton(levelPanel, "BACK", -500f, flow.ShowTitle);
        if (levelFirst == null) levelFirst = back;
    }

    const string ChooseFilePrompt = "Choose a .wav, .mp3, or .ogg file…";

    Button AddFilePicker(GameObject panel, float y)
    {
        var image = new GameObject("ChooseFile", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        image.rectTransform.SetParent(panel.transform, false);
        image.rectTransform.sizeDelta = new Vector2(WideButton, 70f);
        image.rectTransform.anchoredPosition = new Vector2(0f, y);
        image.color = Color.white;

        var outline = image.GetComponent<Outline>();
        outline.effectColor = new Color(0.95f, 0.78f, 0.35f);
        outline.effectDistance = new Vector2(2f, -2f);

        var button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.04f, 0.05f, 0.08f, 0.95f);
        colors.highlightedColor = new Color(0.16f, 0.13f, 0.05f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.95f, 0.78f, 0.35f, 1f);
        colors.disabledColor = new Color(0.08f, 0.08f, 0.1f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(PickSong);

        yourSongLabel = new GameObject("Prompt", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        Stretch(yourSongLabel.rectTransform, image.transform);
        yourSongLabel.rectTransform.offsetMin = new Vector2(28f, 0f);
        yourSongLabel.rectTransform.offsetMax = new Vector2(-210f, 0f);
        yourSongLabel.font = font;
        yourSongLabel.fontSize = 26;
        yourSongLabel.fontStyle = FontStyle.Italic;
        yourSongLabel.alignment = TextAnchor.MiddleLeft;
        yourSongLabel.color = new Color(1f, 1f, 1f, 0.72f);
        yourSongLabel.raycastTarget = false;
        yourSongLabel.text = ChooseFilePrompt;

        var browse = new GameObject("Browse", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        browse.rectTransform.SetParent(image.transform, false);
        browse.rectTransform.anchorMin = browse.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        browse.rectTransform.pivot = new Vector2(1f, 0.5f);
        browse.rectTransform.sizeDelta = new Vector2(168f, 46f);
        browse.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
        browse.color = new Color(0.95f, 0.78f, 0.35f, 1f);
        browse.raycastTarget = false;

        var browseLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        Stretch(browseLabel.rectTransform, browse.transform);
        browseLabel.font = font;
        browseLabel.fontSize = 24;
        browseLabel.fontStyle = FontStyle.Bold;
        browseLabel.alignment = TextAnchor.MiddleCenter;
        browseLabel.color = new Color(0.12f, 0.08f, 0.02f, 1f);
        browseLabel.raycastTarget = false;
        browseLabel.text = "BROWSE…";
        return button;
    }

    // Rank columns share one x so the header and every song line up. x is from the center of an 860-wide row.
    static readonly float[] RankColumnX = { 70f, 210f, 350f };

    void AddRankHeader(GameObject panel, float y)
    {
        string[] titles = { "EASY", "MEDIUM", "HARD" };
        for (int i = 0; i < titles.Length; i++)
        {
            var label = new GameObject(titles[i], typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.rectTransform.SetParent(panel.transform, false);
            label.rectTransform.sizeDelta = new Vector2(140f, 32f);
            label.rectTransform.anchoredPosition = new Vector2(RankColumnX[i], y);
            label.font = font;
            label.fontSize = 18;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = accentColor;
            label.raycastTarget = false;
            label.text = titles[i];
        }
    }

    Button AddSongButton(GameObject panel, GameFlow.Level level, bool playable, UnityEngine.Events.UnityAction onClick)
    {
        Button button = AddButton(panel, "", 0f, onClick, WideButton);
        button.name = level.displayName;
        Text title = button.GetComponentInChildren<Text>();
        title.alignment = TextAnchor.MiddleLeft;
        title.rectTransform.anchorMin = new Vector2(0f, 0f);
        title.rectTransform.anchorMax = new Vector2(0f, 1f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(28f, 0f);
        title.rectTransform.sizeDelta = new Vector2(380f, 0f);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.text = playable ? level.displayName : $"{level.displayName}  (not in build)";

        int[] ranks = { level.easyLevel, level.mediumLevel, level.hardLevel };
        for (int i = 0; i < ranks.Length; i++)
        {
            var rank = new GameObject("Rank", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            rank.rectTransform.SetParent(button.transform, false);
            rank.rectTransform.sizeDelta = new Vector2(120f, 70f);
            rank.rectTransform.anchoredPosition = new Vector2(RankColumnX[i], 0f);
            rank.font = font;
            rank.fontSize = 30;
            rank.fontStyle = FontStyle.Bold;
            rank.alignment = TextAnchor.MiddleCenter;
            rank.color = Color.white;
            rank.raycastTarget = false;
            rank.text = ranks[i] > 0 ? ranks[i].ToString() : "-";
        }
        return button;
    }

    void BuildDifficultyPanel()
    {
        difficultyPanel = NewPanel("Difficulty");
        AddText(difficultyPanel, "SELECT DIFFICULTY", 72, accentColor, 300f, FontStyle.Bold);
        difficultySong = AddText(difficultyPanel, "", 36, Color.white, 210f);

        float y = 90f;
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            var difficulty = (Difficulty)i;
            difficultyButtons[i] = AddButton(difficultyPanel, ScoreKeeper.DifficultyName(difficulty), y,
                () => StartLevel(difficulty), WideButton);
            y -= 90f;
        }

        AddButton(difficultyPanel, "BACK", y - 40f, () => Show(GameState.LevelSelect));
    }

    /// <summary>Second step of level select: pick a difficulty for <paramref name="level"/>.</summary>
    void ShowDifficultySelect(GameFlow.Level level)
    {
        difficultyLevel = level;
        levelPanel.SetActive(false);
        difficultyPanel.SetActive(true);
        difficultySong.text = level.displayName;

        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            var difficulty = (Difficulty)i;
            int high = ScoreKeeper.GetHighScore(level, difficulty);
            int chartLevel = level.ChartLevel(difficulty);
            string rating = chartLevel > 0 ? $"LV {chartLevel}      " : "";
            difficultyButtons[i].GetComponentInChildren<Text>().text =
                $"{ScoreKeeper.DifficultyName(difficulty)}  {rating}HIGH SCORE  {(high >= 0 ? ScoreKeeper.FormatPoints(high) : "-")}";
        }

        Button selected = difficultyButtons[(int)ScoreKeeper.SelectedDifficulty];
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected.gameObject);
    }

    void StartLevel(Difficulty difficulty)
    {
        ScoreKeeper.SelectedDifficulty = difficulty;
        flow.StartLevel(difficultyLevel);
    }

    void PickSong()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Choose a song", "", "wav,mp3,ogg");
        if (string.IsNullOrEmpty(path)) return;
        StartCoroutine(LoadPickedSong(path));
#else
        Debug.LogWarning("Choosing a song file works in the Unity editor.");
#endif
    }

    IEnumerator LoadPickedSong(string path)
    {
        if (yourSongButton != null) yourSongButton.interactable = false;
        if (yourSongLabel != null) yourSongLabel.text = "Loading file…";

        AudioClip clip = null;
        yield return SongFile.Load(path, loaded => clip = loaded);

        if (yourSongButton != null) yourSongButton.interactable = true;
        if (yourSongLabel != null) yourSongLabel.text = ChooseFilePrompt;
        if (clip == null) yield break;

        GameFlow.RememberCustomSong(path, clip, Path.GetFileNameWithoutExtension(path));
        ShowDifficultySelect(GameFlow.CustomLevel);
    }

    void BuildEndPanel()
    {
        endPanel = NewPanel("End");
        endHeading = AddText(endPanel, "", 96, Color.white, 260f, FontStyle.Bold);
        endStats = AddText(endPanel, "", 30, Color.white, 50f);
        endStats.rectTransform.sizeDelta = new Vector2(1200f, 340f);

        endFirst = AddButton(endPanel, "RETRY", -170f, flow.Retry);
        AddButton(endPanel, "LEVEL SELECT", -260f, flow.ShowLevelSelect);
        AddButton(endPanel, "MAIN MENU", -350f, flow.ShowTitle);
    }

    GameObject NewPanel(string name)
    {
        var panel = new GameObject(name, typeof(RectTransform));
        Stretch((RectTransform)panel.transform, canvasObject.transform);
        return panel;
    }

    Text AddText(GameObject panel, string text, int size, Color color, float y, FontStyle style = FontStyle.Normal)
    {
        var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(panel.transform, false);
        label.rectTransform.sizeDelta = new Vector2(1600f, size * 1.4f);
        label.rectTransform.anchoredPosition = new Vector2(0f, y);
        label.font = font;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    Button AddButton(GameObject panel, string text, float y, UnityEngine.Events.UnityAction onClick, float width = 460f)
    {
        var image = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        image.rectTransform.SetParent(panel.transform, false);
        image.rectTransform.sizeDelta = new Vector2(width, 70f);
        image.rectTransform.anchoredPosition = new Vector2(0f, y);
        image.color = Color.white;

        var outline = image.GetComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        colors.highlightedColor = new Color(0.15f, 0.35f, 0.45f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accentColor;
        colors.disabledColor = new Color(0.08f, 0.08f, 0.1f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        Stretch(label.rectTransform, image.transform);
        label.font = font;
        label.fontSize = 30;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = text;

        var element = image.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 70f;
        element.minHeight = 70f;
        return button;
    }

    static void Stretch(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
