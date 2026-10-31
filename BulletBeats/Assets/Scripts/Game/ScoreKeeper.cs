using System.Globalization;
using UnityEngine;

public enum Difficulty
{
    Easy,
    Medium,
    Difficult
}

/// <summary>
/// Scores the current run Guitar Hero style and keeps a high score per level and difficulty.
/// Each Perfect / Good / Fine shot scores its points times the streak multiplier; a Miss or a hit
/// on the ship resets the streak. Runs that end in death count too. High scores live in
/// PlayerPrefs, so they survive reloads (and on WebGL stay in the player's browser).
/// Also applies the chosen <see cref="Difficulty"/> to the asteroid spawn rate when play starts.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class ScoreKeeper : MonoBehaviour
{
    /// <summary>Kept across scene loads, so Play and Retry use the last choice.</summary>
    public static Difficulty SelectedDifficulty = Difficulty.Medium;

    [Header("Points per shot")]
    public int perfectPoints = 100;
    public int goodPoints = 50;
    public int finePoints = 10;

    [Header("Streak multiplier")]
    [Tooltip("Streak needed for each step of the multiplier: x2 at this streak, x3 at twice it, ...")]
    [Min(1)] public int streakPerStep = 10;
    [Min(1)] public int maxMultiplier = 4;

    [Header("Difficulty: one asteroid every N chart notes")]
    [Min(1)] public int easyNoteStride = 7;
    [Min(1)] public int mediumNoteStride = 5;
    [Min(1)] public int difficultNoteStride = 3;

    public int Score { get; private set; }
    /// <summary>Perfect and Good shots in a row; a Miss or getting hit resets it, Fine leaves it.</summary>
    public int Streak { get; private set; }
    public int Multiplier => 1 + Mathf.Min(maxMultiplier - 1, Streak / streakPerStep);
    /// <summary>High score before this run; -1 if this level and difficulty were never played.</summary>
    public int PreviousHighScore { get; private set; } = -1;
    /// <summary>True if this run beat an earlier score (never on the first run).</summary>
    public bool IsNewHighScore { get; private set; }

    GameFlow flow;
    PlayerShooter shooter;
    PlayerHealth health;
    bool recorded = true;

    void Awake()
    {
        flow = GetComponent<GameFlow>();
        shooter = FindFirstObjectByType<PlayerShooter>();
        health = FindFirstObjectByType<PlayerHealth>();
    }

    void OnEnable()
    {
        flow.StateChanged += OnStateChanged;
        if (shooter != null) shooter.ShotFired += OnShotFired;
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        flow.StateChanged -= OnStateChanged;
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (health != null) health.Damaged -= OnDamaged;
    }

    public int NoteStrideFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => easyNoteStride,
        Difficulty.Difficult => difficultNoteStride,
        _ => mediumNoteStride
    };

    /// <summary>Saves the score of the run that just ended. Safe to call more than once.</summary>
    public void RecordResult()
    {
        if (recorded) return;
        recorded = true;

        GameFlow.Level level = flow.CurrentLevel;
        if (level == null) return;
        string key = Key(level, SelectedDifficulty);
        PreviousHighScore = PlayerPrefs.GetInt(key, -1);
        IsNewHighScore = PreviousHighScore >= 0 && Score > PreviousHighScore;
        if (Score > PreviousHighScore)
        {
            PlayerPrefs.SetInt(key, Score);
            PlayerPrefs.Save();
        }
    }

    /// <returns>The saved high score, or -1 if there is none yet.</returns>
    public static int GetHighScore(GameFlow.Level level, Difficulty difficulty) =>
        PlayerPrefs.GetInt(Key(level, difficulty), -1);

    /// <summary>Best high score over all difficulties of <paramref name="level"/>.</summary>
    public static bool TryGetBestHighScore(GameFlow.Level level, out int score, out Difficulty difficulty)
    {
        score = -1;
        difficulty = Difficulty.Medium;
        foreach (Difficulty d in System.Enum.GetValues(typeof(Difficulty)))
        {
            int s = GetHighScore(level, d);
            if (s <= score) continue;
            score = s;
            difficulty = d;
        }
        return score >= 0;
    }

    public static string FormatPoints(int points) => points.ToString("N0", CultureInfo.InvariantCulture);

    public static string DifficultyName(Difficulty difficulty) => difficulty.ToString().ToUpperInvariant();

    static string Key(GameFlow.Level level, Difficulty difficulty)
    {
        string id = string.IsNullOrEmpty(level.id) ? level.sceneName : level.id;
        return $"BulletBeats.HighScore.{id}.{difficulty}";
    }

    void OnStateChanged(GameState state)
    {
        if (state == GameState.Playing) BeginRun();
        else if (state == GameState.Ended) RecordResult();
    }

    void BeginRun()
    {
        Score = 0;
        Streak = 0;
        PreviousHighScore = -1;
        IsNewHighScore = false;
        recorded = false;

        // The session loads the chart (and applies the stride) when it starts, right after this.
        ChartTargetSpawner spawner = ChartTargetSpawner.Instance != null
            ? ChartTargetSpawner.Instance
            : FindFirstObjectByType<ChartTargetSpawner>();
        if (spawner != null) spawner.noteStride = NoteStrideFor(SelectedDifficulty);
    }

    void OnShotFired(BeatJudgment judgment)
    {
        if (flow.State != GameState.Playing) return;

        int points = judgment switch
        {
            BeatJudgment.Perfect => perfectPoints,
            BeatJudgment.Good => goodPoints,
            BeatJudgment.Fine => finePoints,
            _ => 0
        };
        Score += points * Multiplier;

        if (judgment == BeatJudgment.Perfect || judgment == BeatJudgment.Good) Streak++;
        else if (judgment == BeatJudgment.Miss) Streak = 0;
    }

    void OnDamaged(int amount) => Streak = 0;
}
