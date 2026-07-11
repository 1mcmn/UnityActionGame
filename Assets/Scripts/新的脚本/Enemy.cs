using UnityEngine;

public class Enemy : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    // 这是被玩家攻击时调用的方法
    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        Debug.Log($"敌人受到 {damage} 点伤害，剩余血量: {currentHealth}");

        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }
}