using System;
using UnityEngine;

/// <summary>
/// Shield that faces wherever the ship is aiming: the vector from the ship to
/// <see cref="PlayerShooter.AimPoint"/> (the locked-on asteroid, otherwise the mouse point).
/// The pivot's local +Y is turned along that vector. A rock is blocked when it comes within
/// <see cref="blockRadius"/> of the ship within <see cref="blockAngle"/> degrees of that vector
/// (<see cref="ChartTarget"/> asks <see cref="TryBlock"/>). Blocks <see cref="maxBlocks"/> rocks, then it's gone.
/// </summary>
public class DirectionalShield : MonoBehaviour
{
    public static DirectionalShield Instance { get; private set; }

    [Min(1)] public int maxBlocks = 2;
    [Tooltip("Rocks closer to the ship than this (world units) are checked against the shield. Keep it bigger than the spawner's Ship Hit Radius.")]
    public float blockRadius = 8f;
    [Tooltip("Half-width of the shield in degrees: rocks within this angle of the aim direction are blocked.")]
    [Range(1f, 90f)] public float blockAngle = 25f;
    [Tooltip("Optional. Turned so its local +Y points from the ship toward the aim point. Build the arc above the pivot.")]
    public Transform pivot;
    [Tooltip("Optional. Shown while the shield has blocks left.")]
    public GameObject visual;

    /// <summary>Raised when the shield goes up or loses a block.</summary>
    public event Action Changed;

    public int BlocksLeft { get; private set; }
    public bool Active => BlocksLeft > 0;
    /// <summary>World direction the shield faces, from the ship toward the aim point.</summary>
    public Vector3 Direction { get; private set; } = Vector3.forward;

    PlayerShooter shooter;

    void Awake()
    {
        Instance = this;
        shooter = GetComponent<PlayerShooter>();
        ShowVisual();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Raises the shield with full blocks. Does nothing if it's already up.</summary>
    public void Grant()
    {
        if (Active) return;
        BlocksLeft = maxBlocks;
        ShowVisual();
        Changed?.Invoke();
    }

    void LateUpdate()
    {
        if (!Active || shooter == null) return;

        Vector3 toAim = shooter.AimPoint - transform.position;
        if (toAim.sqrMagnitude > 0.0001f) Direction = toAim.normalized;

        // LookRotation points +Z along the vector; the extra 90° turn puts +Y there instead.
        if (pivot != null)
            pivot.rotation = Quaternion.LookRotation(Direction, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>Blocks the rock if it's inside the shield's cone.</summary>
    /// <returns>True if the rock was blocked; the caller destroys it.</returns>
    public bool TryBlock(Vector3 rockPosition)
    {
        if (!Active) return false;

        Vector3 toRock = rockPosition - transform.position;
        if (toRock.sqrMagnitude < 0.0001f) return false;
        if (Vector3.Angle(Direction, toRock) > blockAngle) return false;

        BlocksLeft--;
        ShowVisual();
        Changed?.Invoke();
        return true;
    }

    void ShowVisual()
    {
        if (visual != null) visual.SetActive(Active);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.75f, 0.6f, 1f);
        Vector3 origin = transform.position;
        Vector3 dir = Application.isPlaying ? Direction : transform.forward;
        Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        Gizmos.DrawLine(origin, origin + dir * blockRadius);
        Gizmos.DrawLine(origin, origin + Quaternion.AngleAxis(blockAngle, side) * dir * blockRadius);
        Gizmos.DrawLine(origin, origin + Quaternion.AngleAxis(-blockAngle, side) * dir * blockRadius);
        Gizmos.DrawWireSphere(origin, blockRadius);
    }
}
