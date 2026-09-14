using UnityEngine;

/// <summary>
/// 连击数据资产。把攻击时长/半径/各段伤害从代码解耦，
/// 供 Inspector 直接配置，运行时读取。
/// </summary>
[CreateAssetMenu(fileName = "ComboData", menuName = "Combat/ComboData")]
public class ComboData : ScriptableObject
{
    [Header("攻击参数")]
    public float attackDuration = 0.45f;    // 每段攻击锁定时间
    public float attackRadius   = 2.5f;     // 球形判定半径

    [Header("连击手感")]
    [Tooltip("连击窗口起点（动画进度比例）：动画播到此进度后才接受连击输入，窗口外点击直接忽视。0.35 = 播到 65% 后接受（A2 修改，防动画被切半截）")]
    [Range(0.1f, 0.5f)]
    public float comboWindowPercent = 0.35f;

    [Tooltip("残响窗口：攻击计时结束后、动画回 Idle 前的兜底时长（秒），让慢点击也能续招")]
    public float comboGraceWindow = 0.15f;

    [Tooltip("最大连击段索引（0 起；4 = 共 5 段），防止越界与触发器空转")]
    public int maxComboStep = 4;

    [Header("连击伤害（第 1~N 段）")]
    public float[] comboDamages = { 15f, 18f, 22f, 28f, 35f };
}
