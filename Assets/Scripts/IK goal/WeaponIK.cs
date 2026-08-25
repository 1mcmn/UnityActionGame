using UnityEngine;

[RequireComponent(typeof(Animator))]
public class WeaponIK : MonoBehaviour
{
    [Header("左手（抓剑柄下部）")]
    [Tooltip("剑柄下握点，如 Target_Left")]
    public Transform leftHandTarget;
    [Tooltip("左肘提示点，如 Left_Elbow_Hint")]
    public Transform leftHint;
    [Range(0f, 1f)] public float leftHandWeight = 1f;

    [Header("右手（武器挂在右手骨下时通常关闭，避免循环依赖）")]
    [Tooltip("勾选后才启用右手 IK；目标必须指向独立物体（不是武器子物体）")]
    public bool enableRightHandIK = false;
    public Transform rightHandTarget;
    public Transform rightHint;
    [Range(0f, 1f)] public float rightHandWeight = 1f;

    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;
        // 只在 Base Layer 处理一次，避免多层重复调用 / 层权重为 0 导致 IK 失效
        if (layerIndex != 0) return;

        // 左手 IK
        if (leftHandTarget != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftHandWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftHandWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget.position);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandTarget.rotation);
        }
        if (leftHint != null)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftHandWeight);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftHint.position);
        }

        // 右手 IK（默认关闭）
        if (enableRightHandIK && rightHandTarget != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightHandWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rightHandWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget.position);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandTarget.rotation);
        }
        if (enableRightHandIK && rightHint != null)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, rightHandWeight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, rightHint.position);
        }
    }
}
