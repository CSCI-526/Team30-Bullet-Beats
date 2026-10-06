using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// WASD dodge. Each tap dashes the ship one lane up / left / down / right on a small grid of
/// lanes counted from where the ship starts; tapping into the edge of the grid only bumps.
/// The ship is invulnerable during a dash and shortly after.
/// Only the sideways offset is driven here, so other scripts can still move the ship
/// (e.g. auto-scroll) at the same time.
/// </summary>
public class PlayerDash : MonoBehaviour
{
    [Header("Lanes (the starting lane is 0, 0)")]
    [Tooltip("Furthest lane to the left (x) and down (y).")]
    public Vector2Int minLane = new Vector2Int(-1, -1);
    [Tooltip("Furthest lane to the right (x) and up (y).")]
    public Vector2Int maxLane = new Vector2Int(1, 1);
    [Tooltip("World distance between neighbouring lanes (x = left/right, y = up/down).")]
    public Vector2 laneSpacing = new Vector2(14f, 9f);
    [Tooltip("Left/right and up/down follow this transform's axes, usually the gameplay camera. Empty = the camera drawn on screen.")]
    public Transform axesFrom;

    [Header("Dash")]
    [Min(0.01f)] public float dashDuration = 0.12f;
    [Tooltip("Stays invulnerable for this long after a dash ends.")]
    public float extraInvulnerableTime = 0.05f;
    [Tooltip("Size of the bump when dashing into the edge of the grid, as a fraction of a lane.")]
    [Range(0f, 0.5f)] public float edgeBump = 0.15f;

    /// <summary>Current lane; (0, 0) is the starting lane, x grows to the right and y upwards.</summary>
    public Vector2Int Lane => lane;
    public bool IsDashing => dashTime < dashDuration;
    /// <summary>True during a dash and shortly after. Damage code should ignore hits while this is set.</summary>
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    /// <summary>Raised when a dash starts, with its direction (also when only bumping the edge).</summary>
    public event Action<Vector2Int> Dashed;

    Vector2Int lane;
    Vector2 dashFrom, dashTo, bump;
    Vector2 appliedOffset;
    float dashTime = float.MaxValue;
    float invulnerableUntil = float.NegativeInfinity;
    Vector2Int queued;

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
        Vector2Int tap = ReadTap();
        if (tap != Vector2Int.zero) queued = tap;
        if (queued != Vector2Int.zero && !IsDashing)
        {
            StartDash(queued);
            queued = Vector2Int.zero;
        }

        dashTime += Time.deltaTime;
        ApplyOffset(CurrentOffset());
    }

    static Vector2Int ReadTap()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return Vector2Int.zero;

        Vector2Int dir = Vector2Int.zero;
        if (keyboard.aKey.wasPressedThisFrame) dir.x -= 1;
        if (keyboard.dKey.wasPressedThisFrame) dir.x += 1;
        if (keyboard.wKey.wasPressedThisFrame) dir.y += 1;
        if (keyboard.sKey.wasPressedThisFrame) dir.y -= 1;
        return dir;
    }

    void StartDash(Vector2Int dir)
    {
        Vector2Int target = new Vector2Int(
            Mathf.Clamp(lane.x + dir.x, minLane.x, maxLane.x),
            Mathf.Clamp(lane.y + dir.y, minLane.y, maxLane.y));

        dashFrom = Vector2.Scale(lane, laneSpacing);
        dashTo = Vector2.Scale(target, laneSpacing);
        dashTime = 0f;

        if (target == lane)
        {
            bump = Vector2.Scale(dir, laneSpacing) * edgeBump;
        }
        else
        {
            bump = Vector2.zero;
            lane = target;
            invulnerableUntil = Time.time + dashDuration + extraInvulnerableTime;
        }

        Dashed?.Invoke(dir);
    }

    Vector2 CurrentOffset()
    {
        float t = Mathf.Clamp01(dashTime / dashDuration);
        float eased = 1f - (1f - t) * (1f - t);
        return Vector2.LerpUnclamped(dashFrom, dashTo, eased) + bump * Mathf.Sin(Mathf.PI * t);
    }

    void ApplyOffset(Vector2 offset)
    {
        Vector2 delta = offset - appliedOffset;
        if (delta == Vector2.zero) return;

        Vector3 right = axesFrom != null ? axesFrom.right : Vector3.right;
        Vector3 up = axesFrom != null ? axesFrom.up : Vector3.up;
        transform.position += right * delta.x + up * delta.y;
        appliedOffset = offset;
    }
}
