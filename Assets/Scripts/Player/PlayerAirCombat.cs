using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Q 挑飞破防敌人，E 追击一次；动作数据复用连招编辑器，位移由玩家 OnAnimatorMove 消费。</summary>
[RequireComponent(typeof(ThirdPersonController), typeof(PlayerCombat))]
public class PlayerAirCombat : MonoBehaviour
{
    public AirChainSettings settings;
    public AnimationClip launcherPlaceholder, strikePlaceholder;
    public CombatFeedback feedback;
    public enum AirPhase { None, Launcher, Chase, Strike, Landing }
    public AirPhase Phase { get; private set; }
    public float VerticalSpeed => _verticalSpeed;
    public Quaternion ActionRotation => _facing;
    public string Status { get; private set; } = "Q 挑飞破防敌人 · E 追击一次";
    public int LauncherRevision { get; private set; } = -1;
    public int StrikeRevision { get; private set; } = -1;
    public bool CanFollow => _pendingTarget != null && _pendingTarget.CanAirFollow;
    /// <summary>Q 起手被拒绝的累计次数；HUD 据此抖动键帽，不需要订阅额外事件。</summary>
    public int RejectCount { get; private set; }
    public bool ChaseQueued => Phase == AirPhase.Launcher && _queuedChase;
    public bool UsesAirData(ComboData data) => data != null && settings != null && (data == settings.launcherCombo || data == settings.airStrikeCombo);
    public int RevisionFor(ComboData data) => data == settings?.launcherCombo ? LauncherRevision : data == settings?.airStrikeCombo ? StrikeRevision : -1;
    private ThirdPersonController _controller;
    private PlayerCombat _combat;
    private Animator _animator;
    private Rigidbody _body;
    private Collider _collider;
    private AirChainSettings _snapshot, _pendingSettings;
    private ComboStep _pendingLauncher;
    private List<ComboStep> _pendingStrikes;
    private int _pendingLauncherRevision, _pendingStrikeRevision;
    private ComboStep _launcher, _step;
    private List<ComboStep> _strikes = new List<ComboStep>();
    private int _strikeIndex, _lastSlot;
    private bool _queuedNext, _queuedSlam;
    private float _lmbBufferUntil;
    public const int MaxStrikeSteps = 5;
    public bool InStrike => Phase == AirPhase.Strike;
    public int StrikeIndex => _strikeIndex;
    public int StrikeCount => _strikes.Count;
    public bool SlamQueued => _queuedSlam;
    private bool IsFinalStrike => _strikeIndex >= _strikes.Count - 1;
    private GreatSwordEnemyBrain _target, _pendingTarget;
    private Enemy _targetEnemy;
    private Rigidbody _targetBody;
    private Collider _targetHurt;
    private CameraFollow _camera;
    private Quaternion _facing;
    private AnimationClip _activeClip;
    private readonly AnimationClip[] _clips = new AnimationClip[2];
    private int _token, _stateHash;
    private float _phaseAge, _actionStarted, _verticalSpeed, _feetOffset, _cooldownUntil, _previousTime;
    private Vector3 _previousOrigin;
    private bool _windowSeen, _hit, _launchSucceeded, _queuedChase;
    private const float InputBuffer = .25f;
    private float _qBufferUntil, _eBufferUntil;
    private Collider[] _overlap = new Collider[32];
    private RaycastHit[] _casts = new RaycastHit[32];

    private void Awake()
    {
        _controller = GetComponent<ThirdPersonController>(); _combat = GetComponent<PlayerCombat>();
        _body = GetComponent<Rigidbody>(); _collider = GetComponent<Collider>(); _animator = GetComponentInChildren<Animator>(true);
        if (feedback == null) feedback = GetComponent<CombatFeedback>();
        if (_collider != null) _feetOffset = _body.position.y - _collider.bounds.min.y;
    }
    private void Start() { RequestReload(); }

