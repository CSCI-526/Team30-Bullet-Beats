using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Settings: three ON/OFF toggles on one page, no sub-menus and no sliders.
/// SOUND mutes everything (<see cref="CornerMute"/>), AIM ASSIST turns the shot lock-on fully on
/// or off (<see cref="PlayerShooter.SetAimAssist"/>), HOLD TO FIRE keeps firing while the mouse
/// is held (<see cref="PlayerShooter.SetHoldToFire"/>). BACK or Esc returns to whatever opened it
/// (the main menu now, the pause menu later), through the callback given to <see cref="Open(Action)"/>.
/// </summary>
public class SettingsScreen : UIScreen
{
    class Row
    {
        public PixelButton toggle;
        public Func<bool> get;
        public Action<bool> set;
    }

    readonly Row[] rows = new Row[3];
    MenuBackground background;
    PixelButton back;
    Action onBack;

    protected override int SortingOrder => 220;
    protected override Selectable FirstSelected => rows[0]?.toggle.Button;

    protected override void Build()
    {
        background = MenuBackground.Create(Root, UITheme.BusyScreenDim);

        back = CornerButton("BACK", PixelArt.Icon.ArrowLeft, false, Back);

        TextMeshProUGUI title = UITheme.Text(Content, "SETTINGS", 84f, UITheme.White);
        title.rectTransform.sizeDelta = new Vector2(900f, 110f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 300f);

        RectTransform panel = PixelPanel.Create(Content, new Vector2(980f, 470f), new Vector2(0f, -40f), "SettingsPanel");

        rows[0] = AddRow(panel, "SOUND", 130f, () => !CornerMute.Muted, on => CornerMute.SetMuted(!on));
        rows[1] = AddRow(panel, "AIM ASSIST", 0f, AimAssistOn, SetAimAssist);
        rows[2] = AddRow(panel, "HOLD TO FIRE", -130f, () => PlayerShooter.HoldToFireEnabled, SetHoldToFire);

        TextMeshProUGUI hint = UITheme.Text(Content, "ARROWS  MOVE      ENTER  TOGGLE      ESC  BACK", 24f, UITheme.MutedGrey, display: false);
        hint.rectTransform.sizeDelta = new Vector2(1200f, 40f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, -340f);
    }

    /// <summary>Opens settings. <paramref name="onBack"/> runs after BACK/Esc closes it.</summary>
    public void Open(Action onBack)
    {
        this.onBack = onBack;
        foreach (Row row in rows) Paint(row);
        Open();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsOpen) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Back();
    }

    void Back()
    {
        Close();
        Action callback = onBack;
        onBack = null;
        callback?.Invoke();
    }

    Row AddRow(RectTransform panel, string label, float y, Func<bool> get, Action<bool> set)
    {
        TextMeshProUGUI text = UITheme.Text(panel, label, 40f, UITheme.White, align: TextAlignmentOptions.Left);
        text.rectTransform.sizeDelta = new Vector2(560f, 80f);
        text.rectTransform.anchoredPosition = new Vector2(-150f, y);

        var row = new Row { get = get, set = set };
        row.toggle = PixelButton.Create(panel, "ON", () =>
        {
            row.set(!row.get());
            Paint(row);
        }, UITheme.ButtonStyle.Primary, 210f, 84f, y, 36f);
        ((RectTransform)row.toggle.transform).anchoredPosition = new Vector2(300f, y);
        return row;
    }

    static void Paint(Row row)
    {
        bool on = row.get();
        row.toggle.Label.text = on ? "ON" : "OFF";
        row.toggle.Label.color = on ? UITheme.White : new Color(1f, 1f, 1f, 0.7f);
        row.toggle.SetStyle(on ? UITheme.ButtonStyle.Primary : UITheme.ButtonStyle.Secondary);
    }

    static bool AimAssistOn()
    {
        if (PlayerShooter.SavedAimAssist >= 0f) return PlayerShooter.SavedAimAssist > 0f;
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        return shooter == null || shooter.aimAssist > 0f;
    }

    static void SetAimAssist(bool on)
    {
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        if (shooter != null) shooter.SetAimAssist(on ? 1f : 0f);
        else PlayerShooter.SavedAimAssist = on ? 1f : 0f;
    }

    static void SetHoldToFire(bool on)
    {
        PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
        if (shooter != null) shooter.SetHoldToFire(on);
        else PlayerShooter.HoldToFireEnabled = on;
    }
}
