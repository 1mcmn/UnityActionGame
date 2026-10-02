# AGENTS.md — 项目开发指南（DeepSeek Harness / AI 助手必读）

> 本文件是 AI 协作的"项目说明书"。每次新会话开始时，请先阅读本文件再动手改代码。
> **记忆库：必须先读 `记忆库/README.md`**（决策/踩坑/技术/进度/对话日志），再按需读分库文件，避免重复调查已记录过的问题。
> 用户使用中文交流，回复请用中文。

## 1. 项目是什么

- **毕业设计**：基于 Unity 的第三人称动作游戏与编辑器工具的设计与实现（学生：邬锦飞，指导教师：王欣）
- **引擎**：团结引擎 Tuanjie 1.9.2（内核 Unity 2022.3.62t10），URP 渲染管线
- **角色模型**：VRoid/VRM 角色（momo 1），UniVRM 导入
- **现状**：最小可玩闭环已完成 —— 角色操控/连击/弹反/顿帧/伤害数字/敌人 13 态 FSM/音效/胜负结算均可用
- **待办**：三个编辑器工具（技能编辑器、怪物生成器、音效管理器），详见第 5 节
- 详细需求见根目录 `毕业设计任务书.md`；历史决策与踩坑记录见 `CODELY.md`

## 2. 环境与常用命令

| 项目 | 值 |
|---|---|
| 团结引擎编辑器 | `C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t10\Editor\Tuanjie.exe` |
| 编译检查（不用开引擎） | `compile_check.bat`（MSBuild 编译 Assembly-CSharp / Assembly-CSharp-Editor，秒级报错；MSB3277 程序集统一警告可忽略） |
| 单元测试（批量模式） | `run_editmode_tests.bat`（跑 Assets/Tests/EditMode，结果在 TestResults/） |
| .NET SDK | 10.x 已装；VS 2026 MSBuild 可用 |

## 3. 代码结构（Assets/Scripts）

- **Core**：`GameManager`（场景流程/胜负结算）、`SoundManager`（单例+AudioSource 对象池+字典查表）、`CameraCache`、`DamagePopupManager`、`GameLog`（日志门控）、`TargetFinder`
- **Player**：`ThirdPersonController`（状态机 Idle/Move/Run/Dodge/Attack/Parry/Hit/Dead）、`PlayerLocomotion`（移动）、`PlayerCombat`（连击/弹反/受击/顿帧）、`PlayerAnimController`
- **Enemy**：`EnemyAI`（13 态 FSM：Spawn→Idle→Walk→RunStart→Run→RunEnd→Attack(7段)→Staggered→Stun*→Death）、`Enemy`、`EnemyStatusBar`
- **Data**：`ComboData`、`EnemyConfig`（ScriptableObject 数据资产，运行时"资产优先、内联字段回退"）
- **Audio**：`SoundLibrary`（soundID ↔ AudioClip 映射，CreateAssetMenu 生成）
- 其他：`Camera/CameraFollow`、`UI/HealthBarUI`、`UI/BossHealthBarUI`、`VFX/DamagePopup`、`Animation/AnimationEventRelay`
- `Assets/Scripts/练习/`：练习代码，可忽略；`Assets/Editor/`：编辑器练习脚本与论文工具草稿

## 4. 关键约定（改代码前必须遵守）

1. **移动全部走 Root Motion**（OnAnimatorMove 驱动），攻击/闪避/弹反期间由动画驱动位移、移动期间由代码驱动 —— 用户明确要求，不要改回 velocity 驱动
2. **绝不修改/创建 .meta 文件**（Unity 自动生成）；`Library/`、`Temp/`、`obj/`、`Logs/`、`TestResults/` 不入库
3. **数据层**：可配置参数放 ScriptableObject（ComboData/EnemyConfig），代码里保留内联默认值做回退，资产未赋值时游戏仍可运行
4. 玩家 GameObject 的 tag 必须为 `Player`（EnemyAI 依赖）；相机引用用 `CameraCache` 而非 `Camera.main`
5. **音效命名**：`{prefix}_NN` 变体 + `SoundManager.PlayByPrefix` 随机播放；攻击音效 `atk0X_swing` / `atk0X_hit` 对应第 X 段连击；脚步声 `foot_step` pitch 随机 0.9~1.1
6. 角色材质：URP Simple Lit（不要用 URP/Lit 或 Unlit，历史已踩坑）；骨骼 J_Bip 命名
7. 架构：组件化（MonoBehaviour）+ 单例管理器 + ScriptableObject 数据（任务书指定，勿引入重型框架）
8. 用户偏好：中文交流；不动 Unity 版本；不批量重命名脚本；AI 改完代码先跑 `compile_check.bat` 确认编译通过；**任何代码/资源改动前，先列出方案对比（各方案优点、缺点、改动范围），等用户明确选择后再动手**（用户 2026-08-22 明确要求）

## 5. 论文要求（三个编辑器工具，**必须手写**）

> 任务书指定技术栈：EditorWindow、CustomEditor、SerializedObject、ReorderableList。
> **禁止用 Odin / NaughtyAttributes / Editor Toolbox 等自动生成 Inspector 的插件**（会削弱论文技术深度），可在论文中作为"对比方案"提及。

1. **技能编辑器（Skill Editor）**：EditorWindow 可视化表格 —— 编辑连击段数、每段伤害、攻击动画 clip 映射、音效前缀映射 → 保存为 `ComboData` 资产
2. **怪物生成器（Enemy Spawner）**：EditorWindow + SceneView 交互 —— 选怪物类型 → Scene 视图点击放置 → 自动配置 EnemyAI 参数（检测半径/移动速度/攻击间隔）→ `EnemyConfig` 资产；支持批量生成与巡逻路径可视化编辑
3. **音效管理器（Audio Manager）**：CustomEditor 扩展 `SoundLibrary` Inspector —— 表格化编辑 soundID↔clip、文件夹批量导入、编辑器内试听

核心目标：**编辑器修改 → ScriptableObject 资产 → 运行时即时生效**（数据解耦）。

## 6. AI 高效迭代工作流

1. 写/改 C# → 2. `compile_check.bat`（MSBuild 拿编译错误，不阻塞 Unity）→ 3. 全绿后由用户开引擎验收，或跑 `run_editmode_tests.bat` 回归 → 4. 用户反馈截图 / `Logs/` 与 `TestResults/` 日志 → 5. 继续迭代
- 编辑器代码（Assets/Editor）编进 `Assembly-CSharp-Editor`，同样可 MSBuild 检查
- 不要试图修改 dsh 配置或重启 DeepSeek Harness 服务器（会中断会话）

## 7. 明确不要做

- 不装/不用：Odin、NaughtyAttributes、Feel、Animancer、KCC、A* Pathfinding（付费或架空论文技术点；任务书指定 Rigidbody 方案与距离 FSM）
- 不改 Unity/团结引擎版本、不动 .meta、不删除 `Assets/Imports` 素材、不重命名现有脚本类名
