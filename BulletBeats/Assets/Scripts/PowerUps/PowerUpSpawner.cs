using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Spawns power-up stars while the song plays. Every spawnInterval seconds it parks one at a random
/// W/A/S/D spot the ship isn't on; the ship collects it by moving there. A spawn is slow motion
/// A spawn is a bomb (see <see cref="Bomb"/>) at bombChance; otherwise slow motion
/// (see <see cref="SlowMo"/>) at slowMoChance, otherwise a shield (see <see cref="BasicShield"/>).
/// Skips a power-up the ship already has, and only one pickup is out at a time.
/// Every pickup shows as a glowing star in its power-up's color (<see cref="PickupStar"/>).
/// </summary>
public class PowerUpSpawner : MonoBehaviour
{
    [FormerlySerializedAs("movingShieldPrefab")]
    public PowerUpPickup shieldPrefab;
    [Tooltip("Optional. Without one, the shield prefab is used.")]
    public PowerUpPickup slowMoPrefab;
    [Tooltip("Optional. Without one, the shield prefab is used.")]
    public PowerUpPickup bombPrefab;

    public float firstSpawnDelay = 15f;
    public float spawnInterval = 20f;
    [Tooltip("Chance a spawn is a bomb. Rolled first.")]
    [Range(0f, 1f)] public float bombChance = 0.25f;
    [Tooltip("Chance a spawn that isn't a bomb is slow motion instead of a shield.")]
    [Range(0f, 1f)] public float slowMoChance = 0.35f;

    [Header("Stars")]
    [FormerlySerializedAs("movingShieldColor")]
    public Color shieldColor = new Color(0.75f, 0.5f, 1f);
    public Color slowMoColor = new Color(0.45f, 0.85f, 1f);
    public Color bombColor = new Color(1f, 0.42f, 0.15f);

    [FormerlySerializedAs("movingLifetime")]
    [Tooltip("Seconds a star stays at its spot when it doesn't fly in.")]
    public float starLifetime = 6f;
    [Tooltip("Spot stars fly in from the middle of the screen and land on a beat, instead of appearing at their spot.")]
    public bool flyInStars = true;
    [Tooltip("Song seconds (at least) a spot star takes to fly in; it lands on the next beat after that.")]
    public float starFlightSeconds = 2.5f;
    [Tooltip("Song seconds a spot star waits at its spot after landing.")]
    public float starLingerSeconds = 3f;

    float timer;
    PowerUpPickup live;

    void Start() => timer = firstSpawnDelay;

    void Update()
    {
        BeatClock clock = BeatClock.Instance;
        if (clock != null && !clock.IsMusicPlaying) return;
        if (live != null) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = spawnInterval;
        SpawnOne();
    }

    void SpawnOne()
    {
        PlayerHealth ship = PlayerHealth.Instance;
        if (ship == null || ship.IsDead) return;

        bool canBomb = ship.TryGetComponent(out Bomb bomb) && !bomb.Active
            && (bombPrefab != null || shieldPrefab != null);
        if (canBomb && Random.value < bombChance)
        {
            live = SpawnBomb(ship);
            return;
        }

        bool canSlowMo = ship.TryGetComponent(out SlowMo slowMo) && !slowMo.Active
            && (slowMoPrefab != null || shieldPrefab != null);
        bool canShield = shieldPrefab != null && ship.TryGetComponent(out BasicShield shield) && !shield.Active;

        if (canSlowMo && (!canShield || Random.value < slowMoChance)) live = SpawnSlowMo(ship);
        else if (canShield) live = SpawnShield(ship);
    }

    /// <param name="at">Spot to park it on; random (not the ship's own spot) when left out.</param>
    public PowerUpPickup SpawnShield(PlayerHealth ship, PlayerMovement.Spot? at = null) =>
        SpawnStar(ship, shieldPrefab, PowerUpPickup.Kind.Shield, shieldColor, at);

    /// <param name="at">Spot to park it on; random (not the ship's own spot) when left out.</param>
    public PowerUpPickup SpawnSlowMo(PlayerHealth ship, PlayerMovement.Spot? at = null) =>
        SpawnStar(ship, slowMoPrefab, PowerUpPickup.Kind.SlowMo, slowMoColor, at);

    /// <param name="at">Spot to park it on; random (not the ship's own spot) when left out.</param>
    public PowerUpPickup SpawnBomb(PlayerHealth ship, PlayerMovement.Spot? at = null) =>
        SpawnStar(ship, bombPrefab, PowerUpPickup.Kind.Bomb, bombColor, at);

    /// <summary>Parks a star of <paramref name="kind"/> at a W/A/S/D spot. Without a prefab, the shield prefab is used.</summary>
    public PowerUpPickup SpawnStar(PlayerHealth ship, PowerUpPickup prefab, PowerUpPickup.Kind kind, Color color, PlayerMovement.Spot? at = null)
    {
        if (prefab == null) prefab = shieldPrefab;
        if (prefab == null || !ship.TryGetComponent(out PlayerMovement movement)) return null;

        PlayerMovement.Spot spot;
        if (at.HasValue) spot = at.Value;
        else
        {
            do spot = (PlayerMovement.Spot)Random.Range(0, 4);
            while (spot == movement.Current);
        }

        // Put it where the ship's body will be, not its origin, which sits off to the side of the mesh.
        Vector3 bodyOffset = ShipCenter(ship) - ship.transform.position;
        PowerUpPickup pickup = Instantiate(prefab, movement.WorldPositionOf(spot) + bodyOffset, Quaternion.identity);
        pickup.kind = kind;
        pickup.spot = spot;
        pickup.velocity = Vector3.zero;
        pickup.lifetime = starLifetime;
        pickup.shipOffset = bodyOffset;
        var star = pickup.gameObject.AddComponent<PickupStar>();
        star.Init(color);
        if (flyInStars)
        {
            // The star removes itself after it lands and lingers, so the pickup's own timer is off.
            star.lingerSeconds = starLingerSeconds;
            if (star.FlyTo(pickup.transform.position, starFlightSeconds)) pickup.lifetime = 0f;
        }
        return pickup;
    }

    /// <summary>Center of the ship's visible meshes.</summary>
    static Vector3 ShipCenter(PlayerHealth ship)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        return found ? bounds.center : ship.transform.position;
    }
}
