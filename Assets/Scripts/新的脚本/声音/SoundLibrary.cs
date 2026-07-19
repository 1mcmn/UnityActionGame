using UnityEngine;

// 👑 这一行是“菜单生成器”。它告诉 Unity：
// "在右键菜单里，建一个叫 Audio 的文件夹，里面放一个叫 SoundLibrary 的选项"
[CreateAssetMenu(fileName = "New SoundLibrary", menuName = "Audio/SoundLibrary")]
public class SoundLibrary : ScriptableObject
{
    public SoundItem[] sounds;
}

[System.Serializable]
public class SoundItem
{
    public string soundID;   // 比如 "SwordSlash_01"
    public AudioClip clip;
}