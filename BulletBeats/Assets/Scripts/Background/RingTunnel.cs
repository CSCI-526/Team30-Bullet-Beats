using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game backdrop: big, rough metal rings that fly toward the camera so the ship feels like it is
/// travelling down an endless tunnel, in front of a softly lit blue sky. The tunnel gently bends
/// (far rings drift sideways) so it reads as a moving path rather than a static picture.
/// Clarity rules: rings sit outside the play area (the hole is wider than where asteroids fly),
/// the colors are muted grey-blue, and far rings fade into the sky color instead of using scene
/// fog, so asteroids are never fogged or tinted. The existing star streaks stay; the sky is
/// drawn additively on top of them.
/// </summary>
public class RingTunnel : MonoBehaviour
{
    [Header("Rings")]
    public int ringCount = 7;
    public float ringRadius = 270f;       // inner radius; asteroids fly well inside this
    public float spacing = 230f;
    public float farDistance = 1400f;     // the star sphere is enlarged to fit this
    public float speed = 70f;             // units per second toward the camera
    public int chunksPerRing = 44;

    [Header("Look")]
    public Color rockNear = new Color(0.21f, 0.23f, 0.28f);
    public Color skyTint = new Color(0.06f, 0.09f, 0.17f);
    public float bend = 60f;              // how far the far end of the tunnel drifts sideways

    struct Ring { public Transform t; public MeshRenderer[] renderers; public float spin; }

