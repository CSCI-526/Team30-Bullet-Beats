using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 0: a guided run that plays like the real game. Asteroids fly in, and when one needs the
/// player to act, time slows almost to a stop and an instruction appears; it speeds back up once
/// they do it. Steps: shoot when the ring turns yellow, take a hit to see the hearts, PERFECT heals,
/// dodge a big asteroid, turbo asteroids, shield and slow-mo power-ups, then a short run at full speed.
/// Ends with the goal (survive the song) and a 3-2-1 countdown into the first level on Easy. The ship
/// can't die here.
/// ESC or the Skip button jumps straight to the countdown.
/// Added by <see cref="GameFlow"/> for a tutorial run. It runs on the scene clock (no song), so
/// slowing <see cref="Time.timeScale"/> slows the asteroids, rings and timing together.
/// </summary>
[RequireComponent(typeof(GameFlow))]
public class Tutorial : MonoBehaviour
{
    [Header("Pacing")]
    [Tooltip("Beat length while in the tutorial; also how long each ring shows.")]
    public float tutorialBpm = 60f;
    [Tooltip("Time scale while an instruction is waiting for the player.")]
    [Range(0.02f, 1f)] public float slowScale = 0.1f;
    [Tooltip("Real seconds to slow down or speed back up.")]
    public float rampSeconds = 0.35f;
    [Tooltip("Tries for a step before the tutorial moves on anyway.")]
    [Min(1)] public int maxTries = 3;

    [Header("Look")]
    public Color accentColor = new Color(0.3f, 0.9f, 1f);
    public Color goodColor = new Color(1f, 0.85f, 0.2f);
    public Color warnColor = new Color(1f, 0.55f, 0.2f);
    public Color panelColor = new Color(0.02f, 0.01f, 0.06f, 0.78f);

    /// <summary>Name of the step being played, e.g. "shoot" or "dodge".</summary>
    public string CurrentStep { get; private set; } = "intro";
    /// <summary>True once the countdown into the level has started.</summary>
    public bool IsFinishing { get; private set; }

    GameFlow flow;
    BeatClock clock;
    ChartTargetSpawner spawner;
    PlayerShooter shooter;
    PlayerMovement movement;
    PlayerHealth health;

    readonly List<ChartTarget> spawned = new List<ChartTarget>();
    readonly List<float> turboTimes = new List<float>();
    readonly List<float> bigTimes = new List<float>();
    bool dashed;
    BeatJudgment lastShot = BeatJudgment.Miss;
    float baseFixedDeltaTime;
    Coroutine ramp;

    GameObject canvasObject, banner, livesHighlight;
    Graphic[] livesHighlightParts;
    Text bannerTitle, bannerBody, countdown;
    Font font;
    PowerUpSpawner powerUps;

    float SongTime => clock != null ? clock.SongTime : Time.time;
    float Beat => clock != null ? clock.SecondsPerBeat : 1f;

    void Start()
    {
        flow = GetComponent<GameFlow>();
        clock = BeatClock.Instance;
        spawner = ChartTargetSpawner.Instance != null ? ChartTargetSpawner.Instance : FindFirstObjectByType<ChartTargetSpawner>();
        shooter = FindFirstObjectByType<PlayerShooter>();
        movement = FindFirstObjectByType<PlayerMovement>();
        health = PlayerHealth.Instance;
        baseFixedDeltaTime = Time.fixedDeltaTime;

        if (health != null) health.minHealth = 1;
        if (movement != null) movement.Dashed += OnDashed;
        if (shooter != null) shooter.ShotFired += OnShotFired;
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
            spawner.maxViewportY = 0.68f; // keep asteroids under the instruction banner
            powerUps = FindFirstObjectByType<PowerUpSpawner>();
            if(powerUps != null) powerUps.enabled = false; //tutorial will spawn own pickups
            spawner.Spawned += OnSpawned;
        }

