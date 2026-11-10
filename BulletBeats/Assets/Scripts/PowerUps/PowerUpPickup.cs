using UnityEngine;

/// <summary>
/// A power-up the ship collects by flying into it (e.g. parked at a W/A/S/D spot with zero velocity,
/// see <see cref="PlayerMovement.WorldPositionOf"/>). The spawner sets <see cref="kind"/>, the position
/// and <see cref="velocity"/>. It can't be shot, and it never damages the ship.
/// </summary>
public class PowerUpPickup : MonoBehaviour
{
    public enum Kind { Shield, SlowMo, Bomb }

    /// <summary>Raised once per pickup when it's collected (true) or disappears uncollected (false), for analytics.</summary>
    public static event System.Action<PowerUpPickup, bool> Ended;

    public Kind kind = Kind.Shield;
    /// <summary>The W/A/S/D spot the spawner parked it on.</summary>
    [HideInInspector] public PlayerMovement.Spot spot;
    [Tooltip("World units per second. Zero = stays put.")]
    public Vector3 velocity;
    [Tooltip("How close the ship has to get to collect it.")]
    public float pickupRadius = 4f;
    [Tooltip("Measured from the ship's position plus this, e.g. the visible body's offset from the ship's origin.")]
    public Vector3 shipOffset;
    [Tooltip("Seconds before it disappears uncollected. 0 = never.")]
    public float lifetime = 10f;
    public float spinSpeed = 90f;

    float age;
    bool collected;
    bool reported;

    void Report(bool wasCollected)
    {
        if (reported) return;
        reported = true;
        Ended?.Invoke(this, wasCollected);
    }

    void OnDestroy()
    {
        // A scene unload (retry, level end) isn't the star expiring.
        if (gameObject.scene.isLoaded) Report(false);
    }

    void Update()
    {
        Vector3 last = transform.position;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

        PlayerHealth ship = PlayerHealth.Instance;
        if (ship != null
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
        Report(true);

        PlayerHealth ship = PlayerHealth.Instance;
        if (ship != null)
        {
            if (kind == Kind.Shield && ship.TryGetComponent(out BasicShield shield)) shield.Grant();
            else if (kind == Kind.SlowMo && ship.TryGetComponent(out SlowMo slowMo)) slowMo.Grant();
            else if (kind == Kind.Bomb && ship.TryGetComponent(out Bomb bomb)) bomb.Grant();
        }
        Destroy(gameObject);
    }

    bool BehindCamera()
    {
        Camera cam = DisplayCamera.Find();
        return cam != null && Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward) < 0f;
    }
}
