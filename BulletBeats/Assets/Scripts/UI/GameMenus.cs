using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Title, level select and end-of-level screens for <see cref="GameFlow"/>. Builds its own overlay
/// canvas (and an EventSystem if the scene has none), so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class GameMenus : MonoBehaviour
{
    public string gameTitle = "BULLET BEATS";
    public string tagline = "A 3D rhythm-based bullet hell";
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color winColor = new Color(1f, 0.85f, 0.2f);
    public Color loseColor = new Color(1f, 0.4f, 0.4f);
    public Color backdropColor = new Color(0.02f, 0.01f, 0.06f, 0.82f);

    const string ControlsHint = "W A S D  jump between spots      Mouse  aim      Left click  shoot when the ring closes";

    GameFlow flow;
    Font font;
    GameObject canvasObject;
    GameObject titlePanel, levelPanel, endPanel;
    Button titleFirst, levelFirst, endFirst;
    Text endHeading, endStats;

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

        endStats.text =
            $"{flow.CurrentLevel?.displayName ?? "Level"}\n\n" +
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
        BuildEndPanel();
    }

    void BuildTitlePanel()
    {
        titlePanel = NewPanel("Title");
        AddText(titlePanel, gameTitle, 120, accentColor, 220f, FontStyle.Bold);
        AddText(titlePanel, tagline, 30, Color.white, 120f);

        titleFirst = AddButton(titlePanel, "PLAY", -10f, flow.Play);
        AddButton(titlePanel, "LEVEL SELECT", -100f, flow.ShowLevelSelect);
#if !UNITY_WEBGL
        AddButton(titlePanel, "QUIT", -190f, flow.Quit);
#endif
        BuildAimAssistSlider();
        AddText(titlePanel, ControlsHint, 22, new Color(1f, 1f, 1f, 0.6f), -420f);
    }

    void BuildAimAssistSlider()
    {
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        float initial = shooter != null ? shooter.aimAssist : 1f;
        Text label = AddText(titlePanel, AimAssistLabel(initial), 26, Color.white, -280f);

        var sliderRect = new GameObject("AimAssist", typeof(RectTransform), typeof(Slider)).GetComponent<RectTransform>();
        sliderRect.SetParent(titlePanel.transform, false);
        sliderRect.sizeDelta = new Vector2(460f, 28f);
        sliderRect.anchoredPosition = new Vector2(0f, -330f);

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

    void BuildLevelPanel()
    {
        levelPanel = NewPanel("LevelSelect");
        AddText(levelPanel, "SELECT LEVEL", 72, accentColor, 300f, FontStyle.Bold);

        float y = 150f;
        foreach (GameFlow.Level level in flow.levels)
        {
            bool playable = GameFlow.CanLoad(level);
            string label = playable ? level.displayName : $"{level.displayName}  (not in build)";
            Button button = AddButton(levelPanel, label, y, () => flow.StartLevel(level));
            button.interactable = playable;
            if (playable && levelFirst == null) levelFirst = button;
            y -= 90f;
        }

        Button back = AddButton(levelPanel, "BACK", y - 40f, flow.ShowTitle);
        if (levelFirst == null) levelFirst = back;
    }

    void BuildEndPanel()
    {
        endPanel = NewPanel("End");
        endHeading = AddText(endPanel, "", 96, Color.white, 260f, FontStyle.Bold);
        endStats = AddText(endPanel, "", 30, Color.white, 50f);
        endStats.rectTransform.sizeDelta = new Vector2(1200f, 280f);

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

    Button AddButton(GameObject panel, string text, float y, UnityEngine.Events.UnityAction onClick)
    {
        var image = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        image.rectTransform.SetParent(panel.transform, false);
        image.rectTransform.sizeDelta = new Vector2(460f, 70f);
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
