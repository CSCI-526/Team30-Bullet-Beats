using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse moves the aim, left click fires a bullet from the muzzle towards the point under the
/// cursor. Every shot is judged against the nearest chart note / asteroid hit time
/// (Perfect / Good / Miss); the judgment sets the bullet's damage and colour and is announced
/// through <see cref="ShotFired"/>. Each chart note can only be scored once, so mashing the
/// extras into Misses.
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera the mouse aims through. Empty = the camera drawn on screen.")]
    public Camera aimCamera;
    [Tooltip("Optional. Turned to face the aim point.")]
    public Transform gunPivot;
    [Tooltip("Bullets spawn here. Empty = this object's position.")]
    public Transform muzzle;
    public PlayerBullet bulletPrefab;

    [Header("Aim")]
    [Tooltip("How far from the camera the aim point is when the cursor is not over anything.")]
    public float aimDistance = 400f;
    public LayerMask aimMask = Physics.DefaultRaycastLayers;

    [Header("Shooting")]
    public float bulletSpeed = 250f;
    [Tooltip("Clicks closer together than this many seconds are ignored.")]
    public float minShotInterval = 0.08f;

    [Header("Per judgment")]
    public int perfectDamage = 3;
    public int goodDamage = 2;
    public int missDamage = 1;
    public Color perfectColor = new Color(1f, 0.85f, 0.2f);
    public Color goodColor = new Color(0.3f, 0.9f, 1f);
    public Color missColor = new Color(1f, 0.4f, 0.4f);

    /// <summary>Raised for every shot fired, with its judgment.</summary>
    public event Action<BeatJudgment> ShotFired;

    /// <summary>World point the gun is aiming at.</summary>
    public Vector3 AimPoint { get; private set; }
    /// <summary>Cursor position in screen pixels.</summary>
    public Vector2 AimScreenPosition { get; private set; }

    static readonly RaycastHit[] hits = new RaycastHit[16];

    float lastShotTime = float.NegativeInfinity;
    int lastScoredNote = int.MinValue;

    void Start()
    {
        if (aimCamera == null) aimCamera = DisplayCamera.Find();
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || aimCamera == null) return;

        AimScreenPosition = mouse.position.ReadValue();
        AimPoint = FindAimPoint(AimScreenPosition);

        if (gunPivot != null)
        {
            Vector3 look = AimPoint - gunPivot.position;
            if (look.sqrMagnitude > 0.01f) gunPivot.rotation = Quaternion.LookRotation(look, transform.up);
        }

        if (mouse.leftButton.wasPressedThisFrame && Time.time - lastShotTime >= minShotInterval)
            Fire();
    }

    Vector3 FindAimPoint(Vector2 screenPosition)
    {
        Ray ray = aimCamera.ScreenPointToRay(screenPosition);
        Vector3 point = ray.GetPoint(aimDistance);
        float nearest = aimDistance;

        int count = Physics.RaycastNonAlloc(ray, hits, aimDistance, aimMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(transform) || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            point = hits[i].point;
        }
        return point;
    }

    void Fire()
    {
        lastShotTime = Time.time;
        BeatJudgment judgment = JudgeShot();

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 direction = AimPoint - origin;
        if (direction.sqrMagnitude < 0.01f) direction = aimCamera.transform.forward;
        direction.Normalize();

        if (bulletPrefab != null)
        {
            PlayerBullet bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(direction));
            bullet.Launch(transform, direction * bulletSpeed, DamageFor(judgment), judgment, ColorFor(judgment));
        }

        ShotFired?.Invoke(judgment);
    }

    BeatJudgment JudgeShot()
    {
        BeatClock clock = BeatClock.Instance;
        ChartTargetSpawner spawner = ChartTargetSpawner.Instance;
        if (clock == null) return BeatJudgment.Good;

        // Prefer chart notes (MIDI / asteroids). Fall back to the quarter grid only if no chart.
        if (spawner != null && spawner.Notes.Count > 0)
        {
            BeatJudgment judgment = spawner.JudgeShot(
                clock.SongTime, clock.perfectWindow, clock.goodWindow, out int noteIndex, out _);
            if (judgment == BeatJudgment.Miss) return judgment;
            if (noteIndex == lastScoredNote) return BeatJudgment.Miss;
            lastScoredNote = noteIndex;
            return judgment;
        }

        BeatJudgment grid = clock.Judge(out int beat, out _);
        if (grid == BeatJudgment.Miss) return grid;
        if (beat == lastScoredNote) return BeatJudgment.Miss;
        lastScoredNote = beat;
        return grid;
    }

    public int DamageFor(BeatJudgment judgment) => judgment switch
    {
        BeatJudgment.Perfect => perfectDamage,
        BeatJudgment.Good => goodDamage,
        _ => missDamage
    };

    public Color ColorFor(BeatJudgment judgment) => judgment switch
    {
        BeatJudgment.Perfect => perfectColor,
        BeatJudgment.Good => goodColor,
        _ => missColor
    };
}
