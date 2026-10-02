using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>显式演示配置入口。配置生成的控制器与动作副本，保留源FBX和已编辑的连招数值。</summary>
public static class ComboDemoSetup
{
    public const string ScenePath = "Assets/Game/Scenes/DEMO City Crossing.unity";
    public const string OutputFolder = "Assets/Game/Generated/ComboDemo";
    public const string ControllerPath = OutputFolder + "/ComboDemo.controller";
    public const string DataPath = OutputFolder + "/PlayerCombo.asset";
    private const string PlaceholderAPath = OutputFolder + "/ComboPlaceholderA.anim";
    private const string PlaceholderBPath = OutputFolder + "/ComboPlaceholderB.anim";
    private const string SoundLibraryPath = OutputFolder + "/ComboDemoSounds.asset";

    private sealed class StateEntry
    {
        public string path;
        public AnimatorState state;
    }

    [MenuItem("Tools/连招演示/配置当前场景")]
    public static void ConfigureCurrentScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[连招演示] 请先退出运行模式。");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (!ConfigureScene(scene, out string error))
        {
            Debug.LogError("[连招演示] 配置失败：" + error);
            return;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[连招演示] 当前场景已配置；请保存场景后运行。连招时间窗口为初始值，仍需现场检查动作时序。");
        ComboEditorWindow.OpenAsset(AssetDatabase.LoadAssetAtPath<ComboData>(DataPath));
    }

