using UnityEngine;

/// <summary>
/// Makes a <see cref="PowerUpPickup"/> look like a glowing star in its power-up's color (the
/// prefab's own mesh is hidden; its collider stays for shooting). <see cref="FlyTo"/> can also fly it
/// in from the middle of the screen to a W/A/S/D spot so it lands on a beat, with a shrinking ring
/// for the last beat like an asteroid's. It can't be shot while flying (it crosses the middle of the
/// screen, where the player aims at asteroids). The flight runs on <see cref="BeatClock.SongTime"/>,
/// so slow motion slows it too. Built from code, so it needs no art assets.
/// </summary>
[RequireComponent(typeof(PowerUpPickup))]
public class PickupStar : MonoBehaviour
{
    [Tooltip("Star radius in the pickup's local units.")]
    public float starRadius = 0.9f;
    [Tooltip("How far behind its spot (world units) the star starts its flight.")]
    public float flightDistance = 140f;
    [Tooltip("Song seconds the star waits at its spot after landing before it's gone.")]
    public float lingerSeconds = 3f;

    Color color = Color.white;
    Transform star, glow;
    LineRenderer ring;
    float pulseSeed;

    bool flying, shootable = true;
    Vector3 start, target;
    float startTime, landTime;

    static Mesh starMesh, quadMesh;
    static Texture2D glowTexture;

    BeatClock Clock => BeatClock.Instance;
    float SongTime => Clock != null ? Clock.SongTime : Time.time;

    /// <summary>Hides the prefab's mesh and shows a star of this color instead.</summary>
    public void Init(Color starColor)
    {
        color = starColor;
        foreach (Renderer r in GetComponents<Renderer>()) r.enabled = false;
        pulseSeed = Random.value * 10f;

        star = NewChild("Star", StarMesh(), Color.white, 3001).transform;
        star.localScale = Vector3.one * starRadius;
        TintStar();

        glow = NewChild("Glow", QuadMesh(), new Color(color.r, color.g, color.b, 0.55f), 3000).transform;
        glow.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = GlowTexture();
    }

    /// <summary>
    /// Flies the star in to <paramref name="target"/> (world), landing on the first beat at least
    /// <paramref name="minSeconds"/> of song time away. Returns false if there's no camera to fly from.
    /// </summary>
    public bool FlyTo(Vector3 target, float minSeconds)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        startTime = SongTime;
        landTime = startTime + minSeconds;
        if (Clock != null)
        {
            float beat = Clock.SecondsPerBeat;
            landTime = Clock.firstBeatOffset + Mathf.Ceil((landTime - Clock.firstBeatOffset) / beat) * beat;
        }

