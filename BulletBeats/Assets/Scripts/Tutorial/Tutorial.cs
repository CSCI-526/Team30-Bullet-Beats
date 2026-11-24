using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 0: a short guided run (about 30-45 seconds) that plays like the real game. Each step shows its
/// cue and the player can act right away; the step moves on by itself once the player has done it, and
/// stays (trying again) until then. There are no Continue buttons: a quick NICE! shows between steps.
/// Steps, each with a big title and one line under it: shoot (<see cref="shotsNeeded"/> good shots; SHOOT
/// NOW shows when the brackets turn gold, which is PERFECT), move (W A S D, acted out by a see-through
/// ghost ship while the player can already move), dodge one big red asteroid (the ghost shows the way the
/// first time), then collect the Shield, Freeze and Bomb stars. Then straight into the first level on
/// Easy, which shows its own intro card and count-in. The ship can't die here.
/// The lives and time tour (<see cref="hudStep"/>) is off until the score HUD and the song progress bar
/// are in (#157).
/// Only players who finished the tutorial before can skip it (Skip button or ESC), and only in the first
/// <see cref="skipSeconds"/> seconds; after that, and for everyone else, ESC opens the pause menu.
/// Added by <see cref="GameFlow"/> for a tutorial run. It runs on the scene clock (no song), so
/// slowing <see cref="Time.timeScale"/> slows the asteroids, brackets and timing together.
/// </summary>
public class Tutorial : MonoBehaviour
{
    [Header("Pacing")]
    [Tooltip("Beat length while in the tutorial; also how long each cue shows.")]
    public float tutorialBpm = 60f;
    [Tooltip("Time scale while an instruction is waiting for the player.")]
    [Range(0.02f, 1f)] public float slowScale = 0.1f;
    [Tooltip("Real seconds to slow down or speed back up.")]
    public float rampSeconds = 0.35f;
    [Tooltip("Time scale while an asteroid's brackets close in, before the timing window.")]
    [Range(0.05f, 1f)] public float approachScale = 0.6f;
    [Tooltip("Time scale during the timing window, so there is time to react to SHOOT NOW.")]
    [Range(0.05f, 1f)] public float windowScale = 0.3f;
    [Tooltip("Shots after the first good one: time scale while the brackets close in, and during the window.")]
    [Range(0.05f, 1f)] public float quickApproachScale = 1f;
    [Range(0.05f, 1f)] public float quickWindowScale = 0.6f;
    [Tooltip("Beats before a timed asteroid reaches its beat.")]
    [Min(1f)] public float shotLeadBeats = 1.15f;
    [Tooltip("Good shots (GOOD or PERFECT) that pass the shoot step. A miss doesn't reset the count.")]
    [Min(1)] public int shotsNeeded = 3;
    [Tooltip("The move step passes once the player has moved and this many real seconds have gone by, so the ghost is seen.")]
    public float moveMinSeconds = 1.5f;
    [Tooltip("The move step passes after this many real seconds even without a move (the dodge step needs one anyway).")]
    public float moveMaxSeconds = 5f;
    [Tooltip("Real seconds the NICE! between steps stays up. The game keeps running under it.")]
    public float niceSeconds = 0.6f;
    [Tooltip("Returning players see Skip (and ESC skips) only for this many real seconds at the start.")]
    public float skipSeconds = 30f;

    [Header("Steps")]
    [Tooltip("The lives and time tour at the end. Off until the score HUD and the song progress bar are in (#157).")]
    public bool hudStep;

    [Header("Look")]
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color goodColor = new Color(1f, 0.85f, 0.2f);
    public Color warnColor = new Color(1f, 0.55f, 0.2f);
    public Color panelColor = new Color(0.02f, 0.01f, 0.06f, 0.78f);
    public Color ghostColor = new Color(0.45f, 0.9f, 1f, 0.35f);
    public Color shieldTagColor = new Color(0.75f, 0.5f, 1f);
    public Color freezeTagColor = new Color(0.45f, 0.85f, 1f);

    /// <summary>Name of the step being played, e.g. "shoot" or "dodge".</summary>
    public string CurrentStep { get; private set; } = "intro";
    /// <summary>True once the tutorial is handing over to the first level.</summary>
    public bool IsFinishing { get; private set; }
    /// <summary>True for players who finished the tutorial before: only they can skip it.</summary>
    public bool CanSkip { get; private set; }
    /// <summary>Returning players can skip only in the first <see cref="skipSeconds"/> of the tutorial.</summary>
    public bool SkipAvailable => CanSkip && !IsFinishing && Time.unscaledTime - startedAt < skipSeconds;
    /// <summary>ESC pauses, except while Skip is available (returning players, first 30 s), when it skips.</summary>
    public bool CanPause => !SkipAvailable && !IsFinishing;
    /// <summary>The spot the player is being sent to right now (the glowing one), if any.</summary>
    public PlayerMovement.Spot? GoalSpot { get; private set; }

    GameFlow flow;
    BeatClock clock;
    ChartTargetSpawner spawner;
    PlayerShooter shooter;
    PlayerMovement movement;
    PlayerHealth health;
    PowerUpSpawner powerUps;

