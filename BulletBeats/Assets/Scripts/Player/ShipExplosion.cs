using UnityEngine;

/// <summary>
/// Looks only: the ship blasting apart when the last life is lost. Hides the ship, then plays a white-hot
/// flash, an orange fireball, sparks, embers and spinning chunks that are small copies of the ship's own
/// meshes, all gone in about 1.3 s. Built from code with the existing BurstAdditive material, so it needs
/// no new art. <see cref="GameFlow"/> calls <see cref="Play"/> before the lose screen.
/// Runs on unscaled time, so it finishes cleanly even after the end screen stops time.
/// </summary>
public static class ShipExplosion
{
    const float Lifetime = 1.4f;
    static readonly Color Flash = new Color(1f, 0.97f, 0.85f);
    static readonly Color Fire = new Color(1f, 0.54f, 0f);      // Impact Orange, same as the rock bursts
    static readonly Color Hot = new Color(1f, 0.85f, 0.3f);
    static readonly Color Ember = new Color(1f, 0.3f, 0.08f);

    static Texture2D softDot;

    /// <summary>Hides <paramref name="ship"/> and blows it apart where its body is. <paramref name="sound"/> is optional.</summary>
    public static void Play(Transform ship, AudioClip sound = null)
    {
        if (ship == null) return;

        // Size and centre of the visible body (the ship's origin sits off to the side of the mesh).
        bool found = false;
        Bounds body = new Bounds(ship.position, Vector3.zero);
        MeshFilter biggest = null;
        float biggestSize = 0f;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>())
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                if (found) body.Encapsulate(r.bounds); else body = r.bounds;
                found = true;
                var mf = r.GetComponent<MeshFilter>();
                float size = r.bounds.extents.sqrMagnitude;
                if (mf != null && mf.sharedMesh != null && size > biggestSize)
                {
                    biggest = mf;
                    biggestSize = size;
                }
            }
        }
        Vector3 pos = body.center;
        float r0 = found ? Mathf.Max(body.extents.x, body.extents.y) : 1f;

        // Shards copy the ship's look, so read its mesh and material before hiding it.
        Mesh shardMesh = biggest != null ? biggest.sharedMesh : null;
        Material shardMaterial = biggest != null ? biggest.GetComponent<Renderer>().sharedMaterial : null;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>()) r.enabled = false;

        var root = new GameObject("ShipExplosion");
        root.transform.position = pos;
        PlaySound(root, sound);

        Material additive = Resources.Load<Material>("VFX/BurstAdditive");
        if (additive != null)
        {
            var glow = MakeSystem(root, "Glow", ParticleSystemRenderMode.Billboard, additive, grow: true);
            var sparks = MakeSystem(root, "Sparks", ParticleSystemRenderMode.Stretch, additive, grow: false, drag: 3f);
            var embers = MakeSystem(root, "Embers", ParticleSystemRenderMode.Billboard, additive, grow: false, drag: 2f);

            // 1. White-hot flash, then an orange fireball swelling behind it.
            Emit(glow, pos, Vector3.zero, r0 * 4f, Flash, 0.18f);
            Color fireball = Fire; fireball.a = 0.85f;
            Emit(glow, pos, Vector3.zero, r0 * 6f, fireball, 0.7f);
            Color haze = Ember; haze.a = 0.45f;
            Emit(glow, pos, Vector3.zero, r0 * 9f, haze, 1.1f);

            // 2. Fast sparks flying out in every direction.
            for (int i = 0; i < 28; i++)
                Emit(sparks, pos, Random.onUnitSphere * r0 * Random.Range(10f, 18f), r0 * 0.09f,
                    i % 3 == 0 ? Flash : Hot, Random.Range(0.35f, 0.7f));

            // 3. Slower glowing embers that linger a little.
            for (int i = 0; i < 16; i++)
                Emit(embers, pos + Random.insideUnitSphere * r0 * 0.4f, Random.onUnitSphere * r0 * Random.Range(1.5f, 4f),
                    r0 * Random.Range(0.12f, 0.22f), i % 2 == 0 ? Fire : Ember, Random.Range(0.8f, 1.3f));
        }

        // 4. Chunks of the ship itself, spinning away and shrinking.
        if (shardMesh != null && shardMaterial != null)
        {
            var shards = MakeSystem(root, "Chunks", ParticleSystemRenderMode.Mesh, shardMaterial, grow: false, drag: 2.5f, mesh: true);
            shards.GetComponent<ParticleSystemRenderer>().mesh = shardMesh;
            float meshSize = shardMesh.bounds.extents.magnitude * 2f;
            float worldPerMesh = r0 * 2f / Mathf.Max(meshSize, 0.0001f);
            for (int i = 0; i < 10; i++)
            {
                shards.Emit(new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * r0 * 0.3f,
                    velocity = Random.onUnitSphere * r0 * Random.Range(3f, 7f),
                    startSize = meshSize * worldPerMesh * Random.Range(0.12f, 0.25f),
                    startLifetime = Random.Range(0.8f, 1.2f),
                    rotation3D = Random.insideUnitSphere * 360f,
                    angularVelocity3D = Random.insideUnitSphere * 600f,
                    startColor = Color.white,
                }, 1);
            }
        }

        Object.Destroy(root, Lifetime + 0.5f);
    }

    static void PlaySound(GameObject root, AudioClip sound)
    {
        if (sound == null) return;
        var source = root.AddComponent<AudioSource>();
        source.ignoreListenerPause = true; // the song is paused while the ship explodes
        source.spatialBlend = 0f;
        source.PlayOneShot(sound);
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 velocity, float size, Color color, float life)
    {
        ps.Emit(new ParticleSystem.EmitParams
        {
            position = pos, velocity = velocity, startSize = size, startColor = color, startLifetime = life,
            applyShapeToPosition = false,
        }, 1);
    }

    static ParticleSystem MakeSystem(GameObject parent, string name, ParticleSystemRenderMode mode, Material material,
        bool grow, float drag = 0f, bool mesh = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = 40;
        main.startRotation3D = mesh;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, grow
            ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.3f))
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
        pr.sharedMaterial = material;
        if (mode == ParticleSystemRenderMode.Stretch)
        {
            pr.velocityScale = 0.03f;
            pr.lengthScale = 2f;
        }
        if (!mesh)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_BaseMap", SoftDot());
            pr.SetPropertyBlock(mpb);
        }
        ps.Play();
        return ps;
    }

    /// <summary>Round soft dot, bright centre, no hard edge.</summary>
    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 32;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ExplosionDot" };
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
