using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>大剑敌人的有限攻防循环；旧 EnemyAI 仅保留兼容接口。</summary>
[RequireComponent(typeof(Enemy), typeof(Rigidbody), typeof(EnemyAI))]
public class GreatSwordEnemyBrain : MonoBehaviour
{
    public EnemyConfig config;
    public Animator animator;
    public CombatFeedback feedback;
    public EnemyConfig Config => _snapshot != null ? _snapshot : config;
    public bool IsConfigured => config != null && config.greatSwordEnabled;
    public EnemyState State { get; private set; } = EnemyState.Idle;
    public bool IsBroken => _broken;
    public bool CanLaunch => isActiveAndEnabled && _enemy != null && !_enemy.IsDead && _broken && !_launchedThisBreak && (State == EnemyState.StunStart || State == EnemyState.StunLoop);
    public bool CanAirFollow => isActiveAndEnabled && _enemy != null && State == EnemyState.Airborne && !_followupConsumed && !_enemy.IsDead;
    public string ActionHint => _broken ? (CanLaunch ? "破防！Q 挑飞" : (State == EnemyState.Airborne ? "浮空中" : "压制期")) :
        (CanParryNow ? "红色破绽 · 右键弹反" : (IsGuardState ? $"正面格挡 {_guardHits}/{Config.guardCounterHits}" : "观察起手，侧闪或绕后"));
    public bool CanParryNow => State == EnemyState.Attack && _action != null && _action.parryable &&
        ActionTime >= _action.parryStart && ActionTime < _action.parryEnd;

    private Enemy _enemy;
    private Rigidbody _body;
    private Collider _collider;
    private ThirdPersonController _target;
    private EnemyConfig _snapshot;
    private AnimatorOverrideController _override;
    private RuntimeAnimatorController _original;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private readonly AnimationClip[] _clips = new AnimationClip[2];
    private readonly List<AnimationClip> _staticClips = new List<AnimationClip>();
    private Collider[] _overlap = new Collider[32];
    private RaycastHit[] _casts = new RaycastHit[32];
    private EnemyHurtbox[] _hurtboxes;
    private EnemyAttackDefinition _action;
    private AnimationClip _activeClip;
    private int _actionIndex, _slot, _token, _stateHash;
    private float _age, _duration, _cooldown, _guardUntil, _lastGuardAt, _nextHitReaction;
    private int _guardHits, _attackCycle;
    private bool _windowOpen, _windowSeen, _delivered, _broken, _launchedThisBreak, _followupConsumed, _started;
    private float _previousTime, _breakRemaining, _airVelocity, _airAge, _holdRemaining, _fallVelocity;
    private Vector3 _previousOrigin, _spawn, _patrol, _push;
    private float _patrolWait, _pushRemaining;
    private bool IsGuardState => State == EnemyState.Guard || State == EnemyState.GuardStart || State == EnemyState.GuardHit;

    private void Awake()
    {
        _enemy = GetComponent<Enemy>(); _body = GetComponent<Rigidbody>(); _collider = GetComponent<Collider>();
        _hurtboxes = GetComponentsInChildren<EnemyHurtbox>(true);
        if (!IsConfigured) { enabled = false; return; }
        _snapshot = Instantiate(config); _snapshot.hideFlags = HideFlags.DontSave;
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (feedback == null) feedback = GetComponent<CombatFeedback>();
        _spawn = _body.position; ChoosePatrol();
        _body.useGravity = false; _body.isKinematic = false;
        _body.constraints = RigidbodyConstraints.FreezeRotation;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (animator != null)
        {
            animator.updateMode = AnimatorUpdateMode.AnimatePhysics; animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var relay = animator.GetComponent<EnemyAnimationRelay>();
            if (relay == null) relay = animator.gameObject.AddComponent<EnemyAnimationRelay>();
            relay.Bind(this, animator);
        }
    }

