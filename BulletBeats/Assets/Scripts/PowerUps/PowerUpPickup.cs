using UnityEngine;

/// <summary>
/// A power-up the ship collects. The spawner sets <see cref="kind"/>, <see cref="collectBy"/>, the
/// position and <see cref="velocity"/>. Shot: shooting it collects it, like an asteroid that gives
/// instead of hurts. Touch: the ship collects it by flying into it (e.g. parked at a W/A/S/D spot
/// with zero velocity, see <see cref="PlayerMovement.WorldPositionOf"/>). Never damages the ship.
/// Needs a collider for Shot (a trigger is fine).
/// </summary>
public class PowerUpPickup : MonoBehaviour, IShootable
{
    public enum Kind { BasicShield, MovingShield }
    public enum CollectBy { Shot, Touch }

    public Kind kind = Kind.BasicShield;
    public CollectBy collectBy = CollectBy.Shot;
    [Tooltip("World units per second. Zero = stays put.")]
    public Vector3 velocity;
    [Tooltip("Touch only. How close the ship has to get to collect it.")]
    public float pickupRadius = 4f;
    [Tooltip("Touch only. Measured from the ship's position plus this, e.g. the visible body's offset from the ship's origin.")]
    public Vector3 shipOffset;
    [Tooltip("Seconds before it disappears uncollected. 0 = never.")]
    public float lifetime = 10f;
    public float spinSpeed = 90f;

    float age;
    bool collected;

    public void OnShot(PlayerBullet bullet)
    {
        if (collectBy == CollectBy.Shot) Collect();
    }

    void Update()
    {
        Vector3 last = transform.position;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

        PlayerHealth ship = PlayerHealth.Instance;
        if (collectBy == CollectBy.Touch && ship != null
            && ChartTarget.DistanceToSegment(ship.transform.position + shipOffset, last, transform.position) <= pickupRadius)
        {
            Collect();
            return;
        }

        age += Time.deltaTime;
        if ((lifetime > 0f && age >= lifetime) || BehindCamera())
            Destroy(gameObject);
    }

    void Collect()
    {
        if (collected) return;
        collected = true;

        PlayerHealth ship = PlayerHealth.Instance;
        if (ship != null)
        {
            if (kind == Kind.BasicShield && ship.TryGetComponent(out BasicShield basic)) basic.Grant();
            else if (kind == Kind.MovingShield && ship.TryGetComponent(out DirectionalShield moving)) moving.Grant();
        }
        Destroy(gameObject);
    }

    bool BehindCamera()
    {
        Camera cam = DisplayCamera.Find();
        return cam != null && Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward) < 0f;
    }
}
