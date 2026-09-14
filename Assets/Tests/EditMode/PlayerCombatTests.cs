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

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestPlayer");
        _combat = _go.AddComponent<PlayerCombat>(); // RequireComponent auto-adds Rigidbody
        _combat.Initialize();                       // currentHealth = maxHealth

        _died = false;
        _deathHandler = () => _died = true;
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
    public void ComboFlow_BufferBeforeWindow_IsHeldThenConsumed()
    {
        _combat.StartAttack();           // attackTimer = 0.45, step 0
        _combat.DecrementTimer(0.1f);    // 0.35 -> still in windup (window not open)

        _combat.BufferCombo();
        Assert.IsFalse(_combat.ConsumeBufferedCombo(), "buffer must be held during windup");

        _combat.DecrementTimer(0.15f);   // 0.20 -> window open (<= 0.45*0.55)
        Assert.IsTrue(_combat.ConsumeBufferedCombo(), "buffered input must chain the next step");
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

    // ---- damage / death / invulnerability ----

    [Test]
    public void TakeDamage_ReducesHealth_AndFiresDeathAtZero()
    {
        _combat.TakeDamage(30f);
        Assert.AreEqual(70f, _combat.CurrentHealth, 0.001f);

        _combat.TakeDamage(200f);
        Assert.AreEqual(0f, _combat.CurrentHealth, 0.001f, "health clamps at 0");
        Assert.IsTrue(_died, "death event must fire once health hits 0");
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
