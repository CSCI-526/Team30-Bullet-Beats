using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Mouse moves the aim; left click fires. With <see cref="hitscan"/> on, the click hits whatever is
/// under the cursor that frame and the bullet is only a visual tracer; with it off, a straight
/// projectile flies and hits are resolved by collision. Aim assist never moves the crosshair: it
/// locks the rock the next shot goes to (<see cref="LockedTarget"/>), and <see cref="AimHUD"/> marks it.
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
    /// <summary>Title-screen value kept across scene loads. Negative means the ship still uses its own setting.</summary>
    public static float SavedAimAssist = -1f;

    [Range(0f, 1f)]
    [Tooltip("0 = a shot locks a rock only when the crosshair is on it or inside its visible ring. 1 = it also locks from Aim Assist Reach away. The title screen slider writes this.")]
    public float aimAssist = 1f;
    [Range(0f, 0.5f)]
    [Tooltip("At full Aim Assist, how far the crosshair can be from a rock and still lock it, as a fraction of the screen height.")]
    public float aimAssistReach = 0.12f;

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
    [Tooltip("Hold the button to keep firing. The title screen toggle writes HoldToFireEnabled.")]
    public bool holdToFire = false;
    [Tooltip("Shots per second while the button is held.")]
    [Min(1f)] public float holdFireRate = 12f;

    const string HoldToFirePrefsKey = "HoldToFire";
    /// <summary>Title-screen setting kept in PlayerPrefs so it survives reloads.</summary>
    public static bool HoldToFireEnabled
    {
        get => PlayerPrefs.GetInt(HoldToFirePrefsKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(HoldToFirePrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    [Header("Projectile")]
    [FormerlySerializedAs("missDamage")]
    public int bulletDamage = 1;
    [Header("Judgment colors (bullet and label)")]
    public Color missColor = new Color(1f, 0.4f, 0.4f);

    public Color perfectColor = new Color(1f, 0.85f, 0.2f);
    public Color goodColor = new Color(0.3f, 0.9f, 1f);

    /// <summary>
    /// A shot at an asteroid, judged against that asteroid's beat (a shot at nothing is a Miss).
    /// A Miss doesn't break the asteroid. Shots that collect a pickup aren't judged and don't raise this.
    /// </summary>
    public event Action<BeatJudgment> ShotFired;

    public Color ColorFor(BeatJudgment judgment) => judgment switch
    {
        BeatJudgment.Perfect => perfectColor,
        BeatJudgment.Good => goodColor,
        _ => missColor
    };

    /// <summary>The asteroid the last shot was aimed at; null if it was aimed at nothing.</summary>
    public ChartTarget LastShotTarget { get; private set; }
    /// <summary>Seconds between the last shot and its asteroid's beat: negative = early, positive = late. NaN without an asteroid.</summary>
    public float LastShotOffset { get; private set; } = float.NaN;

    /// <summary>World point the gun is aiming at.</summary>
    public Vector3 AimPoint { get; private set; }
    /// <summary>Cursor position in screen pixels, which is where the crosshair is drawn.</summary>
    public Vector2 AimScreenPosition { get; private set; }
    /// <summary>The rock the next shot goes to; null if it goes straight under the crosshair.</summary>
    public ChartTarget LockedTarget { get; private set; }

    Vector2 shotScreenPosition;

    static readonly RaycastHit[] hits = new RaycastHit[16];

    float lastShotTime = float.NegativeInfinity;

    void Awake()
    {
        if (SavedAimAssist >= 0f) aimAssist = SavedAimAssist;
        holdToFire = HoldToFireEnabled;
    }

    void Start()
    {
        if (aimCamera == null) aimCamera = DisplayCamera.Find();
    }

    void OnDisable() => LockedTarget = null;

    public void SetAimAssist(float value)
    {
        aimAssist = Mathf.Clamp01(value);
        SavedAimAssist = aimAssist;
    }

    public void SetHoldToFire(bool enabled)
    {
        holdToFire = enabled;
        HoldToFireEnabled = enabled;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || aimCamera == null) return;

        Vector2 cursor = mouse.position.ReadValue();
        AimScreenPosition = cursor;
        AimPoint = FindAimPoint(cursor);
        ApplyAimAssist(cursor);

        if (gunPivot != null)
        {
            Vector3 look = AimPoint - gunPivot.position;
            if (look.sqrMagnitude > 0.01f) gunPivot.rotation = Quaternion.LookRotation(look, transform.up);
        }

        if (CornerMute.Contains(cursor)) return;
        float interval = Mathf.Max(minShotInterval, 1f / holdFireRate);
        bool pressed = mouse.leftButton.wasPressedThisFrame && Time.time - lastShotTime >= minShotInterval;
        bool held = holdToFire && mouse.leftButton.isPressed && Time.time - lastShotTime >= interval;
        if (pressed || held)
            Fire();
    }

    void ApplyAimAssist(Vector2 cursor)
    {
        shotScreenPosition = cursor;
        BeatClock clock = BeatClock.Instance;
        float songTime = clock != null ? clock.SongTime : Time.time;
        float reach = aimAssist * aimAssistReach * aimCamera.pixelHeight;
        LockedTarget = ChartTarget.FindAimTarget(cursor, aimCamera, songTime, reach, LockedTarget);

        if (LockedTarget == null) return;
        AimPoint = LockedTarget.transform.position;
        shotScreenPosition = aimCamera.WorldToScreenPoint(AimPoint);
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
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        // The marker is a promise: a locked shot always goes to the marked rock.
        IShootable target = LockedTarget;
        Vector3 hitPoint = AimPoint;
        if (LockedTarget == null) target = FindHitscanTarget(aimCamera.ScreenPointToRay(AimScreenPosition), out hitPoint);
        BeatJudgment judgment = Judge(target as ChartTarget);

        if (hitscan) FireHitscan(origin, judgment, target, hitPoint);
        else FireProjectile(origin, judgment);

        if (target == null || target is ChartTarget) ShotFired?.Invoke(judgment);
    }

    BeatJudgment Judge(ChartTarget rock)
    {
        LastShotTarget = rock;
        LastShotOffset = float.NaN;
        BeatClock clock = BeatClock.Instance;
        if (rock == null || clock == null) return BeatJudgment.Miss;

        BeatJudgment judgment = clock.JudgeAtSongTime(rock.hitTime, out float offset);
        LastShotOffset = offset;
        rock.RecordShot(judgment, offset);
        return judgment;
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

    void FireHitscan(Vector3 origin, BeatJudgment judgment, IShootable target, Vector3 hitPoint)
    {
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
