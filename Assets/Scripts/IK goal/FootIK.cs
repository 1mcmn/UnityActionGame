using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    public LayerMask groundMask;      // 地面层
    public float raycastDistance = 1.5f;
    public float heightOffset = 0.05f; // 脚离地高度
    public float ikWeight = 1f;        // IK 权重

    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        // 处理左脚
        SetFootIK(AvatarIKGoal.LeftFoot, ikWeight);
        // 处理右脚
        SetFootIK(AvatarIKGoal.RightFoot, ikWeight);
    }

    void SetFootIK(AvatarIKGoal foot, float weight)
    {
        // 获取当前脚的位置
        Vector3 footPos = animator.GetIKPosition(foot);

        // 从脚上方往下射线检测地面
        RaycastHit hit;
        if (Physics.Raycast(footPos + Vector3.up * 0.5f, Vector3.down, out hit, raycastDistance, groundMask))
        {
            Vector3 targetPos = hit.point + Vector3.up * heightOffset;
            Vector3 targetNormal = hit.normal;

            // 设置位置
            animator.SetIKPositionWeight(foot, weight);
            animator.SetIKPosition(foot, targetPos);

            // 设置旋转，让脚贴合地面法线
            animator.SetIKRotationWeight(foot, weight);
            Quaternion footRotation = Quaternion.FromToRotation(Vector3.up, targetNormal)
                                      * animator.GetIKRotation(foot);
            animator.SetIKRotation(foot, footRotation);
        }
        else
        {
            // 没有检测到地面，关闭 IK
            animator.SetIKPositionWeight(foot, 0);
            animator.SetIKRotationWeight(foot, 0);
        }
    }
}