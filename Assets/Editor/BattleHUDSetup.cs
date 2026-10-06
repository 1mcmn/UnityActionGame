using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>把菜单风格战斗 HUD 接入当前战斗场景；旧 HUD 保留引用，仅由 BattleHUD 运行时停止渲染。</summary>
public static class BattleHUDSetup
{
    private const string AssetsPath = NativeMenuSetup.Folder + "/NativeMenuAssets.asset";

    [MenuItem("Tools/战斗HUD/应用菜单风格 HUD 到当前场景")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改；保留现场，先保存后再应用。");
        var existing = UnityEngine.Object.FindObjectOfType<BattleHUD>(true);
        if (existing != null) { Selection.activeGameObject = existing.gameObject; Debug.Log("[战斗HUD] 当前场景已存在，保留现有设置。"); return; }
        if (UnityEngine.Object.FindObjectOfType<PlayerCombat>(true) == null) throw new InvalidOperationException("当前场景没有 PlayerCombat，请打开战斗场景后再应用。");
        var config = AssetDatabase.LoadAssetAtPath<NativeMenuAssets>(AssetsPath);
        if (config == null) throw new InvalidOperationException("缺少菜单美术资产：" + AssetsPath + "，请先执行 Tools/开始菜单/应用 Web 原生菜单 A。");

        var root = new GameObject("BattleHUD"); Undo.RegisterCreatedObjectUndo(root, "应用菜单风格战斗 HUD");
        var hud = root.AddComponent<BattleHUD>(); hud.assets = config;
        EditorUtility.SetDirty(hud); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
        Selection.activeGameObject = root;
        Debug.Log("[战斗HUD] 已保存到 " + scene.path + "。F1 显示旧调试面板。");
    }
}

/// <summary>方案 A：用独立资产统一战斗环境，不改导入素材和地形碰撞。</summary>
public static class BattleArtSetup
{
    private const string Folder = "Assets/Game/Generated/BattleArt";
    private static readonly Color Paper = new Color32(224, 227, 218, 255);
    private static readonly Color Ink = new Color32(42, 51, 53, 255);
    private static readonly Color Accent = new Color32(205, 250, 57, 255);

