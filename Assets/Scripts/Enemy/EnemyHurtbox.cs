using UnityEngine;

/// <summary>只有该碰撞体承受攻击；根胶囊负责物理阻挡。</summary>
[RequireComponent(typeof(CapsuleCollider))]
public class EnemyHurtbox : MonoBehaviour
{
    public Enemy Owner { get; private set; }
    private CapsuleCollider _capsule;
    private void Awake() { Owner = GetComponentInParent<Enemy>(); _capsule = GetComponent<CapsuleCollider>(); }
    public void SetProne(bool prone)
    {
        if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
        _capsule.direction = prone ? 2 : 1;
        _capsule.center = prone ? new Vector3(0, .3f, .2f) : new Vector3(0, .9f, 0);
        _capsule.height = prone ? 1.3f : 1.65f;
    }
}