    private void Start()
    {
        if (!IsConfigured || Config.ValidateGreatSword().Count > 0 || animator == null || animator.runtimeAnimatorController == null ||
            !Config.lightAttack1.Valid || !Config.lightAttack2.Valid || !Config.heavyAttack.Valid || !Config.counterAttack.Valid ||
            Config.attackPlaceholderA == null || Config.attackPlaceholderB == null)
        { Debug.LogError("[大剑敌人] 缺动画/控制器或攻击窗口非法，请重新配置演示资源。", this); enabled = false; return; }
        _original = animator.runtimeAnimatorController;
        _override = new AnimatorOverrideController(_original) { hideFlags = HideFlags.DontSave };
        _override.GetOverrides(_overrides);
        for (int i = 0; i < _overrides.Count; i++)
        {
            var key = _overrides[i].Key; AnimationClip source = null;
            switch (key.name)
            {
                case "Idle": source = Config.idleClip; break;
                case "Walk": source = Config.walkClip; break;
                case "Run": source = Config.runClip; break;
                case "GuardStart": source = Config.guardStartClip; break;
                case "GuardLoop": source = Config.guardLoopClip; break;
                case "GuardHit": source = Config.guardHitClip; break;
                case "Hit": source = Config.hitClip; break;
                case "DownStart": source = Config.downStartClip; break;
                case "DownLoop": source = Config.downLoopClip; break;
                case "GetUp": source = Config.getUpClip; break;
                case "Death": source = Config.deathClip; break;
                case "Airborne": source = Config.airborneClip; break;
            }
            if (source == null) continue;
            var copy = Instantiate(source); copy.hideFlags = HideFlags.DontSave; copy.events = Array.Empty<AnimationEvent>();
            _staticClips.Add(copy); _overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, copy);
        }
        _override.ApplyOverrides(_overrides); animator.runtimeAnimatorController = _override;
        _enemy.OnHit += OnDamaged; _enemy.OnKnockdown += BeginBreak; _enemy.OnDeath += Die;
        _started = true; RefreshTarget(); Enter(EnemyState.Idle, "Idle");
    }

    public void RefreshTarget()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        _target = player != null ? player.GetComponent<ThirdPersonController>() : null;
    }

    private void FixedUpdate()
    {
        if (!_started || _enemy.IsDead) return;
        float dt = Time.fixedDeltaTime;
        _age += dt; _cooldown = Mathf.Max(0, _cooldown - dt);
        if (_guardHits > 0 && Time.time - _lastGuardAt > Config.guardCountTimeout) _guardHits = 0;
        if (_target == null || !_target.gameObject.activeInHierarchy) RefreshTarget();
        bool valid = _target != null && _target.gameObject.activeInHierarchy && _target.State != PlayerState.Dead;
        float distance = valid ? Vector3.Distance(_body.position, _target.transform.position) : float.MaxValue;
        switch (State)
        {
            case EnemyState.Idle:
            case EnemyState.Walk:
            case EnemyState.Run:
                if (!valid || distance > Config.detectRadius * Config.loseTargetMultiplier)
                { Enter(Config.patrolEnabled ? EnemyState.Patrol : EnemyState.Idle, Config.patrolEnabled ? "Walk" : "Idle"); break; }
                if (distance <= Config.attackRadius)
                { _guardUntil = Time.time + Config.guardDuration; Enter(EnemyState.GuardStart, "GuardStart", Length(Config.guardStartClip)); }
                else SetMoveState(distance > Config.closeRadius ? EnemyState.Run : EnemyState.Walk);
                break;
            case EnemyState.Patrol:
                if (valid && distance <= Config.detectRadius) { SetMoveState(EnemyState.Walk); break; }
                Vector3 diff = _patrol - _body.position; diff.y = 0;
                if (diff.magnitude <= Config.patrolArrivalDistance)
                {
                    _patrolWait += dt;
                    if (_patrolWait >= Config.patrolWaitTime) { ChoosePatrol(); animator.CrossFadeInFixedTime("Walk", .1f); }
                    else if (_patrolWait <= dt) animator.CrossFadeInFixedTime("Idle", .1f);
                }
                break;
            case EnemyState.GuardStart:
                if (_age >= _duration) Enter(EnemyState.Guard, "GuardLoop");
                break;
            case EnemyState.GuardHit:
                if (_age >= _duration) Enter(EnemyState.Guard, "GuardLoop");
                break;
            case EnemyState.Guard:
                if (!valid || distance > Config.attackRadius * 1.5f) { Enter(EnemyState.Idle, "Idle"); break; }
                if (Time.time >= _guardUntil && _cooldown <= 0) BeginAttack((++_attackCycle % 2) == 0 ? 2 : 0);
                break;
            case EnemyState.Attack:
                if (ActionTime >= _action.clip.length || _age > _duration + .5f)
                {
                    if (_actionIndex == 0 && valid && distance <= Config.attackRadius * 1.35f) BeginAttack(1);
                    else { _cooldown = Config.attackCooldown; Enter(EnemyState.Recover, "Idle", .45f); }
                }
                break;
            case EnemyState.Staggered:
            case EnemyState.Recover:
                if (_age >= _duration) Enter(EnemyState.Idle, "Idle");
                break;
            case EnemyState.StunStart:
                if (_age >= _duration) Enter(EnemyState.StunLoop, "DownLoop");
                break;
            case EnemyState.StunLoop:
                _breakRemaining -= dt;
                if (_breakRemaining <= 0) Enter(EnemyState.StunEnd, "GetUp", Length(Config.getUpClip));
                break;
            case EnemyState.StunEnd:
                if (_age >= _duration)
                { _broken = false; _enemy.ResetPoise(); _cooldown = .5f; Enter(EnemyState.Idle, "Idle"); }
                break;
            case EnemyState.Airborne:
                _airAge += dt;
                if (_airAge > Config.maximumAirDuration) { _holdRemaining = 0; _airVelocity = -Config.slamSpeed; }
                break;
        }
        feedback?.SetWarning(CanParryNow);
    }

    private void SetMoveState(EnemyState state)
    { if (State != state) Enter(state, state == EnemyState.Run ? "Run" : "Walk"); }
    private void ChoosePatrol()
    { Vector2 p = UnityEngine.Random.insideUnitCircle * Config.patrolRadius; _patrol = _spawn + new Vector3(p.x, 0, p.y); _patrolWait = 0; }

    private void Enter(EnemyState state, string animation, float duration = 0)
    {
        _token++; _action = null; _windowOpen = _windowSeen = _delivered = false;
        feedback?.SetTrail(false); feedback?.SetWarning(false);
        if (!IsGuardState && state != EnemyState.GuardStart) _guardHits = 0;
        State = state; _age = 0; _duration = Mathf.Max(.05f, duration);
        foreach (var hurt in _hurtboxes) hurt.SetProne(state == EnemyState.StunLoop || state == EnemyState.StunStart);
        int hash = Animator.StringToHash("Base Layer." + animation);
        if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, .08f, 0, 0);
        else { Debug.LogError("[大剑敌人] 缺少状态：" + animation, this); enabled = false; }
    }

    private void BeginAttack(int index)
    {
        var definition = index == 0 ? Config.lightAttack1 : index == 1 ? Config.lightAttack2 : index == 2 ? Config.heavyAttack : Config.counterAttack;
        Enter(EnemyState.Attack, "Idle"); _actionIndex = index; _action = definition.Copy();
        _duration = _action.clip.length / _action.speed; _slot = 1 - _slot;
        var clip = Instantiate(_action.clip); clip.hideFlags = HideFlags.DontSave; clip.name = "EnemyAttack_" + _token;
        clip.events = new[] { Event("EnemyHitOpen", _action.hitStart, clip), Event("EnemyHitClose", _action.hitEnd, clip) };
        if (_clips[_slot] != null) Destroy(_clips[_slot], .5f);
        _clips[_slot] = _activeClip = clip;
        var placeholder = _slot == 0 ? Config.attackPlaceholderA : Config.attackPlaceholderB;
        for (int i = 0; i < _overrides.Count; i++) if (_overrides[i].Key == placeholder)
            _overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, clip);
        _override.ApplyOverrides(_overrides);
        animator.SetFloat(_slot == 0 ? "EnemySpeedA" : "EnemySpeedB", _action.speed);
        _stateHash = Animator.StringToHash(_slot == 0 ? "Base Layer.EnemySlotA" : "Base Layer.EnemySlotB");
        animator.CrossFadeInFixedTime(_stateHash, .06f, 0, 0);
        _previousTime = 0; _previousOrigin = Origin(_body.position, _body.rotation);
        SoundManager.Instance?.PlayByPrefix("atk01_swing", transform.position);
    }

    private AnimationEvent Event(string name, float time, AnimationClip clip) => new AnimationEvent
    { functionName = name, time = time, intParameter = _token, objectReferenceParameter = clip, messageOptions = SendMessageOptions.RequireReceiver };
    public void OnAttackEvent(AnimationEvent e, bool open)
    {
        if (e == null || State != EnemyState.Attack || e.intParameter != _token || e.objectReferenceParameter != _activeClip) return;
        _windowOpen = open; _windowSeen |= open; feedback?.SetTrail(open);
    }
    private float ActionTime
    {
        get
        {
            if (_action == null || animator == null) return 0;
            var s = animator.GetCurrentAnimatorStateInfo(0);
            if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == _stateHash) s = animator.GetNextAnimatorStateInfo(0);
            return s.fullPathHash == _stateHash ? Mathf.Max(0, s.normalizedTime) * _action.clip.length : 0;
        }
    }
    private Vector3 Origin(Vector3 p, Quaternion r) => p + r * (_action != null ? _action.hitOffset : Vector3.up);

    public void ApplyRootMotion(Animator source)
    {
        if (!_started || !enabled || source != animator) return;
        float dt = Time.fixedDeltaTime;
        Vector3 delta = Vector3.zero; Quaternion rotation = _body.rotation;
        bool moving = State == EnemyState.Walk || State == EnemyState.Run || (State == EnemyState.Patrol && _patrolWait <= 0);
        Vector3 target = State == EnemyState.Patrol ? _patrol : (_target != null ? _target.transform.position : _body.position);
        Vector3 toward = target - _body.position; toward.y = 0;
        bool followFacing = moving || IsGuardState || (State == EnemyState.Attack && ActionTime < _action.lockFacingTime);
        if (followFacing && toward.sqrMagnitude > .001f)
            rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(toward), (IsGuardState ? 90f : Config.rotationSpeed * 45f) * dt);
        if (moving)
        {
            float remaining = Mathf.Max(0, toward.magnitude - (State == EnemyState.Patrol ? Config.patrolArrivalDistance : Config.attackRadius));
            delta = toward.normalized * Mathf.Min(remaining, (State == EnemyState.Run ? Config.runSpeed : Config.walkSpeed) * dt);
        }
        else if (State == EnemyState.Attack) { delta = source.deltaPosition * _action.rootMotionScale; delta.y = 0; }
        else if (State == EnemyState.Staggered || State == EnemyState.StunStart || State == EnemyState.StunEnd || State == EnemyState.Death)
        { delta = source.deltaPosition * .3f; delta.y = 0; }
        if (_pushRemaining > 0)
        { float step = Mathf.Min(dt, _pushRemaining); delta += _push * (step / .15f); _pushRemaining -= step; }
        float floor;
        if (CombatPhysics.Ground(_body.position, transform, out floor))
        {
            if (State == EnemyState.Airborne)
            {
                if (_airVelocity <= 0 && _holdRemaining > 0) { _holdRemaining -= dt; delta.y = 0; }
                else { _airVelocity -= Config.airGravity * dt; delta.y = _airVelocity * dt; }
                if (_body.position.y + delta.y <= floor + .02f && _airVelocity < 0)
                { delta.y = floor + .02f - _body.position.y; _breakRemaining = Mathf.Max(_breakRemaining, 1.5f); Enter(EnemyState.StunStart, "DownStart", Length(Config.downStartClip)); feedback?.Impact(_body.position, Color.yellow); }
            }
            else if (_body.position.y > floor + .06f)
            { _fallVelocity -= Config.airGravity * dt; delta.y = Mathf.Max(floor + .02f - _body.position.y, _fallVelocity * dt); }
            else { _fallVelocity = 0; delta.y = floor + .02f - _body.position.y; }
        }
        delta = CombatPhysics.LimitMotion(_body, _collider, delta);
        Vector3 next = _body.position + delta;
        _body.MovePosition(next); _body.MoveRotation(rotation);
        SampleAttack(next, rotation);
    }

    private void SampleAttack(Vector3 position, Quaternion rotation)
    {
        if (_action == null || State != EnemyState.Attack) return;
        float time = ActionTime; Vector3 origin = Origin(position, rotation);
        float begin = Mathf.Max(_previousTime, _action.hitStart), end = Mathf.Min(time, _action.hitEnd);
        if (!_delivered && (_windowOpen || _windowSeen) && end > begin && time > _previousTime)
        {
            Vector3 from = Vector3.Lerp(_previousOrigin, origin, Mathf.Clamp01((begin - _previousTime) / (time - _previousTime)));
            Vector3 to = Vector3.Lerp(_previousOrigin, origin, Mathf.Clamp01((end - _previousTime) / (time - _previousTime)));
            int count;
            while (true) { count = Physics.OverlapSphereNonAlloc(from, _action.hitRadius, _overlap, ~0, QueryTriggerInteraction.Ignore); if (count < _overlap.Length) break; Array.Resize(ref _overlap, _overlap.Length * 2); }
            for (int i = 0; i < count; i++) TryHitPlayer(_overlap[i], position, origin);
            Vector3 d = to - from;
            if (d.sqrMagnitude > .000001f)
            {
                while (true) { count = Physics.SphereCastNonAlloc(from, _action.hitRadius, d.normalized, _casts, d.magnitude, ~0, QueryTriggerInteraction.Ignore); if (count < _casts.Length) break; Array.Resize(ref _casts, _casts.Length * 2); }
                for (int i = 0; i < count; i++) TryHitPlayer(_casts[i].collider, position, origin);
            }
        }
        _previousTime = time; _previousOrigin = origin;
    }

    private void TryHitPlayer(Collider col, Vector3 root, Vector3 origin)
    {
        if (_delivered || col == null) return;
        var player = col.GetComponentInParent<ThirdPersonController>();
        if (player == null || player.State == PlayerState.Dead || CombatPhysics.Obstructed(root + Vector3.up, col.bounds.center, transform, player.transform)) return;
        _delivered = true; float hp = player.CurrentHealth; bool blocked = player.IsBlocking;
        player.TryTakeDamage(_action.damage, root);
        if (blocked || player.CurrentHealth < hp) feedback?.Impact(col.ClosestPoint(origin), blocked ? Color.yellow : Color.cyan);
    }

    public bool CanGuard(Vector3 attacker)
    {
        Vector3 d = attacker - _body.position; d.y = 0;
        return !_broken && IsGuardState && d.sqrMagnitude > .001f &&
            Vector3.Dot(_body.rotation * Vector3.forward, d.normalized) >= Mathf.Cos(Config.guardHalfAngle * Mathf.Deg2Rad);
    }
    public void OnGuarded()
    {
        feedback?.Impact(transform.position + Vector3.up, Color.yellow);
        if (_broken || _enemy.IsDead) return;
        _lastGuardAt = Time.time; _guardHits++;
        if (_guardHits >= Config.guardCounterHits) { _guardHits = 0; BeginAttack(3); }
        else Enter(EnemyState.GuardHit, "GuardHit", Length(Config.guardHitClip));
    }
    public bool TryParry(Vector3 position, Vector3 facing)
    {
        Vector3 toward = _body.position - position; toward.y = 0; facing.y = 0;
        if (!CanParryNow || _enemy.IsDead || toward.magnitude > Config.attackRadius + .6f ||
            Vector3.Dot(facing.normalized, toward.normalized) < .35f ||
            Vector3.Dot(_body.rotation * Vector3.forward, -toward.normalized) < .25f ||
            CombatPhysics.Obstructed(position + Vector3.up, _body.position + Vector3.up, _target != null ? _target.transform : transform, transform)) return false;
        _enemy.AddPoise(Config.parryPoiseDamage);
        if (!_broken) Enter(EnemyState.Staggered, "Hit", Mathf.Max(.7f, Length(Config.hitClip)));
        feedback?.Impact(_body.position + Vector3.up, Color.yellow);
        return true;
    }
    private void OnDamaged(float damage)
    {
        feedback?.Pulse(Color.white);
        if (_enemy.IsDead || _broken || (State == EnemyState.Attack && _action != null && _action.superArmor) || Time.time < _nextHitReaction) return;
        _nextHitReaction = Time.time + Config.ordinaryHitCooldown;
        Enter(EnemyState.Staggered, "Hit", Length(Config.hitClip));
    }
    private void BeginBreak()
    {
        if (_broken || _enemy.IsDead) return;
        _broken = true; _launchedThisBreak = _followupConsumed = false; _breakRemaining = Config.breakDuration;
        Enter(EnemyState.StunStart, "DownStart", Length(Config.downStartClip)); feedback?.Pulse(Color.yellow, .3f);
    }
    public bool TryLaunch()
    {
        if (!CanLaunch || _enemy.IsDead) return false;
        _launchedThisBreak = true; _followupConsumed = false;
        _airVelocity = Mathf.Sqrt(2 * Config.airGravity * Config.launchHeight); _airAge = 0; _holdRemaining = Config.apexHoldTime;
        Enter(EnemyState.Airborne, "Airborne"); return true;
    }
    public bool ReserveAirFollow()
    { if (!CanAirFollow) return false; _followupConsumed = true; return true; }
    public void SlamDown() { if (State == EnemyState.Airborne) { _holdRemaining = 0; _airVelocity = -Config.slamSpeed; } }
    public void QueueKnockback(Vector3 direction, float distance)
    {
        if (State == EnemyState.Attack && _action != null && _action.superArmor) distance *= .2f;
        direction.y = 0; _push = direction.normalized * Mathf.Clamp(distance, 0, .6f); _pushRemaining = .15f;
    }
    private void Die() { _push = Vector3.zero; _pushRemaining = 0; Enter(EnemyState.Death, "Death"); }
    private float Length(AnimationClip clip) => clip != null ? Mathf.Max(.05f, clip.length) : .3f;
    private void OnEnable() { if (_started && !_enemy.IsDead) { _broken = false; _enemy.ResetPoise(); Enter(EnemyState.Idle, "Idle"); } }
    private void OnDisable() { _token++; _action = null; _windowOpen = _windowSeen = false; _pushRemaining = 0; feedback?.SetTrail(false); feedback?.SetWarning(false); }
    private void OnDestroy()
    {
        if (_enemy != null) { _enemy.OnHit -= OnDamaged; _enemy.OnKnockdown -= BeginBreak; _enemy.OnDeath -= Die; }
        if (animator != null && _original != null) animator.runtimeAnimatorController = _original;
        foreach (var clip in _clips) if (clip != null) Destroy(clip);
        foreach (var clip in _staticClips) if (clip != null) Destroy(clip);
        if (_override != null) Destroy(_override); if (_snapshot != null) Destroy(_snapshot);
    }
}
