using UnityEngine;

public class AsteroidSpawner : MonoBehaviour
{
    public Asteroid asteroidPrefab;
    public PlayerMovement player;

    public float spawnDistance = 150f;
    public float spawnSpread = 20f;
    public float asteroidSpeed = 40f;

    // Testing only: turn off once the beat system calls SpawnAsteroid()
    public bool autoSpawn = true;
    public float spawnInterval = 2f;

    private Transform cam;
    private Vector3 playerAnchor;
    private float spawnTimer;

    private void Start()
    {
        Camera displayCamera = DisplayCamera.Find();
        if (displayCamera != null)
        {
            cam = displayCamera.transform;
        }
    }

    private void Update()
    {
        // Where the ship's spots are measured from. Skipped mid-dash since the ship is between spots
        if (player != null && !player.IsDashing)
        {
            playerAnchor = player.transform.position - SpotOffset(player.Current);
        }

        if (!autoSpawn) return;

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            SpawnAsteroid();
        }
    }

    [ContextMenu("Spawn Asteroid")]
    public void SpawnAsteroid()
    {
        PlayerMovement.Spot randomSpot = (PlayerMovement.Spot)Random.Range(0, 4);
        SpawnAsteroid(randomSpot);
    }

    public void SpawnAsteroid(PlayerMovement.Spot spot)
    {
        if (asteroidPrefab == null || player == null || cam == null)
        {
            Debug.LogWarning(name + ": asteroidPrefab or player not assigned");
            return;
        }

        Vector3 target = playerAnchor + SpotOffset(spot);
        Vector2 spread = Random.insideUnitCircle * spawnSpread;
        Vector3 spawnPosition = target + cam.forward * spawnDistance + cam.right * spread.x + cam.up * spread.y;

        Asteroid asteroid = Instantiate(asteroidPrefab, spawnPosition, Quaternion.identity);
        asteroid.startPosition = spawnPosition;
        asteroid.direction = target - spawnPosition;
        asteroid.speed = asteroidSpeed;
    }

    private Vector3 SpotOffset(PlayerMovement.Spot spot)
    {
        Vector2 offset = spot switch
        {
            PlayerMovement.Spot.Center => player.center,
            PlayerMovement.Spot.Left => player.left,
            PlayerMovement.Spot.Right => player.right,
            _ => player.bottom
        };

        Vector3 right = player.axesFrom != null ? player.axesFrom.right : Vector3.right;
        Vector3 up = player.axesFrom != null ? player.axesFrom.up : Vector3.up;
        return right * offset.x + up * offset.y;
    }
}