    public static void ConfigureDemoBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!ConfigureScene(scene, out string error)) throw new InvalidOperationException("连招演示配置失败：" + error);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("连招演示场景保存失败。");
        AssetDatabase.SaveAssets();
        SyncProjectFiles();
        Debug.Log("[连招演示] 批处理配置与保存完成：" + ScenePath);
    }

    public static bool ConfigureScene(Scene scene, out string error)
    {
        error = null;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("配置只能在编辑模式执行。");
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("请先打开并保存需要配置的场景。");

            PlayerCombat player = FindPlayer(scene);
            if (player == null) throw new InvalidOperationException("场景内未找到唯一主玩家 PlayerCombat；请在 Hierarchy 明确选择一个玩家。");
            PlayerAnimController motion = player.GetComponent<PlayerAnimController>();
            if (motion == null) throw new InvalidOperationException("主玩家缺少 PlayerAnimController。");
            Animator animator = ResolveAnimator(player, motion);
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("主玩家没有已绑定 Controller 的 Animator。");

            var sourceController = animator.runtimeAnimatorController as AnimatorController;
            if (sourceController == null)
                throw new InvalidOperationException("编辑模式玩家使用的是 AnimatorOverrideController；请明确指定其基础 AnimatorController 后再配置，避免丢失已有覆盖。");
            if (sourceController.layers.Length == 0 || sourceController.layers[0].name != "Base Layer")
                throw new InvalidOperationException("运行接口要求第 0 层名为 Base Layer；当前控制器不满足，请先核对动画机。");

            AnimationClip[] attacks = FindAttackClips(sourceController);
            if (attacks.Any(c => c == null))
            {
                string names = string.Join("\n", sourceController.animationClips.Where(c => c != null).Select(c => c.name).Distinct());
                throw new InvalidOperationException("实际玩家控制器中未找到三段攻击状态或明确的 attack1/at2/attack3 片段，未猜测或替换素材。实际片段：\n" + names);
            }
            foreach (AnimationClip clip in attacks)
            {
                if (clip.length <= 0 || clip.legacy || clip.isLooping)
                    throw new InvalidOperationException("攻击片段不满足非循环、非 Legacy 且有时长：" + clip.name + "。请检查导入；本工具不会修改 FBX 或 .meta。");
                if (animator.isHuman && !clip.humanMotion)
                    throw new InvalidOperationException("Humanoid 玩家使用的攻击片段不是 Humanoid：" + clip.name);
            }

            EnsureFolder(OutputFolder);
            AnimationClip placeholderA = LoadOrCreatePlaceholder(PlaceholderAPath, attacks[0], "ComboPlaceholderA");
            AnimationClip placeholderB = LoadOrCreatePlaceholder(PlaceholderBPath, attacks[1], "ComboPlaceholderB");
            AnimationClip jump = FindJumpClip(animator.isHuman);
            AnimatorController controller = PrepareController(sourceController, placeholderA, placeholderB, jump);
            EnsureSceneAudio(scene);
            ComboData data = LoadOrCreateCombo(attacks, scene);
            AddMissingDemoSwingEvents(data, GetSceneSoundIds(scene));
            List<string> issues = data.Validate();
            if (data.steps.Count < 3) issues.Add("演示验收资产至少需要三段。");
            if (issues.Count != 0) throw new InvalidOperationException("演示资产校验失败，未覆盖已有内容：\n" + string.Join("\n", issues));

            List<StateEntry> states = CollectStates(controller);
            string locomotion = FindState(states, "Locomotion", "Idle", "Movement");
            if (string.IsNullOrEmpty(locomotion)) locomotion = FindDefaultStatePath(controller.layers[0].stateMachine, "Base Layer");
            if (string.IsNullOrEmpty(locomotion)) throw new InvalidOperationException("控制器中未找到可用的待机/移动状态。");

            Undo.RecordObjects(new UnityEngine.Object[] { player, player.gameObject, motion, animator }, "配置连招演示玩家");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = true;
            motion.comboPlaceholderA = placeholderA;
            motion.comboPlaceholderB = placeholderB;
            motion.idleStateName = FindState(states, "Idle");
            motion.locomotionStateName = locomotion;
            motion.dodgeStateName = FindState(states, "Dodge", "Evade", "Evade_Front", "Dodge_Front");
            motion.parryStateName = FindState(states, "Parry", "Parry_Start", "Block_Start");
            motion.hitFrontStateName = FindState(states, "HitFront", "HitForward", "Hit_Front", "Hit_Front_Light", "Hit_Front_01", "BeHit_Front");
            motion.hitLeftStateName = FindState(states, "HitLeft", "Hit_Left", "BeHit_Left");
            motion.hitRightStateName = FindState(states, "HitRight", "Hit_Right", "BeHit_Right");
            motion.deathStateName = FindState(states, "Death", "Death_Hit_Front", "Die");
            motion.blockStateName = FindState(states, "Block", "Blocking", "Block_Loop");
            motion.knockdownStateName = FindState(states, "Knockdown", "KnockDown", "Knockdown_Front", "Hit_Fly");
            if (string.IsNullOrEmpty(motion.hitLeftStateName)) motion.hitLeftStateName = motion.hitFrontStateName;
            if (string.IsNullOrEmpty(motion.hitRightStateName)) motion.hitRightStateName = motion.hitFrontStateName;
            player.AssignComboData(data);
            player.gameObject.tag = "Player";
            var serializedMotion = new SerializedObject(motion);
            SerializedProperty animatorField = serializedMotion.FindProperty("animator");
            if (animatorField != null) { animatorField.objectReferenceValue = animator; serializedMotion.ApplyModifiedProperties(); }

            AddComponentByName(player.gameObject, "ComboHUD");
            AddComponentByName(player.gameObject, "DemoPerformanceRecorder");
            EnsureBuildScene(scene.path);
            foreach (UnityEngine.Object obj in new UnityEngine.Object[] { player, player.gameObject, motion, animator })
            {
                EditorUtility.SetDirty(obj);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            string stateLog = string.Join("\n", states.Select(s => s.path + " → " + (s.state.motion != null ? s.state.motion.name : "无 Motion")));
            Debug.Log("[连招演示] 已配置玩家：" + player.name + "\n原控制器：" + AssetDatabase.GetAssetPath(sourceController) +
                      "\n演示控制器：" + ControllerPath + "\n移动：" + locomotion + "\n闪避：" + motion.dodgeStateName +
                      "\n受击：" + motion.hitFrontStateName + "\n死亡：" + motion.deathStateName + "\n实际状态列表：\n" + stateLog, player);
            if (string.IsNullOrEmpty(motion.dodgeStateName) || string.IsNullOrEmpty(motion.hitFrontStateName) || string.IsNullOrEmpty(motion.deathStateName))
                Debug.LogWarning("[连招演示] 闪避/受击/死亡映射有缺项，需按上方真实状态表检查；不能据此认定对应动画验收通过。", player);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    private static PlayerCombat FindPlayer(Scene scene)
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null && selected.scene == scene)
        {
            PlayerCombat choice = selected.GetComponentInParent<PlayerCombat>();
            if (choice != null) return choice;
        }
        var players = new List<PlayerCombat>();
        foreach (GameObject root in scene.GetRootGameObjects()) players.AddRange(root.GetComponentsInChildren<PlayerCombat>(true));
        var tagged = players.Where(p => p.CompareTag("Player")).ToList();
        if (tagged.Count == 1) return tagged[0];
        var active = tagged.Where(p => p.gameObject.activeInHierarchy).ToList();
        if (active.Count == 1) return active[0];
        return players.Count == 1 ? players[0] : null;
    }

    private static Animator ResolveAnimator(PlayerCombat player, PlayerAnimController motion)
    {
        var serialized = new SerializedObject(motion);
        SerializedProperty prop = serialized.FindProperty("animator");
        Animator animator = prop != null ? prop.objectReferenceValue as Animator : null;
        return animator != null ? animator : player.GetComponentInChildren<Animator>(true);
    }

    private static AnimationClip[] FindAttackClips(AnimatorController controller)
    {
        var result = new AnimationClip[3];
        List<StateEntry> states = CollectStates(controller);
        // 状态名与 FBX 子片段名不同：从真实状态的 Motion 读取，避免依赖模型长文件名。
        string[][] stateNames =
        {
            new[] { "Avatar_Female_Size02_Anbi_Ani_Attack_Normal_01", "Attack_Normal_01" },
            new[] { "Avatar_Female_Size02_Anbi_Ani_Attack_Normal_02", "Attack_Normal_02", "AT1", "at2" },
            new[] { "Avatar_Female_Size02_Anbi_Ani_Attack_Normal_03", "Attack_Normal_03", "at3" }
        };
        string[] clipAliases = { "attack1", "at2", "attack3" };
        for (int i = 0; i < result.Length; i++)
        {
            StateEntry sourceState = null;
            foreach (string stateName in stateNames[i])
            {
                sourceState = states.FirstOrDefault(s => s.path.StartsWith("Base Layer.", StringComparison.Ordinal) &&
                    s.state.motion is AnimationClip && string.Equals(s.state.name, stateName, StringComparison.OrdinalIgnoreCase));
                if (sourceState != null) break;
            }
            result[i] = sourceState != null ? sourceState.state.motion as AnimationClip :
                controller.animationClips.FirstOrDefault(c => c != null && string.Equals(c.name, clipAliases[i], StringComparison.OrdinalIgnoreCase));
            AnimationClip clip = result[i];
            if (clip != null)
                Debug.Log($"[连招演示] 第 {i + 1} 段来源：状态={sourceState?.path ?? "按明确片段别名匹配"}；" +
                          $"资产={AssetDatabase.GetAssetPath(clip)}；片段={clip.name}；时长={clip.length:0.###} 秒；Loop={clip.isLooping}；Humanoid={clip.humanMotion}", clip);
        }
        return result;
    }

    private static AnimationClip LoadOrCreatePlaceholder(string path, AnimationClip source, string name)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("占位动画路径被其他资产占用：" + path);
        AnimationClip clip = UnityEngine.Object.Instantiate(source);
        clip.name = name;
        clip.events = Array.Empty<AnimationEvent>();
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static AnimatorController PrepareController(AnimatorController source, AnimationClip a, AnimationClip b, AnimationClip jump)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null)
        {
            ValidateSlots(existing, a, b, false);
            EnsureComboSpeedParameters(existing);
            ValidateSlots(existing, a, b);
            PrepareActionPlayback(existing);
            return existing;
        }
        if (AssetDatabase.LoadMainAssetAtPath(ControllerPath) != null) throw new InvalidOperationException("演示控制器路径被其他资产占用。");
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, ControllerPath))
            throw new IOException("无法复制原动画控制器：" + sourcePath);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AddSlot(machine, "ComboSlotA", a, new Vector3(450, 80));
        AddSlot(machine, "ComboSlotB", b, new Vector3(450, 160));
        EnsureComboSpeedParameters(controller);
        if (jump != null) AddSlot(machine, "ComboJump", jump, new Vector3(450, 240));
        PrepareActionPlayback(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        ValidateSlots(controller, a, b);
        return controller;
    }

    /// <summary>生成控制器的闪避由代码按动画末尾结束；源FBX保持原样。</summary>
    public static void PrepareActionPlayback(AnimatorController controller)
    {
        string controllerPath = AssetDatabase.GetAssetPath(controller);
        if (string.IsNullOrEmpty(controllerPath) || !controllerPath.StartsWith("Assets/Game/Generated/", StringComparison.Ordinal))
            throw new InvalidOperationException("动作退出修复仅支持Game/Generated下的演示控制器。");
        var states = CollectStates(controller);
        string dodgePath = FindState(states, "Dodge", "Evade", "Evade_Front", "Dodge_Front");
        if (string.IsNullOrEmpty(dodgePath)) return;
        var dodge = states.First(s => s.path == dodgePath).state;
        var source = dodge.motion as AnimationClip;
        if (source == null || source.length <= 0f || source.legacy || dodge.speed <= 0f)
            throw new InvalidOperationException("闪避需使用正向播放的有效AnimationClip：" + dodgePath);
        Undo.RecordObject(dodge, "修复闪避完整播放");
        if (source.isLooping)
        {
            string path = Path.GetDirectoryName(controllerPath).Replace('\\', '/') + "/DodgeOnce.anim";
            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (copy == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("闪避副本路径被其他资产占用：" + path);
                copy = UnityEngine.Object.Instantiate(source);
                copy.name = "DodgeOnce";
                copy.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(copy, path);
            }
            else if (copy != source)
            {
                Undo.RecordObject(copy, "更新生成的闪避副本");
                EditorUtility.CopySerialized(source, copy);
                copy.name = "DodgeOnce";
                copy.hideFlags = HideFlags.None;
            }
            copy.events = Array.Empty<AnimationEvent>();
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            dodge.motion = copy;
        }
        var idlePath = FindState(states, "Idle");
        var movePath = FindState(states, "Locomotion", "Movement");
        var idle = states.FirstOrDefault(s => s.path == idlePath)?.state;
        var move = states.FirstOrDefault(s => s.path == movePath)?.state;
        // 只移除普通收招出口，保留用户可能配置的受击/死亡等打断过渡。
        foreach (var transition in dodge.transitions.Where(t =>
            t.destinationState != null && (t.destinationState == idle || t.destinationState == move)).ToArray())
            dodge.RemoveTransition(transition);
        EditorUtility.SetDirty(dodge);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
    }

    private static void AddSlot(AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position)
    {
        if (machine.states.Any(s => s.state.name == name)) throw new InvalidOperationException("原控制器已有同名专用状态，未覆盖：" + name);
        AnimatorState state = machine.AddState(name, position);
        state.motion = clip;
        state.speed = 1f;
        state.speedParameterActive = false;
        state.writeDefaultValues = true;
    }

    private static void ValidateSlots(AnimatorController controller, AnimationClip a, AnimationClip b, bool requireSpeedParameters = true)
    {
        if (controller.layers.Length == 0 || controller.layers[0].name != "Base Layer") throw new InvalidOperationException("既有演示控制器缺少 Base Layer。");
        var states = controller.layers[0].stateMachine.states;
        string[] names = { "ComboSlotA", "ComboSlotB" };
        AnimationClip[] clips = { a, b };
        for (int i = 0; i < names.Length; i++)
        {
            AnimatorState state = states.Where(s => s.state.name == names[i]).Select(s => s.state).FirstOrDefault();
            string speedParameter = i == 0 ? PlayerCombat.ComboSpeedParameterA : PlayerCombat.ComboSpeedParameterB;
            if (state == null || state.motion != clips[i] || state.transitions.Length != 0 || state.speed != 1f ||
                (requireSpeedParameters && (!state.speedParameterActive || state.speedParameter != speedParameter)))
                throw new InvalidOperationException("既有演示控制器槽不满足运行约定：" + names[i] + "。未覆盖已有资产，请人工检查。");
        }
        List<StateEntry> all = CollectStates(controller);
        if (a == b || all.Count(s => s.state.motion == a) != 1 || all.Count(s => s.state.motion == b) != 1)
            throw new InvalidOperationException("两个占位动画必须各自唯一，不能被其他状态复用。");
    }

    // 只升级项目生成的专用槽，不改源 FBX 或原攻击状态。
    private static void EnsureComboSpeedParameters(AnimatorController controller)
    {
        var slots = controller.layers[0].stateMachine.states;
        string[] names = { "ComboSlotA", "ComboSlotB" };
        string[] parameters = { PlayerCombat.ComboSpeedParameterA, PlayerCombat.ComboSpeedParameterB };
        var states = new AnimatorState[2];
        for (int i = 0; i < names.Length; i++)
        {
            states[i] = slots.Where(s => s.state.name == names[i]).Select(s => s.state).FirstOrDefault();
            if (states[i] == null) throw new InvalidOperationException("缺少连招槽：" + names[i]);
            var parameter = controller.parameters.FirstOrDefault(p => p.name == parameters[i]);
            if (parameter != null && parameter.type != AnimatorControllerParameterType.Float)
                throw new InvalidOperationException("连招倍速参数不是Float：" + parameters[i]);
        }
        for (int i = 0; i < names.Length; i++)
        {
            if (!controller.parameters.Any(p => p.name == parameters[i]))
                controller.AddParameter(new AnimatorControllerParameter { name = parameters[i], type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            states[i].speedParameter = parameters[i];
            states[i].speedParameterActive = true;
            EditorUtility.SetDirty(states[i]);
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
    }

    private static AnimationClip FindJumpClip(bool requireHumanoid)
    {
        string[] paths = { "Assets/Imports/Pro Melee Axe Pack/standing jump.fbx", "Assets/Imports/Great Sword Pack (1)/great sword jump.fbx" };
        foreach (string path in paths)
            foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var clip = obj as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase) || clip.legacy || clip.length <= 0) continue;
                if (requireHumanoid && !clip.humanMotion) continue;
                Debug.Log("[连招演示] 跳跃动画：" + path + " / " + clip.name + "，Humanoid=" + clip.humanMotion);
                return clip;
            }
        Debug.LogWarning("[连招演示] 未找到匹配玩家骨架类型的跳跃片段。跳跃位移仍可运行，跳跃动画待人工配置。");
        return null;
    }

    private static ComboData LoadOrCreateCombo(AnimationClip[] clips, Scene scene)
    {
        ComboData existing = AssetDatabase.LoadAssetAtPath<ComboData>(DataPath);
        if (existing != null) return existing;
        if (AssetDatabase.LoadMainAssetAtPath(DataPath) != null) throw new InvalidOperationException("连招资产路径被其他类型资产占用。");
        var data = ScriptableObject.CreateInstance<ComboData>();
        data.displayName = "演示普通三段连招";
        data.schemaVersion = 1;
        data.revision = 1;
        data.steps = new List<ComboStep>();
        float[] damage = { 15f, 18f, 22f };
        var soundIds = GetSceneSoundIds(scene);
        for (int i = 0; i < 3; i++)
        {
            float length = clips[i].length;
            var step = new ComboStep
            {
                stepId = Guid.NewGuid().ToString("N"), displayName = "普通攻击 " + (i + 1), animationClip = clips[i],
                damage = damage[i], playbackSpeed = 2f, hitStart = length * 0.2f, hitEnd = length * 0.45f,
                comboStart = length * 0.6f, comboEnd = length * 0.9f, rootMotionScaleXZ = 1f,
                hitRadius = 0.75f, hitOffset = new Vector3(0f, 1f, 1.2f),
                allowCancel = true, cancelStart = length * 0.55f, cancelEnd = length * 0.95f,
                soundEvents = new List<ComboSoundEvent>()
            };
            string prefix = "atk0" + (i + 1) + "_swing";
            string id = soundIds.FirstOrDefault(s => s == prefix) ?? soundIds.FirstOrDefault(s => s.StartsWith(prefix + "_", StringComparison.Ordinal));
            if (id != null) step.soundEvents.Add(new ComboSoundEvent { time = length * 0.12f, soundId = id });
            else Debug.LogWarning("[连招演示] 场景音效库没有 " + prefix + "，第 " + (i + 1) + " 段暂不自动添加音效事件。");
            data.steps.Add(step);
        }
        AssetDatabase.CreateAsset(data, DataPath);
        AssetDatabase.SaveAssetIfDirty(data);
        return data;
    }

    private static List<string> GetSceneSoundIds(Scene scene)
    {
        var result = new List<string>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (SoundManager manager in root.GetComponentsInChildren<SoundManager>(true))
            {
                SerializedProperty libraries = new SerializedObject(manager).FindProperty("_libraries");
                if (libraries == null) continue;
                for (int i = 0; i < libraries.arraySize; i++)
                {
                    var lib = libraries.GetArrayElementAtIndex(i).objectReferenceValue as SoundLibrary;
                    if (lib == null || lib.sounds == null) continue;
                    foreach (var sound in lib.sounds)
                        if (sound != null && sound.clip != null && !string.IsNullOrEmpty(sound.soundID) && !result.Contains(sound.soundID)) result.Add(sound.soundID);
                }
            }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static void EnsureSceneAudio(Scene scene)
    {
        SoundLibrary library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
        if (library == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(SoundLibraryPath) != null)
                throw new InvalidOperationException("演示音效库路径被其他类型资产占用。");
            var sounds = new List<SoundItem>();
            for (int i = 1; i <= 5; i++)
            {
                AddSound(sounds, $"atk0{i}_swing_01", $"Assets/Imports/FOOT/安比攻击{i}.wav");
                AddSound(sounds, $"atk0{i}_hit_01", $"Assets/Imports/FOOT/安比攻击受击音{i}.wav");
            }
            for (int i = 1; i <= 8; i++) AddSound(sounds, $"foot_step_{i:00}", $"Assets/Imports/FOOT/脚步声{i}.wav");
            AddSound(sounds, "sword_hit_03", "Assets/Imports/audio/u_fe12rqkbth-sword-clash-241729.mp3");
            AddSound(sounds, "sword_sheath_01", "Assets/Imports/FOOT/收脚音1.wav");
            AddSound(sounds, "sword_scabbard_01", "Assets/Imports/FOOT/收脚音2.wav");
            AddSound(sounds, "weapon_end_01", "Assets/Imports/FOOT/收脚音1.wav");
            AddSound(sounds, "weapon_back_01", "Assets/Imports/FOOT/收脚音2.wav");
            library = ScriptableObject.CreateInstance<SoundLibrary>();
            library.sounds = sounds.ToArray();
            AssetDatabase.CreateAsset(library, SoundLibraryPath);
            AssetDatabase.SaveAssetIfDirty(library);
        }
        var managers = new List<SoundManager>();
        foreach (GameObject root in scene.GetRootGameObjects())
            managers.AddRange(root.GetComponentsInChildren<SoundManager>(true));
        if (managers.Count == 0)
        {
            var audio = new GameObject("Combo Demo Audio");
            SceneManager.MoveGameObjectToScene(audio, scene);
            Undo.RegisterCreatedObjectUndo(audio, "创建演示音效入口");
            managers.Add(Undo.AddComponent<SoundManager>(audio));
        }
        // 旧场景存在两个入口，均补同一库，避免 Awake 先后导致实际单例缺少音效。
        // 保留原库引用和场景对象；不覆盖其中的历史内容。
        foreach (SoundManager manager in managers)
        {
            var serialized = new SerializedObject(manager);
            SerializedProperty libraries = serialized.FindProperty("_libraries");
            bool present = false;
            for (int i = 0; i < libraries.arraySize; i++)
                if (libraries.GetArrayElementAtIndex(i).objectReferenceValue == library) present = true;
            if (present) continue;
            Undo.RecordObject(manager, "接入演示音效库");
            int index = libraries.arraySize;
            libraries.arraySize++;
            libraries.GetArrayElementAtIndex(index).objectReferenceValue = library;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        }
        Debug.Log("[连招演示] 音效库已接入 " + managers.Count + " 个场景入口：" + SoundLibraryPath);
    }

    private static void AddSound(List<SoundItem> sounds, string id, string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("演示音效来源不存在：" + path);
        sounds.Add(new SoundItem { soundID = id, clip = clip });
    }

    private static void AddMissingDemoSwingEvents(ComboData data, List<string> soundIds)
    {
        bool changed = false;
        for (int i = 0; i < data.steps.Count; i++)
        {
            ComboStep step = data.steps[i];
            if (step == null || step.animationClip == null || step.soundEvents == null || step.soundEvents.Count > 0) continue;
            string prefix = "atk0" + (i + 1) + "_swing";
            string id = soundIds.FirstOrDefault(s => s == prefix) ?? soundIds.FirstOrDefault(s => s.StartsWith(prefix + "_", StringComparison.Ordinal));
            if (id == null) continue;
            step.soundEvents.Add(new ComboSoundEvent { time = step.animationClip.length * .12f, soundId = id });
            changed = true;
        }
        if (!changed) return;
        data.revision++;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        Debug.Log("[连招演示] 已为演示连招的空音效列表补入默认挥砍事件，已有事件与其他参数保留。版本=" + data.revision);
    }

    private static void AddComponentByName(GameObject target, string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
            throw new InvalidOperationException("缺少运行组件 " + typeName + "；请先编译新脚本后再配置。");
        if (target.GetComponent(type) == null) Undo.AddComponent(target, type);
    }

    private static List<StateEntry> CollectStates(AnimatorController controller)
    {
        var states = new List<StateEntry>();
        foreach (AnimatorControllerLayer layer in controller.layers) CollectStates(layer.stateMachine, layer.name, states);
        return states;
    }

    private static void CollectStates(AnimatorStateMachine machine, string prefix, List<StateEntry> result)
    {
        foreach (ChildAnimatorState child in machine.states) result.Add(new StateEntry { path = prefix + "." + child.state.name, state = child.state });
        foreach (ChildAnimatorStateMachine child in machine.stateMachines) CollectStates(child.stateMachine, prefix + "." + child.stateMachine.name, result);
    }

    private static string FindState(List<StateEntry> states, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            StateEntry exact = states.FirstOrDefault(s => s.path.StartsWith("Base Layer.", StringComparison.Ordinal) &&
                s.state.motion != null && string.Equals(s.state.name, token, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.path;
        }
        foreach (string token in tokens)
        {
            StateEntry suffix = states.FirstOrDefault(s => s.path.StartsWith("Base Layer.", StringComparison.Ordinal) && s.state.motion != null &&
                (s.state.name.EndsWith("_" + token, StringComparison.OrdinalIgnoreCase) || s.state.motion.name.EndsWith("_" + token, StringComparison.OrdinalIgnoreCase)));
            if (suffix != null) return suffix.path;
        }
        return "";
    }

    private static string FindDefaultStatePath(AnimatorStateMachine machine, string prefix)
    {
        if (machine.defaultState != null && machine.defaultState.motion != null) return prefix + "." + machine.defaultState.name;
        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
        {
            string result = FindDefaultStatePath(child.stateMachine, prefix + "." + child.stateMachine.name);
            if (!string.IsNullOrEmpty(result)) return result;
        }
        return "";
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string parent = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parent + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[i]);
            parent = next;
        }
    }

    private static void EnsureBuildScene(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        EditorBuildSettingsScene existing = scenes.FirstOrDefault(s => s.path == path);
        if (existing != null) existing.enabled = true;
        else scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void SyncProjectFiles()
    {
        Type sync = typeof(Editor).Assembly.GetType("UnityEditor.SyncVS");
        MethodInfo method = sync?.GetMethod("SyncSolution", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (method != null) method.Invoke(null, null);
        else Debug.LogWarning("[连招演示] 未找到 SyncVS.SyncSolution，请从团结引擎重新生成 C# 工程文件后运行编译检查。");
    }

    [MenuItem("Tools/连招演示/构建 Windows Demo")]
    public static void BuildWindowsBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式再构建。");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            throw new InvalidOperationException("未安装 Windows Standalone 构建支持模块。");
        if (AssetDatabase.LoadAssetAtPath<ComboData>(DataPath) == null || AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
            throw new InvalidOperationException("请先执行配置当前场景并保存，再构建 Demo。");
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string output = Path.Combine(root, "Builds", "Windows", "UnityActionGame.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var text = new StringBuilder();
        text.AppendLine("Build: " + report.summary.result);
        text.AppendLine("Output: " + report.summary.outputPath);
        text.AppendLine("Errors: " + report.summary.totalErrors + " / Warnings: " + report.summary.totalWarnings);
        text.AppendLine("Size bytes: " + report.summary.totalSize + " / Duration: " + report.summary.totalTime);
        foreach (BuildStep step in report.steps)
            foreach (BuildStepMessage message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception) text.AppendLine(step.name + ": " + message.content);
        string reportPath = Path.Combine(root, "Logs", "combo-windows-build-report.txt");
        File.WriteAllText(reportPath, text.ToString(), new UTF8Encoding(false));
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows 构建失败，参见 " + reportPath);
        Debug.Log("[连招演示] Windows 构建成功：" + output + "；构建成功不代表性能与玩法已验收。");
    }
}
