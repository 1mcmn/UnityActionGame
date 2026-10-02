# AGENTS.md — 项目开发指南（DeepSeek Harness / AI 助手必读）

> 本文件是 AI 协作的"项目说明书"。每次新会话开始时，请先阅读本文件再动手改代码。
> **记忆库：必须先读 `记忆库/README.md`**（决策/踩坑/技术/进度/对话日志），再按需读分库文件，避免重复调查已记录过的问题。
> 用户使用中文交流，回复请用中文。

## 1. 项目是什么

- **毕业设计**：基于 Unity 的第三人称动作游戏与编辑器工具的设计与实现（学生：邬锦飞，指导教师：王欣）
- **引擎**：团结引擎 Tuanjie 1.9.2（内核 Unity 2022.3.62t10），URP 渲染管线
- **角色模型**：VRoid/VRM 角色（momo 1），UniVRM 导入
- **当前目标（2026-10-01）**：动作游戏 Demo + 一个连招配置编辑器。用户确认新版任务书已提交或获导师确认；旧“三工具”要求属于历史方案。
- **现状**：已实现 ComboData.steps、手写连招窗口、普通左键连招接入、内置动画事件、保存/显式重载、跳跃、巡逻、连招提示与性能记录；主场景已通过引擎 API 保存接线。现场动作时序与PC战斗60FPS仍需实测，代码完成不能代替验收。
- **已选方案 A**：用户已选择渐进扩展 ComboData 并授权本轮持续完成实现和验收说明，不需重复询问同一方向。旧 SkillSystem 保留有引用原型，主入口为 Tools/连招配置编辑器；完整结果见 `实现与验收说明_2026-10-01.md`。
- 详细需求见根目录 `毕业设计任务书.md`；历史决策与踩坑记录见 `CODELY.md`

## 2. 环境与常用命令

| 项目 | 值 |
|---|---|
| 团结引擎编辑器 | `C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t10\Editor\Tuanjie.exe` |
| 编译检查（不用开引擎） | `compile_check.bat --nopause`（自动定位项目和 MSBuild；依次编译 Game、Assembly-CSharp、Assembly-CSharp-Editor 及其项目引用；失败返回非零） |
| 单元测试（批量模式） | `run_editmode_tests.bat`（跑 Assets/Tests/EditMode，结果在 TestResults/） |
| MSBuild | 当前可用：VS 2022 Community；通过 vswhere 自动发现，可用环境变量 `COMPILE_CHECK_MSBUILD` 指定 MSBuild.exe 完整路径 |

> 2026-10-01 更新：编译脚本按所在目录发现工程与可用 MSBuild，三个程序集编译通过；增删脚本后仍需由引擎重新生成工程文件。EditMode入口已修复旧E盘路径与恒定成功码，支持 `--nopause`、`--check-only` 及 `EDITMODE_EDITOR`，每次创建独立结果目录并检查新鲜XML及编辑器退出码。最终检查、测试和构建结果见验收说明；编译成功不能代替测试或运行验收。

## 3. 代码结构（Assets/Scripts）

- **Core**：`GameManager`（场景流程/胜负结算）、`SoundManager`（单例+AudioSource 对象池+字典查表）、`CameraCache`、`DamagePopupManager`、`GameLog`（日志门控）、`TargetFinder`
- **Player**：`ThirdPersonController`（状态分组与Jump）、`PlayerLocomotion`（移动计算）、`PlayerCombat` + partial `PlayerComboRuntime`（普通连招/事件/判定/重载/弹反/受击/顿帧）、`PlayerAnimController`（动画映射与根位移入口）
- **Enemy**：`EnemyAI`（原FSM追加Patrol；攻击仅执行真实存在的动画，不伪造缺失段）、`Enemy`、`EnemyStatusBar`
- **Data**：`ComboData`、`EnemyConfig`（ScriptableObject 数据资产，运行时"资产优先、内联字段回退"）
- **Audio**：`SoundLibrary`（soundID ↔ AudioClip 映射，CreateAssetMenu 生成）
- 其他：`Camera/CameraFollow`、`UI/HealthBarUI`、`UI/BossHealthBarUI`、`VFX/DamagePopup`、`Animation/AnimationEventRelay`
- `Assets/Scripts/练习/`：练习代码，可忽略；`Assets/Editor/ComboEditorWindow.cs` 为正式连招窗口，`ComboDemoSetup.cs` 为显式接线/构建入口；历史编辑器草稿保留
- `Assets/Game/Generated/ComboDemo/`：主场景专用控制器、两个唯一占位动画、PlayerCombo.asset、ComboDemoSounds.asset。首次资源由引擎创建，.meta只能由引擎生成

## 4. 关键约定（改代码前必须遵守）

