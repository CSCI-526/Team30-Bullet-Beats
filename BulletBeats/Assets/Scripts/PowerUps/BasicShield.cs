using System;
using UnityEngine;

/// <summary>
/// One extra hit on top of the ship's health. <see cref="PlayerHealth.TakeDamage"/> breaks it before
/// any health is lost, and healing never refills it. Granted by a <see cref="PowerUpPickup"/>.
/// </summary>
public class BasicShield : MonoBehaviour
{
    [Tooltip("Optional bubble shown while the shield is up.")]
    public GameObject visual;

    /// <summary>Raised when the shield goes up or breaks, with the new state.</summary>
    public event Action<bool> Changed;

    public bool Active { get; private set; }

    void Awake() => ShowVisual();

    /// <summary>Raises the shield. Does nothing if it's already up.</summary>
    public void Grant()
    {
        if (Active) return;
        Active = true;
        ShowVisual();
        Changed?.Invoke(true);
    }

    /// <returns>True if the shield took the hit (and broke).</returns>
    public bool TryAbsorb()
    {
        if (!Active) return false;
        Active = false;
        ShowVisual();
        Changed?.Invoke(false);
        return true;
    }

    void ShowVisual()
    {
        if (visual != null) visual.SetActive(Active);
    }
}
