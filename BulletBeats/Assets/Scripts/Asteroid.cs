using UnityEngine;

public class Asteroid : MonoBehaviour
{
    // Set by the spawner (or in the Inspector for testing)
    public Vector3 startPosition;
    public Vector3 direction;
    public float speed;

    public int health = 3;
    public float minSpinSpeed = 20f;
    public float maxSpinSpeed = 90f;
    public float despawnDistanceBehindCamera = 10f;

    private Vector3 spinAxis;
    private float spinSpeed;

    private void Start()
    {
        transform.position = startPosition;

        spinAxis = Random.onUnitSphere;
        spinSpeed = Random.Range(minSpinSpeed, maxSpinSpeed);
    }

    private void Update()
    {
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        transform.position += direction.normalized * speed * Time.deltaTime;

        Camera cam = Camera.main;
        if (cam != null && transform.position.z < cam.transform.position.z - despawnDistanceBehindCamera)
        {
            Destroy(gameObject);
        }
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
