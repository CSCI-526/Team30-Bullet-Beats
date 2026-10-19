using UnityEngine;

/// <summary>
/// Sends gameplay events to <see cref="Analytics"/> for the four GDD metrics:
/// every shot's judgment (shot accuracy), every dodge (dodging), the ship dying (death spots)
/// and each level's result (win / loss). Listens to the player and <see cref="GameFlow"/>
/// events, so none of those scripts need to know about analytics.
/// </summary>
public class AnalyticsHooks : MonoBehaviour
{
    PlayerShooter shooter;
    PlayerMovement movement;
    PlayerHealth health;
    GameFlow flow;
    PlayerMovement.Spot lastSpot;
    bool deathLogged;

    void Start()
    {
        shooter = FindFirstObjectByType<PlayerShooter>();
        movement = FindFirstObjectByType<PlayerMovement>();
        health = FindFirstObjectByType<PlayerHealth>();
        flow = GameFlow.Instance;

        if (shooter != null) shooter.ShotFired += OnShotFired;
        if (movement != null)
        {
            lastSpot = movement.Current;
            movement.Dashed += OnDashed;
        }
        if (health != null) health.Died += OnDied;
        if (flow != null) flow.StateChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (movement != null) movement.Dashed -= OnDashed;
        if (health != null) health.Died -= OnDied;
        if (flow != null) flow.StateChanged -= OnStateChanged;
    }

    void OnShotFired(BeatJudgment judgment) => Analytics.Instance?.LogShot(judgment.ToString());

    void OnDashed(PlayerMovement.Spot to)
    {
        Analytics.Instance?.LogDodge(lastSpot.ToString(), to.ToString());
        lastSpot = to;
    }

    void OnDied() => LogDeathOnce();

    void OnStateChanged(GameState state)
    {
        if (state != GameState.Ended) return;
        // GameFlow can hear Died before this script does; log the death before the loss either way.
        if (!flow.Won && health != null && health.IsDead) LogDeathOnce();
        Analytics.Instance?.LogLevelEnd(flow.Won);
    }

    void LogDeathOnce()
    {
        if (deathLogged) return;
        deathLogged = true;
        Analytics.Instance?.LogDeath("asteroid");
    }
}
