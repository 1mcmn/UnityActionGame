using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "AudioCreatetable", menuName = "createAudiotable")]
public class createSritable : ScriptableObject
{
    [System.Serializable]
    public struct AudioEntry
    {
        public string audioname;
        public AudioClip clip;

    }
    public List<AudioEntry> Audioentry = new List<AudioEntry>();
}
