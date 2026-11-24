using UnityEngine;

/// <summary>
/// Hit and low-health effects on the ship (looks only). The ship keeps its own colours; on a hit it
/// flashes bright white for a moment, then eases back.
/// Low health glitches the ship (looks only): for <see cref="twoLivesGlitchTime"/> after a hit drops it to 2 or 1,
/// and on the last life it then turns slightly see-through (<see cref="lastLifeAlpha"/>). Tints every renderer on the ship
/// through a property block, so the shared material is untouched.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class ShipDamageFlash : MonoBehaviour
{
    [Tooltip("Hit flash colour. Values above 1 over-brighten the ship's material so it reads as a white-hot flash; the slight blue bias gives a cool cyan edge.")]
    [ColorUsage(false, true)] public Color hitFlashColor = new Color(3.2f, 3.8f, 4.2f);
    [Tooltip("Seconds for the hit flash to ease back to the health colour.")]
    public float hitFlashTime = 0.16f;

    [Header("Low-health glitch")]
    [Tooltip("Seconds the ship glitches after a hit drops it to 2 or 1 lives.")]
    public float twoLivesGlitchTime = 1f;
    [Tooltip("Glitch frames per second.")]
    public float glitchRate = 28f;
    [ColorUsage(false, true)] public Color glitchColor = new Color(0.4f, 2.2f, 2.6f);

    [Tooltip("Ship opacity while on its last life (1 = solid).")]
    [Range(0.1f, 1f)] public float lastLifeAlpha = 0.6f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    PlayerHealth health;
    Renderer[] renderers;
    Material[][] originalMaterials;
    Material fadeMaterial;
    bool faded;
    MaterialPropertyBlock block;
    float flashStart = float.NegativeInfinity;
    float glitchUntil = float.NegativeInfinity;
    bool flashing;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        renderers = GetComponentsInChildren<Renderer>();
        originalMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++) originalMaterials[i] = renderers[i].sharedMaterials;
        fadeMaterial = Resources.Load<Material>("ShipFade");
        block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.HealthChanged += OnHealthChanged;
        ApplyHealthTint();
    }

    void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.HealthChanged -= OnHealthChanged;
        SetFaded(false);
    }

    void OnDamaged(int _)
    {
        flashStart = Time.time;
        glitchUntil = health.Health == 2 || health.Health == 1 ? Time.time + hitFlashTime + twoLivesGlitchTime : float.NegativeInfinity;
    }

    void OnHealthChanged(int _)
    {
        if (!flashing) ApplyHealthTint();
    }

    bool OnLastLife => !health.IsDead && health.Health == 1;

    void Update()
    {
        SetFaded(OnLastLife && Time.time >= glitchUntil); // glitch first, then fade

        Color rest = Color.white; // no tint: the ship's own colours
        float t = hitFlashTime > 0f ? (Time.time - flashStart) / hitFlashTime : 1f;
        if (t < 1f)
        {
            float ease = 1f - (1f - t) * (1f - t); // fast out, smooth landing
            SetTint(Color.Lerp(hitFlashColor, rest, ease));
            flashing = true;
            return;
        }

        bool glitch = Time.time < glitchUntil;
        if (glitch)
        {
            SetTint(GlitchFrame(rest));
            flashing = true;
        }
        else if (flashing)
        {
            flashing = false;
            SetTint(rest);
        }
    }

    /// <summary>Swaps the ship's default (opaque) materials for a see-through copy, and back.
    /// Only renderers still on their original material are touched, so other effects keep theirs.</summary>
    void SetFaded(bool on)
    {
        if (on == faded || fadeMaterial == null) return;
        faded = on;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || renderers[i] is ParticleSystemRenderer || renderers[i] is LineRenderer) continue;
            if (on)
            {
                var mats = (Material[])originalMaterials[i].Clone();
                for (int m = 0; m < mats.Length; m++) mats[m] = fadeMaterial;
                renderers[i].sharedMaterials = mats;
            }
            else renderers[i].sharedMaterials = originalMaterials[i];
        }
        ApplyHealthTint();
    }

    /// <summary>Irregular flicker: mostly dark drop-outs, the odd cyan frame, otherwise normal.</summary>
    Color GlitchFrame(Color rest)
    {
        int frame = Mathf.FloorToInt(Time.time * glitchRate);
        float r = Mathf.Repeat(Mathf.Sin(frame * 12.9898f) * 43758.5453f, 1f);
        if (r < 0.35f) return new Color(rest.r * 0.15f, rest.g * 0.15f, rest.b * 0.15f, rest.a);
        if (r < 0.5f) return glitchColor;
        return rest;
    }

    void ApplyHealthTint() => SetTint(Color.white);

    void SetTint(Color color)
    {
        if (faded) color.a = lastLifeAlpha;
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(block);
    }
}
