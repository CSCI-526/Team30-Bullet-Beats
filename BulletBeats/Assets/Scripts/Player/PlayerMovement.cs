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
/// another key is pressed. Crossing the full width (A↔D) stops at S first: Left+D or Right+A
/// goes to Bottom, then the same key again reaches the far side.
/// It is invulnerable while dashing and shortly after.
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

    [Header("Size")]
    [Tooltip("Ship size relative to the prefab. The asteroids' hit radius around the ship shrinks with it.")]
    [Range(0.3f, 1f)] public float shipScale = 0.7f;
    [Tooltip("At A and D the whole ship stays this far inside the screen edge (share of the screen width). On narrow screens A and D move in.")]
    [Range(0f, 0.2f)] public float screenMargin = 0.02f;

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

    /// <summary>Size of the ship in this scene relative to the prefab (1 = prefab size).</summary>
    public static float SizeScale { get; private set; } = 1f;

    Vector2 offset, appliedOffset, dashFrom, dashTo;
    float dashTime = float.MaxValue;
    float invulnerableUntil = float.NegativeInfinity;

    void Awake()
    {
        transform.localScale *= shipScale;
        SizeScale = shipScale;
    }

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
        if (keyboard != null && TryReadPress(keyboard, out Spot pressed))
        {
            Spot target = ResolveTarget(Current, pressed);
            if (target != Current) DashTo(target);
        }

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

    /// <summary>
    /// Left↔Right is two steps through Bottom. Every other key still goes straight there.
    /// </summary>
    static Spot ResolveTarget(Spot from, Spot pressed)
    {
        if (from == Spot.Left && pressed == Spot.Right) return Spot.Bottom;
        if (from == Spot.Right && pressed == Spot.Left) return Spot.Bottom;
        return pressed;
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
        Spot.Left => new Vector2(Mathf.Max(left.x, -SideLimit(-1f)), left.y),
        Spot.Right => new Vector2(Mathf.Min(right.x, SideLimit(1f)), right.y),
        _ => bottom
    };

    /// <summary>
    /// How far right (<paramref name="side"/> 1) or left (-1) of S the ship can go and still be fully on
    /// screen in the current window.
    /// </summary>
    float SideLimit(float side)
    {
        Camera cam = DisplayCamera.Find();
        if (cam == null) return float.MaxValue;

        Vector3 rightAxis = axesFrom != null ? axesFrom.right : Vector3.right;
        Vector3 upAxis = axesFrom != null ? axesFrom.up : Vector3.up;
        Vector3 toS = -(rightAxis * appliedOffset.x + upAxis * appliedOffset.y);
        // Moving sideways at a fixed depth moves the ship linearly on screen, so two samples give the limit.
        float atS = Reach(cam, toS, side);
        float perUnit = Reach(cam, toS + rightAxis * side, side) - atS;
        return perUnit > 0f ? Mathf.Max(0f, (1f - screenMargin - atS) / perUnit) : float.MaxValue;
    }

    /// <summary>
    /// How close the ship's meshes, moved by <paramref name="shift"/>, come to the right (<paramref name="side"/> 1)
    /// or left (-1) screen edge, as a share of the screen width (1 = touching it).
    /// </summary>
    float Reach(Camera cam, Vector3 shift, float side)
    {
        float reach = 0f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                float x = cam.WorldToViewportPoint(b.center + Vector3.Scale(b.extents, corner) + shift).x;
                reach = Mathf.Max(reach, side > 0f ? x : 1f - x);
            }
        }
        return reach;
    }

    /// <summary>World position of a spot, e.g. to park a pickup the ship has to dash to.</summary>
    public Vector3 WorldPositionOf(Spot spot)
    {
        Vector3 rightAxis = axesFrom != null ? axesFrom.right : Vector3.right;
        Vector3 upAxis = axesFrom != null ? axesFrom.up : Vector3.up;
        Vector2 delta = OffsetOf(spot) - appliedOffset;
        return transform.position + rightAxis * delta.x + upAxis * delta.y;
    }

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
