using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EnemyConfig (enemy config asset) unit tests:
/// defaults + ScriptableObject asset save/load roundtrip.
/// </summary>
public class EnemyConfigTests
{
    private const string TestFolder = "Assets/Tests/Generated";

    [SetUp]
    public void SetUp()
    {
        if (!AssetDatabase.IsValidFolder(TestFolder))
            AssetDatabase.CreateFolder("Assets/Tests", "Generated");
    }

    [TearDown]
    public void TearDown()
    {
        if (AssetDatabase.IsValidFolder(TestFolder))
            AssetDatabase.DeleteAsset(TestFolder);
    }

    [Test]
    public void Defaults_AreSane()
    {
        var cfg = ScriptableObject.CreateInstance<EnemyConfig>();

        Assert.AreEqual(10f, cfg.detectRadius, 0.001f);
        Assert.AreEqual(4f, cfg.closeRadius, 0.001f);
        Assert.AreEqual(2.5f, cfg.attackRadius, 0.001f);
        Assert.Greater(cfg.runSpeed, cfg.walkSpeed, "run should be faster than walk");
        Assert.AreEqual(100f, cfg.maxHealth, 0.001f);
        Assert.AreEqual(100f, cfg.maxPoise, 0.001f);
        Assert.AreEqual(1.5f, cfg.attackCooldown, 0.001f);
    }

    [Test]
    public void Asset_Roundtrip_PreservesFields()
    {
        string path = TestFolder + "/TestEnemy.asset";

        var cfg = ScriptableObject.CreateInstance<EnemyConfig>();
        cfg.detectRadius = 20f;
        cfg.attackRadius = 1.2f;
        cfg.maxHealth = 250f;
        cfg.maxPoise = 80f;

        AssetDatabase.CreateAsset(cfg, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var loaded = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
        Assert.NotNull(loaded);
        Assert.AreEqual(20f, loaded.detectRadius, 0.001f);
        Assert.AreEqual(1.2f, loaded.attackRadius, 0.001f);
        Assert.AreEqual(250f, loaded.maxHealth, 0.001f);
        Assert.AreEqual(80f, loaded.maxPoise, 0.001f);
    }
}
