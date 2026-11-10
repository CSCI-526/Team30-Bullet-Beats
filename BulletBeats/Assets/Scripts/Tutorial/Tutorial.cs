using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 0: a guided run that plays like the real game, taught by showing instead of telling.
/// A see-through ghost ship (<see cref="TutorialGhost"/>) acts each skill out first, then the player
/// does it. A step only passes once the player has really done it, and after every
/// <see cref="failsBeforeDemo"/> fails the ghost shows it again. Prompts are one or two words; key caps,
/// glowing spots, arrows and marks on the asteroids (<see cref="TutorialCues"/>) do the rest. After each
/// step a check mark waits for SPACE (or Enter).
/// Steps, each with a big title and one line under it: shoot (3 in a row; SHOOT NOW shows when the
/// brackets turn gold, which is PERFECT), move (W A S D), dodge big red asteroids (two ways), a turbo
/// asteroid, the Shield star, the Freeze and Bomb stars, a practice run, then the goal and a 3-2-1
/// into the first level on Easy. The ship can't die here.
/// Only players who finished the tutorial before can skip it (Skip button or ESC); for everyone else
/// ESC opens the pause menu.
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
    [Tooltip("The two quicker shots after the first: time scale while the brackets close in, and during the window.")]
    [Range(0.05f, 1f)] public float quickApproachScale = 1f;
    [Range(0.05f, 1f)] public float quickWindowScale = 0.6f;
    [Tooltip("Beats before a timed asteroid reaches its beat.")]
    [Min(1f)] public float shotLeadBeats = 1.15f;
    [Tooltip("Fails before the ghost shows the step again.")]
    [Min(1)] public int failsBeforeDemo = 2;

    [Header("Look")]
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color goodColor = new Color(1f, 0.85f, 0.2f);
    public Color warnColor = new Color(1f, 0.55f, 0.2f);
    public Color panelColor = new Color(0.02f, 0.01f, 0.06f, 0.78f);
    public Color ghostColor = new Color(0.45f, 0.9f, 1f, 0.35f);
    public Color shieldTagColor = new Color(0.75f, 0.5f, 1f);
    public Color freezeTagColor = new Color(0.45f, 0.85f, 1f);

    /// <summary>Name of the step being played, e.g. "shoot" or "big".</summary>
    public string CurrentStep { get; private set; } = "intro";
    /// <summary>True once the countdown into the level has started.</summary>
    public bool IsFinishing { get; private set; }
    /// <summary>True for players who finished the tutorial before: only they can skip it.</summary>
    public bool CanSkip { get; private set; }
    /// <summary>ESC pauses only for first-time players; for returning players it skips.</summary>
    public bool CanPause => !CanSkip && !IsFinishing;
    /// <summary>The spot the player is being sent to right now (the glowing one), if any.</summary>
    public PlayerMovement.Spot? GoalSpot { get; private set; }
    /// <summary>True while the check mark waits for SPACE.</summary>
    public bool WaitingForNext => cues != null && cues.WaitingForNext;

    GameFlow flow;
    BeatClock clock;
    ChartTargetSpawner spawner;
    PlayerShooter shooter;
    PlayerMovement movement;
    PlayerHealth health;
    PowerUpSpawner powerUps;

    readonly List<ChartTarget> spawned = new List<ChartTarget>();
    readonly List<float> turboTimes = new List<float>();
    readonly List<float> bigTimes = new List<float>();
    readonly Dictionary<PowerUpPickup, PlayerMovement.Spot> starSpots = new Dictionary<PowerUpPickup, PlayerMovement.Spot>();
    /// <summary>Counters a step keeps between its tries (cleared when a step starts).</summary>
    readonly Dictionary<string, int> stepState = new Dictionary<string, int>();
    ChartTarget lastShotTarget;
    int damageTaken;
    bool shooterLocked, movementLocked;
    float baseFixedDeltaTime;
    float wantedTimeScale = 1f;
    NoteLane nextLane = NoteLane.RightUpper;

    Canvas canvas;
    GameObject canvasObject, banner, livesHighlight, timeHighlight, skipButton;
    Graphic[] livesHighlightParts, timeHighlightParts;
    Text clockSample;
    Text bannerTitle, bannerDesc, countdown;
    Font font;
    TutorialCues cues;
    TutorialGhost ghost;

    float SongTime => clock != null ? clock.SongTime : Time.time;
    float Beat => clock != null ? clock.SecondsPerBeat : 1f;
    float GoodWindow => clock != null ? clock.goodWindow : 0.25f;
    float PerfectWindow => clock != null ? clock.perfectWindow : 0.12f;

    /// <summary>One try at a step: <see cref="passed"/> ends the step, <see cref="failed"/> counts toward a replay of the demo.</summary>
    class Try
    {
        public bool passed, failed;
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

        if (health != null)
        {
            health.minHealth = 1;
            health.Damaged += OnDamaged;
        }
        if (shooter != null) shooter.ShotFired += OnShotFired;
        if (flow != null) flow.PausedChanged += OnPausedChanged;
        if (clock != null)
        {
            clock.bpm = tutorialBpm;
            clock.ResetBeatTracking(); // the scene clock starts now
        }
        if (spawner != null)
        {
            spawner.noteStride = 1;
            spawner.turboEveryN = 0; // turbo and big asteroids only where a step asks for one
            spawner.bigEveryN = 0;
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
        if (health != null) health.Damaged -= OnDamaged;
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (flow != null) flow.PausedChanged -= OnPausedChanged;
        if (spawner != null) spawner.Spawned -= OnSpawned;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        if (health != null && health.TryGetComponent(out Bomb bombPower)) bombPower.goOffImmediately = false;
        ghost?.Destroy();
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (CanSkip && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Skip();
        // Resuming from the pause menu turns the controls back on, so keep a locked one off.
        if (movement != null && movementLocked && movement.enabled) movement.enabled = false;
        PulseLivesHighlight();
        ghost.Update();
        cues.Update(DisplayCamera.Find());
    }

    /// <summary>Ends the tutorial early (returning players only): clears everything and goes to the countdown.</summary>
    public void Skip()
    {
        if (IsFinishing || !CanSkip) return;
        StopAllCoroutines();
        ShowLivesHighlight(false);
        if (timeHighlight != null) timeHighlight.SetActive(false);
        if (clockSample != null) clockSample.gameObject.SetActive(false);
        cues.ClearGuides();
        cues.HidePopup();
        cues.ShowProgress(0, 0);
        cues.ShowHearts(0, 0, accentColor);
        ghost.Hide();
        GoalSpot = null;
        LockShooter(false);
        LockMovement(false);
        ClearAsteroids();
        SetTimeScale(1f);
        Analytics.Instance?.LogTutorial("skipped", CurrentStep);
        StartCoroutine(CountdownThenFinish());
    }

    // ------------------------------------------------------------------ steps

    IEnumerator Run()
    {
        yield return RunStep("shoot", null, ShootTry); // the player shoots straight away
        yield return RunStep("move", ControlsDemo, ControlsTry);
        yield return RunStep("big", BigDemo, BigTry);
        yield return RunStep("turbo", null, TurboTry);
        yield return RunStep("shield", ShieldDemo, ShieldTry);
        yield return RunStep("stars", null, StarsTry);
        yield return RunStep("practice", null, PracticeTry);

        CurrentStep = "done";
        Analytics.Instance?.LogTutorial("complete", CurrentStep);
        ClearAsteroids(); // a star from the practice run shouldn't sit behind the cards
        yield return HudTour();
        yield return GoalCard();
        yield return CountdownThenFinish();
    }

    /// <summary>
    /// Plays a step until the player passes it: the ghost demo first, then tries; after every
    /// <see cref="failsBeforeDemo"/> fails the demo plays again. Then the check mark waits for SPACE.
    /// Logs the step's tries and seconds.
    /// </summary>
    IEnumerator RunStep(string step, Func<IEnumerator> demo, Func<Try, IEnumerator> attempt)
    {
        CurrentStep = step;
        stepState.Clear();
        float start = Time.unscaledTime;
        int tries = 0, fails = 0;
        if (demo != null) yield return Demo(demo);

        var result = new Try();
        while (!result.passed)
        {
            result.failed = false;
            tries++;
            yield return attempt(result);
            if (result.failed && ++fails % failsBeforeDemo == 0 && demo != null) yield return Demo(demo);
        }

        cues.ClearGuides();
        cues.ShowProgress(0, 0);
        ghost.Hide();
        GoalSpot = null;
        HideBanner();
        Analytics.Instance?.LogTutorialStep(step, tries, Time.unscaledTime - start);
        yield return WaitForNext();
    }

    int State(string key) => stepState.TryGetValue(key, out int value) ? value : 0;
    void SetState(string key, int value) => stepState[key] = value;

    /// <summary>A demo with the player's shots off, so they can't take the ghost's asteroid.</summary>
    IEnumerator Demo(Func<IEnumerator> demo)
    {
        bool wasLocked = shooterLocked, wasStill = movementLocked;
        LockShooter(true);
        LockMovement(true); // the ghost may stand in for the ship
        yield return demo();
        ghost.Hide();
        cues.ClearGuides();
        LockShooter(wasLocked);
        LockMovement(wasStill);
        yield return SpeedUp();
    }

    IEnumerator WaitForNext()
    {
        yield return SpeedUp();
        cues.ShowPopup(); // SPACE continues; the mouse and crosshair stay free
        yield return new WaitUntil(() => cues.NextPressed);
        cues.HidePopup();
        yield return null;
    }

    // Step 3: W A S D, every spot once.

    IEnumerator ControlsDemo()
    {
        ShowStep("move");
        cues.ShowKeys(true);
        ghost.Show(movement.Current);
        foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
        {
            yield return ghost.MoveTo(spot, 0.4f);
            cues.LightKey(spot, true);
            yield return new WaitForSecondsRealtime(0.35f);
            cues.LightKey(spot, false);
        }
        yield return ghost.MoveTo(movement.Current, 0.3f);
    }

    IEnumerator ControlsTry(Try result)
    {
        // Every spot once. The ones still to visit glow, with their key lit.
        ShowStep("move");
        cues.ShowKeys(true);
        int visited = State("visited") | SpotBit(movement.Current);
        while (visited != 0b1111)
        {
            GoalSpot = null;
            foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
            {
                bool todo = (visited & SpotBit(spot)) == 0;
                cues.LightKey(spot, todo);
                cues.Glow(spot, todo);
                if (todo && GoalSpot == null) GoalSpot = spot;
            }
            yield return null;
            if (!movement.IsDashing) visited |= SpotBit(movement.Current);
            SetState("visited", visited);
        }
        GoalSpot = null;
        cues.ClearGuides();

        result.passed = true;
    }

    static int SpotBit(PlayerMovement.Spot spot) => 1 << (int)spot;

    // Step 1: shoot when the brackets turn gold (PERFECT), 3 in a row. No demo: the player starts right away.

    IEnumerator ShootTry(Try result)
    {
        ShowStep("shoot");
        cues.ShowProgress(State("streak"), 3);
        bool hit = false;
        bool first = State("streak") == 0; // the first shot of the three has the most time
        yield return OneTimedShot(NextLane(), false, h => hit = h, BeatJudgment.Good,
            first ? approachScale : quickApproachScale, first ? windowScale : quickWindowScale);
        int streak = hit ? State("streak") + 1 : 0;
        SetState("streak", streak);
        cues.ShowProgress(streak, 3);
        result.passed = streak >= 3;
        result.failed = !hit;
    }

    /// <summary>
    /// One asteroid the player should shoot on the beat. Time slows as it comes in and crawls while the
    /// timing window is open. <paramref name="hit"/> gets whether it broke with <paramref name="needed"/>
    /// or better.
    /// </summary>
    IEnumerator OneTimedShot(NoteLane lane, bool turbo, Action<bool> hit, BeatJudgment needed = BeatJudgment.Good,
        float approach = -1f, float window = -1f)
    {
        if (approach < 0f) approach = approachScale;
        if (window < 0f) window = windowScale;
        ChartTarget target = null;
        yield return SpawnOne(lane, turbo, t => target = t, false, shotLeadBeats);
        if (target == null)
        {
            hit(false);
            yield break;
        }
        target.shipHitRadius = -1f; // a missed shot here shouldn't cost a life
        if (turbo)
        {
            cues.Mark(target, TutorialCues.MarkKind.Pips);
            cues.Tag(target, "TURBO", warnColor);
        }

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
        cues.Mark(null, TutorialCues.MarkKind.None);
        cues.ClearTag();
        cues.HideNow();
        yield return SpeedUp();
        // Judgments run Perfect < Good < Miss, so "worst <= needed" means every shot was good enough.
        hit(broke && shotAt && worst <= needed);
    }

    // Step 4: big red asteroids can't be shot; move out of the way, two different ways.

    IEnumerator BigDemo()
    {
        ShowStep("big");
        PlayerMovement.Spot from = movement.Current;
        PlayerMovement.Spot safe = SafeSpot(from, 0);
        ChartTarget demo = null;
        yield return SpawnOne(LaneToward(from), false, t => demo = t, big: true);
        if (demo == null) yield break;
        demo.shipHitRadius = -1f; // the player isn't asked to move during the demo
        ghost.Show(from);
        yield return new WaitUntil(() => demo == null || SongTime >= demo.hitTime - 0.6f * Beat);
        yield return SlowTo(slowScale);
        cues.ShowArrow(from, safe);
        cues.Glow(safe, true);
        yield return ghost.MoveTo(safe, 0.6f);
        yield return new WaitForSecondsRealtime(0.6f);
        cues.ClearGuides();
        yield return SpeedUp();
        float giveUp = Time.unscaledTime + 4f;
        yield return new WaitUntil(() => demo == null || SongTime > demo.hitTime + Beat || Time.unscaledTime > giveUp);
        if (demo != null) Destroy(demo.gameObject);
    }

    IEnumerator BigTry(Try result)
    {
        ShowStep("big");
        LockShooter(true);
        if (health.Health <= health.minHealth) health.Heal(1); // so a hit can show
        PlayerMovement.Spot from = movement.Current;
        PlayerMovement.Spot safe = SafeSpot(from, State("dodged"));

        ChartTarget target = null;
        yield return SpawnOne(LaneToward(from), false, t => target = t, big: true);
        // Past its cue an asteroid rushes the ship, so slow down while it's still out there.
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - 0.6f * Beat);

        int hitsBefore = damageTaken;
        cues.ShowArrow(from, safe);
        cues.Glow(safe, true);
        cues.ShowKeys(true);
        cues.LightKey(safe, true);
        GoalSpot = safe;
        yield return SlowTo(slowScale * 0.6f);
        yield return new WaitUntil(() => target == null || movement.Current != from);
        cues.ClearGuides();
        GoalSpot = null;
        yield return SpeedUp();

        float giveUp = Time.unscaledTime + 6f;
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + Beat || Time.unscaledTime > giveUp);
        if (target != null) Destroy(target.gameObject);
        LockShooter(false);

        bool dodged = damageTaken == hitsBefore && movement.Current != from;
        if (dodged) SetState("dodged", State("dodged") | SpotBit(movement.Current));
        result.passed = CountBits(State("dodged")) >= 2;
        result.failed = !dodged;
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

    static int CountBits(int bits)
    {
        int count = 0;
        for (; bits != 0; bits &= bits - 1) count++;
        return count;
    }

    // Step 5: a turbo asteroid takes two hits.

    IEnumerator TurboTry(Try result)
    {
        ShowStep("turbo");
        bool hit = false;
        yield return OneTimedShot(NextLane(), true, h => hit = h);
        result.passed = hit;
        result.failed = !hit;
    }

    // Step 6a: the shield star, then a hit it takes for you.

    IEnumerator ShieldDemo()
    {
        PowerUpPickup star = EnsureStar(PowerUpPickup.Kind.Shield);
        if (star == null) yield break;
        PlayerMovement.Spot spot = SpotOf(star);
        ShowStep("shield");
        cues.TagWorld(star.transform, "SHIELD", shieldTagColor);
        ghost.Show(movement.Current);
        cues.ShowArrow(movement.Current, spot);
        cues.Glow(spot, true);
        yield return new WaitForSecondsRealtime(0.8f);
        yield return ghost.MoveTo(spot, 0.6f);
        yield return new WaitForSecondsRealtime(0.6f);
    }

    IEnumerator ShieldTry(Try result)
    {
        BasicShield shield = health.GetComponent<BasicShield>();
        if (shield == null || powerUps == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("shield");
        if (!shield.Active)
        {
            PowerUpPickup star = EnsureStar(PowerUpPickup.Kind.Shield);
            if (star != null) cues.TagWorld(star.transform, "SHIELD", shieldTagColor);
            float giveUp = Time.unscaledTime + 18f;
            cues.ShowKeys(true);
            while (!shield.Active && star != null && Time.unscaledTime < giveUp)
            {
                PlayerMovement.Spot spot = SpotOf(star);
                foreach (PlayerMovement.Spot s in TutorialCues.Spots)
                {
                    cues.LightKey(s, s == spot);
                    cues.Glow(s, s == spot);
                }
                GoalSpot = spot;
                yield return null;
            }
            cues.ClearGuides();
            GoalSpot = null;
            if (!shield.Active)
            {
                if (star != null) Destroy(star.gameObject);
                result.failed = true;
                yield break;
            }
        }

        // The shield takes the next hit: hold still and let one land.
        ShowBanner("POWER-UPS", "SHIELD is on. Stay put: it blocks the next hit.", shieldTagColor);
        LockShooter(true);
        LockMovement(true);
        int healthBefore = health.Health;
        ChartTarget target = null;
        yield return SpawnOne(LaneToward(movement.Current), false, t => target = t);
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + Beat);
        if (target != null) Destroy(target.gameObject);
        LockMovement(false);
        LockShooter(false);

        result.passed = !shield.Active && health.Health == healthBefore;
        result.failed = !result.passed;
    }

    // Step 6b: Freeze and Bomb, learned by using them.

    IEnumerator StarsTry(Try result)
    {
        SlowMo slowMo = health.GetComponent<SlowMo>();
        Bomb bomb = health.GetComponent<Bomb>();
        if (powerUps == null || slowMo == null || bomb == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("stars");
        bool gotFreeze = false, gotBomb = false, slowed = false;
        PowerUpPickup freezeStar = null, bombStar = null;
        var targets = new List<ChartTarget>();
        float nextWave = Time.unscaledTime;

        float giveUp = Time.unscaledTime + 45f;
        float bothAt = -1f;
        cues.ShowKeys(true);
        while (Time.unscaledTime < giveUp)
        {
            foreach (ChartTarget t in targets) if (t != null) t.shipHitRadius = -1f;
            // Keep a few asteroids coming, so the Bomb has something to clear and Freeze something to slow.
            if (Time.unscaledTime >= nextWave)
            {
                nextWave = Time.unscaledTime + 5f;
                StartCoroutine(SpawnWave(new[] { (2f, NoteLane.LeftUpper, false), (2.7f, NoteLane.RightUpper, false), (3.4f, NoteLane.LeftUpper, false) }, targets));
            }
            // Stars wait until grabbed; one that's gone without being grabbed comes back.
            if (!gotFreeze && freezeStar == null && !slowMo.Active)
            {
                freezeStar = SpawnStar(PowerUpPickup.Kind.SlowMo, bombStar);
                if (freezeStar != null) cues.TagWorld(freezeStar.transform, "FREEZE", freezeTagColor);
            }
            if (!gotBomb && bombStar == null && !bomb.Active)
            {
                bombStar = SpawnStar(PowerUpPickup.Kind.Bomb, freezeStar);
                if (bombStar != null) cues.TagWorld(bombStar.transform, "BOMB", warnColor);
            }
            gotFreeze |= slowMo.Active;
            gotBomb |= bomb.Active;
            if (gotFreeze && gotBomb)
            {
                // Both taken: a few more seconds of slowed asteroids, then on. No waiting for Freeze to run out.
                if (bothAt < 0f) bothAt = Time.unscaledTime;
                if (Time.unscaledTime - bothAt > 3.5f) break;
            }

            // No song here for Freeze to slow, so slow the scene clock while it lasts.
            if (slowMo.Active && !slowed)
            {
                slowed = true;
                StartCoroutine(SlowTo(slowMo.speed));
            }

            GoalSpot = null;
            foreach (PlayerMovement.Spot s in TutorialCues.Spots)
            {
                bool star = (freezeStar != null && SpotOf(freezeStar) == s) || (bombStar != null && SpotOf(bombStar) == s);
                cues.LightKey(s, star);
                cues.Glow(s, star);
                if (star && GoalSpot == null) GoalSpot = s;
            }
            yield return null;
        }
        GoalSpot = null;
        cues.ClearGuides();
        if (freezeStar != null) Destroy(freezeStar.gameObject);
        if (bombStar != null) Destroy(bombStar.gameObject);
        slowMo.Cancel();
        yield return new WaitUntil(() => !bomb.Active);
        yield return SpeedUp();
        yield return WaitUntilCleared(targets, 0.5f);
        result.passed = gotFreeze && gotBomb;
        result.failed = !result.passed;
    }

    // Step 7: a short run on your own.

    IEnumerator PracticeTry(Try result)
    {
        ShowStep("practice");
        yield return new WaitForSecondsRealtime(2.5f);
        HideBanner();
        var targets = new List<ChartTarget>();
        // A short run on a timer: seven asteroids (one turbo), a big one and a star. It ends on time even if
        // the spawner skips a note, because only four asteroids can be on screen at once.
        StartCoroutine(SpawnWave(new[]
        {
            (2f, NoteLane.LeftUpper, false), (4f, NoteLane.RightUpper, false), (6f, NoteLane.LeftUpper, false),
            (8f, NoteLane.RightUpper, true), (10f, NoteLane.LeftUpper, false), (12f, NoteLane.RightUpper, false),
            (14f, NoteLane.LeftUpper, false)
        }, targets));
        bool bigSent = false, starSent = false;
        float start = SongTime;
        float end = start + 17f * Beat;
        float giveUp = Time.unscaledTime + 40f;
        while (SongTime < end && Time.unscaledTime < giveUp)
        {
            if (!bigSent && SongTime - start > 9f * Beat)
            {
                bigSent = true;
                StartCoroutine(SpawnWave(new[] { (2f, LaneToward(movement.Current), false) }, new List<ChartTarget>(), true));
            }
            if (!starSent && SongTime - start > 4f * Beat)
            {
                starSent = true;
                SpawnStar(PowerUpPickup.Kind.Shield, null);
            }
            yield return null;
        }
        yield return WaitUntilCleared(targets, 0.5f);

        // Practice is just practice: no replay if it goes badly, since that only meant another empty wait.
        result.passed = true;
    }

    // Step 8: where your lives and time are, and what the ship's colour means. One go, no NEXT.

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
        cues.ShowCallout("YOUR LIVES", new Vector2(0f, 1f), new Vector2(300f, -32f), true);
        yield return new WaitForSecondsRealtime(3.4f);
        ShowLivesHighlight(false);
        cues.HideCallout();

        // Time: top right, where the clock sits in a level.
        ShowBanner("YOUR TIME", "The clock counts down the time left in the song.");
        clockSample.gameObject.SetActive(true);
        timeHighlight.SetActive(true);
        cues.ShowCallout("YOUR TIME", new Vector2(1f, 1f), new Vector2(-250f, -32f), false);
        yield return new WaitForSecondsRealtime(3.4f);
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

    // Step 9: the goal, then into the first level.

    IEnumerator GoalCard()
    {
        ShowBanner("SURVIVE THE SONG", "Hits cost hearts. Perfect shots win them back.", goodColor);
        cues.ShowHearts(health.Health, health.Health, accentColor);
        yield return new WaitForSecondsRealtime(3.5f);
        cues.ShowHearts(0, 0, accentColor);
        HideBanner();
    }

    IEnumerator CountdownThenFinish()
    {
        IsFinishing = true;
        GameFlow.TutorialDone = true;
        HideBanner();
        if (skipButton != null) skipButton.SetActive(false);
        countdown.gameObject.SetActive(true);
        for (int n = 3; n >= 1; n--)
        {
            countdown.text = n.ToString();
            yield return new WaitForSecondsRealtime(0.8f);
        }
        countdown.text = "GO!";
        yield return new WaitForSecondsRealtime(0.4f);
        flow.FinishTutorial();
    }

    // ------------------------------------------------------------------ stars

    PowerUpPickup EnsureStar(PowerUpPickup.Kind kind)
    {
        foreach (PowerUpPickup p in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None))
            if (p.kind == kind) return p;
        return SpawnStar(kind, null);
    }

    /// <summary>Parks a star on a side spot (not where the ship or <paramref name="other"/> is) until it's grabbed.</summary>
    PowerUpPickup SpawnStar(PowerUpPickup.Kind kind, PowerUpPickup other)
    {
        if (powerUps == null) return null;
        int used = SpotBit(movement.Current) | (other != null ? SpotBit(SpotOf(other)) : 0);
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

    IEnumerator SpawnOne(NoteLane lane, bool turbo, Action<ChartTarget> got, bool big = false, float leadBeats = 2f)
    {
        var list = new List<ChartTarget>();
        yield return SpawnWave(new[] { (leadBeats, lane, turbo) }, list, big);
        got(list.Count > 0 ? list[0] : null);
    }

    /// <summary>Queues notes (beats from now, lane, turbo) and waits until they have all spawned.
    /// With <paramref name="big"/>, every note in the wave is a big, unshootable asteroid.</summary>
    IEnumerator SpawnWave((float beats, NoteLane lane, bool turbo)[] wave, List<ChartTarget> into, bool big = false)
    {
        if (spawner == null) yield break;
        var notes = new List<ChartNote>();
        float now = SongTime;
        float lastTime = now;
        foreach (var (beats, lane, turbo) in wave)
        {
            float time = now + beats * Beat;
            notes.Add(new ChartNote(time, 0, lane));
            if (turbo) turboTimes.Add(time);
            if (big) bigTimes.Add(time);
            lastTime = Mathf.Max(lastTime, time);
        }

        int before = spawned.Count;
        spawner.SetNotes(notes);
        float giveUp = Time.unscaledTime + 20f;
        yield return new WaitUntil(() => spawned.Count - before >= wave.Length || SongTime > lastTime || Time.unscaledTime > giveUp);
        for (int i = before; i < spawned.Count; i++) into.Add(spawned[i]);
    }

    /// <summary>Waits until every target is destroyed or <paramref name="grace"/> beats past its cue; leftovers are removed.</summary>
    IEnumerator WaitUntilCleared(List<ChartTarget> targets, float grace)
    {
        yield return new WaitUntil(() => targets.TrueForAll(t => t == null || SongTime > t.hitTime + grace * Beat));
        foreach (ChartTarget t in targets)
            if (t != null) Destroy(t.gameObject);
    }

    void OnSpawned(ChartTarget target)
    {
        spawned.Add(target);
        int big = bigTimes.FindIndex(time => Mathf.Abs(time - target.hitTime) < 0.01f);
        if (big >= 0)
        {
            bigTimes.RemoveAt(big);
            target.MakeBig(spawner.bigScale, spawner.bigHitRadius, spawner.bigColor);
            target.steadyWarning = true;
            return;
        }
        int turbo = turboTimes.FindIndex(time => Mathf.Abs(time - target.hitTime) < 0.01f);
        if (turbo < 0) return;
        turboTimes.RemoveAt(turbo);
        target.MakeTurbo(spawner.turboHits, spawner.turboHeal, spawner.turboScale, spawner.turboColor);
    }

    NoteLane NextLane()
    {
        NoteLane lane = nextLane;
        nextLane = nextLane == NoteLane.RightUpper ? NoteLane.LeftUpper : NoteLane.RightUpper;
        return lane;
    }

    /// <summary>The lane on the ship's side of the screen, for an asteroid meant to reach it.</summary>
    static NoteLane LaneToward(PlayerMovement.Spot spot) => spot == PlayerMovement.Spot.Right ? NoteLane.RightUpper : NoteLane.LeftUpper;

    void OnShotFired(BeatJudgment judgment) => lastShotTarget = shooter.LastShotTarget;

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
        shooterLocked = locked;
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
        ["big"] = ("DODGE", "Big red asteroids can't be shot. Move out of the way!"),
        ["turbo"] = ("TURBO", "Shoot the orange asteroid twice to break it."),
        ["shield"] = ("POWER-UPS", "SHIELD: move onto the purple star. It blocks one hit."),
        ["stars"] = ("POWER-UPS", "FREEZE and BOMB: move onto the blue and the orange star."),
        ["practice"] = ("YOUR TURN", "Shoot on the beat, dodge red asteroids, grab stars."),
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

    void HideBanner() => banner.SetActive(false);

    void BuildCanvas()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        panel.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        panel.rectTransform.sizeDelta = new Vector2(1100f, 156f);
        panel.color = panelColor;
        panel.raycastTarget = false;
        banner = panel.gameObject;
        bannerTitle = AddText(banner.transform, 64, FontStyle.Bold, new Vector2(0f, 32f));
        bannerDesc = AddText(banner.transform, 32, FontStyle.Normal, new Vector2(0f, -38f));
        banner.SetActive(false);

        countdown = AddText(canvasObject.transform, 220, FontStyle.Bold, Vector2.zero);
        countdown.color = goodColor;
        countdown.rectTransform.sizeDelta = new Vector2(800f, 320f);
        countdown.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
        countdown.gameObject.SetActive(false);

        if (CanSkip) BuildSkipButton();
        BuildLivesHighlight();
        BuildClockSample();
    }

    /// <summary>A frame around the lives in the top left (same layout as <see cref="HealthHUD"/>).</summary>
    void BuildLivesHighlight()
    {
        // HealthHUD: hearts 42 wide with a 6 gap, 38 tall, 32 in from the top-left corner, same canvas scaling.
        const float pad = 14f, thickness = 7f;
        int hearts = health != null ? health.maxHealth : 5;
        Vector2 size = new Vector2(hearts * 48f - 6f + pad * 2f, 38f + pad * 2f);

        var root = new GameObject("LivesHighlight", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(32f - pad, -32f + pad);
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
        // LevelClock: 56 pt bold, 28 in from the top-right corner.
        clockSample = AddText(canvasObject.transform, 56, FontStyle.Bold, Vector2.zero);
        RectTransform rect = clockSample.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-28f, -28f);
        rect.sizeDelta = new Vector2(220f, 73f);
        clockSample.alignment = TextAnchor.UpperRight;
        clockSample.text = "1:30";
        clockSample.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        clockSample.gameObject.SetActive(false);

        const float pad = 12f, thickness = 7f;
        var root = new GameObject("TimeHighlight", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-28f + pad, -28f + pad);
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

    void BuildSkipButton()
    {
        var image = new GameObject("SKIP TUTORIAL", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline)).GetComponent<Image>();
        RectTransform rect = image.rectTransform;
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-40f, 40f);
        rect.sizeDelta = new Vector2(380f, 64f);
        image.color = new Color(0.08f, 0.08f, 0.16f, 0.9f);
        image.GetComponent<Outline>().effectColor = accentColor;
        image.GetComponent<Button>().onClick.AddListener(Skip);
        skipButton = image.gameObject;

        Text label = AddText(rect, 26, FontStyle.Bold, Vector2.zero);
        label.rectTransform.sizeDelta = rect.sizeDelta;
        label.text = "SKIP TUTORIAL  (ESC)";
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