    public bool RequestReload()
    {
        if (settings == null || settings.launcherCombo == null || settings.airStrikeCombo == null)
        { Status = "浮空配置缺失"; return false; }
        if (settings.Validate().Count > 0) { Status = "浮空移动参数非法"; return false; }
#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsDirty(settings.launcherCombo) || UnityEditor.EditorUtility.IsDirty(settings.airStrikeCombo))
        { Status = "请先保存挑飞/追击配置"; return false; }
#endif
        if (settings.launcherCombo.Validate().Count > 0 || settings.airStrikeCombo.Validate().Count > 0 ||
            settings.launcherCombo.steps.Count != 1 || settings.airStrikeCombo.steps.Count < 1 || settings.airStrikeCombo.steps.Count > MaxStrikeSteps)
        { Status = "挑飞需一段、空中连斩需 1～" + MaxStrikeSteps + " 段合法配置"; return false; }
        if (_pendingSettings != null) Destroy(_pendingSettings);
        _pendingSettings = Instantiate(settings); _pendingSettings.hideFlags = HideFlags.DontSave;
        _pendingLauncher = settings.launcherCombo.steps[0].Copy();
        _pendingStrikes = settings.airStrikeCombo.steps.ConvertAll(s => s.Copy());
        _pendingLauncherRevision = settings.launcherCombo.revision; _pendingStrikeRevision = settings.airStrikeCombo.revision;
        if (Phase == AirPhase.None && _controller.CanReloadCombo) ApplyReload();
        else Status = "浮空配置待安全状态重载";
        return true;
    }
    private void ApplyReload()
    {
        if (_snapshot != null) Destroy(_snapshot);
        _snapshot = _pendingSettings;
        _launcher = _pendingLauncher; _strikes = _pendingStrikes;
        LauncherRevision = _pendingLauncherRevision; StrikeRevision = _pendingStrikeRevision;
        _pendingSettings = null; Status = "Q 挑飞破防敌人 · E 追击一次";
    }

    // 与主控制器在一个输入入口消费，避免 MonoBehaviour Update 顺序影响状态切换。
    public bool TickInput()
    {
        UpdateCameraLift();
        if (Input.GetKeyDown(KeyCode.F5)) RequestReload();
        if (_pendingSettings != null && Phase == AirPhase.None && _controller.CanReloadCombo) ApplyReload();
        if (_snapshot == null) return false;
        // 输入缓冲按现实时间计，顿帧期间按下的键不会过期过快。
        bool qPressed = Input.GetKeyDown(KeyCode.Q), ePressed = Input.GetKeyDown(KeyCode.E);
        if (qPressed) _qBufferUntil = Time.unscaledTime + InputBuffer;
        if (ePressed) _eBufferUntil = Time.unscaledTime + InputBuffer;
        if (Input.GetMouseButtonDown(0)) _lmbBufferUntil = Time.unscaledTime + InputBuffer;
        if (Phase != AirPhase.None)
        {
            bool eBuffered = Time.unscaledTime < _eBufferUntil;
            // 挑飞动作中任意时刻按 E 都记为追击意图，命中成功后自动接上。
            if (Phase == AirPhase.Launcher && eBuffered) { _queuedChase = true; _eBufferUntil = 0; }
            // 追击或连斩中按 E：预约砸地终结（最后一段不再重复预约）。
            else if ((Phase == AirPhase.Chase || (Phase == AirPhase.Strike && !IsFinalStrike)) && eBuffered) { _queuedSlam = true; _eBufferUntil = 0; }
            // 连斩中左键：本段接段窗口结束前按下都记为“接下一段”，允许提前连按。
            if (Phase == AirPhase.Strike && !IsFinalStrike && Time.unscaledTime < _lmbBufferUntil && _step != null && ClipTime < _step.comboEnd)
            { _queuedNext = true; _lmbBufferUntil = 0; }
            TickAction(); return true;
        }
        bool safe = _controller.CanReloadCombo || (_controller.State == PlayerState.Attack && _combat.CanCancelAttack);
        if (Time.unscaledTime < _eBufferUntil && CanFollow)
        { _eBufferUntil = 0; StartChase(_pendingTarget); return true; }
        if (Time.unscaledTime < _qBufferUntil)
        {
            var nearest = safe && _controller.IsGroundedForActions && Time.time >= _cooldownUntil ? FindLaunchTarget() : null;
            if (nearest != null)
            {
                _qBufferUntil = 0;
                BindTarget(nearest); _pendingTarget = null; _launchSucceeded = _queuedChase = false;
                BeginPhase(AirPhase.Launcher); _controller.BeginAirAction();
                if (!PlayStep(_launcher, 0)) Finish(false);
                return true;
            }
            if (qPressed) Reject(safe);
        }
        return false;
    }

