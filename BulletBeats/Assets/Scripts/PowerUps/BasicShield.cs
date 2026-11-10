using System;
using UnityEngine;

/// <summary>
/// One extra hit on top of the ship's health. <see cref="PlayerHealth.TakeDamage"/> breaks it before
/// any health is lost. While it's up, a glowing bubble in the shield's color surrounds the ship, facing
/// the camera. Granted by a <see cref="PowerUpPickup"/>. The bubble is built from code, so it needs no art.
/// </summary>
public class BasicShield : MonoBehaviour
{
    public Color ringColor = new Color(0.75f, 0.5f, 1f);
    [Tooltip("Ring radius in world units. 0 = fit it around the ship's meshes.")]
    public float ringRadius = 0f;
    [Tooltip("When Ring Radius is 0: how much bigger than the ship the ring is.")]
    public float ringPadding = 1.25f;
    [Tooltip("Ring thickness in world units.")]
    public float ringWidth = 0.45f;
    [Tooltip("How strongly the inside of the bubble is tinted, 0 to 1.")]
    [Range(0f, 1f)] public float bubbleTint = 0.18f;

    /// <summary>Raised when the shield goes up or breaks, with the new state.</summary>
    public event Action<bool> Changed;

    public bool Active { get; private set; }

    LineRenderer ring;
    MeshRenderer bubble;
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
        if (bubble != null) bubble.enabled = Active;
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
        bubble = BuildBubble(go.transform, line.material);
        return line;
    }

    /// <summary>A disc that is faint in the middle, brightest at the rim, with a soft glow past it.</summary>
    MeshRenderer BuildBubble(Transform parent, Material material)
    {
        float[] radii = { 0f, 0.7f, 0.96f, 1f, 1.22f };
        float[] alphas = { bubbleTint * 0.4f, bubbleTint, bubbleTint * 3f, 0.75f, 0f };
        const int segments = 64;
        var vertices = new Vector3[radii.Length * segments];
        var colors = new Color[vertices.Length];
        var triangles = new int[(radii.Length - 1) * segments * 6];
        int tri = 0;
        for (int r = 0; r < radii.Length; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                int i = r * segments + s;
                vertices[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radii[r];
                colors[i] = new Color(ringColor.r, ringColor.g, ringColor.b, alphas[r]);
                if (r == radii.Length - 1) continue;
                int next = r * segments + (s + 1) % segments;
                triangles[tri++] = i;
                triangles[tri++] = i + segments;
                triangles[tri++] = next + segments;
                triangles[tri++] = i;
                triangles[tri++] = next + segments;
                triangles[tri++] = next;
            }
        }

        var mesh = new Mesh { name = "ShieldBubble", vertices = vertices, colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        var go = new GameObject("Bubble", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var view = go.GetComponent<MeshRenderer>();
        view.sharedMaterial = material;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.enabled = false;
        return view;
    }
}
