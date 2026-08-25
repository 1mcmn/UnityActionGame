using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class 音效条目管理器 : EditorWindow
{
    [MenuItem("自定义菜单/音效条目管理器")]
    static void 打开窗户()
    {
        窗户();
    }
    static void 窗户()
    {
        GetWindow<音效条目管理器>(false, "音效条目窗口");

    }
    private SoundLibrary _audioconfig;
    private SerializedObject _serializeobject;
    private SerializedProperty _serializedproperty;
    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("音效窗口",GUILayout.Width(100));
        SoundLibrary newconfig = (SoundLibrary)EditorGUILayout.ObjectField(_audioconfig, typeof(SoundLibrary), false);
        if (newconfig != _audioconfig)
            {
                _audioconfig = newconfig;
            if (_audioconfig != null)
            {
                _serializeobject = new SerializedObject(_audioconfig);
                _serializedproperty = _serializeobject.FindProperty("sounds");
            }

        }
       
        EditorGUILayout.EndHorizontal();
        if(_audioconfig==null)
        {
            EditorGUILayout.HelpBox("请选择音频资产", MessageType.Warning);
            return;
        }
        if (_serializedproperty == null)
        {
            EditorGUILayout.HelpBox("找不到资产中的 'sounds' 列表，请检查变量名拼写！", MessageType.Error);
            return; // 直接结束，防止崩溃
        }

        _serializeobject.Update();
        for (int i=0;i<_serializedproperty.arraySize;i++)
        {
          EditorGUILayout.BeginHorizontal();
            SerializedProperty element = _serializedproperty.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = element.FindPropertyRelative("soundID");
            SerializedProperty clipProp = element.FindPropertyRelative("clip");
            nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue,GUILayout.Width(100));
            clipProp.objectReferenceValue=EditorGUILayout.ObjectField(clipProp.objectReferenceValue, typeof(AudioClip), false);
            if(GUILayout.Button("删除"))
            {
                _serializedproperty.DeleteArrayElementAtIndex(i);
                break;
            }
          EditorGUILayout.EndHorizontal();
            
        }
        if(GUILayout.Button("添加音效"))
            {
                _serializedproperty.InsertArrayElementAtIndex(_serializedproperty.arraySize);

            }
            _serializeobject.ApplyModifiedProperties();
    }
}
