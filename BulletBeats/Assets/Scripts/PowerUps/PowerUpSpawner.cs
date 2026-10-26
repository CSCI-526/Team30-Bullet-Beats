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

        Vector3 viewport = new Vector3(Random.Range(0.15f, 0.85f), Random.Range(0.6f, 0.9f), basicSpawnDistance);
        Vector3 spawn = cam.ViewportToWorldPoint(viewport);
        PowerUpPickup pickup = Instantiate(basicShieldPrefab, spawn, Quaternion.identity);
        pickup.kind = PowerUpPickup.Kind.BasicShield;
        pickup.velocity = (ship.transform.position - spawn) / Mathf.Max(0.1f, basicFlightSeconds);
        pickup.lifetime = basicFlightSeconds * 2f;
        return pickup;
    }

    PowerUpPickup SpawnMoving(PlayerHealth ship)
    {
        if (!ship.TryGetComponent(out PlayerMovement movement)) return null;

        PlayerMovement.Spot spot;
        do spot = (PlayerMovement.Spot)Random.Range(0, 4);
        while (spot == movement.Current);

        PowerUpPickup pickup = Instantiate(movingShieldPrefab, movement.WorldPositionOf(spot), Quaternion.identity);
        pickup.kind = PowerUpPickup.Kind.MovingShield;
        pickup.velocity = Vector3.zero;
        pickup.lifetime = movingLifetime;
        return pickup;
    }
}
