using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Globalization;

/// <summary>
/// Posts one Google Form response per event. The form's columns stay the same; new values are packed
/// with "|" and unpacked by Analytics/visualize_analytics.py:
/// session = player-session, level = song|difficulty|attempt.
/// Events: level_start, asteroid (one per asteroid, when it ends), dodge, star, level_end, tutorial.
/// </summary>
public class Analytics : MonoBehaviour
{
    const string url = "https://docs.google.com/forms/d/e/1FAIpQLSdO_9eMBvuDOB7bRShcifXLsajxRsKS4SuN_Gp4Mv69vslBuw/formResponse";
    const string PlayerIdKey = "BulletBeats.AnalyticsPlayer";
    const string AttemptKeyPrefix = "BulletBeats.Attempt.";

    [Header("Testing")]
    [Tooltip("Editor Play mode only sends dada when this is ticked. Leave OFF when committing. WebGL builds will always send.")]
    [SerializeField] private bool sendInEditor = false;

    public static Analytics Instance;

    string playerId;
    string sessionId;
    float levelStartTime;
    int attempt = 1;
    string levelContext = "-";

    /// <summary>Raised for every logged event (event, level time, data 1-3), even when nothing is sent.</summary>
    public static event System.Action<string, float, string, string, string> Logged;

    //Time since level started: the song's playback time once a song is playing (so the
    //count-in isn't counted; when the song finishes it keeps the last time), otherwise
    //time since the scene loaded
    public float LevelTime
    {
        get
        {
            BeatClock clock = BeatClock.Instance;
            if (clock == null || !clock.HasMusic) return Time.time - levelStartTime;
            float songTime = clock.SongTime;
            if (songTime > lastSongTime) lastSongTime = songTime;
            return lastSongTime;
        }
    }

    float lastSongTime;

    void Awake()
    {
        if (Instance != null) {Destroy(gameObject); return;}

        Instance = this;
        DontDestroyOnLoad(gameObject);
        sessionId = NewId();
        playerId = PlayerPrefs.GetString(PlayerIdKey, "");
        if (string.IsNullOrEmpty(playerId))
        {
            playerId = NewId();
            PlayerPrefs.SetString(PlayerIdKey, playerId);
            PlayerPrefs.Save();
        }
        levelStartTime = Time.time;
        SceneManager.sceneLoaded += OnSceneLoaded;

    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Keep the last song time current, so an event right after the song ends still gets it.
    void Update() => _ = LevelTime;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        levelStartTime = Time.time; //reset clock on restart
        lastSongTime = 0f;
    }

    static string NewId() => System.Guid.NewGuid().ToString().Substring(0, 8);

    static string Num(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Starts a run: counts this attempt at the level (kept across reloads) and tags every later row with it.</summary>
    public void LogLevelStart(string levelId, string difficulty, bool holdToFire, float aimAssist, bool tutorialSeen)
    {
        levelId = string.IsNullOrEmpty(levelId) ? "unknown" : levelId.Replace("|", "-");
        string key = AttemptKeyPrefix + levelId;
        attempt = PlayerPrefs.GetInt(key, 0) + 1;
        PlayerPrefs.SetInt(key, attempt);
        PlayerPrefs.Save();
        levelContext = levelId + "|" + difficulty + "|" + attempt;
        Log("level_start", holdToFire ? "1" : "0", Num(aimAssist), tutorialSeen ? "seen" : "new");
    }

    /// <summary>One row per asteroid. <paramref name="hitTime"/> is its beat, so the row's time is the same for every player.</summary>
    public void LogAsteroid(float hitTime, string type, string outcome, string shipSpot, float firstOffset, int tries, bool slowMo)
    {
        string offsetMs = float.IsNaN(firstOffset) ? "-" : Mathf.RoundToInt(firstOffset * 1000f).ToString(CultureInfo.InvariantCulture);
        Log("asteroid", hitTime, type + "|" + outcome, shipSpot, offsetMs + "|" + tries + "|" + (slowMo ? "1" : "0"));
    }

    public void LogDodge(string fromLane, string toLane) => Log("dodge", fromLane, toLane);

    /// <summary>One row per power-up star, when it's collected or disappears.</summary>
    public void LogStar(string kind, string spot, bool collected) => Log("star", kind, spot, collected ? "collected" : "expired");

    /// <summary>Tutorial result: "complete" or "skipped", and the step it ended on.</summary>
    public void LogTutorial(string result, string step) => Log("tutorial", result, step);

    /// <param name="emptyClicks">Shots fired at nothing during the run.</param>
    public void LogLevelEnd(bool won, int emptyClicks) => Log("level_end", won ? "win" : "loss", attempt.ToString(), emptyClicks.ToString());

    void Log(string evt, string d1 = "", string d2 = "", string d3 = "") => Log(evt, LevelTime, d1, d2, d3);

    void Log(string evt, float time, string d1, string d2, string d3)
    {
        Logged?.Invoke(evt, time, d1, d2, d3);
        if (!isActiveAndEnabled) return; //disable the component to stop sending (e.g. for tests)
#if UNITY_EDITOR
        if (!sendInEditor) return;
#endif
        StartCoroutine(Send(evt, time, d1, d2, d3, levelContext));
    }

    static string Fill(string s) => string.IsNullOrEmpty(s) ? "-" : s;

    IEnumerator Send(string evt, float levelTime, string d1, string d2, string d3, string level)
    {
        WWWForm form = new WWWForm();
        form.AddField("entry.1397884980", playerId + "-" + sessionId);
        form.AddField("entry.953471315", evt);
        form.AddField("entry.586014973", level);
        form.AddField("entry.688079236", levelTime.ToString("F2", CultureInfo.InvariantCulture));
        form.AddField("entry.713456091", Fill(d1));
        form.AddField("entry.677601856", Fill(d2));
        form.AddField("entry.1545569130", Fill(d3));

        using (UnityWebRequest req = UnityWebRequest.Post(url, form))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.Log("Analytics error (in WebGL this can appear even when the row arrives): " + req.error);
            else
                Debug.Log("Analytics sent: " + evt);
        }
    }
}
