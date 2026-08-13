using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private Rigidbody rb;

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
    public void TriggerHit(int hitType) { if (animator != null) { animator.SetInteger("HitType", hitType); animator.SetTrigger("Hit"); } }
    public void TriggerDeath() { if (animator != null) animator.SetTrigger("Death"); }

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