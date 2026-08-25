using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    // 对外的 C# 属性（只是访问器，不参与序列化，SerializedObject 不认这个）
    public int CurrentLevel => level;
    public float CurrentHealth => health;

    // ========== 真正被 Unity 序列化的字段 ==========
    // SerializedObject 操作的永远是这些带 [SerializeField] 的字段
    [SerializeField] private int level = 1;
    [SerializeField] public float health = 100f;
    [SerializeField] private bool isInvincible = false;
    [SerializeField] private Vector3 moveSpeed = Vector3.one;
}