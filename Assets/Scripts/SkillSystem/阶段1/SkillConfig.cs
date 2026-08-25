using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    [CreateAssetMenu(fileName = "NewSkillConfig", menuName = "SkillSystem/Skill Config")]
    public class SkillConfig : ScriptableObject
    {
        public string skillId;
        public string skillName;
        public List<SkillComponent> components = new List<SkillComponent>();
    }
}



