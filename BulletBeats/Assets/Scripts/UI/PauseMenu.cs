using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Esc (or P) pauses a level and opens this card over the blurred, dimmed game: RESUME / RESTART /
/// SETTINGS / EXIT TO MENU and a short controls line. Esc again resumes (or, while Settings is open,
/// goes back to this card). <see cref="GameFlow.SetPaused"/> decides when pausing is allowed
/// (not in the tutorial, where Esc skips).
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class PauseMenu : CardScreen
{
    const string Controls = "W A S D  MOVE      CLICK  SHOOT ON THE BEAT      MOVE ONTO A STAR  COLLECT IT";

    GameFlow flow;
    PixelButton resume;

    protected override int SortingOrder => 250;
    protected override Selectable FirstSelected => resume != null ? resume.Button : null;

    protected override void Awake()
    {
        flow = GetComponent<GameFlow>();
        base.Awake();
    }

    void OnEnable() => flow.PausedChanged += Show;
    void OnDisable() => flow.PausedChanged -= Show;

    protected override void Build()
    {
        RectTransform card = PixelPanel.Create(Content, new Vector2(760f, 700f), Vector2.zero, "PauseCard");

        TextMeshProUGUI title = UITheme.Text(card, "PAUSED", 80f, UITheme.White);
        title.rectTransform.sizeDelta = new Vector2(700f, 100f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 255f);

        float y = 120f;
        resume = PixelButton.Create(card, "RESUME", () => flow.SetPaused(false), UITheme.ButtonStyle.Primary, 520f, 90f, y, 40f);
        PixelButton.Create(card, "RESTART", () => flow.Retry(), UITheme.ButtonStyle.Secondary, 520f, 90f, y - 100f, 36f);
        PixelButton.Create(card, "SETTINGS", OpenSettings, UITheme.ButtonStyle.Secondary, 520f, 90f, y - 200f, 36f);
        PixelButton.Create(card, "EXIT TO MENU", () => flow.ShowTitle(), UITheme.ButtonStyle.Secondary, 520f, 90f, y - 300f, 36f);

        TextMeshProUGUI controls = UITheme.Text(Content, Controls, 22f, UITheme.MutedGrey, display: false);
        controls.rectTransform.sizeDelta = new Vector2(1600f, 40f);
        controls.rectTransform.anchoredPosition = new Vector2(0f, -400f);
    }

    protected override void Update()
    {
        base.Update();
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard.escapeKey.wasPressedThisFrame && !keyboard.pKey.wasPressedThisFrame) return;

        // Esc in Settings only closes Settings (back to this card).
        SettingsScreen settings = GetComponent<SettingsScreen>();
        if (settings != null && (settings.IsOpen || settings.ClosedFrame == Time.frameCount)) return;
        flow.SetPaused(!flow.IsPaused);
    }

    void OpenSettings()
    {
        SettingsScreen settings = GetComponent<SettingsScreen>();
        if (settings != null) settings.Open(onBack: null);
    }

    void Show(bool paused)
    {
        if (paused) Open();
        else
        {
            GetComponent<SettingsScreen>()?.Close();
            Close();
        }
    }
}
