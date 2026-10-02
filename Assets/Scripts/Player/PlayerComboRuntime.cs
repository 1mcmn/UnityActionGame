using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>逐段普攻：冻结已加载数据、内置动画事件、单次挥击去重以及安全重载。</summary>
public partial class PlayerCombat
{
    public const string ComboSpeedParameterA = "ComboSpeedA";
    public const string ComboSpeedParameterB = "ComboSpeedB";
    public ComboData Data => _loadedData != null ? _loadedData : comboData;
    public int LoadedRevision { get; private set; } = -1;
    public string ReloadStatus { get; private set; } = "使用旧参数回退";
    public bool UsesConfiguredCombo => _loadedSteps != null && _loadedSteps.Count > 0;
    public bool IsComboActive => _swingActive;
    public int CurrentStepIndex => _comboStep;
    public int StepCount => UsesConfiguredCombo ? _loadedSteps.Count : MaxComboStep + 1;
    public int AttackToken => _attackToken;
    public bool HitWindowOpen => _hitWindowOpen;
    public float CurrentStepTime => ReadConfiguredTime();
    public float CurrentStepLength => ActiveStep?.animationClip != null ? ActiveStep.animationClip.length : AttackDuration;
    public float CurrentStepPlaybackSpeed => ActiveStep != null ? ActiveStep.playbackSpeed : 1f;
    public float AttackRootMotionScale => UsesConfiguredCombo && _comboRunning ? ActiveStep.rootMotionScaleXZ : 1f;
    public bool CanCancelAttack => UsesConfiguredCombo ? _comboRunning && ActiveStep.CanCancel(ReadConfiguredTime()) : AttackProgress01 > .55f;
    public bool ComboInputOpen => UsesConfiguredCombo && _comboRunning && _comboStep + 1 < StepCount && ActiveStep.AcceptsCombo(ReadConfiguredTime());
    public event Action<int, int> ComboStepChanged;
    public event Action ComboEnded;

    private List<ComboStep> _loadedSteps, _pendingSteps;
    private ComboData _pendingData, _loadedData;
    private int _pendingRevision;
    private PlayerAnimController _comboAnimator;
    private CombatFeedback _comboFeedback;
    private Animator _animator;
    private AnimatorOverrideController _overrideController;
    private RuntimeAnimatorController _originalController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private readonly AnimationClip[] _slotClips = new AnimationClip[2];
    private AnimationClip _activeAttackClip;
    private bool _comboRunning, _hitWindowOpen, _swingActive;
    private bool _hasComboSpeedParameters;
    private int _attackToken, _activeStateHash, _lastAdvanceFrame = -1;
    private float _attackStartTime;
    private Vector3 _lastFrameHitOrigin;
    private float _lastFrameClipTime;
    private bool _windowEventThisFrame, _hasRootMotionPosition;
    private Vector3 _rootMotionPosition;
    private Quaternion _rootMotionRotation;
    private readonly HashSet<Enemy> _hitEnemies = new HashSet<Enemy>();
    private Collider[] _overlapResults = new Collider[32];
    private RaycastHit[] _castResults = new RaycastHit[32];
    private readonly HashSet<string> _firedSounds = new HashSet<string>();
    private ComboStep ActiveStep => UsesConfiguredCombo && _comboStep >= 0 && _comboStep < _loadedSteps.Count ? _loadedSteps[_comboStep] : null;
    private float ConfiguredProgress => ActiveStep == null ? 0f : Mathf.Clamp01(ReadConfiguredTime() / ActiveStep.animationClip.length);

    private void InitializeComboRuntime()
    {
        _comboAnimator = GetComponent<PlayerAnimController>();
        _comboFeedback = GetComponent<CombatFeedback>();
        _animator = GetComponentInChildren<Animator>();
        if (comboData != null && comboData.HasSteps) RequestReload(comboData);
    }

    public void AssignComboData(ComboData data)
    {
        if (Application.isPlaying) { RequestReload(data); return; }
        comboData = data;
    }

