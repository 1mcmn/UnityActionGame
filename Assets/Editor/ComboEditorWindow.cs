using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>编辑草稿、已保存资产与运行快照分别管理；窗口关闭会提示保存草稿。</summary>
public class ComboEditorWindow : EditorWindow
{
    // HideAndDontSave 包含 NotEditable，会锁住 PropertyField；草稿只需隐藏且不持久化。
    private const HideFlags EditableDraftFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
    [SerializeField] private ComboData asset;
    [SerializeField] private ComboData draft;
    [SerializeField] private string savedDraftJson;
    [SerializeField] private string sourceAssetJson;
    [SerializeField] private int sourceRevision;
    [SerializeField] private int selectedStep;
    [SerializeField] private PlayerCombat reloadTarget;
    [SerializeField] private Animator previewTarget;
    [SerializeField] private bool advancedExpanded;
    [SerializeField] private bool soundExpanded;
    [SerializeField] private bool previewExpanded;
    private SerializedObject draftObject;
    private ReorderableList stepsList;
    private Vector2 detailScroll;
    private Vector2 stepsScroll;
    private Vector2 errorsScroll;
    private List<string> errors = new List<string>();
    private readonly List<string> soundIds = new List<string>();
    private float previewTime;
    private bool previewPlaying;
    private bool ownsAnimationMode;
    private double lastPreviewTick;
    private string previewMessage;

    [MenuItem("Tools/连招配置编辑器")]
    public static void ShowWindow()
    {
        GetWindow<ComboEditorWindow>("连招配置编辑器").minSize = new Vector2(640f, 520f);
    }

    public static void OpenAsset(ComboData data)
    {
        var window = GetWindow<ComboEditorWindow>("连招配置编辑器");
        window.minSize = new Vector2(640f, 520f);
        if (window.ConfirmDiscardOrSave()) window.LoadAsset(data);
    }

    [MenuItem("Tools/连招演示/重载选中玩家的已保存配置")]
    public static void ReloadSelectedPlayer()
    {
        PlayerCombat player = GetSelectedPlayer();
        if (!EditorApplication.isPlaying || player == null || player.Data == null)
        {
            Debug.LogWarning("[连招重载] 请在运行模式选择带 PlayerCombat 的场景玩家，并为它配置连招资产。");
            return;
        }
        RequestSavedReload(player, player.Data);
    }

    private void OnEnable()
    {
        minSize = new Vector2(640f, 520f);
        saveChangesMessage = "连招草稿尚未保存。是否保存到所选 ComboData 资产？";
        if (draft != null) BindDraft();
        else if (asset != null) LoadAsset(asset);
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        RefreshSoundIds();
    }

