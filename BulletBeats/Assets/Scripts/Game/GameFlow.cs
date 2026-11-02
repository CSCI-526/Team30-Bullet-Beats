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
/// draws the screens. A tutorial run (<see cref="StartTutorial"/>, and the first PLAY ever) plays
/// <see cref="Tutorial"/> instead of the song, then goes into the first level on Easy.
/// </summary>
public class GameFlow : MonoBehaviour
{
    [Serializable]
    public class Level
    {
        [Tooltip("Stable id for high scores. Empty uses the scene name.")]
        public string id;
        public string displayName;
        [Tooltip("Scene to load. It must be in File > Build Profiles > Scene List.")]
        public string sceneName;
        [Tooltip("Song for this level. Empty keeps the clip assigned on Rhythm Session.")]
        public AudioClip music;
        [Tooltip("MIDI chart to spawn from. Empty builds the chart from the song instead.")]
        public string midiFileName;
        [Tooltip("Quarter-note tempo of this song. 0 estimates it from the audio.")]
        public float bpm;
        [Tooltip("Seconds from the start of the file to beat 1. 0 if the downbeat is at the start.")]
        public float firstBeatOffset;
    }

    public static GameFlow Instance { get; private set; }

    public Level[] levels = { new Level { displayName = "Tetris", sceneName = "MainLevel" } };
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
    public int FineCount { get; private set; }
    public PlayerHealth PlayerHealth => health;
    /// <summary>Id of a song the player picked from disk. One high-score slot for whatever they loaded last.</summary>
    public const string CustomLevelId = "custom";

    /// <summary>Survives scene reloads. Several levels share one scene, so the scene name is not enough.</summary>
    static string selectedLevelId;
    static Level customLevel;

    /// <summary>Absolute path of the song chosen on the level select screen. The clip itself may not survive a reload.</summary>
    public static string CustomSongPath { get; private set; }
    public static Level CustomLevel => customLevel;

    public static void RememberCustomSong(string path, AudioClip clip, string displayName)
    {
        CustomSongPath = path;
        customLevel = new Level
        {
            id = CustomLevelId,
            displayName = displayName,
            sceneName = "MainLevel",
            music = clip
        };
    }

    public Level CurrentLevel
    {
        get
        {
            if (customLevel != null && selectedLevelId == customLevel.id)
                return customLevel;
            if (levels == null || levels.Length == 0) return null;
            if (!string.IsNullOrEmpty(selectedLevelId))
            {
                Level picked = Array.Find(levels, level => level.id == selectedLevelId);
                if (picked != null) return picked;
            }
            return Array.Find(levels, level => level.sceneName == SceneManager.GetActiveScene().name);
        }
    }
    /// <summary>True while this run is the tutorial rather than a level.</summary>
    public bool IsTutorial { get; private set; }

    /// <summary>True once the player has finished or skipped the tutorial (saved in PlayerPrefs).</summary>
    public static bool TutorialDone
    {
        get => PlayerPrefs.GetInt(TutorialDoneKey, 0) == 1;
        set { PlayerPrefs.SetInt(TutorialDoneKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    const string TutorialDoneKey = "BulletBeats.TutorialDone";

    /// <summary>Screen the next loaded scene opens on; Playing skips the menus.</summary>
    static GameState stateAfterLoad = GameState.Title;
    /// <summary>Set before a scene load so the next Playing state is a tutorial run.</summary>
    static bool tutorialAfterLoad;

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

        LevelCatalog catalog = Resources.Load<LevelCatalog>("LevelCatalog");
        if (catalog != null && catalog.levels != null && catalog.levels.Length > 0)
            levels = catalog.levels;

        // The count-in runs on the audio clock, which pausing doesn't stop, so hold the session
        // back until play starts. Playback itself begins in BeginPlay.
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
        bool tutorial = tutorialAfterLoad;
        tutorialAfterLoad = false;
        if (first == GameState.Playing) BeginPlay(tutorial);
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
        if (IsTutorial) return; // the tutorial decides when it's over

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

    /// <summary>
    /// With more than one song, opens level select. The very first time, plays the tutorial
    /// instead (it leads into the first song afterwards).
    /// </summary>
    public void Play()
    {
        if (!TutorialDone)
        {
            StartTutorial();
            return;
        }

        if (levels != null && levels.Length > 1)
        {
            ShowLevelSelect();
            return;
        }

        Level level = CurrentLevel;
        if (level != null) StartLevel(level);
        else if (levels.Length > 0) StartLevel(levels[0]);
    }

    public void StartLevel(Level level)
    {
        if (level == null) return;
        if (!string.IsNullOrEmpty(level.id)) selectedLevelId = level.id;
        bool sameScene = level.sceneName == SceneManager.GetActiveScene().name;
        if (sameScene && !started) BeginPlay();
        else LoadScene(level.sceneName, GameState.Playing);
    }

    public void Retry() => LoadScene(SceneManager.GetActiveScene().name, GameState.Playing);

    /// <summary>Plays the tutorial in the first level's scene.</summary>
    public void StartTutorial()
    {
        if (levels.Length == 0) return;
        bool sameScene = levels[0].sceneName == SceneManager.GetActiveScene().name;
        if (sameScene && !started) BeginPlay(true);
        else
        {
            tutorialAfterLoad = true;
            LoadScene(levels[0].sceneName, GameState.Playing);
        }
    }

    /// <summary>Called by <see cref="Tutorial"/> when it ends: remembers it, then starts the first level on Easy.</summary>
    public void FinishTutorial()
    {
        TutorialDone = true;
        ScoreKeeper.SelectedDifficulty = Difficulty.Easy;
        if (levels.Length > 0) StartLevel(levels[0]);
    }

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

    void BeginPlay(bool tutorial = false)
    {
        started = true;
        IsTutorial = tutorial;
        TimePlayed = 0f;
        PerfectCount = GoodCount = MissCount = FineCount = 0;
        // Difficulty sets the spawn stride from this state change, before the chart is built.
        SetState(GameState.Playing);
        // The tutorial runs on the scene clock with its own asteroids, so the song never starts.
        if (session != null && !tutorial)
        {
            session.enabled = true;
            session.Begin(CurrentLevel);
        }
        if (tutorial && GetComponent<Tutorial>() == null) gameObject.AddComponent<Tutorial>();
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
            case BeatJudgment.Fine: FineCount++; break;
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