    public bool RequestReload(ComboData savedData)
    {
        if (savedData == null) { ReloadStatus = "重载失败：未指定资产"; return false; }
#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsDirty(savedData)) { ReloadStatus = "重载失败：请先保存资产"; return false; }
#endif
        var errors = savedData.Validate();
        if (errors.Count > 0) { ReloadStatus = "重载失败：" + errors[0]; return false; }
        if (_comboAnimator == null) _comboAnimator = GetComponent<PlayerAnimController>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_animator == null || _comboAnimator == null || _animator.runtimeAnimatorController == null ||
            _comboAnimator.comboPlaceholderA == null || _comboAnimator.comboPlaceholderB == null ||
            !_animator.HasState(0, Animator.StringToHash("Base Layer.ComboSlotA")) ||
            !_animator.HasState(0, Animator.StringToHash("Base Layer.ComboSlotB")))
        { ReloadStatus = "重载失败：请先执行 Tools/连招演示/配置当前场景，检查动画槽"; return false; }
        _hasComboSpeedParameters = HasComboSpeedParameters();
        if (!_hasComboSpeedParameters)
        { ReloadStatus = "重载失败：连招槽缺少倍速参数，请退出运行后执行 Tools/连招演示/配置当前场景"; return false; }
        var clips = _animator.runtimeAnimatorController.animationClips;
        if (_overrideController == null && (Array.IndexOf(clips, _comboAnimator.comboPlaceholderA) < 0 || Array.IndexOf(clips, _comboAnimator.comboPlaceholderB) < 0))
        { ReloadStatus = "重载失败：占位片段不属于当前控制器"; return false; }