    [MenuItem("Tools/战斗美术/应用纸灰与墨黑风格 A")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty) throw new InvalidOperationException("请先保存当前场景，再应用美术方案。");
        if (scene.path != "Assets/Game/Scenes/DEMO City Crossing.unity" && scene.path != "Assets/Game/Scenes/GreatSword Battle Demo.unity")
            throw new InvalidOperationException("此入口只用于两个战斗场景。");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Generated", "BattleArt");
        var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) throw new InvalidOperationException("缺少 URP Simple Lit。");
        var steel = Solid("WeaponSteel", new Color32(117, 131, 127, 255));
        steel.SetFloat("_Smoothness", .55f); steel.SetColor("_SpecColor", new Color(.32f, .35f, .34f));
        var enemy = Solid("EnemyInk", Ink);
        var cloth = Solid("PlayerPaperCloth", Paper);
        var dark = Solid("PlayerInkCloth", new Color32(63, 73, 72, 255));
        var hair = Solid("PlayerAshHair", new Color32(149, 162, 155, 255));
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer) continue;
                bool player = renderer.GetComponentInParent<PlayerCombat>() != null;
                bool foe = renderer.GetComponentInParent<Enemy>() != null;
                if (!player && !foe) continue;
                Undo.RecordObject(renderer, "统一战斗材质");
                var materials = renderer.sharedMaterials;
                bool weapon = renderer.name.Contains("Weapon");
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i]; if (source == null) continue;
                    if (weapon) materials[i] = steel;
                    else if (foe) materials[i] = enemy;
                    else if (renderer.name == "Hair" || source.name.Contains("HAIR")) materials[i] = hair;
                    else if (source.name.Contains("CLOTH")) materials[i] = source.name.Contains("Shoes") || source.name.Contains("Onepiece") ? dark : cloth;
                    else if (source == dark || source == cloth) materials[i] = renderer.name == "Body" && i == 3 ? cloth : dark;
                    // 面部、眼睛与皮肤贴图保留，避免破坏五官与透明区域。
                }
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var feedback in root.GetComponentsInChildren<CombatFeedback>(true))
            {
                Undo.RecordObject(feedback, "统一武器强调色"); feedback.trailColor = Accent;
                EditorUtility.SetDirty(feedback); PrefabUtility.RecordPrefabInstancePropertyModifications(feedback);
            }
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true)) ApplyGround(terrain);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional) continue;
                Undo.RecordObject(light, "有层次的战斗光照"); light.color = new Color(1, .96f, .89f); light.intensity = 1.2f;
                light.shadows = LightShadows.Soft; light.shadowStrength = .78f;
                light.shadowBias = .025f; light.shadowNormalBias = .22f;
                light.transform.rotation = Quaternion.Euler(40, -38, 0);
                var lightData = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                if (lightData == null) lightData = light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                lightData.softShadowQuality = UnityEngine.Rendering.Universal.SoftShadowQuality.High;
                EditorUtility.SetDirty(lightData); PrefabUtility.RecordPrefabInstancePropertyModifications(lightData);
                EditorUtility.SetDirty(light); PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            }
        }
        RenderSettings.skybox = Sky();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.25f, .32f, .40f);
        RenderSettings.ambientEquatorColor = new Color(.17f, .22f, .26f);
        RenderSettings.ambientGroundColor = new Color(.09f, .10f, .12f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.48f, .56f, .60f); RenderSettings.fogStartDistance = 48; RenderSettings.fogEndDistance = 135;
        RestoreCharacterDetails(scene);
        BuildArena(scene);
        ImproveMaterialSurfaces();
        ConfigureClarity(scene);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("战斗场景保存失败。");
        Debug.Log("[战斗美术] 已保存纸灰/墨黑方案 A：" + scene.path);
    }

    /// <summary>引擎批处理入口：资源及 meta 均由 AssetDatabase 生成。</summary>
    public static void ApplyAll()
    {
        foreach (string path in new[] { "Assets/Game/Scenes/DEMO City Crossing.unity", "Assets/Game/Scenes/GreatSword Battle Demo.unity" })
        {
            EditorSceneManager.OpenScene(path); Apply();
        }
        EditorSceneManager.OpenScene("Assets/Game/Scenes/DEMO City Crossing.unity");
        RenderPreview();
        ValidateCameraTranslation();
        EditorSceneManager.OpenScene("Assets/Game/Scenes/menu.scene");
        Debug.Log("[战斗美术] 两个场景已保存，预览位于 Logs/arena-preview.png。");
    }

    private static void ConfigureClarity(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects()) foreach (var camera in root.GetComponentsInChildren<Camera>(true))
        {
            Undo.RecordObject(camera, "战斗画面清晰度"); camera.allowMSAA = true; camera.allowDynamicResolution = false;
            var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data != null)
            {
                Undo.RecordObject(data, "战斗画面清晰度"); data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                data.renderPostProcessing = false; EditorUtility.SetDirty(data);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }
            EditorUtility.SetDirty(camera); PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        }
        // 只提升 PC 高画质档；不改变低画质档、物理频率或 Animator 更新模式。
        foreach (string name in new[] { "High", "Very High", "Ultra" })
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/" + name + "_PipelineAsset.asset");
            if (pipeline == null) continue;
            pipeline.renderScale = 1; pipeline.msaaSampleCount = 4; EditorUtility.SetDirty(pipeline);
            pipeline.shadowDistance = 85; pipeline.shadowCascadeCount = 4; pipeline.cascadeBorder = .18f;
            var pipelineObject = new SerializedObject(pipeline);
            pipelineObject.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineObject.FindProperty("m_Cascade4Split").vector3Value = new Vector3(.10f, .27f, .55f);
            pipelineObject.ApplyModifiedPropertiesWithoutUndo();
            AddAmbientOcclusion(name);
        }
    }

    private static void RestoreCharacterDetails(UnityEngine.SceneManagement.Scene scene)
    {
        string[] bodySources = { "N00_000_00_Body_00_SKIN", "N00_010_01_Onepiece_00_CLOTH", "N00_000_00_HairBack_00_HAIR", "N00_005_01_Tops_01_CLOTH", "N00_004_01_Shoes_01_CLOTH" };
        var armour = Solid("SentinelArmour", new Color32(122, 137, 136, 255));
        armour.SetFloat("_Smoothness", .4f);
        var fabric = Solid("SentinelUndersuit", new Color32(37, 47, 51, 255));
        var mask = Solid("SentinelMask", new Color32(204, 214, 207, 255));
        var joints = Solid("SentinelJoints", new Color32(66, 79, 80, 255));
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<PlayerCombat>() != null && (renderer.name == "Body" || renderer.name == "Hair"))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string sourceName = renderer.name == "Hair" ? "N00_000_Hair_00_HAIR" : i < bodySources.Length ? bodySources[i] : null;
                        if (sourceName == null) continue;
                        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imports/momo/Material/" + sourceName + " (Instance).mat");
                        if (source == null) continue;
                        string path = Folder + "/Detailed_" + sourceName + ".mat";
                        var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (copy == null) { copy = new Material(source); AssetDatabase.CreateAsset(copy, path); }
                        copy.CopyPropertiesFromMaterial(source); copy.shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                        Color tint = sourceName.Contains("SKIN") ? Color.white : sourceName.Contains("HAIR") ? new Color(.77f, .84f, .81f) :
                            sourceName.Contains("Tops") ? new Color(.72f, .78f, .80f) : new Color(.24f, .31f, .35f);
                        copy.SetColor("_BaseColor", tint); copy.SetFloat("_Smoothness", sourceName.Contains("HAIR") ? .22f : .08f);
                        EditorUtility.SetDirty(copy); materials[i] = copy;
                    }
                    Undo.RecordObject(renderer, "恢复玩家纹理细节"); renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                var skin = renderer as SkinnedMeshRenderer;
                if (skin == null || skin.GetComponentInParent<Enemy>() == null || skin.sharedMesh == null) continue;
                string meshPath = Folder + "/SentinelSegmented.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null)
                {
                    mesh = UnityEngine.Object.Instantiate(skin.sharedMesh); mesh.name = "SentinelSegmented";
                    var triangles = mesh.triangles; var weights = mesh.boneWeights;
                    var groups = new System.Collections.Generic.List<int>[4];
                    for (int i = 0; i < groups.Length; i++) groups[i] = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int group = 1;
                        if (weights.Length == mesh.vertexCount)
                        {
                            int bone = weights[triangles[i]].boneIndex0;
                            string name = bone < skin.bones.Length && skin.bones[bone] != null ? skin.bones[bone].name : "";
                            group = name.Contains("head") || name.Contains("neck") ? 2 :
                                name.Contains("spine") || name.Contains("clavicle") || name.Contains("upperarm") || name.Contains("calf") ? 0 :
                                name.Contains("lowerarm") || name.Contains("foot") ? 3 : 1;
                        }
                        groups[group].Add(triangles[i]); groups[group].Add(triangles[i + 1]); groups[group].Add(triangles[i + 2]);
                    }
                    mesh.subMeshCount = 4;
                    for (int i = 0; i < groups.Length; i++) mesh.SetTriangles(groups[i], i, false);
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }
                Undo.RecordObject(skin, "敌人护甲与关节分色"); skin.sharedMesh = mesh;
                skin.sharedMaterials = new[] { armour, fabric, mask, joints };
                EditorUtility.SetDirty(skin); PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            }
        }
    }

    private static void BuildArena(UnityEngine.SceneManagement.Scene scene)
    {
        Transform player = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var existing = root.name == "Interblade Arena" ? root : null;
            if (existing != null) { Undo.DestroyObjectImmediate(existing); continue; }
            var controller = root.GetComponentInChildren<PlayerCombat>(true); if (controller != null) player = controller.transform;
        }
        if (player == null) throw new InvalidOperationException("没有玩家，不能定位竞技场。");
        Vector3 center = player.position + new Vector3(1.5f, 0, -3);
        center.y = 0;
        foreach (var root in scene.GetRootGameObjects()) foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            center.y = terrain.SampleHeight(center) + terrain.transform.position.y;
        var host = new GameObject("Interblade Arena"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
        Undo.RegisterCreatedObjectUndo(host, "建立训练竞技场"); host.transform.position = center;
        var concrete = Solid("ArenaConcrete", new Color32(183, 189, 181, 255));
        concrete.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/PaperConcrete.asset"));
        concrete.SetColor("_BaseColor", new Color(.97f, .98f, .97f));
        var chalk = Solid("ArenaChalk", new Color32(205, 211, 201, 255));
        var basalt = Solid("ArenaBasalt", new Color32(58, 69, 72, 255));
        var trim = Solid("ArenaTrim", new Color32(110, 126, 127, 255));
        var accent = Solid("ArenaSignal", Accent);
        accent.SetColor("_EmissionColor", Accent * .12f); accent.EnableKeyword("_EMISSION");
        // 大块铺装与内场：低频细节，给角色和战斗反馈留出画面空间。
        for (int x = 0; x < 3; x++) for (int z = 0; z < 6; z++)
            Block(host.transform, "Concrete slab", new Vector3((x - 1) * 16, -.10f, (z - 2.5f) * 8), new Vector3(15.98f, .24f, 7.98f), concrete);
        for (int side = 0; side < 4; side++)
        {
            float angle = side * 90;
            var group = new GameObject("Perimeter " + side).transform; group.SetParent(host.transform, false); group.localRotation = Quaternion.Euler(0, angle, 0);
            Block(group, "Boundary band", new Vector3(0, .025f, 24), new Vector3(48, .02f, 1.4f), basalt);
            Block(group, "Outer apron", new Vector3(0, -.04f, 27.5f), new Vector3(55, .13f, 5.4f), trim);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Block(group, "Low barrier", new Vector3(sign * 16, .5f, 28), new Vector3(16, 1, 1.3f), basalt, true);
                Block(group, "Barrier cap", new Vector3(sign * 16, 1.01f, 28), new Vector3(16.1f, .12f, 1.45f), concrete);
                Block(group, "Entry marker", new Vector3(sign * 7.5f, .046f, 24), new Vector3(1.2f, .025f, 1.2f), accent);
                for (int n = 0; n < 4; n++)
                    Block(group, "Track marking", new Vector3(sign * (18 + n * 1.2f), .045f, 22.5f), new Vector3(.12f, .015f, 1.5f), chalk);
                // 远景采用错落的窄塔与悬挑梁，地平线有明确的建筑节奏。
                Block(group, "Outer buttress", new Vector3(sign * 23, 4, 33), new Vector3(2.5f, 8, 5), concrete);
                Block(group, "Buttress stripe", new Vector3(sign * 23, 4.1f, 30.45f), new Vector3(.5f, 6.5f, .08f), accent);
                Block(group, "Buttress recessed panel", new Vector3(sign * 23 + .48f, 4, 30.44f), new Vector3(.3f, 7.1f, .09f), basalt);
                Block(group, "Distant mass", new Vector3(sign * 37, 5.5f + side * .5f, 51), new Vector3(9, 11 + side, 13), concrete);
                Block(group, "Distant dark face", new Vector3(sign * 37, 5.5f + side * .5f, 44.45f), new Vector3(7, 10 + side, .08f), trim);
            }
        }
        // 北侧的门架是朝向地标；中间通道保持开放。
        Block(host.transform, "Gate left", new Vector3(-6, 5, -35), new Vector3(2, 10, 3), basalt);
        Block(host.transform, "Gate right", new Vector3(6, 5, -35), new Vector3(2, 10, 3), basalt);
        Block(host.transform, "Gate lintel", new Vector3(0, 9, -35), new Vector3(14, 2, 3), basalt);
        Block(host.transform, "Gate signal recess", new Vector3(0, 8.35f, -33.43f), new Vector3(11.6f, .65f, .09f), trim);
        Block(host.transform, "Gate signal", new Vector3(0, 8.35f, -33.35f), new Vector3(11, .3f, .10f), accent);
        Block(host.transform, "Gate left face", new Vector3(-6, 5, -33.43f), new Vector3(1.25f, 8.6f, .10f), trim);
        Block(host.transform, "Gate right face", new Vector3(6, 5, -33.43f), new Vector3(1.25f, 8.6f, .10f), trim);
        Label(host.transform, "FIELD / 01", new Vector3(0, 9, -33.43f), Quaternion.Euler(0, 180, 0), 1.1f, Paper);
        Label(host.transform, "INTERBLADE", new Vector3(-20, .04f, -18), Quaternion.Euler(90, 0, 0), 1.1f, Ink);
        Label(host.transform, "TRAINING DIVISION     /     01", new Vector3(-20, .042f, -20), Quaternion.Euler(90, 0, 0), .35f, Ink);
    }

    private static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = false)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = name; block.transform.SetParent(parent, false);
        block.transform.localPosition = position; block.transform.localScale = size;
        var renderer = block.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        if (size.y >= .12f && size.x >= .12f && size.z >= .12f) block.GetComponent<MeshFilter>().sharedMesh = ChamferedBox(size);
        if (size.y < .05f) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (!collision) UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
        return block;
    }

    private static void Label(Transform parent, string value, Vector3 position, Quaternion rotation, float size, Color color)
    {
        var host = new GameObject(value); host.transform.SetParent(parent, false); host.transform.localPosition = position; host.transform.localRotation = rotation;
        var text = host.AddComponent<TMPro.TextMeshPro>();
        var assets = AssetDatabase.LoadAssetAtPath<NativeMenuAssets>(NativeMenuSetup.Folder + "/NativeMenuAssets.asset");
        if (assets != null && assets.condensedFont != null) text.font = assets.condensedFont;
        text.text = value; text.fontSize = size; text.color = color; text.alignment = TMPro.TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(22, 3); text.enableWordWrapping = false;
    }

    public static void RenderPreview()
    {
        Camera productionCamera = null;
        foreach (var follow in UnityEngine.Object.FindObjectsOfType<CameraFollow>(true))
            if (follow.GetComponent<Camera>() != null) { productionCamera = follow.GetComponent<Camera>(); break; }
        var host = new GameObject("Temporary Art Preview"); var camera = host.AddComponent<Camera>();
        try
        {
            AnimationMode.StartAnimationMode(); AnimationMode.BeginSampling();
            foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>(true))
            {
                if (animator.runtimeAnimatorController == null) continue;
                Vector3 position = animator.transform.localPosition, scale = animator.transform.localScale;
                Quaternion rotation = animator.transform.localRotation;
                AnimationClip idle = null;
                var brain = animator.GetComponentInParent<GreatSwordEnemyBrain>();
                if (brain != null && brain.Config != null) idle = brain.Config.idleClip;
                var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (idle == null && controller != null) foreach (var layer in controller.layers)
                { idle = FindIdle(layer.stateMachine); if (idle != null) break; }
                if (idle != null) AnimationMode.SampleAnimationClip(animator.gameObject, idle, Mathf.Min(.3f, idle.length * .5f));
                // Humanoid 片段可能包含根变换曲线；预览只采用骨骼姿态，不改变场景中角色的摆放。
                animator.transform.localPosition = position; animator.transform.localRotation = rotation; animator.transform.localScale = scale;
            }
            AnimationMode.EndSampling();
            camera.transform.position = new Vector3(44, 3.2f, 44); camera.transform.LookAt(new Vector3(50, 1.3f, 36.5f));
            camera.fieldOfView = 58; camera.clearFlags = CameraClearFlags.Skybox; camera.allowMSAA = true;
            CapturePreview(camera, "Logs/arena-preview.png");
            if (productionCamera != null)
            {
                camera.CopyFrom(productionCamera); camera.enabled = false;
                var data = new SerializedObject(productionCamera.GetComponent<CameraFollow>());
                var actor = (Transform)data.FindProperty("target").objectReferenceValue;
                if (actor != null)
                {
                    // 与 CameraFollow.Start/LateUpdate 的常态取景一致；静态画面不能替代动态验收。
                    camera.transform.position = actor.position + Quaternion.Euler(15, productionCamera.transform.eulerAngles.y, 0) * data.FindProperty("offset").vector3Value;
                    camera.transform.LookAt(actor.position + Vector3.up * 1.5f);
                    CapturePreview(camera, "Logs/arena-gameplay-preview.png");
                }
            }
            ValidateArtAssets();
        }
        finally { AnimationMode.StopAnimationMode(); UnityEngine.Object.DestroyImmediate(host); }
    }

    private static AnimationClip FindIdle(UnityEditor.Animations.AnimatorStateMachine machine)
    {
        foreach (var state in machine.states)
            if (state.state.name.Equals("idle", StringComparison.OrdinalIgnoreCase)) return state.state.motion as AnimationClip;
        foreach (var child in machine.stateMachines) { var clip = FindIdle(child.stateMachine); if (clip != null) return clip; }
        return null;
    }

    private static void CapturePreview(Camera camera, string path)
    {
        var target = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; var previous = RenderTexture.active;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            System.IO.Directory.CreateDirectory("Logs"); System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
    }

    private static void ValidateArtAssets()
    {
        var report = new System.Text.StringBuilder();
        var sky = Shader.Find("Interblade/Atmosphere");
        if (sky == null || ShaderUtil.ShaderHasError(sky)) throw new InvalidOperationException("天空着色器编译失败。");
        report.AppendLine("Atmosphere shader: compiled; preview: 1600x900, 4x MSAA.");
        foreach (string quality in new[] { "High", "Very High", "Ultra" })
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>("Assets/Settings/" + quality + "_PipelineAsset_ForwardRenderer.asset");
            int count = 0;
            foreach (var feature in renderer.rendererFeatures)
                if (feature != null && feature.isActive && feature.GetType().Name == "ScreenSpaceAmbientOcclusion") count++;
            if (count != 1) throw new InvalidOperationException(quality + " 的 AO 缺失或重复。");
            report.AppendLine(quality + ": one active native URP SSAO feature.");
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            if (!mesh.name.StartsWith("Chamfer")) continue;
            if (mesh.vertexCount != 96 || mesh.triangles.Length != 132 || mesh.tangents.Length != mesh.vertexCount)
                throw new InvalidOperationException("建筑倒角网格不完整：" + mesh.name);
        }
        report.AppendLine("Chamfer meshes: closed 6 faces + 12 edges + 8 corners, normals and tangents present.");
        System.IO.File.WriteAllText("Logs/battle-art-validation.txt", report.ToString());
    }

    private static void ValidateCameraTranslation()
    {
        // 受控平移检查实际 LateUpdate：镜头环绕不变时，相机与目标之间的相对位置应保持不变。
        var host = new GameObject("Camera translation check"); var target = new GameObject("Translation target");
        try
        {
            host.AddComponent<Camera>(); var follow = host.AddComponent<CameraFollow>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(CameraFollow).GetField("target", flags).SetValue(follow, target.transform);
            typeof(CameraFollow).GetField("lockCursorOnStart", flags).SetValue(follow, false);
            typeof(CameraFollow).GetField("snapNext", flags).SetValue(follow, true);
            typeof(CameraFollow).GetMethod("Start", flags).Invoke(follow, null);
            var update = typeof(CameraFollow).GetMethod("LateUpdate", flags); update.Invoke(follow, null);
            Vector3 offset = host.transform.position - target.transform.position; float maxError = 0;
            for (int i = 0; i < 120; i++)
            {
                target.transform.position += new Vector3(.10f, 0, .025f); update.Invoke(follow, null);
                maxError = Mathf.Max(maxError, ((host.transform.position - target.transform.position) - offset).magnitude);
            }
            System.IO.File.WriteAllText("Logs/camera-translation-check.txt", "120 controlled translation steps; max relative offset error (metres)=" + maxError.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (maxError > .001f) throw new InvalidOperationException("相机平移检查失败：" + maxError);
            Debug.Log("[战斗美术] 相机120步平移检查通过，最大相对误差=" + maxError);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(target); }
    }

    private static Material Solid(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", color); mat.SetTexture("_BaseMap", null);
        SetSpecular(mat, new Color(.035f, .04f, .045f), .12f); mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat); return mat;
    }

    private static void SetSpecular(Material material, Color color, float smoothness)
    {
        // Simple Lit 的光滑度存于 _SpecColor.a；仅写 _Smoothness 不会产生正确的材质响应。
        color.a = smoothness; material.SetColor("_SpecColor", color); material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_SpecularHighlights", 1); material.EnableKeyword("_SPECULAR_COLOR");
        material.DisableKeyword("_SPECGLOSSMAP"); material.DisableKeyword("_GLOSSINESS_FROM_BASE_ALPHA");
    }

    private static void AddAmbientOcclusion(string quality)
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(
            "Assets/Settings/" + quality + "_PipelineAsset_ForwardRenderer.asset");
        if (renderer == null) throw new InvalidOperationException("缺少渲染器：" + quality);
        // URP 14 的内置 SSAO 类型为 internal，通过实际包类型创建，设置仍走 SerializedObject。
        var type = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).Assembly.GetType(
            "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", true);
        UnityEngine.Rendering.Universal.ScriptableRendererFeature feature = null;
        foreach (var item in renderer.rendererFeatures) if (item != null && item.GetType() == type) { feature = item; break; }
        if (feature == null)
        {
            feature = (UnityEngine.Rendering.Universal.ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "Interblade Contact AO"; AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
        }
        var serialized = new SerializedObject(feature); var settings = serialized.FindProperty("m_Settings");
        settings.FindPropertyRelative("AOMethod").enumValueIndex = 1; // 确定性的 InterleavedGradient，不依赖时间抖动。
        settings.FindPropertyRelative("Downsample").boolValue = true;
        settings.FindPropertyRelative("AfterOpaque").boolValue = false;
        settings.FindPropertyRelative("Source").enumValueIndex = 1;
        settings.FindPropertyRelative("Intensity").floatValue = 1.15f;
        settings.FindPropertyRelative("DirectLightingStrength").floatValue = .16f;
        settings.FindPropertyRelative("Radius").floatValue = .32f;
        settings.FindPropertyRelative("Samples").enumValueIndex = 1;
        settings.FindPropertyRelative("BlurQuality").enumValueIndex = 0;
        settings.FindPropertyRelative("Falloff").floatValue = 70;
        var shader = Shader.Find("Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion");
        if (shader == null) throw new InvalidOperationException("缺少 URP 内置 SSAO 着色器。");
        serialized.FindProperty("m_Shader").objectReferenceValue = shader; serialized.ApplyModifiedPropertiesWithoutUndo();
        feature.SetActive(true); feature.Create(); EditorUtility.SetDirty(feature);
        // 同步子资产 ID，避免重新载入或 PC 构建时 RendererFeature 的引用丢失。
        var rendererObject = new SerializedObject(renderer); var map = rendererObject.FindProperty("m_RendererFeatureMap");
        map.arraySize = renderer.rendererFeatures.Count;
        for (int i = 0; i < map.arraySize; i++)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string guid, out long id))
                throw new InvalidOperationException("AO 子资产 ID 未生成。");
            map.GetArrayElementAtIndex(i).longValue = id;
        }
        rendererObject.ApplyModifiedPropertiesWithoutUndo(); renderer.SetDirty(); EditorUtility.SetDirty(renderer);
    }

    private static void ImproveMaterialSurfaces()
    {
        var stone = SurfaceTexture("Stone", false); var stoneNormal = SurfaceTexture("Stone", true);
        var metal = SurfaceTexture("BrushedMetal", false); var metalNormal = SurfaceTexture("BrushedMetal", true);
        var clothNormal = SurfaceTexture("Weave", true);
        foreach (string name in new[] { "ArenaConcrete", "ArenaChalk", "ArenaBasalt", "ArenaTrim" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            material.SetTexture("_BaseMap", stone); material.SetTextureScale("_BaseMap", new Vector2(2, 2));
            material.SetTexture("_BumpMap", stoneNormal); material.SetTextureScale("_BumpMap", new Vector2(2, 2));
            material.EnableKeyword("_NORMALMAP"); SetSpecular(material, new Color(.055f, .06f, .065f), .13f);
            if (name == "ArenaConcrete") material.SetColor("_BaseColor", new Color(.78f, .84f, .85f));
            if (name == "ArenaChalk") material.SetColor("_BaseColor", new Color(.9f, .94f, .91f));
            EditorUtility.SetDirty(material);
        }
        foreach (string name in new[] { "WeaponSteel", "SentinelArmour", "SentinelJoints", "SentinelMask", "SentinelUndersuit" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            bool fabric = name == "SentinelUndersuit";
            material.SetTexture("_BumpMap", fabric ? clothNormal : metalNormal); material.EnableKeyword("_NORMALMAP");
            material.SetTextureScale("_BumpMap", fabric ? new Vector2(3, 3) : Vector2.one);
            if (!fabric) material.SetTexture("_BaseMap", metal);
            SetSpecular(material, fabric ? new Color(.025f, .03f, .035f) : new Color(.22f, .25f, .28f), fabric ? .07f : .32f);
            EditorUtility.SetDirty(material);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); if (!path.Contains("Detailed_")) continue;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (path.Contains("CLOTH"))
            {
                material.SetTexture("_BumpMap", clothNormal); material.SetTextureScale("_BumpMap", new Vector2(4, 4));
                material.EnableKeyword("_NORMALMAP"); SetSpecular(material, new Color(.025f, .03f, .035f), .08f);
            }
            else if (path.Contains("HAIR"))
            {
                string file = path.Contains("HairBack") ? "N00_000_00_HairBack_00_nml.png" : "N00_000_Hair_00_nml.png";
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imports/momo/Untitled.fbm/" + file);
                if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
                SetSpecular(material, new Color(.07f, .08f, .085f), .2f);
            }
            EditorUtility.SetDirty(material);
        }
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Folder + "/PaperGroundLayer.terrainlayer");
        if (layer != null)
        {
            layer.diffuseTexture = stone; layer.normalMapTexture = stoneNormal; layer.tileSize = new Vector2(16, 16);
            layer.normalScale = .6f; EditorUtility.SetDirty(layer);
        }
    }

    private static Texture2D SurfaceTexture(string family, bool normal)
    {
        const int resolution = 512;
        string name = family + (normal ? "Normal" : "Albedo"), path = Folder + "/" + name + ".asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, normal) { name = name };
            AssetDatabase.CreateAsset(texture, path);
        }
        texture.wrapMode = TextureWrapMode.Repeat; texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = 8;
        // 整数周期噪声与织纹：生成可重复的表面，法线由同一高度场求导，不把阴影烘进颜色。
        var heights = new float[resolution * resolution]; var pixels = new Color[heights.Length];
        for (int y = 0; y < resolution; y++) for (int x = 0; x < resolution; x++)
        {
            float u = x / (float)resolution, v = y / (float)resolution;
            float broad = PeriodicNoise(u, v, 8), medium = PeriodicNoise(u, v, 32), fine = PeriodicNoise(u, v, 128);
            uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663); hash ^= hash >> 13; hash *= 1274126177;
            float grain = (hash & 65535) / 65535f;
            float height;
            if (family == "Weave") height = Mathf.Sin(u * Mathf.PI * 128) * Mathf.Sin(v * Mathf.PI * 128) * .045f + (grain - .5f) * .012f;
            else if (family == "BrushedMetal") height = Mathf.Sin(v * Mathf.PI * 256) * .012f + (fine - .5f) * .05f + (grain - .5f) * .018f;
            else height = broad * .12f + medium * .1f + fine * .09f + (grain - .5f) * .035f;
            heights[y * resolution + x] = height;
            float value = family == "BrushedMetal" ? .86f + height * .8f + (grain - .5f) * .025f :
                .68f + (broad - .5f) * .085f + (medium - .5f) * .075f + (fine - .5f) * .045f + (grain - .5f) * .028f;
            // 稀疏孔隙及微弱磨损；保持整体低对比，避免移动时出现高频闪烁。
            if (family == "Stone" && grain < .022f) value -= .065f;
            pixels[y * resolution + x] = new Color(value * .98f, value, value * 1.015f, 1);
        }
        if (normal)
        {
            for (int y = 0; y < resolution; y++) for (int x = 0; x < resolution; x++)
            {
                float dx = heights[y * resolution + (x + 1) % resolution] - heights[y * resolution + (x + resolution - 1) % resolution];
                float dy = heights[((y + 1) % resolution) * resolution + x] - heights[((y + resolution - 1) % resolution) * resolution + x];
                var n = new Vector3(-dx * 1.5f, -dy * 1.5f, 1).normalized;
                pixels[y * resolution + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }
        }
        texture.SetPixels(pixels); texture.Apply(true, false); EditorUtility.SetDirty(texture); return texture;
    }

    private static float PeriodicNoise(float u, float v, int cells)
    {
        float x = u * cells, y = v * cells; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        Func<int, int, float> hash = (a, b) =>
        {
            uint value = (uint)((a % cells) * 374761393) + (uint)((b % cells) * 668265263);
            value = (value ^ (value >> 13)) * 1274126177; return (value & 65535) / 65535f;
        };
        return Mathf.Lerp(Mathf.Lerp(hash(ix, iy), hash(ix + 1, iy), fx), Mathf.Lerp(hash(ix, iy + 1), hash(ix + 1, iy + 1), fx), fy);
    }

    private static Mesh ChamferedBox(Vector3 size)
    {
        string key = size.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "_" +
            size.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "_" + size.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string path = Folder + "/Chamfer_" + key + ".asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        var vertices = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>();
        var triangles = new System.Collections.Generic.List<int>();
        Vector3 outer = size * .5f; float bevel = Mathf.Clamp(Mathf.Min(size.x, size.y, size.z) * .14f, .018f, .16f);
        Vector3 inner = outer - Vector3.one * bevel;
        Action<Vector3[], Vector3> face = (points, normal) =>
        {
            int start = vertices.Count; int axis = Mathf.Abs(normal.x) > Mathf.Abs(normal.y) ? 0 : 1;
            if (Mathf.Abs(normal.z) > Mathf.Abs(normal[axis])) axis = 2;
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            foreach (var point in points)
            {
                vertices.Add(new Vector3(point.x / size.x, point.y / size.y, point.z / size.z));
                uv.Add(new Vector2(point[a] / size[a] + .5f, point[b] / size[b] + .5f));
            }
            bool forward = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) > 0;
            for (int i = 1; i < points.Length - 1; i++)
            { triangles.Add(start); triangles.Add(start + (forward ? i : i + 1)); triangles.Add(start + (forward ? i + 1 : i)); }
        };
        for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
        {
            int a = (axis + 1) % 3, b = (axis + 2) % 3; var points = new Vector3[4];
            for (int i = 0; i < 4; i++) { points[i][axis] = outer[axis] * sign; points[i][a] = inner[a] * (i == 0 || i == 3 ? -1 : 1); points[i][b] = inner[b] * (i < 2 ? -1 : 1); }
            var normal = Vector3.zero; normal[axis] = sign; face(points, normal);
        }
        for (int axis = 0; axis < 3; axis++) for (int sa = -1; sa <= 1; sa += 2) for (int sb = -1; sb <= 1; sb += 2)
        {
            int a = (axis + 1) % 3, b = (axis + 2) % 3; var points = new Vector3[4];
            for (int i = 0; i < 4; i++)
            { points[i][axis] = inner[axis] * (i == 0 || i == 3 ? -1 : 1); points[i][a] = (i < 2 ? outer[a] : inner[a]) * sa; points[i][b] = (i < 2 ? inner[b] : outer[b]) * sb; }
            var normal = Vector3.zero; normal[a] = sa; normal[b] = sb; face(points, normal);
        }
        for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
        {
            var corner = new Vector3(inner.x * sx, inner.y * sy, inner.z * sz);
            face(new[] { corner + Vector3.right * (bevel * sx), corner + Vector3.up * (bevel * sy), corner + Vector3.forward * (bevel * sz) }, new Vector3(sx, sy, sz));
        }
        mesh = new Mesh { name = "Chamfer " + key }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
    }

    private static void ApplyGround(Terrain terrain)
    {
        string texturePath = Folder + "/PaperConcrete.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            texture = new Texture2D(512, 512, TextureFormat.RGBA32, true) { name = "PaperConcrete", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            AssetDatabase.CreateAsset(texture, texturePath);
        }
        {
            var pixels = new Color[512 * 512];
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            {
                float grain = (((x * 73856093L ^ y * 19349663L) & 255) / 255f - .5f) * .009f;
                float seam = x < 2 || y < 2 ? -.035f : 0;
                pixels[y * 512 + x] = new Color(.74f + grain + seam, .745f + grain + seam, .73f + grain + seam);
            }
            texture.SetPixels(pixels); texture.Apply(true, false); EditorUtility.SetDirty(texture);
        }
        string layerPath = Folder + "/PaperGroundLayer.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, layerPath); }
        layer.diffuseTexture = texture; layer.tileSize = new Vector2(16, 16); layer.smoothness = .03f; EditorUtility.SetDirty(layer);
        string dataPath = Folder + "/" + terrain.gameObject.scene.name.Replace(" ", "_") + "_Ground.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (data == null) { data = UnityEngine.Object.Instantiate(terrain.terrainData); AssetDatabase.CreateAsset(data, dataPath); }
        data.terrainLayers = new[] { layer };
        var weights = new float[data.alphamapHeight, data.alphamapWidth, 1];
        for (int y = 0; y < data.alphamapHeight; y++) for (int x = 0; x < data.alphamapWidth; x++) weights[y, x, 0] = 1;
        data.SetAlphamaps(0, 0, weights); EditorUtility.SetDirty(data);
        Undo.RecordObject(terrain, "统一地面显示"); terrain.terrainData = data;
        var collider = terrain.GetComponent<TerrainCollider>();
        if (collider != null) { Undo.RecordObject(collider, "保持地形碰撞接线"); collider.terrainData = data; EditorUtility.SetDirty(collider); }
        EditorUtility.SetDirty(terrain); PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
    }

    private static Material Sky()
    {
        string path = Folder + "/PaperSky.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Interblade/Atmosphere"); if (shader == null) throw new InvalidOperationException("天空着色器未导入。");
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); } else mat.shader = shader;
        mat.SetColor("_Zenith", new Color(.23f, .34f, .45f).linear);
        mat.SetColor("_Horizon", new Color(.56f, .64f, .67f).linear);
        mat.SetColor("_Ground", new Color(.24f, .30f, .34f).linear);
        EditorUtility.SetDirty(mat); return mat;
    }
}
