using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private Rigidbody rb;
    private bool syncRootMotion;

    // Normal 模式下 OnAnimatorMove 在 Update 中触发，不能直接调 rb.MovePosition，
    // 改为缓存 delta，由 ThirdPersonController.FixedUpdate 消费。
    private Vector3 cachedDeltaPos;
    private Quaternion cachedDeltaRot = Quaternion.identity;

    public void Initialize()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
        }
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>仅在 Attack/Dodge 期间开启，Move/Run/Idle 必须关闭。</summary>
    public void SetSyncRootMotion(bool value) => syncRootMotion = value;

    /// <summary>FixedUpdate 调用，取出并清空缓存的根运动 delta。</summary>
    public (Vector3 pos, Quaternion rot) ConsumeRootMotionDelta()
    {
        var result = (cachedDeltaPos, cachedDeltaRot);
        cachedDeltaPos = Vector3.zero;
        cachedDeltaRot = Quaternion.identity;
        return result;
    }

    public void SetMovement(float value) { if (animator != null) animator.SetFloat("Movement", value); }
    public void SetLastMoveSpeed(float value) { if (animator != null) animator.SetFloat("LastMoveSpeed", value); }

    public void TriggerAttack() { if (animator != null) animator.SetTrigger("Attack"); }
    public void TriggerNextAttack() { if (animator != null) { animator.ResetTrigger("NextAttack"); animator.SetTrigger("NextAttack"); } }
    public void TriggerDodge() { if (animator != null) animator.SetTrigger("Dodge"); }
    public void TriggerParry() { if (animator != null) animator.SetTrigger("Parry"); }

    public bool IsInState(string name, int layer = 0)
    {
        if (animator == null) return false;
        return animator.GetCurrentAnimatorStateInfo(layer).IsName(name);
    }

    private void OnAnimatorMove()
    {
        if (!syncRootMotion || animator == null) return;
        cachedDeltaPos += animator.deltaPosition;
        cachedDeltaRot *= animator.deltaRotation;
    }
}