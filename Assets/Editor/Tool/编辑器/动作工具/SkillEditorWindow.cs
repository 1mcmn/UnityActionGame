using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SkillSystem
{
    public class SkillEditorWindow : EditorWindow
    {
        // 技能库固定路径：自动创建/加载，无需手动选择（v2 调整）
        private const string LibraryPath = "Assets/SkillSystem/SkillLibrary.asset";

        private SkillLibrary library;
        private SerializedObject librarySerializedObject;
        private SerializedProperty skillsProperty;

        private SerializedObject skillSerializedObject;
        private SerializedProperty skillIdProperty;
        private SerializedProperty skillNameProperty;
        private SerializedProperty componentsProperty;

        private ReorderableList skillsList;
        private ReorderableList componentsList;

        private int selectedSkillIndex = -1;
        private Vector2 skillsScroll;
        private Vector2 componentsScroll;

        // ── 阶段 5：帧事件表格 ──
        private int selectedComponentIndex = -1;
        private ReorderableList frameEventsList;
        private SerializedProperty frameEventsProperty;

        // 音效库数据（soundId 下拉 + 编辑器内试听）
        private List<string> soundIds = new List<string>();
        private Dictionary<string, AudioClip> soundIdToClip = new Dictionary<string, AudioClip>();

        // 编辑器内试听（参考 AudioBrowser 的临时 AudioSource 方案）
        private AudioSource previewSource;

        // 动画预览（阶段 5 可选：拖时间轴看帧、一键加事件）
        private AnimationClip previewClip;
        private float previewTime = 0f;

        // ── P0-1 预览目标：手动指定优先；留空则自动找 tag=Player 的 Animator ──
        private Animator previewTarget;          // 手动指定（拖进来的）；null = 自动
        private Animator autoPreviewTarget;      // 自动查找结果缓存
        private Animator lastSampledTarget;      // 上一次采样的对象（换目标时先还原旧对象）

        // ── P0-4 AnimationMode 进出配对：只关自己开的，不碰 Animation 窗口的 ──
        private bool previewInAnimationMode;

        // ── P0-3 第一步：简易播放控制（内置控件版；第二步再换自绘时间轴）──
        private bool previewPlaying;
        private bool previewLoop = true;
        private double previewLastTick;
        private double previewLastRepaint;
        private const double PreviewRepaintInterval = 1.0 / 60.0;

        // ── 预览时试听帧事件：播放头跨过事件帧就响一声（不必进游戏）──
        private bool previewAuditionSfx = true;
        private int previewFrameCursor = -1;
        private const int MaxAuditionSpan = 15;   // 一次跨超过这么多帧算"跳转"，不试听

        // ── P0-9 运行时音效ID集合：只统计"场景里 SoundManager 挂载的库" ──
        //    （编辑器下拉用的是全项目所有 SoundLibrary，两者不一致时游戏里会静默无声）
        private readonly HashSet<string> runtimeSoundIds = new HashSet<string>();
        private readonly List<string> runtimeLibraryNames = new List<string>();
        private bool runtimeSoundManagerFound;

        // ── P0-2(A) 玩家动画机状态表：stateName 校验 + 自动解析出真正的 clip ──
        private Dictionary<string, AnimationClip> playerStates;
        private readonly Dictionary<string, float> playerStateSpeeds = new Dictionary<string, float>();
        // state 在【基础 controller】里原本绑的 clip —— 这是 AnimatorOverrideController 的映射 key
        private readonly Dictionary<string, AnimationClip> playerBaseClips = new Dictionary<string, AnimationClip>();
        private string playerStatesSource = "";

        // ── B：判定盒 SceneView 可视化 + 手柄调参 ──
        private bool showHitBoxGizmo = true;

        // ── P1-4 编辑器状态持久化（SessionState：关窗口/重编译都不丢）──
        private const string SS_PreviewClip = "SkillEditor.PreviewClipId";
        private const string SS_PreviewTarget = "SkillEditor.PreviewTargetId";
        private const string SS_PreviewLoop = "SkillEditor.PreviewLoop";
        private const string SS_Audition = "SkillEditor.AuditionSfx";
        private const string SS_SkillId = "SkillEditor.SelectedSkillId";

        // 基础组件注册表：右侧添加组件时的可选类型（与参考视频的"基础组件"概念对应）
        private static readonly Dictionary<string, Type> ComponentTypes = new Dictionary<string, Type>
        {
            { "播放动画", typeof(PlayAnimationComponent) },
            { "判定窗口", typeof(HitWindowComponent) },
            { "伤害", typeof(DamageComponent) },
            { "命中反馈", typeof(HitFeedbackComponent) },
            { "无敌帧", typeof(InvincibleWindowComponent) },
        };

        [MenuItem("Tools/技能编辑器")]
        public static void ShowWindow()
        {
            var window = GetWindow<SkillEditorWindow>("技能编辑器");
            window.minSize = new Vector2(900, 500);
        }

        private void OnEnable()
        {
            LoadOrCreateLibrary();
            LoadSoundData();
            RestoreEditorState();                          // 恢复上次的预览目标/预览动画/选中的技能
            EditorApplication.update += OnEditorUpdate;    // 预览播放的驱动（P0-3 第一步）
            SceneView.duringSceneGui += OnSceneGUI;        // B：判定盒可视化
        }

        private void OnDisable()
        {
            SaveEditorState();                             // 关窗口也记住，下次不用重选
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            previewPlaying = false;
            ExitAnimationMode();                 // 只关自己开的动画模式；StopAnimationMode 会把场景姿态还原
            lastSampledTarget = null;
            if (previewSource != null)
            {
                DestroyImmediate(previewSource.gameObject);
                previewSource = null;
            }
        }

        // ──────────────── 编辑器状态持久化（P1-4）────────────────

        private void SaveEditorState()
        {
            SessionState.SetInt(SS_PreviewClip, previewClip != null ? previewClip.GetInstanceID() : 0);
            SessionState.SetInt(SS_PreviewTarget, previewTarget != null ? previewTarget.GetInstanceID() : 0);
            SessionState.SetBool(SS_PreviewLoop, previewLoop);
            SessionState.SetBool(SS_Audition, previewAuditionSfx);

            string id = "";
            if (skillsProperty != null && selectedSkillIndex >= 0 && selectedSkillIndex < skillsProperty.arraySize)
            {
                var cfg = skillsProperty.GetArrayElementAtIndex(selectedSkillIndex).objectReferenceValue as SkillConfig;
                if (cfg != null) id = cfg.skillId;
            }
            SessionState.SetString(SS_SkillId, id);
        }

        private void RestoreEditorState()
        {
            int clipId = SessionState.GetInt(SS_PreviewClip, 0);
            if (clipId != 0) previewClip = EditorUtility.InstanceIDToObject(clipId) as AnimationClip;

            int targetId = SessionState.GetInt(SS_PreviewTarget, 0);
            if (targetId != 0) previewTarget = EditorUtility.InstanceIDToObject(targetId) as Animator;

            previewLoop = SessionState.GetBool(SS_PreviewLoop, true);
            previewAuditionSfx = SessionState.GetBool(SS_Audition, true);

            // 按 skillId 恢复上次选中的技能（按下标会漂移）
            string lastSkillId = SessionState.GetString(SS_SkillId, "");
            if (string.IsNullOrEmpty(lastSkillId) || skillsProperty == null) return;

            for (int i = 0; i < skillsProperty.arraySize; i++)
            {
                var cfg = skillsProperty.GetArrayElementAtIndex(i).objectReferenceValue as SkillConfig;
                if (cfg == null || cfg.skillId != lastSkillId) continue;
                selectedSkillIndex = i;
                LoadSelectedSkill();
                break;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"技能库：{LibraryPath}（自动管理）", EditorStyles.miniLabel);

            if (library == null)
            {
                LoadOrCreateLibrary();
                if (library == null) return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawSkillList();       // 左栏：技能列表
            DrawSkillInspector();  // 右栏：技能详情（组件内嵌，直接调参）
            EditorGUILayout.EndHorizontal();

            DrawSaveButton();
        }

        #region 库管理（自动）

        private void LoadOrCreateLibrary()
        {
            library = AssetDatabase.LoadAssetAtPath<SkillLibrary>(LibraryPath);
            if (library == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/SkillSystem"))
                    AssetDatabase.CreateFolder("Assets", "SkillSystem");

                library = ScriptableObject.CreateInstance<SkillLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[SkillEditor] 已自动创建技能库：{LibraryPath}");
            }

            selectedSkillIndex = -1;
            skillSerializedObject = null;
            InitializeLibrarySerializedObject();
        }

        private void InitializeLibrarySerializedObject()
        {
            if (library == null) return;
            librarySerializedObject = new SerializedObject(library);
            skillsProperty = librarySerializedObject.FindProperty("skills");
            skillsList = null; // 强制重建
        }

        #endregion

        #region 左栏：技能列表（技能本身仍是资产，符合任务书"保存为 ScriptableObject"）

        private void DrawSkillList()
        {
            if (librarySerializedObject == null)
                InitializeLibrarySerializedObject();

            if (skillsList == null)
                BuildSkillsList();

            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            skillsScroll = EditorGUILayout.BeginScrollView(skillsScroll);
            skillsList.DoLayoutList();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void BuildSkillsList()
        {
            skillsList = new ReorderableList(
                librarySerializedObject,
                skillsProperty,
                true, true, true, true
            );

            skillsList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "技能列表");
            };

            skillsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = skillsProperty.GetArrayElementAtIndex(index);
                var skillConfig = element.objectReferenceValue as SkillConfig;

                if (skillConfig == null)
                {
                    EditorGUI.LabelField(rect, "空槽位");
                    return;
                }

                string displayName = $"[{skillConfig.skillId}] {skillConfig.skillName}";
                if (index == selectedSkillIndex)
                {
                    GUI.color = Color.cyan;
                    EditorGUI.LabelField(rect, displayName, EditorStyles.boldLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    EditorGUI.LabelField(rect, displayName);
                }
            };

            skillsList.onSelectCallback = (ReorderableList list) =>
            {
                selectedSkillIndex = list.index;
                LoadSelectedSkill();
            };

            skillsList.onAddCallback = (ReorderableList list) =>
            {
                librarySerializedObject.Update();
                skillsProperty.arraySize++;
                var newElement = skillsProperty.GetArrayElementAtIndex(skillsProperty.arraySize - 1);
                newElement.objectReferenceValue = CreateNewSkillAsset();
                librarySerializedObject.ApplyModifiedProperties();
                selectedSkillIndex = skillsProperty.arraySize - 1;
                LoadSelectedSkill();
            };

            skillsList.onRemoveCallback = (ReorderableList list) =>
            {
                if (selectedSkillIndex < 0 || selectedSkillIndex >= skillsProperty.arraySize) return;

                if (!EditorUtility.DisplayDialog("移除技能", "只从库列表移除引用，不删除资产文件。", "移除", "取消"))
                    return;

                librarySerializedObject.Update();
                skillsProperty.DeleteArrayElementAtIndex(selectedSkillIndex);
                librarySerializedObject.ApplyModifiedProperties();
                selectedSkillIndex = -1;
                skillSerializedObject = null;
            };
        }

        private SkillConfig CreateNewSkillAsset()
        {
            string folderPath = "Assets/SkillSystem/Skills";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string parent = "Assets/SkillSystem";
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", "SkillSystem");
                AssetDatabase.CreateFolder(parent, "Skills");
            }

            var newSkill = ScriptableObject.CreateInstance<SkillConfig>();
            newSkill.skillId = "new_skill";
            newSkill.skillName = "新技能";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/NewSkill.asset");
            AssetDatabase.CreateAsset(newSkill, path);
            AssetDatabase.SaveAssets();
            return newSkill;
        }

        #endregion

        #region 右栏：技能详情（组件内嵌，参数直接画在界面里）

        private void LoadSelectedSkill()
        {
            if (selectedSkillIndex < 0 || selectedSkillIndex >= skillsProperty.arraySize) return;

            var element = skillsProperty.GetArrayElementAtIndex(selectedSkillIndex);
            var skillConfig = element.objectReferenceValue as SkillConfig;
            if (skillConfig == null) return;

            skillSerializedObject = new SerializedObject(skillConfig);
            skillIdProperty = skillSerializedObject.FindProperty("skillId");
            skillNameProperty = skillSerializedObject.FindProperty("skillName");
            componentsProperty = skillSerializedObject.FindProperty("components");
            componentsList = null; // 强制重建组件列表
            frameEventsList = null;
            frameEventsProperty = null;

            // 帧事件挂在「播放动画」组件上 —— 选中技能就自动指到第一个，省得用户找不到表格
            selectedComponentIndex = FindFirstPlayAnimationIndex();
            if (selectedComponentIndex >= 0) RebuildFrameEventList();
        }

        private void DrawSkillInspector()
        {
            if (skillSerializedObject == null) return;

            skillSerializedObject.Update();

            EditorGUILayout.BeginVertical();
            componentsScroll = EditorGUILayout.BeginScrollView(componentsScroll);

            EditorGUILayout.LabelField("技能配置", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(skillIdProperty, new GUIContent("技能 ID"));
            EditorGUILayout.PropertyField(skillNameProperty, new GUIContent("技能名称"));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            if (componentsList == null)
                BuildComponentsList();

            componentsList.DoLayoutList();

            // 阶段 5：帧事件表格 + 动画预览（仅选中"播放动画"组件时显示）
            showHitBoxGizmo = EditorGUILayout.ToggleLeft(
                "在 Scene 视图显示判定盒（选中「判定窗口」组件后可拖手柄改大小/偏移）", showHitBoxGizmo);
            DrawStateNameCheck();     // P0-2(A)：用玩家动画机校验 stateName 是否存在
            DrawFrameEventTable();
            DrawAnimationPreviewPanel();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // 内嵌数据全部归技能资产管，直接写回 + 标脏
            skillSerializedObject.ApplyModifiedProperties();
            if (selectedSkillIndex >= 0 && selectedSkillIndex < skillsProperty.arraySize)
            {
                var element = skillsProperty.GetArrayElementAtIndex(selectedSkillIndex);
                if (element.objectReferenceValue != null)
                    EditorUtility.SetDirty(element.objectReferenceValue);
            }
        }

        private void BuildComponentsList()
        {
            componentsList = new ReorderableList(
                skillSerializedObject,
                componentsProperty,
                true, true, true, true
            );

            componentsList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "组件列表（顺序影响执行，点击 + 添加基础组件）");
            };

            // 内嵌组件（SerializeReference）：逐字段绘制，frameEvents 除外（由下方专属表格编辑）
            componentsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = componentsProperty.GetArrayElementAtIndex(index);
                var component = element.managedReferenceValue as SkillComponent;

                if (component == null)
                {
                    EditorGUI.LabelField(rect, "空槽位");
                    return;
                }

                string label = string.IsNullOrEmpty(component.displayName)
                    ? component.GetType().Name
                    : $"{component.displayName}（{component.GetType().Name}）";
                EditorGUI.LabelField(
                    new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                    label, EditorStyles.boldLabel);

                var prop = element.Copy();
                var end = prop.GetEndProperty();
                bool enter = true;
                float y = rect.y + EditorGUIUtility.singleLineHeight + 2;

                while (prop.NextVisible(enter))
                {
                    enter = false;
                    if (SerializedProperty.EqualContents(prop, end)) break;
                    if (prop.name == "m_Script") continue;
                    if (prop.name == "frameEvents") continue;   // 帧事件走下方专属表格

                    float h = EditorGUI.GetPropertyHeight(prop, true);
                    if (prop.name == "attachNodeName")
                    {
                        DrawAttachNodeField(new Rect(rect.x + 15, y, rect.width - 15, h), prop);
                        y += h + 2;
                        continue;
                    }
                    bool readOnly = prop.name == "baseClip";      // 自动填充字段，禁止手改
                    if (readOnly) EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.PropertyField(new Rect(rect.x + 15, y, rect.width - 15, h), prop, GetFieldLabel(prop), true);
                    if (readOnly) EditorGUI.EndDisabledGroup();
                    y += h + 2;
                }
            };

            componentsList.elementHeightCallback = (int index) =>
            {
                var element = componentsProperty.GetArrayElementAtIndex(index);
                if (element.managedReferenceValue == null)
                    return EditorGUIUtility.singleLineHeight + 4;

                var prop = element.Copy();
                var end = prop.GetEndProperty();
                float height = EditorGUIUtility.singleLineHeight + 4;
                bool enter = true;
                while (prop.NextVisible(enter))
                {
                    enter = false;
                    if (SerializedProperty.EqualContents(prop, end)) break;
                    if (prop.name == "m_Script") continue;
                    if (prop.name == "frameEvents") continue;
                    height += EditorGUI.GetPropertyHeight(prop, true) + 2;
                }
                return height;
            };

            componentsList.onSelectCallback = (ReorderableList list) =>
            {
                selectedComponentIndex = list.index;
                RebuildFrameEventList();
            };

            componentsList.onAddCallback = (ReorderableList list) =>
            {
                ShowAddComponentMenu();
            };

            componentsList.onRemoveCallback = (ReorderableList list) =>
            {
                if (componentsProperty.arraySize == 0) return;
                skillSerializedObject.Update();
                componentsProperty.DeleteArrayElementAtIndex(list.index);
                skillSerializedObject.ApplyModifiedProperties();
                selectedComponentIndex = -1;
                RebuildFrameEventList();
            };
        }

        /// <summary>
        /// 组件里几个"名字看不出含义"的字段，给中文标签 + 悬停说明。
        /// （字段名本身不能改，改了会破坏已有资产的序列化）
        /// </summary>
        private static readonly Dictionary<string, GUIContent> FieldLabelOverrides = new Dictionary<string, GUIContent>
        {
            { "displayName",   new GUIContent("displayName（显示名）", "只影响编辑器里显示，可留空") },
            { "stateName",     new GUIContent("stateName（动画机状态名）", "必须是 Animator 里的【状态名】，不是 clip 名。写错的话按 J 动作不会变。") },
            { "overrideClip",  new GUIContent("overrideClip（★动画覆盖·可选）", "填了 = 运行时用 AnimatorOverrideController 把这个状态的动画换成它（策划换动画不用碰 Animator）。留空 = 播状态原本的动画。") },
            { "baseClip",      new GUIContent("baseClip（原动画·自动填充）", "stateName 状态在【基础 controller】里原本绑的 clip。由编辑器自动解析写入，运行时用它做覆盖映射 —— 请不要手改。") },
            { "crossFade",     new GUIContent("crossFade（过渡秒数）", "0.1 = 平滑过渡；0.02~0.05 = 瞬间切换（振刀/受击这类）") },
            { "duration",      new GUIContent("duration（★技能总时长·秒）", "整个技能跑这么久就结束。帧事件的帧号必须落在 duration×帧率 之内，否则永远不会触发。填 0 = 不自动结束，一直播到被 InterruptSkill / 切技能打断（架势、循环类用）。") },
            { "startFrame",    new GUIContent("startFrame（判定起始帧）", "判定窗口的起始帧号") },
            { "endFrame",      new GUIContent("endFrame（判定结束帧）", "判定窗口的结束帧号") },
            { "attachNodeName", new GUIContent("attachNodeName（★绑定节点名）", "填骨骼名字（如 J_Bip_R_Hand）或相对路径。留空 = 挂在角色根节点（判定不跟动画走 → 隔空）。ScriptableObject 不能引用场景对象，所以这里存名字。") },
            { "shape",         new GUIContent("shape（检测形状）", "Sphere 球 / Capsule 胶囊（刀剑，沿节点前向）/ Box 盒（锤）") },
            { "radius",        new GUIContent("radius（检测半径）", "角色高 1.6m 时，单手武器建议 0.15~0.25；别再用 1.5 那种大球") },
            { "capsuleLength", new GUIContent("capsuleLength（刃长）", "胶囊沿节点前向的总长度，单手剑约 0.6~0.8") },
            { "offset",        new GUIContent("offset（相对节点的偏移）", "从节点出发、沿节点朝向的偏移量；用来把判定盒挪到刀身中部") },
            { "rotationOffset", new GUIContent("rotationOffset（★朝向对齐·欧拉角）", "判定盒相对绑定节点的旋转偏移。骨骼轴向通常不沿刀身，用这个转过来对准武器（最直观是拖 Scene 视图里的旋转圈）。") },
            { "blockedDamageScale", new GUIContent("blockedDamageScale（格挡减伤倍率）", "被格挡时只吃这个比例的伤害，0.2 = 只吃 20%") },
            { "blockSoundIdPrefix", new GUIContent("blockSoundIdPrefix（格挡音效前缀）", "被格挡时播这个前缀下的音效，如 block_") },
            { "parrySoundIdPrefix", new GUIContent("parrySoundIdPrefix（弹反音效前缀）", "被弹反时播这个前缀下的音效，如 parry_") },
            { "blockedHitStopDuration", new GUIContent("blockedHitStopDuration（格挡/弹反顿帧·秒）", "格挡比命中的顿帧更长，手感更「重」") },
            { "damage",        new GUIContent("damage（伤害数值）", "") },
            { "soundIdPrefix", new GUIContent("soundIdPrefix（命中音效前缀）", "命中时按这个前缀随机播一条，例：hit_") },
            { "hitStopDuration", new GUIContent("hitStopDuration（顿帧时长·秒）", "命中瞬间画面暂停的现实秒数") },
            { "hitStopTimeScale", new GUIContent("hitStopTimeScale（顿帧缩放）", "0.1 = 放到 10% 速度") },
        };

        private static GUIContent GetFieldLabel(SerializedProperty prop)
        {
            if (prop != null && FieldLabelOverrides.TryGetValue(prop.name, out var content))
                return content;
            return new GUIContent(prop != null ? prop.displayName : "?", prop != null ? prop.tooltip : "");
        }

        // ──────────────── B：绑定节点下拉 + 判定盒可视化 ────────────────

        /// <summary>
        /// 绑定节点用「下拉选骨骼」而不是拖 Transform。
        /// 原因：ScriptableObject 资产不允许引用场景对象 —— 拖进去也会被清空。
        /// </summary>
        private void DrawAttachNodeField(Rect rect, SerializedProperty prop)
        {
            Rect fieldRect = EditorGUI.PrefixLabel(rect, GetFieldLabel(prop));
            var options = GetBonePathOptions();

            if (options.Count == 0)
            {
                // 拿不到骨骼（没指定预览目标）→ 退化为手输
                prop.stringValue = EditorGUI.TextField(fieldRect, prop.stringValue ?? "");
                return;
            }

            var display = new List<string> { "（不绑定 = 跟随角色根节点）" };
            display.AddRange(options);

            int cur = options.IndexOf(prop.stringValue ?? "");
            int newIdx = EditorGUI.Popup(fieldRect, cur < 0 ? 0 : cur + 1, display.ToArray());
            if (newIdx != cur + 1)
                prop.stringValue = newIdx == 0 ? "" : options[newIdx - 1];
        }

        /// <summary>预览目标身上所有骨骼的相对路径（手/武器相关排前面）</summary>
        private List<string> GetBonePathOptions()
        {
            var list = new List<string>();
            var anim = ResolvePreviewTarget();
            if (anim == null) return list;

            Transform root = anim.transform;
            foreach (var t in anim.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = GetRelativePath(root, t);
                if (!string.IsNullOrEmpty(path) && !list.Contains(path)) list.Add(path);
            }

            list.Sort((a, b) =>
            {
                int pa = BonePriority(a), pb = BonePriority(b);
                return pa != pb ? pa.CompareTo(pb) : string.CompareOrdinal(a, b);
            });
            return list;
        }

        private static int BonePriority(string path)
        {
            if (path.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (path.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            return 2;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                parts.Insert(0, cur.name);
                cur = cur.parent;
            }
            return cur == root ? string.Join("/", parts.ToArray()) : "";
        }

        private HitWindowComponent GetSelectedHitWindowComponent()
        {
            if (skillSerializedObject == null || componentsProperty == null) return null;
            if (selectedComponentIndex < 0 || selectedComponentIndex >= componentsProperty.arraySize) return null;
            return componentsProperty.GetArrayElementAtIndex(selectedComponentIndex).managedReferenceValue as HitWindowComponent;
        }

        // ── SceneView：画判定盒线框 + 拖手柄改大小/偏移 ──

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showHitBoxGizmo) return;

            var hit = GetSelectedHitWindowComponent();
            if (hit == null) return;

            var anim = ResolvePreviewTarget();
            if (anim == null) return;

            // 没绑定节点时用角色根节点（和运行时行为一致）
            Transform node = HitWindowComponent.ResolveNodeStatic(anim.transform, hit.attachNodeName);
            if (node == null) node = anim.transform;

            Vector3 nodePos = node.position;
            Quaternion nodeRot = node.rotation;
            Vector3 center = hit.GetHitCenter(node);          // 偏移在节点坐标系里算
            Quaternion shapeRot = hit.GetHitRotation(node);   // 含"朝向对齐"偏移

            DrawHitBoxWire(hit, center, shapeRot);
            DrawHitBoxHandles(hit, nodePos, nodeRot, shapeRot, center);
        }

        private static void DrawHitBoxWire(HitWindowComponent hit, Vector3 center, Quaternion rot)
        {
            var oldColor = Handles.color;
            var oldMatrix = Handles.matrix;

            Handles.color = new Color(1f, 0.35f, 0.2f, 0.95f);
            Handles.matrix = Matrix4x4.TRS(center, rot, Vector3.one);

            switch (hit.shape)
            {
                case HitShape.Sphere:
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, hit.radius);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.right, hit.radius);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, hit.radius);
                    break;

                case HitShape.Capsule:
                    float half = hit.capsuleLength * 0.5f;
                    Vector3 back = new Vector3(0, 0, -half);
                    Vector3 front = new Vector3(0, 0, half);
                    Handles.DrawWireDisc(back, Vector3.forward, hit.radius);
                    Handles.DrawWireDisc(front, Vector3.forward, hit.radius);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f;
                        Vector3 o = new Vector3(Mathf.Cos(a) * hit.radius, Mathf.Sin(a) * hit.radius, 0f);
                        Handles.DrawLine(back + o, front + o);
                    }
                    break;

                case HitShape.Box:
                    Handles.DrawWireCube(Vector3.zero, hit.boxHalfExtents * 2f);
                    break;
            }

            Handles.matrix = oldMatrix;
            Handles.color = oldColor;
        }

        private void DrawHitBoxHandles(HitWindowComponent hit, Vector3 nodePos, Quaternion nodeRot,
                                       Quaternion shapeRot, Vector3 center)
        {
            // ① 半径手柄：沿判定盒「上方向」滑动，往外拖 = 变大
            EditorGUI.BeginChangeCheck();
            Vector3 handlePos = center + shapeRot * (Vector3.up * hit.radius);
            Vector3 moved = Handles.Slider(handlePos, shapeRot * Vector3.up, 0.15f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
                SetSelectedFloat("radius", Mathf.Max(0.01f, Vector3.Distance(center, moved)));

            // ② 偏移手柄（拖中心点）
            //    ⚠ offset 定义在【节点坐标系】里，所以换算用 nodeRot，不是判定盒朝向
            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, shapeRot);
            if (EditorGUI.EndChangeCheck())
                SetSelectedVector3("offset", Quaternion.Inverse(nodeRot) * (newCenter - nodePos));

            // ③ 朝向手柄（红/绿/蓝圈）：把判定盒转到对准武器方向
            EditorGUI.BeginChangeCheck();
            Quaternion newShapeRot = Handles.RotationHandle(shapeRot, center);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion local = Quaternion.Inverse(nodeRot) * newShapeRot;
                SetSelectedVector3("rotationOffset", NormalizeEuler(local.eulerAngles));
            }
        }

        /// <summary>欧拉角夹到 -180~180，避免手柄拖出一堆 359.9 这种数</summary>
        private static Vector3 NormalizeEuler(Vector3 e)
        {
            return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        }

        private void SetSelectedFloat(string fieldName, float value)
        {
            var prop = GetSelectedComponentField(fieldName);
            if (prop == null) return;

            skillSerializedObject.Update();
            prop.floatValue = value;
            skillSerializedObject.ApplyModifiedProperties();
            if (skillSerializedObject.targetObject != null)
                EditorUtility.SetDirty(skillSerializedObject.targetObject);
            SceneView.RepaintAll();
        }

        private void SetSelectedVector3(string fieldName, Vector3 value)
        {
            var prop = GetSelectedComponentField(fieldName);
            if (prop == null) return;

            skillSerializedObject.Update();
            prop.vector3Value = value;
            skillSerializedObject.ApplyModifiedProperties();
            if (skillSerializedObject.targetObject != null)
                EditorUtility.SetDirty(skillSerializedObject.targetObject);
            SceneView.RepaintAll();
        }

        private SerializedProperty GetSelectedComponentField(string fieldName)
        {
            if (skillSerializedObject == null || componentsProperty == null) return null;
            if (selectedComponentIndex < 0 || selectedComponentIndex >= componentsProperty.arraySize) return null;
            return componentsProperty.GetArrayElementAtIndex(selectedComponentIndex).FindPropertyRelative(fieldName);
        }

        private void ShowAddComponentMenu()
        {
            GenericMenu menu = new GenericMenu();
            foreach (var kvp in ComponentTypes)
            {
                string label = kvp.Key;
                menu.AddItem(new GUIContent($"添加 {label}"), false, () => AddComponentOfType(kvp.Value));
            }
            menu.ShowAsContext();
        }

        private void AddComponentOfType(Type componentType)
        {
            skillSerializedObject.Update();
            int index = componentsProperty.arraySize;
            componentsProperty.arraySize++;
            var element = componentsProperty.GetArrayElementAtIndex(index);
            element.managedReferenceValue = Activator.CreateInstance(componentType);
            element.isExpanded = true;   // 新组件默认展开，字段直接可见可改
            skillSerializedObject.ApplyModifiedProperties();

            selectedComponentIndex = index;
            RebuildFrameEventList();
        }

        #endregion

        #region 阶段 5：帧事件表格 + 编辑器试听 + 动画预览

        /// <summary>当前选中的组件是否为"播放动画"；是则重建帧事件列表</summary>
        private void RebuildFrameEventList()
        {
            frameEventsList = null;
            frameEventsProperty = null;

            if (selectedComponentIndex < 0 || selectedComponentIndex >= componentsProperty.arraySize) return;
            var element = componentsProperty.GetArrayElementAtIndex(selectedComponentIndex);
            var component = element.managedReferenceValue as PlayAnimationComponent;
            if (component == null) return;

            frameEventsProperty = element.FindPropertyRelative("frameEvents");
            if (frameEventsProperty == null) return;

            frameEventsList = new ReorderableList(skillSerializedObject, frameEventsProperty, true, true, true, true);

            frameEventsList.drawHeaderCallback = (Rect rect) =>
            {
                float frameW = 45f, btnW = 40f, sliderW = 90f, playW = 28f, pad = 4f;
                float x = rect.x;
                float idW = rect.width - (frameW * 2 + btnW + sliderW + playW + pad * 5);
                EditorGUI.LabelField(new Rect(x + frameW, rect.y, frameW, rect.height), "帧号");
                x += frameW * 2 + pad;
                EditorGUI.LabelField(new Rect(x, rect.y, idW, rect.height), "音效ID（▾ 从库选择）");
                x += idW + btnW + pad;
                EditorGUI.LabelField(new Rect(x, rect.y, sliderW, rect.height), "音量");
                x += sliderW + pad;
                EditorGUI.LabelField(new Rect(x, rect.y, playW, rect.height), "试听");
            };

            frameEventsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var evt = frameEventsProperty.GetArrayElementAtIndex(index);
                var frameProp = evt.FindPropertyRelative("frame");
                var idProp = evt.FindPropertyRelative("soundId");
                var volumeProp = evt.FindPropertyRelative("volume");

                const float frameW = 45f, btnW = 40f, sliderW = 90f, playW = 28f, pad = 4f;
                float x = rect.x;

                EditorGUI.LabelField(new Rect(x, rect.y, frameW, rect.height), "帧:");
                x += frameW;
                frameProp.intValue = EditorGUI.IntField(new Rect(x, rect.y, frameW, rect.height), frameProp.intValue);
                x += frameW + pad;

                float idW = rect.width - (frameW * 2 + btnW + sliderW + playW + pad * 5);
                idProp.stringValue = EditorGUI.TextField(new Rect(x, rect.y, idW, rect.height), idProp.stringValue);
                x += idW + pad;

                if (GUI.Button(new Rect(x, rect.y, btnW, rect.height), "选择▾"))
                {
                    ShowSoundIdMenu(index);
                }
                x += btnW + pad;

                volumeProp.floatValue = EditorGUI.Slider(new Rect(x, rect.y, sliderW, rect.height), volumeProp.floatValue, 0f, 1f);
                x += sliderW + pad;

                AudioClip clip = GetClip(idProp.stringValue);
                GUI.enabled = clip != null;
                if (GUI.Button(new Rect(x, rect.y, playW, rect.height), "▶"))
                {
                    PlayClipInEditor(clip);
                }
                GUI.enabled = true;
            };

            frameEventsList.elementHeightCallback = (int index) => EditorGUIUtility.singleLineHeight + 2;

            frameEventsList.onAddCallback = (ReorderableList list) =>
            {
                skillSerializedObject.Update();
                frameEventsProperty.arraySize++;
                skillSerializedObject.ApplyModifiedProperties();
            };

            frameEventsList.onRemoveCallback = (ReorderableList list) =>
            {
                if (frameEventsProperty.arraySize == 0) return;
                skillSerializedObject.Update();
                frameEventsProperty.DeleteArrayElementAtIndex(list.index);
                skillSerializedObject.ApplyModifiedProperties();
            };
        }

        /// <summary>
        /// 帧事件挂在「播放动画」组件上。用户什么都没选时自动选中第一个，
        /// 省得他猜"为什么添加事件按钮是灰的"。用户明确选了别的组件则不抢。
        /// </summary>
        private void EnsureFrameEventTarget()
        {
            if (frameEventsList != null) return;
            if (componentsProperty == null || skillSerializedObject == null) return;
            if (selectedComponentIndex >= 0) return;

            int index = FindFirstPlayAnimationIndex();
            if (index < 0) return;
            selectedComponentIndex = index;
            RebuildFrameEventList();
        }

        /// <summary>技能里第一个「播放动画」组件的下标；没有返回 -1</summary>
        private int FindFirstPlayAnimationIndex()
        {
            var all = GetPlayAnimationIndices();
            return all.Count > 0 ? all[0] : -1;
        }

        /// <summary>技能里所有「播放动画」组件的下标</summary>
        private List<int> GetPlayAnimationIndices()
        {
            var list = new List<int>();
            if (componentsProperty == null) return list;

            for (int i = 0; i < componentsProperty.arraySize; i++)
            {
                if (componentsProperty.GetArrayElementAtIndex(i).managedReferenceValue is PlayAnimationComponent)
                    list.Add(i);
            }
            return list;
        }

        private void DrawFrameEventTable()
        {
            EnsureFrameEventTarget();

            if (frameEventsList == null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "帧事件是挂在「播放动画」组件上的列表（没有动画就谈不上「第几帧」）。\n" +
                    "→ 先在下面的组件列表里选中一个「播放动画」组件；还没有的话，点组件列表的 + 添加一个。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("帧事件表格", EditorStyles.boldLabel);

            // 一个技能里可能有多个「播放动画」组件 → 显示当前在编哪个，并可切换
            var animIndices = GetPlayAnimationIndices();
            if (animIndices.Count > 1)
            {
                var labels = new List<string>();
                foreach (var i in animIndices)
                {
                    var c = componentsProperty.GetArrayElementAtIndex(i).managedReferenceValue as PlayAnimationComponent;
                    labels.Add($"#{i + 1} {c.stateName}");
                }
                int cur = Mathf.Max(0, animIndices.IndexOf(selectedComponentIndex));
                int sel = EditorGUILayout.Popup(cur, labels.ToArray(), GUILayout.Width(160));
                if (sel != cur)
                {
                    selectedComponentIndex = animIndices[sel];
                    RebuildFrameEventList();
                }
            }
            else if (animIndices.Count == 1)
            {
                var c = componentsProperty.GetArrayElementAtIndex(animIndices[0]).managedReferenceValue as PlayAnimationComponent;
                EditorGUILayout.LabelField($"（挂在组件 #{animIndices[0] + 1}「{c.stateName}」上）", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新音效ID", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                LoadSoundData();
            }
            EditorGUILayout.EndHorizontal();

            frameEventsList.DoLayoutList();

            if (soundIds.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没收集到任何音效ID：先去音效编辑器把音频录进 SoundLibrary，再点上面「刷新音效ID」。",
                    MessageType.Warning);
            }

            // ── P0-9 运行时可用性检查：编辑器能试听 ≠ 游戏里能播 ──
            if (!runtimeSoundManagerFound)
            {
                EditorGUILayout.HelpBox(
                    "场景里没找到 SoundManager，无法校验这些音效ID在游戏里能不能播。",
                    MessageType.Warning);
                return;
            }

            var comp = GetSelectedPlayAnimationComponent();
            if (comp == null || comp.frameEvents == null || comp.frameEvents.Count == 0) return;

            var missing = new List<string>();
            foreach (var evt in comp.frameEvents)
            {
                if (evt == null || string.IsNullOrEmpty(evt.soundId)) continue;
                if (!runtimeSoundIds.Contains(evt.soundId) && !missing.Contains(evt.soundId))
                    missing.Add(evt.soundId);
            }

            string libs = runtimeLibraryNames.Count > 0 ? string.Join("、", runtimeLibraryNames) : "（空）";
            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"这些音效ID游戏里查不到：{string.Join("、", missing)}\n" +
                    $"运行时 SoundManager 挂载的库是：{libs}（共 {runtimeSoundIds.Count} 条音效）。\n" +
                    "→ 游戏里会静默无声，Console 会打 [SoundManager] 未找到音效。\n" +
                    "→ 修法：把 ID 改成库里已有的，或把对应 SoundLibrary 挂到场景 SoundManager 的 Libraries 上。",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"运行时校验通过（SoundManager 挂载：{libs}，共 {runtimeSoundIds.Count} 条音效）。",
                    MessageType.None);
            }
        }

        /// <summary>
        /// 音效ID选择菜单。
        /// ⚠ 只传下标，不传 SerializedProperty —— 菜单回调是在下一次事件里才执行的，
        ///   那时候旧的 SerializedProperty 已经失效，写进去会静默失败（"选了没反应"）。
        /// </summary>
        private void ShowSoundIdMenu(int eventIndex)
        {
            var menu = new GenericMenu();

            if (soundIds.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（没有可用音效ID，先点「刷新音效ID」）"));
                menu.ShowAsContext();
                return;
            }

            string currentId = null;
            if (frameEventsProperty != null && eventIndex >= 0 && eventIndex < frameEventsProperty.arraySize)
            {
                var p = frameEventsProperty.GetArrayElementAtIndex(eventIndex).FindPropertyRelative("soundId");
                if (p != null) currentId = p.stringValue;
            }

            foreach (var id in soundIds)
            {
                string captured = id;
                menu.AddItem(new GUIContent(captured), captured == currentId, () => ApplySoundId(eventIndex, captured));
            }
            menu.ShowAsContext();
        }

        /// <summary>菜单回调真正落地的地方：先 Update，按下标重新取属性，再 Apply</summary>
        private void ApplySoundId(int eventIndex, string soundId)
        {
            if (skillSerializedObject == null || frameEventsProperty == null) return;

            skillSerializedObject.Update();
            if (eventIndex < 0 || eventIndex >= frameEventsProperty.arraySize)
            {
                Debug.LogWarning($"[SkillEditor] 帧事件下标 {eventIndex} 已失效（列表被改过），请重新选择。");
                return;
            }

            var p = frameEventsProperty.GetArrayElementAtIndex(eventIndex).FindPropertyRelative("soundId");
            if (p != null) p.stringValue = soundId;

            skillSerializedObject.ApplyModifiedProperties();
            if (skillSerializedObject.targetObject != null)
                EditorUtility.SetDirty(skillSerializedObject.targetObject);

            Repaint();
        }

        /// <summary>从项目里所有 SoundLibrary 资产收集 soundId 与 clip（下拉数据源 + 试听）</summary>
        private void LoadSoundData()
        {
            soundIds.Clear();
            soundIdToClip.Clear();

            string[] guids = AssetDatabase.FindAssets("t:SoundLibrary");
            foreach (var guid in guids)
            {
                var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (lib == null || lib.sounds == null) continue;
                foreach (var item in lib.sounds)
                {
                    if (item == null || string.IsNullOrEmpty(item.soundID)) continue;
                    if (!soundIds.Contains(item.soundID)) soundIds.Add(item.soundID);
                    if (item.clip != null && !soundIdToClip.ContainsKey(item.soundID))
                        soundIdToClip[item.soundID] = item.clip;
                }
            }
            soundIds.Sort();
            LoadRuntimeSoundIds();
            Debug.Log($"[SkillEditor] 已收集 {soundIds.Count} 个音效 ID（其中运行时可用 {runtimeSoundIds.Count} 个）");
        }

        /// <summary>
        /// 收集"运行时真正能解析"的音效 ID —— 只数场景里 SoundManager 挂载的那些库。
        /// 编辑器下拉用的是全项目所有 SoundLibrary（AssetDatabase.FindAssets），
        /// 后者包含"游戏根本没挂上"的库 —— 这就是"编辑器能试听、游戏里没声音"的常见原因。
        /// </summary>
        private void LoadRuntimeSoundIds()
        {
            runtimeSoundIds.Clear();
            runtimeLibraryNames.Clear();
            runtimeSoundManagerFound = false;

            var sm = UnityEngine.Object.FindObjectOfType<SoundManager>(true);
            if (sm == null) return;
            runtimeSoundManagerFound = true;

            var so = new SerializedObject(sm);
            var arr = so.FindProperty("_libraries");
            if (arr == null) return;

            for (int i = 0; i < arr.arraySize; i++)
            {
                var lib = arr.GetArrayElementAtIndex(i).objectReferenceValue as SoundLibrary;
                if (lib == null) continue;

                runtimeLibraryNames.Add(lib.name);
                if (lib.sounds == null) continue;

                foreach (var item in lib.sounds)
                {
                    if (item == null || string.IsNullOrEmpty(item.soundID) || item.clip == null) continue;
                    runtimeSoundIds.Add(item.soundID);
                }
            }
        }

        private AudioClip GetClip(string soundId)
        {
            if (string.IsNullOrEmpty(soundId)) return null;
            return soundIdToClip.TryGetValue(soundId, out var clip) ? clip : null;
        }

        /// <summary>编辑器内试听（同 AudioBrowser：临时 AudioSource，退出窗口时销毁）</summary>
        private void PlayClipInEditor(AudioClip clip)
        {
            if (clip == null) return;
            if (previewSource == null)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("_SkillEditorSfxPreview", HideFlags.HideAndDontSave);
                previewSource = go.AddComponent<AudioSource>();
            }
            previewSource.clip = clip;
            previewSource.Play();
        }

        private void DrawAnimationPreviewPanel()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("动画预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "预览只是【配帧工具】：拖时间轴看第几帧、听音效响没响。\n" +
                "它不会存进技能、也不影响游戏 —— 游戏里播哪个动画由组件里的 stateName 决定。",
                MessageType.None);

            DrawPreviewTargetField();

            previewClip = (AnimationClip)EditorGUILayout.ObjectField("预览动画", previewClip, typeof(AnimationClip), false);
            WarnIfPreviewClipMismatch();

            if (previewClip == null || previewClip.length <= 0f)
            {
                if (previewPlaying || previewInAnimationMode) StopPreview();
                EditorGUILayout.HelpBox("拖入一个 AnimationClip（仅辅助配帧，不保存进技能）。", MessageType.Info);
                return;
            }

            if (previewTime > previewClip.length) previewTime = previewClip.length;

            // 时间轴（第一步：内置 Slider + 帧吸附；第二步再换成自绘控件）
            EditorGUI.BeginChangeCheck();
            float dragged = EditorGUILayout.Slider("时间轴", previewTime, 0f, previewClip.length);
            if (EditorGUI.EndChangeCheck())
            {
                previewPlaying = false;
                previewTime = SkillFrameUtil.SnapTimeToFrame(previewClip, dragged);
                SamplePreview();
            }

            int currentFrame = SkillFrameUtil.TimeToFrame(previewClip, previewTime);
            int totalFrames = SkillFrameUtil.TotalFrames(previewClip);
            EditorGUILayout.LabelField(
                $"当前帧：{currentFrame} / 总帧：{totalFrames}    帧率：{SkillFrameUtil.GetFrameRate(previewClip):0.##} fps    " +
                $"时间：{previewTime:0.###} / {previewClip.length:0.###} s");

            DrawPreviewTransport();

            bool canAddFrameEvent = frameEventsProperty != null;
            using (new EditorGUI.DisabledScope(!canAddFrameEvent))
            {
                if (GUILayout.Button($"在第 {currentFrame} 帧添加事件", GUILayout.Height(24)))
                    AddFrameEventAtFrame(currentFrame);
            }
            if (!canAddFrameEvent)
            {
                EditorGUILayout.HelpBox(
                    "帧事件挂在「播放动画」组件上：请先在组件列表里选中它（还没有就点组件列表的 + 添加一个）。",
                    MessageType.Warning);
            }

            if (previewPlaying) Repaint();
        }

        // ──────────────── 预览目标（P0-1）────────────────

        private void DrawPreviewTargetField()
        {
            EditorGUILayout.BeginHorizontal();
            previewTarget = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("预览目标", "留空 = 自动找场景里 tag=Player 的 Animator"),
                previewTarget, typeof(Animator), true);
            if (GUILayout.Button("自动找主角", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                StopPreview();
                previewTarget = null;
                autoPreviewTarget = FindPlayerAnimator();
            }
            EditorGUILayout.EndHorizontal();

            var resolved = ResolvePreviewTarget();
            if (resolved == null)
            {
                EditorGUILayout.HelpBox(
                    "找不到预览对象：场景里没有 tag=Player 的 Animator。请把主角的 Animator 拖到「预览目标」。",
                    MessageType.Warning);
                return;
            }

            string source = previewTarget != null
                ? "手动指定"
                : (IsPlayerRoot(resolved) ? "自动：tag=Player" : "自动：退化为场景第一个 Animator");
            EditorGUILayout.LabelField($"正在预览：{resolved.gameObject.name}（{source}）", EditorStyles.miniLabel);

            if (previewTarget == null && !IsPlayerRoot(resolved))
            {
                EditorGUILayout.HelpBox(
                    $"没找到 tag=Player 的对象，正在预览「{resolved.gameObject.name}」——这多半不是主角，请手动指定。",
                    MessageType.Warning);
            }
        }

        private static bool IsPlayerRoot(Animator animator)
        {
            return animator != null && animator.gameObject != null
                   && animator.transform.root.CompareTag("Player");
        }

        private Animator ResolvePreviewTarget()
        {
            if (previewTarget != null) return previewTarget;
            if (autoPreviewTarget != null && autoPreviewTarget.gameObject != null) return autoPreviewTarget;
            autoPreviewTarget = FindPlayerAnimator();
            return autoPreviewTarget;
        }

        /// <summary>优先找 tag=Player 的 Animator；找不到退化为场景第一个 Animator（界面会明确警告）</summary>
        private static Animator FindPlayerAnimator()
        {
            Animator fallback = null;
            foreach (var a in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (a == null || a.gameObject == null) continue;
                var go = a.gameObject;
                if (EditorUtility.IsPersistent(go)) continue;   // 排除项目资产（prefab / 模型文件）
                if (!go.scene.IsValid()) continue;              // 排除非场景对象
                if (IsPlayerRoot(a)) return a;
                if (fallback == null) fallback = a;
            }
            return fallback;
        }

        // ──────────────── 简易播放控制（P0-3 第一步）────────────────

        private void DrawPreviewTransport()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⏮", GUILayout.Width(32))) SetPreviewTime(0f);
            if (GUILayout.Button("◀|", GUILayout.Width(32))) StepPreviewFrame(-1);
            if (GUILayout.Button(previewPlaying ? "⏸ 暂停" : "▶ 播放", GUILayout.Width(84)))
            {
                previewPlaying = !previewPlaying;
                if (previewPlaying)
                {
                    previewLastTick = EditorApplication.timeSinceStartup;
                    SamplePreview();
                }
            }
            if (GUILayout.Button("|▶", GUILayout.Width(32))) StepPreviewFrame(1);
            if (GUILayout.Button("⏭", GUILayout.Width(32))) SetPreviewTime(previewClip.length);
            previewLoop = GUILayout.Toggle(previewLoop, "循环", GUILayout.Width(50));
            previewAuditionSfx = GUILayout.Toggle(previewAuditionSfx, "试听帧事件", GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("还原姿态 / 退出预览", EditorStyles.miniButton, GUILayout.Width(140)))
                StopPreview();
            EditorGUILayout.EndHorizontal();
        }

        private void StepPreviewFrame(int delta)
        {
            if (previewClip == null) return;
            previewPlaying = false;
            int frame = SkillFrameUtil.TimeToFrame(previewClip, previewTime) + delta;
            frame = Mathf.Clamp(frame, 0, Mathf.Max(0, SkillFrameUtil.TotalFrames(previewClip)));
            SetPreviewTime(SkillFrameUtil.FrameToTime(previewClip, frame));
        }

        private void SetPreviewTime(float time)
        {
            if (previewClip == null) return;
            previewPlaying = false;
            previewTime = Mathf.Clamp(time, 0f, previewClip.length);
            SamplePreview();
            Repaint();
        }

        /// <summary>由 EditorApplication.update 驱动：推进时间 → 采样 → 重绘</summary>
        private void OnEditorUpdate()
        {
            if (Application.isPlaying)
            {
                if (previewPlaying || previewInAnimationMode) StopPreview();
                return;
            }
            if (!previewPlaying || previewClip == null || previewClip.length <= 0f) return;

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - previewLastTick);
            previewLastTick = now;
            if (dt <= 0f) return;

            previewTime += dt;
            if (previewTime >= previewClip.length)
            {
                if (previewLoop) previewTime = Mathf.Repeat(previewTime, previewClip.length);
                else { previewTime = previewClip.length; previewPlaying = false; }
            }

            SamplePreview();

            if (now - previewLastRepaint >= PreviewRepaintInterval)
            {
                previewLastRepaint = now;
                Repaint();
            }
        }

        /// <summary>停掉播放并还原场景姿态（StopAnimationMode 会恢复预览前的状态）</summary>
        private void StopPreview()
        {
            previewPlaying = false;
            previewFrameCursor = -1;
            ExitAnimationMode();
            lastSampledTarget = null;
            Repaint();
        }

        /// <summary>
        /// 播放/拖动时间轴时，播放头"向前跨过"某个帧事件 → 在编辑器里试听该音效。
        /// 规则与运行时一致（frame &gt; 上次帧 &amp;&amp; frame &lt;= 本次帧），
        /// 所以你在编辑器里听到的位置，就是游戏里应该响的位置。
        /// </summary>
        private void AuditionFrameEvents()
        {
            if (previewClip == null) return;

            int frame = SkillFrameUtil.TimeToFrame(previewClip, previewTime);

            if (previewFrameCursor < 0)
            {
                previewFrameCursor = frame;
                return;
            }

            int span = frame - previewFrameCursor;

            // 只处理"向前、且跨度正常"的移动；一次跳很远（点了⏭/点了别处）不当成播放
            if (previewAuditionSfx && span > 0 && span <= MaxAuditionSpan)
            {
                var comp = GetSelectedPlayAnimationComponent();
                if (comp != null && comp.frameEvents != null)
                {
                    foreach (var evt in comp.frameEvents)
                    {
                        if (evt == null) continue;
                        if (evt.frame > previewFrameCursor && evt.frame <= frame)
                            PlayClipInEditor(GetClip(evt.soundId));
                    }
                }
            }

            previewFrameCursor = frame;
        }

        // ──────────────── 采样（P0-1 / P0-4）────────────────

        private void SamplePreview()
        {
            if (previewClip == null) return;
            if (Application.isPlaying) return;

            AuditionFrameEvents();   // 播放头跨过帧事件就试听（与有没有预览目标无关）

            var animator = ResolvePreviewTarget();
            if (animator == null)
            {
                Debug.LogWarning("[SkillEditor] 找不到预览对象（场景里没有 tag=Player 的 Animator，且未手动指定），只保留时间轴/帧号功能。");
                return;
            }

            // 换了预览目标：先把上一个对象的姿态还原
            if (lastSampledTarget != null && lastSampledTarget != animator)
            {
                ExitAnimationMode();
                lastSampledTarget = null;
            }

            try
            {
                EnsureAnimationMode();
                AnimationMode.SampleAnimationClip(animator.gameObject, previewClip, previewTime);
                lastSampledTarget = animator;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkillEditor] 动画预览采样失败：" + e.Message);
            }
        }

        private void EnsureAnimationMode()
        {
            if (previewInAnimationMode) return;
            if (AnimationMode.InAnimationMode()) return;   // 别人（Animation 窗口）开着的，不碰
            AnimationMode.StartAnimationMode();
            previewInAnimationMode = true;
        }

        /// <summary>只关自己开的动画模式（StopAnimationMode 会把场景还原到预览前）</summary>
        private void ExitAnimationMode()
        {
            if (!previewInAnimationMode) return;
            previewInAnimationMode = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // ──────────────── 预览 clip 与技能的一致性提示（P0-2 的 B 方案；A 方案留到第二步）────────────────

        private void WarnIfPreviewClipMismatch()
        {
            var playAnim = GetSelectedPlayAnimationComponent();
            if (playAnim == null || previewClip == null) return;

            string state = string.IsNullOrEmpty(playAnim.stateName) ? "(空)" : playAnim.stateName;
            EditorGUILayout.HelpBox(
                $"运行时按 stateName=\"{state}\" 播动画机里的状态；这里预览的是 clip「{previewClip.name}」。" +
                "两者必须是同一个动画，否则配的帧号对不上。",
                MessageType.Info);
        }

        // ──────────────── stateName 校验 + 从动画机解析 clip（P0-2 A 方案）────────────────

        // 说明：这里刻意不用 UnityEditor.Animations.AnimatorController
        // （编辑器程序集不一定引用 UnityEditor.AnimationModule，会 CS0246），
        // 改为用 SerializedObject 直接读 controller 资产里的序列化字段，零额外依赖。

        /// <summary>
        /// 把玩家 Animator 上的状态表读出来（递归子状态机；AnimatorOverrideController
        /// 会换算成覆盖后的 clip）。只在控制器换了才重读。
        /// </summary>
        private void RefreshPlayerStates()
        {
            var animator = ResolvePreviewTarget();
            var rac = animator != null ? animator.runtimeAnimatorController : null;
            string src = rac != null ? rac.GetInstanceID().ToString() : "";

            if (src == playerStatesSource && playerStates != null) return;

            playerStatesSource = src;
            playerStates = new Dictionary<string, AnimationClip>();
            playerStateSpeeds.Clear();
            playerBaseClips.Clear();
            if (rac == null) return;

            var aoc = rac as AnimatorOverrideController;
            var baseController = aoc != null ? aoc.runtimeAnimatorController : rac;
            if (baseController == null) return;

            var controllerSo = new SerializedObject(baseController);
            var layers = controllerSo.FindProperty("m_AnimatorLayers");
            if (layers == null) return;

            for (int i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                var smProp = layer.FindPropertyRelative("m_StateMachine");
                if (smProp == null) continue;
                CollectStates(smProp.objectReferenceValue, aoc);
            }
        }

        private void CollectStates(UnityEngine.Object stateMachine, AnimatorOverrideController aoc)
        {
            if (stateMachine == null) return;

            var smSo = new SerializedObject(stateMachine);

            // 本层的状态
            var states = smSo.FindProperty("m_ChildStates");
            if (states != null)
            {
                for (int i = 0; i < states.arraySize; i++)
                {
                    var stProp = states.GetArrayElementAtIndex(i).FindPropertyRelative("m_State");
                    if (stProp == null) continue;
                    var state = stProp.objectReferenceValue;
                    if (state == null) continue;

                    var stateSo = new SerializedObject(state);
                    var nameProp = stateSo.FindProperty("m_Name");
                    if (nameProp == null || string.IsNullOrEmpty(nameProp.stringValue)) continue;
                    if (playerStates.ContainsKey(nameProp.stringValue)) continue;

                    var motionProp = stateSo.FindProperty("m_Motion");
                    var rawClip = motionProp != null ? motionProp.objectReferenceValue as AnimationClip : null;

                    // 有效 clip（把 Animator 上已有的 Override 算进去）—— 预览/算帧率用这个
                    var clip = rawClip;
                    if (aoc != null && clip != null) clip = aoc[clip];

                    // 状态自己的播放速度（帧号跑多快由它决定：currentFrame ≈ 时间 × 帧率 × Speed）
                    var speedProp = stateSo.FindProperty("m_Speed");
                    float speed = speedProp != null ? speedProp.floatValue : 1f;
                    if (Mathf.Approximately(speed, 0f)) speed = 1f;

                    playerStates[nameProp.stringValue] = clip;
                    playerBaseClips[nameProp.stringValue] = rawClip;   // 覆盖映射的 key 必须是这个
                    playerStateSpeeds[nameProp.stringValue] = Mathf.Abs(speed);
                }
            }

            // 递归子状态机
            var subs = smSo.FindProperty("m_ChildStateMachines");
            if (subs != null)
            {
                for (int i = 0; i < subs.arraySize; i++)
                {
                    var subProp = subs.GetArrayElementAtIndex(i).FindPropertyRelative("m_StateMachine");
                    if (subProp == null) continue;
                    CollectStates(subProp.objectReferenceValue, aoc);
                }
            }
        }

        /// <summary>
        /// 用玩家的动画机校验 stateName。
        /// 名字写错时游戏里 CrossFade 会失败 → 表现就是"按了键动作没变"，这里提前报出来。
        /// </summary>
        private void DrawStateNameCheck()
        {
            var playAnim = GetSelectedPlayAnimationComponent();
            if (playAnim == null) return;

            RefreshPlayerStates();

            if (playerStates == null || playerStates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "拿不到玩家的动画机状态表（没指定「预览目标」，或它的 Animator 没挂 Controller），无法校验 stateName。",
                    MessageType.Warning);
                return;
            }

            if (string.IsNullOrEmpty(playAnim.stateName))
            {
                EditorGUILayout.HelpBox("stateName 是空的，技能不会播任何动画。", MessageType.Error);
                return;
            }

            if (!playerStates.TryGetValue(playAnim.stateName, out var clip))
            {
                EditorGUILayout.HelpBox(
                    $"stateName「{playAnim.stateName}」在这个动画机里不存在 —— 游戏里 CrossFade 会失败，" +
                    "表现就是「按了键动作没变」。\n可用的状态名：" +
                    string.Join("、", new List<string>(playerStates.Keys).ToArray()),
                    MessageType.Error);
                return;
            }

            if (clip == null)
            {
                EditorGUILayout.HelpBox(
                    $"stateName「{playAnim.stateName}」存在，但这个状态没绑动画（m_Motion 为空），游戏里也不会有动作。",
                    MessageType.Warning);
                return;
            }

            float stateSpeed = playerStateSpeeds.TryGetValue(playAnim.stateName, out var sp) ? sp : 1f;

            // 方案B：baseClip 必须写进资产（运行时读不到 UnityEditor，没法现场解析动画机）
            var rawBaseClip = playerBaseClips.TryGetValue(playAnim.stateName, out var rb) ? rb : clip;
            SyncBaseClip(rawBaseClip);

            // 有效动画：配了 overrideClip 就用它 —— 预览和游戏看到的是同一个
            var effectiveClip = playAnim.overrideClip != null ? playAnim.overrideClip : clip;

            int maxFrame = GetMaxReachableFrame(playAnim, effectiveClip, stateSpeed);

            // 预览动画空着、或还停在"没覆盖前那个 clip"上，就自动同步成有效动画
            if (previewClip == null || previewClip == clip)
            {
                previewClip = effectiveClip;
                previewTime = 0f;
                previewFrameCursor = -1;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"stateName 校验通过：{playAnim.stateName} → clip「{effectiveClip.name}」" +
                $"（{SkillFrameUtil.GetFrameRate(effectiveClip):0.##}fps，Speed={stateSpeed:0.##}，最多走到第 {maxFrame} 帧）" +
                (playAnim.overrideClip != null ? "   ★动画覆盖已启用" : ""),
                EditorStyles.miniLabel);
            if (GUILayout.Button("用它做预览动画", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                previewClip = effectiveClip;
                previewTime = 0f;
                previewFrameCursor = -1;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // 动画覆盖的坑：Humanoid 角色套非 Humanoid 动画会骨骼错乱
            if (playAnim.overrideClip != null)
            {
                var targetAnimator = ResolvePreviewTarget();
                var avatar = targetAnimator != null ? targetAnimator.avatar : null;
                if (avatar != null && avatar.isHuman && !playAnim.overrideClip.isHumanMotion)
                {
                    EditorGUILayout.HelpBox(
                        $"overrideClip「{playAnim.overrideClip.name}」不是 Humanoid 动画，而目标角色是 Humanoid Avatar ——\n" +
                        "运行时很可能骨骼错乱或根本不动。请换一个同类型的动画。",
                        MessageType.Error);
                }
            }

            // ★ 帧号超出技能时长 —— 游戏里永远不会触发，而且完全静默（最容易踩的坑）
            if (playAnim.frameEvents != null && playAnim.frameEvents.Count > 0)
            {
                var over = new List<string>();
                foreach (var evt in playAnim.frameEvents)
                {
                    if (evt != null && evt.frame > maxFrame) over.Add(evt.frame.ToString());
                }

                if (over.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"这些帧事件永远不会触发：第 {string.Join("、", over.ToArray())} 帧。\n" +
                        $"duration={playAnim.duration:0.###}s × Speed={stateSpeed:0.##} × " +
                        $"{SkillFrameUtil.GetFrameRate(effectiveClip):0.##}fps" +
                        $" → 这个技能最多只能走到第 {maxFrame} 帧。\n" +
                        "→ 改法：把 duration 调大，或把帧号改小。游戏里它不会报任何错，只是不响。",
                        MessageType.Error);
                }
            }
        }

        /// <summary>
        /// 把 state 在【基础 controller】里原本绑的 clip 写进组件的 baseClip 字段。
        /// 这是 AnimatorOverrideController 的映射 key，运行时要用，所以必须存进资产。
        /// </summary>
        private void SyncBaseClip(AnimationClip rawClip)
        {
            if (skillSerializedObject == null || componentsProperty == null) return;
            if (selectedComponentIndex < 0 || selectedComponentIndex >= componentsProperty.arraySize) return;

            var element = componentsProperty.GetArrayElementAtIndex(selectedComponentIndex);
            var prop = element.FindPropertyRelative("baseClip");
            if (prop == null) return;
            if (prop.objectReferenceValue == rawClip) return;   // 已经对了，别再每帧 Apply

            skillSerializedObject.Update();
            prop.objectReferenceValue = rawClip;
            skillSerializedObject.ApplyModifiedProperties();
            if (skillSerializedObject.targetObject != null)
                EditorUtility.SetDirty(skillSerializedObject.targetObject);
        }

        /// <summary>这个技能实际能走到的最大帧号（duration 与 clip 长度取小；Speed 会放大帧号）</summary>
        private static int GetMaxReachableFrame(PlayAnimationComponent playAnim, AnimationClip clip, float speed)
        {
            float fps = SkillFrameUtil.GetFrameRate(clip);
            float s = Mathf.Max(0.0001f, speed);

            // 结束条件二选一：duration 到点，或动画先播完
            float playSeconds = clip != null ? Mathf.Min(playAnim.duration, clip.length / s) : playAnim.duration;
            return Mathf.Max(0, Mathf.FloorToInt(playSeconds * fps * s));
        }

        private PlayAnimationComponent GetSelectedPlayAnimationComponent()
        {
            if (skillSerializedObject == null || componentsProperty == null) return null;
            if (selectedComponentIndex < 0 || selectedComponentIndex >= componentsProperty.arraySize) return null;
            return componentsProperty.GetArrayElementAtIndex(selectedComponentIndex).managedReferenceValue
                   as PlayAnimationComponent;
        }

        private void AddFrameEventAtFrame(int frame)
        {
            if (frameEventsProperty == null || skillSerializedObject == null)
            {
                Debug.LogWarning("[SkillEditor] 请先在组件列表里选中一个“播放动画”组件，再添加帧事件。");
                EditorUtility.DisplayDialog("无法添加帧事件", "请先在组件列表里选中一个「播放动画」组件。", "知道了");
                return;
            }

            // 帧号夹取到合法范围（P0-7）
            int maxFrame = previewClip != null ? SkillFrameUtil.TotalFrames(previewClip) : int.MaxValue;
            int finalFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, maxFrame));

            skillSerializedObject.Update();
            int index = frameEventsProperty.arraySize;
            frameEventsProperty.arraySize++;

            var evt = frameEventsProperty.GetArrayElementAtIndex(index);
            evt.FindPropertyRelative("frame").intValue = finalFrame;

            // Unity 新建数组元素时字段会落成 0（不走 C# 字段初始值），volume 显式补成 1
            var volumeProp = evt.FindPropertyRelative("volume");
            if (volumeProp != null && volumeProp.floatValue <= 0f) volumeProp.floatValue = 1f;

            // 默认音效 ID：继承上一条，避免乱填一个不存在的 ID；没有就留空
            var idProp = evt.FindPropertyRelative("soundId");
            if (string.IsNullOrEmpty(idProp.stringValue))
            {
                idProp.stringValue = index > 0
                    ? frameEventsProperty.GetArrayElementAtIndex(index - 1).FindPropertyRelative("soundId").stringValue
                    : "";
            }
            string finalSoundId = idProp.stringValue;

            SortFrameEventsByFrame();              // 按帧号升序，避免乱序漏触发
            skillSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(skillSerializedObject.targetObject);

            // 时间轴跳到这一帧，方便接着配
            if (previewClip != null)
                previewTime = SkillFrameUtil.FrameToTime(previewClip, finalFrame);
            Repaint();

            Debug.Log($"[SkillEditor] 已添加帧事件 @ 第 {finalFrame} 帧" +
                      (string.IsNullOrEmpty(finalSoundId) ? "（音效ID为空，记得填）" : $"（音效ID = {finalSoundId}）"));
        }

        /// <summary>按 frame 升序重排。用 MoveArrayElement，不依赖字段个数，不会丢字段。</summary>
        private void SortFrameEventsByFrame()
        {
            if (frameEventsProperty == null) return;

            for (int i = 1; i < frameEventsProperty.arraySize; i++)
            {
                int j = i;
                while (j > 0)
                {
                    int prev = frameEventsProperty.GetArrayElementAtIndex(j - 1).FindPropertyRelative("frame").intValue;
                    int cur = frameEventsProperty.GetArrayElementAtIndex(j).FindPropertyRelative("frame").intValue;
                    if (prev <= cur) break;
                    frameEventsProperty.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }

        #endregion

        #region 底部保存

        private void DrawSaveButton()
        {
            EditorGUILayout.Space(10);
            if (GUILayout.Button("保存所有修改", GUILayout.Height(30)))
            {
                if (librarySerializedObject != null)
                {
                    librarySerializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(library);
                }
                if (skillSerializedObject != null)
                {
                    skillSerializedObject.ApplyModifiedProperties();
                    if (selectedSkillIndex >= 0 && selectedSkillIndex < skillsProperty.arraySize)
                    {
                        var element = skillsProperty.GetArrayElementAtIndex(selectedSkillIndex);
                        if (element.objectReferenceValue != null)
                        {
                            EditorUtility.SetDirty(element.objectReferenceValue);
                        }
                    }
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[SkillEditor] 已保存");
            }
        }

        #endregion
    }
}
