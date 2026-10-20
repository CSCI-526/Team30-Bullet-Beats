using UnityEngine;

/// <summary>
/// A straight projectile that stops at the first <see cref="IShootable"/> or solid collider.
/// Trigger colliders without an <see cref="IShootable"/> are passed through.
/// Launched with <see cref="LaunchTracer"/> it is visual only and never hits anything.
/// </summary>
public class PlayerBullet : MonoBehaviour
{
    [Tooltip("Seconds before the bullet disappears if it hits nothing.")]
    public float lifetime = 2f;
    [Tooltip("Radius used for hit detection.")]
    public float radius = 1f;

    public int Damage { get; private set; } = 1;
    public BeatJudgment Judgment { get; private set; } = BeatJudgment.Good;

    static readonly RaycastHit[] hits = new RaycastHit[16];

    Transform owner;
    Vector3 velocity;
    float age;
    bool tracer;
    float tracerDistance;
    float travelled;

    /// <param name="owner">The bullet never hits this object or its children.</param>
    public void Launch(Transform owner, Vector3 velocity, int damage, BeatJudgment judgment, Color color)
    {
        this.owner = owner;
        this.velocity = velocity;
        Damage = damage;
        Judgment = judgment;
        if (velocity != Vector3.zero) transform.rotation = Quaternion.LookRotation(velocity);

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.SetPropertyBlock(block);
    }

    /// <summary>Visual only (hitscan): flies <paramref name="distance"/> and disappears without hitting
    /// anything, since the shooter already resolved the hit.</summary>
    public void LaunchTracer(Transform owner, Vector3 velocity, float distance, int damage, BeatJudgment judgment, Color color)
    {
        Launch(owner, velocity, damage, judgment, color);
        tracer = true;
        tracerDistance = distance;
    }

    void Update()
    {
        float step = velocity.magnitude * Time.deltaTime;
        if (tracer)
        {
            travelled += step;
            if (travelled >= tracerDistance)
            {
                Destroy(gameObject);
                return;
            }
        }
        else if (step > 0f && HitSomething(step))
        {
            Destroy(gameObject);
            return;
        }

        transform.position += velocity * Time.deltaTime;
        age += Time.deltaTime;
        if (age >= lifetime) Destroy(gameObject);
    }

    bool HitSomething(float step)
    {
        int count = Physics.SphereCastNonAlloc(transform.position, radius, velocity.normalized, hits, step,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        float nearest = float.MaxValue;
        IShootable target = null;
        bool stopped = false;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (owner != null && col.transform.IsChildOf(owner)) continue;

            IShootable shootable = col.GetComponentInParent<IShootable>();
            if (shootable == null && col.isTrigger) continue;
            if (hits[i].distance >= nearest) continue;

            nearest = hits[i].distance;
            target = shootable;
            stopped = true;
        }

        if (stopped) target?.OnShot(this);
        return stopped;
    }
}
