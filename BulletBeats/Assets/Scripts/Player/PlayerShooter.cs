using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Mouse moves the aim; left click fires. With <see cref="hitscan"/> on, the click hits whatever is
/// under the cursor that frame and the bullet is only a visual tracer; with it off, a straight
/// projectile flies and hits are resolved by collision.
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
    [Tooltip("On: a click instantly hits whatever is under the cursor; the bullet is only a visual tracer. " +
             "Off: the bullet flies at Bullet Speed and hits by collision.")]
    public bool hitscan = true;
    [Tooltip("Hitscan only. Radius of the hit check along the cursor ray.")]
    public float hitscanRadius = 1f;
    [Tooltip("Hitscan only. Speed of the visual tracer.")]
    public float tracerSpeed = 1500f;
    [Tooltip("Clicks closer together than this many seconds are ignored.")]
    public float minShotInterval = 0.08f;

    [Header("Projectile")]
    [FormerlySerializedAs("missDamage")]
    public int bulletDamage = 1;
    [Header("Judgment colors (bullet and label)")]
    public Color missColor = new Color(1f, 0.4f, 0.4f);

    public Color perfectColor = new Color(1f, 0.85f, 0.2f);
    public Color goodColor = new Color(0.3f, 0.9f, 1f);

    /// <summary>Timing feedback only; projectile flight and collision are independent of the judgment.</summary>
    public event Action<BeatJudgment> ShotFired;

    public Color ColorFor(BeatJudgment judgment) => judgment switch
    {
        BeatJudgment.Perfect => perfectColor,
        BeatJudgment.Good => goodColor,
        _ => missColor
    };

    /// <summary>World point the gun is aiming at.</summary>
    public Vector3 AimPoint { get; private set; }
    /// <summary>Cursor position in screen pixels.</summary>
    public Vector2 AimScreenPosition { get; private set; }

    static readonly RaycastHit[] hits = new RaycastHit[16];

    float lastShotTime = float.NegativeInfinity;

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
        BeatClock clock = BeatClock.Instance;
        BeatJudgment judgment = ChartTarget.JudgeUnderCursor(
            AimScreenPosition, aimCamera,
            clock != null ? clock.SongTime : Time.time,
            clock != null ? clock.perfectWindow : 0.07f,
            clock != null ? clock.goodWindow : 0.15f);
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        if (hitscan) FireHitscan(origin, judgment);
        else FireProjectile(origin, judgment);

        ShotFired?.Invoke(judgment);
    }

    void FireProjectile(Vector3 origin, BeatJudgment judgment)
    {
        Vector3 direction = AimPoint - origin;
        if (direction.sqrMagnitude < 0.01f) direction = aimCamera.transform.forward;

        if (bulletPrefab != null)
        {
            PlayerBullet bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(direction.normalized));
            bullet.Launch(transform, direction.normalized * bulletSpeed, bulletDamage, judgment, ColorFor(judgment));
        }
    }

    void FireHitscan(Vector3 origin, BeatJudgment judgment)
    {
        Ray ray = aimCamera.ScreenPointToRay(AimScreenPosition);
        IShootable target = FindHitscanTarget(ray, out Vector3 hitPoint);
        Vector3 end = target != null ? hitPoint : AimPoint;

        Vector3 direction = end - origin;
        if (direction.sqrMagnitude < 0.01f) direction = aimCamera.transform.forward;

        PlayerBullet tracer = null;
        if (bulletPrefab != null)
        {
            tracer = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(direction.normalized));
            tracer.LaunchTracer(transform, direction.normalized * tracerSpeed, direction.magnitude,
                bulletDamage, judgment, ColorFor(judgment));
        }

        target?.OnShot(tracer);
    }

    /// <summary>Same rules as <see cref="PlayerBullet"/>: the nearest <see cref="IShootable"/> or solid
    /// collider stops the shot; trigger colliders without an <see cref="IShootable"/> are passed through.</summary>
    IShootable FindHitscanTarget(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = default;
        int count = Physics.SphereCastNonAlloc(ray, hitscanRadius, hits, aimDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        float nearest = float.MaxValue;
        IShootable target = null;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col.transform.IsChildOf(transform)) continue;

            IShootable shootable = col.GetComponentInParent<IShootable>();
            if (shootable == null && col.isTrigger) continue;
            if (hits[i].distance >= nearest) continue;

            nearest = hits[i].distance;
            target = shootable;
            hitPoint = hits[i].distance > 0f ? hits[i].point : col.bounds.center;
        }
        return target;
    }
}