    readonly List<ChartTarget> spawned = new List<ChartTarget>();
    readonly List<float> bigTimes = new List<float>();
    readonly Dictionary<PowerUpPickup, PlayerMovement.Spot> starSpots = new Dictionary<PowerUpPickup, PlayerMovement.Spot>();
    /// <summary>Counters a step keeps between its tries (cleared when a step starts).</summary>
    readonly Dictionary<string, int> stepState = new Dictionary<string, int>();
    int damageTaken;
    bool movementLocked;
    float baseFixedDeltaTime, startedAt;
    /// <summary>While true no asteroid can hurt the ship: the power-ups step is for learning, not surviving.</summary>
    bool harmless;
    float savedPlanetLead = -1f;
    float wantedTimeScale = 1f;
    NoteLane nextLane = NoteLane.Right;

    Canvas canvas;
    GameObject canvasObject, banner, livesHighlight, timeHighlight, skipButton;
    Graphic[] livesHighlightParts, timeHighlightParts;
    Text clockSample;
    Text bannerTitle, bannerDesc;
    RectTransform bannerRect;
    Font font;
    TutorialCues cues;
    TutorialGhost ghost;

    float SongTime => clock != null ? clock.SongTime : Time.time;
    float Beat => clock != null ? clock.SecondsPerBeat : 1f;
    float GoodWindow => clock != null ? clock.goodWindow : 0.25f;
    float PerfectWindow => clock != null ? clock.perfectWindow : 0.12f;

    /// <summary>One try at a step: <see cref="passed"/> ends the step; otherwise it is tried again.</summary>
    class Try
    {
        public bool passed;
    }

