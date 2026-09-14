using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SkillSystem;

namespace SkillSystem {
    [CreateAssetMenu(fileName ="newSkillLibrary",menuName ="SkillSystem/SkillLibrary")]
        public class SkillLibrary: ScriptableObject
        {
            
          public  List<SkillConfig> skills =new List<SkillConfig>();

            public SkillConfig Get(string id)
            {
                foreach (var skill in skills)
                {
                    if (skill != null && skill.skillId == id)
                    {
                        return skill;
                    }
                }
                return null;
            }
        }

}


