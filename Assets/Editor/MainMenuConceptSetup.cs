using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>菜单概念图的真实三维实现：仅处理 menu，不调整战斗资源。</summary>
public static class MainMenuConceptSetup
{
    private const string Folder="Assets/Game/Generated/MainMenu/Concept";
    private const int BackgroundLayer=30;
    private const int PortraitLayer=31;
    private static readonly Color Cyan=new Color(.12f,.64f,.78f,1f);
    private static readonly Color Ink=new Color(.06f,.10f,.16f,1f);

    [MenuItem("Tools/开始菜单/应用线框半调概念图风格")]
    public static void ApplyConcept()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出Play后配置菜单。");
        Scene scene=SceneManager.GetActiveScene();
        if(scene.path!=MainMenuSetup.ScenePath) throw new InvalidOperationException("请单独打开 menu 场景。");
        if(scene.isDirty) throw new InvalidOperationException("menu有未保存修改，请先保存。");
        GameObject root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Main Menu");
        if(root==null) throw new InvalidOperationException("请先配置基础菜单。");
        Shader portraitShader=Shader.Find("Menu/Toon Halftone Portrait");
        Shader gridShader=Shader.Find("Menu/Spatial Grid");
        Shader lineShader=Shader.Find("Menu/Spatial Line");
        if(portraitShader==null||gridShader==null||lineShader==null||ShaderUtil.ShaderHasError(portraitShader)||
            ShaderUtil.ShaderHasError(gridShader)||ShaderUtil.ShaderHasError(lineShader))
            throw new InvalidOperationException("菜单Shader缺失或有编译错误，请等待导入完成。");
        EnsureFolder();
        Undo.RegisterFullObjectHierarchyUndo(root,"菜单概念图风格");
        Transform previous=root.transform.Find("Concept Environment");
        if(previous!=null) Object.DestroyImmediate(previous.gameObject);
        var environment=Child("Concept Environment",root.transform);
        var camera=GameObject.Find("Menu Camera").GetComponent<Camera>();
        camera.backgroundColor=new Color(.967f,.977f,.989f);
        camera.clearFlags=CameraClearFlags.Depth;
        camera.orthographicSize=.235f;
        camera.transform.position=new Vector3(.17f,.065f,2f);
        camera.cullingMask=1<<PortraitLayer;
        camera.allowHDR=false;
        Camera background=Child("Spatial Background Camera",environment.transform).AddComponent<Camera>();
        background.clearFlags=CameraClearFlags.SolidColor;
        background.backgroundColor=camera.backgroundColor;
        background.cullingMask=1<<BackgroundLayer;
        background.depth=camera.depth-1;
        background.fieldOfView=48;
        background.nearClipPlane=.1f; background.farClipPlane=75;
        background.allowHDR=false;
        background.transform.position=new Vector3(0,1.65f,-5);
        background.transform.LookAt(new Vector3(0,1.3f,15));
        // URP使用正式相机堆叠；两个Base相机即使Depth清除也会覆盖颜色。
        var portraitData=camera.GetUniversalAdditionalCameraData();
        portraitData.renderType=CameraRenderType.Overlay;
        portraitData.renderPostProcessing=false;
        var backgroundData=background.GetUniversalAdditionalCameraData();
        backgroundData.renderType=CameraRenderType.Base;
        backgroundData.renderPostProcessing=false;
        backgroundData.cameraStack.Add(camera);
        // 相机本身不需要新的AudioListener。
        var grid=Child("Perspective Grid Floor",environment.transform);
        grid.AddComponent<MeshFilter>().sharedMesh=SaveMesh("GridFloor",FloorMesh());
        var floorMaterial=MaterialAsset("GridFloor",gridShader);
        floorMaterial.SetColor("_BaseColor",new Color(.92f,.945f,.965f));
        floorMaterial.SetColor("_LineColor",new Color(.35f,.44f,.55f));
        floorMaterial.SetFloat("_Spacing",1.15f); floorMaterial.SetFloat("_Opacity",.32f);
        grid.AddComponent<MeshRenderer>().sharedMaterial=floorMaterial;
        Transform network=Child("Node Network",environment.transform).transform;
        CreateNetwork(network,background,lineShader);
        SetLayer(environment.transform,BackgroundLayer);

