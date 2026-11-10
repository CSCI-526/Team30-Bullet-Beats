using UnityEngine;

/// <summary>
/// Sends gameplay events to <see cref="Analytics"/> for the four Beta graphs: Ship Position Heat Map
/// (dodges), Asteroid Outcome Timeline and Timing Profile (one row per asteroid), and Power-up Value
/// (one row per star). Listens to the player, asteroid, pickup and <see cref="GameFlow"/> events, so
/// none of those scripts need to know about analytics. Nothing is logged during the tutorial, so it
/// doesn't mix with real level data (the tutorial logs its own result).
/// </summary>
public class AnalyticsHooks : MonoBehaviour
{
    PlayerShooter shooter;
    PlayerMovement movement;
    GameFlow flow;
    PlayerMovement.Spot lastSpot;
    bool runStarted;
    int emptyClicks;

    void Start()
    {
        shooter = FindFirstObjectByType<PlayerShooter>();
        movement = FindFirstObjectByType<PlayerMovement>();
        flow = GameFlow.Instance;

        if (shooter != null) shooter.ShotFired += OnShotFired;
        if (movement != null)
        {
            lastSpot = movement.Current;
            movement.Dashed += OnDashed;
        }
        ChartTarget.Ended += OnAsteroidEnded;
        PowerUpPickup.Ended += OnStarEnded;
        if (flow != null)
        {
            flow.StateChanged += OnStateChanged;
            // The level may have started before this script did.
            if (flow.State == GameState.Playing) BeginRun();
        }
    }

    void OnDestroy()
    {
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (movement != null) movement.Dashed -= OnDashed;
        ChartTarget.Ended -= OnAsteroidEnded;
        PowerUpPickup.Ended -= OnStarEnded;
        if (flow != null) flow.StateChanged -= OnStateChanged;
    }

    bool InTutorial => flow != null && flow.IsTutorial;
    bool InRun => runStarted && !InTutorial && flow != null && flow.State == GameState.Playing;

    void BeginRun()
    {
        if (runStarted || InTutorial) return;
        runStarted = true;
        GameFlow.Level level = flow.CurrentLevel;
        string levelId = level == null ? "unknown" : !string.IsNullOrEmpty(level.id) ? level.id : level.displayName;
        Analytics.Instance?.LogLevelStart(levelId, ScoreKeeper.SelectedDifficulty.ToString(),
            shooter != null && shooter.holdToFire, shooter != null ? shooter.aimAssist : 0f, GameFlow.TutorialDone);
    }

    void OnShotFired(BeatJudgment judgment)
    {
        if (InRun && shooter.LastShotTarget == null) emptyClicks++;
    }

    void OnDashed(PlayerMovement.Spot to)
    {
        if (InRun) Analytics.Instance?.LogDodge(lastSpot.ToString(), to.ToString());
        lastSpot = to;
    }

    void OnAsteroidEnded(ChartTarget asteroid, ChartTarget.Outcome outcome)
    {
        if (!InRun) return;
        Analytics.Instance?.LogAsteroid(asteroid.hitTime, asteroid.TypeName, outcome.ToString().ToLowerInvariant(),
            lastSpot.ToString(), asteroid.FirstShotOffset, asteroid.Tries, asteroid.FirstShotInSlowMo);
    }

    void OnStarEnded(PowerUpPickup star, bool collected)
    {
        if (InRun) Analytics.Instance?.LogStar(star.kind.ToString(), star.spot.ToString(), collected);
    }

    void OnStateChanged(GameState state)
    {
        if (state == GameState.Playing) BeginRun();
        if (state != GameState.Ended || InTutorial || !runStarted) return;
        Analytics.Instance?.LogLevelEnd(flow.Won, emptyClicks);
    }
}
