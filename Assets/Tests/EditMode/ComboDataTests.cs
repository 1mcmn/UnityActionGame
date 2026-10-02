using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ComboData (combo data asset) unit tests:
/// default values + ScriptableObject asset save/load roundtrip.
/// Directly supports the thesis claim "editor edits data assets -> runtime reads them".
/// </summary>
public class ComboDataTests
{
    private string _testFolder;
    private readonly List<UnityEngine.Object> _temporaryObjects = new List<UnityEngine.Object>();

    [SetUp]
    public void SetUp()
    {
        // Only remove assets owned by this invocation, never a shared Generated directory.
        string folderName = "_ComboDataTests_" + Guid.NewGuid().ToString("N");
        _testFolder = null;
        string folderGuid = AssetDatabase.CreateFolder("Assets/Tests", folderName);
        Assert.IsNotEmpty(folderGuid, "could not create the isolated test asset folder");
        _testFolder = AssetDatabase.GUIDToAssetPath(folderGuid);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var temporary in _temporaryObjects)
            if (temporary != null && !EditorUtility.IsPersistent(temporary))
                UnityEngine.Object.DestroyImmediate(temporary);
        _temporaryObjects.Clear();
        if (!string.IsNullOrEmpty(_testFolder) && AssetDatabase.IsValidFolder(_testFolder))
            AssetDatabase.DeleteAsset(_testFolder);
    }

    [Test]
    public void Defaults_AreSane()
    {
        var data = CreateData();

        Assert.AreEqual(0.45f, data.attackDuration, 0.001f);
        Assert.AreEqual(2.5f, data.attackRadius, 0.001f);
        Assert.AreEqual(0.35f, data.comboWindowPercent, 0.001f);
        Assert.IsFalse(data.HasSteps, "old assets keep the inline fallback until configured");
        Assert.AreEqual(5, data.comboDamages.Length);
        Assert.AreEqual(15f, data.comboDamages[0], 0.001f);

        for (int i = 1; i < data.comboDamages.Length; i++)
            Assert.Greater(data.comboDamages[i], data.comboDamages[i - 1],
                "combo damages should scale up per step");
    }

    [Test]
    public void Asset_Roundtrip_PreservesFields()
    {
        string path = _testFolder + "/TestCombo.asset";

        var data = CreateData();
        data.attackDuration = 0.6f;
        data.attackRadius = 3f;
        data.comboWindowPercent = 0.5f;
        data.comboDamages = new[] { 10f, 20f, 30f };

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssetIfDirty(data);
        Resources.UnloadAsset(data);

        var loaded = AssetDatabase.LoadAssetAtPath<ComboData>(path);
        Assert.NotNull(loaded);
        Assert.AreEqual(0.6f, loaded.attackDuration, 0.001f);
        Assert.AreEqual(3f, loaded.attackRadius, 0.001f);
        Assert.AreEqual(0.5f, loaded.comboWindowPercent, 0.001f);
        CollectionAssert.AreEqual(new[] { 10f, 20f, 30f }, loaded.comboDamages);
    }

    [Test]
    public void DamageStep_Clamps_OutOfRangeIndex()
    {
        var data = CreateData();
        data.comboDamages = new[] { 15f, 18f, 22f };

        // mirrors PlayerCombat.CurrentAttackDamage clamping: never throw, never index < 0
        int first = Mathf.Clamp(0, 0, data.comboDamages.Length - 1);
        int last  = Mathf.Clamp(99, 0, data.comboDamages.Length - 1);

        Assert.AreEqual(15f, data.comboDamages[first], 0.001f);
        Assert.AreEqual(22f, data.comboDamages[last], 0.001f);
    }

    [Test]
    public void ConfiguredAsset_Roundtrip_PreservesStepsAndAnimationReference()
    {
        var data = CreateValidData();
        data.displayName = "Three step acceptance combo";
        data.revision = 7;
        data.steps[1].damage = 42f;
        data.steps[1].playbackSpeed = 1.75f;
        data.steps[1].rootMotionScaleXZ = 1.5f;
        data.steps[1].soundEvents.Add(new ComboSoundEvent { time = 0.2f, soundId = "atk02_swing" });
        string clipPath = _testFolder + "/Attack.anim";
        AssetDatabase.CreateAsset(data.steps[0].animationClip, clipPath);
        string path = _testFolder + "/ConfiguredCombo.asset";
        string stepId = data.steps[1].stepId;
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssetIfDirty(data);
        Resources.UnloadAsset(data);

        var loaded = AssetDatabase.LoadAssetAtPath<ComboData>(path);
        Assert.NotNull(loaded);
        Assert.AreEqual(7, loaded.revision);
        Assert.AreEqual("Three step acceptance combo", loaded.displayName);
        Assert.AreEqual(3, loaded.steps.Count);
        Assert.AreEqual(stepId, loaded.steps[1].stepId);
        Assert.AreEqual(42f, loaded.steps[1].damage);
        Assert.AreEqual(1.75f, loaded.steps[1].playbackSpeed);
        Assert.AreEqual(1.5f, loaded.steps[1].rootMotionScaleXZ);
        Assert.AreEqual(clipPath, AssetDatabase.GetAssetPath(loaded.steps[1].animationClip));
        Assert.AreEqual("atk02_swing", loaded.steps[1].soundEvents[0].soundId);
        Assert.AreEqual(0.2f, loaded.steps[1].soundEvents[0].time);
        Assert.IsEmpty(loaded.Validate());
    }

    [Test]
    public void Validate_ValidThreeSteps_HasNoErrors()
    {
        Assert.IsEmpty(CreateValidData().Validate());
    }

    [Test]
    public void Validate_EmptyClip_ReportsError()
    {
        var data = CreateValidData();
        data.steps[0].animationClip = null;
        Assert.IsTrue(data.Validate().Exists(error => error.Contains("未指定动画")));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(.05f)]
    [TestCase(5.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Validate_InvalidPlaybackSpeed_ReportsError(float speed)
    {
        var data = CreateValidData();
        data.steps[0].playbackSpeed = speed;
        Assert.IsTrue(data.Validate().Exists(error => error.Contains("播放倍速")));
    }

    [Test]
    public void PlaybackSpeed_ChangesDuration_AndPreservesClipTimelineWindows()
    {
        var step = CreateValidData().steps[0];
        Assert.AreEqual(1f, step.playbackSpeed, "unconfigured assets preserve original speed");
        float start = step.comboStart;
        float end = step.comboEnd;
        step.playbackSpeed = 2f;
        Assert.AreEqual(step.animationClip.length / 2f, step.PlaybackDuration);
        Assert.AreEqual(start, step.comboStart);
        Assert.AreEqual(end, step.comboEnd);
        Assert.IsTrue(step.AcceptsCombo(start));
        Assert.IsFalse(step.AcceptsCombo(end));
        var snapshot = step.Copy();
        step.playbackSpeed = 3f;
        Assert.AreEqual(2f, snapshot.playbackSpeed);
    }

    [Test]
    public void Validate_DuplicateStepIds_ReportsError()
    {
        var data = CreateValidData();
        data.steps[1].stepId = data.steps[0].stepId;
        Assert.IsTrue(data.Validate().Exists(error => error.Contains("内部标识")));
    }

    [TestCase(-0.1f, 0.5f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(0.6f, 0.5f)]
    [TestCase(0.2f, 1.1f)]
    public void Validate_InvalidHitWindow_ReportsError(float start, float end)
    {
        var data = CreateValidData();
        data.steps[0].hitStart = start;
        data.steps[0].hitEnd = end;
        Assert.IsTrue(data.Validate().Exists(error => error.Contains("判定区间")));
    }

    [Test]
    public void Validate_InvalidComboAndCancelWindows_ReportsBoth()
    {
        var data = CreateValidData();
        data.steps[0].comboEnd = data.steps[0].comboStart;
        data.steps[0].cancelEnd = 2f;
        var errors = data.Validate();
        Assert.IsTrue(errors.Exists(error => error.Contains("接段区间")));
        Assert.IsTrue(errors.Exists(error => error.Contains("取消区间")));
    }

    [Test]
    public void Validate_NonFiniteValuesAndOutOfRangeSound_ReportsErrors()
    {
        var data = CreateValidData();
        data.steps[0].damage = float.NaN;
        data.steps[0].hitRadius = float.PositiveInfinity;
        data.steps[0].soundEvents.Add(new ComboSoundEvent { time = 1f, soundId = "atk01_swing" });
        var errors = data.Validate();
        Assert.IsTrue(errors.Exists(error => error.Contains("伤害")));
        Assert.IsTrue(errors.Exists(error => error.Contains("判定半径")));
        Assert.IsTrue(errors.Exists(error => error.Contains("音效事件")));
    }

    [Test]
    public void Copy_IsolatesEditableValuesAndSoundEvents()
    {
        var original = CreateValidData().steps[0];
        original.soundEvents.Add(new ComboSoundEvent { time = 0.1f, soundId = "original" });
        var snapshot = original.Copy();
        original.damage = 999f;
        original.soundEvents[0].soundId = "edited";
        original.soundEvents.Add(new ComboSoundEvent());

        Assert.AreEqual(15f, snapshot.damage);
        Assert.AreEqual("original", snapshot.soundEvents[0].soundId);
        Assert.AreEqual(1, snapshot.soundEvents.Count);
        Assert.AreEqual(original.stepId, snapshot.stepId, "snapshot keeps stable segment identity");
        Assert.AreSame(original.animationClip, snapshot.animationClip, "clip assets are shared references");
    }

    [Test]
    public void StepWindows_IncludeStart_ExcludeEnd()
    {
        var step = new ComboStep { comboStart = 0.2f, comboEnd = 0.5f, cancelStart = 0.3f, cancelEnd = 0.6f };
        Assert.IsFalse(step.AcceptsCombo(0.19f));
        Assert.IsTrue(step.AcceptsCombo(0.2f));
        Assert.IsFalse(step.AcceptsCombo(0.5f));
        Assert.IsFalse(step.CanCancel(0.29f));
        Assert.IsTrue(step.CanCancel(0.3f));
        Assert.IsFalse(step.CanCancel(0.6f));
        step.allowCancel = false;
        Assert.IsFalse(step.CanCancel(0.4f));
    }

    [Test]
    public void EditorDraft_AnimationNameAndDamage_AreEditableAndIsolated()
    {
        var source = CreateValidData();
        var replacementClip = new AnimationClip { name = "ReplacementAttack" };
        _temporaryObjects.Add(replacementClip);
        var window = CreateComboEditorWindow();
        try
        {
            InvokeEditorMethod(window, "LoadAsset", source);
            var draft = GetEditorDraft(window);
            Assert.AreNotSame(source, draft);
            Assert.AreEqual(HideFlags.None, draft.hideFlags & HideFlags.NotEditable);

            using (var serialized = new SerializedObject(draft))
            {
                var step = serialized.FindProperty("steps").GetArrayElementAtIndex(0);
                var animation = step.FindPropertyRelative("animationClip");
                var name = step.FindPropertyRelative("displayName");
                var damage = step.FindPropertyRelative("damage");
                Assert.IsTrue(animation.editable, "animation picker must be enabled");
                Assert.IsTrue(name.editable, "step name must be enabled");
                Assert.IsTrue(damage.editable, "damage field must be enabled");
                animation.objectReferenceValue = replacementClip;
                name.stringValue = "Edited attack";
                damage.floatValue = 37f;
                Assert.IsTrue(serialized.ApplyModifiedProperties());
            }

            Assert.AreSame(replacementClip, draft.steps[0].animationClip);
            Assert.AreEqual("Edited attack", draft.steps[0].displayName);
            Assert.AreEqual(37f, draft.steps[0].damage);
            Assert.AreNotSame(replacementClip, source.steps[0].animationClip);
            Assert.AreEqual("Attack 1", source.steps[0].displayName);
            Assert.AreEqual(15f, source.steps[0].damage, "draft editing must not save implicitly");
            Assert.AreEqual(HideFlags.None, source.hideFlags);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    }

    [Test]
    public void EditorDraft_Rebind_RemovesLegacyReadOnlyFlagWithoutLosingEdits()
    {
        var window = CreateComboEditorWindow();
        try
        {
            InvokeEditorMethod(window, "LoadAsset", CreateValidData());
            var draft = GetEditorDraft(window);
            draft.steps[0].damage = 77f;
            draft.hideFlags = HideFlags.HideAndDontSave;

            InvokeEditorMethod(window, "BindDraft");

            Assert.AreSame(draft, GetEditorDraft(window), "existing draft must be preserved");
            Assert.AreEqual(HideFlags.None, draft.hideFlags & HideFlags.NotEditable);
            Assert.AreEqual(HideFlags.DontSave, draft.hideFlags & HideFlags.DontSave);
            Assert.AreEqual(77f, draft.steps[0].damage);
            using (var serialized = new SerializedObject(draft))
                Assert.IsTrue(serialized.FindProperty("steps").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("animationClip").editable);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    }

    // 测试程序集不能直接引用预定义的 Assembly-CSharp-Editor，反射访问正式窗口。
    private static EditorWindow CreateComboEditorWindow()
    {
        var type = Type.GetType("ComboEditorWindow, Assembly-CSharp-Editor");
        Assert.NotNull(type, "formal combo editor assembly must be loaded");
        return (EditorWindow)ScriptableObject.CreateInstance(type);
    }

    private static void InvokeEditorMethod(EditorWindow window, string methodName, params object[] arguments)
    {
        var method = window.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(window, arguments);
    }

    private static ComboData GetEditorDraft(EditorWindow window)
    {
        var field = window.GetType().GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (ComboData)field.GetValue(window);
    }

    private ComboData CreateData()
    {
        var data = ScriptableObject.CreateInstance<ComboData>();
        _temporaryObjects.Add(data);
        return data;
    }

    private ComboData CreateValidData()
    {
        var data = CreateData();
        var clip = new AnimationClip { name = "TestAttack" };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 0f));
        _temporaryObjects.Add(clip);
        for (int i = 0; i < 3; i++)
            data.steps.Add(new ComboStep { animationClip = clip, displayName = "Attack " + (i + 1) });
        return data;
    }
}