        Transform pivot=root.transform.Find("Portrait Pivot");
        pivot.localRotation=Quaternion.Euler(0,12,0);
        var visible=new List<Renderer>();
        var sourceRenderers=pivot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach(var source in sourceRenderers)
        {
            Transform old=source.transform.Find("Concept Head Mesh");
            if(old!=null) Object.DestroyImmediate(old.gameObject);
            // Face/Hair全保留；Body中仅保留HairBack子网格，完全不保留身体和衣服。
            var slots=Enumerable.Range(0,source.sharedMesh.subMeshCount).Where(i=>source.name!="Body"||
                source.sharedMaterials[i].name.Contains("HairBack")).ToArray();
            if(slots.Length>0)
            {
                var baked=new Mesh(); source.BakeMesh(baked,true);
                var mesh=ExpandForWireframe(baked,slots);
                Object.DestroyImmediate(baked);
                var display=Child("Concept Head Mesh",source.transform);
                display.AddComponent<MeshFilter>().sharedMesh=SaveMesh("Head_"+source.name,mesh);
                var renderer=display.AddComponent<MeshRenderer>();
                var palette=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imports/momo/momo.fbx")
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name==source.name).sharedMaterials;
                renderer.sharedMaterials=slots.Select(i=>CreatePortraitMaterial(source.name+"_"+i,palette[i],portraitShader)).ToArray();
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                renderer.gameObject.layer=PortraitLayer; visible.Add(renderer);
            }
            source.enabled=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        }
        var motion=root.GetComponent<MenuPortraitMotion>();
        motion.Configure(pivot,camera); motion.ConfigureFraming(.71f,20f,10f);
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=new Color(.65f,.70f,.78f);
        var key=root.GetComponentsInChildren<Light>(true).First(l=>l.name=="Portrait Key");
        key.intensity=1; key.color=Color.white;
        key.transform.rotation=Quaternion.Euler(22,155,0);
        RenderSettings.sun=key;
        var canvas=root.GetComponentInChildren<Canvas>();
        var front=RebuildLayout(canvas);
        var effects=root.GetComponent<MenuConceptMotion>();
        if(effects==null) effects=root.AddComponent<MenuConceptMotion>();
        effects.Configure(motion,pivot,network,visible.ToArray(),front);
        EditorUtility.SetDirty(motion); EditorUtility.SetDirty(effects);
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(Folder+"/GridFloor.mat")) EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("menu保存失败。");
        Debug.Log("[开始菜单] 线框半调概念图风格已应用并保存；身体隐藏、三维头部保留，转头驱动半调与前景视差。");
    }

    private static Material CreatePortraitMaterial(string name,Material original,Shader shader)
    {
        var mat=MaterialAsset("Portrait_"+name,shader);
        mat.SetTexture("_BaseMap",original.GetTexture("_BaseMap"));
        Color tint=Color.white;
        if(original.name.Contains("HAIR")) tint=new Color(.28f,.72f,.83f);
        else if(original.name.Contains("EyeIris")) tint=new Color(.25f,.58f,1f);
        else if(original.name.Contains("Face_00_SKIN")) tint=new Color(1f,.91f,.84f);
        mat.SetColor("_BaseColor",tint);
        mat.SetColor("_InkColor",new Color(.035f,.09f,.23f));
        mat.SetColor("_PaperColor",new Color(.94f,.97f,1f));
        mat.SetFloat("_DotSize",5f); mat.SetFloat("_Cutoff",.35f); mat.SetFloat("_WireStrength",.55f);
        EditorUtility.SetDirty(mat); return mat;
    }

    private static RectTransform[] RebuildLayout(Canvas canvas)
    {
        Transform sidebar=canvas.transform.Find("Paper Sidebar");
        Place((RectTransform)sidebar,Vector2.zero,new Vector2(.35f,1));
        sidebar.GetComponent<Image>().color=Color.clear;
        foreach(string name in new[]{"Coral Edge","Edition","Subtitle","Title Rule","Footer"})
            if(sidebar.Find(name)!=null) sidebar.Find(name).gameObject.SetActive(false);
        foreach(string name in new[]{"Portrait Footer","Portrait Index","Corner Mark","Portrait Rule","Portrait Caption","Portrait Hint","Portrait Number"})
            if(canvas.transform.Find(name)!=null) canvas.transform.Find(name).gameObject.SetActive(false);
        var title=sidebar.Find("Title").GetComponent<TextMeshProUGUI>();
        Place(title.rectTransform,new Vector2(.21f,.65f),new Vector2(.95f,.85f));
        title.fontSize=144; title.fontStyle=FontStyles.Normal; title.color=Ink;
        var status=sidebar.Find("Status").GetComponent<TextMeshProUGUI>();
        status.text=""; status.color=Ink; status.fontSize=22;
        Transform nav=sidebar.Find("Navigation");
        string[] names={"开始游戏","设置","退出"};
        for(int i=0;i<names.Length;i++)
        {
            var button=nav.Find(names[i]).GetComponent<Button>();
            var rect=button.GetComponent<RectTransform>();
            Place(rect,new Vector2(.17f,.445f-i*.13f),new Vector2(.94f,.53f-i*.13f));
            foreach(string child in new[]{"Index","English","Hover Marker"})
                if(rect.Find(child)!=null) rect.Find(child).gameObject.SetActive(false);
            var label=rect.Find("Label").GetComponent<TextMeshProUGUI>();
            Place(label.rectTransform,new Vector2(.14f,0),new Vector2(1,1));
            label.fontSize=i==0?48:43; label.fontStyle=FontStyles.Normal;
            label.color=i==0?Ink:new Color(.32f,.36f,.4f);
            var colors=button.colors; colors.normalColor=Color.clear;
            colors.highlightedColor=colors.selectedColor=new Color(.1f,.65f,.85f,.07f);
            colors.pressedColor=new Color(.1f,.65f,.85f,.15f); button.colors=colors;
            Transform previous=rect.Find("Play Glyph");
            if(previous!=null) Object.DestroyImmediate(previous.gameObject);
            if(i==0)
            {
                var glyph=Child("Play Glyph",rect,typeof(RectTransform));
                Place(glyph.GetComponent<RectTransform>(),new Vector2(.015f,.25f),new Vector2(.095f,.75f));
                glyph.AddComponent<MenuWireGraphic>().Configure(new[]{new Vector2(.15f,.12f),new Vector2(.9f,.5f),new Vector2(.15f,.88f)},Cyan,1.6f,3.4f,true);
            }
        }
        var settings=canvas.transform.Find("Settings Overlay/Settings Card");
        settings.GetComponent<Image>().color=new Color(.97f,.985f,.995f);
        settings.Find("Settings Accent").GetComponent<Image>().color=Cyan;
        settings.Find("返回").GetComponent<Image>().color=Cyan;
        settings.Find("Master Volume/Fill Area/Fill").GetComponent<Image>().color=Cyan;
        Place((RectTransform)settings.Find("Master Volume/Fill Area"),new Vector2(0,.38f),new Vector2(1,.62f));
        foreach(var toggle in settings.GetComponentsInChildren<Toggle>(true)) toggle.graphic.color=Cyan;
        Transform old=canvas.transform.Find("Foreground Parallax");
        if(old!=null) Object.DestroyImmediate(old.gameObject);
        var parent=Child("Foreground Parallax",canvas.transform,typeof(RectTransform));
        Place(parent.GetComponent<RectTransform>(),Vector2.zero,Vector2.one);
        var upper=Graphic(parent.transform,"Upper Slice Frame",new[]{new Vector2(.47f,.72f),new Vector2(.92f,.82f),new Vector2(.96f,.94f),new Vector2(.53f,.85f)},new Color(.10f,.69f,.82f,.64f));
        var lower=Graphic(parent.transform,"Lower Slice Frame",new[]{new Vector2(.44f,.28f),new Vector2(.91f,.39f),new Vector2(.94f,.22f),new Vector2(.5f,.11f)},new Color(.10f,.69f,.82f,.73f));
        SetLayer(parent.transform,PortraitLayer);
        // 让设置弹窗最后绘制，前景线框永远不拦截输入。
        canvas.transform.Find("Settings Overlay").SetAsLastSibling();
        return new[]{upper,lower};
    }

    private static RectTransform Graphic(Transform parent,string name,Vector2[] points,Color color)
    {
        var obj=Child(name,parent,typeof(RectTransform)); var rect=obj.GetComponent<RectTransform>();
        Place(rect,Vector2.zero,Vector2.one);
        obj.AddComponent<MenuWireGraphic>().Configure(points,color,1.4f,3.5f,true);return rect;
    }

    private static void CreateNetwork(Transform parent,Camera camera,Shader shader)
    {
        var normal=MaterialAsset("NetworkLine",shader); normal.SetColor("_BaseColor",new Color(.34f,.47f,.60f,.50f)); normal.SetFloat("_Round",0);
        var accent=MaterialAsset("NetworkAccent",shader); accent.SetColor("_BaseColor",new Color(.1f,.65f,.8f,.67f)); accent.SetFloat("_Round",0);
        var nodeMat=MaterialAsset("NetworkNodes",shader); nodeMat.SetColor("_BaseColor",new Color(.20f,.45f,.60f,.75f)); nodeMat.SetFloat("_Round",1);
        var random=new System.Random(20261003);
        var nodes=new List<Vector3>();
        for(int i=0;i<14;i++)
        {
            float sign=i%2==0?-1:1;
            float z=1f+(float)random.NextDouble()*19;
            float x=sign*(3f+(float)random.NextDouble()*4);
            float height=1.8f+(float)random.NextDouble()*3.7f;
            var points=new[]{new Vector3(x,.03f,z),new Vector3(x,height*.38f,z),
                new Vector3(x+sign*.9f,height*.65f,z+.4f),new Vector3(x-sign*.35f,height,z+.8f)};
            AddLine("Branch "+i,parent,points,i%3==0?accent:normal,.018f); nodes.AddRange(points);
            if(i%3==0)
            {
                var endpoint=points[1]+new Vector3(-sign*1.1f,.15f,.7f);
                AddLine("Crosslink "+i,parent,new[]{points[1],endpoint,points[2]},normal,.013f);nodes.Add(endpoint);
            }
        }
        var nodeObj=Child("Endpoint Dots",parent);nodeObj.AddComponent<MeshFilter>().sharedMesh=SaveMesh("NetworkDots",NodeMesh(nodes,camera.transform));
        nodeObj.AddComponent<MeshRenderer>().sharedMaterial=nodeMat;
        EditorUtility.SetDirty(normal); EditorUtility.SetDirty(accent); EditorUtility.SetDirty(nodeMat);
    }
    private static void AddLine(string name,Transform parent,Vector3[] points,Material material,float width)
    {
        var obj=Child(name,parent); var line=obj.AddComponent<LineRenderer>();
        line.useWorldSpace=false; line.positionCount=points.Length; line.SetPositions(points);
        line.widthMultiplier=width; line.sharedMaterial=material;line.numCornerVertices=3;
        line.startColor=line.endColor=Color.white;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
    }

    private static Mesh NodeMesh(List<Vector3> nodes,Transform camera)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(var p in nodes)
        {
            int n=vertices.Count;float radius=.055f;
            vertices.Add(p-camera.right*radius-camera.up*radius);vertices.Add(p-camera.right*radius+camera.up*radius);
            vertices.Add(p+camera.right*radius+camera.up*radius);vertices.Add(p+camera.right*radius-camera.up*radius);
            uv.AddRange(new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)});
            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
    }
    private static Mesh FloorMesh()
    {
        var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-35,0,-10),new Vector3(-35,0,50),new Vector3(35,0,50),new Vector3(35,0,-10)};
        mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private static Mesh ExpandForWireframe(Mesh source,int[] slots)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var bary=new List<Vector3>();
        var groups=new List<int[]>();var sv=source.vertices;var sn=source.normals;var st=source.uv;
        foreach(int slot in slots)
        {
            int[] indices=source.GetTriangles(slot);int[] result=new int[indices.Length];
            for(int i=0;i<indices.Length;i++)
            {
                int v=indices[i];result[i]=vertices.Count;vertices.Add(sv[v]);normals.Add(sn[v]);uv.Add(st[v]);
                bary.Add(i%3==0?Vector3.right:i%3==1?Vector3.up:Vector3.forward);
            }
            groups.Add(result);
        }
        var mesh=new Mesh {indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);
        mesh.SetUVs(0,uv);mesh.SetUVs(1,bary);mesh.subMeshCount=slots.Length;
        for(int i=0;i<groups.Count;i++)mesh.SetTriangles(groups[i],i);
        mesh.RecalculateBounds();return mesh;
    }
    private static Mesh SaveMesh(string name,Mesh mesh)
    {
        mesh.name=name;string path=Folder+"/"+name+".asset";
        var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(previous==null) {AssetDatabase.CreateAsset(mesh,path);return mesh;}
        // Mesh是原生对象，CopySerialized只更新CPU数据可能留下旧的GPU顶点缓存。
        previous.Clear();previous.indexFormat=mesh.indexFormat;
        previous.vertices=mesh.vertices;
        if(mesh.normals.Length>0) previous.normals=mesh.normals;
        if(mesh.uv.Length>0) previous.uv=mesh.uv;
        var uv1=new List<Vector3>();mesh.GetUVs(1,uv1);
        if(uv1.Count>0) previous.SetUVs(1,uv1);
        previous.subMeshCount=mesh.subMeshCount;
        for(int i=0;i<mesh.subMeshCount;i++) previous.SetTriangles(mesh.GetTriangles(i),i);
        previous.RecalculateBounds();previous.UploadMeshData(false);
        EditorUtility.SetDirty(previous);Object.DestroyImmediate(mesh);return previous;
    }
    private static Material MaterialAsset(string name,Shader shader)
    {
        string path=Folder+"/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null) {mat=new Material(shader){name=name};AssetDatabase.CreateAsset(mat,path);}
        else mat.shader=shader;
        return mat;
    }
    private static void EnsureFolder()
    {
        if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Generated/MainMenu","Concept");
    }
    private static GameObject Child(string name,Transform parent,params Type[] types)
    {var obj=new GameObject(name,types);obj.transform.SetParent(parent,false);obj.layer=parent.gameObject.layer;return obj;}
    private static void Place(RectTransform rect,Vector2 min,Vector2 max)
    {rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    private static void SetLayer(Transform root,int layer)
    {foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=layer;}
}
