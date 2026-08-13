

## Codely Structured Memories

### User

### Feedback
- [2026-08-10 02:31:50] User communicates in Chinese. Prefers root motion over velocity-driven movement for character animation. Wanted all movement (including normal walk/run) driven by OnAnimatorMove, not just attack/dodge/parry.

### Project
- [2026-08-10 02:31:48] User is working on a Unity 3D action game (third-person character controller) for a graduation project (毕业设计). Character is "momo 1" with ThirdPersonController, PlayerLocomotion, PlayerAnimController, PlayerCombat components. Scene: "DEMO City Crossing". Project uses URP (Ultra_PipelineAsset). Unity version: Tuanjie 1.9.2 (2022.3.62t10).
- [2026-08-10 03:14:52] Player character materials were originally Unlit/Transparent Cutout (built-in RP). URP/Lit was tried but textures did not render (flat grey/white). URP/Unlit worked for textures but had no shading. Final fix: all 14 materials switched to Universal Render Pipeline/Simple Lit — textures display correctly AND have basic cel-shading with light/shadow. Cleaned up fake 8x8 textures (Shader_NoneNormal, Shader_NoneBlack). No alpha clipping (_AlphaClip=0, _Surface=0/Opaque). Skeleton uses J_Bip naming convention.

- [2026-08-14 03:12:17] Animator controller "Player_NewAnimCtrl.controller" Movement SM: idle(base) → Locomotion(1D BlendTree on Movement)，无起步动画。Start Walk/Walk_Start 起步动画曾被反复加回(改名)导致粘滞感，已彻底删除，idle→Locomotion 直接过渡(Movement>0.1, 0.1s)。Base layer Any State transitions: Attack(fixed 0.15s), Dodge, Parry, Hit(×3方向), Death。Attack SM 4 状态(normal/at2/at3/at4) m_Speed 2。PlayerLocomotion accelSpeed 已从 2.5 调到 6 对齐 decelSpeed(消除起步拖沓)。


- [2026-08-13 23:04:01] Scripts reorganized into Assets/Scripts/{Core,Player,Enemy,Camera,UI,VFX,Audio,Animation,Data}; Editor scripts under Assets/Editor. Data layer added: ComboData + EnemyConfig ScriptableObjects, wired via Awake config-seeding with fallback to inline fields (prefabs work without assigning assets). OnPlayerDamaged now only in PlayerCombat. Camera.main replaced with CameraCache; SoundManager uses AudioSource pool; GameLog gates verbose logs.
- [2026-08-14 00:30:43] Attack combo system uses input buffering + explicit combo window. PlayerCombat: BufferCombo() caches attack input, ConsumeBufferedCombo() advances combo only when attackTimer passes comboWindowPercent (default 0.55, configurable in ComboData asset). Removed old CanCombo=>true + TryCombo() (caused "spam stuck in windup" and desynced damage/animation). ThirdPersonController.UpdateAttack() order: buffer input → consume buffered combo → decrement timer. Attack Animator SM has 4 states (normal/at2/at3/at4) all m_Speed 2, chained via NextAttack trigger.
- [2026-08-14 01:18:39] 最小可玩闭环已打通。关键修复：(1) 玩家 tag 从 Untagged 改为 Player（否则 EnemyAI FindGameObjectWithTag 找不到玩家）；(2) GameManager._enemiesContainer 之前误指向敌人本体而非 Enemies 容器对象，导致统计/订阅失效；(3) 敌人 Start 时找玩家但玩家在开始界面隐藏，故新增 EnemyAI.RefreshPlayerReference() 在 StartGame 后调用。敌人伤害：EnemyAI 攻击每段延迟 _hitDelay(0.25s) 距离<攻击半径1.3倍时 TryTakeDamage(_attackDamage=12)。PlayerCombat 血量归零广播 OnPlayerDeath。GameManager 统计敌人死亡(全灭=胜利)/玩家死亡(失败)，弹结算 UI(EndCanvas 动态创建) + Restart 重载场景。
- [2026-08-14 02:00:31] 玩家受击/死亡动画已接入 Animator：新增参数 HitType(Int 0=前/1=左/2=右)、Hit(Trigger)、Death(Trigger)，Base Layer 新增 HitForward/HitLeft/HitRight/Death 四状态，全部从 Any State 触发，受击 ExitTime→idle。PlayerCombat.TakeDamage(damage, attackerPosition) 用 SignedAngle 判定方向广播 OnPlayerHit(int)。ThirdPersonController 新增 Hit/Dead 状态（受击 Halt+硬直0.6s→Idle，死亡不响应输入）。死亡动画不播放的根因是 GameManager.OnPlayerDeath 立即 SetActive(false) 隐藏玩家，修复为 _deathDelay=2s 延迟弹结算 + 死亡时 SetInvulnerable(true)。敌人攻击范围收紧：伤害判定系数 1.3f→0.8f，场景 _attackRadius 2.5→1.8、_closeRadius 4→3。
- [2026-08-14 03:12:23] 敌人血条/僵直条改为屏幕空间 UI：新脚本 UI/BossHealthBarUI.cs 挂在 HUDCanvas/BossHealthBar（屏幕底部中央，血条500×24红色、僵直条520×8橙色紧贴下方），由 GameManager.RegisterEnemies 绑定第一个敌人。删除了旧的头顶 World Space EnemyStatusBar（Enemy.Start 里不再 Instantiate）。音频现状：Assets/Imports/FOOT 有攻击挥砍5个、命中7个、脚步8个、收脚2个 wav，SoundManager+SoundLibrary 已就绪但战斗音效未接线。手感改进待办：顿帧(HitStop 被注释)、攻击/受击/死亡音效、弹刀反馈、BGM。

### Reference

