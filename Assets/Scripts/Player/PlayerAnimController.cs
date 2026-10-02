using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private Rigidbody rb;
    private ThirdPersonController controller;
    private PlayerCombat combat;
    private PlayerAirCombat airCombat;
    private Collider motionCollider;
    public AnimationClip comboPlaceholderA;
    public AnimationClip comboPlaceholderB;
    public string idleStateName = "Base Layer.idle";
    public string locomotionStateName = "Base Layer.Movement.Locomotion";
    [HideInInspector] public string dodgeStateName, parryStateName, hitFrontStateName, hitLeftStateName, hitRightStateName;
    [HideInInspector] public string deathStateName, blockStateName, knockdownStateName;

    [Header("Run 动画朝向校正（Humanoid，移动状态生效）")]
    [Tooltip("动画求值后分别对齐Run的骨盆与可见前向，减少侧身跑；只调整骨骼姿态，不改变移动路线。无需IK Pass。")]
    public bool correctRunHeading = true;
    [Tooltip("留空时按当前控制器中明确名为run的片段查找；换素材时可明确指定该移动片段。")]
    public AnimationClip runHeadingClip;
    [Tooltip("自动对齐后的额外水平角度微调，单位度。0为对齐角色正前方。")]
    [Range(-180f, 180f)] public float runHeadingOffsetDegrees;
    private AnimationClip resolvedRunClip;
    private Animator headingAnimator;
    private RuntimeAnimatorController headingController;
    private AnimationClip headingClipSelection;
    private Avatar headingAvatar;
    private Transform headingHips;
    private Transform headingFace;
    private Transform headingTorso;
    private bool runHeadingPosePending;
    private readonly System.Collections.Generic.List<AnimatorClipInfo> headingClips = new System.Collections.Generic.List<AnimatorClipInfo>();
    private int dodgePlaybackHash;
    private bool dodgeStateObserved;

    [Header("泄力（格挡成功时攻击动画倒放）")]
    [Tooltip("倒放持续时长（秒）≈ 攻击动画长度 ÷ 倒放倍速，按手感调")]
    [SerializeField] private float deflectDuration = 0.3f;

    public void Initialize()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // 根位移与MoveRotation使用同一物理时钟，避免渲染帧重复覆盖MovePosition目标。
            animator.updateMode = AnimatorUpdateMode.AnimatePhysics;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var relay = animator.GetComponent<AnimationEventRelay>();
            if (relay == null) relay = animator.gameObject.AddComponent<AnimationEventRelay>();
            relay.Bind(GetComponent<PlayerCombat>(), this);
            CacheRunHeading();
            runHeadingPosePending = false;
        }
        rb = GetComponent<Rigidbody>();
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
        airCombat = GetComponent<PlayerAirCombat>(); motionCollider = GetComponent<Collider>();
    }

    public void SetMovement(float value) { if (animator != null) animator.SetFloat("Movement", value); }
    public void SetLastMoveSpeed(float value) { if (animator != null) animator.SetFloat("LastMoveSpeed", value); }
    public void SetRun(bool value) { if (animator != null) animator.SetBool("Run", value); }

    public void TriggerAttack()
    {
        if (animator == null) return;
        animator.ResetTrigger("NextAttack");   // 清上一套连招残留的 NextAttack，防第一段被跳过（A2）
        animator.SetTrigger("Attack");
    }

    /// <summary>当前动画进度 0~1（循环取余）。供连击窗口判定（A2）。</summary>
    public float GetNormalizedTime01(int layer = 0)
    {
        if (animator == null) return 0f;
        float t = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
        return t % 1f;
    }
    public void TriggerNextAttack() { if (animator != null) { animator.ResetTrigger("NextAttack"); animator.SetTrigger("NextAttack"); } }
    public void TriggerDodge()
    {
        dodgePlaybackHash = 0;
        dodgeStateObserved = false;
        if (animator == null) return;
        ClearAttackTriggers();
        animator.ResetTrigger("Dodge");
        animator.speed = 1f;
        if (PlayMappedState(dodgeStateName)) dodgePlaybackHash = Animator.StringToHash(dodgeStateName);
        else
        {
            animator.SetTrigger("Dodge");
            Debug.LogWarning("[玩家动画] 闪避状态未映射，使用旧触发器与结束时间回退。请检查演示接线。", this);
        }
    }

    /// <summary>读取未取余的动画进度；变速或顿帧时仍等到闪避片段末尾。</summary>
    public bool DodgePlaybackFinished(float elapsed)
    {
        if (animator == null || !animator.isActiveAndEnabled || dodgePlaybackHash == 0) return elapsed >= .4f;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            if (next.fullPathHash == dodgePlaybackHash) state = next;
        }
        if (state.fullPathHash == dodgePlaybackHash)
        {
            dodgeStateObserved = true;
            return state.normalizedTime >= 1f;
        }
        if (dodgeStateObserved || elapsed > .5f)
        {
            // 缺状态或外部逻辑切走时清理动作；不能留下永久Dodge锁定。
            Debug.LogWarning("[玩家动画] 闪避未进入映射状态或被其他动画提前切走，请检查控制器过渡。", this);
            return true;
        }
        return false;
    }
    public void TriggerParry() { if (animator != null && !PlayMappedState(parryStateName)) animator.SetTrigger("Parry"); }
    public void SetBlocking(bool value)
    {
        if (animator == null) return;
        animator.SetBool("Blocking", value);
        if (value) PlayMappedState(blockStateName);
    }

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
        if (!PlayMappedState(knockdownStateName)) animator.SetTrigger("Knockdown");
    }

    public void TriggerGetUp() { if (animator != null) animator.SetTrigger("GetUp"); }

    public void TriggerHit(int hitType)
    {
        if (animator == null) return;
        CancelInvoke(nameof(OnDeflectEnd));
        animator.ResetTrigger("DeflectEnd");
        animator.SetInteger("HitType", hitType);
        string state = hitType == 1 ? hitLeftStateName : (hitType == 2 ? hitRightStateName : hitFrontStateName);
        if (!PlayMappedState(state)) animator.SetTrigger("Hit");
    }

    public void TriggerDeath()
    {
        if (animator == null) return;
        CancelInvoke(nameof(OnDeflectEnd));
        animator.ResetTrigger("DeflectEnd");
        if (!PlayMappedState(deathStateName)) animator.SetTrigger("Death");
    }

    public bool IsInState(string name, int layer = 0)
    {
        if (animator == null) return false;
        return animator.GetCurrentAnimatorStateInfo(layer).IsName(name);
    }

    public void ClearAttackTriggers()
    {
        if (animator == null) return;
        animator.ResetTrigger("Attack");
        animator.ResetTrigger("NextAttack");
    }

    private bool PlayMappedState(string state)
    {
        if (animator == null || string.IsNullOrEmpty(state) || !animator.HasState(0, Animator.StringToHash(state))) return false;
        animator.CrossFadeInFixedTime(state, .05f, 0, 0f);
        return true;
    }

    public void ReturnToLocomotion(bool idle = false)
    {
        if (animator == null) return;
        ClearAttackTriggers();
        animator.ResetTrigger("Dodge");
        dodgePlaybackHash = 0;
        dodgeStateObserved = false;
        animator.speed = 1f;
        string state = idle ? idleStateName : locomotionStateName;
        if (idle && (string.IsNullOrEmpty(state) || !animator.HasState(0, Animator.StringToHash(state))))
            state = animator.HasState(0, Animator.StringToHash("Base Layer.idle")) ? "Base Layer.idle" :
                animator.HasState(0, Animator.StringToHash("Base Layer.Idle")) ? "Base Layer.Idle" : locomotionStateName;
        if (!string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state)))
            animator.CrossFadeInFixedTime(state, .08f, 0, 0f);
    }

    public void TriggerJump()
    {
        if (animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.ComboJump")))
            animator.CrossFadeInFixedTime("Base Layer.ComboJump", .08f);
    }

    private AnimationClip ResolveRunClip()
    {
        if (runHeadingClip != null) return runHeadingClip;
        if (animator == null || animator.runtimeAnimatorController == null) return null;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            if (clip != null && string.Equals(clip.name, "run", System.StringComparison.OrdinalIgnoreCase)) return clip;
        return null;
    }

    private void CacheRunHeading()
    {
        headingAnimator = animator;
        headingController = animator != null ? animator.runtimeAnimatorController : null;
        headingClipSelection = runHeadingClip;
        resolvedRunClip = ResolveRunClip();
        headingAvatar = animator != null ? animator.avatar : null;
        headingHips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        headingFace = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        headingTorso = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Spine) : null;
    }

    private void LateUpdate()
    {
        // 渲染帧可能没有动画物理步；此时不能再次校正上一次已修改的骨骼姿态。
        if (!runHeadingPosePending) return;
        runHeadingPosePending = false;
        ApplyRunHeading(animator);
    }

    /// <summary>动画与IK求值后分别对齐Run骨盆与上身。当前VRM的骨盆/头部+Z为前向，动画每帧重写姿态。</summary>
    public void ApplyRunHeading(Animator source)
    {
        if (!correctRunHeading || source == null || source != animator || !source.isActiveAndEnabled || !source.isHuman || source.runtimeAnimatorController == null ||
            controller == null || !controller.enabled || controller.StateGroup != PlayerStateGroup.Locomotion || controller.State == PlayerState.Jump)
            return;
        if (headingAnimator != source || headingController != source.runtimeAnimatorController || headingClipSelection != runHeadingClip || headingAvatar != source.avatar)
            CacheRunHeading();
        if (resolvedRunClip == null || headingHips == null || headingHips == source.transform || !headingHips.IsChildOf(source.transform)) return;
        float weight = RunClipWeight(source, false);
        if (source.IsInTransition(0))
        {
            float transition = Mathf.Clamp01(source.GetAnimatorTransitionInfo(0).normalizedTime);
            weight = weight * (1f - transition) + RunClipWeight(source, true) * transition;
        }
        if (weight <= .0001f) return;
        // 使用本帧渲染根朝向，避免Rigidbody插值让物理朝向与模型暂时不同。
        Vector3 rootForward = transform.forward;
        Vector3 poseForward = headingFace != null ? headingFace.forward : headingHips.forward;
        Quaternion faceCorrection = CalculateRunHeadingCorrection(poseForward, rootForward, weight, runHeadingOffsetDegrees);
        bool splitTorso = headingFace != null && headingTorso != null && headingTorso != headingHips &&
            headingTorso.IsChildOf(headingHips) && headingFace.IsChildOf(headingTorso);
        Quaternion hipCorrection = splitTorso
            ? CalculateRunHeadingCorrection(headingHips.forward, rootForward, weight, runHeadingOffsetDegrees)
            : faceCorrection;
        Vector3 pivot = source.transform.position;
        headingHips.position = pivot + hipCorrection * (headingHips.position - pivot);
        headingHips.rotation = hipCorrection * headingHips.rotation;
        if (splitTorso)
        {
            // 上身已经随骨盆旋转：只补剩余角度，避免重复校正或把原骨盆偏角传给头部。
            Quaternion torsoCorrection = faceCorrection * Quaternion.Inverse(hipCorrection);
            headingTorso.rotation = torsoCorrection * headingTorso.rotation;
        }
    }

    private float RunClipWeight(Animator source, bool next)
    {
        headingClips.Clear();
        if (next) source.GetNextAnimatorClipInfo(0, headingClips);
        else source.GetCurrentAnimatorClipInfo(0, headingClips);
        float weight = 0f;
        foreach (var info in headingClips) if (info.clip == resolvedRunClip) weight += info.weight;
        return Mathf.Clamp01(weight);
    }

    public static Quaternion CalculateRunHeadingCorrection(Quaternion bodyRotation, Vector3 rootForward, float weight, float extraDegrees)
    {
        return CalculateRunHeadingCorrection(bodyRotation * Vector3.forward, rootForward, weight, extraDegrees);
    }

    public static Quaternion CalculateRunHeadingCorrection(Vector3 poseForward, Vector3 rootForward, float weight, float extraDegrees)
    {
        if (!ComboData.Finite(weight) || !ComboData.Finite(extraDegrees)) return Quaternion.identity;
        if (!ComboData.Finite(poseForward.x) || !ComboData.Finite(poseForward.z) ||
            !ComboData.Finite(rootForward.x) || !ComboData.Finite(rootForward.z)) return Quaternion.identity;
        poseForward.y = rootForward.y = 0f;
        if (poseForward.sqrMagnitude < .0001f || rootForward.sqrMagnitude < .0001f) return Quaternion.identity;
        float degrees = Vector3.SignedAngle(poseForward, rootForward, Vector3.up) + extraDegrees;
        return Quaternion.AngleAxis(degrees * Mathf.Clamp01(weight), Vector3.up);
    }

    private void OnAnimatorMove()
    {
        // Animator在子对象时由事件转发器调用，避免同一帧应用两次。
        if (animator != null && animator.gameObject == gameObject) ApplyRootMotion(animator);
    }

    public void ApplyRootMotion(Animator source)
    {
        if (source == null || source != animator) return;
        runHeadingPosePending = true;
        if (rb != null && controller != null && controller.enabled)
        {
            float deltaTime = source.updateMode == AnimatorUpdateMode.AnimatePhysics ? Time.fixedDeltaTime : Time.deltaTime;
            Vector3 delta = controller.EvaluateRootMotion(source.deltaPosition, deltaTime);
            if (airCombat != null) delta = CombatPhysics.LimitMotion(rb, motionCollider, delta);
            Quaternion rotation = controller.StateGroup == PlayerStateGroup.Action ? rb.rotation * source.deltaRotation : rb.rotation;
            if (controller.State == PlayerState.AirAction && airCombat != null) rotation = airCombat.ActionRotation;
            Vector3 targetPosition = rb.position + delta;
            combat?.NotifyComboRootPosition(targetPosition, rotation);
            rb.MovePosition(targetPosition);
            if (controller.StateGroup == PlayerStateGroup.Action) rb.MoveRotation(rotation);
            if (controller.State == PlayerState.AirAction && airCombat != null) airCombat.AfterRootMotion(targetPosition, rotation);
        }
        // 物理动画在Update前求值，逐次采样，避免收招清理或同帧多个物理步吞掉合法判定。
        if (source.updateMode == AnimatorUpdateMode.AnimatePhysics) combat?.SampleComboHitAfterRootMotion();
    }
}
