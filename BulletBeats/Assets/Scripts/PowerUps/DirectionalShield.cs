using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shield arc that follows the mouse. The top half of a circle around the ship (on screen) is split
/// into <see cref="slotCount"/> slots, and the shield sits in the slot the cursor points at (a cursor
/// below the ship picks the nearest end slot). Each rock is checked once, when it comes within
/// <see cref="blockRadius"/> of the ship; if it comes from the shield's slot it is destroyed
/// (<see cref="ChartTarget"/> asks <see cref="TryBlock"/>). Blocks <see cref="maxBlocks"/> rocks, then it's gone.
/// </summary>
public class DirectionalShield : MonoBehaviour
{
    public static DirectionalShield Instance { get; private set; }

    [Min(1)] public int slotCount = 6;
    [Min(1)] public int maxBlocks = 2;
    [Tooltip("Rocks closer to the ship than this (world units) are checked against the shield. Keep it bigger than the spawner's Ship Hit Radius.")]
    public float blockRadius = 8f;
    [Tooltip("Optional. Turned so its local +Y points at the current slot (local +Z along the camera's view). Build the arc above the pivot.")]
    public Transform pivot;
    [Tooltip("Optional. Shown while the shield has blocks left.")]
    public GameObject visual;

    /// <summary>Raised when the shield goes up or loses a block.</summary>
    public event Action Changed;

    public int BlocksLeft { get; private set; }
    public bool Active => BlocksLeft > 0;
    /// <summary>0 = rightmost slot, slotCount - 1 = leftmost.</summary>
    public int CurrentSlot { get; private set; }

    Camera cam;

    void Awake()
    {
        Instance = this;
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

    void Update()
    {
        if (!Active) return;
        if (cam == null) cam = DisplayCamera.Find();
        Mouse mouse = Mouse.current;
        if (cam == null || mouse == null) return;

        if (TryScreenAngle(mouse.position.ReadValue(), out float angle))
            CurrentSlot = SlotOf(angle);

        if (pivot != null)
        {
            float center = (CurrentSlot + 0.5f) * SlotDegrees * Mathf.Deg2Rad;
            Vector3 up = cam.transform.right * Mathf.Cos(center) + cam.transform.up * Mathf.Sin(center);
            pivot.rotation = Quaternion.LookRotation(cam.transform.forward, up);
        }
    }

    /// <summary>Blocks the rock if it's coming from the slot the shield is in.</summary>
    /// <returns>True if the rock was blocked; the caller destroys it.</returns>
    public bool TryBlock(Vector3 rockPosition)
    {
        if (!Active) return false;
        if (cam == null) cam = DisplayCamera.Find();
        if (cam == null) return false;

        Vector3 rock = cam.WorldToScreenPoint(rockPosition);
        if (rock.z <= 0f || !TryScreenAngle(rock, out float angle)) return false;
        if (SlotOf(angle) != CurrentSlot) return false;

        BlocksLeft--;
        ShowVisual();
        Changed?.Invoke();
        return true;
    }

    float SlotDegrees => 180f / slotCount;

    /// <summary>Angle of a screen point around the ship: 0 = right, 90 = up, 180 = left; below the ship snaps to 0 or 180.</summary>
    bool TryScreenAngle(Vector2 screenPoint, out float angle)
    {
        angle = 0f;
        Vector3 ship = cam.WorldToScreenPoint(transform.position);
        if (ship.z <= 0f) return false;
        Vector2 dir = screenPoint - new Vector2(ship.x, ship.y);
        if (dir.sqrMagnitude < 1f) return false;

        angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle = angle < -90f ? 180f : 0f;
        return true;
    }

    int SlotOf(float angle) => Mathf.Clamp(Mathf.FloorToInt(angle / SlotDegrees), 0, slotCount - 1);

    void ShowVisual()
    {
        if (visual != null) visual.SetActive(Active);
    }
}
