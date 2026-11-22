using UnityEngine;

/// <summary>
/// Looks only: draws the bullet as a short glowing blaster bolt with a muzzle flash, instead of the old mesh blob.
/// Never touches movement, hits or damage.
/// </summary>
[RequireComponent(typeof(PlayerBullet))]
public class BulletVFX : MonoBehaviour
{
    [Tooltip("Additive, unlit material for the streak layers.")]
    public Material trailMaterial;

    struct Layer { public float width, length, startAlpha, endAlpha, whiteness; }

    // Blaster bolt: short, solid and thick so every shot reads as its own "pshoo".
    // Wide coloured glow, saturated body, white-hot core. The old mesh was ~2 units wide.
    static readonly Layer[] Layers = {
        new Layer { width = 3.2f,  length = 12f,  startAlpha = 0.35f, endAlpha = 0.08f, whiteness = 0f },   // glow
        new Layer { width = 1.4f,  length = 10f,  startAlpha = 1f,    endAlpha = 0.5f,  whiteness = 0.15f }, // body
        new Layer { width = 0.55f, length = 8.5f, startAlpha = 1f,    endAlpha = 0.6f,  whiteness = 1f },    // core
    };

    // Rounded front, full body, slight taper at the back: a capsule-like bolt.
    static readonly AnimationCurve BoltShape = new AnimationCurve(
        new Keyframe(0f, 0.55f), new Keyframe(0.12f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0.3f));

    LineRenderer[] lines;
    Layer[] layers;
    Vector3 spawn;

    void Start()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false; // hide old blob
        if (trailMaterial == null) return;

        spawn = transform.position;
        Color c = GetComponent<PlayerBullet>().TintColor;
        layers = Layers;
        lines = new LineRenderer[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            Layer L = layers[i];
            Color lc = Color.Lerp(c, Color.white, L.whiteness);
            var lr = MakeLine(transform, "Bolt" + i, i, trailMaterial);
            lr.widthCurve = BoltShape;
            lr.widthMultiplier = L.width;
            lr.startColor = new Color(lc.r, lc.g, lc.b, L.startAlpha);
            lr.endColor = new Color(lc.r, lc.g, lc.b, L.endAlpha);
            lines[i] = lr;
        }
        MuzzleFlash.Spawn(spawn, transform.forward, Color.Lerp(c, Color.white, 0.5f), trailMaterial);
        LateUpdate();
    }

    static LineRenderer MakeLine(Transform parent, string name, int order, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.sharedMaterial = mat;
        lr.numCapVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingOrder = order;
        lr.textureMode = LineTextureMode.Stretch;
        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture("_BaseMap", SoftTexture());
        lr.SetPropertyBlock(mpb);
        return lr;
    }

    /// <summary>A quick bright puff at the muzzle (~0.07 s) so each shot feels fired from the ship.</summary>
    class MuzzleFlash : MonoBehaviour
    {
        const float Duration = 0.07f;
        LineRenderer lr;
        Color color;
        float age;

        public static void Spawn(Vector3 at, Vector3 forward, Color color, Material mat)
        {
            var go = new GameObject("MuzzleFlash");
            var f = go.AddComponent<MuzzleFlash>();
            f.color = color;
            f.lr = MakeLine(go.transform, "Flash", 5, mat);
            f.lr.SetPosition(0, at + forward * 3f);
            f.lr.SetPosition(1, at - forward * 0.5f);
            f.lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0.4f));
            f.lr.widthMultiplier = 3.2f;
            f.Update();
        }

        void Update()
        {
            float t = age / Duration;
            if (t >= 1f) { Destroy(gameObject); return; }
            float a = 1f - t;
            lr.widthMultiplier = 3.2f * (0.7f + 0.3f * t);
            lr.startColor = new Color(color.r, color.g, color.b, 0.9f * a);
            lr.endColor = new Color(1f, 1f, 1f, a);
            age += Time.deltaTime;
        }
    }

    void LateUpdate()
    {
        if (lines == null) return;
        Vector3 head = transform.position;
        Vector3 back = -transform.forward;
        float travelled = Vector3.Distance(head, spawn); // never draw behind the muzzle
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i].SetPosition(0, head);
            lines[i].SetPosition(1, head + back * Mathf.Min(layers[i].length, travelled));
        }
    }

    static Texture2D softTex;
    /// <summary>Soft falloff across the line's width so nothing has hard edges.</summary>
    static Texture2D SoftTexture()
    {
        if (softTex != null) return softTex;
        const int h = 32;
        softTex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BulletSoft" };
        for (int y = 0; y < h; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            softTex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 1.6f)));
        }
        softTex.Apply();
        return softTex;
    }
}
