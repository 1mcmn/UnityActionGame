using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>在隔离场景进入 Play Mode，以真实 Animator 和 Animation Event 检查普通连招链路。</summary>
public class ConfiguredComboIntegrationTests
{
    private GameObject _player;
    private PlayerCombat _combat;
    private Animator _animator;
    private ComboData _data;
    private AnimatorController _controller;
    private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // 域重载后测试框架会重放 SetUp；此时不能再次调用编辑模式场景 API。
        if (!Application.isPlaying)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
        }
        Time.timeScale = 1f;
        _player = new GameObject("Configured combo test player");
        _player.SetActive(false);
        new GameObject("Visual").transform.SetParent(_player.transform);
        _animator = _player.AddComponent<Animator>();
        _combat = _player.AddComponent<PlayerCombat>();
        _player.GetComponent<Rigidbody>().isKinematic = true;
        var motion = _player.AddComponent<PlayerAnimController>();
        _controller = new AnimatorController { name = "Isolated combo test controller" };
        _ownedObjects.Add(_controller);
        _controller.AddLayer("Base Layer");
        _controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        _controller.AddParameter("NextAttack", AnimatorControllerParameterType.Trigger);
        _controller.AddParameter(new AnimatorControllerParameter { name = PlayerCombat.ComboSpeedParameterA, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        _controller.AddParameter(new AnimatorControllerParameter { name = PlayerCombat.ComboSpeedParameterB, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var machine = _controller.layers[0].stateMachine;
        var idle = machine.AddState("Idle");
        idle.motion = Clip("Idle");
        machine.defaultState = idle;
        motion.comboPlaceholderA = Clip("PlaceholderA");
        motion.comboPlaceholderB = Clip("PlaceholderB");
        var slotA = machine.AddState("ComboSlotA");
        slotA.motion = motion.comboPlaceholderA;
        slotA.speedParameterActive = true;
        slotA.speedParameter = PlayerCombat.ComboSpeedParameterA;
        var slotB = machine.AddState("ComboSlotB");
        slotB.motion = motion.comboPlaceholderB;
        slotB.speedParameterActive = true;
        slotB.speedParameter = PlayerCombat.ComboSpeedParameterB;
        _animator.runtimeAnimatorController = _controller;

        _data = ScriptableObject.CreateInstance<ComboData>();
        _ownedObjects.Add(_data);
        _data.revision = 1;
        _data.steps = new List<ComboStep>();
        for (int i = 0; i < 3; i++)
            _data.steps.Add(new ComboStep
            {
                stepId = Guid.NewGuid().ToString("N"), displayName = "Step " + (i + 1),
                animationClip = Clip("Attack " + (i + 1)), damage = 10f + i,
                hitStart = .15f, hitEnd = .4f, comboStart = .55f, comboEnd = .85f,
                hitRadius = .75f, hitOffset = new Vector3(0, 1, 1),
                allowCancel = true, cancelStart = .5f, cancelEnd = .9f
            });
        EditorUtility.ClearDirty(_data);
        _player.SetActive(true);
        _animator.Rebind();
        motion.Initialize();
        _combat.Initialize();
        yield return null;
        Assert.IsTrue(_combat.RequestReload(_data), _combat.ReloadStatus);
        Assert.IsTrue(_combat.UsesConfiguredCombo, _combat.ReloadStatus);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying)
        {
            if (_player != null) UnityEngine.Object.Destroy(_player);
            yield return null;
            foreach (var obj in _ownedObjects) if (obj != null) UnityEngine.Object.Destroy(obj);
            _ownedObjects.Clear();
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
    }

    [UnityTest]
    public IEnumerator AnimatorEvents_ThreeStepsAndInputBoundaries()
    {
        Assert.IsTrue(_combat.StartConfiguredAttack());
        Assert.IsFalse(_combat.StartConfiguredAttack(), "active step must not restart");
        Assert.IsFalse(_combat.TryAdvanceConfiguredCombo(), "early input is discarded");
        yield return Until(() => _combat.HitWindowOpen, "Animation Event did not open hit window");
        yield return Until(() => !_combat.HitWindowOpen && _combat.CurrentStepTime >= .4f, "Animation Event did not close hit window");
        for (int next = 1; next < 3; next++)
        {
            yield return Until(() => _combat.ComboInputOpen, "combo input window never opened");
            int previousToken = _combat.AttackToken;
            Assert.IsTrue(_combat.TryAdvanceConfiguredCombo());
            Assert.AreEqual(next, _combat.CurrentStepIndex);
            Assert.AreNotEqual(previousToken, _combat.AttackToken);
            Assert.IsFalse(_combat.TryAdvanceConfiguredCombo(), "same frame must not advance twice");
            yield return null;
        }
        yield return Until(() => _combat.CurrentStepTime >= .6f, "last animation did not progress");
        Assert.IsFalse(_combat.ComboInputOpen, "last step has no next step");
        Assert.IsFalse(_combat.TryAdvanceConfiguredCombo());
        yield return Until(() => _combat.ConfiguredAttackFinished(), "last animation never finished");
        _combat.ResetAllTimers();
        Assert.IsFalse(_combat.IsComboActive);
        Assert.IsFalse(_combat.HitWindowOpen);
    }

    [UnityTest]
    public IEnumerator Reload_UsesSnapshotAndWaitsForAttackToFinish()
    {
        _data.steps[0].damage = 77f;
        Assert.AreEqual(10f, LoadedSteps()[0].damage, "editing must not mutate loaded snapshot");
        _data.revision = 2;
        EditorUtility.SetDirty(_data);
        Assert.IsFalse(_combat.RequestReload(_data), "unsaved data must be rejected");
        EditorUtility.ClearDirty(_data);
        Assert.IsTrue(_combat.StartConfiguredAttack());
        Assert.IsTrue(_combat.RequestReload(_data));
        Assert.AreEqual(1, _combat.LoadedRevision, "attack retains previous version");
        Assert.AreEqual(10f, LoadedSteps()[0].damage);
        yield return Until(() => _combat.ConfiguredAttackFinished(), "animation never finished");
        _combat.ResetAllTimers();
        yield return null;
        Assert.AreEqual(2, _combat.LoadedRevision);
        Assert.AreEqual(77f, LoadedSteps()[0].damage);
        _data.steps[0].animationClip = null;
        Assert.IsFalse(_combat.RequestReload(_data), "invalid data must be rejected");
        Assert.AreEqual(2, _combat.LoadedRevision);
        Assert.AreEqual(77f, LoadedSteps()[0].damage);
    }

    [UnityTest]
    public IEnumerator PlaybackSpeeds_DriveSeparateSlots_AndKeepEventsAndInputOnClipTimeline()
    {
        _data.steps[0].playbackSpeed = 2f;
        _data.steps[1].playbackSpeed = .5f;
        _data.steps[2].playbackSpeed = 3f;
        _data.steps[0].soundEvents.Add(new ComboSoundEvent { time = .2f, soundId = "speed_test" });
        EditorUtility.ClearDirty(_data);
        Assert.IsTrue(_combat.RequestReload(_data), _combat.ReloadStatus);
        Assert.IsTrue(_combat.StartConfiguredAttack());
        Assert.AreEqual(2f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterA));
        Assert.AreEqual(1f, _animator.speed, "only combo states use the multiplier");
        yield return Until(() => _combat.HitWindowOpen, "faster clip did not open hit window");
        Assert.GreaterOrEqual(_combat.CurrentStepTime, .15f);
        yield return Until(() => _combat.CurrentStepTime >= .3f, "faster clip did not advance");
        Assert.AreEqual(2f, _animator.GetCurrentAnimatorStateInfo(0).speedMultiplier);
        var fired = (HashSet<string>)typeof(PlayerCombat).GetField("_firedSounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_combat);
        Assert.AreEqual(1, fired.Count, "sound event follows the same accelerated clip timeline");
        yield return Until(() => _combat.ComboInputOpen, "accelerated input window never opened");
        Assert.GreaterOrEqual(_combat.CurrentStepTime, .55f);
        Assert.IsTrue(_combat.TryAdvanceConfiguredCombo());
        Assert.AreEqual(.5f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterB));
        Assert.AreEqual(2f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterA), "outgoing slot keeps its speed while blending");
        yield return Until(() => _combat.CurrentStepTime >= .2f, "slow clip did not advance");
        Assert.AreEqual(.5f, _animator.GetCurrentAnimatorStateInfo(0).speedMultiplier);
        yield return Until(() => _combat.ComboInputOpen, "slower input window never opened");
        Assert.IsTrue(_combat.TryAdvanceConfiguredCombo());
        Assert.AreEqual(3f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterA));
        _combat.ResetAllTimers();
        Assert.AreEqual(1f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterA));
        Assert.AreEqual(1f, _animator.GetFloat(PlayerCombat.ComboSpeedParameterB));
        Assert.AreEqual(1f, _animator.speed);
    }

    [UnityTest]
    public IEnumerator MainScene_InitialLoadAndActualHumanoidAnimationEvents()
    {
        // 使用已保存的主场景，不调用编辑器配置入口，不手动补一次重载。
        // 检查开始界面激活玩家后的首次接线，而非只有人工 RequestReload 才可运行。
        yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/DEMO City Crossing.unity", LoadSceneMode.Single);
        Assert.NotNull(GameManager.Instance, "main scene GameManager is missing");
        foreach (var ai in UnityEngine.Object.FindObjectsOfType<EnemyAI>(true)) ai.enabled = false;
        GameManager.Instance.StartGame();
        yield return null;
        yield return null;
        _player = GameObject.FindGameObjectWithTag("Player");
        Assert.NotNull(_player, "main scene Player tag is missing");
        _combat = _player.GetComponent<PlayerCombat>();
        _animator = _player.GetComponentInChildren<Animator>();
        Assert.IsTrue(_combat.UsesConfiguredCombo, _combat.ReloadStatus);
        Assert.AreEqual(3, _combat.StepCount);
        Assert.IsTrue(_animator.isHuman, "the real demo character must be Humanoid");
        Assert.IsTrue(_combat.Data.steps.TrueForAll(step => step.animationClip.humanMotion));
        // 本用例隔离输入、战斗AI与位移，保留实际Animator和事件转发对象。
        _player.GetComponent<ThirdPersonController>().enabled = false;
        Assert.IsTrue(_combat.StartConfiguredAttack());
        yield return Until(() => _combat.HitWindowOpen, "real Humanoid animation event did not open hit window");
        yield return Until(() => !_combat.HitWindowOpen && _combat.CurrentStepTime >= _combat.Data.steps[0].hitEnd,
            "real Humanoid animation event did not close hit window");
        for (int next = 1; next < 3; next++)
        {
            yield return Until(() => _combat.ComboInputOpen, "real demo combo window did not open");
            Assert.IsTrue(_combat.TryAdvanceConfiguredCombo());
            Assert.AreEqual(next, _combat.CurrentStepIndex);
            yield return null;
        }
        yield return Until(() => _combat.ConfiguredAttackFinished(), "real last animation did not finish");
        Assert.IsFalse(_combat.TryAdvanceConfiguredCombo());
    }

    [UnityTest]
    public IEnumerator EventHit_MultipleCollidersDamageOnceAndCancellationStopsOldEvents()
    {
        var target = new GameObject("Multi collider enemy");
        _ownedObjects.Add(target);
        target.transform.position = new Vector3(0, 1, 1);
        target.AddComponent<BoxCollider>().size = Vector3.one * .3f;
        var enemy = target.AddComponent<Enemy>();
        var extra = new GameObject("Extra collider");
        extra.transform.SetParent(target.transform, false);
        extra.AddComponent<SphereCollider>().radius = .2f;
        typeof(PlayerCombat).GetField("enemyLayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_combat, (LayerMask)(1 << target.layer));
        Physics.SyncTransforms();
        float before = enemy.CurrentHealth;
        Assert.IsTrue(_combat.StartConfiguredAttack());
        yield return Until(() => enemy.CurrentHealth < before, "event driven attack never hit enemy");
        float after = enemy.CurrentHealth;
        yield return Until(() => _combat.CurrentStepTime >= .5f, "animation did not leave hit interval");
        Assert.AreEqual(before - 3f, after, .001f, "10 damage with default 70% reduction");
        Assert.AreEqual(after, enemy.CurrentHealth, "multiple colliders/repeated samples must not duplicate damage");
        _combat.ResetAllTimers();
        Assert.IsTrue(_combat.StartConfiguredAttack());
        _combat.ResetAllTimers();
        yield return new WaitForSeconds(.5f);
        Assert.AreEqual(after, enemy.CurrentHealth, "cancelled animation events must not deal damage");
    }

    private AnimationClip Clip(string name)
    {
        var clip = new AnimationClip { name = name };
        clip.SetCurve("Visual", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
        _ownedObjects.Add(clip);
        return clip;
    }

    private List<ComboStep> LoadedSteps() => (List<ComboStep>)typeof(PlayerCombat)
        .GetField("_loadedSteps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_combat);

    private static IEnumerator Until(Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(condition(), message);
    }
}
