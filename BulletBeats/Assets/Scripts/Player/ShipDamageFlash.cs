using UnityEngine;

/// <summary>
/// Shows the ship's health on the ship itself: blue when healthy, orange at <see cref="warningAt"/>
/// health or less, red at <see cref="dangerAt"/> (<see cref="ColorFor"/>, which the HUD pips use too),
/// and it blinks red while it can't be hit again after a hit. Tints every renderer on the ship
/// through a property block, so the shared material is untouched.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class ShipDamageFlash : MonoBehaviour
{
    [Tooltip("Ship tint above Warning At health.")]
    public Color healthyColor = new Color(0.35f, 0.6f, 1f);
    public Color warningColor = new Color(1f, 0.55f, 0.2f);
    [Tooltip("Orange at this much health or less.")]
    [Min(1)] public int warningAt = 2;
    public Color dangerColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Red at this much health or less.")]
    [Min(1)] public int dangerAt = 1;
    public Color flashColor = new Color(1f, 0.1f, 0.1f);
    [Tooltip("Blinks per second while the ship can't be hit again.")]
    public float blinkRate = 12f;
    [Tooltip("Minimum blink time, for when the hit invulnerability is shorter.")]
    public float minFlashTime = 0.3f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    PlayerHealth health;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    float flashUntil = float.NegativeInfinity;
    bool flashing;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        renderers = GetComponentsInChildren<Renderer>();
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
    }

    void OnDamaged(int _) => flashUntil = Time.time + Mathf.Max(minFlashTime, health.hitInvulnerableTime);

    void OnHealthChanged(int _)
    {
        if (Time.time >= flashUntil) ApplyHealthTint();
    }

    void Update()
    {
        if (Time.time < flashUntil)
        {
            flashing = true;
            bool red = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
            if (red) SetTint(flashColor);
            else ApplyHealthTint();
        }
        else if (flashing)
        {
            flashing = false;
            ApplyHealthTint();
        }
    }

    /// <summary>The colour for this much health: blue, orange or red.</summary>
    public Color ColorFor(int value)
    {
        if (value <= dangerAt) return dangerColor;
        if (value <= warningAt) return warningColor;
        return healthyColor;
    }

    void ApplyHealthTint() => SetTint(ColorFor(health.Health));

    void SetTint(Color color)
    {
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(block);
    }
}
