using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>把菜单风格战斗 HUD 接入当前战斗场景；旧 HUD 保留引用，仅由 BattleHUD 运行时停止渲染。</summary>
public static class BattleHUDSetup
{
    private const string AssetsPath = NativeMenuSetup.Folder + "/NativeMenuAssets.asset";

    [MenuItem("Tools/战斗HUD/应用菜单风格 HUD 到当前场景")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改；保留现场，先保存后再应用。");
        var existing = UnityEngine.Object.FindObjectOfType<BattleHUD>(true);
        if (existing != null) { Selection.activeGameObject = existing.gameObject; Debug.Log("[战斗HUD] 当前场景已存在，保留现有设置。"); return; }
        if (UnityEngine.Object.FindObjectOfType<PlayerCombat>(true) == null) throw new InvalidOperationException("当前场景没有 PlayerCombat，请打开战斗场景后再应用。");
        var config = AssetDatabase.LoadAssetAtPath<NativeMenuAssets>(AssetsPath);
        if (config == null) throw new InvalidOperationException("缺少菜单美术资产：" + AssetsPath + "，请先执行 Tools/开始菜单/应用 Web 原生菜单 A。");

        var root = new GameObject("BattleHUD"); Undo.RegisterCreatedObjectUndo(root, "应用菜单风格战斗 HUD");
        var hud = root.AddComponent<BattleHUD>(); hud.assets = config;
        EditorUtility.SetDirty(hud); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
        Selection.activeGameObject = root;
        Debug.Log("[战斗HUD] 已保存到 " + scene.path + "。F1 显示旧调试面板。");
    }
}
