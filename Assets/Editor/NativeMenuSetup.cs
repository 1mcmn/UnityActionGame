using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;

/// <summary>经引擎创建资源与保存场景；原菜单仅停用，保留全部引用。</summary>
public static class NativeMenuSetup
{
    public const string Folder = "Assets/Game/Generated/MainMenu/Native";
    private const string ScenePath = "Assets/Game/Scenes/menu.scene";

    [MenuItem("Tools/开始菜单/应用 Web 原生菜单 A")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("当前场景有未保存修改；保留现场，先保存后再应用。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
        var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Native Menu");
        if (existing != null) { Selection.activeGameObject = existing; Debug.Log("[原生菜单] 已存在，保留当前布局与参数。"); return; }
        EnsureFolder(Folder); AssetDatabase.Refresh();
        var config = CreateAssets();
        var root = new GameObject("Native Menu"); Undo.RegisterCreatedObjectUndo(root, "移植网页菜单 A");
        var cameraObject = new GameObject("Native Menu Camera", typeof(Camera), typeof(AudioListener)); cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.GetComponent<Camera>(); camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = config.paper;
        camera.transform.position = new Vector3(0, 0, -1100); camera.fieldOfView = 52.3f; camera.nearClipPlane = .1f; camera.farClipPlane = 10000;
        camera.cullingMask = (1 << 29) | (1 << 5); camera.allowHDR = false; camera.allowMSAA = true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var background = Canvas("Paper Background", root.transform, camera, 4000, -1000);
        var floor = NativeMenuUI.Graphic("PerspectiveGrid", background.transform, NativeMenuGraphic.Pattern.Floor, new Color(.48f, .55f, .48f, 1));
        var front = Canvas("Native Menu Canvas", root.transform, camera, 10, 2000);
        front.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var ui = front.gameObject.AddComponent<NativeMenuUI>(); ui.Build(config, camera, front); ui.floor = floor;
        ui.artPanel.gameObject.AddComponent<CanvasGroup>();
        var grain = NativeMenuUI.Graphic("PaperGrain", front.transform, NativeMenuGraphic.Pattern.Grain, new Color(.25f, .3f, .24f, .07f)); grain.transform.SetSiblingIndex(front.transform.childCount - 3);
        var audioObject = new GameObject("Menu Audio", typeof(NativeMenuAudio)); audioObject.transform.SetParent(root.transform, false);
        var carouselObject = new GameObject("Disc Carousel", typeof(NativeDiscCarousel)); carouselObject.transform.SetParent(root.transform, false);
        var carousel = carouselObject.GetComponent<NativeDiscCarousel>(); carousel.ui = ui; carousel.assets = config; carousel.discContainer = carouselObject.transform; carousel.menuAudio = audioObject.GetComponent<NativeMenuAudio>();
        ui.surface.carousel = carousel;
        var controller = root.AddComponent<NativeMenuController>(); controller.ui = ui; controller.assets = config; controller.carousel = carousel; controller.menuAudio = carousel.menuAudio;
        var eventsObject = new GameObject("Native EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); eventsObject.transform.SetParent(root.transform, false);
        for (int i = 0; i < ui.mainButtons.Length; i++)
        {
            var navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = ui.mainButtons[(i + 2) % 3], selectOnDown = ui.mainButtons[(i + 1) % 3] };
            ui.mainButtons[i].navigation = navigation;
        }
        ui.savesPanel.gameObject.SetActive(false); ui.settingsPanel.gameObject.SetActive(false); ui.quitPanel.gameObject.SetActive(false); ui.modalPanel.gameObject.SetActive(false); carouselObject.SetActive(false);
        foreach (var oldRoot in scene.GetRootGameObjects().Where(g => g != root)) { Undo.RecordObject(oldRoot, "保留并停用旧菜单"); oldRoot.SetActive(false); }
        UnityEngine.Canvas.ForceUpdateCanvases(); ui.Layout(Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight));
        camera.transform.position = new Vector3(0, 0, -1100 * ui.Unit); camera.fieldOfView = 2 * Mathf.Atan(camera.pixelHeight / 2200f) * Mathf.Rad2Deg;
        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(carousel); EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("菜单场景保存失败。");
        Selection.activeGameObject = root;
        Debug.Log("[原生菜单] A 已保存到 " + ScenePath + "。旧菜单停用保留；开始按钮进入原战斗场景。");
    }

