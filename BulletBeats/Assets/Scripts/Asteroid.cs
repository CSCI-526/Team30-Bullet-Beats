using UnityEngine;

public class Asteroid : MonoBehaviour, IShootable
{
    // Set by the spawner (or in the Inspector for testing)
    public Vector3 startPosition;
    public Vector3 direction;
    public float speed;

    public int goodHitsToDestroy = 2;
    public float minSpinSpeed = 20f;
    public float maxSpinSpeed = 90f;
    public float despawnDistanceBehindCamera = 10f;

    private Vector3 spinAxis;
    private float spinSpeed;
    private int goodHits;
    private Transform cam;

    private void Start()
    {
        transform.position = startPosition;

        spinAxis = Random.onUnitSphere;
        spinSpeed = Random.Range(minSpinSpeed, maxSpinSpeed);

        Camera displayCamera = DisplayCamera.Find();
        if (displayCamera != null)
        {
            cam = displayCamera.transform;
        }
    }

    private void Update()
    {
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        transform.position += direction.normalized * speed * Time.deltaTime;

        // Negative = behind the camera
        if (cam != null && Vector3.Dot(transform.position - cam.position, cam.forward) < -despawnDistanceBehindCamera)
        {
            Destroy(gameObject);
        }
    }

    public void OnShot(PlayerBullet bullet)
    {
        if (bullet.Judgment == BeatJudgment.Perfect)
        {
            Destroy(gameObject);
        }
        else if (bullet.Judgment == BeatJudgment.Good)
        {
            goodHits++;
            if (goodHits >= goodHitsToDestroy)
            {
                Destroy(gameObject);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null || player.IsInvulnerable) return;

        Debug.Log(name + " hit the player");
        Destroy(gameObject);
    }
}
