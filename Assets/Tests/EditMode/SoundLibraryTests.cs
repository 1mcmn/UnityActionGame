using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SoundManager dictionary-building tests (EditMode, no audio playback):
/// verifies the ID -> clip lookup table built from SoundLibrary assets,
/// including the duplicate-ID "last wins" rule and the {prefix}_ matching rule.
/// </summary>
public class SoundLibraryTests
{
    private GameObject _go;
    private SoundManager _manager;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestSoundManager");
        _manager = _go.AddComponent<SoundManager>();

        var lib1 = ScriptableObject.CreateInstance<SoundLibrary>();
        lib1.sounds = new[]
        {
            new SoundItem { soundID = "foot_step_01", clip = AudioClip.Create("s1", 1, 1, 1000, false) },
            new SoundItem { soundID = "foot_step_02", clip = AudioClip.Create("s2", 1, 1, 1000, false) },
            new SoundItem { soundID = "sword_hit_01", clip = AudioClip.Create("s3", 1, 1, 1000, false) },
        };

        var lib2 = ScriptableObject.CreateInstance<SoundLibrary>();
        lib2.sounds = new[]
        {
            // duplicate ID across libraries: the later library wins
            new SoundItem { soundID = "sword_hit_01", clip = AudioClip.Create("s3b", 1, 1, 1000, false) },
        };

        SetField(_manager, "_libraries", new[] { lib1, lib2 });
        CallPrivate(_manager, "BuildDictionary");
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
    }

    [Test]
    public void BuildDictionary_RegistersAllValidIds()
    {
        Assert.IsTrue(_manager.HasSound("foot_step_01"));
        Assert.IsTrue(_manager.HasSound("foot_step_02"));
        Assert.IsTrue(_manager.HasSound("sword_hit_01"));
        Assert.IsFalse(_manager.HasSound("missing_id"));
    }

    [Test]
    public void BuildDictionary_DuplicateId_LastLibraryWins()
    {
        var dict = GetField<Dictionary<string, AudioClip>>(_manager, "_dict");
        Assert.AreEqual("s3b", dict["sword_hit_01"].name, "later library should override earlier one");
        Assert.AreEqual(3, dict.Count);
    }

    [Test]
    public void PrefixMatching_Rule_CountsVariants()
    {
        // mirrors SoundManager.PlayByPrefix: matches keys starting with prefix + "_"
        var dict = GetField<Dictionary<string, AudioClip>>(_manager, "_dict");
        int footStepCount = 0;
        foreach (var key in dict.Keys)
            if (key.StartsWith("foot_step_")) footStepCount++;

        Assert.AreEqual(2, footStepCount);
        Assert.IsFalse(dict.ContainsKey("foot_step"), "bare prefix without _NN must not be a key here");
    }

    // ---- reflection helpers (SoundManager internals are private by design) ----

    private static void SetField(object target, string name, object value)
    {
        var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, "field not found: " + name);
        f.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f, "field not found: " + name);
        return (T)f.GetValue(target);
    }

    private static void CallPrivate(object target, string name)
    {
        var m = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(m, "method not found: " + name);
        m.Invoke(target, null);
    }
}
