using System;
using UnityEngine;

/// <summary>
/// One extra hit on top of the ship's health. <see cref="PlayerHealth.TakeDamage"/> breaks it before
/// any health is lost. While it's up, a thin ring in the shield's color circles the ship, facing the
/// camera. Granted by a <see cref="PowerUpPickup"/>. The ring is built from code, so it needs no art.
/// </summary>
public class BasicShield : MonoBehaviour
{
    public Color ringColor = new Color(0.75f, 0.5f, 1f);
    [Tooltip("Ring radius in world units. 0 = fit it around the ship's meshes.")]
    public float ringRadius = 0f;
    [Tooltip("When Ring Radius is 0: how much bigger than the ship the ring is.")]
    public float ringPadding = 1.25f;
    [Tooltip("Ring thickness in world units.")]
    public float ringWidth = 0.15f;

    /// <summary>Raised when the shield goes up or breaks, with the new state.</summary>
    public event Action<bool> Changed;

    public bool Active { get; private set; }

    LineRenderer ring;
    Vector3 localCenter;
    float radius = 1f;

    void Start()
    {
        FitToShip();
        ring = BuildRing();
        ShowRing();
    }

    void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
    }

    /// <summary>Raises the shield. Does nothing if it's already up.</summary>
    public void Grant()
    {
        if (Active) return;
        Active = true;
        ShowRing();
        Changed?.Invoke(true);
    }

    /// <returns>True if the shield took the hit (and broke).</returns>
    public bool TryAbsorb()
    {
        if (!Active) return false;
        Active = false;
        ShowRing();
        Changed?.Invoke(false);
        return true;
    }

    // The ring isn't a child of the ship, so it doesn't count toward the ship's bounds
    // (PowerUpSpawner.ShipCenter) and doesn't scale with it. It follows the ship here instead.
    void LateUpdate()
    {
        if (ring == null || !ring.enabled) return;
        ring.transform.position = transform.TransformPoint(localCenter);
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam != null) ring.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        float pulse = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 6f);
        ring.transform.localScale = Vector3.one * radius * pulse;
    }

    void ShowRing()
    {
        if (ring != null) ring.enabled = Active;
    }

    /// <summary>Centers the ring on the ship's visible body (its origin sits off to the side of the mesh).</summary>
    void FitToShip()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        Vector3 center = found ? bounds.center : transform.position;
        localCenter = transform.InverseTransformPoint(center);
        float fit = found ? Mathf.Max(bounds.extents.x, bounds.extents.y) * ringPadding : 2f;
        radius = ringRadius > 0f ? ringRadius : fit;
    }

    LineRenderer BuildRing()
    {
        var go = new GameObject("ShieldRing");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        const int segments = 64;
        line.positionCount = segments;
        line.widthMultiplier = ringWidth;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        // Same unlit shader as the star and asteroid rings, which is known to be in the web build.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) line.material = new Material(shader);
        line.startColor = line.endColor = ringColor;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        line.enabled = false;
        return line;
    }
}
