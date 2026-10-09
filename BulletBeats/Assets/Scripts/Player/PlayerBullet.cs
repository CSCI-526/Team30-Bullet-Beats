using UnityEngine;

/// <summary>
/// A player shot. Flies straight and stops at the first <see cref="IShootable"/> or solid collider
/// in its path. The path is swept every frame, so fast bullets can't skip through thin targets.
/// Trigger colliders without an <see cref="IShootable"/> are passed through.
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

    void Update()
    {
        float step = velocity.magnitude * Time.deltaTime;
        if (step > 0f && HitSomething(step))
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
