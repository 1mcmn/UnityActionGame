using UnityEngine;
namespace SkillSystem {

    public abstract class SkillComponent : ScriptableObject
    {
        public abstract void OnStart(SkillContext ctx);
        public abstract void OnTick(SkillContext ctx);
        public abstract void OnEnd(SkillContext ctx);
        public abstract void OnInterrupt(SkillContext ctx);

    }

}