    private void OnDisable()
    {
        StopPreview();
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void OnDestroy()
    {
        if (draft != null) DestroyImmediate(draft);
    }

    public override void SaveChanges()
    {
        if (SaveDraft()) base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        LoadAsset(asset);
        base.DiscardChanges();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawRuntimeStatus();
        if (draft == null || draftObject == null)
        {
            EditorGUILayout.HelpBox("选择或新建一份连招资产，再添加至少三段攻击。每段的动画同时用于配置和预览。", MessageType.Info);
            return;
        }

        draftObject.Update();
        EditorGUILayout.PropertyField(draftObject.FindProperty("displayName"), new GUIContent("连招名称"));
        bool wide = position.width >= 850f;
        if (wide) EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(wide ? GUILayout.Width(235f) : GUILayout.ExpandWidth(true));
        DrawSteps(wide);
        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        DrawStepDetails();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        if (wide) EditorGUILayout.EndHorizontal();
        if (draftObject.ApplyModifiedProperties()) UpdateDirtyState();
        DrawValidation();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var next = (ComboData)EditorGUILayout.ObjectField(asset, typeof(ComboData), false, GUILayout.MinWidth(130f));
            if (next != asset && ConfirmDiscardOrSave()) LoadAsset(next);
            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(42f))) CreateAsset();
            using (new EditorGUI.DisabledScope(asset == null))
            {
                if (GUILayout.Button("加载 / 还原", EditorStyles.toolbarButton, GUILayout.Width(82f)) && ConfirmDiscardOrSave()) LoadAsset(asset);
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(42f))) SaveDraft();
            }
            using (new EditorGUI.DisabledScope(draft == null))
            {
                if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(42f))) ValidateDraft();
            }
        }
        if (asset != null)
            EditorGUILayout.LabelField($"编辑：{(hasUnsavedChanges ? "有未保存修改" : "已保存")}    资产：r{asset.revision}", EditorStyles.miniLabel);
        if (HasSourceConflict())
            EditorGUILayout.HelpBox("资产已在 Inspector 或其他窗口被修改。请先保存那里的修改，再加载最新资产；当前草稿仍被保留，不能直接覆盖。", MessageType.Warning);
    }

    private void DrawRuntimeStatus()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            reloadTarget = (PlayerCombat)EditorGUILayout.ObjectField("重载目标", reloadTarget, typeof(PlayerCombat), true);
            if (GUILayout.Button("使用当前选择", GUILayout.Width(100f))) reloadTarget = GetSelectedPlayer();
            bool canReload = EditorApplication.isPlaying && reloadTarget != null && asset != null && !EditorUtility.IsPersistent(reloadTarget);
            using (new EditorGUI.DisabledScope(!canReload))
            {
                if (GUILayout.Button("重载已保存版本", GUILayout.Width(120f)))
                {
                    RequestSavedReload(reloadTarget, asset);
                    Repaint();
                }
            }
        }
        if (!EditorApplication.isPlaying)
            EditorGUILayout.LabelField("运行：进入 Play 模式后，明确选择玩家再重载。", EditorStyles.miniLabel);
        else if (reloadTarget == null)
            EditorGUILayout.LabelField("运行：请选择需要应用配置的玩家。", EditorStyles.miniLabel);
        else
        {
            var air = reloadTarget.GetComponent<PlayerAirCombat>();
            if (air != null && air.UsesAirData(asset))
            {
                int revision = air.RevisionFor(asset);
                string airSync = revision == asset.revision ? "版本已同步" : "当前所选资产尚未同步";
                EditorGUILayout.LabelField($"运行：浮空动作 {asset.name} r{revision} · {airSync} · {air.Status}", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            string dataName = reloadTarget.Data != null ? reloadTarget.Data.name : "无资产";
            string sync = reloadTarget.Data == asset && asset != null && reloadTarget.LoadedRevision == asset.revision
                ? "版本已同步" : "当前所选资产尚未同步";
            EditorGUILayout.LabelField($"运行：{dataName} r{reloadTarget.LoadedRevision} · {sync} · {reloadTarget.ReloadStatus}", EditorStyles.wordWrappedMiniLabel);
        }
    }

    private static PlayerCombat GetSelectedPlayer()
    {
        GameObject go = Selection.activeGameObject;
        return go == null ? null : go.GetComponentInParent<PlayerCombat>();
    }

    private static void RequestSavedReload(PlayerCombat player, ComboData saved)
    {
        if (EditorUtility.IsDirty(saved))
        {
            Debug.LogWarning("[连招重载] 资产 Inspector 中还有未写盘修改，请先保存资产。编辑器窗口的独立草稿不会被重载。");
            return;
        }
        List<string> issues = saved.Validate();
        if (issues.Count > 0)
        {
            Debug.LogError("[连招重载] 已保存配置不合法，保留原运行版本：\n" + string.Join("\n", issues), saved);
            return;
        }
        var air = player.GetComponent<PlayerAirCombat>();
        if (air != null && air.UsesAirData(saved))
        {
            if (!air.RequestReload()) Debug.LogWarning("[浮空重载] " + air.Status, player);
            else Debug.Log("[浮空重载] " + air.Status, player);
            return;
        }
        if (!player.RequestReload(saved)) Debug.LogWarning("[连招重载] " + player.ReloadStatus, player);
        else Debug.Log("[连招重载] " + player.ReloadStatus, player);
    }

    private void DrawSteps(bool wide)
    {
        if (stepsList == null) BindDraft();
        stepsScroll = EditorGUILayout.BeginScrollView(stepsScroll,
            wide ? GUILayout.ExpandHeight(true) : GUILayout.Height(160f));
        stepsList.DoLayoutList();
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope())
        {
            bool selected = draft.steps != null && selectedStep >= 0 && selectedStep < draft.steps.Count;
            using (new EditorGUI.DisabledScope(!selected))
            {
                if (GUILayout.Button("复制段")) DuplicateStep();
            }
            using (new EditorGUI.DisabledScope(!selected || selectedStep == 0))
            {
                if (GUILayout.Button("上移")) MoveStep(-1);
            }
            using (new EditorGUI.DisabledScope(!selected || selectedStep >= draft.steps.Count - 1))
            {
                if (GUILayout.Button("下移")) MoveStep(1);
            }
        }
        EditorGUILayout.LabelField("列表顺序决定接段顺序，末段自动结束。", EditorStyles.wordWrappedMiniLabel);
    }

    private void BindDraft()
    {
        if (draft == null) return;
        // 脚本重载或撤销后，已有草稿也要恢复可编辑性，保留其中未保存的值。
        draft.hideFlags = EditableDraftFlags;
        draftObject = new SerializedObject(draft);
        stepsList = new ReorderableList(draftObject, draftObject.FindProperty("steps"), true, true, true, true);
        stepsList.elementHeight = 42f;
        stepsList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, $"连招段 · {draft.steps.Count} 段");
        stepsList.drawElementCallback = (rect, index, active, focused) =>
        {
            SerializedProperty list = draftObject.FindProperty("steps");
            if (index >= list.arraySize) return;
            SerializedProperty step = list.GetArrayElementAtIndex(index);
            var clip = step.FindPropertyRelative("animationClip").objectReferenceValue as AnimationClip;
            string title = step.FindPropertyRelative("displayName").stringValue;
            EditorGUI.LabelField(new Rect(rect.x, rect.y + 2, rect.width, 18), $"{index + 1}. {title}", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(rect.x, rect.y + 21, rect.width, 18),
                $"{(clip != null ? clip.name : "未配置动画")} · 伤害 {step.FindPropertyRelative("damage").floatValue:0.##}", EditorStyles.miniLabel);
        };
        stepsList.onSelectCallback = list => SelectStep(list.index);
        stepsList.onAddCallback = list => AddStep();
        stepsList.onRemoveCallback = list => RemoveStep();
        stepsList.onReorderCallback = list => { SelectStep(list.index); UpdateDirtyState(); };
        selectedStep = Mathf.Clamp(selectedStep, 0, Mathf.Max(0, draft.steps.Count - 1));
        stepsList.index = selectedStep;
    }

    private void SelectStep(int index)
    {
        StopPreview();
        previewTime = 0f;
        selectedStep = index;
        if (stepsList != null) stepsList.index = index;
    }

    private void AddStep()
    {
        draftObject.ApplyModifiedProperties();
        Undo.RecordObject(draft, "添加连招段");
        var step = new ComboStep
        {
            stepId = Guid.NewGuid().ToString("N"), displayName = "攻击 " + (draft.steps.Count + 1),
            damage = 15f, playbackSpeed = 2f, hitStart = 0.1f, hitEnd = 0.3f, comboStart = 0.4f, comboEnd = 0.7f,
            rootMotionScaleXZ = 1f, hitRadius = 1.2f, hitOffset = new Vector3(0f, 0.8f, 1f),
            allowCancel = true, cancelStart = 0.5f, cancelEnd = 0.8f,
            soundEvents = new List<ComboSoundEvent>()
        };
        draft.steps.Add(step);
        draftObject.Update();
        SelectStep(draft.steps.Count - 1);
        UpdateDirtyState();
    }

    private void DuplicateStep()
    {
        draftObject.ApplyModifiedProperties();
        Undo.RecordObject(draft, "复制连招段");
        ComboStep copy = draft.steps[selectedStep].Copy();
        copy.stepId = Guid.NewGuid().ToString("N");
        copy.displayName += " 副本";
        draft.steps.Insert(selectedStep + 1, copy);
        draftObject.Update();
        SelectStep(selectedStep + 1);
        UpdateDirtyState();
    }

    private void RemoveStep()
    {
        if (selectedStep < 0 || selectedStep >= draft.steps.Count) return;
        draftObject.ApplyModifiedProperties();
        Undo.RecordObject(draft, "删除连招段");
        draft.steps.RemoveAt(selectedStep);
        draftObject.Update();
        SelectStep(Mathf.Clamp(selectedStep, 0, Mathf.Max(0, draft.steps.Count - 1)));
        UpdateDirtyState();
    }

    private void MoveStep(int direction)
    {
        int next = selectedStep + direction;
        if (next < 0 || next >= draft.steps.Count) return;
        draftObject.ApplyModifiedProperties();
        Undo.RecordObject(draft, "排序连招段");
        ComboStep step = draft.steps[selectedStep];
        draft.steps.RemoveAt(selectedStep);
        draft.steps.Insert(next, step);
        draftObject.Update();
        SelectStep(next);
        UpdateDirtyState();
    }

    private void DrawStepDetails()
    {
        SerializedProperty list = draftObject.FindProperty("steps");
        if (list.arraySize == 0 || selectedStep >= list.arraySize)
        {
            EditorGUILayout.HelpBox("点击左侧 + 添加一个攻击段。", MessageType.Info);
            return;
        }
        var step = list.GetArrayElementAtIndex(selectedStep);
        EditorGUILayout.LabelField($"第 {selectedStep + 1} 段 / 共 {list.arraySize} 段", EditorStyles.boldLabel);
        Field(step, "displayName", "段名称");
        SerializedProperty clipProp = step.FindPropertyRelative("animationClip");
        AnimationClip previous = clipProp.objectReferenceValue as AnimationClip;
        EditorGUILayout.PropertyField(clipProp, new GUIContent("动画片段"));
        AnimationClip clip = clipProp.objectReferenceValue as AnimationClip;
        if (clip != previous)
        {
            StopPreview();
            previewTime = 0f;
            if (previous == null && clip != null)
            {
                SetWindow(step, "hitStart", "hitEnd", clip.length * 0.2f, clip.length * 0.45f);
                SetWindow(step, "comboStart", "comboEnd", clip.length * 0.6f, clip.length * 0.9f);
                SetWindow(step, "cancelStart", "cancelEnd", clip.length * 0.55f, clip.length * 0.95f);
            }
        }
        Field(step, "playbackSpeed", "播放倍速");
        float playbackSpeed = step.FindPropertyRelative("playbackSpeed").floatValue;
        if (clip != null && ComboData.Finite(playbackSpeed) && playbackSpeed >= ComboStep.MinPlaybackSpeed)
            EditorGUILayout.LabelField($"原片段 {clip.length:0.###} 秒 · 实际播放约 {clip.length / playbackSpeed:0.###} 秒", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("窗口、音效和播放头使用原动画秒数；提高倍速后会随动画同步提前。", EditorStyles.wordWrappedMiniLabel);
        Field(step, "damage", "伤害");
        DrawWindowFields(step, "hitStart", "hitEnd", "攻击判定（秒）");
        using (new EditorGUI.DisabledScope(selectedStep == list.arraySize - 1))
            DrawWindowFields(step, "comboStart", "comboEnd", "连招输入（秒）");
        if (selectedStep == list.arraySize - 1)
            EditorGUILayout.LabelField("当前为末段：不再接续下一段。", EditorStyles.miniLabel);
        if (clip != null) DrawTimeline(step, clip, selectedStep < list.arraySize - 1);

        advancedExpanded = EditorGUILayout.Foldout(advancedExpanded, "位移、取消与判定形状", true);
        if (advancedExpanded)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                Field(step, "rootMotionScaleXZ", "水平 Root Motion 倍率");
                EditorGUILayout.LabelField("1 = 原动画位移，0 = 原地；倍率不代表前冲米数。", EditorStyles.wordWrappedMiniLabel);
                Field(step, "hitRadius", "判定半径（米）");
                Field(step, "hitOffset", "判定中心偏移");
                Field(step, "allowCancel", "允许闪避取消");
                if (step.FindPropertyRelative("allowCancel").boolValue)
                    DrawWindowFields(step, "cancelStart", "cancelEnd", "取消窗口（秒）");
            }
        }
        DrawSoundEvents(step, clip);
        DrawPreview(clip);
    }

    private static void Field(SerializedProperty parent, string field, string label)
    {
        EditorGUILayout.PropertyField(parent.FindPropertyRelative(field), new GUIContent(label));
    }

    private static void SetWindow(SerializedProperty step, string start, string end, float a, float b)
    {
        step.FindPropertyRelative(start).floatValue = a;
        step.FindPropertyRelative(end).floatValue = b;
    }

    private static void DrawWindowFields(SerializedProperty step, string start, string end, string label)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            var a = step.FindPropertyRelative(start);
            var b = step.FindPropertyRelative(end);
            a.floatValue = EditorGUILayout.FloatField(a.floatValue, GUILayout.MinWidth(50));
            GUILayout.Label("—", GUILayout.Width(14));
            b.floatValue = EditorGUILayout.FloatField(b.floatValue, GUILayout.MinWidth(50));
        }
    }

    private void DrawTimeline(SerializedProperty step, AnimationClip clip, bool hasNext)
    {
        EditorGUILayout.Space(5f);
        Rect rect = GUILayoutUtility.GetRect(100f, 104f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.15f));
        float duration = Mathf.Max(0.0001f, clip.length);
        DrawBand(rect, 3f, step, "hitStart", "hitEnd", "判定", new Color(0.95f, 0.43f, 0.2f), duration);
        if (hasNext) DrawBand(rect, 27f, step, "comboStart", "comboEnd", "接段", new Color(0.22f, 0.65f, 0.95f), duration);
        if (step.FindPropertyRelative("allowCancel").boolValue)
            DrawBand(rect, 51f, step, "cancelStart", "cancelEnd", "取消", new Color(0.36f, 0.72f, 0.42f), duration);
        GUI.Label(new Rect(rect.x + 4f, rect.y + 75f, rect.width - 8f, 20f), "音效", EditorStyles.whiteMiniLabel);
        SerializedProperty soundEvents = step.FindPropertyRelative("soundEvents");
        for (int i = 0; i < soundEvents.arraySize; i++)
        {
            SerializedProperty sound = soundEvents.GetArrayElementAtIndex(i);
            float eventTime = sound.FindPropertyRelative("time").floatValue;
            if (!ComboData.Finite(eventTime)) continue;
            float markerX = rect.x + Mathf.Clamp01(eventTime / duration) * (rect.width - 7f);
            var marker = new Rect(markerX, rect.y + 77f, 7f, 16f);
            EditorGUI.DrawRect(marker, eventTime >= 0f && eventTime < duration ? new Color(0.92f, 0.76f, 0.22f) : Color.red);
            GUI.Label(marker, new GUIContent("", $"{sound.FindPropertyRelative("soundId").stringValue} · {eventTime:0.###} 秒"));
        }
        float x = rect.x + Mathf.Clamp01(previewTime / duration) * rect.width;
        EditorGUI.DrawRect(new Rect(x, rect.y, 2f, rect.height), Color.white);
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            previewPlaying = false;
            previewTime = Mathf.Clamp01((Event.current.mousePosition.x - rect.x) / rect.width) * duration;
            SamplePreview(clip);
            Event.current.Use();
        }
        float time = EditorGUILayout.Slider("预览播放头（秒）", previewTime, 0f, clip.length);
        if (!Mathf.Approximately(time, previewTime))
        {
            previewTime = time;
            previewPlaying = false;
            SamplePreview(clip);
        }
        EditorGUILayout.LabelField($"第 {Mathf.RoundToInt(previewTime * clip.frameRate)} 帧 · 窗口为 [开始, 结束)，在上方数值框调整。", EditorStyles.miniLabel);
    }

    private static void DrawBand(Rect rect, float y, SerializedProperty step, string start, string end, string label, Color color, float duration)
    {
        float a = Mathf.Clamp01(step.FindPropertyRelative(start).floatValue / duration);
        float b = Mathf.Clamp01(step.FindPropertyRelative(end).floatValue / duration);
        if (b > a) EditorGUI.DrawRect(new Rect(rect.x + a * rect.width, rect.y + y, (b - a) * rect.width, 20f), color);
        GUI.Label(new Rect(rect.x + 4f, rect.y + y, rect.width - 8f, 20f), label, EditorStyles.whiteMiniLabel);
    }

    private void DrawSoundEvents(SerializedProperty step, AnimationClip clip)
    {
        var events = step.FindPropertyRelative("soundEvents");
        soundExpanded = EditorGUILayout.Foldout(soundExpanded, $"音效事件 · {events.arraySize} 条", true);
        if (!soundExpanded) return;
        for (int i = 0; i < events.arraySize; i++)
        {
            var evt = events.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                var time = evt.FindPropertyRelative("time");
                time.floatValue = EditorGUILayout.FloatField(time.floatValue, GUILayout.Width(70f));
                GUILayout.Label("秒", GUILayout.Width(20f));
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("soundId"), GUIContent.none);
                if (GUILayout.Button("选择", GUILayout.Width(45f))) ShowSoundMenu(evt.FindPropertyRelative("soundId"));
                if (GUILayout.Button("−", GUILayout.Width(25f)))
                {
                    events.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("在播放头添加音效"))
            {
                int index = events.arraySize++;
                var evt = events.GetArrayElementAtIndex(index);
                evt.FindPropertyRelative("time").floatValue = clip == null ? 0f : previewTime;
                evt.FindPropertyRelative("soundId").stringValue = "";
            }
            if (GUILayout.Button("刷新音效列表", GUILayout.Width(105f))) RefreshSoundIds();
        }
        EditorGUILayout.LabelField("只配置触发时间和 SoundLibrary 中的音效 ID。", EditorStyles.miniLabel);
    }

    private void RefreshSoundIds()
    {
        soundIds.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:SoundLibrary"))
        {
            SoundLibrary library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if (library == null || library.sounds == null) continue;
            foreach (var sound in library.sounds)
                if (sound != null && sound.clip != null && !string.IsNullOrEmpty(sound.soundID) && !soundIds.Contains(sound.soundID))
                    soundIds.Add(sound.soundID);
        }
        soundIds.Sort(StringComparer.Ordinal);
    }

    private void ShowSoundMenu(SerializedProperty id)
    {
        var menu = new GenericMenu();
        if (soundIds.Count == 0) menu.AddDisabledItem(new GUIContent("未找到音效 ID"));
        string path = id.propertyPath;
        foreach (string soundId in soundIds)
        {
            string value = soundId;
            menu.AddItem(new GUIContent(value), id.stringValue == value, () =>
            {
                if (draftObject == null) return;
                draftObject.Update();
                var property = draftObject.FindProperty(path);
                if (property == null) return;
                property.stringValue = value;
                draftObject.ApplyModifiedProperties();
                UpdateDirtyState();
            });
        }
        menu.ShowAsContext();
    }

    private void DrawPreview(AnimationClip clip)
    {
        previewExpanded = EditorGUILayout.Foldout(previewExpanded, "动画预览设置", true);
        if (!previewExpanded) return;
        Animator next = (Animator)EditorGUILayout.ObjectField("预览目标", previewTarget, typeof(Animator), true);
        if (next != previewTarget) { StopPreview(); previewTarget = next; }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("使用所选玩家动画机"))
            {
                StopPreview();
                PlayerCombat player = GetSelectedPlayer();
                previewTarget = player != null ? player.GetComponentInChildren<Animator>(true) : null;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || previewTarget == null || clip == null))
            {
                if (GUILayout.Button(previewPlaying ? "暂停" : "播放"))
                {
                    previewPlaying = !previewPlaying;
                    lastPreviewTick = EditorApplication.timeSinceStartup;
                    if (previewPlaying && previewTime >= clip.length) previewTime = 0f;
                    SamplePreview(clip);
                }
            }
            if (GUILayout.Button("结束预览 / 恢复姿态")) StopPreview();
        }
        if (!string.IsNullOrEmpty(previewMessage)) EditorGUILayout.HelpBox(previewMessage, MessageType.Info);
        EditorGUILayout.LabelField("仅在编辑模式预览当前段动画；结束预览、换段或进入运行模式会还原场景姿态。", EditorStyles.wordWrappedMiniLabel);
    }

    private AnimationClip CurrentClip()
    {
        if (draft == null || draft.steps == null || selectedStep < 0 || selectedStep >= draft.steps.Count) return null;
        return draft.steps[selectedStep].animationClip;
    }

    private void SamplePreview(AnimationClip clip)
    {
        if (EditorApplication.isPlaying || clip == null || previewTarget == null || EditorUtility.IsPersistent(previewTarget)) return;
        if (!ownsAnimationMode)
        {
            if (AnimationMode.InAnimationMode())
            {
                previewMessage = "其他窗口正在使用动画预览，请先结束那个预览。";
                previewPlaying = false;
                return;
            }
            AnimationMode.StartAnimationMode();
            ownsAnimationMode = true;
        }
        previewMessage = null;
        AnimationMode.BeginSampling();
        try { AnimationMode.SampleAnimationClip(previewTarget.gameObject, clip, previewTime); }
        finally { AnimationMode.EndSampling(); }
        SceneView.RepaintAll();
    }

    private void StopPreview()
    {
        previewPlaying = false;
        if (ownsAnimationMode && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        ownsAnimationMode = false;
        previewMessage = null;
        SceneView.RepaintAll();
    }

    private void OnEditorUpdate()
    {
        if (EditorApplication.isPlaying) { Repaint(); return; }
        if (!previewPlaying) return;
        AnimationClip clip = CurrentClip();
        if (clip == null || previewTarget == null) { StopPreview(); return; }
        double now = EditorApplication.timeSinceStartup;
        float speed = draft.steps[selectedStep].playbackSpeed;
        if (!ComboData.Finite(speed) || speed < ComboStep.MinPlaybackSpeed || speed > ComboStep.MaxPlaybackSpeed)
        { StopPreview(); previewMessage = "播放倍速无效，请先修正为 0.1～5。"; Repaint(); return; }
        previewTime = Mathf.Min(clip.length, previewTime + (float)(now - lastPreviewTick) * speed);
        lastPreviewTick = now;
        SamplePreview(clip);
        if (previewTime >= clip.length) previewPlaying = false;
        Repaint();
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state) { StopPreview(); }
    private void OnUndoRedo() { StopPreview(); BindDraft(); UpdateDirtyState(); Repaint(); }

    private void UpdateDirtyState()
    {
        if (draft == null) return;
        hasUnsavedChanges = EditorJsonUtility.ToJson(draft) != savedDraftJson;
        ValidateDraft();
    }

    private void ValidateDraft()
    {
        if (draftObject != null) draftObject.ApplyModifiedProperties();
        errors = draft != null ? draft.Validate() : new List<string>();
        Repaint();
    }

    private void DrawValidation()
    {
        if (draft != null && (draft.steps == null || draft.steps.Count < 3))
            EditorGUILayout.HelpBox("任务书验收至少需要三段完整连招；当前段数不足，可继续编辑和保存。", MessageType.Warning);
        if (errors.Count == 0)
        {
            EditorGUILayout.HelpBox("配置校验通过。保存后可重载到选中的运行中玩家。", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField($"校验：{errors.Count} 项待处理 · 可保存草稿，不能应用到运行时", EditorStyles.boldLabel);
        errorsScroll = EditorGUILayout.BeginScrollView(errorsScroll, GUILayout.MaxHeight(100f));
        foreach (string issue in errors)
        {
            if (GUILayout.Button(issue, EditorStyles.wordWrappedLabel))
            {
                Match match = Regex.Match(issue, @"第\s*(\d+)\s*段");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                    SelectStep(Mathf.Clamp(number - 1, 0, Mathf.Max(0, draft.steps.Count - 1)));
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void LoadAsset(ComboData next)
    {
        StopPreview();
        if (draft != null) DestroyImmediate(draft);
        asset = next;
        draft = null;
        draftObject = null;
        stepsList = null;
        errors.Clear();
        hasUnsavedChanges = false;
        if (asset == null) return;
        draft = Instantiate(asset);
        draft.name = asset.name + "（编辑草稿）";
        draft.hideFlags = EditableDraftFlags;
        sourceRevision = asset.revision;
        sourceAssetJson = EditorJsonUtility.ToJson(asset);
        savedDraftJson = EditorJsonUtility.ToJson(draft);
        selectedStep = 0;
        BindDraft();
        ValidateDraft();
    }

    private bool ConfirmDiscardOrSave()
    {
        if (!hasUnsavedChanges) return true;
        int choice = EditorUtility.DisplayDialogComplex("连招草稿尚未保存", "保存当前草稿后继续，或者放弃本次未保存的修改。", "保存", "取消", "放弃修改");
        return choice == 0 ? SaveDraft() : choice == 2;
    }

    private void CreateAsset()
    {
        if (!ConfirmDiscardOrSave()) return;
        string path = EditorUtility.SaveFilePanelInProject("新建连招资产", "NewCombo", "asset", "请选择连招资产的保存位置。");
        if (string.IsNullOrEmpty(path)) return;
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
        {
            EditorUtility.DisplayDialog("无法覆盖", "目标位置已有资产，请选择其他文件名。", "确定");
            return;
        }
        ComboData next = CreateInstance<ComboData>();
        next.displayName = System.IO.Path.GetFileNameWithoutExtension(path);
        next.steps = new List<ComboStep>();
        next.revision = 0;
        AssetDatabase.CreateAsset(next, path);
        AssetDatabase.SaveAssetIfDirty(next);
        LoadAsset(next);
    }

    private bool SaveDraft()
    {
        if (asset == null || draft == null) return false;
        draftObject.ApplyModifiedProperties();
        if (HasSourceConflict())
        {
            EditorUtility.DisplayDialog("保存冲突", "资产在 Inspector 或其他窗口已有修改。请先保存那里的修改，再加载最新资产；当前草稿未被覆盖。", "确定");
            return false;
        }
        string assetName = asset.name;
        HideFlags assetFlags = asset.hideFlags;
        Undo.RecordObject(asset, "保存连招配置");
        draft.revision = asset.revision + 1;
        EditorUtility.CopySerialized(draft, asset);
        asset.name = assetName;
        asset.hideFlags = assetFlags;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        sourceRevision = asset.revision;
        sourceAssetJson = EditorJsonUtility.ToJson(asset);
        draftObject.Update();
        savedDraftJson = EditorJsonUtility.ToJson(draft);
        hasUnsavedChanges = false;
        ValidateDraft();
        ShowNotification(new GUIContent(errors.Count == 0 ? $"已保存 r{asset.revision}，运行时请明确重载" : $"草稿已保存 r{asset.revision}，仍有配置错误"));
        return true;
    }

    private bool HasSourceConflict()
    {
        // Inspector 保存不会增加 revision，内容指纹同时保护这种外部修改。
        return asset != null && draft != null &&
            (EditorUtility.IsDirty(asset) || asset.revision != sourceRevision ||
             sourceAssetJson != EditorJsonUtility.ToJson(asset));
    }
}