        BuildCanvas();
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (movement != null) movement.Dashed -= OnDashed;
        if (shooter != null) shooter.ShotFired -= OnShotFired;
        if (spawner != null) spawner.Spawned -= OnSpawned;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Skip();
        PulseLivesHighlight();
    }

    /// <summary>Ends the tutorial early: clears the asteroids and goes to the countdown.</summary>
    public void Skip()
    {
        if (IsFinishing) return;
        StopAllCoroutines();
        ShowLivesHighlight(false);
        ClearAsteroids();
        SetTimeScale(1f);
        Analytics.Instance?.LogTutorial("skipped", CurrentStep);
        StartCoroutine(CountdownThenFinish());
    }

    // ------------------------------------------------------------------ steps

    IEnumerator Run()
    {
        yield return ShowFor("TUTORIAL", "Learn the basics in under a minute.  Press ESC to skip.", 2.5f);
        yield return ShootStep();
        yield return HeartsStep();
        yield return PerfectStep();
        yield return DodgeStep();
        yield return TurboStep();
        yield return PowerUpStep();
        yield return PracticeStep();

        CurrentStep = "done";
        Analytics.Instance?.LogTutorial("complete", CurrentStep);
        yield return ShowFor("YOUR GOAL: SURVIVE THE SONG!", "Keep at least 1 life till the end", 3.5f, goodColor);
        yield return CountdownThenFinish();
    }

    IEnumerator ShootStep()
    {
        CurrentStep = "shoot";
        for (int attempt = 0; attempt < maxTries; attempt++)
        {
            ChartTarget target = null;
            yield return SpawnOne(NoteLane.RightUpper, false, t => target = t);
            yield return new WaitUntil(() => target == null || RingShowing(target));

            lastShot = BeatJudgment.Miss;
            ShowBanner("SHOOT!", "Aim at the asteroid and click when the ring turns <color=#fe5>YELLOW</color>");
            yield return SlowTo(slowScale * 1.5f);
            // The ring is yellow (the PERFECT window) for only a moment before it closes, so crawl
            // through it: slow further once it turns orange, then speed up when the ring is gone.
            yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - GoodWindow);
            if (target != null) yield return SlowTo(slowScale * 0.25f);
            yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - PerfectWindow);
            if (target != null) ShowBanner("CLICK NOW!", "The ring is YELLOW", goodColor);
            yield return new WaitUntil(() => target == null || SongTime >= target.hitTime);
            if (target != null) yield return SlowTo(0.5f);
            yield return new WaitUntil(() => target == null || SongTime > target.hitTime + 0.4f);

            bool hit = target == null && lastShot != BeatJudgment.Miss;
            if (hit && lastShot == BeatJudgment.Perfect) ShowBanner("PERFECT!", "Clicking on YELLOW is a PERFECT shot", goodColor);
            else if (hit) ShowBanner("NICE SHOT!", "Wait for YELLOW next time for a PERFECT", goodColor);
            else
            {
                if (target != null) Destroy(target.gameObject);
                ShowBanner("ALMOST!", "Try again - wait for the ring to turn YELLOW, then click");
            }
            yield return SpeedUp();
            yield return HideAfter(hit ? 2f : 1.5f);
            if (hit) yield break;
        }
    }

    IEnumerator HeartsStep()
    {
        CurrentStep = "hearts";
        if (shooter != null) shooter.enabled = false; // this one has to land
        ShowBanner("LIVES", "Your lives are in the top left.  Let this one hit you...");
        ShowLivesHighlight(true);

        if (health != null && health.Health <= health.minHealth) health.Heal(1); // keep a heart to lose
        int before = health != null ? health.Health : 0;
        ChartTarget target = null;
        yield return SpawnOne(NoteLane.RightUpper, false, t => target = t);
        yield return new WaitUntil(() => target == null || SongTime > target.hitTime + Beat);
        // If the asteroid missed the ship (the player moved away), take the heart anyway for the demo.
        if (health != null && health.Health >= before) health.TakeDamage(1);
        if (target != null) Destroy(target.gameObject);

        ShowBanner("-1 LIFE", "Your ship's color shows your lives:   <color=#59f>BLUE 3-5</color>    <color=#f93>ORANGE 2</color>    <color=#f44>RED 1</color>", warnColor);
        yield return new WaitForSecondsRealtime(4f);
        ShowLivesHighlight(false);
        if (shooter != null) shooter.enabled = true;
        HideBanner();
    }

    IEnumerator PerfectStep()
    {
        CurrentStep = "perfect";
        ShowBanner("PERFECT = +1 LIFE", "PERFECT shots give you a life");
        ShowLivesHighlight(true);
        yield return SlowTo(0.45f);
        var targets = new List<ChartTarget>();
        yield return SpawnWave(new[] { (2f, NoteLane.LeftUpper, false), (3.5f, NoteLane.RightUpper, false), (5f, NoteLane.LeftUpper, false) }, targets);
        yield return WaitUntilCleared(targets, 0.5f);
        yield return SpeedUp();
        ShowLivesHighlight(false);
        HideBanner();
    }

    IEnumerator DodgeStep()
    {
        CurrentStep = "dodge";
        if (shooter != null) shooter.enabled = false; // this one has to be dodged
        ShowBanner("DODGE!", "<color=#f44>RED GIANTS</color> are too big to shoot.  Watch this one...");

        ChartTarget target = null;
        yield return SpawnOne(NoteLane.LeftUpper, false, t => target = t, big: true);
        // Past its ring an asteroid rushes the ship, so slow down while it's still at the ring.
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - 0.6f * Beat);

        dashed = false;
        ShowBanner("DODGE!", "Press  W  A  S  D  to jump out of its way");
        yield return SlowTo(slowScale * 0.6f);
        yield return new WaitUntil(() => dashed || target == null);
        ShowBanner("NICE MOVE!", "W top     A left     S bottom     D right", goodColor);
        yield return SpeedUp();

        float giveUp = Time.unscaledTime + 6f;
        yield return new WaitUntil(() => target == null || Time.unscaledTime > giveUp);
        if (target != null) Destroy(target.gameObject);
        if (shooter != null) shooter.enabled = true;
        HideBanner();
    }

    IEnumerator TurboStep()
    {
        CurrentStep = "turbo";
        for (int attempt = 0; attempt < maxTries; attempt++)
        {
            ChartTarget target = null;
            yield return SpawnOne(NoteLane.RightUpper, true, t => target = t);
            yield return new WaitUntil(() => target == null || RingShowing(target));

            lastShot = BeatJudgment.Miss;
            ShowBanner("TURBO ASTEROID", "Orange ones need 2 HITS.  Destroy it for +2 LIVES");
            yield return SlowTo(slowScale * 1.5f);
            yield return new WaitUntil(() => target == null || SongTime > target.hitTime + 0.6f);

            bool hit = target == null && lastShot != BeatJudgment.Miss;
            if (hit) ShowBanner("+2 LIVES!", "Turbo asteroids are worth the extra shot", goodColor);
            else
            {
                if (target != null) Destroy(target.gameObject);
                ShowBanner("ALMOST!", "Click it twice while the ring is showing");
            }
            yield return SpeedUp();
            yield return HideAfter(hit ? 2f : 1.5f);
            if (hit) yield break;
        }
    }

    IEnumerator PracticeStep()
    {
        CurrentStep = "practice";
        yield return ShowFor("NOW ON YOUR OWN!", "Shoot on YELLOW, dodge what you can't hit", 2f, goodColor);
        var targets = new List<ChartTarget>();
        yield return SpawnWave(new[]
        {
            (2f, NoteLane.LeftUpper, false), (3.5f, NoteLane.RightUpper, false), (5f, NoteLane.LeftUpper, false),
            (6.5f, NoteLane.RightUpper, true), (8f, NoteLane.LeftUpper, false), (9.5f, NoteLane.RightUpper, false)
        }, targets);
        yield return WaitUntilCleared(targets, 1.5f);
    }

    IEnumerator CountdownThenFinish()
    {
        IsFinishing = true;
        HideBanner();
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

    // ------------------------------------------------------------------ asteroids

    IEnumerator SpawnOne(NoteLane lane, bool turbo, System.Action<ChartTarget> got, bool big = false)
    {
        var list = new List<ChartTarget>();
        yield return SpawnWave(new[] { (2f, lane, turbo) }, list, big);
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

    /// <summary>Waits until every target is destroyed or <paramref name="grace"/> beats past its ring; leftovers are removed.</summary>
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
            return;
        }
        int turbo = turboTimes.FindIndex(time => Mathf.Abs(time - target.hitTime) < 0.01f);
        if (turbo < 0) return;
        turboTimes.RemoveAt(turbo);
        target.MakeTurbo(spawner.turboHits, spawner.turboHeal, spawner.turboScale, spawner.turboColor);
    }

    void OnDashed(PlayerMovement.Spot _) => dashed = true;

    /// <summary>A (left) or D (right), whichever the ship isn't on: the top spot sits under the banner.</summary>
    PlayerMovement.Spot SideSpot()
    {
        PlayerMovement.Spot current = movement != null ? movement.Current : PlayerMovement.Spot.Bottom;
        if (current == PlayerMovement.Spot.Left) return PlayerMovement.Spot.Right;
        if (current == PlayerMovement.Spot.Right) return PlayerMovement.Spot.Left;
        return Random.value < 0.5f ? PlayerMovement.Spot.Left : PlayerMovement.Spot.Right;
    }
    void OnShotFired(BeatJudgment judgment) => lastShot = judgment;

    bool RingShowing(ChartTarget target) => SongTime >= target.hitTime - Beat;
    float PerfectWindow => clock != null ? clock.perfectWindow : 0.07f;
    float GoodWindow => clock != null ? clock.goodWindow : 0.15f;

    void ClearAsteroids()
    {
        foreach (ChartTarget t in FindObjectsByType<ChartTarget>(FindObjectsSortMode.None))
            Destroy(t.gameObject);
        foreach (PowerUpPickup p in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None))
            Destroy(p.gameObject);
        spawner?.SetNotes(new List<ChartNote>());
    }

    // ------------------------------------------------------------------ time

    IEnumerator SlowDown() => SlowTo(slowScale);
    IEnumerator SpeedUp() => SlowTo(1f);

    /// <summary>Eases <see cref="Time.timeScale"/> to <paramref name="target"/> over <see cref="rampSeconds"/> (real time).</summary>
    IEnumerator SlowTo(float target)
    {
        float from = Time.timeScale;
        for (float t = 0f; t < rampSeconds; t += Time.unscaledDeltaTime)
        {
            SetTimeScale(Mathf.Lerp(from, target, t / rampSeconds));
            yield return null;
        }
        SetTimeScale(target);
    }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(0.01f, scale);
    }

    // ------------------------------------------------------------------ UI

    IEnumerator ShowFor(string title, string body, float seconds, Color? color = null)
    {
        ShowBanner(title, body, color);
        yield return new WaitForSecondsRealtime(seconds);
        HideBanner();
    }

    void ShowBanner(string title, string body, Color? color = null)
    {
        bannerTitle.text = title;
        bannerTitle.color = color ?? accentColor;
        bannerBody.text = body;
        banner.SetActive(true);
    }

    void HideBanner() => banner.SetActive(false);

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        HideBanner();
    }

    IEnumerator PowerUpStep()
    {
        yield return ShowFor("POWER-UPS", "Shields block hits for you. Here are two kinds.", 2.5f);
        yield return BasicShieldStep();
        yield return MovingShieldStep();
        yield return SlowMoStep();
    }

    IEnumerator BasicShieldStep()
    {
        CurrentStep = "powerup_basic";
        BasicShield basic = health != null ? health.GetComponent<BasicShield>() : null;
        if (powerUps == null || basic == null) yield break;

        for (int attempt = 0; attempt < maxTries; attempt++)
        {
            PowerUpPickup pickup = powerUps.SpawnBasic(health);
            if (pickup == null) yield break;

            ShowBanner("SHIELD PICKUP", "A glowing shield is flying in");
            float start = Time.time;
            yield return new WaitUntil(() => pickup == null || Time.time - start >= powerUps.basicFlightSeconds * 0.4f);

            if (pickup != null)
            {
                ShowBanner("SHOOT THE SHIELD!", "Click it to collect.");
                yield return SlowDown();
                yield return new WaitUntil(() => pickup == null);
            }

            bool got = basic.Active;
            if (got) ShowBanner("SHIELD UP!", "It takes the next hit for you", goodColor);
            else ShowBanner("ALMOST!", "Click the shield before it reaches you");
            yield return SpeedUp();
            yield return HideAfter(got ? 2.5f : 1.5f);
            if (got) yield break;
        }
    }

    IEnumerator MovingShieldStep()
    {
        CurrentStep = "powerup_moving";
        DirectionalShield shield = health != null ? health.GetComponent<DirectionalShield>() : null;
        if (powerUps == null || shield == null) yield break;

        for (int attempt = 0; attempt < maxTries && !shield.Active; attempt++)
        {
            PowerUpPickup pickup = powerUps.SpawnMoving(health, null, SideSpot());
            if (pickup == null) yield break;
            pickup.lifetime = 0f; 

            ShowBanner("MOVING SHIELD", "Press W A S D to go pick up the shield!");
            float giveUp = Time.unscaledTime + 12f;
            yield return new WaitUntil(() => shield.Active || pickup == null || Time.unscaledTime > giveUp);
            if (pickup != null) Destroy(pickup.gameObject);
        }

        if (!shield.Active) { HideBanner(); yield break; }

        ShowBanner("AIM IT", "This shield faces where you shoot! Aim at the incoming asteroids!", goodColor);
        var targets = new List<ChartTarget>();
        yield return SpawnWave(new[] { (2f, NoteLane.LeftUpper, false), (3.5f, NoteLane.RightUpper, false) }, targets);
        yield return WaitUntilCleared(targets, 0.5f);

        bool blocked = shield.BlocksLeft < shield.maxBlocks;
        if (blocked) ShowBanner("BLOCKED!", "The shield stops asteroids in the direction you aim", goodColor);
        else ShowBanner("NICE!", "Aim toward an asteroid and the shield turns with you");
        yield return HideAfter(2.5f);
    }
    IEnumerator SlowMoStep()
    {
        CurrentStep = "powerup_slowmo";
        SlowMo slowMo = health != null ? health.GetComponent<SlowMo>() : null;
        if (powerUps == null || slowMo == null) yield break;

        for (int attempt = 0; attempt < maxTries && !slowMo.Active; attempt++)
        {
            PowerUpPickup pickup = powerUps.SpawnSlowMo(health, SideSpot());
            if (pickup == null) yield break;
            pickup.lifetime = 0f;

            ShowBanner("SLOW-MO", "Press W A S D to grab the <color=#7df>BLUE</color> ball!");
            float giveUp = Time.unscaledTime + 12f;
            yield return new WaitUntil(() => slowMo.Active || pickup == null || Time.unscaledTime > giveUp);
            if (pickup != null) Destroy(pickup.gameObject);
        }

        if (!slowMo.Active) { HideBanner(); yield break; }

        // There's no song in the tutorial for slow-mo to slow, so slow the scene clock while it lasts.
        ShowBanner("SLOW-MO!", "Everything moves at half speed for a few seconds", goodColor);
        yield return SlowTo(slowMo.speed);
        var targets = new List<ChartTarget>();
        yield return SpawnWave(new[] { (1f, NoteLane.LeftUpper, false), (2f, NoteLane.RightUpper, false), (3f, NoteLane.LeftUpper, false) }, targets);
        yield return new WaitUntil(() => !slowMo.Active || targets.TrueForAll(t => t == null));
        yield return SpeedUp();
        yield return WaitUntilCleared(targets, 0.5f);
        HideBanner();
    }

    void BuildCanvas()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasObject = new GameObject("TutorialUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
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
        panel.rectTransform.anchoredPosition = new Vector2(0f, -90f);
        panel.rectTransform.sizeDelta = new Vector2(1240f, 170f);
        panel.color = panelColor;
        panel.raycastTarget = false;
        banner = panel.gameObject;
        bannerTitle = AddText(banner.transform, 58, FontStyle.Bold, new Vector2(0f, 34f));
        bannerBody = AddText(banner.transform, 30, FontStyle.Normal, new Vector2(0f, -38f));
        banner.SetActive(false);

        countdown = AddText(canvasObject.transform, 220, FontStyle.Bold, Vector2.zero);
        countdown.color = goodColor;
        countdown.rectTransform.sizeDelta = new Vector2(800f, 320f);
        countdown.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
        countdown.gameObject.SetActive(false);

        BuildSkipButton();
        BuildLivesHighlight();
    }

    /// <summary>A frame around the lives in the top left (same layout as <see cref="HealthHUD"/>), with a label.</summary>
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

        Text label = AddText(root, 44, FontStyle.Bold, Vector2.zero);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(size.x + 16f, 0f);
        label.rectTransform.sizeDelta = new Vector2(600f, 60f);
        label.alignment = TextAnchor.MiddleLeft;
        label.text = "<<  YOUR LIVES";
        label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);
        parts.Add(label);

        livesHighlightParts = parts.ToArray();
        livesHighlight.SetActive(false);
    }

    void ShowLivesHighlight(bool show)
    {
        if (livesHighlight != null) livesHighlight.SetActive(show);
    }

    void PulseLivesHighlight()
    {
        if (livesHighlight == null || !livesHighlight.activeSelf) return;
        Color c = goodColor;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        c.a = Mathf.Lerp(0.5f, 1f, pulse);
        livesHighlight.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, pulse);
        foreach (Graphic g in livesHighlightParts) g.color = c;
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
