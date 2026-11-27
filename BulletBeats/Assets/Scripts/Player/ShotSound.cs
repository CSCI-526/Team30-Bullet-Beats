using UnityEngine;

/// <summary>
/// Plays a laser "pew" on every Perfect and Good shot. Perfect is the full sound; Good is the same
/// sound, softer and quieter. Misses stay silent. The clips are generated in code, so there are no
/// audio files to import. The HIT SOUNDS toggle in Settings turns them on or off (on by default).
/// </summary>
[RequireComponent(typeof(PlayerShooter))]
public class ShotSound : MonoBehaviour
{
    public enum Style { RetroSquare, ClassicPew }

    public Style style = Style.ClassicPew;
    [Range(0f, 1f)] public float perfectVolume = 0.55f;
    [Range(0f, 1f)] public float goodVolume = 0.25f;

    const string EnabledPrefsKey = "HitSounds";
    /// <summary>Settings toggle, kept in PlayerPrefs so it survives reloads. On by default.</summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(EnabledPrefsKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(EnabledPrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    const int Rate = 44100;
    PlayerShooter shooter;
    AudioSource source;
    AudioClip perfectClip, goodClip;

    void Awake()
    {
        shooter = GetComponent<PlayerShooter>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        perfectClip = Build("pew_perfect", soft: false);
        goodClip = Build("pew_good", soft: true);
    }

    void OnEnable() { if (shooter != null) shooter.ShotFired += OnShot; }
    void OnDisable() { if (shooter != null) shooter.ShotFired -= OnShot; }

    void OnShot(BeatJudgment judgment)
    {
        if (!Enabled) return;
        if (judgment == BeatJudgment.Perfect) source.PlayOneShot(perfectClip, perfectVolume);
        else if (judgment == BeatJudgment.Good) source.PlayOneShot(goodClip, goodVolume);
    }

    AudioClip Build(string name, bool soft)
    {
        bool retro = style == Style.RetroSquare;
        float dur = retro ? 0.15f : 0.18f;
        float f0 = retro ? 1400f : 1800f, f1 = retro ? 200f : 300f;
        if (soft) { dur *= 0.8f; f0 *= 0.8f; }

        int n = (int)(Rate * dur);
        var data = new float[n];
        double phase = 0;
        float smooth = 0f, lowpass = soft ? 0.25f : 1f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = f1 + (f0 - f1) * Mathf.Exp(-3f * t / dur);
            phase += 2 * Mathf.PI * f / Rate;
            float s = Mathf.Sin((float)phase);
            if (retro) s = Mathf.Sign(s) * 0.5f;
            smooth += (s - smooth) * lowpass;  // soft = rounder edges
            float env = Mathf.Min(t / 0.002f, 1f) * Mathf.Exp(-t / (dur / 3f));
            data[i] = smooth * env;
        }
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