        // Start far away in the middle of the screen (where the asteroids come from) and travel out
        // to the spot, so the player sees it heading for that spot.
        this.target = target;
        start = cam.transform.position + cam.transform.forward * (Vector3.Distance(cam.transform.position, target) + flightDistance);
        flying = true;
        SetShootable(false);
        ring = NewRing();
        Place(0f);
        return true;
    }

    /// <summary>Makes a star already flying land at this exact song time.</summary>
    public void LandAt(float songTime) => landTime = songTime;

    void Update()
    {
        if (star == null) return;

        // Gentle pulse on the glow.
        float pulse = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime + pulseSeed) * 5f);
        glow.localScale = Vector3.one * starRadius * Mathf.Lerp(2.6f, 3.3f, pulse);

        if (!flying) return;
        float now = SongTime;
        float t = Mathf.InverseLerp(startTime, landTime, now);
        Place(t);
        if (t >= 1f) SetShootable(true);
        UpdateRing(landTime - now);
        if (now > landTime + lingerSeconds) Destroy(gameObject);
    }

    void LateUpdate()
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        if (cam == null || glow == null) return;
        Quaternion facing = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        glow.rotation = facing;
        if (ring != null) ring.transform.rotation = facing;
        // Face the player and turn flat (the pickup's own spin would show the star edge-on),
        // with a slight wobble so its facets catch the eye.
        float t = Time.unscaledTime + pulseSeed;
        star.rotation = facing * Quaternion.Euler(Mathf.Sin(t * 2f) * 18f, Mathf.Cos(t * 1.7f) * 18f, t * 70f);
    }

    void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
    }

    // Off while flying, so shots and aim assist go to the asteroids it passes in front of.
    void SetShootable(bool on)
    {
        if (shootable == on) return;
        shootable = on;
        foreach (Collider c in GetComponents<Collider>()) c.enabled = on;
    }

    void Place(float t) => transform.position = Vector3.LerpUnclamped(start, target, Mathf.Min(t, 1f));

    void UpdateRing(float untilLand)
    {
        if (ring == null) return;
        float beat = Clock != null ? Clock.SecondsPerBeat : 1f;
        bool show = untilLand >= 0f && untilLand <= beat;
        ring.enabled = show;
        if (!show) return;
        float worldStar = starRadius * transform.lossyScale.x;
        ring.transform.position = transform.position;
        ring.transform.localScale = Vector3.one * Mathf.Lerp(worldStar * 1.3f, worldStar * 3f, untilLand / beat);
    }

    // ------------------------------------------------------------------ building

    void TintStar()
    {
        // Light center, full color at the tips.
        Mesh mesh = star.GetComponent<MeshFilter>().sharedMesh;
        var colors = new Color[mesh.vertexCount];
        Color center = Color.Lerp(color, Color.white, 0.65f);
        for (int i = 0; i < colors.Length; i++) colors[i] = i < 2 ? center : color;
        Mesh tinted = Instantiate(mesh);
        tinted.colors = colors;
        star.GetComponent<MeshFilter>().sharedMesh = tinted;
    }

    GameObject NewChild(string name, Mesh mesh, Color tint, int queue)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Material material = NewMaterial();
        if (material != null)
        {
            material.color = tint;
            material.renderQueue = queue;
            renderer.sharedMaterial = material;
        }
        return go;
    }

    LineRenderer NewRing()
    {
        var go = new GameObject("StarRing");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        const int segments = 48;
        line.positionCount = segments;
        line.widthMultiplier = 0.2f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = NewMaterial();
        line.startColor = line.endColor = color;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }
        line.enabled = false;
        return line;
    }

    static Material NewMaterial()
    {
        // Same unlit shader as the asteroid rings, which is known to be in the web build.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        return shader != null ? new Material(shader) : null;
    }

    /// <summary>A chunky five-point star: two center points (front and back) joined to a ring of 10 points.</summary>
    static Mesh StarMesh()
    {
        if (starMesh != null) return starMesh;
        const int points = 5;
        const float inner = 0.45f, depth = 0.35f;
        var vertices = new Vector3[2 + points * 2];
        vertices[0] = new Vector3(0f, 0f, -depth);
        vertices[1] = new Vector3(0f, 0f, depth);
        for (int i = 0; i < points * 2; i++)
        {
            float a = Mathf.PI / 2f + i * Mathf.PI / points;
            float r = i % 2 == 0 ? 1f : inner;
            vertices[2 + i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
        }
        var triangles = new int[points * 2 * 6];
        for (int i = 0, n = points * 2; i < n; i++)
        {
            int a = 2 + i, b = 2 + (i + 1) % n;
            triangles[i * 6 + 0] = 0; triangles[i * 6 + 1] = b; triangles[i * 6 + 2] = a;
            triangles[i * 6 + 3] = 1; triangles[i * 6 + 4] = a; triangles[i * 6 + 5] = b;
        }
        starMesh = new Mesh { name = "Star", vertices = vertices, triangles = triangles };
        starMesh.RecalculateNormals();
        starMesh.RecalculateBounds();
        return starMesh;
    }

    static Mesh QuadMesh()
    {
        if (quadMesh != null) return quadMesh;
        quadMesh = new Mesh
        {
            name = "GlowQuad",
            vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        quadMesh.RecalculateBounds();
        return quadMesh;
    }

    /// <summary>Soft round glow: white, fading out from the middle.</summary>
    static Texture2D GlowTexture()
    {
        if (glowTexture != null) return glowTexture;
        const int size = 64;
        glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float fade = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, fade * fade);
            }
        glowTexture.SetPixels(pixels);
        glowTexture.Apply();
        return glowTexture;
    }
}
