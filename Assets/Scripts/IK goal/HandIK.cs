using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HandIK : MonoBehaviour
{
    public Transform target;          // 要抓取的目标
    public AvatarIKGoal hand = AvatarIKGoal.RightHand;
    public float weight = 1f;

    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        animator.SetIKPositionWeight(hand, weight);
        animator.SetIKRotationWeight(hand, weight);

        animator.SetIKPosition(hand, target.position);
        animator.SetIKRotation(hand, target.rotation);
    }
}