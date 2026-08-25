using UnityEngine;

/// <summary>
/// 把武器 prefab 挂到角色手部骨骼上（玩家/怪物通用，放在 Core 供两边复用）。
/// 阶段 1：纯视觉挂载（本文件）；阶段 2：在此基础上加武器级攻击判定（hitbox，由动画事件/动作编辑器驱动）。
/// 用法：挂在带 Animator 的角色上 → weaponPrefab 拖入武器（如 PP_Theme_11_Sword_One-Handed_003）→
/// 进 Play Mode 边看边调 localPosition / localEulerAngles / localScale → 满意后把值复制回 Inspector。
/// </summary>
[RequireComponent(typeof(Animator))]
public class WeaponAttachment : MonoBehaviour
{
    [Header("武器")]
    [Tooltip("要挂到手上的武器 prefab")]
    [SerializeField] private GameObject weaponPrefab;

    [Header("挂点")]
    [Tooltip("手部骨骼（Humanoid 通用）")]
    [SerializeField] private HumanBodyBones handBone = HumanBodyBones.RightHand;
    [Tooltip("非 Humanoid 骨架 / 手骨拿不到时，手动指定手部 Transform（优先于 handBone）")]
    [SerializeField] private Transform overrideBone;

    [Header("微调（Play Mode 里调完复制回这里）")]
    [SerializeField] private Vector3 localPosition = Vector3.zero;
    [SerializeField] private Vector3 localEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 localScale = Vector3.one;

    [Tooltip("把武器自带的碰撞体改成 Trigger，避免跟角色身体/地面乱撞（阶段 2 会换成专门的武器 hitbox）")]
    [SerializeField] private bool weaponCollidersAsTrigger = true;

    private Animator _animator;
    private GameObject _weapon;

    public GameObject Weapon => _weapon;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        AttachWeapon();
    }

    /// <summary>实例化武器并挂到手部骨骼（重复调用安全）</summary>
    public void AttachWeapon()
    {
        if (weaponPrefab == null || _weapon != null) return;

        Transform hand = overrideBone;
        if (hand == null && _animator != null && _animator.isHuman)
            hand = _animator.GetBoneTransform(handBone);
        Transform parent = hand != null ? hand : transform;

        _weapon = Instantiate(weaponPrefab, parent);
        _weapon.name = weaponPrefab.name;
        _weapon.transform.localPosition = localPosition;
        _weapon.transform.localRotation = Quaternion.Euler(localEulerAngles);
        _weapon.transform.localScale = localScale;

        if (weaponCollidersAsTrigger)
        {
            foreach (Collider col in _weapon.GetComponentsInChildren<Collider>(true))
                col.isTrigger = true;
        }
    }
}
