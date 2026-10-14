using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    Title,
    LevelSelect,
    Playing,
    Ended
}

/// <summary>
/// Runs a level from the title screen to the end screen. Outside of <see cref="GameState.Playing"/>
/// the game is frozen (time scale 0, audio paused) and the player's controls are off, and the
/// <see cref="RhythmSession"/> (count-in, song, chart) only starts when play starts.
/// The level ends in a loss when the ship's health runs out, or in a win when the song ends
/// (or after <see cref="levelLengthSeconds"/> while there is no song). <see cref="GameMenus"/>
/// draws the screens.
/// </summary>
public class GameFlow : MonoBehaviour
{
    [Serializable]
    public class Level
    {
        public string displayName;
        [Tooltip("Scene to load. It must be in File > Build Profiles > Scene List.")]
        public string sceneName;
    }

    public static GameFlow Instance { get; private set; }

    public Level[] levels = { new Level { displayName = "Main Level", sceneName = "MainLevel" } };
    [Tooltip("Level length in seconds while BeatClock has no song. 0 = never ends by time.")]
    public float levelLengthSeconds = 90f;
    [Tooltip("If off, running out of health does not end the level (for testing).")]
    //public bool endOnDeath = false;
    public bool endOnDeath = true;
    [Tooltip("Turned off outside of play. Empty = the player's shooter, dash, aim HUD and health.")]
    public Behaviour[] playerControls;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event Action<GameState> StateChanged;

    public GameState State { get; private set; } = GameState.Title;
    /// <summary>True if the last level ended with the song finishing, false if the ship died.</summary>
    public bool Won { get; private set; }
    /// <summary>Seconds played in the current level.</summary>
    public float TimePlayed { get; private set; }
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int MissCount { get; private set; }
    public PlayerHealth PlayerHealth => health;
    public Level CurrentLevel => Array.Find(levels, l => l.sceneName == SceneManager.GetActiveScene().name);

    /// <summary>Screen the next loaded scene opens on; Playing skips the menus.</summary>
    static GameState stateAfterLoad = GameState.Title;

    PlayerHealth health;
    PlayerShooter shooter;
    RhythmSession session;
    bool started;
    bool musicStarted;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("More than one GameFlow in the scene; using the newest one.", this);
        Instance = this;

        // The count-in runs on the audio clock, which pausing doesn't stop, so hold the session
        // back until play starts. This runs before the session's Start.
        session = FindFirstObjectByType<RhythmSession>();
        if (session != null) session.enabled = false;
    }

    void Start()
    {
        health = FindFirstObjectByType<PlayerHealth>();
        shooter = FindFirstObjectByType<PlayerShooter>();
        if (health != null) health.Died += OnPlayerDied;
        if (shooter != null) shooter.ShotFired += OnShotFired;

        if (playerControls == null || playerControls.Length == 0)
            playerControls = new Behaviour[]
            {
                shooter,
                FindFirstObjectByType<PlayerMovement>(),
                FindFirstObjectByType<AimHUD>(),
                health
            };

        GameState first = stateAfterLoad;
        stateAfterLoad = GameState.Title;
        if (first == GameState.Playing) BeginPlay();
        else SetState(first);
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnPlayerDied;
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (Instance == this)
        {
            Instance = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    void Update()
    {
        if (State != GameState.Playing) return;

        TimePlayed += Time.deltaTime;

        BeatClock clock = BeatClock.Instance;
        if (clock != null && clock.HasMusic)
        {
            // Win once the song has started and then finished.
            if (clock.IsMusicPlaying) musicStarted = true;
            else if (musicStarted) EndLevel(true);
        }
        else if (levelLengthSeconds > 0f && TimePlayed >= levelLengthSeconds)
        {
            EndLevel(true);
        }
    }

    public void ShowTitle()
    {
        // Back from an ended level: reload so the level starts fresh next time.
        if (started) LoadScene(SceneManager.GetActiveScene().name, GameState.Title);
        else SetState(GameState.Title);
    }

    public void ShowLevelSelect()
    {
        if (started) LoadScene(SceneManager.GetActiveScene().name, GameState.LevelSelect);
        else SetState(GameState.LevelSelect);
    }

    /// <summary>Plays the current scene's level, or the first level when this scene isn't one.</summary>
    public void Play()
    {
        Level level = CurrentLevel;
        if (level != null) StartLevel(level);
        else if (levels.Length > 0) StartLevel(levels[0]);
    }

    public void StartLevel(Level level)
    {
        bool sameScene = level.sceneName == SceneManager.GetActiveScene().name;
        if (sameScene && !started) BeginPlay();
        else LoadScene(level.sceneName, GameState.Playing);
    }

    public void Retry() => LoadScene(SceneManager.GetActiveScene().name, GameState.Playing);

    /// <summary>Ends the level now; for anything else that can finish a level (e.g. reaching the end of the track).</summary>
    public void EndLevel(bool won)
    {
        if (State != GameState.Playing) return;
        Won = won;
        SetState(GameState.Ended);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public static bool CanLoad(Level level) =>
        !string.IsNullOrEmpty(level.sceneName) && Application.CanStreamedLevelBeLoaded(level.sceneName);

    void BeginPlay()
    {
        started = true;
        TimePlayed = 0f;
        PerfectCount = GoodCount = MissCount = 0;
        if (session != null) session.enabled = true;
        SetState(GameState.Playing);
    }

    void OnPlayerDied()
    {
        if (endOnDeath) EndLevel(false);
    }

    void OnShotFired(BeatJudgment judgment)
    {
        if (State != GameState.Playing) return;
        switch (judgment)
        {
            case BeatJudgment.Perfect: PerfectCount++; break;
            case BeatJudgment.Good: GoodCount++; break;
            default: MissCount++; break;
        }
    }

    void LoadScene(string sceneName, GameState openOn)
    {
        stateAfterLoad = openOn;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(sceneName);
    }

    void SetState(GameState state)
    {
        State = state;
        bool playing = state == GameState.Playing;
        Time.timeScale = playing ? 1f : 0f;
        AudioListener.pause = !playing;

        StopAllCoroutines();
        if (playing) StartCoroutine(EnableControlsNextFrame());
        else SetControlsEnabled(false);

        StateChanged?.Invoke(state);
    }

    // Wait a frame so the click on a menu button doesn't also fire a shot.
    IEnumerator EnableControlsNextFrame()
    {
        SetControlsEnabled(false);
        yield return null;
        SetControlsEnabled(true);
    }

    void SetControlsEnabled(bool enabled)
    {
        foreach (Behaviour control in playerControls)
            if (control != null) control.enabled = enabled;
    }
}
