using UnityEngine;
using System.Collections;

public class Enemy : MonoBehaviour
{
    public EnemyData enemyData;
    private float currentHealth;

    [Header("攻击")]
    public bool isAttacking;
    [SerializeField] private float attackInterval = 2.5f;
    [SerializeField] private float attackWindup = 0.4f;
    [SerializeField] private float attackRange = 3f;
    [SerializeField] private float attackDamage = 15f;
    [SerializeField] private LayerMask playerLayer;

    private Transform player;
    private float attackCooldown;

    private void Start()
    {
        if (enemyData != null)
            currentHealth = enemyData.maxHealth;
        else
        {
            Debug.LogError("【配置错误】敌人挂载了 Enemy 脚本，但 Enemy Data 没有赋值！");
            currentHealth = 100f;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
    }

    private void Update()
    {
        if (player == null) return;

        attackCooldown -= Time.deltaTime;
        if (attackCooldown > 0f) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            StartCoroutine(AttackRoutine());
            attackCooldown = attackInterval;
        }
    }

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        yield return new WaitForSeconds(attackWindup);

        if (player == null) { isAttacking = false; yield break; }

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            ThirdPersonController pc = player.GetComponent<ThirdPersonController>();
            if (pc != null) pc.TryTakeDamage(attackDamage);
        }

        isAttacking = false;
    }

    public void TakeDamage(float damage, Vector3 knockbackDir)
    {
        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }
}