using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// W / A / S / D each send the ship to a fixed spot, laid out like the keys:
/// <code>
///              W (center)
///   A (left)   S (bottom)   D (right)
/// </code>
/// The ship starts at S, dashes quickly to the spot of the key pressed and stays there until
/// another key is pressed. It is invulnerable while dashing and shortly after.
/// Only the sideways offset is driven here, so other scripts can still move the ship
/// (e.g. auto-scroll) at the same time.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    public enum Spot { Center, Left, Bottom, Right }

    [Header("Spots (world units from the start position)")]
    [Tooltip("W")] public Vector2 center = new Vector2(0f, 13f);
    [Tooltip("A")] public Vector2 left = new Vector2(-14f, 0f);
    [Tooltip("S")] public Vector2 bottom = Vector2.zero;
    [Tooltip("D")] public Vector2 right = new Vector2(14f, 0f);
    [Tooltip("Left/right and up/down follow this transform's axes, usually the gameplay camera. Empty = the camera drawn on screen.")]
    public Transform axesFrom;

    [Header("Dash")]
    [Min(0.01f)] public float dashDuration = 0.12f;
    [Tooltip("Stays invulnerable for this long after a dash ends.")]
    public float extraInvulnerableTime = 0.05f;

    /// <summary>The spot the ship is at, or dashing to.</summary>
    public Spot Current { get; private set; } = Spot.Bottom;
    public bool IsDashing => dashTime < dashDuration;
    /// <summary>True during a dash and shortly after. Damage code should ignore hits while this is set.</summary>
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    /// <summary>Raised when a dash to a new spot starts.</summary>
    public event Action<Spot> Dashed;

    Vector2 offset, appliedOffset, dashFrom, dashTo;
    float dashTime = float.MaxValue;
    float invulnerableUntil = float.NegativeInfinity;

    void Start()
    {
        if (axesFrom == null)
        {
            Camera cam = DisplayCamera.Find();
            if (cam != null) axesFrom = cam.transform;
        }
        offset = OffsetOf(Current);
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && TryReadPress(keyboard, out Spot pressed) && pressed != Current)
            DashTo(pressed);

        if (IsDashing)
        {
            dashTime += Time.deltaTime;
            float t = Mathf.Clamp01(dashTime / dashDuration);
            offset = Vector2.LerpUnclamped(dashFrom, dashTo, 1f - (1f - t) * (1f - t));
        }

        ApplyOffset(offset);
    }

    static bool TryReadPress(Keyboard keyboard, out Spot spot)
    {
        spot = Spot.Bottom;
        if (keyboard.wKey.wasPressedThisFrame) spot = Spot.Center;
        else if (keyboard.aKey.wasPressedThisFrame) spot = Spot.Left;
        else if (keyboard.sKey.wasPressedThisFrame) spot = Spot.Bottom;
        else if (keyboard.dKey.wasPressedThisFrame) spot = Spot.Right;
        else return false;
        return true;
    }

    void DashTo(Spot spot)
    {
        Current = spot;
        dashFrom = offset;
        dashTo = OffsetOf(spot);
        dashTime = 0f;
        invulnerableUntil = Time.time + dashDuration + extraInvulnerableTime;
        Dashed?.Invoke(spot);
    }

    Vector2 OffsetOf(Spot spot) => spot switch
    {
        Spot.Center => center,
        Spot.Left => left,
        Spot.Right => right,
        _ => bottom
    };

    void ApplyOffset(Vector2 newOffset)
    {
        Vector2 delta = newOffset - appliedOffset;
        if (delta == Vector2.zero) return;

        Vector3 rightAxis = axesFrom != null ? axesFrom.right : Vector3.right;
        Vector3 upAxis = axesFrom != null ? axesFrom.up : Vector3.up;
        transform.position += rightAxis * delta.x + upAxis * delta.y;
        appliedOffset = newOffset;
    }
}
