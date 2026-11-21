using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Song select as a pixel iPod.
/// <para><b>Browse:</b> the iPod is centred and its screen shows three songs: the highlighted one in the
/// middle with the previous and next above and below. ▲/▼ show only when there are more songs that way.
/// Up/Down keys, the mouse wheel, the wheel's top/bottom and clicking a row move the highlight; the
/// centre SELECT button or Enter confirms.</para>
/// <para><b>Details:</b> confirming slides the iPod left and the details card in on the right (song,
/// high score, EASY / MEDIUM / HARD, START). The iPod keeps working, so the song can change without going
/// back. No difficulty is picked to begin with, and START needs one.</para>
/// <para>Keys in details: Up/Down change song, Left/Right pick difficulty, Enter starts. Esc goes from
/// details back to browsing, and from browsing back to the main menu.</para>
/// </summary>
public class SongSelectScreen : UIScreen
{
    const float IPodHeight = 760f;
    const float IPodShift = -430f;
    const float RowHeight = 92f;
    /// <summary>Seconds for the rows to slide one step.</summary>
    const float ScrollSeconds = 0.12f;

    GameFlow flow;
    RectTransform ipod, rowsRoot, details;
    readonly TextMeshProUGUI[] rowLabels = new TextMeshProUGUI[3];
    readonly Image[] rowFrames = new Image[3];
    Image upArrow, downArrow;
    TextMeshProUGUI detailTitle, detailBest;
    readonly PixelButton[] difficultyButtons = new PixelButton[3];
    PixelButton start;

    int index;
    int? difficulty;
    bool showingDetails;
    Coroutine scroll, move;
    float nextWheelStep;

    protected override int SortingOrder => 210;
    protected override Selectable FirstSelected => null; // keys are handled here, not by the EventSystem

    GameFlow.Level[] Levels => flow.levels;

    protected override void Awake()
    {
        flow = GetComponent<GameFlow>();
        base.Awake();
    }

    protected override void Build()
    {
        MenuBackground.Create(Root, UITheme.BusyScreenDim);

        CornerButton("BACK", PixelArt.Icon.ArrowLeft, false, Back);
        CornerButton("SETTINGS", PixelArt.Icon.Gear, true, () => GetComponent<SettingsScreen>()?.Open(onBack: null));

        RectTransform logo = Art(Content, UITheme.Logo, 330f);
        logo.anchoredPosition = new Vector2(0f, 465f);
        TextMeshProUGUI title = UITheme.Text(Content, "SONG SELECT", 56f, UITheme.White);
        title.rectTransform.sizeDelta = new Vector2(800f, 70f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 345f);

        BuildIPod();
        BuildDetails();

        TextMeshProUGUI hint = UITheme.Text(Content, "UP / DOWN  BROWSE      ENTER  SELECT      ESC  BACK", 24f, UITheme.MutedGrey, display: false);
        hint.rectTransform.sizeDelta = new Vector2(1400f, 40f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, -505f);
    }

    void BuildIPod()
    {
        ipod = Art(Content, UITheme.IPod, 0f, IPodHeight);
        ipod.anchoredPosition = new Vector2(0f, -70f);
        float s = IPodHeight / 1397f; // art pixels to canvas units

        // The screen inside the art: centre (495, 385), about 760 x 580 art pixels, measured from the top-left.
        var screen = new GameObject("Screen", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        screen.SetParent(ipod, false);
        screen.sizeDelta = new Vector2(760f * s, 580f * s);
        screen.anchoredPosition = new Vector2((495f - 493f) * s, (698f - 385f) * s);

        rowsRoot = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
        rowsRoot.SetParent(screen, false);
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            PixelButton row = PixelButton.Create(rowsRoot, "", () => ClickRow(slot),
                i == 1 ? UITheme.ButtonStyle.SolidOrange : UITheme.ButtonStyle.Secondary, 380f, 78f, (1 - i) * RowHeight, i == 1 ? 34f : 26f);
            row.hoverScale = 1.02f;
            row.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            rowLabels[i] = row.Label;
            rowFrames[i] = row.GetComponent<Image>();
        }

        upArrow = PixelArt.IconImage(screen, PixelArt.Icon.ArrowUp, 26f, UITheme.White);
        upArrow.rectTransform.anchoredPosition = new Vector2(0f, 1.5f * RowHeight + 6f);
        downArrow = PixelArt.IconImage(screen, PixelArt.Icon.ArrowDown, 26f, UITheme.White);
        downArrow.rectTransform.anchoredPosition = new Vector2(0f, -1.5f * RowHeight - 6f);

        // Click wheel: centre (493, 1030), outer radius about 285 art pixels, centre button about 130.
        Vector2 wheel = new Vector2(0f, (698f - 1030f) * s);
        float outer = 285f * s, inner = 130f * s;
        WheelZone("Wheel Up", wheel + new Vector2(0f, (outer + inner) * 0.5f), new Vector2(outer * 1.1f, outer - inner), () => Step(-1));
        WheelZone("Wheel Down", wheel - new Vector2(0f, (outer + inner) * 0.5f), new Vector2(outer * 1.1f, outer - inner), () => Step(1));
        PixelArt.IconImage(ipod, PixelArt.Icon.ArrowUp, 30f, UITheme.White).rectTransform.anchoredPosition = wheel + new Vector2(0f, (outer + inner) * 0.5f);
        PixelArt.IconImage(ipod, PixelArt.Icon.ArrowDown, 30f, UITheme.White).rectTransform.anchoredPosition = wheel - new Vector2(0f, (outer + inner) * 0.5f);

        Button select = WheelZone("Select", wheel, new Vector2(inner * 1.7f, inner * 1.7f), Confirm);
        TextMeshProUGUI selectLabel = UITheme.Text(select.transform, "SELECT", 24f, UITheme.White);
        selectLabel.rectTransform.sizeDelta = new Vector2(inner * 1.7f, 40f);
        select.targetGraphic = selectLabel;
        ColorBlock colors = select.colors;
        colors.highlightedColor = UITheme.AccentYellow;
        colors.pressedColor = UITheme.ImpactOrange;
        colors.selectedColor = Color.white;
        select.colors = colors;
    }