    readonly List<Ring> rings = new List<Ring>();
    MaterialPropertyBlock block;
    Camera cam;
    Vector3 origin;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindFirstObjectByType<RingTunnel>() != null) return;
        if (GameObject.Find("skysphere") == null) return; // only the gameplay scene
        new GameObject("RingTunnel").AddComponent<RingTunnel>();
    }

    void Start()
    {
        cam = DisplayCamera.Find();
        if (cam == null) cam = Camera.main;
        origin = cam != null ? cam.transform.position : new Vector3(0f, 1f, -100f);
        block = new MaterialPropertyBlock();

        Material rock = Resources.Load<Material>("Tunnel/TunnelRock");
        Material lights = Resources.Load<Material>("Tunnel/TunnelLights");
        Material sky = Resources.Load<Material>("Tunnel/TunnelSky");

        // Enlarge the star sphere so the tunnel has room to stretch into the distance.
        GameObject stars = GameObject.Find("skysphere");
        if (stars != null) stars.transform.localScale *= 3f;
        foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.farClipPlane = Mathf.Max(c.farClipPlane, 2200f);

        BuildSky(sky);

        var rng = new System.Random(526);
        Mesh[] variants = { BuildRingMesh(rng), BuildRingMesh(rng), BuildRingMesh(rng) };
        for (int i = 0; i < ringCount; i++)
        {
            var go = new GameObject("Ring" + i);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = variants[i % variants.Length];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { rock, lights };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, i * 37f);
            rings.Add(new Ring
            {
                t = go.transform,
                renderers = new[] { mr },
                spin = (i % 2 == 0 ? 1f : -1f) * (1.5f + i % 3),
            });
            Place(rings[i], farDistance - i * spacing);
        }
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        foreach (Ring r in rings)
        {
            float d = r.t.position.z - origin.z - step;
            if (d < -80f) d += ringCount * spacing; // recycle to the far end
            r.t.Rotate(0f, 0f, r.spin * Time.deltaTime, Space.Self);
            Place(r, d);
        }
    }

    void Place(Ring r, float d)
    {
        // The tunnel bends slowly: the further away, the more it drifts, so it feels like a path.
        float t = Time.time;
        float k = Mathf.Clamp01(d / farDistance);
        k *= k;
        Vector3 drift = new Vector3(Mathf.Sin(t * 0.21f) * bend, Mathf.Sin(t * 0.17f + 1.3f) * bend * 0.6f, 0f) * k;
        r.t.position = new Vector3(origin.x, origin.y, origin.z + d) + drift;

        // Fade far rings into the sky (no fog, so asteroids stay crisp).
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(250f, farDistance, d));
        block.SetColor(BaseColor, Color.Lerp(rockNear, skyTint, fade));
        r.renderers[0].SetPropertyBlock(block, 0);
        block.SetColor(BaseColor, Color.Lerp(new Color(0.55f, 0.42f, 0.25f), skyTint, Mathf.Max(fade, 0.25f)));
        r.renderers[0].SetPropertyBlock(block, 1);
    }

    // ---------- Ring mesh: chunky, uneven stone-metal blocks with a few dim lit panels ----------

    Mesh BuildRingMesh(System.Random rng)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var rockTris = new List<int>();
        var lightTris = new List<int>();
        float R = ringRadius;
        float arc = 2f * Mathf.PI / chunksPerRing;

        for (int i = 0; i < chunksPerRing; i++)
        {
            float a = i * arc + Rand(rng, -0.04f, 0.04f);
            float thick = Rand(rng, 14f, 26f);
            float depth = Rand(rng, 20f, 34f);
            float inner = R + Rand(rng, -3f, 4f);
            float len = R * arc * Rand(rng, 0.92f, 1.08f);
            Vector3 center = Dir(a) * (inner + thick * 0.5f);
            Quaternion rot = Quaternion.Euler(Rand(rng, -4f, 4f), Rand(rng, -4f, 4f), a * Mathf.Rad2Deg - 90f);
            AddBox(verts, norms, rockTris, center, rot, new Vector3(len, thick, depth));

            // Outer rubble band: smaller blocks stacked on the outside for a rocky silhouette.
            if (rng.NextDouble() < 0.5)
            {
                float h = Rand(rng, 6f, 14f);
                Vector3 c2 = Dir(a + Rand(rng, -0.3f, 0.3f) * arc) * (inner + thick + h * 0.4f);
                AddBox(verts, norms, rockTris, c2, rot * Quaternion.Euler(0, 0, Rand(rng, -12f, 12f)),
                    new Vector3(len * Rand(rng, 0.4f, 0.8f), h, depth * Rand(rng, 0.5f, 0.9f)));
            }

            // A few dim, warm panels on the face toward the camera.
            if (rng.NextDouble() < 0.18)
            {
                Vector3 c3 = center + rot * new Vector3(0f, Rand(rng, -thick * 0.25f, thick * 0.25f), -depth * 0.5f - 0.4f);
                AddBox(verts, norms, lightTris, c3, rot, new Vector3(len * 0.35f, 2.2f, 0.6f));
            }
        }

        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.subMeshCount = 2;
        m.SetTriangles(rockTris, 0);
        m.SetTriangles(lightTris, 1);
        m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * (R * 2.6f)); // never culled while on screen
        return m;
    }

    static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
    static float Rand(System.Random r, float lo, float hi) => lo + (float)r.NextDouble() * (hi - lo);

    static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    static void AddBox(List<Vector3> v, List<Vector3> n, List<int> tris, Vector3 c, Quaternion q, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        foreach (Vector3 f in FaceN)
        {
            Vector3 u = f.x != 0 ? Vector3.up : Vector3.right;
            Vector3 w = Vector3.Cross(f, u);
            int b = v.Count;
            for (int k = 0; k < 4; k++)
            {
                float su = (k == 0 || k == 3) ? -1 : 1, sw = (k < 2) ? -1 : 1;
                Vector3 p = Vector3.Scale(f + u * su + w * sw, h);
                v.Add(c + q * p);
                n.Add(q * f);
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    // ---------- Sky: soft, low-detail blue glow added over the star streaks ----------

    void BuildSky(Material sky)
    {
        if (sky == null) return;
        const int W = 256, H = 144;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float u = (float)x / W, v = (float)y / H;
            float dx = (u - 0.5f) * 1.6f, dy = (v - 0.47f) * 1.9f;
            float glow = Mathf.Exp(-(dx * dx + dy * dy) * 3.2f);
            float clouds = Fbm(u * 5f, v * 3f);
            float band = Mathf.Exp(-Mathf.Pow((v - 0.45f) * 4f, 2f)); // light low across the middle
            float c = glow * 0.30f + clouds * (0.16f + band * 0.16f);
            // Darker toward the edges so the rings and screen edges stay calm.
            c *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(dx) * 1.2f));
            px[y * W + x] = new Color(c * 0.55f, c * 0.72f, c * 1.05f, 1f);
        }
        tex.SetPixels(px);
        tex.Apply();

        var mat = new Material(sky) { mainTexture = tex };
        mat.SetTexture("_BaseMap", tex);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "TunnelSky";
        quad.transform.SetParent(transform, false);
        quad.transform.position = origin + new Vector3(0f, 0f, farDistance + 15f);
        quad.transform.localScale = new Vector3(3600f, 2000f, 1f);
        var qr = quad.GetComponent<MeshRenderer>();
        qr.sharedMaterial = mat;
        qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static float Fbm(float x, float y)
    {
        float s = 0f, a = 0.5f;
        for (int i = 0; i < 4; i++) { s += a * Mathf.PerlinNoise(x + 13.1f * i, y + 7.7f * i); x *= 2f; y *= 2f; a *= 0.5f; }
        return Mathf.Clamp01((s - 0.35f) * 1.6f);
    }
}
