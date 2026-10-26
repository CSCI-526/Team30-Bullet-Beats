using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 0: a guided run that plays like the real game. Asteroids fly in, and when one needs the
/// player to act, time slows almost to a stop and an instruction appears; it speeds back up once
/// they do it. Steps: shoot on the ring, PERFECT heals, dodge, turbo asteroids, then a short run at
/// full speed. Ends with a 3-2-1 countdown into the first level on Easy. The ship can't die here.
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
    bool dashed;
    float baseFixedDeltaTime;
    Coroutine ramp;

    GameObject canvasObject, banner;
    Text bannerTitle, bannerBody, countdown;
    Font font;

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
        if (clock != null)
        {
            clock.bpm = tutorialBpm;
            clock.ResetBeatTracking(); // the scene clock starts now
        }
        if (spawner != null)
        {
            spawner.noteStride = 1;
            spawner.turboEveryN = 0; // turbo asteroids only where a step asks for one
            spawner.Spawned += OnSpawned;
        }

        BuildCanvas();
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (movement != null) movement.Dashed -= OnDashed;
        if (spawner != null) spawner.Spawned -= OnSpawned;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Skip();
    }

    /// <summary>Ends the tutorial early: clears the asteroids and goes to the countdown.</summary>
    public void Skip()
    {
        if (IsFinishing) return;
        StopAllCoroutines();
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
        yield return PerfectStep();
        yield return DodgeStep();
        yield return TurboStep();
        yield return PracticeStep();

        CurrentStep = "done";
        Analytics.Instance?.LogTutorial("complete", CurrentStep);
        yield return ShowFor("YOU'RE READY!", "Next up: the first level on Easy", 2f, goodColor);
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

            ShowBanner("SHOOT!", "Aim at the asteroid and CLICK when the ring is smallest");
            yield return SlowDown();
            yield return new WaitUntil(() => target == null || SongTime > target.hitTime + 0.4f);

            bool hit = target == null;
            if (hit) ShowBanner("NICE SHOT!", "The closer to the ring's smallest point, the better", goodColor);
            else
            {
                Destroy(target.gameObject);
                ShowBanner("ALMOST!", "Try again - click while the ring is showing");
            }
            yield return SpeedUp();
            yield return HideAfter(hit ? 2f : 1.5f);
            if (hit) yield break;
        }
    }

    IEnumerator PerfectStep()
    {
        CurrentStep = "perfect";
        ShowBanner("PERFECT = +1 LIFE", "PERFECT shots (right as the ring closes) give you a life.  Hit these three!");
        yield return SlowTo(0.45f);
        var targets = new List<ChartTarget>();
        yield return SpawnWave(new[] { (2f, NoteLane.LeftUpper, false), (3.5f, NoteLane.RightUpper, false), (5f, NoteLane.LeftUpper, false) }, targets);
        yield return WaitUntilCleared(targets, 0.5f);
        yield return SpeedUp();
        HideBanner();
    }

    IEnumerator DodgeStep()
    {
        CurrentStep = "dodge";
        if (shooter != null) shooter.enabled = false; // this one has to be dodged
        ShowBanner("DODGE!", "You can't shoot everything.  Watch this one...");

        ChartTarget target = null;
        yield return SpawnOne(NoteLane.LeftUpper, false, t => target = t);
        // Past its ring an asteroid rushes the ship, so slow down while it's still at the ring.
        yield return new WaitUntil(() => target == null || SongTime >= target.hitTime - 0.6f * Beat);

        dashed = false;
        ShowBanner("DODGE!", "Press  W  A  S  D  to jump to another spot");
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

            ShowBanner("TURBO ASTEROID", "Orange ones need 2 HITS.  Destroy it for +2 LIVES!");
            yield return SlowTo(slowScale * 1.5f);
            yield return new WaitUntil(() => target == null || SongTime > target.hitTime + 0.6f);

            bool hit = target == null;
            if (hit) ShowBanner("+2 LIVES!", "Turbo asteroids are worth the extra shot", goodColor);
            else
            {
                Destroy(target.gameObject);
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
        yield return ShowFor("NOW ON YOUR OWN!", "Shoot on the ring, dodge what you can't hit", 2f, goodColor);
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

    IEnumerator SpawnOne(NoteLane lane, bool turbo, System.Action<ChartTarget> got)
    {
        var list = new List<ChartTarget>();
        yield return SpawnWave(new[] { (2f, lane, turbo) }, list);
        got(list.Count > 0 ? list[0] : null);
    }

    /// <summary>Queues notes (beats from now, lane, turbo) and waits until they have all spawned.</summary>
    IEnumerator SpawnWave((float beats, NoteLane lane, bool turbo)[] wave, List<ChartTarget> into)
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
        int turbo = turboTimes.FindIndex(time => Mathf.Abs(time - target.hitTime) < 0.01f);
        if (turbo < 0) return;
        turboTimes.RemoveAt(turbo);
        target.MakeTurbo(spawner.turboHits, spawner.turboHeal, spawner.turboScale, spawner.turboColor);
    }

    void OnDashed(PlayerMovement.Spot _) => dashed = true;

    bool RingShowing(ChartTarget target) => SongTime >= target.hitTime - Beat;

    void ClearAsteroids()
    {
        foreach (ChartTarget t in FindObjectsByType<ChartTarget>(FindObjectsSortMode.None))
            Destroy(t.gameObject);
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