    void Start()
    {
        flow = GetComponent<GameFlow>();
        clock = BeatClock.Instance;
        spawner = ChartTargetSpawner.Instance != null ? ChartTargetSpawner.Instance : FindFirstObjectByType<ChartTargetSpawner>();
        shooter = FindFirstObjectByType<PlayerShooter>();
        movement = FindFirstObjectByType<PlayerMovement>();
        health = PlayerHealth.Instance;
        powerUps = FindFirstObjectByType<PowerUpSpawner>();
        baseFixedDeltaTime = Time.fixedDeltaTime;
        CanSkip = GameFlow.TutorialDone;
        startedAt = Time.unscaledTime;

        if (health != null)
        {
            health.minHealth = 1;
            health.Damaged += OnDamaged;
        }
        if (flow != null) flow.PausedChanged += OnPausedChanged;
        if (clock != null)
        {
            clock.bpm = tutorialBpm;
            clock.ResetBeatTracking(); // the scene clock starts now
        }
        if (spawner != null)
        {
            spawner.noteStride = 1;
            spawner.turboEveryN = 0; // big asteroids only where a step asks for one, and no turbo ones
            spawner.bigEveryN = 0;
            spawner.spotAimChance = 0f; // lessons aim at the ship so dodge and shoot stay predictable
            spawner.maxViewportY = 0.68f; // keep asteroids under the prompt
            spawner.Spawned += OnSpawned;
        }
        if (powerUps != null) powerUps.enabled = false; // the tutorial spawns its own stars
        if (health != null && health.TryGetComponent(out Bomb bombPower)) bombPower.goOffImmediately = true; // no waiting for a beat here

        BuildCanvas();
        cues = new TutorialCues(canvas, font, movement, accentColor, goodColor, warnColor);
        ghost = new TutorialGhost(movement, shooter != null ? shooter.muzzle : null, ghostColor);
        Analytics.Instance?.LogTutorial("started", CurrentStep);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        CornerMute.HiddenForSkip = false; // give the corner back to the pause button
        if (health != null) health.Damaged -= OnDamaged;
        if (flow != null) flow.PausedChanged -= OnPausedChanged;
        if (spawner != null) spawner.Spawned -= OnSpawned;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        RestoreAsteroidSpeed();
        if (health != null && health.TryGetComponent(out Bomb bombPower)) bombPower.goOffImmediately = false;
        ghost?.Destroy();
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (skipButton != null && skipButton.activeSelf != SkipAvailable) skipButton.SetActive(SkipAvailable); // gone after the first 30 s
        CornerMute.HiddenForSkip = skipButton != null && skipButton.activeSelf;
        if (SkipAvailable && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Skip();
        // Resuming from the pause menu turns the controls back on, so keep a locked one off.
        if (movement != null && movementLocked && movement.enabled) movement.enabled = false;
        FitBanner();
        PulseLivesHighlight();
        ghost.Update();
        cues.Update(DisplayCamera.Find());
    }

    /// <summary>Ends the tutorial early (returning players, in the first 30 s): clears everything and goes to the level.</summary>
    public void Skip()
    {
        if (!SkipAvailable) return;
        StopAllCoroutines();
        RestoreAsteroidSpeed();
        ShowLivesHighlight(false);
        if (timeHighlight != null) timeHighlight.SetActive(false);
        if (clockSample != null) clockSample.gameObject.SetActive(false);
        cues.ClearGuides();
        cues.ShowProgress(0, 0);
        ghost.Hide();
        GoalSpot = null;
        LockShooter(false);
        LockMovement(false);
        Analytics.Instance?.LogTutorial("skipped", CurrentStep);
        Finish();
    }

    // ------------------------------------------------------------------ steps

    IEnumerator Run()
    {
        yield return RunStep("shoot", ShootTry); // the player shoots straight away
        yield return RunStep("move", MoveTry);
        yield return RunStep("dodge", DodgeTry);
        yield return RunStep("powerups", PowerUpsTry);
        if (hudStep)
        {
            CurrentStep = "hud";
            yield return HudTour();
        }

        CurrentStep = "done";
        Analytics.Instance?.LogTutorial("complete", CurrentStep);
        Finish();
    }

    /// <summary>
    /// Plays a step until the player passes it, trying again after each miss, then shows NICE! for a
    /// moment and moves on by itself. Logs the step's tries and seconds.
    /// </summary>
    IEnumerator RunStep(string step, Func<Try, IEnumerator> attempt)
    {
        CurrentStep = step;
        stepState.Clear();
        float start = Time.unscaledTime;
        int tries = 0;
        var result = new Try();
        while (!result.passed)
        {
            tries++;
            yield return attempt(result);
        }

        cues.ClearGuides();
        cues.ShowProgress(0, 0);
        ghost.Hide();
        GoalSpot = null;
        Analytics.Instance?.LogTutorialStep(step, tries, Time.unscaledTime - start);
        yield return SpeedUp();
        ShowBanner("NICE!", "", goodColor); // yellow, like SHOOT NOW!
        yield return new WaitForSecondsRealtime(niceSeconds);
        HideBanner();
    }

    int State(string key) => stepState.TryGetValue(key, out int value) ? value : 0;
    void SetState(string key, int value) => stepState[key] = value;

    // Step 1: shoot when the brackets turn gold (PERFECT). Good shots add up; a miss just sends another
    // asteroid. No demo: the player starts right away.

    IEnumerator ShootTry(Try result)
    {
        ShowStep("shoot");
        int hits = State("hits");
        cues.ShowProgress(hits, shotsNeeded);
        bool hit = false;
        bool first = hits == 0; // more time until the first good shot lands
        yield return OneTimedShot(NextLane(), h => hit = h, BeatJudgment.Good,
            first ? approachScale : quickApproachScale, first ? windowScale : quickWindowScale);
        if (hit) hits++;
        SetState("hits", hits);
        cues.ShowProgress(hits, shotsNeeded);
        result.passed = hits >= shotsNeeded;
    }

    /// <summary>
    /// One asteroid the player should shoot on the beat. Time slows as it comes in and crawls while the
    /// timing window is open. <paramref name="hit"/> gets whether it broke with <paramref name="needed"/>
    /// or better.
    /// </summary>
    IEnumerator OneTimedShot(NoteLane lane, Action<bool> hit, BeatJudgment needed, float approach, float window)
    {
        ChartTarget target = null;
        yield return SpawnOne(lane, t => target = t, false, shotLeadBeats);
        if (target == null)
        {
            hit(false);
            yield break;
        }
        target.shipHitRadius = -1f; // a missed shot here shouldn't cost a life

        BeatJudgment worst = BeatJudgment.Perfect;
        bool shotAt = false;
        Action<BeatJudgment> track = j =>
        {
            if (shooter.LastShotTarget != target) return;
            shotAt = true;
            if (j > worst) worst = j;
        };
        shooter.ShotFired += track;

        yield return new WaitUntil(() => target == null || RingShowing(target));
        yield return SlowTo(approach);
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - GoodWindow);
        if (target != null) yield return SlowTo(window);
        // The brackets turn gold for the PERFECT window: that is when to shoot.
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - PerfectWindow);
        if (target != null) cues.ShowNow(target);
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime + PerfectWindow);
        cues.HideNow();
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime + GoodWindow);
        if (target != null) yield return SlowTo(0.5f);
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + 0.4f);

        shooter.ShotFired -= track;
        bool broke = target == null;
        if (target != null) Destroy(target.gameObject);
        cues.HideNow();
        yield return SpeedUp();
        // Judgments run Perfect < Good < Miss, so "worst <= needed" means every shot was good enough.
        hit(broke && shotAt && worst <= needed);
    }

    // Step 2: W A S D. The keys show and the ghost slides through the spots at once, and the ship can
    // move the whole time. One move (once the ghost has had a moment) passes; the step never waits long.

    IEnumerator MoveTry(Try result)
    {
        ShowStep("move");
        cues.ShowKeys(true);
        PlayerMovement.Spot from = movement.Current;
        float start = Time.unscaledTime;
        bool moved = false;
        Coroutine demo = StartCoroutine(MoveDemo());
        while (true)
        {
            moved |= movement.Current != from;
            float seconds = Time.unscaledTime - start;
            if ((moved && seconds >= moveMinSeconds) || seconds >= moveMaxSeconds) break;
            yield return null;
        }
        StopCoroutine(demo);
        result.passed = true;
    }

    /// <summary>The ghost visits W, A, S and D once, each key lighting up as it gets there (about 1.5 s).</summary>
    IEnumerator MoveDemo()
    {
        ghost.Show(movement.Current);
        foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
        {
            cues.LightKey(spot, true);
            yield return ghost.MoveTo(spot, 0.25f);
            yield return new WaitForSecondsRealtime(0.12f);
            cues.LightKey(spot, false);
        }
        ghost.Hide();
    }

    // Step 3: big red asteroids can't be shot, so move out of the way. One dodge passes; a hit sends
    // another. The first time, the ghost slides to the safe spot as the asteroid closes in.

    IEnumerator DodgeTry(Try result)
    {
        ShowStep("dodge");
        LockShooter(true);
        if (health.Health <= health.minHealth) health.Heal(1); // so a hit can show
        int hitsBefore = damageTaken;
        PlayerMovement.Spot from = movement.Current;
        PlayerMovement.Spot safe = SafeSpot(from, 0);
        bool showGhost = State("shown") == 0;
        SetState("shown", 1);

        ChartTarget target = null;
        yield return SpawnOne(LaneToward(from), t => target = t, true);
        // Past its cue an asteroid rushes the ship, so slow down while it's still out there.
        yield return new WaitUntil(() => target == null || movement.Current != from || SongTime >= target.hitTime - 0.6f * Beat);

        if (target != null && movement.Current == from)
        {
            cues.ShowArrow(from, safe);
            cues.Glow(safe, true);
            GoalSpot = safe;
            if (showGhost)
            {
                ghost.Show(from);
                StartCoroutine(ghost.MoveTo(safe, 0.5f));
            }
            yield return SlowTo(slowScale * 0.6f);
            yield return new WaitUntil(() => target == null || movement.Current != from);
            cues.ClearGuides();
            ghost.Hide();
            GoalSpot = null;
            yield return SpeedUp();
        }

        float giveUp = Time.unscaledTime + 6f;
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + Beat || Time.unscaledTime > giveUp);
        if (target != null) Destroy(target.gameObject);
        LockShooter(false);
        result.passed = damageTaken == hitsBefore && movement.Current != from;
    }

    /// <summary>
    /// A spot to send the ship to from <paramref name="from"/>, not in <paramref name="used"/> (spot bits).
    /// One key press away comes first (A and D only reach each other through S), and W last, since it
    /// sits under the prompt.
    /// </summary>
    static PlayerMovement.Spot SafeSpot(PlayerMovement.Spot from, int used)
    {
        PlayerMovement.Spot[] order = from == PlayerMovement.Spot.Left || from == PlayerMovement.Spot.Right
            ? new[] { PlayerMovement.Spot.Bottom, from == PlayerMovement.Spot.Left ? PlayerMovement.Spot.Right : PlayerMovement.Spot.Left, PlayerMovement.Spot.Center }
            : new[] { PlayerMovement.Spot.Left, PlayerMovement.Spot.Right, PlayerMovement.Spot.Bottom, PlayerMovement.Spot.Center };
        foreach (PlayerMovement.Spot spot in order)
            if (spot != from && (used & SpotBit(spot)) == 0) return spot;
        return from == PlayerMovement.Spot.Bottom ? PlayerMovement.Spot.Left : PlayerMovement.Spot.Bottom;
    }

    static int SpotBit(PlayerMovement.Spot spot) => 1 << (int)spot;

    // Step 4: the Shield, Freeze and Bomb stars wait on the other three spots; move onto each one. The
    // power-up tabs at the top (PowerUpBanners) name each one as it's collected, like in a level. A few slow
    // asteroids that can't hurt drift in, so Freeze has something to slow and the Bomb something to clear.

    IEnumerator PowerUpsTry(Try result)
    {
        BasicShield shield = health.GetComponent<BasicShield>();
        SlowMo slowMo = health.GetComponent<SlowMo>();
        Bomb bomb = health.GetComponent<Bomb>();
        if (powerUps == null || shield == null || slowMo == null || bomb == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("powerups");
        var stars = new Dictionary<PowerUpPickup.Kind, PowerUpPickup>();
        bool gotShield = false, gotFreeze = false, gotBomb = false, slowed = false;
        var targets = new List<ChartTarget>();

        harmless = true;
        if (spawner != null)
        {
            savedPlanetLead = spawner.planetLeadSeconds;
            spawner.planetLeadSeconds = 3.5f; // about 3.5 s from appearing to arriving, instead of 1
        }
        int sent = 0;
        float nextAsteroid = Time.unscaledTime + 0.5f;
        float allAt = -1f;

        // Parks the star for a power-up not taken yet; one that's gone without being taken comes back.
        void Park(PowerUpPickup.Kind kind, bool taken, string label, Color color)
        {
            if (taken || (stars.TryGetValue(kind, out PowerUpPickup parked) && parked != null)) return;
            PowerUpPickup star = SpawnStar(kind, stars.Values);
            if (star == null) return;
            stars[kind] = star;
            cues.TagWorld(star.transform, label, color);
            cues.BackGlow(star.transform, color);
        }

        while (true)
        {
            if (!gotBomb && sent < 3 && Time.unscaledTime >= nextAsteroid && ChartTarget.Active.Count == 0)
            {
                sent++;
                nextAsteroid = Time.unscaledTime + 3f;
                StartCoroutine(SpawnWave(new[] { (3.7f, sent % 2 == 0 ? NoteLane.Right : NoteLane.Left) }, targets));
            }

            gotShield |= shield.Active;
            gotFreeze |= slowMo.Active;
            gotBomb |= bomb.Active;
            Park(PowerUpPickup.Kind.Shield, gotShield, "SHIELD", shieldTagColor);
            Park(PowerUpPickup.Kind.SlowMo, gotFreeze, "FREEZE", freezeTagColor);
            Park(PowerUpPickup.Kind.Bomb, gotBomb, "BOMB", warnColor);

            // No song here for Freeze to slow, so slow the scene clock while it lasts.
            if (slowMo.Active && !slowed)
            {
                slowed = true;
                StartCoroutine(SlowTo(slowMo.speed));
            }
            if (gotShield && gotFreeze && gotBomb)
            {
                // All three taken: a moment to see them work, then on. No waiting for Freeze to run out.
                if (allAt < 0f) allAt = Time.unscaledTime;
                if (Time.unscaledTime - allAt > 1.2f) break;
            }

            GoalSpot = null;
            foreach (PlayerMovement.Spot s in TutorialCues.Spots)
            {
                bool star = false;
                foreach (PowerUpPickup p in stars.Values)
                    star |= p != null && SpotOf(p) == s;
                if (star && GoalSpot == null) GoalSpot = s;
            }
            yield return null;
        }

        GoalSpot = null;
        cues.ClearGuides();
        slowMo.Cancel();
        yield return new WaitUntil(() => !bomb.Active);
        ClearAsteroids();
        RestoreAsteroidSpeed();
        result.passed = true;
    }

    /// <summary>Back to normal asteroids: they can hurt again and arrive at the usual speed.</summary>
    void RestoreAsteroidSpeed()
    {
        harmless = false;
        if (spawner != null && savedPlanetLead >= 0f)
        {
            spawner.planetLeadSeconds = savedPlanetLead;
            savedPlanetLead = -1f;
        }
    }

    // The end: straight into the first level.

    /// <summary>Hands over to the first level on Easy. The level shows its own intro card and count-in.</summary>
    void Finish()
    {
        IsFinishing = true;
        HideBanner();
        if (skipButton != null) skipButton.SetActive(false);
        ClearAsteroids();
        SetTimeScale(1f);
        flow.FinishTutorial();
    }

    // Off for now (hudStep): where your lives and time are, and what the ship's colour means. To be
    // redone, short and without locking the controls, with the score HUD and the song progress bar (#157).

    IEnumerator HudTour()
    {
        LockShooter(true);
        LockMovement(true);
        BasicShield shield = health.GetComponent<BasicShield>();
        if (shield != null && shield.Active) shield.TryAbsorb(); // a shield would soak up the demo hits
        health.Heal(health.maxHealth);

        // Lives: top left.
        ShowBanner("YOUR LIVES", "Each heart is a life. A hit costs one.");
        ShowLivesHighlight(true);
        // Under the banner and the hearts, pointing up at them.
        cues.ShowCallout("YOUR LIVES", new Vector2(0f, 1f), new Vector2(-10f, -125f), TutorialCues.CalloutArrow.Up);
        yield return new WaitForSecondsRealtime(4.2f);
        ShowLivesHighlight(false);
        cues.HideCallout();

        // Time: top right, where the clock sits in a level.
        ShowBanner("YOUR TIME", "The clock counts down the time left in the song.");
        clockSample.gameObject.SetActive(true);
        timeHighlight.SetActive(true);
        cues.ShowCallout("YOUR TIME", new Vector2(1f, 1f), new Vector2(24f, -125f), TutorialCues.CalloutArrow.Up);
        yield return new WaitForSecondsRealtime(4.2f);
        timeHighlight.SetActive(false);
        clockSample.gameObject.SetActive(false);
        cues.HideCallout();

        // Ship colour: blue, then orange at 2 lives, then red at 1.
        ShowBanner("SHIP COLOR", "Your ship changes color as you lose lives.");
        cues.TagWorld(health.transform, "BLUE: healthy", accentColor);
        yield return new WaitForSecondsRealtime(2f);
        yield return LoseLives(2);
        cues.ClearWorldTags();
        cues.TagWorld(health.transform, "ORANGE: 2 lives left", warnColor);
        yield return new WaitForSecondsRealtime(2f);
        yield return LoseLives(1);
        cues.ClearWorldTags();
        cues.TagWorld(health.transform, "RED: last life!", new Color(1f, 0.35f, 0.35f));
        yield return new WaitForSecondsRealtime(2f);

        cues.ClearWorldTags();
        health.Heal(health.maxHealth);
        LockShooter(false);
        LockMovement(false);
    }

    /// <summary>Takes hits, one at a time, until the ship is down to <paramref name="lives"/>.</summary>
    IEnumerator LoseLives(int lives)
    {
        for (int tries = 0; health.Health > lives && tries < 8; tries++)
        {
            health.TakeDamage(1);
            yield return new WaitForSecondsRealtime(0.85f); // past the ship's short invulnerable time
        }
    }

    // ------------------------------------------------------------------ stars

    /// <summary>Parks a star on a spot that isn't the ship's or another star's, until it's grabbed.</summary>
    PowerUpPickup SpawnStar(PowerUpPickup.Kind kind, IEnumerable<PowerUpPickup> others)
    {
        if (powerUps == null) return null;
        int used = SpotBit(movement.Current);
        foreach (PowerUpPickup other in others)
            if (other != null) used |= SpotBit(SpotOf(other));
        PlayerMovement.Spot spot = SafeSpot(movement.Current, used);
        PowerUpPickup star = kind switch
        {
            PowerUpPickup.Kind.SlowMo => powerUps.SpawnSlowMo(health, spot),
            PowerUpPickup.Kind.Bomb => powerUps.SpawnBomb(health, spot),
            _ => powerUps.SpawnShield(health, spot)
        };
        if (star == null) return null;
        KeepUntilGrabbed(star);
        starSpots[star] = spot;
        return star;
    }

    /// <summary>The spot a star is parked on (the nearest one, for stars spawned elsewhere).</summary>
    PlayerMovement.Spot SpotOf(PowerUpPickup star)
    {
        if (starSpots.TryGetValue(star, out PlayerMovement.Spot known)) return known;
        PlayerMovement.Spot best = PlayerMovement.Spot.Bottom;
        float nearest = float.MaxValue;
        Vector3 at = star.transform.position - star.shipOffset;
        foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
        {
            float d = (movement.WorldPositionOf(spot) - at).sqrMagnitude;
            if (d < nearest)
            {
                nearest = d;
                best = spot;
            }
        }
        return best;
    }

    // Spot stars fly in like in a level, but then wait at their spot until grabbed.
    static void KeepUntilGrabbed(PowerUpPickup pickup)
    {
        pickup.lifetime = 0f;
        if (pickup.TryGetComponent(out PickupStar star)) star.lingerSeconds = float.PositiveInfinity;
    }

    // ------------------------------------------------------------------ asteroids

    IEnumerator SpawnOne(NoteLane lane, Action<ChartTarget> got, bool big = false, float leadBeats = 2f)
    {
        var list = new List<ChartTarget>();
        yield return SpawnWave(new[] { (leadBeats, lane) }, list, big);
        got(list.Count > 0 ? list[0] : null);
    }

    /// <summary>Queues notes (beats from now, lane) and waits until they have all spawned.
    /// With <paramref name="big"/>, every note in the wave is a big, unshootable asteroid.</summary>
    IEnumerator SpawnWave((float beats, NoteLane lane)[] wave, List<ChartTarget> into, bool big = false)
    {
        if (spawner == null) yield break;
        var notes = new List<ChartNote>();
        float now = SongTime;
        float lastTime = now;
        foreach (var (beats, lane) in wave)
        {
            float time = now + beats * Beat;
            notes.Add(new ChartNote(time, 0, lane));
            if (big) bigTimes.Add(time);
            lastTime = Mathf.Max(lastTime, time);
        }

        int before = spawned.Count;
        spawner.SetNotes(notes);
        float giveUp = Time.unscaledTime + 20f;
        yield return new WaitUntil(() => spawned.Count - before >= wave.Length || SongTime > lastTime || Time.unscaledTime > giveUp);
        for (int i = before; i < spawned.Count; i++) into.Add(spawned[i]);
    }

    void OnSpawned(ChartTarget target)
    {
        spawned.Add(target);
        if (harmless) target.shipHitRadius = -1f;
        int big = bigTimes.FindIndex(time => Mathf.Abs(time - target.hitTime) < 0.01f);
        if (big < 0) return;
        bigTimes.RemoveAt(big);
        target.MakeBig(spawner.bigScale, spawner.bigHitRadius, spawner.bigColor);
        target.steadyWarning = true;
    }

    NoteLane NextLane()
    {
        NoteLane lane = nextLane;
        nextLane = nextLane == NoteLane.Right ? NoteLane.Left : NoteLane.Right;
        return lane;
    }

    /// <summary>The lane on the ship's side of the screen, for an asteroid meant to reach it.</summary>
    static NoteLane LaneToward(PlayerMovement.Spot spot) => spot == PlayerMovement.Spot.Right ? NoteLane.Right : NoteLane.Left;

    void OnDamaged(int amount)
    {
        if (amount > 0) damageTaken++;
    }

    bool RingShowing(ChartTarget target) => SongTime >= target.hitTime - Beat;

    void ClearAsteroids()
    {
        foreach (ChartTarget t in FindObjectsByType<ChartTarget>(FindObjectsSortMode.None))
            Destroy(t.gameObject);
        foreach (PowerUpPickup p in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None))
            Destroy(p.gameObject);
        spawner?.SetNotes(new List<ChartNote>());
    }

    void LockShooter(bool locked)
    {
        // Only the firing is blocked: the crosshair keeps following the mouse the whole time.
        if (shooter != null) shooter.fireBlocked = locked;
    }

    void LockMovement(bool locked)
    {
        movementLocked = locked;
        if (movement != null) movement.enabled = !locked;
    }

    // ------------------------------------------------------------------ time

    IEnumerator SpeedUp() => SlowTo(1f);

    /// <summary>Eases <see cref="Time.timeScale"/> to <paramref name="target"/> over <see cref="rampSeconds"/> (real time).</summary>
    IEnumerator SlowTo(float target)
    {
        float from = wantedTimeScale;
        for (float t = 0f; t < rampSeconds; t += Time.unscaledDeltaTime)
        {
            SetTimeScale(Mathf.Lerp(from, target, t / rampSeconds));
            yield return null;
        }
        SetTimeScale(target);
    }

    /// <summary>While the pause menu is open the scale is only remembered, then put back on resume.</summary>
    void SetTimeScale(float scale)
    {
        wantedTimeScale = scale;
        if (flow != null && flow.IsPaused) return;
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(0.01f, scale);
    }

    void OnPausedChanged(bool paused)
    {
        if (!paused) SetTimeScale(wantedTimeScale);
    }

    // ------------------------------------------------------------------ UI

    /// <summary>What each step says: a big title and one short line under it.</summary>
    static readonly Dictionary<string, (string title, string line)> Words = new Dictionary<string, (string, string)>
    {
        ["shoot"] = ("SHOOT", "Click the moment the brackets turn gold for a PERFECT."),
        ["move"] = ("MOVE", "Press W A S D to move your ship."),
        ["dodge"] = ("AVOID BIG ASTEROIDS", "Big red asteroids can't be shot. Move out of the way!"),
        ["powerups"] = ("POWER-UPS", "Move to collect power-ups: SHIELD, FREEZE and BOMB."),
    };

    void ShowStep(string step)
    {
        (string title, string line) = Words[step];
        ShowBanner(title, line);
    }

    /// <summary>A big title with one short line under it, at the top.</summary>
    void ShowBanner(string title, string line = "", Color? color = null)
    {
        bannerTitle.text = title;
        bannerTitle.color = color ?? accentColor;
        bannerDesc.text = line;
        banner.SetActive(true);
    }

    /// <summary>The banner stays clear of the hearts hint at the top left, so it is narrower in a narrow window.</summary>
    void FitBanner()
    {
        if (bannerRect == null || canvas == null) return;
        float width = Mathf.Clamp(Screen.width / Mathf.Max(0.01f, canvas.scaleFactor) - 700f, 640f, 1100f);
        if (Mathf.Approximately(bannerRect.sizeDelta.x, width)) return;
        bannerRect.sizeDelta = new Vector2(width, bannerRect.sizeDelta.y);
        bannerTitle.rectTransform.sizeDelta = new Vector2(width - 30f, bannerTitle.rectTransform.sizeDelta.y);
        bannerDesc.rectTransform.sizeDelta = new Vector2(width - 30f, bannerDesc.rectTransform.sizeDelta.y);
    }

    void HideBanner() => banner.SetActive(false);

    void BuildCanvas()
    {
        // Pixelify Sans: lightly pixel but easy to read (the menus' small-text font).
        font = Resources.Load<Font>("UI/Fonts/PixelifySans");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasObject = new GameObject("TutorialUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var panel = new GameObject("Banner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        panel.rectTransform.SetParent(canvasObject.transform, false);
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        panel.rectTransform.pivot = new Vector2(0.5f, 1f);
        // Compact and at the very top, so it ends above the ship when it sits at the W spot.
        panel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        panel.rectTransform.sizeDelta = new Vector2(1100f, 112f);
        panel.color = panelColor;
        panel.raycastTarget = false;
        banner = panel.gameObject;
        bannerRect = panel.rectTransform;
        bannerTitle = AddText(banner.transform, 56, FontStyle.Bold, new Vector2(0f, 24f));
        bannerDesc = AddText(banner.transform, 29, FontStyle.Normal, new Vector2(0f, -30f));
        // Sentences in Chakra Petch (clean, techy, easy to read); all-caps words stay in pixel Pixelify Sans.
        Font sentenceFont = Resources.Load<Font>("UI/Fonts/ChakraPetch-SemiBold");
        if (sentenceFont != null) bannerDesc.font = sentenceFont;
        // Long lines shrink to fit when the window is narrow.
        bannerDesc.resizeTextForBestFit = true;
        bannerDesc.resizeTextMinSize = 18;
        bannerDesc.resizeTextMaxSize = 29;
        banner.SetActive(false);

        if (CanSkip) BuildSkipButton();
        BuildLivesHighlight();
        BuildClockSample();
    }

    /// <summary>A frame around the lives in the top left (same layout as <see cref="HealthHUD"/>).</summary>
    void BuildLivesHighlight()
    {
        // Read from HealthHUD's layout so the frame always fits the hearts (same canvas scaling).
        const float pad = 14f, thickness = 7f;
        int hearts = health != null ? health.maxHealth : 5;
        float heartsWidth = hearts * HealthHUD.PipWidth + (hearts - 1) * HealthHUD.PipGap;
        Vector2 size = new Vector2(heartsWidth + pad * 2f, HealthHUD.PipHeight + pad * 2f);

        var root = new GameObject("LivesHighlight", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(HealthHUD.Margin - pad, -HealthHUD.Margin + pad);
        root.sizeDelta = size;
        livesHighlight = root.gameObject;

        var parts = new List<Graphic>();
        void Bar(Vector2 min, Vector2 max, Vector2 sizeDelta)
        {
            var bar = new GameObject("Edge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bar.rectTransform.SetParent(root, false);
            bar.rectTransform.anchorMin = min;
            bar.rectTransform.anchorMax = max;
            bar.rectTransform.sizeDelta = sizeDelta;
            bar.raycastTarget = false;
            parts.Add(bar);
        }
        Bar(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness));
        Bar(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness));
        Bar(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f));
        Bar(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f));

        livesHighlightParts = parts.ToArray();
        livesHighlight.SetActive(false);
    }

    /// <summary>The game's clock is hidden in the tutorial, so show a sample where it sits in a level, with a frame.</summary>
    void BuildClockSample()
    {
        // Same spot and size as LevelClock: under the pause button in the top-right corner.
        clockSample = AddText(canvasObject.transform, LevelClock.FontSize, FontStyle.Bold, Vector2.zero);
        RectTransform rect = clockSample.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-LevelClock.Margin, -LevelClock.Top);
        rect.sizeDelta = new Vector2(220f, 73f);
        clockSample.alignment = TextAnchor.UpperRight;
        clockSample.text = "1:30";
        clockSample.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        clockSample.gameObject.SetActive(false);

        const float pad = 12f, thickness = 7f;
        var root = new GameObject("TimeHighlight", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-LevelClock.Margin + pad, -LevelClock.Top + pad);
        root.sizeDelta = new Vector2(170f + pad * 2f, 66f + pad * 2f);
        var parts = new List<Graphic>();
        void Bar(Vector2 min, Vector2 max, Vector2 sizeDelta)
        {
            var bar = new GameObject("Edge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bar.rectTransform.SetParent(root, false);
            bar.rectTransform.anchorMin = min;
            bar.rectTransform.anchorMax = max;
            bar.rectTransform.sizeDelta = sizeDelta;
            bar.raycastTarget = false;
            parts.Add(bar);
        }
        Bar(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness));
        Bar(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness));
        Bar(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f));
        Bar(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f));
        timeHighlight = root.gameObject;
        timeHighlightParts = parts.ToArray();
        timeHighlight.SetActive(false);
    }

    void ShowLivesHighlight(bool show)
    {
        if (livesHighlight != null) livesHighlight.SetActive(show);
    }

    void PulseLivesHighlight()
    {
        PulseFrame(livesHighlight, livesHighlightParts);
        PulseFrame(timeHighlight, timeHighlightParts);
    }

    void PulseFrame(GameObject frame, Graphic[] parts)
    {
        if (frame == null || !frame.activeSelf) return;
        Color c = goodColor;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        c.a = Mathf.Lerp(0.5f, 1f, pulse);
        frame.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, pulse);
        foreach (Graphic g in parts) g.color = c;
    }

    /// <summary>SKIP in the top-right corner, where the pause button sits in a level: same cyan pixel HUD
    /// frame and height, a bit more opaque, with the word SKIP instead of the pause symbol.
    /// The pause button is hidden while this shows.</summary>
    void BuildSkipButton()
    {
        float margin = CornerMute.ButtonMargin, height = CornerMute.ButtonHeight;
        var back = new GameObject("SKIP", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        RectTransform rect = back.rectTransform;
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-margin, -margin);
        rect.sizeDelta = new Vector2(130f, height);
        back.color = new Color(0.04f, 0.06f, 0.12f, 0.92f); // more opaque than the HUD frame alone

        var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        frame.rectTransform.SetParent(rect, false);
        frame.rectTransform.anchorMin = Vector2.zero;
        frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = frame.rectTransform.offsetMax = Vector2.zero;
        frame.sprite = PixelArt.HudFrame;
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 0.5f; // same scale as the pause button
        frame.raycastTarget = false;

        Button button = back.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.colorMultiplier = 1.25f;
        button.colors = colors;
        button.targetGraphic = frame;
        button.onClick.AddListener(Skip);
        skipButton = back.gameObject;

        Text label = AddText(rect, 26, FontStyle.Bold, Vector2.zero);
        label.rectTransform.sizeDelta = rect.sizeDelta;
        label.color = UITheme.ElectricCyan;
        label.text = "SKIP";
    }

    Text AddText(Transform parent, int size, FontStyle style, Vector2 position)
    {
        var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(1200f, size * 1.5f);
        text.rectTransform.anchoredPosition = position;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }
}
