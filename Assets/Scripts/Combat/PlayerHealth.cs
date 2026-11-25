using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 20;

    private int currentHealth;

    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    [Header("Shield")]
    public int CurrentShield { get; private set; }


    private void Awake()
    {
        currentHealth = maxHealth;
        CurrentShield = 0;
    }


    public void TakeDamage(int damage)
    {
        if (IsDead || damage <= 0)
            return;

        // Shield absorbs damage first
        int damageToShield = Mathf.Min(CurrentShield, damage);

        CurrentShield -= damageToShield;
        damage -= damageToShield;

        // Remaining damage goes to health
        if (damage > 0)
            currentHealth -= damage;

        currentHealth = Mathf.Max(currentHealth, 0);

        Debug.Log(
            $"[Player] Took damage. " +
            $"Health: {currentHealth}/{maxHealth} | " +
            $"Shield: {CurrentShield}"
        );

        if (IsDead)
            Die();
    }


    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
            return;

        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);

        Debug.Log(
            $"[Player] Healed {amount}. " +
            $"Health: {currentHealth}/{maxHealth}"
        );
    }


    public void AddShield(int amount)
    {
        if (IsDead || amount <= 0)
            return;

        CurrentShield += amount;

        Debug.Log(
            $"[Player] Gained {amount} shield. " +
            $"Health: {currentHealth}/{maxHealth} | " +
            $"Shield: {CurrentShield}"
        );
    }


    public void ClearShield()
    {
        CurrentShield = 0;

        Debug.Log("[Player] Shield cleared.");
    }


    private void Die()
    {
        Debug.Log("[Player] Player died.");

        // Death behaviour will be implemented later.
    }

    public void IncreaseMaxHealthAndFullyHeal(int amount)
    {
        if (IsDead || amount <= 0)
            return;

        maxHealth += amount;
        currentHealth = maxHealth;

        Debug.Log(
            $"[Player] Max health increased by {amount}. " +
            $"Fully healed: {currentHealth}/{maxHealth}"
        );
    }
}