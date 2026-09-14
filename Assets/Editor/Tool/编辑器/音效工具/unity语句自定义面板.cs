using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class unity语句自定义面板 : EditorWindow

{
    [MenuItem("自定义菜单/打开测试用窗口")]
   static void 菜单按下()
    {
        创建窗口();
    }
    //static unity语句自定义面板(){
    //    创建窗口();
        
    //}
     static void 创建窗口()
    {
        GetWindow<unity语句自定义面板>(false, "测试用窗口");

    }
    private void OnGUI()
    {
        GUILayout.TextArea("文本提示:", GUILayout.Width(200));
        if(GUILayout.Button("退出按钮",GUILayout.Width(200)))
            {
            this.Close();

        }
    }
}