    /// <summary>Q 未能起手时说明具体原因，并通知 HUD 抖动键帽；缓冲仍保留，条件随后满足会自动起手。</summary>
    private void Reject(bool safe)
    {
        RejectCount++;
        if (Time.time < _cooldownUntil) Status = "挑飞冷却中";
        else if (!_controller.IsGroundedForActions) Status = "落地后才能挑飞";
        else if (!safe) Status = "当前动作结束后挑飞";
        else Status = NearbyBrokenTarget() ? "再靠近一些，贴近破防敌人按 Q" : "先削满架势，破防后再按 Q";
    }

    /// <summary>挑飞后敌人在空中时，把敌人身体中心交给镜头做双目标取景，主角与敌人同框。</summary>
    private void UpdateCameraLift()
    {
        if (_camera == null && CameraCache.Main != null) _camera = CameraCache.Main.GetComponent<CameraFollow>();
        if (_camera == null) return;
        var airborne = _target != null && Phase != AirPhase.None ? _target : _pendingTarget;
        bool active = airborne != null && airborne.State == EnemyState.Airborne;
        _camera.HasSecondaryFocus = active;
        if (active)
            _camera.SecondaryFocus = airborne == _target && _targetHurt != null ? _targetHurt.bounds.center : airborne.transform.position + Vector3.up * 1.5f;
    }

    private bool NearbyBrokenTarget()
    {
        foreach (var col in Physics.OverlapSphere(_body.position, _snapshot.targetRange * 3f, _combat.EnemyLayer, QueryTriggerInteraction.Collide))
        {
            var enemy = col.GetComponentInParent<Enemy>(); var brain = enemy != null ? enemy.GetComponent<GreatSwordEnemyBrain>() : null;
            if (brain != null && brain.CanLaunch) return true;
        }
        return false;
    }

    private GreatSwordEnemyBrain FindLaunchTarget()
    {
        GreatSwordEnemyBrain best = null; float distance = _snapshot.targetRange;
        foreach (var col in Physics.OverlapSphere(_body.position, distance, _combat.EnemyLayer, QueryTriggerInteraction.Collide))
        {
            var enemy = col.GetComponentInParent<Enemy>(); var brain = enemy != null ? enemy.GetComponent<GreatSwordEnemyBrain>() : null;
            if (brain == null || !brain.CanLaunch || enemy.IsDead || !enemy.AcceptsCollider(col)) continue;
            float d = Vector3.Distance(_body.position, brain.transform.position);
            if (d <= distance && !CombatPhysics.Obstructed(_body.position + Vector3.up, col.bounds.center, transform, brain.transform))
            { best = brain; distance = d; }
        }
        return best;
    }
    private void BindTarget(GreatSwordEnemyBrain brain)
    {
        _target = brain; _targetEnemy = brain.GetComponent<Enemy>(); _targetBody = brain.GetComponent<Rigidbody>();
        var hurt = brain.GetComponentInChildren<EnemyHurtbox>(); _targetHurt = hurt != null ? hurt.GetComponent<Collider>() : brain.GetComponent<Collider>();
        Vector3 d = _targetBody.position - _body.position; d.y = 0;
        _facing = d.sqrMagnitude > .001f ? Quaternion.LookRotation(d) : _body.rotation;
    }
    private void StartChase(GreatSwordEnemyBrain brain)
    {
        if (brain == null || !brain.ReserveAirFollow()) return;
        BindTarget(brain); _pendingTarget = null; _queuedChase = _queuedSlam = _queuedNext = false;
        BeginPhase(AirPhase.Chase); _controller.BeginAirAction();
        _controller.GetComponent<PlayerAnimController>().TriggerJump(); feedback?.Pulse(Color.cyan, .18f);
        Status = "空中接近";
    }
    private void BeginPhase(AirPhase phase)
    { Phase = phase; _phaseAge = 0; _actionStarted = Time.time; _verticalSpeed = 0; _token++; _windowSeen = _hit = false; feedback?.SetTrail(false); }

    /// <summary>播放第 index 段空中连斩；两个动画槽交替使用，避免同一状态内替换片段造成跳帧。</summary>
    private bool PlayStrike(int index)
    {
        _strikeIndex = Mathf.Clamp(index, 0, _strikes.Count - 1); _queuedNext = _queuedSlam = false;
        Status = IsFinalStrike ? "空中砸地！" : $"空中连斩 {_strikeIndex + 1}/{_strikes.Count} · 左键继续 · E 砸地";
        return PlayStep(_strikes[_strikeIndex], 1 - _lastSlot);
    }

