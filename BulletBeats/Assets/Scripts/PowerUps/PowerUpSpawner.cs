using UnityEngine;

/// <summary>
/// Spawns shield pickups while the song plays. Every spawnInterval seconds it spawns one:
/// a basic shield that flies in from the top of the screen toward the ship (shoot it), or a
/// moving shield parked at a random W/A/S/D spot the ship isn't on (dash to it).
/// Skips a kind the ship already has, and only one pickup is out at a time.
/// </summary>
public class PowerUpSpawner : MonoBehaviour
{
    public PowerUpPickup basicShieldPrefab;
    public PowerUpPickup movingShieldPrefab;

    public float firstSpawnDelay = 15f;
    public float spawnInterval = 20f;
    public float movingShieldChance = 0.5f;

    public float basicSpawnDistance = 60f;
    public float basicFlightSeconds = 6f;
    public Vector2 basicViewportMin = new Vector2(0.3f, 0.55f);
    public Vector2 basicViewportMax = new Vector2(0.7f, 0.8f);
    public float movingLifetime = 6f;

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

        bool hasBasic = ship.TryGetComponent(out BasicShield basic) && basic.Active;
        bool hasMoving = ship.TryGetComponent(out DirectionalShield moving) && moving.Active;
        bool canBasic = basicShieldPrefab != null && !hasBasic;
        bool canMoving = movingShieldPrefab != null && !hasMoving;
        if (!canBasic && !canMoving) return;

        bool pickMoving = canMoving && (!canBasic || Random.value < movingShieldChance);
        live = pickMoving ? SpawnMoving(ship) : SpawnBasic(ship);
    }

    PowerUpPickup SpawnBasic(PlayerHealth ship)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) return null;

        Vector3 viewport = new Vector3(
            Random.Range(basicViewportMin.x, basicViewportMax.x),
            Random.Range(basicViewportMin.y, basicViewportMax.y),
            basicSpawnDistance);
        Vector3 spawn = cam.ViewportToWorldPoint(viewport);
        PowerUpPickup pickup = Instantiate(basicShieldPrefab, spawn, Quaternion.identity);
        pickup.kind = PowerUpPickup.Kind.BasicShield;
        pickup.velocity = (ShipCenter(ship) - spawn) / Mathf.Max(0.1f, basicFlightSeconds);
        // Gone once it reaches the ship, instead of flying on into the camera.
        pickup.lifetime = basicFlightSeconds;
        return pickup;
    }

    PowerUpPickup SpawnMoving(PlayerHealth ship)
    {
        if (!ship.TryGetComponent(out PlayerMovement movement)) return null;

        PlayerMovement.Spot spot;
        do spot = (PlayerMovement.Spot)Random.Range(0, 4);
        while (spot == movement.Current);

        // Put it where the ship's body will be, not its origin, which sits off to the side of the mesh.
        Vector3 bodyOffset = ShipCenter(ship) - ship.transform.position;
        PowerUpPickup pickup = Instantiate(movingShieldPrefab, movement.WorldPositionOf(spot) + bodyOffset, Quaternion.identity);
        pickup.kind = PowerUpPickup.Kind.MovingShield;
        pickup.velocity = Vector3.zero;
        pickup.lifetime = movingLifetime;
        pickup.shipOffset = bodyOffset;
        return pickup;
    }

    /// <summary>Center of the ship's visible meshes, leaving out the shield visuals.</summary>
    static Vector3 ShipCenter(PlayerHealth ship)
    {
        Transform shieldA = ship.TryGetComponent(out DirectionalShield moving) && moving.pivot != null ? moving.pivot : null;
        Transform shieldB = ship.TryGetComponent(out BasicShield basic) && basic.visual != null ? basic.visual.transform : null;

        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>())
        {
            if ((shieldA != null && r.transform.IsChildOf(shieldA)) || (shieldB != null && r.transform.IsChildOf(shieldB)))
                continue;
            if (found) bounds.Encapsulate(r.bounds);
            else bounds = r.bounds;
            found = true;
        }
        return found ? bounds.center : ship.transform.position;
    }
}
