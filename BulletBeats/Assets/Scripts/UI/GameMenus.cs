using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Opens the right screen for each <see cref="GameFlow"/> state: <see cref="MainMenuScreen"/> on the
/// title, <see cref="SongSelectScreen"/> for level select and <see cref="ResultCard"/> at the end.
/// Also adds <see cref="SettingsScreen"/> and an EventSystem if the scene has none, so it needs no
/// scene setup.
/// </summary>
public class GameMenus : MonoBehaviour
{
    GameFlow flow;
    MainMenuScreen mainMenu;
    SettingsScreen settings;
    SongSelectScreen songSelect;
    ResultCard resultCard;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        EnsureEventSystem();
        mainMenu = gameObject.AddComponent<MainMenuScreen>();
        settings = gameObject.AddComponent<SettingsScreen>();
        songSelect = gameObject.AddComponent<SongSelectScreen>();
        resultCard = gameObject.AddComponent<ResultCard>();
        Show(flow.State);
    }

    void OnEnable() => flow.StateChanged += Show;
    void OnDisable() => flow.StateChanged -= Show;

    void Show(GameState state)
    {
        if (state == GameState.Title) mainMenu.Open(); else mainMenu.Close();
        if (state == GameState.LevelSelect) songSelect.Open(); else songSelect.Close();
        if (state == GameState.Ended) resultCard.Open(); else resultCard.Close();
        if (state == GameState.Playing) settings.Close();
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
        // The project uses the Input System only, so the UI needs its input module.
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
