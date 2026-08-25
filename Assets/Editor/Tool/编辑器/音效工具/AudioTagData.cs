using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
[CreateAssetMenu(fileName ="AudioTagData",menuName ="createAudioTagData")]
public class AudioTagData : ScriptableObject
{
    [System.Serializable]
    public struct AudioTagEntry
    {
        public string audioPath;
        public List<string> audioTags;
    }
    
    public List<AudioTagEntry> tagList = new List<AudioTagEntry>();


}
