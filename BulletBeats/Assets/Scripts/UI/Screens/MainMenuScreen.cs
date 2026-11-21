using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main menu, following the approved reference: galaxy backdrop, the Bullet Beats logo, a
/// text-only button stack (PLAY, SONG SELECT, TUTORIAL) and the ship at the bottom. Settings
/// is only the gear button in the top-right corner. There is no Quit, and Esc does nothing here.
/// PLAY goes to the tutorial the first time and to song select after that (<see cref="GameFlow.Play"/>).
/// </summary>
public class MainMenuScreen : UIScreen
{
    [Tooltip("Logo bob height and speed, in canvas units and cycles per second.")]
    public float logoBob = 6f, logoBobSpeed = 0.35f;
    [Tooltip("Ship bob height and speed.")]
    public float shipBob = 10f, shipBobSpeed = 0.6f;

    GameFlow flow;
    PixelButton play;
    RectTransform logo, ship;
    Vector2 logoHome, shipHome;

    protected override int SortingOrder => 210;
    protected override Selectable FirstSelected => play != null ? play.Button : null;

    protected override void Awake()
    {
        flow = GetComponent<GameFlow>();
        base.Awake();
    }

    protected override void Build()
    {
        MenuBackground.Create(Root);

        logo = ArtImage(UITheme.Logo, 860f);
        logoHome = logo.anchoredPosition = new Vector2(0f, 290f);

        const float buttonWidth = 560f, buttonHeight = 96f;
        play = PixelButton.Create(Content, "PLAY", () => flow.Play(), UITheme.ButtonStyle.Primary, buttonWidth, buttonHeight, 10f, 46f);
        PixelButton.Create(Content, "SONG SELECT", () => flow.ShowLevelSelect(), UITheme.ButtonStyle.Primary, buttonWidth, buttonHeight, -100f, 40f);
        PixelButton.Create(Content, "TUTORIAL", () => flow.StartTutorial(), UITheme.ButtonStyle.Primary, buttonWidth, buttonHeight, -210f, 40f);

        ship = ArtImage(UITheme.MenuShip, 300f);
        ship.anchorMin = ship.anchorMax = new Vector2(0.5f, 0f);
        shipHome = ship.anchoredPosition = new Vector2(0f, 120f);

        CornerButton("SETTINGS", PixelArt.Icon.Gear, true, OpenSettings);
    }

    protected override void Update()
    {
        base.Update();
        if (!IsOpen) return;
        float t = Time.unscaledTime;
        logo.anchoredPosition = logoHome + Vector2.up * (logoBob * Mathf.Sin(t * logoBobSpeed * Mathf.PI * 2f));
        ship.anchoredPosition = shipHome + Vector2.up * (shipBob * Mathf.Sin(t * shipBobSpeed * Mathf.PI * 2f));
    }

    void OpenSettings()
    {
        SettingsScreen settings = GetComponent<SettingsScreen>();
        if (settings == null) return;
        settings.Open(onBack: Focus);
    }

    RectTransform ArtImage(string art, float width)
    {
        var image = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(Content, false);
        image.sprite = UITheme.Art(art);
        image.preserveAspect = true;
        image.raycastTarget = false;
        float aspect = image.sprite != null ? image.sprite.rect.height / image.sprite.rect.width : 0.5f;
        image.rectTransform.sizeDelta = new Vector2(width, width * aspect);
        return image.rectTransform;
    }
}
