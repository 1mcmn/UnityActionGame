using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// PlayerCombat unit tests (EditMode, no Play mode needed):
/// combo input buffering / window logic, damage + death, invulnerability,
/// hit-direction calculation and attack progress.
/// </summary>
public class PlayerCombatTests
{
    private GameObject _go;
    private PlayerCombat _combat;
    private Action _deathHandler;
    private bool _died;
    private int _deathCount;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestPlayer");
        _combat = _go.AddComponent<PlayerCombat>(); // RequireComponent auto-adds Rigidbody
        _combat.Initialize();                       // currentHealth = maxHealth

        _died = false;
        _deathCount = 0;
        _deathHandler = () => { _died = true; _deathCount++; };
        PlayerCombat.OnPlayerDeath += _deathHandler;
    }

    [TearDown]
    public void TearDown()
    {
        PlayerCombat.OnPlayerDeath -= _deathHandler;
        if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
    }

    // ---- combo buffering / window ----

    [Test]
    public void ComboFlow_InputBeforeWindow_IsDiscarded()
    {
        _combat.StartAttack();           // attackTimer = 0.45, step 0
        _combat.DecrementTimer(0.1f);    // 0.35 -> still in windup (window not open)

        _combat.BufferCombo();
        Assert.IsFalse(_combat.HasBufferedCombo, "input before the window must not be retained");
        Assert.IsFalse(_combat.ConsumeBufferedCombo());

        _combat.DecrementTimer(0.2f);   // progress 0.667: the default 0.35 window is now open
        Assert.IsFalse(_combat.ConsumeBufferedCombo(), "old input must not become valid when the window opens");
        _combat.BufferCombo();
        Assert.IsTrue(_combat.ConsumeBufferedCombo(), "a fresh input inside the window chains");
        Assert.IsTrue(_combat.IsAttacking, "combo consume restarts the attack timer");
        Assert.AreEqual(1, GetComboStep(), "combo should have advanced to step 2");
    }

    [Test]
    public void ComboFlow_AttackEnded_BufferIsDiscarded()
    {
        _combat.StartAttack();
        _combat.DecrementTimer(0.5f);    // attack finished

        _combat.BufferCombo();
        Assert.IsFalse(_combat.ConsumeBufferedCombo(), "stale buffer must be dropped");

        _combat.ResetAllTimers();
        _combat.StartAttack();
        Assert.AreEqual(0, GetComboStep(), "new attack restarts at step 1");
    }

    [Test]
    public void ComboFlow_NoBuffer_NoAdvance()
    {
        _combat.StartAttack();
        _combat.DecrementTimer(0.3f);    // window open
        Assert.IsFalse(_combat.ConsumeBufferedCombo());
    }

    [Test]
    public void AttackProgress_StartsAtZero_AdvancesWithTime()
    {
        _combat.StartAttack();
        Assert.AreEqual(0f, _combat.AttackProgress01, 0.001f, "0 = just started");

        _combat.DecrementTimer(0.225f);
        Assert.AreEqual(0.5f, _combat.AttackProgress01, 0.001f, "half the attack elapsed");
    }

    [Test]
    public void StartAttack_DuringSameSwing_DoesNotRestartOrChangeToken()
    {
        _combat.StartAttack();
        _combat.DecrementTimer(0.1f);
        int token = _combat.AttackToken;
        float progress = _combat.AttackProgress01;
        _combat.StartAttack();
        Assert.AreEqual(token, _combat.AttackToken);
        Assert.AreEqual(progress, _combat.AttackProgress01);
    }

    [Test]
    public void FinalStep_RepeatedInput_DoesNotStartAnotherSwing()
    {
        _combat.StartAttack();
        for (int i = 0; i < 4; i++)
        {
            _combat.DecrementTimer(0.3f);
            _combat.BufferCombo();
            Assert.IsTrue(_combat.ConsumeBufferedCombo());
        }
        _combat.DecrementTimer(0.3f);
        int token = _combat.AttackToken;
        float progress = _combat.AttackProgress01;
        for (int i = 0; i < 3; i++)
        {
            _combat.BufferCombo();
            Assert.IsFalse(_combat.ConsumeBufferedCombo());
        }
        Assert.AreEqual(4, GetComboStep());
        Assert.AreEqual(token, _combat.AttackToken, "rejected input must not schedule new damage");
        Assert.AreEqual(progress, _combat.AttackProgress01);
    }

    [Test]
    public void CancelAttack_InvalidatesTokenAndClearsInput()
    {
        _combat.StartAttack();
        _combat.DecrementTimer(0.3f);
        _combat.BufferCombo();
        int token = _combat.AttackToken;
        _combat.ResetAllTimers();
        Assert.AreNotEqual(token, _combat.AttackToken);
        Assert.IsFalse(_combat.IsComboActive);
        Assert.IsFalse(_combat.IsAttacking);
        Assert.IsFalse(_combat.HasBufferedCombo);
        Assert.IsFalse(_combat.ConsumeBufferedCombo());
    }

    // ---- damage / death / invulnerability ----

    [Test]
    public void TakeDamage_ReducesHealth_AndFiresDeathAtZero()
    {
        _combat.TakeDamage(30f);
        Assert.AreEqual(70f, _combat.CurrentHealth, 0.001f);

        _combat.TakeDamage(200f);
        Assert.AreEqual(0f, _combat.CurrentHealth, 0.001f, "health clamps at 0");
        Assert.IsTrue(_died, "death event must fire once health hits 0");
        _combat.TakeDamage(10f);
        Assert.AreEqual(1, _deathCount, "dead characters must not fire the death event again");
    }

    [Test]
    public void TakeDamage_WhileInvulnerable_IsIgnored()
    {
        _combat.SetInvulnerable(true);
        _combat.TakeDamage(50f);
        Assert.AreEqual(100f, _combat.CurrentHealth, 0.001f);
        Assert.IsFalse(_died);
    }

    // ---- hit direction (parry / hit animation selection) ----

    [Test]
    public void CalcHitType_Directions_AreCorrect()
    {
        _go.transform.position = Vector3.zero;
        _go.transform.rotation = Quaternion.identity; // forward = +Z

        Assert.AreEqual((int)PlayerHitType.Forward, CalcHitType(new Vector3(0f, 0f, 2f)), "attacker in front");
        Assert.AreEqual((int)PlayerHitType.Right, CalcHitType(new Vector3(2f, 0f, 0f)), "attacker on the right");
        Assert.AreEqual((int)PlayerHitType.Left, CalcHitType(new Vector3(-2f, 0f, 0f)), "attacker on the left");
        Assert.AreEqual((int)PlayerHitType.Forward, CalcHitType(null), "no position -> forward");
    }

    [Test]
    public void KeyboardForward_IgnoresOtherAxisHorizontalDrift()
    {
        var input = PlayerLocomotion.ResolveMoveAxes(new Vector2(-.4f, 1f), false, false, true, false);
        Assert.AreEqual(Vector2.up, input);
        Assert.AreEqual(new Vector2(-.4f, .5f), PlayerLocomotion.ResolveMoveAxes(new Vector2(-.4f, .5f), false, false, false, false));
    }

    [Test]
    public void CameraRelativeMovement_UsesAnOrthogonalHorizontalBasis()
    {
        var camera = new GameObject("Movement basis camera");
        try
        {
            camera.transform.rotation = Quaternion.Euler(48f, 30f, 22f);
            var motion = _go.AddComponent<PlayerLocomotion>();
            motion.Initialize(_go.GetComponent<Rigidbody>(), camera.transform);
            Vector3 forward = motion.GetCameraRelativeInput(0f, 1f);
            Vector3 right = motion.GetCameraRelativeInput(1f, 0f);
            Assert.AreEqual(0f, forward.y, .0001f);
            Assert.AreEqual(0f, right.y, .0001f);
            Assert.AreEqual(0f, Vector3.Dot(forward, right), .0001f);
            Assert.AreEqual(1f, forward.magnitude, .0001f);
        }
        finally { UnityEngine.Object.DestroyImmediate(camera); }
    }

    [Test]
    public void CameraFollowMovementHeading_DoesNotDriftWithSmoothedCameraRotation()
    {
        var camera = new GameObject("Follow heading camera");
        try
        {
            var follow = camera.AddComponent<CameraFollow>();
            typeof(CameraFollow).GetField("orientationInitialized", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(follow, true);
            typeof(CameraFollow).GetField("yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(follow, 0f);
            var motion = _go.AddComponent<PlayerLocomotion>();
            motion.Initialize(_go.GetComponent<Rigidbody>(), camera.transform);
            camera.transform.rotation = Quaternion.Euler(20f, -25f, 0f);
            Assert.Less(Vector3.Distance(Vector3.forward, motion.GetCameraRelativeInput(0f, 1f)), .0001f);
            camera.transform.rotation = Quaternion.Euler(20f, -10f, 0f);
            Assert.Less(Vector3.Distance(Vector3.forward, motion.GetCameraRelativeInput(0f, 1f)), .0001f);
        }
        finally { UnityEngine.Object.DestroyImmediate(camera); }
    }

    [TestCase(PlayerState.Move, .6f)]
    [TestCase(PlayerState.Run, .96f)]
    public void LocomotionRootMotion_IgnoresAnimationSidewaysDelta(PlayerState state, float expectedForward)
    {
        var locomotion = _go.AddComponent<PlayerLocomotion>();
        locomotion.Initialize(_go.GetComponent<Rigidbody>(), null);
        _go.AddComponent<PlayerAnimController>();
        var controller = _go.AddComponent<ThirdPersonController>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ThirdPersonController).GetField("locomotion", flags).SetValue(controller, locomotion);
        typeof(ThirdPersonController).GetField("currentState", flags).SetValue(controller, state);
        typeof(ThirdPersonController).GetField("moveInput", flags).SetValue(controller, Vector3.forward);
        Vector3 delta = controller.EvaluateRootMotion(new Vector3(-4f, 0f, 2f), .1f);
        Assert.AreEqual(0f, delta.x, .0001f, "sideways motion in the clip must not steer locomotion");
        Assert.AreEqual(expectedForward, delta.z, .0001f);
    }

    [TestCase(0f, -20f)]
    [TestCase(.5f, -10f)]
    [TestCase(1f, 0f)]
    public void RunHeadingCorrection_BlendsWithoutRotatingThePhysicalRoot(float weight, float expectedYaw)
    {
        Quaternion rootBefore = _go.transform.rotation;
        Quaternion body = Quaternion.Euler(0f, -20f, 0f);
        Quaternion corrected = PlayerAnimController.CalculateRunHeadingCorrection(body, Vector3.forward, weight, 0f) * body;
        Assert.AreEqual(expectedYaw, Vector3.SignedAngle(Vector3.forward, corrected * Vector3.forward, Vector3.up), .001f);
        Assert.AreEqual(rootBefore, _go.transform.rotation);
    }

    [Test]
    public void RunHeadingCorrection_AlignsVisibleFacingRatherThanTheBodyCenter()
    {
        Vector3 faceForward = Quaternion.Euler(0f, -25f, 0f) * Vector3.forward;
        Vector3 rootForward = Quaternion.Euler(0f, 40f, 0f) * Vector3.forward;
        Quaternion correction = PlayerAnimController.CalculateRunHeadingCorrection(faceForward, rootForward, 1f, 0f);
        Assert.AreEqual(0f, Vector3.SignedAngle(rootForward, correction * faceForward, Vector3.up), .001f);
        Quaternion offset = PlayerAnimController.CalculateRunHeadingCorrection(faceForward, rootForward, 1f, 8f);
        Assert.AreEqual(8f, Vector3.SignedAngle(rootForward, offset * faceForward, Vector3.up), .001f);
    }

    [Test]
    public void RunHeadingCorrection_DegenerateOrNonFiniteDirection_IsIgnored()
    {
        Assert.AreEqual(Quaternion.identity, PlayerAnimController.CalculateRunHeadingCorrection(Vector3.up, Vector3.forward, 1f, 0f));
        Assert.AreEqual(Quaternion.identity, PlayerAnimController.CalculateRunHeadingCorrection(new Vector3(float.NaN, 0f, 1f), Vector3.forward, 1f, 0f));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(1f)]
    public void RunHeadingCorrection_HipsAndFaceUseIndependentBlendedTargets(float weight)
    {
        Quaternion hips = Quaternion.Euler(0f, -28f, 0f);
        Quaternion face = Quaternion.Euler(0f, 20f, 0f);
        Quaternion hipCorrection = PlayerAnimController.CalculateRunHeadingCorrection(hips, Vector3.forward, weight, 0f);
        Quaternion faceCorrection = PlayerAnimController.CalculateRunHeadingCorrection(face, Vector3.forward, weight, 0f);
        Quaternion torsoCorrection = faceCorrection * Quaternion.Inverse(hipCorrection);
        Vector3 correctedHips = hipCorrection * hips * Vector3.forward;
        Vector3 correctedFace = torsoCorrection * hipCorrection * face * Vector3.forward;
        Assert.AreEqual(-28f * (1f - weight), Vector3.SignedAngle(Vector3.forward, correctedHips, Vector3.up), .001f);
        Assert.AreEqual(20f * (1f - weight), Vector3.SignedAngle(Vector3.forward, correctedFace, Vector3.up), .001f);
    }

    // ---- reflection helpers ----

    private int GetComboStep()
    {
        return (int)GetField("_comboStep");
    }

    private int CalcHitType(Vector3? attackerPosition)
    {
        var m = typeof(PlayerCombat).GetMethod("CalcHitType", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(m);
        return (int)m.Invoke(_combat, new object[] { attackerPosition });
    }

    private object GetField(string name)
    {
        var f = typeof(PlayerCombat).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, "field not found: " + name);
        return f.GetValue(_combat);
    }
}
