using UnityEngine;

/// <summary>
/// 放在 Animator 所在 GameObject 上。
/// 所有 Animation Event 统一由此转发。
/// 攻击音效通过 Animation Event 的 String 参数指定前缀（一一对应）。
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    [Header("攻击")]
    [SerializeField] private PlayerCombat _combat;

    // ========== 攻击：带参（Animation Event String 栏填前缀） ==========

    /// <summary>Animation Event String 填前缀，如 atk01_swing</summary>
    public void PlaySfx(string prefix)
    {
        if (_combat != null) _combat.PlaySfx(prefix);
    }

    public void PerformHitDetection()
    {
        if (_combat != null) _combat.PerformHitDetection();
    }

    /// <summary>Animation Event String 填前缀，如 atk01_hit</summary>
    public void PlayHitSfx(string prefix)
    {
        if (_combat != null) _combat.PlayHitSfx(prefix);
    }

    public void TriggerHitStop()
    {
        if (_combat != null) _combat.TriggerHitStop();
    }

    // ========== 脚步 ==========

    /// <summary>普通脚步（Animation Event 无参调用），从 foot_step_* 中随机选取，pitch 0.9~1.1</summary>
    public void PlayFootSound()       { if (_combat != null) _combat.PlayFootstepSfx("foot_step"); }

    /// <summary>收脚/重踏（Animation Event 无参调用），从 foot_back_* 中随机选取，pitch 0.9~1.1</summary>
    public void PlayFootBackSound()   { if (_combat != null) _combat.PlayFootstepSfx("foot_back"); }

    /// <summary>随机脚步（Animation Event 无参调用），等同 PlayFootSound</summary>
    public void PlayRandomFootstep()  { if (_combat != null) _combat.PlayFootstepSfx("foot_step"); }

    // ========== 其他 ==========

    public void PlayRandomVoice()     { }
    public void PlaySkillSfx()        { }
    public void PlaySheathSfx()       { if (_combat != null) _combat.PlaySfx("sword_sheath"); }
    public void PlayScabbardSfx()     { if (_combat != null) _combat.PlaySfx("sword_scabbard"); }
    public void PlayWeaponEndSound()  { if (_combat != null) _combat.PlaySfx("weapon_end"); }
    public void PlayWeaponBackSound() { if (_combat != null) _combat.PlaySfx("weapon_back"); }

    // ========== 模型自带（消除 warning，空方法） ==========

    public void PlayVFX()             { }
    public void ATK()                 { }
    public void EnablePreInput()      { }
    public void DisableLinkCombo()    { }
    public void CancelAttackColdTime(){ }
    public void NewEvent()            { }
}