1. **移动全部走 Root Motion**（OnAnimatorMove 驱动），攻击/闪避/弹反期间由动画驱动位移、移动期间由代码驱动 —— 用户明确要求，不要改回 velocity 驱动
2. **绝不修改/创建 .meta 文件**（Unity 自动生成）；`Library/`、`Temp/`、`obj/`、`Logs/`、`TestResults/` 不入库
3. **数据层**：可配置参数放 ScriptableObject（ComboData/EnemyConfig），代码里保留内联默认值做回退，资产未赋值时游戏仍可运行
4. 玩家 GameObject 的 tag 必须为 `Player`（EnemyAI 依赖）；相机引用用 `CameraCache` 而非 `Camera.main`
5. **音效命名**：`{prefix}_NN` 变体 + `SoundManager.PlayByPrefix` 随机播放；攻击音效 `atk0X_swing` / `atk0X_hit` 对应第 X 段连击；脚步声 `foot_step` pitch 随机 0.9~1.1
6. 角色材质：URP Simple Lit（不要用 URP/Lit 或 Unlit，历史已踩坑）；骨骼 J_Bip 命名
7. 架构：组件化（MonoBehaviour）+ 单例管理器 + ScriptableObject 数据（任务书指定，勿引入重型框架）
8. 用户偏好：中文交流；不动 Unity 版本；不批量重命名脚本；AI 改完代码先跑 `compile_check.bat` 确认编译通过；**任何代码/资源改动前，先列出方案对比（各方案优点、缺点、改动范围），等用户明确选择后再动手**（用户 2026-08-22 明确要求）

## 5. 当前论文要求（一个连招配置编辑器，必须手写）

> 新任务书要求 EditorWindow、ScriptableObject；继续采用 SerializedObject、ReorderableList 等手写编辑器技术。CustomEditor 可作资产入口，不扩张为独立必做工具。
> 禁止用 Odin / NaughtyAttributes / Editor Toolbox 等自动生成 Inspector 的插件。

1. **连招编辑器**：段的添加/删除/排序；每段动画片段、伤害、判定起止、输入窗口、位移、动画事件；保存/加载 ComboData，提供错误校验。
2. **普通攻击接入**：至少三段完整配置与运行；窗口外不接段、同段不重复启动、打断清理；明确提供配置重载入口。不能用技能测试键代替普通连招接入。
3. **游戏验证平台**：Rigidbody 与 Root Motion、走跑/跳跃、Blend Tree、状态层次与打断、受击/击退/硬直、敌人巡逻/追击/攻击、血量及连招提示。
4. **测试与论文**：任务书六章结构；功能、保存/加载/重载测试，PC 构建和 60 FPS 实测记录。

怪物生成器、独立音效编辑器、通用技能图和 UI 主题不作为当前新增必做范围。已有 SkillSystem 在场景中有引用，必须核对依赖再迁移清理。当前 ComboData 已包含连招段列表，旧全局参数仅保留回退。正式演示不能以旧技能测试键替代普通左键。

核心目标：**编辑器修改 → 保存 ScriptableObject → 明确重载 → 普通连招执行新配置**。区分编辑值、已保存值、运行中版本；迁移期间保留内联回退，但正式演示必须挂载真实资产。

任务书两处命中 API 口径有差异，见任务书末尾核对注。不得将 OverlapCapsule 等同于 CapsuleCollider，或把自制帧事件称为已完成的 Unity Animation Event。

当前采用 SphereCastNonAlloc + OverlapSphereNonAlloc，运行时动画副本生成真实 Animation Event，经 Relay 检查clip/token后开关判定。2026-10-02用户选抖动修复方案A：Animator使用AnimatePhysics，地面检测/转向/OnAnimatorMove统一物理节奏并保留刚体插值；每次OnAnimatorMove计算根位移后按目标姿态采样合法区间，Normal模式保留LateUpdate回退。Run朝向补偿只在新动画姿态求值后执行一次。位移配置是水平根位移倍率，不是精确米数；窗口为秒数且左闭右开。源FBX和原控制器不写入新事件。攻击中重载排队，安全移动状态整体应用深复制快照。

设计与实施依据：连招编辑器设计方案.md、连招编辑器阶段计划.md；审计证据：项目核对与分步整改建议.md；旧方案位于文档归档。

## 6. AI 高效迭代工作流

1. 写/改 C# → 2. `compile_check.bat`（MSBuild 拿编译错误，不阻塞 Unity）→ 3. 全绿后由用户开引擎验收，或跑 `run_editmode_tests.bat` 回归 → 4. 用户反馈截图 / `Logs/` 与 `TestResults/` 日志 → 5. 继续迭代
- 编辑器代码（Assets/Editor）编进 `Assembly-CSharp-Editor`，同样可 MSBuild 检查
- 不要试图修改 dsh 配置或重启 DeepSeek Harness 服务器（会中断会话）

## 7. 明确不要做

- 不装/不用：Odin、NaughtyAttributes、Feel、Animancer、KCC、A* Pathfinding（付费或架空论文技术点；任务书指定 Rigidbody 方案与距离 FSM）
- 不改 Unity/团结引擎版本、不动 .meta、不删除 `Assets/Imports` 素材、不重命名现有脚本类名
