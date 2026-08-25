using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private Rigidbody rb;

    [Header("泄力（格挡成功时攻击动画倒放）")]
    [Tooltip("倒放持续时长（秒）≈ 攻击动画长度 ÷ 倒放倍速，按手感调")]
    [SerializeField] private float deflectDuration = 0.3f;

    public void Initialize()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.updateMode = AnimatorUpdateMode.Normal;
        }
        rb = GetComponent<Rigidbody>();
    }

    public void SetMovement(float value) { if (animator != null) animator.SetFloat("Movement", value); }
    public void SetLastMoveSpeed(float value) { if (animator != null) animator.SetFloat("LastMoveSpeed", value); }
    public void SetRun(bool value) { if (animator != null) animator.SetBool("Run", value); }

    public void TriggerAttack() { if (animator != null) animator.SetTrigger("Attack"); }
    public void TriggerNextAttack() { if (animator != null) { animator.ResetTrigger("NextAttack"); animator.SetTrigger("NextAttack"); } }
    public void TriggerDodge() { if (animator != null) animator.SetTrigger("Dodge"); }
    public void TriggerParry() { if (animator != null) animator.SetTrigger("Parry"); }
    public void SetBlocking(bool value) { if (animator != null) animator.SetBool("Blocking", value); }

    /// <summary>
    /// 泄力：让动画机播 "Deflect" 状态，并从收尾帧（1f=最后一帧）开始播。
    /// 倒放本身由 Deflect 状态的负 Speed 完成（动画机里设），代码只做两件事：
    /// 1) Play(..., 1f) —— 从最后一帧开始（非循环动画倒放必须从末尾起步，否则卡第一帧）；
    /// 2) Invoke —— 播够 deflectDuration 秒后，抛 DeflectEnd 触发器，让动画机走 Deflect → Block 过渡。
    /// </summary>
    public void TriggerDeflect()
    {
        if (animator == null) return;
        animator.ResetTrigger("DeflectEnd");
        CancelInvoke(nameof(OnDeflectEnd));             // 连挡时取消上一个倒计时，防止触发器堆积、泄力被提前打断
        animator.Play("Deflect", 0, 1f);                // 从攻击动画的最后一帧开始播
        Invoke(nameof(OnDeflectEnd), deflectDuration);  // 定时结束，通知动画机回格挡
    }

    private void OnDeflectEnd()
    {
        if (animator == null) return;
        animator.SetTrigger("DeflectEnd");              // 动画机里 Deflect → Block 过渡用这个触发条件
    }

    public void TriggerKnockdown()
    {
        if (animator == null) return;
        CancelInvoke(nameof(OnDeflectEnd));
        animator.ResetTrigger("DeflectEnd");
        animator.SetTrigger("Knockdown");
    }

    public void TriggerGetUp() { if (animator != null) animator.SetTrigger("GetUp"); }

    public void TriggerHit(int hitType)
    {
        if (animator == null) return;
        CancelInvoke(nameof(OnDeflectEnd));
        animator.ResetTrigger("DeflectEnd");
        animator.SetInteger("HitType", hitType);
        animator.SetTrigger("Hit");
    }

    public void TriggerDeath()
    {
        if (animator == null) return;
        CancelInvoke(nameof(OnDeflectEnd));
        animator.ResetTrigger("DeflectEnd");
        animator.SetTrigger("Death");
    }

    public bool IsInState(string name, int layer = 0)
    {
        if (animator == null) return false;
        return animator.GetCurrentAnimatorStateInfo(layer).IsName(name);
    }

    private void OnAnimatorMove()
    {
        if (animator == null || rb == null) return;
        rb.MovePosition(rb.position + animator.deltaPosition);
        rb.MoveRotation(rb.rotation * animator.deltaRotation);
    }
}