        var snapshot = new List<ComboStep>();
        foreach (var step in savedData.steps) snapshot.Add(step.Copy());
        _pendingSteps = snapshot;
        _pendingData = savedData;
        _pendingRevision = savedData.revision;
        if (!CanApplyReload()) { ReloadStatus = "待当前动作结束后重载"; return true; }
        ApplyPendingReload();
        return true;
    }

    private bool CanApplyReload()
    {
        var controller = GetComponent<ThirdPersonController>();
        return !_swingActive && (controller == null || controller.CanReloadCombo);
    }

    private bool HasComboSpeedParameters()
    {
        bool a = false, b = false;
        foreach (var parameter in _animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float) continue;
            a |= parameter.name == ComboSpeedParameterA;
            b |= parameter.name == ComboSpeedParameterB;
        }
        return a && b;
    }

    public void ApplyPendingReloadIfSafe()
    {
        if (_pendingSteps != null && CanApplyReload()) ApplyPendingReload();
    }

    private void ApplyPendingReload()
    {
        if (_pendingSteps == null) return;
        if (_overrideController == null)
        {
            _originalController = _animator.runtimeAnimatorController;
            _overrideController = new AnimatorOverrideController(_originalController) { name = "连招运行时覆盖", hideFlags = HideFlags.DontSave };
            _overrideController.GetOverrides(_overrides);
            _animator.runtimeAnimatorController = _overrideController;
        }
        _loadedSteps = _pendingSteps;
        comboData = _pendingData;
        _loadedData = _pendingData;
        LoadedRevision = _pendingRevision;
        _pendingSteps = null;
        _pendingData = null;
        ReloadStatus = "已加载版本 " + LoadedRevision;
    }

    public bool StartConfiguredAttack()
    {
        if (!UsesConfiguredCombo || _comboRunning || currentHealth <= 0f) return false;
        _comboStep = 0;
        _comboRunning = true;
        return PlayConfiguredStep();
    }

    public bool TryAdvanceConfiguredCombo()
    {
        // 每次新按下只推进一段；范围为左闭右开；末段没有下一段。
        if (!ComboInputOpen || _lastAdvanceFrame == Time.frameCount) return false;
        _comboStep++;
        return PlayConfiguredStep();
    }

    private bool PlayConfiguredStep()
    {
        BeginSwing();
        _lastAdvanceFrame = Time.frameCount;
        _attackStartTime = Time.time;
        _comboBuffered = false;
        int slot = _comboStep % 2;
        AnimationClip placeholder = slot == 0 ? _comboAnimator.comboPlaceholderA : _comboAnimator.comboPlaceholderB;
        var clip = Instantiate(ActiveStep.animationClip);
        clip.name = $"Combo_{_comboStep + 1}_{_attackToken}";
        clip.hideFlags = HideFlags.DontSave;
        // 副本只保留当前配置生成的事件，防旧素材伤害/音效双重触发。
        var events = new List<AnimationEvent>
        {
            MakeEvent("ComboHitOpen", ActiveStep.hitStart, clip),
            MakeEvent("ComboHitClose", ActiveStep.hitEnd, clip)
        };
        foreach (var sound in ActiveStep.soundEvents)
        {
            var e = MakeEvent("ComboSound", sound.time, clip);
            e.stringParameter = sound.soundId;
            events.Add(e);
        }
        events.Sort((a, b) => a.time.CompareTo(b.time));
        clip.events = events.ToArray();
        if (_slotClips[slot] != null) Destroy(_slotClips[slot], .5f);
        _slotClips[slot] = clip;
        _activeAttackClip = clip;
        _lastFrameHitOrigin = HitOrigin();
        _lastFrameClipTime = 0f;
        for (int i = 0; i < _overrides.Count; i++)
            if (_overrides[i].Key == placeholder) _overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, clip);
        _overrideController.ApplyOverrides(_overrides);
        _activeStateHash = Animator.StringToHash(slot == 0 ? "Base Layer.ComboSlotA" : "Base Layer.ComboSlotB");
        _animator.ResetTrigger("Attack");
        _animator.ResetTrigger("NextAttack");
        // 两个槽各用自己的倍速；接段过渡中不改变上一段的播放速度。
        _animator.SetFloat(slot == 0 ? ComboSpeedParameterA : ComboSpeedParameterB, ActiveStep.playbackSpeed);
        _animator.speed = 1f;
        _animator.CrossFadeInFixedTime(_activeStateHash, .05f, 0, 0f);
        if (ActiveStep.soundEvents.Count == 0) PlaySwingSfx();
        ComboStepChanged?.Invoke(_comboStep, StepCount);
        return true;
    }

    private AnimationEvent MakeEvent(string function, float time, AnimationClip source)
    {
        return new AnimationEvent { functionName = function, time = time, intParameter = _attackToken, objectReferenceParameter = source, messageOptions = SendMessageOptions.RequireReceiver };
    }

    private float ReadConfiguredTime()
    {
        if (!_comboRunning || _animator == null || ActiveStep == null) return 0f;
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        if (_animator.IsInTransition(0))
        {
            var next = _animator.GetNextAnimatorStateInfo(0);
            if (next.fullPathHash == _activeStateHash) state = next;
        }
        if (state.fullPathHash != _activeStateHash) return 0f;
        return Mathf.Max(0, state.normalizedTime) * ActiveStep.animationClip.length;
    }

    public bool ConfiguredAttackFinished()
    {
        if (!_comboRunning) return true;
        if (ReadConfiguredTime() >= ActiveStep.animationClip.length) return true;
        // Animator意外被其他逻辑切走时也必须清理，不能永远停在Attack。
        if (Time.time - _attackStartTime > ActiveStep.PlaybackDuration + .5f && ReadConfiguredTime() <= 0f) return true;
        return false;
    }

    private bool IsCurrentEvent(AnimationEvent e) => e != null && _comboRunning && _swingActive && e.intParameter == _attackToken && e.objectReferenceParameter == _activeAttackClip;

    public void OnComboHitOpen(AnimationEvent e)
    {
        if (!IsCurrentEvent(e)) return;
        _hitWindowOpen = true;
        _comboFeedback?.SetTrail(true);
        _windowEventThisFrame = true;
    }

    public void OnComboHitClose(AnimationEvent e)
    {
        if (!IsCurrentEvent(e)) return;
        // 事件只记录开关；动画模块计算本次根位移后再采样合法路径。
        _windowEventThisFrame = true;
        _hitWindowOpen = false;
        _comboFeedback?.SetTrail(false);
    }

    public void OnComboSound(AnimationEvent e)
    {
        if (!IsCurrentEvent(e) || !_firedSounds.Add(e.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + e.stringParameter)) return;
        PlaySfx(e.stringParameter);
    }

    private Vector3 HitOrigin() => transform.TransformPoint(ActiveStep.hitOffset);

    public void NotifyComboRootPosition(Vector3 position, Quaternion rotation)
    {
        _rootMotionPosition = position;
        _rootMotionRotation = rotation;
        _hasRootMotionPosition = true;
    }

    /// <summary>在一次动画根位移计算后采样；物理模式每个动画物理步执行一次。</summary>
    public void SampleComboHitAfterRootMotion()
    {
        if (_comboRunning)
        {
            float time = ReadConfiguredTime();
            Vector3 origin = _hasRootMotionPosition ? _rootMotionPosition + _rootMotionRotation * Vector3.Scale(transform.lossyScale, ActiveStep.hitOffset) : HitOrigin();
            if (_swingActive && (_hitWindowOpen || _windowEventThisFrame))
            {
                float start = Mathf.Max(_lastFrameClipTime, ActiveStep.hitStart);
                float end = Mathf.Min(time, ActiveStep.hitEnd);
                float span = time - _lastFrameClipTime;
                if (end >= start && start < ActiveStep.hitEnd && time >= ActiveStep.hitStart)
                {
                    float a = span > .000001f ? Mathf.Clamp01((start - _lastFrameClipTime) / span) : 1f;
                    float b = span > .000001f ? Mathf.Clamp01((end - _lastFrameClipTime) / span) : 1f;
                    SampleConfiguredHit(Vector3.Lerp(_lastFrameHitOrigin, origin, a), Vector3.Lerp(_lastFrameHitOrigin, origin, b));
                }
            }
            _lastFrameHitOrigin = origin;
            _lastFrameClipTime = time;
        }
        _windowEventThisFrame = false;
        _hasRootMotionPosition = false;
    }

    private void LateUpdate()
    {
        // Normal动画仍支持原LateUpdate路径；物理动画已逐步采样，不再用插值姿态重复检测。
        if (_animator == null || _animator.updateMode != AnimatorUpdateMode.AnimatePhysics)
            SampleComboHitAfterRootMotion();
        if (Input.GetKeyDown(KeyCode.F5) && comboData != null) RequestReload(comboData);
        ApplyPendingReloadIfSafe();
    }

    private void SampleConfiguredHit(Vector3 from, Vector3 origin)
    {
        if (ActiveStep == null || !_swingActive) return;
        float radius = ActiveStep.hitRadius;
        bool hitBefore = _hasHitThisSwing;
        int count;
        do
        {
            count = Physics.OverlapSphereNonAlloc(from, radius, _overlapResults, enemyLayer, QueryTriggerInteraction.Collide);
            if (count < _overlapResults.Length) break;
            Array.Resize(ref _overlapResults, _overlapResults.Length * 2);
        } while (true);
        for (int i = 0; i < count; i++) ApplySwingDamage(_overlapResults[i], ActiveStep.damage);
        Vector3 delta = origin - from;
        if (delta.sqrMagnitude > .000001f)
        {
            do
            {
                count = Physics.SphereCastNonAlloc(from, radius, delta.normalized, _castResults, delta.magnitude, enemyLayer, QueryTriggerInteraction.Collide);
                if (count < _castResults.Length) break;
                Array.Resize(ref _castResults, _castResults.Length * 2);
            } while (true);
            for (int i = 0; i < count; i++) ApplySwingDamage(_castResults[i].collider, ActiveStep.damage);
        }
        if (_hasHitThisSwing && !hitBefore) { PlayHitSfxByStep(); TriggerHitStop(); }
    }

    private void ApplySwingDamage(Collider col, float damage)
    {
        if (col == null || !_swingActive || col.transform.IsChildOf(transform)) return;
        Enemy target = col.GetComponentInParent<Enemy>();
        if (target == null || target.IsDead || !target.AcceptsCollider(col) || _hitEnemies.Contains(target)) return;
        if (UsesConfiguredCombo && Obstructed(col)) return;
        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        // 实体级去重，不依赖敌人的短暂无敌帧；只有真实掉血才显示命中数字。
        _hitEnemies.Add(target);
        float before = target.CurrentHealth;
        Vector3 hitPoint = col.bounds.center;
        var result = target.ReceiveHit(damage, damage, transform.position, direction.sqrMagnitude > .0001f ? direction.normalized : transform.forward);
        if (!isActiveAndEnabled || !_swingActive) return;
        if (result == EnemyHitResult.Blocked) { SoundManager.Instance?.Play("sword_hit_03", hitPoint); return; }
        float actual = Mathf.Max(0f, before - target.CurrentHealth);
        if (actual <= 0f) return;
        _hasHitThisSwing = true;
        _lastHitPoint = hitPoint;
        _comboFeedback?.Impact(hitPoint, Color.cyan);
        DamagePopupManager.Instance?.Show(_lastHitPoint, actual);
    }

    private bool Obstructed(Collider target)
    {
        Vector3 from = transform.position + Vector3.up * ActiveStep.hitOffset.y;
        Vector3 delta = target.bounds.center - from;
        if (delta.sqrMagnitude < .0001f) return false;
        // 障碍物检测独立于 enemyLayer：墙体不应因不在敌人层而被穿透。
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Enemy>() != null) continue;
            return true;
        }
        return false;
    }

    private void BeginSwing()
    {
        _comboFeedback?.SetTrail(false);
        _attackToken++;
        _swingActive = true;
        _hitWindowOpen = false;
        _windowEventThisFrame = false;
        _hasRootMotionPosition = false;
        _hitEnemies.Clear();
        _firedSounds.Clear();
        _hasHitThisSwing = false;
    }

    /// <summary>额外动作复用同一运行覆盖表，避免嵌套覆盖控制器吞掉普通连招重载。</summary>
    public bool PlayExternalClip(AnimationClip placeholder, AnimationClip clip, string state, string speedParameter, float speed)
    {
        if (_overrideController == null || _animator == null || placeholder == null || clip == null ||
            !_animator.HasState(0, Animator.StringToHash(state))) return false;
        bool found = false;
        for (int i = 0; i < _overrides.Count; i++)
            if (_overrides[i].Key == placeholder) { _overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, clip); found = true; }
        if (!found) return false;
        _overrideController.ApplyOverrides(_overrides);
        _animator.SetFloat(speedParameter, speed);
        _animator.CrossFadeInFixedTime(state, .06f, 0, 0);
        return true;
    }

    private void CancelSwing()
    {
        _comboFeedback?.SetTrail(false);
        bool hadSwing = _swingActive;
        _attackToken++;
        _swingActive = false;
        _hitWindowOpen = false;
        _hasRootMotionPosition = false;
        _windowEventThisFrame = false;
        _hitEnemies.Clear();
        _hasHitThisSwing = false;
        if (hadSwing) ComboEnded?.Invoke();
        if (_animator != null && _hasComboSpeedParameters)
        {
            _animator.SetFloat(ComboSpeedParameterA, 1f);
            _animator.SetFloat(ComboSpeedParameterB, 1f);
        }
    }

    private void OnDisable()
    {
        ResetAllTimers();
        if (_hitStopRoutine != null)
        {
            StopCoroutine(_hitStopRoutine);
            _hitStopRoutine = null;
            Time.timeScale = _timeScaleBeforeHitStop;
        }
    }

    private void OnDestroy()
    {
        if (_animator != null && _originalController != null) _animator.runtimeAnimatorController = _originalController;
        foreach (var clip in _slotClips) if (clip != null) Destroy(clip);
        if (_overrideController != null) Destroy(_overrideController);
    }

    private void OnDrawGizmosSelected()
    {
        ComboStep step = Application.isPlaying ? ActiveStep : (comboData != null && comboData.HasSteps ? comboData.steps[0] : null);
        if (step == null) return;
        Gizmos.color = _hitWindowOpen ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(step.hitOffset), step.hitRadius);
    }
}