    Button WheelZone(string name, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var zone = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)zone.transform;
        rect.SetParent(ipod, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        zone.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // invisible, only catches clicks
        var button = zone.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        return button;
    }

    void BuildDetails()
    {
        details = PixelPanel.Create(Content, new Vector2(820f, 700f), new Vector2(390f, -70f), "Details");

        // Song visual: a framed equalizer until each song has its own art.
        RectTransform art = PixelPanel.Create(details, new Vector2(560f, 210f), new Vector2(0f, 200f), "SongArt");
        Image bars = PixelArt.IconImage(art, PixelArt.Icon.Equalizer, 150f, UITheme.ElectricCyan);
        bars.rectTransform.anchoredPosition = Vector2.zero;

        detailTitle = UITheme.Text(details, "", 50f, UITheme.White);
        detailTitle.rectTransform.sizeDelta = new Vector2(760f, 70f);
        detailTitle.rectTransform.anchoredPosition = new Vector2(0f, 45f);
        detailTitle.enableAutoSizing = true;
        detailTitle.fontSizeMin = 28f;
        detailTitle.fontSizeMax = 50f;

        detailBest = UITheme.Text(details, "", 26f, UITheme.AccentYellow, display: false);
        detailBest.rectTransform.sizeDelta = new Vector2(760f, 40f);
        detailBest.rectTransform.anchoredPosition = new Vector2(0f, -15f);

        TextMeshProUGUI label = UITheme.Text(details, "DIFFICULTY", 26f, UITheme.MutedGrey);
        label.rectTransform.sizeDelta = new Vector2(760f, 40f);
        label.rectTransform.anchoredPosition = new Vector2(0f, -75f);

        string[] names = { "EASY", "MEDIUM", "HARD" };
        for (int i = 0; i < 3; i++)
        {
            int d = i;
            difficultyButtons[i] = PixelButton.Create(details, names[i], () => PickDifficulty(d),
                UITheme.ButtonStyle.Secondary, 230f, 80f, -140f, 30f);
            ((RectTransform)difficultyButtons[i].transform).anchoredPosition = new Vector2(-245f + 245f * i, -140f);
            difficultyButtons[i].Button.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        start = PixelButton.Create(details, "START", StartSong, UITheme.ButtonStyle.Primary, 420f, 96f, -265f, 46f);
        start.Button.navigation = new Navigation { mode = Navigation.Mode.None };

        details.gameObject.SetActive(false);
    }

    public override void Open()
    {
        showingDetails = false;
        difficulty = null;
        if (Levels != null && Levels.Length > 0)
        {
            int current = System.Array.IndexOf(Levels, flow.CurrentLevel);
            index = Mathf.Clamp(current, 0, Levels.Length - 1);
        }
        ipod.anchoredPosition = new Vector2(0f, ipod.anchoredPosition.y);
        details.gameObject.SetActive(false);
        Refresh();
        base.Open();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsOpen || Levels == null || Levels.Length == 0) return;
        SettingsScreen settings = GetComponent<SettingsScreen>();
        if (ChangedThisFrame || (settings != null && (settings.IsOpen || settings.ClosedFrame == Time.frameCount))) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) Step(-1);
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) Step(1);
            if (showingDetails && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame))
                PickDifficulty(Mathf.Max(0, (difficulty ?? 1) - 1));
            if (showingDetails && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame))
                PickDifficulty(Mathf.Min(2, (difficulty ?? 1) + 1));
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (!showingDetails) Confirm();
                else if (difficulty.HasValue) StartSong();
            }
            if (keyboard.escapeKey.wasPressedThisFrame) Back();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            // One step per notch: browsers send many small scroll events, so wait a moment between steps.
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && Time.unscaledTime >= nextWheelStep)
            {
                Step(wheel > 0f ? -1 : 1);
                nextWheelStep = Time.unscaledTime + 0.12f;
            }
        }

        // Keys are handled above; keep the EventSystem from also pressing a focused button.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void Step(int direction)
    {
        int next = Mathf.Clamp(index + direction, 0, Levels.Length - 1);
        if (next == index) return;
        index = next;
        Refresh();
        if (scroll != null) StopCoroutine(scroll);
        // Rows start one step away and glide into place, so the list scrolls instead of jumping.
        rowsRoot.anchoredPosition = new Vector2(0f, -direction * RowHeight * 0.6f);
        scroll = StartCoroutine(ScreenTransition.SlideTo(rowsRoot, Vector2.zero, ScrollSeconds));
    }

    void ClickRow(int slot)
    {
        if (slot == 1) Confirm();
        else Step(slot - 1);
    }

    void Confirm()
    {
        if (showingDetails || !GameFlow.CanLoad(Levels[index])) return;
        showingDetails = true;
        Refresh();
        if (move != null) StopCoroutine(move);
        move = StartCoroutine(ShowDetails());
    }

    IEnumerator ShowDetails()
    {
        StartCoroutine(ScreenTransition.SlideTo(ipod, new Vector2(IPodShift, ipod.anchoredPosition.y)));
        yield return ScreenTransition.PopIn(details, 0.12f);
    }

    void Back()
    {
        if (showingDetails)
        {
            showingDetails = false;
            difficulty = null;
            if (move != null) StopCoroutine(move);
            details.gameObject.SetActive(false);
            move = StartCoroutine(ScreenTransition.SlideTo(ipod, new Vector2(0f, ipod.anchoredPosition.y)));
            Refresh();
            return;
        }
        flow.ShowTitle();
    }

    void PickDifficulty(int d)
    {
        if (!showingDetails) return;
        difficulty = d;
        Refresh();
    }

    void StartSong()
    {
        if (!showingDetails || !difficulty.HasValue) return;
        GameFlow.Level level = Levels[index];
        if (!GameFlow.CanLoad(level)) return;
        ScoreKeeper.SelectedDifficulty = (Difficulty)difficulty.Value;
        flow.StartLevel(level);
    }

    void Refresh()
    {
        if (Levels == null || Levels.Length == 0) return;
        for (int i = 0; i < 3; i++)
        {
            int song = index - 1 + i;
            bool exists = song >= 0 && song < Levels.Length;
            rowFrames[i].gameObject.SetActive(exists);
            if (!exists) continue;
            GameFlow.Level level = Levels[song];
            rowLabels[i].text = GameFlow.CanLoad(level) ? level.displayName : level.displayName + " (soon)";
        }
        upArrow.enabled = index > 0;
        downArrow.enabled = index < Levels.Length - 1;

        GameFlow.Level selected = Levels[index];
        detailTitle.text = selected.displayName;
        if (difficulty.HasValue)
        {
            int high = ScoreKeeper.GetHighScore(selected, (Difficulty)difficulty.Value);
            detailBest.text = high >= 0 ? $"HIGH SCORE  {ScoreKeeper.FormatPoints(high)}" : "NO HIGH SCORE YET";
        }
        else detailBest.text = "PICK A DIFFICULTY";

        for (int i = 0; i < 3; i++)
            difficultyButtons[i].SetStyle(difficulty == i ? UITheme.ButtonStyle.Primary : UITheme.ButtonStyle.Secondary);
        start.Button.interactable = difficulty.HasValue;
    }

    static RectTransform Art(Transform parent, string art, float width, float height = 0f)
    {
        var image = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.sprite = UITheme.Art(art);
        image.preserveAspect = true;
        image.raycastTarget = false;
        float aspect = image.sprite != null ? image.sprite.rect.width / image.sprite.rect.height : 1f;
        image.rectTransform.sizeDelta = height > 0f ? new Vector2(height * aspect, height) : new Vector2(width, width / aspect);
        return image.rectTransform;
    }
}
