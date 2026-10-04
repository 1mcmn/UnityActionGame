using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>通过引擎生成并保存菜单，源模型、材质、动画与战斗场景保持独立。</summary>
public static class MainMenuSetup
{
    public const string ScenePath = "Assets/Game/Scenes/menu.scene";
    public const string GameplayPath = "Assets/Game/Scenes/DEMO City Crossing.unity";
    private const string Folder = "Assets/Game/Generated/MainMenu";
    private const int DisplayLayer = 31;
    private static readonly Color Paper = new Color32(242, 238, 227, 255);
    private static readonly Color Ink = new Color32(27, 34, 42, 255);
    private static readonly Color Muted = new Color32(105, 112, 111, 255);
    private static readonly Color Accent = new Color32(237, 102, 81, 255);
    private static TMP_FontAsset font;

    [MenuItem("Tools/开始菜单/配置并保存 menu 场景")]
    public static void ConfigureAndSave()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 Play 模式。");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("menu 有未保存修改，请先保存后再执行配置。");
        SceneManager.SetActiveScene(scene);
        GameObject previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Main Menu");
        if (previous != null) throw new InvalidOperationException("菜单已经配置；请直接修改场景中的参数，避免覆盖美术调整。");
        font = CreateMenuFont();
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imports/momo/momo 1.fbx");
        GameObject materialSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imports/momo/momo.fbx");
        if (modelAsset == null || materialSource == null) throw new InvalidOperationException("缺少 momo 模型或贴图来源。");
        var sourceAnimator = modelAsset.GetComponent<Animator>();
        if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isHuman)
            throw new InvalidOperationException("momo 1 模型没有有效 Humanoid Avatar。");
        var sourceRenderers = materialSource.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in modelAsset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var palette = sourceRenderers.FirstOrDefault(r => r.name == renderer.name);
            if (palette == null || palette.sharedMesh.subMeshCount != renderer.sharedMesh.subMeshCount ||
                palette.sharedMesh.vertexCount != renderer.sharedMesh.vertexCount)
                throw new InvalidOperationException("两份 momo 模型拓扑不匹配，无法安全复用材质：" + renderer.name);
        }
        EnsureFolder();
        GameObject root = new GameObject("Main Menu");
        var controller = root.AddComponent<MainMenuController>();
        Camera camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
        if (camera == null) camera = Child("Menu Camera", root.transform).AddComponent<Camera>();
        camera.name = "Menu Camera";
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Ink;
        camera.orthographic = true;
        camera.orthographicSize = .30f;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 20f;
        camera.depth = 100f;
        camera.cullingMask = 1 << DisplayLayer;
        camera.transform.position = new Vector3(.29f, .08f, 2f);
        camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
        foreach (Light light in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)))
            light.enabled = false;
        Light key = Child("Portrait Key", root.transform).AddComponent<Light>();
        key.type = LightType.Directional; key.intensity = .95f;
        key.color = new Color(1f, .91f, .83f); key.shadows = LightShadows.None;
        key.transform.rotation = Quaternion.Euler(24f, 155f, 0f); key.cullingMask = 1 << DisplayLayer;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.55f, .6f, .67f);

        Transform pivot = Child("Portrait Pivot", root.transform).transform;
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
        model.name = "momo · Menu Portrait";
        model.transform.SetParent(pivot, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Transform node in model.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = DisplayLayer;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var palette = sourceRenderers.FirstOrDefault(r => r.name == renderer.name);
            if (palette != null) renderer.sharedMaterials = palette.sharedMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        Animator animator = model.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("momo 模型没有有效 Humanoid Avatar。");
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        LowerPortraitArms(model);
        Transform head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name == "J_Bip_C_Head");
        if (head == null) throw new InvalidOperationException("momo 缺少头部骨骼。");
        model.transform.position -= head.position;
        pivot.localRotation = Quaternion.Euler(0f, 8f, 0f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject);
        root.AddComponent<MenuPortraitMotion>().Configure(pivot, camera);

        var canvasObject = Child("Menu Canvas", root.transform, typeof(RectTransform));
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera; canvas.planeDistance = 1f;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        RectTransform layout = canvasObject.GetComponent<RectTransform>();
        RectTransform left = Panel("Paper Sidebar", layout, new Vector2(0,0), new Vector2(.43f,1), Paper);
        Panel("Coral Edge", left, new Vector2(.988f,0), Vector2.one, Accent);
        Label("Edition", left, "ACTION GAME  /  01", .09f,.88f,.89f,.93f, 19, Muted);
        Label("Title", left, "刃间", .085f,.685f,.9f,.88f, 112, Ink, FontStyles.Bold);
        Label("Subtitle", left, "BETWEEN THE BLADES", .095f,.665f,.9f,.715f, 20, Ink);
        Panel("Title Rule", left, new Vector2(.095f,.635f), new Vector2(.88f,.637f), new Color32(204,202,190,255));
        var navigation = Child("Navigation", left, typeof(RectTransform)).GetComponent<RectTransform>();
        Fill(navigation); CanvasGroup group = navigation.gameObject.AddComponent<CanvasGroup>();
        Button start = MenuButton(navigation, "01", "开始游戏", "START GAME", .44f);
        Button settings = MenuButton(navigation, "02", "设置", "SETTINGS", .325f);
        Button quit = MenuButton(navigation, "03", "退出", "EXIT", .21f);
        var status = Label("Status", left, "准备好，进入下一场战斗。", .095f,.115f,.9f,.16f, 21, Muted);
        Label("Footer", left, "UNITY ACTION DEMO     /     2026", .095f,.045f,.92f,.08f, 17, Muted);

        Panel("Portrait Footer", layout, new Vector2(.43f,0),new Vector2(1,.14f),Ink);
        Label("Portrait Index", layout, "MOMO   /   CHARACTER 01", .475f,.9f,.88f,.94f, 18, Paper);
        Label("Corner Mark", layout, "+", .918f,.88f,.97f,.97f, 45, Accent);
        Panel("Portrait Rule", layout, new Vector2(.475f,.876f), new Vector2(.61f,.877f), new Color32(85,94,100,255));
        Label("Portrait Caption", layout, "MOVE YOUR CURSOR", .475f,.073f,.94f,.11f, 22, Paper);
        Label("Portrait Hint", layout, "随鼠标转动  /  20 度 × 10 度", .475f,.035f,.94f,.07f, 18, new Color32(158,169,174,255));
        // 简洁的右下角编号，不遮挡脸部。
        Label("Portrait Number", layout, "01", .895f,.105f,.96f,.18f, 55, Accent, FontStyles.Bold);

        RectTransform modal = Panel("Settings Overlay", layout, Vector2.zero, Vector2.one, new Color(0f,0f,0f,.65f), true);
        RectTransform card = Panel("Settings Card", modal, new Vector2(.29f,.2f), new Vector2(.71f,.8f), Paper, true);
        Panel("Settings Accent", card, new Vector2(0,.986f), Vector2.one, Accent);
        Label("Settings Title", card, "设置", .09f,.78f,.9f,.92f, 48, Ink, FontStyles.Bold);
        Label("Volume Label", card, "主音量", .09f,.66f,.85f,.73f, 25, Ink);
        Slider volume = CreateSlider(card);
        Toggle fullscreen = CreateToggle(card, "全屏显示", .39f);
        Toggle vsync = CreateToggle(card, "垂直同步", .27f);
        Label("Settings Hint", card, "设置自动保存  ·  ESC 返回", .09f,.15f,.91f,.22f, 19, Muted);
        Button close = SolidButton(card, "返回", new Vector2(.09f,.045f), new Vector2(.91f,.135f));
        controller.Configure(modal.gameObject, volume, fullscreen, vsync, status, start, settings, close, group);
        UnityEventTools.AddPersistentListener(start.onClick, controller.StartGame);
        UnityEventTools.AddPersistentListener(settings.onClick, controller.OpenSettings);
        UnityEventTools.AddPersistentListener(quit.onClick, controller.QuitGame);
        UnityEventTools.AddPersistentListener(close.onClick, controller.CloseSettings);
        UnityEventTools.AddPersistentListener(volume.onValueChanged, controller.SetVolume);
        UnityEventTools.AddPersistentListener(fullscreen.onValueChanged, controller.SetFullscreen);
        UnityEventTools.AddPersistentListener(vsync.onValueChanged, controller.SetVSync);
        modal.gameObject.SetActive(false);
        GameObject events = Child("Menu EventSystem", root.transform);
        events.AddComponent<EventSystem>(); events.AddComponent<StandaloneInputModule>();
        foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = DisplayLayer;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("menu 场景保存失败。");
        AssetDatabase.SaveAssets();
        // 保留已启用的其他场景，菜单作为启动场景。
        EditorBuildSettings.scenes = new [] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene(GameplayPath, true) }
            .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != GameplayPath)).ToArray();
        Debug.Log("[开始菜单] menu 已配置并保存。请单独打开 menu 运行；开始游戏进入 DEMO City Crossing。");
    }

    [MenuItem("Tools/开始菜单/打开 menu 场景")]
    public static void OpenMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Tools/开始菜单/构建 Windows 菜单 Demo")]
    public static void BuildWindows()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string output = Path.GetFullPath("Builds/MenuDemo/UnityActionGame.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new [] {ScenePath, GameplayPath},
            locationPathName = output, target = BuildTarget.StandaloneWindows64 });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("菜单构建失败：" + report.summary.result);
        Debug.Log("[开始菜单] 构建完成：" + output);
    }

    private static TMP_FontAsset CreateMenuFont()
    {
        EnsureFolder();
        string path = Folder + "/MenuFont.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Scenes/AlimamaFangYuanTiVF-Thin.ttf");
        if (source == null) throw new InvalidOperationException("缺少中文字体源文件。");
        var result = TMP_FontAsset.CreateFontAsset(source, 90, 9,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        result.name = "MenuFont";
        string characters = string.Concat(Enumerable.Range(32,95).Select(c => ((char)c).ToString())) +
            "刃间开始游戏设置退出准备好进入下一场景战斗正在…加载失败请检查构建列表主音量全屏显示垂直同步自动保存返回随鼠标转度·×，。";
        if (!result.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("菜单字体缺少字符：" + missing);
        result.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(result, path);
        result.material.name = "MenuFont Material";
        AssetDatabase.AddObjectToAsset(result.material, result);
        foreach (Texture2D texture in result.atlasTextures)
        {
            texture.name = "MenuFont Atlas";
            AssetDatabase.AddObjectToAsset(texture, result);
        }
        EditorUtility.SetDirty(result);
        AssetDatabase.SaveAssets();
        return result;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game/Generated")) AssetDatabase.CreateFolder("Assets/Game", "Generated");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Generated", "MainMenu");
    }
    private static void LowerPortraitArms(GameObject model)
    {
        var bones = model.GetComponentsInChildren<Transform>(true);
        foreach (string side in new [] {"L", "R"})
        {
            var upper = bones.FirstOrDefault(t => t.name == "J_Bip_" + side + "_UpperArm");
            var lower = bones.FirstOrDefault(t => t.name == "J_Bip_" + side + "_LowerArm");
            if (upper == null || lower == null) continue;
            Vector3 current = lower.position - upper.position;
            Vector3 direction = new Vector3(side == "L" ? .12f : -.12f, -1f, .08f);
            upper.rotation = Quaternion.FromToRotation(current, direction) * upper.rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(upper);
        }
    }
    private static GameObject Child(string name, Transform parent, params Type[] types)
    {
        var obj = new GameObject(name, types); obj.transform.SetParent(parent, false); return obj;
    }
    private static void Fill(RectTransform rect) { Place(rect, Vector2.zero, Vector2.one); }
    private static void Place(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color, bool raycast = false)
    {
        var obj = Child(name, parent, typeof(RectTransform)); var rect = obj.GetComponent<RectTransform>(); Place(rect,min,max);
        Image image = obj.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return rect;
    }
    private static TextMeshProUGUI Label(string name, Transform parent, string text, float x0, float y0, float x1, float y1,
        float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var obj = Child(name, parent, typeof(RectTransform)); Place(obj.GetComponent<RectTransform>(),new Vector2(x0,y0),new Vector2(x1,y1));
        var label = obj.AddComponent<TextMeshProUGUI>(); label.font = font; label.text = text; label.fontSize = size;
        label.fontStyle = style; label.color = color; label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis; return label;
    }
    private static Button MenuButton(Transform parent, string number, string text, string english, float y)
    {
        var rect = Panel(text, parent, new Vector2(.075f,y),new Vector2(.91f,y+.097f),Color.white,true);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        ColorBlock colors = button.colors; colors.normalColor = Color.clear;
        colors.highlightedColor = colors.selectedColor = new Color(0f,0f,0f,.055f); colors.pressedColor = new Color(0f,0f,0f,.11f);
        button.colors = colors;
        Label("Index",rect,number,.035f,.15f,.13f,.85f,20,Accent);
        var label = Label("Label",rect,text,.16f,.12f,.67f,.9f,38,Ink,FontStyles.Bold);
        Label("English",rect,english,.64f,.2f,.98f,.8f,17,Muted);
        RectTransform marker = Panel("Hover Marker",rect,new Vector2(0,.13f),new Vector2(.007f,.87f),Accent);
        rect.gameObject.AddComponent<MenuButtonFeedback>().Configure(label.rectTransform,marker.GetComponent<Image>());
        marker.GetComponent<Image>().enabled = false;
        return button;
    }
    private static Button SolidButton(Transform parent,string text,Vector2 min,Vector2 max)
    {
        var rect = Panel(text,parent,min,max,Accent,true); var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>(); var label = Label("Label",rect,text,0,0,1,1,25,Color.white);
        label.alignment = TextAlignmentOptions.Center; return button;
    }
    private static Slider CreateSlider(Transform parent)
    {
        var obj = Child("Master Volume",parent,typeof(RectTransform)); var rect = obj.GetComponent<RectTransform>();
        Place(rect,new Vector2(.09f,.56f),new Vector2(.91f,.64f));
        Panel("Track",rect,new Vector2(0,.38f),new Vector2(1,.62f),new Color32(207,204,191,255),true);
        var fillArea = Child("Fill Area",rect,typeof(RectTransform)).GetComponent<RectTransform>(); Fill(fillArea);
        RectTransform fill = Panel("Fill",fillArea,new Vector2(0,.38f),new Vector2(1,.62f),Accent);
        var handleArea = Child("Handle Area",rect,typeof(RectTransform)).GetComponent<RectTransform>(); Fill(handleArea);
        RectTransform handle = Panel("Handle",handleArea,new Vector2(.5f,0),new Vector2(.5f,1),Ink,true);
        handle.sizeDelta = new Vector2(14,0);
        var slider = obj.AddComponent<Slider>(); slider.fillRect = fill; slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = 0; slider.maxValue = 1; slider.value = 1;
        return slider;
    }
    private static Toggle CreateToggle(Transform parent,string title,float y)
    {
        var obj = Child(title,parent,typeof(RectTransform)); var rect = obj.GetComponent<RectTransform>();
        Place(rect,new Vector2(.09f,y),new Vector2(.91f,y+.09f));
        RectTransform box = Panel("Box",rect,new Vector2(0,.12f),new Vector2(.06f,.88f),new Color32(206,204,191,255),true);
        RectTransform check = Panel("Check",box,new Vector2(.2f,.2f),new Vector2(.8f,.8f),Accent);
        Label("Label",rect,title,.095f,0,1,1,25,Ink);
        // 整行均可点击，勾选标记不拦截射线。
        Image hit = obj.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var toggle = obj.AddComponent<Toggle>(); toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = true; return toggle;
    }
}
