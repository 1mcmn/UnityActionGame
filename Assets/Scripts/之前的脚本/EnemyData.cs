using UnityEngine;

// 这一行非常关键！它告诉 Unity 在右键菜单里创建一个创建选项
[CreateAssetMenu(fileName = "New Enemy Data", menuName = "GameData/Enemy Data")]
public class EnemyData : ScriptableObject
{
    public string enemyName;      // 比如 "哥布林"
    public float maxHealth = 100f; // 最大生命值
    public float damage = 20f;    // 攻击力
    public float moveSpeed = 3f;  // 移动速度
}