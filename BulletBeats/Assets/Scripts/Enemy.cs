using UnityEngine;

public class Enemy : MonoBehaviour
{
    // Set by the spawner (or in the Inspector for testing)
    public Vector3 startPosition;
    public Vector3 direction;
    public float speed;

    public int health = 5;
    public EnemyBullet bulletPrefab;
    public Transform firePoint;
    public bool aimAtPlayer = true;
    public float despawnDistanceBehindCamera = 10f;

    // Testing only: turn off once the beat system calls Fire()
    public bool autoFire = false;
    public float fireInterval = 1f;

    private Transform player;
    private float fireTimer;

    private void Start()
    {
        transform.position = startPosition;

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    private void Update()
    {
        transform.position += direction.normalized * speed * Time.deltaTime;

        Camera cam = Camera.main;
        if (cam != null && transform.position.z < cam.transform.position.z - despawnDistanceBehindCamera)
        {
            Destroy(gameObject);
            return;
        }

        if (!autoFire) return;

        fireTimer += Time.deltaTime;
        if (fireTimer >= fireInterval)
        {
            fireTimer = 0f;
            Fire();
        }
    }

    [ContextMenu("Fire")]
    public void Fire()
    {
        if (bulletPrefab == null || firePoint == null)
        {
            Debug.LogWarning(name + ": bulletPrefab or firePoint not assigned");
            return;
        }

        Quaternion rotation = firePoint.rotation;
        if (aimAtPlayer && player != null)
        {
            rotation = Quaternion.LookRotation(player.position - firePoint.position);
        }

        Instantiate(bulletPrefab, firePoint.position, rotation);
    }

    public void TakeDamage(int amount)
    {
        health -= amount;
        if (health <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log(name + " hit the player");
            Destroy(gameObject);
        }
    }
}
