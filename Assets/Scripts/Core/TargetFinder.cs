using UnityEngine;

/// <summary>
/// 战斗目标查找工具。从 ThirdPersonController 抽出，减少上帝类耦合。
/// </summary>
public static class TargetFinder
{
    /// <summary>
    /// 在半径内找到距离 from 最近的带 Collider 目标（忽略 Y 轴）。
    /// </summary>
    public static Transform FindNearest(Transform from, LayerMask mask, float radius)
    {
        if (from == null) return null;

        Collider[] cols = Physics.OverlapSphere(from.position, radius, mask);
        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (Collider c in cols)
        {
            Vector3 toTarget = c.transform.position - from.position;
            toTarget.y = 0f;
            float d = toTarget.sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = c.transform;
            }
        }
        return best;
    }
}
