using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hold W/A/S/D to fly smoothly around an area around the start position; double-tap a direction
/// to dash that way. The ship is invulnerable during a dash and shortly after.
/// Only the sideways offset is driven here, so other scripts can still move the ship
/// (e.g. auto-scroll) at the same time.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("Area (world units from the start position)")]
    [Tooltip("Furthest the ship can go left (x) and down (y).")]
    public Vector2 areaMin = new Vector2(-14f, -9f);
    [Tooltip("Furthest the ship can go right (x) and up (y).")]
    public Vector2 areaMax = new Vector2(14f, 9f);
    [Tooltip("Left/right and up/down follow this transform's axes, usually the gameplay camera. Empty = the camera drawn on screen.")]
    public Transform axesFrom;

    [Header("Movement")]
    public float moveSpeed = 40f;
    [Tooltip("How fast the ship speeds up and slows down, in units per second squared.")]
    public float acceleration = 300f;

    [Header("Dash (double-tap a direction)")]
    public float dashDistance = 12f;
    [Min(0.01f)] public float dashDuration = 0.12f;
    [Tooltip("Max seconds between the two taps.")]
    public float doubleTapWindow = 0.25f;
    [Tooltip("Seconds from the start of one dash until the next one is allowed.")]
    public float dashCooldown = 0.4f;
    [Tooltip("Stays invulnerable for this long after a dash ends.")]
    public float extraInvulnerableTime = 0.05f;

    public bool IsDashing => dashTime < dashDuration;
    /// <summary>True during a dash and shortly after. Damage code should ignore hits while this is set.</summary>
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    /// <summary>Raised when a dash starts, with its direction.</summary>
    public event Action<Vector2Int> Dashed;

    Vector2 offset, appliedOffset, velocity;
    Vector2 dashFrom, dashTo;
    float dashTime = float.MaxValue;
    float nextDashTime;
    float invulnerableUntil = float.NegativeInfinity;
    Vector2Int lastTap;
    float lastTapTime = float.NegativeInfinity;

    void Start()
    {
        if (axesFrom == null)
        {
            Camera cam = DisplayCamera.Find();
            if (cam != null) axesFrom = cam.transform;
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null) HandleTap(ReadTap(keyboard));

        if (IsDashing)
        {
            dashTime += Time.deltaTime;
            float t = Mathf.Clamp01(dashTime / dashDuration);
            offset = Vector2.LerpUnclamped(dashFrom, dashTo, 1f - (1f - t) * (1f - t));
        }
        else
        {
            Vector2 input = keyboard != null ? ReadHeld(keyboard) : Vector2.zero;
            velocity = Vector2.MoveTowards(velocity, input * moveSpeed, acceleration * Time.deltaTime);
            offset += velocity * Time.deltaTime;

            Vector2 clamped = ClampToArea(offset);
            if (clamped.x != offset.x) velocity.x = 0f;
            if (clamped.y != offset.y) velocity.y = 0f;
            offset = clamped;
        }

        ApplyOffset(offset);
    }

    static Vector2Int ReadTap(Keyboard keyboard)
    {
        if (keyboard.aKey.wasPressedThisFrame) return Vector2Int.left;
        if (keyboard.dKey.wasPressedThisFrame) return Vector2Int.right;
        if (keyboard.wKey.wasPressedThisFrame) return Vector2Int.up;
        if (keyboard.sKey.wasPressedThisFrame) return Vector2Int.down;
        return Vector2Int.zero;
    }

    static Vector2 ReadHeld(Keyboard keyboard)
    {
        Vector2 dir = Vector2.zero;
        if (keyboard.aKey.isPressed) dir.x -= 1f;
        if (keyboard.dKey.isPressed) dir.x += 1f;
        if (keyboard.wKey.isPressed) dir.y += 1f;
        if (keyboard.sKey.isPressed) dir.y -= 1f;
        return dir.sqrMagnitude > 1f ? dir.normalized : dir;
    }

    void HandleTap(Vector2Int tap)
    {
        if (tap == Vector2Int.zero) return;

        bool doubleTap = tap == lastTap && Time.time - lastTapTime <= doubleTapWindow;
        if (doubleTap && !IsDashing && Time.time >= nextDashTime)
        {
            TryDash(tap);
            lastTap = Vector2Int.zero;
        }
        else
        {
            lastTap = tap;
            lastTapTime = Time.time;
        }
    }

    void TryDash(Vector2Int dir)
    {
        Vector2 target = ClampToArea(offset + (Vector2)dir * dashDistance);
        if ((target - offset).sqrMagnitude < 0.0001f) return;

        dashFrom = offset;
        dashTo = target;
        dashTime = 0f;
        nextDashTime = Time.time + dashCooldown;
        invulnerableUntil = Time.time + dashDuration + extraInvulnerableTime;
        Dashed?.Invoke(dir);
    }

    Vector2 ClampToArea(Vector2 p) => new Vector2(
        Mathf.Clamp(p.x, areaMin.x, areaMax.x),
        Mathf.Clamp(p.y, areaMin.y, areaMax.y));

    void ApplyOffset(Vector2 newOffset)
    {
        Vector2 delta = newOffset - appliedOffset;
        if (delta == Vector2.zero) return;

        Vector3 right = axesFrom != null ? axesFrom.right : Vector3.right;
        Vector3 up = axesFrom != null ? axesFrom.up : Vector3.up;
        transform.position += right * delta.x + up * delta.y;
        appliedOffset = newOffset;
    }
}
