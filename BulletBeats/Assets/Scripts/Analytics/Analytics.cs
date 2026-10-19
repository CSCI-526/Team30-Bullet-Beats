using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Collections;

public class Analytics : MonoBehaviour
{
    const string url = "https://docs.google.com/forms/d/e/1FAIpQLSdO_9eMBvuDOB7bRShcifXLsajxRsKS4SuN_Gp4Mv69vslBuw/formResponse";

    [Header("Testing")]
    [Tooltip("Editor Play mode only sends dada when this is ticked. Leave OFF when committing. WebGL builds will always send.")]
    [SerializeField] private bool sendInEditor = false;

    public static Analytics Instance;

    string sessionId;
    float levelStartTime;
    int attempt = 1;

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
        sessionId = System.Guid.NewGuid().ToString().Substring(0,8);
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

    public void LogShot(string judgement) => Log("shot", judgement);

    public void LogDodge(string fromLane, string toLane) => Log("dodge", fromLane, toLane);

    public void LogDeath(string cause = "") => Log("death", cause);

    public void LogLevelEnd(bool won)
    {
        Log("level_end", won ? "win" : "loss", attempt.ToString());
        attempt++;
    }

    void Log(string evt, string d1 = "", string d2 = "", string d3 = "")
    {
        Logged?.Invoke(evt, LevelTime, d1, d2, d3);
        if (!isActiveAndEnabled) return; //disable the component to stop sending (e.g. for tests)
#if UNITY_EDITOR
        if (!sendInEditor) return;
#endif
        StartCoroutine(Send(evt, LevelTime, d1, d2, d3));
    }

    static string Fill(string s) => string.IsNullOrEmpty(s) ? "-" : s;

    IEnumerator Send(string evt, float levelTime, string d1, string d2, string d3)
    {
        WWWForm form = new WWWForm();
        form.AddField("entry.1397884980", sessionId);
        form.AddField("entry.953471315", evt);
        form.AddField("entry.586014973", "MainLevel");
        form.AddField("entry.688079236", levelTime.ToString("F2"));
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