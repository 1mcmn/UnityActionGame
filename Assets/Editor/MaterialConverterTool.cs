using UnityEngine;
using UnityEditor;

public class MaterialConverterTool : EditorWindow
{
    [MenuItem("Tools/Batch Convert Shaders to URP Lit")]
    public static void ConvertMaterials()
    {
        // 查找项目里所有后缀为 .mat 的文件（材质球）
        string[] guids = AssetDatabase.FindAssets("t:Material");
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            // 如果找到了材质，且它当前的 Shader 不是 URP Lit
            if (mat != null && mat.shader.name != "Universal Render Pipeline/Lit")
            {
                // 强制替换为 URP Lit Shader
                mat.shader = Shader.Find("Universal Render Pipeline/Lit");
                count++;
                // 标记这个材质已经被修改过
                EditorUtility.SetDirty(mat);
            }
        }

        // 保存所有修改
        AssetDatabase.SaveAssets();
        Debug.Log($"✅ 批量修改完成！共将 {count} 个材质球的 Shader 替换成了 URP Lit！");
    }
}