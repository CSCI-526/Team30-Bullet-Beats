using UnityEngine;

/// <summary>
/// Shows the ship's health on the ship itself: it is tinted by how damaged it is
/// (<see cref="healthColors"/>, full health first) and blinks red while it can't be hit again after a
/// hit. Tints every renderer on the ship through a property block, so the shared material is untouched.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class ShipDamageFlash : MonoBehaviour
{
    [Tooltip("Ship tint from full health (first) to 1 health left (last). Empty = keep the material's colour.")]
    public Color[] healthColors =
    {
        new Color(0.35f, 0.6f, 1f),
        new Color(0.35f, 0.9f, 0.45f),
        new Color(1f, 0.9f, 0.3f),
        new Color(1f, 0.55f, 0.2f),
        new Color(1f, 0.25f, 0.25f)
    };
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
    bool tinted;

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

    void ApplyHealthTint()
    {
        if (healthColors == null || healthColors.Length == 0)
        {
            ClearTint();
            return;
        }

        int lost = health.maxHealth - health.Health;
        float t = health.maxHealth > 1 ? Mathf.Clamp01(lost / (float)(health.maxHealth - 1)) : 1f;
        SetTint(Sample(t));
    }

    Color Sample(float t)
    {
        if (healthColors.Length == 1) return healthColors[0];
        float scaled = t * (healthColors.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(scaled), healthColors.Length - 2);
        return Color.Lerp(healthColors[i], healthColors[i + 1], scaled - i);
    }

    void SetTint(Color color)
    {
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(block);
        tinted = true;
    }

    void ClearTint()
    {
        if (!tinted) return;
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(null);
        tinted = false;
    }
}
