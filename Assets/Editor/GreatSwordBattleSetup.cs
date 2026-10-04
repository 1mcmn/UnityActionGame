using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>生成独立演示资源与场景副本；原导入包及当前未保存场景不覆盖。</summary>
public static class GreatSwordBattleSetup
{
    public const string Folder = "Assets/Game/Generated/GreatSwordBattle";
    public const string ScenePath = "Assets/Game/Scenes/GreatSword Battle Demo.unity";
    public const string PrefabPath = Folder + "/GreatSwordEnemy.prefab";
    private const string Pack = "Assets/Imports/Grruzam Powerful Sword Animation(Great Sword, Katana)";
    private const string SourcePrefab = Pack + "/Prefebs/Unity_Grruzam_BaseModeling_GreatSword.prefab";

    [MenuItem("Tools/大剑战斗/构建 Windows 演示")]
    public static void BuildWindows()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new InvalidOperationException("请先创建并保存大剑战斗演示场景。");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/GreatSwordBattle")); Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = Path.Combine(directory, "UnityActionGame.exe"), options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows 构建失败：" + report.summary.result);
        Debug.Log("[大剑战斗] Windows 构建完成：" + directory);
    }

    [MenuItem("Tools/大剑战斗/创建演示场景副本")]
    public static void CreateSceneCopy()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var active = SceneManager.GetActiveScene();
        if (!active.IsValid() || !active.isLoaded) throw new InvalidOperationException("请先打开主场景。");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("演示场景已存在；请打开它，使用“配置当前演示场景”。不会覆盖现有场景。");
        EnsureFolder(Folder); EnsureFolder("Assets/Game/Scenes");
        // 保存副本包含当前编辑结果；当前场景继续保持原有 dirty 状态。
        if (!EditorSceneManager.SaveScene(active, ScenePath, true)) throw new IOException("无法保存场景副本。");
        var copy = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try { ConfigureScene(copy); if (!EditorSceneManager.SaveScene(copy)) throw new IOException("演示接线保存失败。"); }
        finally { EditorSceneManager.CloseScene(copy, true); SceneManager.SetActiveScene(active); }
        AssetDatabase.SaveAssets();
        Debug.Log("[大剑战斗] 已保存 " + ScenePath + "；原场景未自动保存。双击新场景进入演示。");
    }

    [MenuItem("Tools/大剑战斗/配置当前演示场景")]
    public static void ConfigureCurrent()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        ConfigureScene(SceneManager.GetActiveScene());
        Debug.Log("[大剑战斗] 当前场景接线完成，请保存场景。已有配置数值保留。");
    }

    [MenuItem("Tools/大剑战斗/应用压制期调整（浮空+倒地，体型1.1）")]
    public static void ApplySuppressionUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var scene = SceneManager.GetActiveScene();
        if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改；保留现场，先保存后再应用。");
        AddAirHitClip();
        ExpandAirStrike();
        var downHit = CleanClip("DownHit", "KnockDown_Front_Damage");
        foreach (var path in AssetDatabase.FindAssets("t:EnemyConfig").Select(AssetDatabase.GUIDToAssetPath))
        {
            var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
            if (config == null || !config.greatSwordEnabled || config.downHitClip != null) continue;
            Undo.RecordObject(config, "补充倒地受击动画"); config.downHitClip = downHit; EditorUtility.SetDirty(config);
        }
        AssetDatabase.SaveAssets();
        // 攻击判定按 1 倍体型标定；把放大的敌人整体缩回 1.1 倍，视觉与判定一致。只改最上层的非 1 缩放节点。
        int scaled = 0;
        foreach (var brain in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GreatSwordEnemyBrain>(true)))
        {
            float current = brain.transform.lossyScale.y;
            if (Mathf.Abs(current - EnemyBodyScale) < .01f) continue;
            Transform node = null;
            for (var t = brain.transform; t != null; t = t.parent) if (t.localScale != Vector3.one) node = t;
            if (node == null) node = brain.transform;
            Undo.RecordObject(node, "调整敌人体型");
            node.localScale *= EnemyBodyScale / current;
            PrefabUtility.RecordPrefabInstancePropertyModifications(node); scaled++;
        }
        if (scaled > 0) { EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。"); }
        Debug.Log($"[大剑战斗] 压制期调整完成：浮空受击/倒地受击动画已写入配置，空中三连已检查，{scaled} 个敌人缩放为 {EnemyBodyScale} 倍并保存 {scene.path}。");
    }
    private const float EnemyBodyScale = 1.1f;

    [MenuItem("Tools/大剑战斗/扩展空中三连")]
    public static void ExpandAirStrike()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var settings = AssetDatabase.LoadAssetAtPath<AirChainSettings>(Folder + "/AirChainSettings.asset");
        var data = settings != null ? settings.airStrikeCombo : null;
        if (data == null) throw new InvalidOperationException("缺少空中追击资产，请先执行 Tools/大剑战斗/配置当前演示场景。");
        if (data.steps.Count >= 3) { Selection.activeObject = data; Debug.Log("[大剑战斗] 空中连斩已有 " + data.steps.Count + " 段，保留现有配置。"); return; }
        Undo.RecordObject(data, "扩展空中三连");
        // 原单段（Combo_2）保留为第 2 段，只补接段窗口；前后补 Combo_1 与 Combo_3，最后一段为砸地终结。
        var middle = data.steps[0];
        OpenComboWindow(middle);
        middle.displayName = "空中连斩 2";
        var first = AirStep("空中连斩 1", "Jump_Attack_Combo_1_ZeroHeight", .25f, .55f, 18, 1.6f);
        var last = AirStep("空中砸地", "Jump_Attack_Combo_3_ZeroHeight", .35f, .75f, 34, 1.5f);
        OpenComboWindow(first);
        data.steps.Clear(); data.steps.Add(first); data.steps.Add(middle); data.steps.Add(last);
        data.displayName = "空中连斩"; data.revision++;
        var errors = data.Validate();
        EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); Selection.activeObject = data;
        if (errors.Count > 0) Debug.LogWarning("[大剑战斗] 空中三连已保存，但需在连招编辑器中调整：" + string.Join("；", errors), data);
        else Debug.Log("[大剑战斗] 空中三连已保存（版本 " + data.revision + "）。判定时间为按片段比例的初值，请在连招编辑器中按实际挥砍微调后 F5 重载。", data);
    }
    // 判定与接段窗口按片段时长比例给初值；具体帧由连招编辑器调整。
    private static ComboStep AirStep(string display, string clipName, float hitStart, float hitEnd, float damage, float speed)
    {
        var clip = SourceClip(clipName); float length = clip.length;
        return new ComboStep { displayName = display, animationClip = clip, hitStart = length * hitStart, hitEnd = length * hitEnd, playbackSpeed = speed,
            damage = damage, hitRadius = .7f, hitOffset = new Vector3(0, 1, 1.05f), rootMotionScaleXZ = .15f, allowCancel = false,
            comboStart = length * Mathf.Min(hitEnd, .9f), comboEnd = length,
            soundEvents = new List<ComboSoundEvent> { new ComboSoundEvent { time = Mathf.Max(0, length * hitStart - .08f), soundId = "atk01_swing" } } };
    }
    private static void OpenComboWindow(ComboStep step)
    {
        float length = step.animationClip != null ? step.animationClip.length : 1f;
        step.comboStart = Mathf.Min(step.hitStart + .05f, length * .9f); step.comboEnd = length;
    }

    [MenuItem("Tools/大剑战斗/补充浮空受击动画")]
    public static void AddAirHitClip()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var clip = CleanClip("AirHit", "Damage_Front_Flying_ver_B_ZeroHeight");
        int count = 0;
        foreach (var path in AssetDatabase.FindAssets("t:EnemyConfig").Select(AssetDatabase.GUIDToAssetPath))
        {
            var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
            if (config == null || !config.greatSwordEnabled || config.airHitClip != null) continue;
            Undo.RecordObject(config, "补充浮空受击动画"); config.airHitClip = clip; EditorUtility.SetDirty(config); count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[大剑战斗] 已生成 {Folder}/AirHit.anim，写入 {count} 份大剑敌人配置；已有设置保留。");
    }

    [MenuItem("Tools/大剑战斗/补接当前玩家浮空链")]
    public static void RepairCurrentAirWiring()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var scene = SceneManager.GetActiveScene();
        var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerCombat>(true)).Single();
        var enemy = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GreatSwordEnemyBrain>(true))
            .OrderBy(e => Vector3.Distance(e.transform.position, player.transform.position)).FirstOrDefault();
        if (enemy == null) throw new InvalidOperationException("当前场景需先有大剑敌人。此入口只补玩家，不替换敌人或场景布局。");
        var animator = player.GetComponentInChildren<Animator>(true);
        var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        if (controller == null || !AssetDatabase.GetAssetPath(controller).StartsWith("Assets/Game/Generated/", StringComparison.Ordinal))
            throw new InvalidOperationException("玩家需绑定Game/Generated下的普通演示控制器，避免修改导入包控制器。");
        var settings = AirSettings();
        var issues = settings.Validate();
        if (settings.launcherCombo == null || settings.airStrikeCombo == null) issues.Add("缺少挑飞/空斩资产。");
        else
        {
            issues.AddRange(settings.launcherCombo.Validate()); issues.AddRange(settings.airStrikeCombo.Validate());
            if (settings.launcherCombo.steps.Count != 1 || settings.airStrikeCombo.steps.Count < 1 || settings.airStrikeCombo.steps.Count > PlayerAirCombat.MaxStrikeSteps)
                issues.Add("挑飞需一段，空中连斩需 1～" + PlayerAirCombat.MaxStrikeSteps + " 段。");
        }
        if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
        EnsurePlayerAirSlots(controller);
        var air = GetOrAdd<PlayerAirCombat>(player.gameObject);
        var feedback = GetOrAdd<CombatFeedback>(player.gameObject);
        Undo.RecordObjects(new UnityEngine.Object[] { air, feedback }, "补接当前玩家浮空链");
        air.settings = settings;
        air.launcherPlaceholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/AirLaunchPlaceholder.anim");
        air.strikePlaceholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/AirStrikePlaceholder.anim");
        if (feedback.effectMaterial == null) feedback.effectMaterial = EffectMaterial();
        if (feedback.weaponTip == null) feedback.weaponTip = WeaponTip(player.gameObject);
        air.feedback = feedback;
        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).SingleOrDefault();
        if (manager != null)
        {
            var hud = GetOrAdd<CombatCycleHUD>(manager.gameObject);
            Undo.RecordObject(hud, "补接浮空提示"); hud.player = air; hud.enemy = enemy;
            EditorUtility.SetDirty(hud); PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        }
        foreach (var component in new UnityEngine.Object[] { air, feedback })
        { EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
        AssetDatabase.SaveAssetIfDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[大剑战斗] 已补接当前玩家Q/E组件与动画槽，保留原控制器及普通连招。请保存场景后重新进入Play。", player);
    }

    public static void ConfigureScene(Scene scene)
    {
        var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerCombat>(true)).Single();
        var animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null || !(animator.runtimeAnimatorController is AnimatorController))
            throw new InvalidOperationException("编辑模式玩家需绑定普通 AnimatorController。");
        EnsureFolder(Folder);
        var material = EffectMaterial();
        var config = EnemySettings();
        var errors = config.ValidateGreatSword();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        var prefab = EnemyPrefab(config, material);
        var airSettings = AirSettings();
        var controller = PlayerController((AnimatorController)animator.runtimeAnimatorController);
        Undo.RecordObject(animator, "配置大剑战斗玩家动画"); animator.runtimeAnimatorController = controller;
        var air = GetOrAdd<PlayerAirCombat>(player.gameObject);
        air.settings = airSettings;
        air.launcherPlaceholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/AirLaunchPlaceholder.anim");
        air.strikePlaceholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/AirStrikePlaceholder.anim");
        var playerFeedback = GetOrAdd<CombatFeedback>(player.gameObject); playerFeedback.effectMaterial = material;
        playerFeedback.weaponTip = WeaponTip(player.gameObject); air.feedback = playerFeedback;
        Assign(player, "comboData", GroundCombo());

        var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).SingleOrDefault();
        if (manager == null) throw new InvalidOperationException("场景缺少 GameManager。");
        var so = new SerializedObject(manager);
        var previous = so.FindProperty("_enemiesContainer").objectReferenceValue as Transform;
        Transform container = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "GreatSwordEnemies")?.transform;
        if (container == null) { var root = new GameObject("GreatSwordEnemies"); SceneManager.MoveGameObjectToScene(root, scene); Undo.RegisterCreatedObjectUndo(root, "创建大剑敌人容器"); container = root.transform; }
        if (previous != null && previous != container) previous.gameObject.SetActive(false);
        var enemy = container.GetComponentInChildren<GreatSwordEnemyBrain>(true);
        if (enemy == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); instance.transform.SetParent(container, true);
            instance.transform.position = player.transform.position + new Vector3(0, 0, 3.5f);
            float floor; if (CombatPhysics.Ground(instance.transform.position, instance.transform, out floor)) instance.transform.position = new Vector3(instance.transform.position.x, floor + .02f, instance.transform.position.z);
            Vector3 facing = player.transform.position - instance.transform.position; facing.y = 0;
            if (facing.sqrMagnitude > .001f) instance.transform.rotation = Quaternion.LookRotation(facing);
            Undo.RegisterCreatedObjectUndo(instance, "创建大剑敌人"); enemy = instance.GetComponent<GreatSwordEnemyBrain>();
        }
        so.FindProperty("_enemiesContainer").objectReferenceValue = container; so.ApplyModifiedPropertiesWithoutUndo();
        var hud = GetOrAdd<CombatCycleHUD>(manager.gameObject); hud.player = air; hud.enemy = enemy;
        EditorUtility.SetDirty(player); EditorUtility.SetDirty(air); EditorUtility.SetDirty(animator); EditorUtility.SetDirty(playerFeedback); EditorUtility.SetDirty(manager); EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
    }

    private static EnemyConfig EnemySettings()
    {
        string path = Folder + "/GreatSwordEnemyConfig.asset";
        var data = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
        if (data != null) return data;
        data = ScriptableObject.CreateInstance<EnemyConfig>(); data.greatSwordEnabled = true;
        data.maxHealth = 260; data.maxPoise = 65; data.poiseDecayRate = 1.5f; data.damageReduction = .1f;
        data.invulnerabilityDuration = .06f; data.knockbackForce = 3; data.deathDelay = 2.5f;
        data.detectRadius = 12; data.closeRadius = 5; data.attackRadius = 1.75f;
        data.walkSpeed = 1.8f; data.runSpeed = 3.5f; data.guardDuration = 4.8f; data.breakDuration = 3.2f;
        data.idleClip = CleanClip("Idle", "Idle", true);
        data.walkClip = CleanClip("Walk", "Jogging_8Way_verA_F", true); data.runClip = CleanClip("Run", "Jogging_8Way_verA_F", true);
        data.guardStartClip = CleanClip("GuardStart", "Revenge_Guard_Start");
        data.guardLoopClip = CleanClip("GuardLoop", "Revenge_Guard_Loop", true);
        data.guardHitClip = CleanClip("GuardHit", "Revenge_Guard_Accept");
        data.hitClip = CleanClip("Hit", "Damage_Front_Small_ver_A");
        data.downStartClip = CleanClip("DownStart", "Damage_Front_High_KnockDown_ZeroHeight");
        data.downLoopClip = CleanClip("DownLoop", "Damage_Front_Down_Loop", true);
        data.getUpClip = CleanClip("GetUp", "Damage_Front_Down_StandUp");
        data.deathClip = CleanClip("Death", "Damage_Die");
        data.airborneClip = CleanClip("Airborne", "Damage_Front_Flying_ver_A_ZeroHeight");
        data.attackPlaceholderA = CleanClip("EnemyPlaceholderA", "Attack_3Combo_1");
        data.attackPlaceholderB = CleanClip("EnemyPlaceholderB", "Attack_3Combo_2");
        data.lightAttack1 = Attack("Attack_3Combo_1", .4f, .72f, 12, false);
        data.lightAttack2 = Attack("Attack_3Combo_2", .22f, .48f, 14, false);
        data.heavyAttack = Attack("Attack_4Combo_4", .65f, .95f, 20, true);
        data.counterAttack = Attack("Revenge_Guard_Attack", .55f, .82f, 16, true);
        AssetDatabase.CreateAsset(data, path); return data;
    }
    private static EnemyAttackDefinition Attack(string name, float start, float end, float damage, bool strong)
    {
        return new EnemyAttackDefinition { clip = SourceClip(name), hitStart = start, hitEnd = end, damage = damage,
            speed = 1.15f, rootMotionScale = .35f, lockFacingTime = start - .1f, parryable = strong, superArmor = strong,
            parryStart = .06f, parryEnd = start + .06f, hitRadius = .5f, hitOffset = new Vector3(0, .9f, 1.1f) };
    }
    private static GameObject EnemyPrefab(EnemyConfig data, Material effects)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); if (existing != null) return existing;
        string controllerPath = Folder + "/GreatSwordEnemy.controller";
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("EnemySpeedA", AnimatorControllerParameterType.Float); controller.AddParameter("EnemySpeedB", AnimatorControllerParameterType.Float);
        AddState(controller, "Idle", data.idleClip); AddState(controller, "Walk", data.walkClip, null, .7f); AddState(controller, "Run", data.runClip);
        AddState(controller, "GuardStart", data.guardStartClip); AddState(controller, "GuardLoop", data.guardLoopClip); AddState(controller, "GuardHit", data.guardHitClip);
        AddState(controller, "Hit", data.hitClip); AddState(controller, "DownStart", data.downStartClip); AddState(controller, "DownLoop", data.downLoopClip);
        AddState(controller, "GetUp", data.getUpClip); AddState(controller, "Death", data.deathClip); AddState(controller, "Airborne", data.airborneClip);
        AddState(controller, "EnemySlotA", data.attackPlaceholderA, "EnemySpeedA"); AddState(controller, "EnemySlotB", data.attackPlaceholderB, "EnemySpeedB");
        var root = new GameObject("GreatSwordEnemy"); root.SetActive(false);
        try
        {
            var layer = LayerMask.NameToLayer("Enemy"); if (layer < 0) layer = LayerMask.NameToLayer("enemy"); if (layer < 0) throw new InvalidOperationException("缺少 Enemy 层，请核对玩家敌人 LayerMask。");
            root.layer = layer;
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeRotation; body.interpolation = RigidbodyInterpolation.Interpolate;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.center = new Vector3(0, .9f, 0); capsule.height = 1.8f; capsule.radius = .28f;
            var enemy = root.AddComponent<Enemy>(); Assign(enemy, "_config", data);
            var facade = root.AddComponent<EnemyAI>(); Assign(facade, "_config", data);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab));
            visual.name = "Visual"; visual.transform.SetParent(root.transform, false); visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity;
            ConvertMaterials(visual);
            var animator = visual.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = true; animator.updateMode = AnimatorUpdateMode.AnimatePhysics; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var hurt = new GameObject("Hurtbox"); hurt.transform.SetParent(root.transform, false); hurt.layer = layer;
            var hurtCollider = hurt.AddComponent<CapsuleCollider>(); hurtCollider.isTrigger = true; hurtCollider.radius = .28f; hurtCollider.height = 1.65f; hurtCollider.center = new Vector3(0, .9f, 0); hurt.AddComponent<EnemyHurtbox>();
            var feedback = root.AddComponent<CombatFeedback>(); feedback.effectMaterial = effects; feedback.weaponTip = WeaponTip(visual); feedback.trailColor = new Color(1f, .25f, .1f);
            var brain = root.AddComponent<GreatSwordEnemyBrain>(); brain.config = data; brain.animator = animator; brain.feedback = feedback;
            animator.gameObject.AddComponent<EnemyAnimationRelay>();
            root.SetActive(true);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static AirChainSettings AirSettings()
    {
        string path = Folder + "/AirChainSettings.asset"; var existing = AssetDatabase.LoadAssetAtPath<AirChainSettings>(path); if (existing != null) return existing;
        var data = ScriptableObject.CreateInstance<AirChainSettings>();
        data.launcherCombo = OneStep("LauncherCombo", "破防挑飞", "UpperAttack_ZeroHeight", .78f, 1.05f, 22, 1.8f);
        data.airStrikeCombo = OneStep("AirStrikeCombo", "空中追击", "Jump_Attack_Combo_2_ZeroHeight", .32f, .7f, 28, 1.7f);
        AssetDatabase.CreateAsset(data, path); return data;
    }
    private static ComboData OneStep(string asset, string display, string clipName, float start, float end, float damage, float speed)
    {
        string path = Folder + "/" + asset + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<ComboData>(path); if (existing != null) return existing;
        var data = ScriptableObject.CreateInstance<ComboData>(); data.displayName = display; data.revision = 1;
        data.steps.Add(new ComboStep { displayName = display, animationClip = SourceClip(clipName), hitStart = start, hitEnd = end, playbackSpeed = speed,
            damage = damage, hitRadius = .65f, hitOffset = new Vector3(0, 1, 1.05f), rootMotionScaleXZ = .15f,
            allowCancel = false, soundEvents = new List<ComboSoundEvent> { new ComboSoundEvent { time = start - .08f, soundId = "atk01_swing" } } });
        AssetDatabase.CreateAsset(data, path); return data;
    }
    private static ComboData GroundCombo()
    {
        string path = Folder + "/PlayerGroundCombo.asset"; var existing = AssetDatabase.LoadAssetAtPath<ComboData>(path); if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<ComboData>(ComboDemoSetup.DataPath); if (source == null) throw new InvalidOperationException("缺少原普通连招资产。");
        var data = UnityEngine.Object.Instantiate(source); data.name = "PlayerGroundCombo"; data.displayName = "大剑战斗普通三段";
        // 修正第一段提前跳段和第二段挥砍音晚于命中；仅修改演示副本。
        if (data.steps.Count > 1)
        {
            var first = data.steps[0]; first.comboStart = Mathf.Max(first.comboStart, first.hitStart + .1f);
            first.comboEnd = Mathf.Max(first.comboEnd, Mathf.Min(first.animationClip.length, first.comboStart + .2f));
            foreach (var sound in data.steps[1].soundEvents) if (sound.soundId.Contains("swing")) sound.time = Mathf.Max(0, data.steps[1].hitStart - .05f);
        }
        data.revision++; AssetDatabase.CreateAsset(data, path); return data;
    }
    private static AnimatorController PlayerController(AnimatorController source)
    {
        string path = Folder + "/PlayerBattle.controller"; var result = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (result == null) { if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new IOException("玩家控制器复制失败。"); result = AssetDatabase.LoadAssetAtPath<AnimatorController>(path); }
        EnsurePlayerAirSlots(result);
        ComboDemoSetup.PrepareActionPlayback(result);
        EditorUtility.SetDirty(result); return result;
    }
    private static void EnsurePlayerAirSlots(AnimatorController controller)
    {
        foreach (var name in new[] { "AirLaunchSpeed", "AirStrikeSpeed" })
        {
            var parameter = controller.parameters.FirstOrDefault(p => p.name == name);
            if (parameter != null && parameter.type != AnimatorControllerParameterType.Float)
                throw new InvalidOperationException("浮空倍速参数必须为Float：" + name);
            if (parameter == null) controller.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        }
        AddState(controller, "AirLaunch", CleanClip("AirLaunchPlaceholder", "UpperAttack_ZeroHeight"), "AirLaunchSpeed");
        AddState(controller, "AirStrike", CleanClip("AirStrikePlaceholder", "Jump_Attack_Combo_2_ZeroHeight"), "AirStrikeSpeed");
        EditorUtility.SetDirty(controller);
    }
    private static void AddState(AnimatorController controller, string name, AnimationClip clip, string speedParameter = null, float speed = 1)
    {
        var sm = controller.layers[0].stateMachine; var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? sm.AddState(name);
        state.motion = clip; state.speed = speed;
        if (speedParameter != null) { state.speedParameter = speedParameter; state.speedParameterActive = true; }
        if (name == "Idle") sm.defaultState = state;
    }
    private static AnimationClip SourceClip(string name)
    {
        string file = "M_Big_Sword@" + name;
        string path = AssetDatabase.FindAssets("t:AnimationClip", new[] { Pack + "/Animation/M_Big_Sword" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == file);
        var clip = path == null ? null : AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null || !clip.isHumanMotion) throw new InvalidOperationException("缺少 Humanoid 片段：" + file);
        return clip;
    }
    private static AnimationClip CleanClip(string asset, string source, bool loop = false)
    {
        string path = Folder + "/" + asset + ".anim"; var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); if (result != null) return result;
        result = UnityEngine.Object.Instantiate(SourceClip(source)); result.name = asset; result.events = Array.Empty<AnimationEvent>(); result.hideFlags = HideFlags.None;
        var settings = AnimationUtility.GetAnimationClipSettings(result); settings.loopTime = loop; AnimationUtility.SetAnimationClipSettings(result, settings);
        AssetDatabase.CreateAsset(result, path); return result;
    }
    private static Material EffectMaterial()
    {
        string path = Folder + "/CombatEffects.mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path); if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (shader == null) throw new InvalidOperationException("缺少 URP 粒子着色器。");
        mat = new Material(shader) { name = "CombatEffects" }; mat.SetFloat("_Surface", 1); mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000; AssetDatabase.CreateAsset(mat, path); return mat;
    }
    private static void ConvertMaterials(GameObject visual)
    {
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                if (source == null) return null;
                string path = Folder + "/" + source.name + "_SimpleLit.mat"; var result = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (result == null) { result = new Material(source) { shader = Shader.Find("Universal Render Pipeline/Simple Lit"), name = source.name + "_SimpleLit" }; AssetDatabase.CreateAsset(result, path); }
                return result;
            }).ToArray();
        }
    }
    private static Transform WeaponTip(GameObject root)
    {
        var existing = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "CombatWeaponTip"); if (existing != null) return existing;
        var weapon = root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null &&
            (m.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("blade", StringComparison.OrdinalIgnoreCase) >= 0)).OrderByDescending(m => m.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();
        if (weapon == null) return null;
        Bounds b = weapon.sharedMesh.bounds; Vector3 direction = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
        var tip = new GameObject("CombatWeaponTip"); tip.transform.SetParent(weapon.transform, false);
        float half = Vector3.Dot(b.extents, direction); Vector3 a = b.center + direction * half, c = b.center - direction * half;
        tip.transform.localPosition = a.sqrMagnitude >= c.sqrMagnitude ? a : c; return tip.transform;
    }
    private static T GetOrAdd<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
    private static void Assign(UnityEngine.Object target, string property, UnityEngine.Object value)
    { var so = new SerializedObject(target); so.FindProperty(property).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int index = path.LastIndexOf('/'); string parent = path.Substring(0, index); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(index + 1));
    }
}
