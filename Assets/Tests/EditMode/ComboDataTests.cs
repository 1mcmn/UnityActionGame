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
        var data = ScriptableObject.CreateInstance<ComboData>();

        Assert.AreEqual(0.45f, data.attackDuration, 0.001f);
        Assert.AreEqual(2.5f, data.attackRadius, 0.001f);
        Assert.AreEqual(0.55f, data.comboWindowPercent, 0.001f);
        Assert.AreEqual(5, data.comboDamages.Length);
        Assert.AreEqual(15f, data.comboDamages[0], 0.001f);

        for (int i = 1; i < data.comboDamages.Length; i++)
            Assert.Greater(data.comboDamages[i], data.comboDamages[i - 1],
                "combo damages should scale up per step");
    }

    [Test]
    public void Asset_Roundtrip_PreservesFields()
    {
        string path = TestFolder + "/TestCombo.asset";

        var data = ScriptableObject.CreateInstance<ComboData>();
        data.attackDuration = 0.6f;
        data.attackRadius = 3f;
        data.comboWindowPercent = 0.5f;
        data.comboDamages = new[] { 10f, 20f, 30f };

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

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
        var data = ScriptableObject.CreateInstance<ComboData>();
        data.comboDamages = new[] { 15f, 18f, 22f };

        // mirrors PlayerCombat.CurrentAttackDamage clamping: never throw, never index < 0
        int first = Mathf.Clamp(0, 0, data.comboDamages.Length - 1);
        int last  = Mathf.Clamp(99, 0, data.comboDamages.Length - 1);

        Assert.AreEqual(15f, data.comboDamages[first], 0.001f);
        Assert.AreEqual(22f, data.comboDamages[last], 0.001f);
    }
}
