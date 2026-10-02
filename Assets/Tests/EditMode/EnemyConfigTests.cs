using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EnemyConfig (enemy config asset) unit tests:
/// defaults + ScriptableObject asset save/load roundtrip.
/// </summary>
public class EnemyConfigTests
{
    private string _testFolder;
    private readonly List<EnemyConfig> _temporaryConfigs = new List<EnemyConfig>();

    [SetUp]
    public void SetUp()
    {
        // Each case owns its directory; never remove another test's assets.
        string folderName = "_EnemyConfigTests_" + Guid.NewGuid().ToString("N");
        _testFolder = null;
        string folderGuid = AssetDatabase.CreateFolder("Assets/Tests", folderName);
        Assert.IsNotEmpty(folderGuid, "could not create the isolated test asset folder");
        _testFolder = AssetDatabase.GUIDToAssetPath(folderGuid);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var config in _temporaryConfigs)
            if (config != null && !EditorUtility.IsPersistent(config))
                UnityEngine.Object.DestroyImmediate(config);
        _temporaryConfigs.Clear();
        if (!string.IsNullOrEmpty(_testFolder) && AssetDatabase.IsValidFolder(_testFolder))
            AssetDatabase.DeleteAsset(_testFolder);
    }

    [Test]
    public void Defaults_AreSane()
    {
        var cfg = CreateConfig();

        Assert.AreEqual(10f, cfg.detectRadius, 0.001f);
        Assert.AreEqual(4f, cfg.closeRadius, 0.001f);
        Assert.AreEqual(2.5f, cfg.attackRadius, 0.001f);
        Assert.Greater(cfg.runSpeed, cfg.walkSpeed, "run should be faster than walk");
        Assert.AreEqual(100f, cfg.maxHealth, 0.001f);
        Assert.AreEqual(100f, cfg.maxPoise, 0.001f);
        Assert.AreEqual(1.5f, cfg.attackCooldown, 0.001f);
        Assert.IsTrue(cfg.patrolEnabled);
        Assert.AreEqual(4f, cfg.patrolRadius, 0.001f);
        Assert.AreEqual(1.5f, cfg.patrolWaitTime, 0.001f);
        Assert.Greater(cfg.loseTargetMultiplier, 1f);
    }

    [Test]
    public void Asset_Roundtrip_PreservesFields()
    {
        string path = _testFolder + "/TestEnemy.asset";

        var cfg = CreateConfig();
        cfg.detectRadius = 20f;
        cfg.attackRadius = 1.2f;
        cfg.maxHealth = 250f;
        cfg.maxPoise = 80f;
        cfg.patrolEnabled = false;
        cfg.patrolRadius = 6f;
        cfg.patrolWaitTime = 2f;
        cfg.patrolArrivalDistance = 0.5f;
        cfg.loseTargetMultiplier = 2f;

        AssetDatabase.CreateAsset(cfg, path);
        AssetDatabase.SaveAssetIfDirty(cfg);
        Resources.UnloadAsset(cfg);

        var loaded = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
        Assert.NotNull(loaded);
        Assert.AreEqual(20f, loaded.detectRadius, 0.001f);
        Assert.AreEqual(1.2f, loaded.attackRadius, 0.001f);
        Assert.AreEqual(250f, loaded.maxHealth, 0.001f);
        Assert.AreEqual(80f, loaded.maxPoise, 0.001f);
        Assert.IsFalse(loaded.patrolEnabled);
        Assert.AreEqual(6f, loaded.patrolRadius, 0.001f);
        Assert.AreEqual(2f, loaded.patrolWaitTime, 0.001f);
        Assert.AreEqual(0.5f, loaded.patrolArrivalDistance, 0.001f);
        Assert.AreEqual(2f, loaded.loseTargetMultiplier, 0.001f);
    }

    private EnemyConfig CreateConfig()
    {
        var config = ScriptableObject.CreateInstance<EnemyConfig>();
        _temporaryConfigs.Add(config);
        return config;
    }
}
