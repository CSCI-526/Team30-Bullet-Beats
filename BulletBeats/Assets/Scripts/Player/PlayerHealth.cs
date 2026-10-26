using System;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The ship's hit points. Anything that hurts the player calls <see cref="TakeDamage"/>, e.g.
/// <c>PlayerHealth.Instance?.TakeDamage(1)</c>; asteroid notes that reach their beat without being
/// shot do this (<see cref="ChartTarget"/>).
/// Hits are ignored while dashing (<see cref="PlayerMovement.IsInvulnerable"/>) and for a moment after
/// each hit. Shots on the beat (Perfect / Good) build a streak of notes; every
/// <see cref="healStreak"/> notes in a row heals the ship. A Miss or taking a hit resets the streak.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Min(1)] public int maxHealth = 5;
    [Tooltip("Health never drops below this (1 = the ship can't die, e.g. in the tutorial).")]
    [Min(0)] public int minHealth = 0;
    [Tooltip("Seconds the ship can't be hit again after taking a hit.")]
    public float hitInvulnerableTime = 0.6f;

    [Header("Healing")]
    [Tooltip("Notes in a row (Perfect or Good shots) needed to heal.")]
    [Min(1)] public int healStreak = 8;
    [Min(1)] public int healAmount = 1;

    [Header("Testing")]
    [Tooltip("Editor and development builds only: press K to take a hit, until enemies deal damage.")]
    public bool debugDamageKey = true;

    /// <summary>Raised whenever <see cref="Health"/> changes, with the new value.</summary>
    public event Action<int> HealthChanged;
    /// <summary>Raised when a hit lands, with the damage taken (e.g. for a damage flash).</summary>
    public event Action<int> Damaged;
    /// <summary>Raised when the streak heals the ship, with the health gained.</summary>
    public event Action<int> Healed;
    /// <summary>Raised when the streak changes, with the new streak.</summary>
    public event Action<int> StreakChanged;
    /// <summary>Raised once when health reaches 0.</summary>
    public event Action Died;

    public int Health { get; private set; }
    /// <summary>Notes hit on the beat in a row.</summary>
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }
    public bool IsDead => Health <= 0;
    /// <summary>Notes still needed for the next heal.</summary>
    public int NotesUntilHeal => healStreak - Streak % healStreak;
    public bool IsInvulnerable => Time.time < invulnerableUntil || (movement != null && movement.IsInvulnerable);

    PlayerShooter shooter;
    PlayerMovement movement;
    float invulnerableUntil = float.NegativeInfinity;

    void Awake()
    {
        shooter = GetComponent<PlayerShooter>();
        movement = GetComponent<PlayerMovement>();
        Health = maxHealth;
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (shooter != null) shooter.ShotFired += OnShotFired;
    }

    void OnDisable()
    {
        if (shooter != null) shooter.ShotFired -= OnShotFired;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void Update()
    {
        if (debugDamageKey && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            TakeDamage(1);
    }
#endif

    /// <summary>Deals damage unless the ship is invulnerable or already dead.</summary>
    /// <returns>True if the hit landed.</returns>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || IsInvulnerable) return false;

        invulnerableUntil = Time.time + hitInvulnerableTime;
        SetHealth(Health - amount);
        SetStreak(0);
        Damaged?.Invoke(amount);
        if (IsDead) Died?.Invoke();
        return true;
    }

    //<summary> Adds health as reward for a turbo asteroid </summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = Health;
        SetHealth(Health + amount);
        Healed?.Invoke(Health - before);
    }

    [ContextMenu("Take 1 damage")]
    void TakeOneDamage() => TakeDamage(1);

    void OnShotFired(BeatJudgment judgment)
    {
        if (IsDead) return;
        if (judgment == BeatJudgment.Miss)
        {
            SetStreak(0);
            return;
        }

        if (judgment == BeatJudgment.Fine) return;

        SetStreak(Streak + 1);
        if (judgment == BeatJudgment.Perfect)
        {
            int before = Health;
            SetHealth(Health + healAmount);
            Healed?.Invoke(Health - before);
        }
    }

    void SetHealth(int value)
    {
        value = Mathf.Max(minHealth, value);
        if (value == Health) return;
        Health = value;
        HealthChanged?.Invoke(Health);
    }

    void SetStreak(int value)
    {
        if (value == Streak) return;
        Streak = value;
        if (Streak > BestStreak) BestStreak = Streak;
        StreakChanged?.Invoke(Streak);
    }
}
