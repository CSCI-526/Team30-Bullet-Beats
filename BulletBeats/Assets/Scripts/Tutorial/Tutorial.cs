using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 0: a short guided run (about 45 seconds) that plays like the real game. Each step shows its
/// cue and the player can act right away; the step moves on by itself once the player has really done
/// it, and stays (trying again) until then. There are no Continue buttons: a quick NICE! (or a line about
/// what just happened) shows between steps.
/// Steps, each with a big title and one line under it:
/// shoot (<see cref="shotsNeeded"/> good shots; SHOOT NOW shows when the brackets turn gold, which is PERFECT),
/// an orange asteroid (two shots, one on each gold bracket),
/// move (W A S D, acted out by a see-through ghost ship while the player can already move; passes only on a move),
/// avoid a big red asteroid (a red ! warns first, the ghost shows the way the first time),
/// the power-ups one at a time, each collected and then shown working: the Shield takes a hit, Freeze slows a
/// stream of asteroids, the Bomb clears a cluster,
/// then a short look at the HUD (lives, score, song progress) with the controls left on.
/// Then straight into the first level on Easy, which shows its own intro card and count-in. The ship can't die here.
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
    [Range(0.05f, 1f)] public float approachScale = 0.5f;
    [Tooltip("Time scale during the timing window, so there is time to react to SHOOT NOW.")]
    [Range(0.05f, 1f)] public float windowScale = 0.25f;
    [Tooltip("Shots after the first good one: time scale while the brackets close in, and during the window. Still a little slow.")]
    [Range(0.05f, 1f)] public float quickApproachScale = 0.8f;
    [Range(0.05f, 1f)] public float quickWindowScale = 0.5f;
    [Tooltip("Beats before a timed asteroid reaches its beat.")]
    [Min(1f)] public float shotLeadBeats = 1.15f;
    [Tooltip("Good shots (GOOD or PERFECT) that pass the shoot step. A miss doesn't reset the count.")]
    [Min(1)] public int shotsNeeded = 3;
    [Tooltip("The move step passes once the player has moved and this many real seconds have gone by, so the ghost is seen.")]
    public float moveMinSeconds = 1.5f;
    [Tooltip("Real seconds without a move before the ghost shows the move again and the other spots glow.")]
    public float moveHintSeconds = 3f;
    [Tooltip("Real seconds the red ! shows where the ship is before the big asteroid comes in.")]
    public float bigWarningSeconds = 1.5f;
    [Tooltip("Real seconds the NICE! between steps stays up. The game keeps running under it.")]
    public float niceSeconds = 0.6f;
    [Tooltip("Real seconds a power-up's result line (BLOCKED!, FROZEN!, BOOM!) stays up.")]
    public float resultSeconds = 1.2f;
    [Tooltip("Real seconds Freeze runs before the tutorial moves on, so the slow-down is easy to see.")]
    public float freezeShowSeconds = 1.8f;
    [Tooltip("Real seconds each HUD stop (lives, score, song progress) stays up.")]
    public float hudStopSeconds = 2.2f;
    [Tooltip("Seconds a power-up star takes to fly in to its spot. Quicker than in a level, so nobody waits on it.")]
    public float starFlySeconds = 1f;
    [Tooltip("Returning players see Skip (and ESC skips) only for this many real seconds at the start.")]
    public float skipSeconds = 30f;

    [Header("Steps")]
    [Tooltip("The short look at the lives, score and song progress at the end. The controls stay on.")]
    public bool hudStep = true;

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

    /// <summary>
    /// Where <see cref="PowerUpBanners"/> puts its tabs in the tutorial, in canvas units from the top of the
    /// screen: a lane just under the banner (and its progress dots), so the tabs are never under the banner's text.
    /// </summary>
    public float NotificationTop
    {
        get
        {
            float bannerBottom = bannerRect != null ? -bannerRect.anchoredPosition.y + bannerRect.sizeDelta.y : 120f;
            return bannerBottom + (cues != null && cues.ProgressShowing ? 52f : 0f) + 26f;
        }
    }

    GameFlow flow;
    BeatClock clock;
    ChartTargetSpawner spawner;
    PlayerShooter shooter;
    PlayerMovement movement;
    PlayerHealth health;
    PowerUpSpawner powerUps;

    readonly List<ChartTarget> spawned = new List<ChartTarget>();
    readonly List<float> bigTimes = new List<float>();
    readonly List<float> turboTimes = new List<float>();
    readonly Dictionary<PowerUpPickup, PlayerMovement.Spot> starSpots = new Dictionary<PowerUpPickup, PlayerMovement.Spot>();
    readonly Dictionary<PowerUpPickup, float> starLandsAt = new Dictionary<PowerUpPickup, float>();
    /// <summary>Counters a step keeps between its tries (cleared when a step starts).</summary>
    readonly Dictionary<string, int> stepState = new Dictionary<string, int>();
    int damageTaken;
    bool movementLocked;
    float baseFixedDeltaTime, startedAt;
    /// <summary>While true no asteroid can hurt the ship: the Freeze and Bomb demos are for watching.</summary>
    bool harmless;
    float savedPlanetLead = -1f;
    float wantedTimeScale = 1f;
    NoteLane nextLane = NoteLane.Right;
    bool moveDemoRunning;

    Canvas canvas;
    GameObject canvasObject, banner, highlight, skipButton;
    Graphic[] highlightParts;
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
            spawner.turboEveryN = 0; // big and orange asteroids only where a step asks for one
            spawner.bigEveryN = 0;
            spawner.spotAimChance = 0f; // lessons aim at the ship so dodge and shoot stay predictable
            spawner.maxViewportY = 0.68f; // keep asteroids under the prompt
            spawner.Spawned += OnSpawned;
        }
        if (powerUps != null) powerUps.enabled = false; // the tutorial spawns its own stars
        if (health != null && health.TryGetComponent(out Bomb bombPower))
        {
            bombWaitedForBeat = bombPower.waitForBeat;
            bombPower.waitForBeat = false; // no waiting for a beat here
        }

        BuildCanvas();
        cues = new TutorialCues(canvas, font, movement, accentColor, goodColor, warnColor);
        ghost = new TutorialGhost(movement, shooter != null ? shooter.muzzle : null, ghostColor);
        Analytics.Instance?.LogTutorial("started", CurrentStep);
        StartCoroutine(Run());
    }

    bool bombWaitedForBeat;

    void OnDestroy()
    {
        if (health != null) health.Damaged -= OnDamaged;
        if (flow != null) flow.PausedChanged -= OnPausedChanged;
        if (spawner != null)
        {
            spawner.Spawned -= OnSpawned;
            spawner.ClearBigWarnings();
        }
        Time.fixedDeltaTime = baseFixedDeltaTime;
        RestoreAsteroidSpeed();
        StopProgressPreview();
        if (health != null && health.TryGetComponent(out Bomb bombPower)) bombPower.waitForBeat = bombWaitedForBeat;
        ghost?.Destroy();
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (skipButton != null && skipButton.activeSelf != SkipAvailable) skipButton.SetActive(SkipAvailable); // gone after the first 30 s
        if (SkipAvailable && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Skip();
        // Resuming from the pause menu turns the controls back on, so keep a locked one off.
        if (movement != null && movementLocked && movement.enabled) movement.enabled = false;
        FitBanner();
        PulseFrame(highlight, highlightParts);
        ghost.Update();
        cues.Update(DisplayCamera.Find());
    }

    /// <summary>Ends the tutorial early (returning players, in the first 30 s): clears everything and goes to the level.</summary>
    public void Skip()
    {
        if (!SkipAvailable) return;
        StopAllCoroutines();
        RestoreAsteroidSpeed();
        StopProgressPreview();
        if (highlight != null) highlight.SetActive(false);
        spawner?.ClearBigWarnings();
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
        yield return RunStep("turbo", TurboTry);
        yield return RunStep("move", MoveTry);
        yield return RunStep("dodge", DodgeTry);
        // Power-ups, one at a time: collect it, see it work, then the next one.
        yield return RunStep("shield", ShieldTry, "BLOCKED!", "The shield took the hit. No life lost.");
        yield return RunStep("freeze", FreezeTry, "FROZEN!", "Freeze slows everything down for a while.");
        yield return RunStep("bomb", BombTry, "BOOM!", "The bomb cleared every asteroid on screen.");
        if (hudStep)
        {
            CurrentStep = "hud";
            float start = Time.unscaledTime;
            yield return HudTour();
            Analytics.Instance?.LogTutorialStep("hud", 1, Time.unscaledTime - start);
        }

        CurrentStep = "done";
        Analytics.Instance?.LogTutorial("complete", CurrentStep);
        Finish();
    }

    /// <summary>
    /// Plays a step until the player passes it, trying again after each miss, then shows NICE! (or
    /// <paramref name="doneTitle"/> with <paramref name="doneLine"/>) for a moment and moves on by itself.
    /// Logs the step's tries and seconds.
    /// </summary>
    IEnumerator RunStep(string step, Func<Try, IEnumerator> attempt, string doneTitle = "NICE!", string doneLine = "")
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
        ShowBanner(doneTitle, doneLine, goodColor); // yellow, like SHOOT NOW!
        yield return new WaitForSecondsRealtime(string.IsNullOrEmpty(doneLine) ? niceSeconds : resultSeconds);
        HideBanner();
    }

    int State(string key) => stepState.TryGetValue(key, out int value) ? value : 0;
    void SetState(string key, int value) => stepState[key] = value;

    // Step 1: shoot when the brackets turn gold (PERFECT). Good shots add up; a miss just sends another
    // asteroid. No demo: the player starts right away. Time runs a little slow the whole step.

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

    // Step 2: an orange asteroid takes two shots. Its brackets turn gold twice, once for each hit (the first
    // while it's still far out), and SHOOT NOW shows both times. Hit pips on it count down. Passes once it
    // breaks; a miss sends another one.

    IEnumerator TurboTry(Try result)
    {
        if (spawner == null || shooter == null)
        {
            result.passed = true;
            yield break;
        }
        ShowStep("turbo");
        SlowAsteroids(3f); // longer flight, so the first beat comes a moment after it appears

        ChartTarget target = null;
        yield return SpawnOne(NextLane(), t => target = t, false, 3.6f, turbo: true);
        if (target == null)
        {
            RestoreAsteroidSpeed();
            yield break;
        }
        target.shipHitRadius = -1f; // a miss here shouldn't cost a life
        cues.Mark(target, TutorialCues.MarkKind.Pips);
        cues.Tag(target, "2 SHOTS", warnColor);

        int needed = Mathf.Max(1, target.HitsLeft), landed = 0;
        Action<BeatJudgment> track = j =>
        {
            if (shooter.LastShotTarget == target && j != BeatJudgment.Miss) landed++;
        };
        shooter.ShotFired += track;
        float perfect = PerfectWindow * spawner.turboWindowScale, good = GoodWindow * spawner.turboWindowScale;

        for (int shot = 0; shot < needed && target != null; shot++)
        {
            float judge = target.JudgeTime;
            int before = landed;
            yield return new WaitUntil(() => target == null || SongTime >= judge - good - 0.6f * Beat);
            yield return SlowTo(approachScale);
            yield return new WaitUntil(() => target == null || SongTime >= judge - good);
            if (target != null) yield return SlowTo(windowScale);
            yield return new WaitUntil(() => target == null || landed > before || SongTime >= judge - perfect);
            if (target != null && landed == before) cues.ShowNow(target);
            yield return new WaitUntil(() => target == null || landed > before || SongTime >= judge + good);
            cues.HideNow();
            yield return SpeedUp();
            if (landed == before) break; // missed this beat: a fresh asteroid comes next
            if (target != null) cues.Tag(target, "1 MORE!", warnColor);
        }

        shooter.ShotFired -= track;
        yield return new WaitForSecondsRealtime(0.3f);
        if (target != null) Destroy(target.gameObject);
        cues.Mark(null, TutorialCues.MarkKind.None);
        cues.ClearTag();
        cues.HideNow();
        RestoreAsteroidSpeed();
        result.passed = landed >= needed;
    }

    // Step 3: W A S D. The keys show and the ghost slides through the spots at once, and the ship can
    // move the whole time. Passes only once the player has moved; while they wait, the ghost shows it again
    // and the other spots glow.

    IEnumerator MoveTry(Try result)
    {
        ShowStep("move");
        cues.ShowKeys(true);
        PlayerMovement.Spot from = movement.Current;
        float start = Time.unscaledTime, lastHint = start;
        Coroutine demo = StartCoroutine(MoveDemo());
        while (movement.Current == from || Time.unscaledTime - start < moveMinSeconds)
        {
            bool idle = movement.Current == from;
            if (idle && !moveDemoRunning && Time.unscaledTime - lastHint >= moveHintSeconds)
            {
                // Still here: show it again, and light up every spot that's one key away.
                lastHint = Time.unscaledTime;
                foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
                    cues.Glow(spot, spot != from);
                demo = StartCoroutine(MoveDemo());
            }
            if (!idle)
                foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
                    cues.Glow(spot, false);
            yield return null;
        }
        if (demo != null) StopCoroutine(demo);
        moveDemoRunning = false;
        result.passed = true;
    }

    /// <summary>The ghost visits W, A, S and D once, each key lighting up as it gets there (about 1.5 s).</summary>
    IEnumerator MoveDemo()
    {
        moveDemoRunning = true;
        ghost.Show(movement.Current);
        foreach (PlayerMovement.Spot spot in TutorialCues.Spots)
        {
            cues.LightKey(spot, true);
            yield return ghost.MoveTo(spot, 0.25f);
            yield return new WaitForSecondsRealtime(0.12f);
            cues.LightKey(spot, false);
        }
        ghost.Hide();
        moveDemoRunning = false;
    }

    // Step 4: big red asteroids can't be shot, so move out of the way. A blinking red ! shows where the ship
    // is first, like in a level. One dodge passes; a hit sends another. The first time, the ghost slides to
    // the safe spot as the asteroid closes in.

    IEnumerator DodgeTry(Try result)
    {
        ShowStep("dodge");
        LockShooter(true);
        if (health.Health <= health.minHealth) health.Heal(1); // so a hit can show
        int hitsBefore = damageTaken;
        bool showGhost = State("shown") == 0;
        SetState("shown", 1);

        // The warning: a blinking red ! on the ship, for a moment, before the asteroid comes in.
        int warning = spawner != null ? spawner.ShowBigWarning(ShipViewport()) : -1;
        for (float until = Time.unscaledTime + bigWarningSeconds; Time.unscaledTime < until;)
        {
            spawner?.MoveBigWarning(warning, ShipViewport());
            yield return null;
        }

        PlayerMovement.Spot from = movement.Current;
        PlayerMovement.Spot safe = SafeSpot(from, 0);
        ChartTarget target = null;
        yield return SpawnOne(LaneToward(from), t => target = t, true);
        spawner?.HideBigWarning(warning);
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

    /// <summary>The ship's spot on screen (viewport 0-1), for the big asteroid warning.</summary>
    Vector2 ShipViewport()
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null || health == null) return new Vector2(0.5f, 0.2f);
        Vector3 v = cam.WorldToViewportPoint(health.transform.position);
        return new Vector2(v.x, v.y);
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

    // Steps 5-7: the power-ups, one at a time. Each star flies in quickly and waits on a spot next to the
    // ship (never W, so the top of the screen stays clear for the power-up tab); once it has landed, an arrow
    // leads the ship onto it. Once it's collected, a short setup shows what it does, then a line names the
    // result. The power-up tabs (PowerUpBanners) show like in a level.

    // Shield: then hold still and let one asteroid hit it. Hearts don't change.
    IEnumerator ShieldTry(Try result)
    {
        BasicShield shield = health.GetComponent<BasicShield>();
        if (powerUps == null || shield == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("shield");
        if (!shield.Active) yield return Collect(PowerUpPickup.Kind.Shield, "SHIELD", shieldTagColor, () => shield.Active);

        // One asteroid on a straight path to the ship, clearly marked; the ship holds still so it lands.
        ShowBanner("SHIELD", "Hold still: the shield takes the next hit.");
        LockShooter(true);
        LockMovement(true);
        int healthBefore = health.Health, hitsBefore = damageTaken;
        ChartTarget target = null;
        yield return SpawnOne(LaneToward(movement.Current), t => target = t);
        if (target != null) cues.Tag(target, "INCOMING", warnColor);
        yield return new WaitUntil(() => target == null || RingShowing(target));
        yield return SlowTo(approachScale);
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + Beat);
        if (target != null) Destroy(target.gameObject);
        cues.ClearTag();
        yield return SpeedUp();
        LockMovement(false);
        LockShooter(false);

        result.passed = !shield.Active && health.Health >= healthBefore && damageTaken == hitsBefore;
    }

    // Freeze: a stream of asteroids flies past at full speed; collecting Freeze slows them right down.
    IEnumerator FreezeTry(Try result)
    {
        SlowMo slowMo = health.GetComponent<SlowMo>();
        if (powerUps == null || slowMo == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("freeze");
        LockShooter(true);
        harmless = true;
        var rocks = new List<ChartTarget>();
        float streamEnds = -1f;
        void KeepStreamGoing()
        {
            if (SongTime < streamEnds - Beat) return;
            // Six asteroids, one every 0.8 beats, at normal speed.
            var wave = new (float, NoteLane)[6];
            for (int i = 0; i < wave.Length; i++) wave[i] = (1.4f + i * 0.8f, NextLane());
            streamEnds = SongTime + (1.4f + (wave.Length - 1) * 0.8f) * Beat;
            StartCoroutine(SpawnWave(wave, rocks));
        }

        KeepStreamGoing();
        yield return new WaitForSecondsRealtime(1f); // see them at full speed first
        yield return Collect(PowerUpPickup.Kind.SlowMo, "FREEZE", freezeTagColor, () => slowMo.Active, KeepStreamGoing);

        // No song here for Freeze to slow, so slow the scene clock while it lasts.
        ShowBanner("FREEZE", "Everything slows down: the asteroids, and the beat with them.");
        yield return SlowTo(slowMo.speed);
        yield return new WaitForSecondsRealtime(freezeShowSeconds);
        slowMo.Cancel();
        yield return SpeedUp();
        ClearAsteroids();
        RestoreAsteroidSpeed();
        LockShooter(false);
        result.passed = true;
    }

    // Bomb: a cluster of slow asteroids drifts in; collecting the Bomb blows them all up.
    IEnumerator BombTry(Try result)
    {
        Bomb bomb = health.GetComponent<Bomb>();
        if (powerUps == null || bomb == null)
        {
            result.passed = true;
            yield break;
        }

        ShowStep("bomb");
        LockShooter(true);
        harmless = true;
        SlowAsteroids(3.5f); // about 3.5 s from appearing to arriving, so the cluster stays in view
        var cluster = new List<ChartTarget>();
        float clusterEnds = -1f;
        void KeepClusterInView()
        {
            if (SongTime < clusterEnds - 0.5f * Beat) return;
            cluster.Clear();
            var wave = new[] { (4.2f, NoteLane.Left), (4.4f, NoteLane.Right), (4.6f, NoteLane.Left), (4.8f, NoteLane.Right) };
            clusterEnds = SongTime + 4.8f * Beat;
            StartCoroutine(SpawnWave(wave, cluster));
        }

        KeepClusterInView();
        yield return new WaitForSecondsRealtime(1f); // let the cluster come into view first
        yield return Collect(PowerUpPickup.Kind.Bomb, "BOMB", warnColor, () => bomb.Active, KeepClusterInView);
        yield return new WaitUntil(() => !bomb.Active);
        yield return new WaitForSecondsRealtime(0.4f);
        ClearAsteroids();
        RestoreAsteroidSpeed();
        LockShooter(false);
        result.passed = true;
    }

    /// <summary>
    /// The star for <paramref name="kind"/> flies in quickly to a spot next to the ship. Once it has landed,
    /// an arrow and a glow lead the ship onto it: the banner says to move onto the star, so the way is shown
    /// only when the star is there. Waits until <paramref name="collected"/> is true. A star that's gone
    /// without being collected comes back. <paramref name="whileWaiting"/> runs every frame (e.g. to keep asteroids coming).
    /// </summary>
    IEnumerator Collect(PowerUpPickup.Kind kind, string label, Color color, Func<bool> collected, Action whileWaiting = null)
    {
        PowerUpPickup star = null;
        PlayerMovement.Spot? lit = null;
        while (!collected())
        {
            if (star == null)
            {
                star = SpawnStar(kind);
                if (star != null)
                {
                    cues.TagWorld(star.transform, label, color);
                    cues.BackGlow(star.transform, color);
                }
            }
            PlayerMovement.Spot? goal = star != null && HasLanded(star) ? SpotOf(star) : (PlayerMovement.Spot?)null;
            if (goal != lit)
            {
                if (lit.HasValue) cues.Glow(lit.Value, false);
                if (goal.HasValue) cues.Glow(goal.Value, true);
                lit = goal;
            }
            if (goal.HasValue && movement.Current != goal.Value) cues.ShowArrow(movement.Current, goal.Value);
            else cues.HideArrow();
            GoalSpot = goal;
            whileWaiting?.Invoke();
            yield return null;
        }
        GoalSpot = null;
        cues.HideArrow();
        if (lit.HasValue) cues.Glow(lit.Value, false);
        cues.ClearWorldTags();
    }

    /// <summary>True once a star has flown in and sits on its spot.</summary>
    bool HasLanded(PowerUpPickup star) => !starLandsAt.TryGetValue(star, out float at) || SongTime >= at;

    /// <summary>Asteroids take <paramref name="seconds"/> from appearing to arriving, until <see cref="RestoreAsteroidSpeed"/>.</summary>
    void SlowAsteroids(float seconds)
    {
        if (spawner == null) return;
        if (savedPlanetLead < 0f) savedPlanetLead = spawner.planetLeadSeconds;
        spawner.planetLeadSeconds = seconds;
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

    // Step 8: a short look at the HUD. Each stop frames one part and names it; the controls stay on.

    IEnumerator HudTour()
    {
        yield return HudStop("YOUR LIVES", "Each heart is a life. A hit costs one.", HeartsRect);
        // No callout for the score: under the card sit the hearts, so the frame and the title name it.
        yield return HudStop("YOUR SCORE", "PERFECT and GOOD shots score points. Hits in a row raise the multiplier, up to x4.", ScoreRect, false);

        // The song progress bar is hidden in the tutorial, so show it part-way filled while it's pointed at.
        SongProgressBar bar = flow != null ? flow.GetComponent<SongProgressBar>() : null;
        if (bar != null)
        {
            bar.previewFill = 0.35f;
            yield return null; // let it lay itself out
            yield return HudStop("SONG PROGRESS", "The bar fills as the song plays. When it's full, the level is done.", ProgressRect);
            StopProgressPreview();
        }
    }

    /// <summary>
    /// Frames the part of the HUD <paramref name="target"/> finds (screen pixels) and names it in the banner
    /// (and with a callout next to it, if <paramref name="callout"/>), for <see cref="hudStopSeconds"/>.
    /// Skipped if that part isn't on screen. The HUD doesn't move, so it's measured once.
    /// </summary>
    IEnumerator HudStop(string title, string line, Func<Rect?> target, bool callout = true)
    {
        Rect? found = target();
        if (found == null) yield break;
        ShowBanner(title, line);
        FrameAround(found.Value);
        highlight.SetActive(true);
        if (callout) ShowCalloutNextTo(found.Value, title);
        yield return new WaitForSecondsRealtime(hudStopSeconds);
        highlight.SetActive(false);
        cues.HideCallout();
        HideBanner();
    }

    /// <summary>The hearts in the top-left corner (HealthHUD), in screen pixels.</summary>
    Rect? HeartsRect() => ScreenRectOf(GameObject.Find("HealthHUD")?.transform.Find("Corner"));

    /// <summary>The score card above the hearts (ScoreHUD), in screen pixels.</summary>
    Rect? ScoreRect() => ScreenRectOf(GameObject.Find("ScoreHUD")?.transform.Find("Card"));

    Rect? ProgressRect()
    {
        SongProgressBar bar = flow != null ? flow.GetComponent<SongProgressBar>() : null;
        return bar != null && bar.TryGetScreenRect(out Rect rect) && rect.width > 1f && rect.height > 1f ? rect : (Rect?)null;
    }

    void StopProgressPreview()
    {
        SongProgressBar bar = flow != null ? flow.GetComponent<SongProgressBar>() : null;
        if (bar != null) bar.previewFill = -1f;
    }

    /// <summary>
    /// The screen rectangle around every visible image under <paramref name="root"/> (an overlay canvas is in
    /// pixels). Text is left out, so a hint line under the hearts doesn't stretch the frame.
    /// </summary>
    static Rect? ScreenRectOf(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return null;
        bool any = false;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        var corners = new Vector3[4];
        foreach (Image g in root.GetComponentsInChildren<Image>())
        {
            if (!g.isActiveAndEnabled || g.color.a <= 0.01f) continue;
            g.rectTransform.GetWorldCorners(corners);
            foreach (Vector3 c in corners)
            {
                min = Vector2.Min(min, c);
                max = Vector2.Max(max, c);
            }
            any = true;
        }
        return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : (Rect?)null;
    }

    /// <summary>Puts the pulsing highlight frame around a screen rectangle.</summary>
    void FrameAround(Rect screen)
    {
        const float pad = 14f;
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        var rect = (RectTransform)highlight.transform;
        rect.anchoredPosition = screen.center / scale;
        rect.sizeDelta = screen.size / scale + Vector2.one * pad * 2f;
    }

    /// <summary>A callout naming the framed part: under it (pointing up), or to its left for something on the right edge.</summary>
    void ShowCalloutNextTo(Rect screen, string text)
    {
        const float gap = 22f;
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        Rect r = new Rect(screen.position / scale, screen.size / scale);
        if (screen.center.x > Screen.width * 0.6f)
            cues.ShowCallout(text, Vector2.zero, new Vector2(r.xMin - gap - 440f, r.center.y - 35f), TutorialCues.CalloutArrow.Right);
        else
            cues.ShowCallout(text, Vector2.zero, new Vector2(Mathf.Max(8f, r.center.x - 165f), r.yMin - gap - 120f), TutorialCues.CalloutArrow.Up);
    }

    // The end: straight into the first level.

    /// <summary>Hands over to the first level on Easy. The level shows its own intro card and count-in.</summary>
    void Finish()
    {
        IsFinishing = true;
        HideBanner();
        if (skipButton != null) skipButton.SetActive(false);
        ClearAsteroids();
        StopProgressPreview();
        SetTimeScale(1f);
        flow.FinishTutorial();
    }

    // ------------------------------------------------------------------ stars

    /// <summary>
    /// Parks a star on a spot next to the ship (not W, which sits under the banner and the power-up tab),
    /// until it's grabbed.
    /// </summary>
    PowerUpPickup SpawnStar(PowerUpPickup.Kind kind)
    {
        if (powerUps == null) return null;
        int used = SpotBit(movement.Current) | SpotBit(PlayerMovement.Spot.Center);
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
        // It still flies in from the middle like in a level, to show where stars come from, just quicker.
        float landsAt = SongTime;
        if (star.TryGetComponent(out PickupStar flight))
        {
            landsAt = SongTime + starFlySeconds;
            flight.LandAt(landsAt);
        }
        starLandsAt[star] = landsAt;
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

    IEnumerator SpawnOne(NoteLane lane, Action<ChartTarget> got, bool big = false, float leadBeats = 2f, bool turbo = false)
    {
        var list = new List<ChartTarget>();
        yield return SpawnWave(new[] { (leadBeats, lane) }, list, big, turbo);
        got(list.Count > 0 ? list[0] : null);
    }

    /// <summary>Queues notes (beats from now, lane) and waits until they have all spawned.
    /// With <paramref name="big"/>, every note in the wave is a big, unshootable asteroid; with
    /// <paramref name="turbo"/>, an orange two-shot one.</summary>
    IEnumerator SpawnWave((float beats, NoteLane lane)[] wave, List<ChartTarget> into, bool big = false, bool turbo = false)
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
            else if (turbo) turboTimes.Add(time);
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
        // Same as an orange asteroid in a level.
        target.MakeTurbo(spawner.turboHits, spawner.turboHeal, spawner.turboScale, spawner.turboColor,
            spawner.turboShrinkTo, spawner.turboWiden, spawner.turboFirstHitDelay, spawner.turboWindowScale);
        target.SlowApproach(spawner.turboSlow);
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
        ["turbo"] = ("ORANGE ASTEROIDS", "These take 2 shots. Shoot each time the brackets turn gold."),
        ["move"] = ("MOVE", "Press W A S D to move your ship."),
        ["dodge"] = ("AVOID BIG ASTEROIDS", "A red ! means a big asteroid is coming. It can't be shot: move out of the way!"),
        ["shield"] = ("SHIELD", "Move onto the purple star to collect it. It blocks one hit."),
        ["freeze"] = ("FREEZE", "Move onto the blue star to collect it. Watch the asteroids."),
        ["bomb"] = ("BOMB", "Move onto the orange star to collect it. Watch the asteroids."),
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
        BuildHighlight();
    }

    /// <summary>A pulsing frame the HUD tour puts around the hearts, the score and the song progress bar.</summary>
    void BuildHighlight()
    {
        const float thickness = 7f;
        var root = new GameObject("HudHighlight", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = Vector2.zero; // positioned in canvas units from the bottom-left corner
        root.pivot = new Vector2(0.5f, 0.5f);
        highlight = root.gameObject;

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

        highlightParts = parts.ToArray();
        highlight.SetActive(false);
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

    /// <summary>SKIP in the top-right corner, where the pause button sits in a level, using Lakshi's
    /// pixel SKIP art (Resources/UI/Art/skip). The pause button itself is levels only.</summary>
    void BuildSkipButton()
    {
        float margin = CornerMute.ButtonMargin, height = CornerMute.ButtonHeight + 12f;
        var image = new GameObject("SKIP", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        RectTransform rect = image.rectTransform;
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-margin, -margin + 6f);
        image.sprite = UITheme.Art("skip");
        image.preserveAspect = true;
        float aspect = image.sprite != null ? image.sprite.rect.width / image.sprite.rect.height : 1.2f;
        rect.sizeDelta = new Vector2(height * aspect, height);

        Button button = image.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.colorMultiplier = 1.25f;
        button.colors = colors;
        button.onClick.AddListener(Skip);
        skipButton = image.gameObject;
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
