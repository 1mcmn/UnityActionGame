using UnityEngine;

public class EnemyAnimationRelay : MonoBehaviour
{
    private GreatSwordEnemyBrain _brain;
    private Animator _animator;
    public void Bind(GreatSwordEnemyBrain brain, Animator animator) { _brain = brain; _animator = animator; }
    private void Awake() { Bind(GetComponentInParent<GreatSwordEnemyBrain>(), GetComponent<Animator>()); }
    private void OnAnimatorMove() { if (_brain != null && _brain.isActiveAndEnabled) _brain.ApplyRootMotion(_animator); }
    public void EnemyHitOpen(AnimationEvent e) { _brain?.OnAttackEvent(e, true); }
    public void EnemyHitClose(AnimationEvent e) { _brain?.OnAttackEvent(e, false); }
}
