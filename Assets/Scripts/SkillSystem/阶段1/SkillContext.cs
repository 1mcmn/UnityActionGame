using System;
using UnityEngine;
namespace SkillSystem { 
    public class SkillContext 
    {
        public Transform caster;
        public Vector3 targetPos;
        public Transform target;
        public string skillId;
        public float startTime;
        public Action onFinish;
        public SkillContext(Transform caster,Vector3 targetPos,
        Transform target=null,string skillId="")
        {
            this.caster = caster;
            this.targetPos= targetPos;
            this.target = target;
            this.skillId = skillId;
            this.startTime = Time.time;
        }

    }
   
}