    private bool PlayStep(ComboStep step, int slot)
    {
        _step = step; _token++; _windowSeen = _hit = false; _previousTime = 0; _lastSlot = slot; _actionStarted = Time.time;
        _stateHash = Animator.StringToHash(slot == 0 ? "Base Layer.AirLaunch" : "Base Layer.AirStrike");
        var clip = Instantiate(step.animationClip); clip.hideFlags = HideFlags.DontSave; clip.name = "AirAction_" + _token;
        var events = new List<AnimationEvent> { Event("AirHitOpen", step.hitStart, clip), Event("AirHitClose", step.hitEnd, clip) };
        foreach (var sound in step.soundEvents) { var e = Event("AirSound", sound.time, clip); e.stringParameter = sound.soundId; events.Add(e); }
        events.Sort((a, b) => a.time.CompareTo(b.time)); clip.events = events.ToArray();
        if (_clips[slot] != null) Destroy(_clips[slot], .5f); _clips[slot] = _activeClip = clip;
        _previousOrigin = Origin(_body.position, _facing);
        bool ok = _combat.PlayExternalClip(slot == 0 ? launcherPlaceholder : strikePlaceholder, clip,
            slot == 0 ? "Base Layer.AirLaunch" : "Base Layer.AirStrike", slot == 0 ? "AirLaunchSpeed" : "AirStrikeSpeed", step.playbackSpeed);
        if (!ok) { Status = "浮空动画槽未接线，请配置演示资源"; Debug.LogError(Status, this); }
        return ok;
    }
    private AnimationEvent Event(string name, float time, AnimationClip source) => new AnimationEvent
    { functionName = name, time = time, intParameter = _token, objectReferenceParameter = source, messageOptions = SendMessageOptions.RequireReceiver };
    private bool ValidEvent(AnimationEvent e) => e != null && (Phase == AirPhase.Launcher || Phase == AirPhase.Strike) && e.intParameter == _token && e.objectReferenceParameter == _activeClip;
    public void OnHitEvent(AnimationEvent e, bool open) { if (ValidEvent(e)) { _windowSeen |= open; feedback?.SetTrail(open); } }
    public void OnSound(AnimationEvent e) { if (ValidEvent(e)) SoundManager.Instance?.PlayByPrefix(e.stringParameter, transform.position); }
    private float ClipTime
    {
        get
        {
            if (_step == null || _animator == null) return 0;
            var s = _animator.GetCurrentAnimatorStateInfo(0);
            if (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).fullPathHash == _stateHash) s = _animator.GetNextAnimatorStateInfo(0);
            return s.fullPathHash == _stateHash ? Mathf.Max(0, s.normalizedTime) * _step.animationClip.length : 0;
        }
    }
    private void TickAction()
    {
        if (_controller.State != PlayerState.AirAction) { Cancel(); return; }
        if (_target == null || _targetEnemy == null || _targetEnemy.IsDead)
        { if (Phase == AirPhase.Launcher) Finish(false); else if (Phase != AirPhase.Landing) BeginLanding(); }
        // 空中连斩：到达本段接段窗口时，左键接下一段，E 直接跳到最后一段砸地。
        if (Phase == AirPhase.Strike && _step != null && !IsFinalStrike && (_queuedNext || _queuedSlam) && ClipTime >= _step.comboStart)
        { if (!PlayStrike(_queuedSlam ? _strikes.Count - 1 : _strikeIndex + 1)) BeginLanding(); }
        if ((Phase == AirPhase.Launcher || Phase == AirPhase.Strike) && _step != null &&
            (ClipTime >= _step.animationClip.length || Time.time - _actionStarted > _step.PlaybackDuration + .5f))
        {
            if (Phase == AirPhase.Launcher)
            {
                if (_launchSucceeded && _queuedChase && _target.CanAirFollow) StartChase(_target);
                else { _pendingTarget = _launchSucceeded ? _target : null; Finish(true); }
            }
            else BeginLanding();
        }
        if (Phase == AirPhase.Chase && _target != null && _targetBody != null)
        {
            float d = Vector3.Distance(_body.position, ChasePosition());
            if (_phaseAge >= _snapshot.chaseDuration && d < .8f)
            { BeginPhase(AirPhase.Strike); if (!PlayStrike(_queuedSlam ? _strikes.Count - 1 : 0)) BeginLanding(); }
            else if (_phaseAge > _snapshot.chaseDuration + .6f || _target.State != EnemyState.Airborne) BeginLanding();
        }
        if ((Phase == AirPhase.Chase || Phase == AirPhase.Strike) && _phaseAge > _snapshot.maximumAirDuration) BeginLanding();
        if (Phase == AirPhase.Landing && _controller.IsGroundedForActions && _phaseAge > .08f) Finish(false);
    }
    /// <summary>追击落点：对准敌人受击体中心高度，水平距离至少留出敌人半径；敌人缩放变化时仍砍在躯干上。</summary>
    private Vector3 ChasePosition()
    {
        Bounds b = _targetHurt != null ? _targetHurt.bounds : new Bounds(_targetBody.position + Vector3.up, Vector3.one);
        float chest = _collider != null ? _collider.bounds.center.y - _body.position.y : .9f;
        float reach = Mathf.Max(_snapshot.strikeDistance, Mathf.Min(b.extents.x, b.extents.z) + .35f);
        Vector3 goal = b.center - (_facing * Vector3.forward) * reach;
        goal.y = b.center.y - chest;
        return goal;
    }
    private void BeginLanding()
    { BeginPhase(AirPhase.Landing); _verticalSpeed = -2f; _controller.GetComponent<PlayerAnimController>().TriggerJump(); Status = "落地恢复"; }

    public Vector3 EvaluateMotion(Vector3 rootDelta, float dt)
    {
        _phaseAge += dt;
        Vector3 delta = Vector3.zero;
        if (Phase == AirPhase.Chase && _targetBody != null)
        {
            // 按剩余冲刺时间反推速度，保证在 chaseDuration 内贴到目标，不再因距离偏差落空；上限 3 倍防止瞬移。
            Vector3 goal = ChasePosition();
            float remaining = Mathf.Max(_snapshot.chaseDuration - _phaseAge, dt);
            float speed = Mathf.Clamp(Vector3.Distance(_body.position, goal) / remaining, _snapshot.chaseSpeed, _snapshot.chaseSpeed * 3f);
            delta = Vector3.MoveTowards(_body.position, goal, speed * dt) - _body.position;
        }
        else if (Phase == AirPhase.Launcher || Phase == AirPhase.Strike)
        {
            delta = rootDelta * (_step != null ? _step.rootMotionScaleXZ : 0); delta.y = 0;
            // 连斩期间柔和跟随敌人高度，敌人被顶起或下落时始终砍在躯干上。
            if (Phase == AirPhase.Strike && _targetBody != null && _target != null && _target.State == EnemyState.Airborne)
                delta.y = (ChasePosition().y - _body.position.y) * (1f - Mathf.Exp(-8f * dt));
        }
        if (Phase == AirPhase.Landing)
        {
            _verticalSpeed -= _snapshot.gravity * dt; delta.y = _verticalSpeed * dt;
            // 先离开敌人的身体投影，避免落在其物理胶囊顶上而无法结束落地。
            if (_targetBody != null)
            {
                Vector3 away = _body.position - _targetBody.position; away.y = 0;
                if (away.magnitude < _snapshot.strikeDistance)
                { if (away.sqrMagnitude < .001f) away = -(_facing * Vector3.forward); delta += away.normalized * Mathf.Min(_snapshot.strikeDistance - away.magnitude, 3f * dt); }
            }
        }
        float height;
        if (CombatPhysics.Ground(_body.position, transform, out height))
        {
            float floor = height + _feetOffset + .015f;
            if (Phase == AirPhase.Launcher) delta.y = floor - _body.position.y;
            else if (Phase == AirPhase.Landing && _body.position.y + delta.y <= floor)
            { delta.y = floor - _body.position.y; _verticalSpeed = 0; }
        }
        return delta;
    }
    private Vector3 Origin(Vector3 p, Quaternion r) => p + r * _step.hitOffset;
    public void AfterRootMotion(Vector3 position, Quaternion rotation)
    {
        if (_step == null || (Phase != AirPhase.Launcher && Phase != AirPhase.Strike)) return;
        float time = ClipTime, begin = Mathf.Max(_previousTime, _step.hitStart), end = Mathf.Min(time, _step.hitEnd);
        Vector3 origin = Origin(position, rotation);
        if (!_hit && _windowSeen && time > _previousTime && end > begin)
        {
            Vector3 from = Vector3.Lerp(_previousOrigin, origin, Mathf.Clamp01((begin - _previousTime) / (time - _previousTime)));
            Vector3 to = Vector3.Lerp(_previousOrigin, origin, Mathf.Clamp01((end - _previousTime) / (time - _previousTime)));
            int count;
            do { count = Physics.OverlapSphereNonAlloc(from, _step.hitRadius, _overlap, _combat.EnemyLayer, QueryTriggerInteraction.Collide); if (count == _overlap.Length) Array.Resize(ref _overlap, _overlap.Length * 2); else break; } while (true);
            for (int i = 0; i < count; i++) Hit(_overlap[i], position);
            Vector3 d = to - from;
            if (d.sqrMagnitude > .000001f)
            {
                do { count = Physics.SphereCastNonAlloc(from, _step.hitRadius, d.normalized, _casts, d.magnitude, _combat.EnemyLayer, QueryTriggerInteraction.Collide); if (count == _casts.Length) Array.Resize(ref _casts, _casts.Length * 2); else break; } while (true);
                for (int i = 0; i < count; i++) Hit(_casts[i].collider, position);
            }
        }
        _previousTime = time; _previousOrigin = origin;
    }
    private void Hit(Collider col, Vector3 root)
    {
        if (_hit || col == null || _targetEnemy == null || col.GetComponentInParent<Enemy>() != _targetEnemy || !_targetEnemy.AcceptsCollider(col)) return;
        if (CombatPhysics.Obstructed(root + Vector3.up, col.bounds.center, transform, _target.transform)) return;
        _hit = true;
        float before = _targetEnemy.CurrentHealth;
        Vector3 d = _targetBody.position - root; d.y = 0;
        var result = _targetEnemy.ReceiveHit(_step.damage, _step.damage * _snapshot.poiseMultiplier, root, d.normalized);
        if (result != EnemyHitResult.Damaged) return;
        DamagePopupManager.Instance?.Show(col.bounds.center, Mathf.Max(0, before - _targetEnemy.CurrentHealth));
        // 致死命中可能同步触发胜利界面并禁用玩家。
        if (!isActiveAndEnabled || Phase == AirPhase.None || _target == null) return;
        bool launching = Phase == AirPhase.Launcher;
        if (launching)
        {
            _launchSucceeded = _target.TryLaunch();
            Status = !_launchSucceeded ? "挑飞条件已结束" : _queuedChase ? "已挑飞！自动追击" : "已挑飞！按 E 追击";
        }
        // 只有最后一段砸地；中间段由敌人空中受击自行上顶，维持滞空。
        else if (IsFinalStrike) _target.SlamDown();
        var kind = launching ? (_launchSucceeded ? CombatImpact.Kind.Launch : CombatImpact.Kind.Hit) : IsFinalStrike ? CombatImpact.Kind.Slam : CombatImpact.Kind.Hit;
        feedback?.Impact(col.bounds.center, Color.cyan);
        _combat.TriggerHitStopScaled(CombatImpact.DefaultStrength(kind));
        CombatImpact.Raise(kind, col.bounds.center);
        SoundManager.Instance?.PlayByPrefix("atk01_hit", col.bounds.center);
    }
    private void Finish(bool keepTarget)
    {
        float speed = _verticalSpeed;
        if (!keepTarget) _pendingTarget = null;
        if (keepTarget && _pendingTarget != null) Status = "已挑飞！按 E 追击";
        else if (Phase == AirPhase.Landing || Phase == AirPhase.Strike) Status = "Q 挑飞破防敌人 · E 追击一次";
        Phase = AirPhase.None; _token++; feedback?.SetTrail(false); _cooldownUntil = Time.time + _snapshot.launcherCooldown;
        _controller.EndAirAction(speed);
    }
    public void Cancel()
    { Phase = AirPhase.None; _token++; _pendingTarget = null; _target = null; _windowSeen = false; feedback?.SetTrail(false); }
    private void OnDisable() { Cancel(); if (_camera != null) _camera.HasSecondaryFocus = false; }
    private void OnDestroy() { foreach (var clip in _clips) if (clip != null) Destroy(clip); if (_snapshot != null) Destroy(_snapshot); if (_pendingSettings != null) Destroy(_pendingSettings); }
}
