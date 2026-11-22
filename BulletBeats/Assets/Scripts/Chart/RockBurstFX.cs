using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Looks only: a short burst where a shot asteroid was destroyed. Impact flash (~0.07 s), a few thin
/// light rays (~0.12 s), small shards that are tiny copies of the rock itself, and a few sparks,
/// all gone in ~0.35 s. Listens to <see cref="ChartTarget.Ended"/>, so it never changes when or how
/// an asteroid is destroyed.
/// </summary>
public class RockBurstFX : MonoBehaviour
{
    const float Lifetime = 0.4f;
    const int ShardCount = 6, RayCount = 5, SparkCount = 6;

    static readonly Color PerfectGold = new Color(1f, 0.835f, 0.302f);
    static readonly Color GoodCyan = new Color(0.043f, 0.898f, 1f);
    static readonly Color Ember = new Color(1f, 0.54f, 0f); // Impact Orange

    static RockBurstFX instance;
    static Material additive;
    static Texture2D softDot;

    readonly Stack<Burst> pool = new Stack<Burst>();

    class Burst
    {
        public GameObject root;
        public ParticleSystem flash, rays, shards, sparks;
        public float until;
    }
    readonly List<Burst> live = new List<Burst>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        additive = Resources.Load<Material>("VFX/BurstAdditive");
        if (additive == null) return;
        var go = new GameObject("RockBurstFX");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RockBurstFX>();
        ChartTarget.Ended += instance.OnEnded;
    }

    void OnDestroy() => ChartTarget.Ended -= OnEnded;

    void OnEnded(ChartTarget target, ChartTarget.Outcome outcome)
    {
        if (target == null) return;
        if (outcome != ChartTarget.Outcome.Perfect && outcome != ChartTarget.Outcome.Good) return;
        Play(target, outcome == ChartTarget.Outcome.Perfect ? PerfectGold : GoodCyan);
    }

    void Play(ChartTarget target, Color judgment)
    {
        var mr = target.GetComponentInChildren<MeshRenderer>();
        var mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
        float r = mr != null ? mr.bounds.extents.magnitude * 0.7f : 1f; // rough rock radius
        Vector3 pos = mr != null ? mr.bounds.center : target.transform.position;

        Burst b = pool.Count > 0 ? pool.Pop() : Create();
        b.root.transform.position = pos;
        b.root.SetActive(true);

        // 1. Impact flash: one soft white dot with a judgment-coloured rim, very short.
        Emit(b.flash, pos, Vector3.zero, r * 1.8f, Color.Lerp(judgment, Color.white, 0.75f), 0.09f);

        // 1b. Small light blast: a soft coloured glow that swells and fades behind the shards.
        Color blast = Color.Lerp(judgment, Color.white, 0.3f); blast.a = 0.6f;
        Emit(b.flash, pos, Vector3.zero, r * 3.2f, blast, 0.24f);

        // 2. Thin light rays shooting out (stretched billboards).
        for (int i = 0; i < RayCount; i++)
            Emit(b.rays, pos, Random.onUnitSphere * r * 22f, r * 0.12f, Color.Lerp(judgment, Color.white, 0.5f),
                Random.Range(0.08f, 0.13f));

        // 3. Shards: tiny copies of the rock itself (keeps Sophia's art), spinning out and shrinking away.
        if (mf != null && mf.sharedMesh != null)
        {
            var pr = b.shards.GetComponent<ParticleSystemRenderer>();
            pr.mesh = mf.sharedMesh;
            pr.sharedMaterial = mr.sharedMaterial;
            float meshSize = mf.sharedMesh.bounds.extents.magnitude * 2f;
            float worldPerMesh = r * 2f / Mathf.Max(meshSize, 0.0001f);
            for (int i = 0; i < ShardCount; i++)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * r * 0.3f,
                    velocity = Random.onUnitSphere * r * Random.Range(3f, 5.5f),
                    startSize = meshSize * worldPerMesh * Random.Range(0.1f, 0.2f),
                    startLifetime = Random.Range(0.28f, 0.36f),
                    rotation3D = Random.insideUnitSphere * 360f,
                    angularVelocity3D = Random.insideUnitSphere * 540f,
                    startColor = Color.white,
                };
                b.shards.Emit(p, 1);
            }
        }

        // 4. A few small sparks: judgment colour mixed with warm embers.
        for (int i = 0; i < SparkCount; i++)
            Emit(b.sparks, pos, Random.onUnitSphere * r * Random.Range(8f, 13f), r * 0.06f,
                i % 2 == 0 ? judgment : Ember, Random.Range(0.22f, 0.32f));

        b.until = Time.time + Lifetime;
        live.Add(b);
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 velocity, float size, Color color, float life)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = pos, velocity = velocity, startSize = size, startColor = color, startLifetime = life,
            applyShapeToPosition = false,
        };
        ps.Emit(p, 1);
    }

    void Update()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (Time.time < live[i].until) continue;
            live[i].root.SetActive(false);
            pool.Push(live[i]);
            live.RemoveAt(i);
        }
    }

    Burst Create()
    {
        var b = new Burst { root = new GameObject("Burst") };
        b.root.transform.SetParent(transform, false);
        b.flash = MakeSystem(b.root, "Flash", ParticleSystemRenderMode.Billboard, grow: true);
        b.rays = MakeSystem(b.root, "Rays", ParticleSystemRenderMode.Stretch, grow: false);
        b.sparks = MakeSystem(b.root, "Sparks", ParticleSystemRenderMode.Stretch, grow: false, drag: 6f);
        b.shards = MakeSystem(b.root, "Shards", ParticleSystemRenderMode.Mesh, grow: false, drag: 4f, mesh: true);
        return b;
    }

    static ParticleSystem MakeSystem(GameObject parent, string name, ParticleSystemRenderMode mode, bool grow,
        float drag = 0f, bool mesh = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = 16;
        main.startRotation3D = mesh;
        main.startSize3D = false;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, grow
            ? new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.2f))
            : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));

        if (!mesh)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
        else
        {
            var rot = ps.rotationOverLifetime; rot.enabled = false; // angular velocity comes from EmitParams
        }

        if (drag > 0f)
        {
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = drag;
            lim.multiplyDragByParticleSize = false;
            lim.multiplyDragByParticleVelocity = false;
        }

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = mode;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        if (mode == ParticleSystemRenderMode.Stretch)
        {
            pr.velocityScale = 0.03f;
            pr.lengthScale = 2f;
        }
        if (!mesh)
        {
            pr.sharedMaterial = additive;
            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_BaseMap", SoftDot());
            pr.SetPropertyBlock(mpb);
        }
        return ps;
    }

    /// <summary>Round soft dot, bright centre, no hard edge.</summary>
    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 32;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BurstDot" };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            softDot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.5f)));
        }
        softDot.Apply();
        return softDot;
    }
}
