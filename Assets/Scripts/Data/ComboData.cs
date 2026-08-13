using UnityEngine;

/// <summary>
/// 连击数据资产。把攻击时长/半径/各段伤害从代码解耦，
/// 供技能编辑器（Skill Editor）可视化编辑，运行时读取。
/// </summary>
[CreateAssetMenu(fileName = "ComboData", menuName = "Combat/ComboData")]
public class ComboData : ScriptableObject
{
    [Header("攻击参数")]
    public float attackDuration = 0.45f;    // 每段攻击锁定时间
    public float attackRadius   = 2.5f;     // 球形判定半径

    [Header("连击手感")]
    [Tooltip("连击窗口：攻击进度越过此比例（0~1）后允许接下一段")]
    [Range(0.1f, 0.9f)]
    public float comboWindowPercent = 0.55f;

    [Header("连击伤害（第 1~N 段）")]
    public float[] comboDamages = { 15f, 18f, 22f, 28f, 35f };
}