    private static NativeMenuAssets CreateAssets()
    {
        string path = Folder + "/NativeMenuAssets.asset";
        var config = AssetDatabase.LoadAssetAtPath<NativeMenuAssets>(path);
        if (config == null) { config = ScriptableObject.CreateInstance<NativeMenuAssets>(); AssetDatabase.CreateAsset(config, path); }
        string chinese = string.Concat(Enumerable.Range(32, 95).Select(n => (char)n)) +
            "刃间一瞬之间锋芒尽现把下一次交给自己开始游戏设置退出选择记录进入战场存档新建本机菜单已创建保存取消请输入个字符的名称故事从这里给这张光盘名字初始每一次出发日期音量主音乐效让清晰可闻自动返加载失败请检查构列表损坏无法读取仅会话正在新的从入口进入。·，…～↑↓←→↗▶≡";
        config.chineseFont = FontAsset("MenuChinese", "Assets/Game/Scenes/AlimamaFangYuanTiVF-Thin.ttf", chinese, true);
        config.displayFont = FontAsset("Anton", Folder + "/Fonts/Anton.ttf", string.Concat(Enumerable.Range(32, 95).Select(n => (char)n)), false);
        config.condensedFont = FontAsset("BarlowCondensed", Folder + "/Fonts/BarlowCondensed.ttf", string.Concat(Enumerable.Range(32, 95).Select(n => (char)n)), false);
        config.displayFont.fallbackFontAssetTable = new List<TMP_FontAsset> { config.chineseFont };
        config.condensedFont.fallbackFontAssetTable = new List<TMP_FontAsset> { config.chineseFont };
        config.blade = Texture("BladeSculpture"); config.discFaces = new[] { Texture("Disc-rain"), Texture("Disc-fracture"), Texture("Disc-light"), Texture("Disc-empty") };
        var shader = Shader.Find("Menu/Native Disc"); if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("光盘 Shader 未正确编译。");
        config.discMaterial = Material("DiscPrint", shader); config.discMaterial.SetTexture("_MainTex", config.discFaces[3]);
        config.heroMaterial = Material("HeroPrint", Shader.Find("Menu/Native Hero Print"));
        config.outlineMaterial = Material("StrikeOutline", config.displayFont.material.shader);
        config.outlineMaterial.CopyPropertiesFromMaterial(config.displayFont.material);
        config.outlineMaterial.SetColor(ShaderUtilities.ID_FaceColor, new Color(1, 1, 1, 0));
        config.outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(186, 197, 184, 255)); config.outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .06f);
        config.outlineMaterial.EnableKeyword("OUTLINE_ON");
        string meshPath = Folder + "/DiscRing.asset"; config.discMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (config.discMesh == null) { config.discMesh = MakeDisc(); AssetDatabase.CreateAsset(config.discMesh, meshPath); }
        EditorUtility.SetDirty(config.displayFont); EditorUtility.SetDirty(config.condensedFont); EditorUtility.SetDirty(config.outlineMaterial); EditorUtility.SetDirty(config); return config;
    }
    private static Texture2D Texture(string name)
    {
        string path = Folder + "/Art/" + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("缺少菜单资源：" + path);
        importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    private static TMP_FontAsset FontAsset(string name, string sourcePath, string characters, bool dynamic)
    {
        string path = Folder + "/Fonts/" + name + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path); if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath); if (source == null) throw new InvalidOperationException("字体源缺失：" + sourcePath);
        var font = TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = name + " SDF"; font.TryAddCharacters(characters, out string missing); font.atlasPopulationMode = dynamic ? AtlasPopulationMode.Dynamic : AtlasPopulationMode.Static;
        if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[原生菜单] 字体需备用符号：" + missing);
        AssetDatabase.CreateAsset(font, path); AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font); return font;
    }
    private static Material Material(string name, Shader shader)
    {
        if (shader == null) throw new InvalidOperationException("菜单 Shader 缺失：" + name);
        string path = Folder + "/" + name + ".mat"; var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null) { result = new Material(shader) { name = name }; AssetDatabase.CreateAsset(result, path); } return result;
    }
    private static Mesh MakeDisc()
    {
        const int count = 128; var positions = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        for (int i = 0; i <= count; i++)
        {
            float angle = i * Mathf.PI * 2 / count; Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            foreach (float z in new[] { 0f, .006f }) foreach (float radius in new[] { .0275f, .5f })
            { Vector2 p = direction * radius; positions.Add(new Vector3(p.x, p.y, z)); uv.Add(p + Vector2.one * .5f); }
            if (i == count) continue;
            int n = i * 4;
            triangles.AddRange(new[] { n, n + 1, n + 5, n, n + 5, n + 4, n + 2, n + 6, n + 7, n + 2, n + 7, n + 3,
                n + 1, n + 3, n + 7, n + 1, n + 7, n + 5, n, n + 4, n + 6, n, n + 6, n + 2 });
        }
        var mesh = new Mesh { name = "MenuDiscRing" }; mesh.SetVertices(positions); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    private static Canvas Canvas(string name, Transform parent, Camera camera, float distance, int order)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler)); obj.transform.SetParent(parent, false); obj.layer = 5;
        var canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = distance; canvas.sortingOrder = order;
        var scaler = obj.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f; return canvas;
    }
    private static void EnsureFolder(string path)
    {
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
