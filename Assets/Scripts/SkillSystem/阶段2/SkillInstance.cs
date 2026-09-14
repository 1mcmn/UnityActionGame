
using UnityEngine;
namespace SkillSystem
{
    public enum SkillLifecycleState
    {
        None,
        Released,
        Running,
        Ended,
        Interrupted
    }
    public class SkillInstance
    {
        public SkillConfig config;
        public SkillContext context;
        public SkillLifecycleState state;
        public float startTime;

        public SkillInstance(SkillConfig config, SkillContext context)
        {
            this.config = config;
            this.context = context;
            this.state = SkillLifecycleState.Released;
            this.startTime = Time.time;
        }
        public void RequestFinish()
        {
            if (state == SkillLifecycleState.Running)
            {
                state = SkillLifecycleState.Ended;
            }
        }
        public void RequestInterrupt()
        {
            if (state == SkillLifecycleState.Running)
            {

                state = SkillLifecycleState.Interrupted;
            }
        }
    }
}

