using System;
using UnityEngine;

namespace SkillSystem
{
    [Serializable]
    public class InvincibleWindowComponent : SkillComponent
    {
        public int startFrame = 0;
        public int endFrame = 10;

        private class InvincibleState { public bool isActive = false; }

        public override void OnStart(SkillContext ctx)
        {
            ctx.runtimeData["InvincibleWindow"] = new InvincibleState();
        }

        public override void OnTick(SkillContext ctx)
        {
            if (!ctx.runtimeData.ContainsKey("CurrentFrame")) return;
            int currentFrame = (int)ctx.runtimeData["CurrentFrame"];
            var state = (InvincibleState)ctx.runtimeData["InvincibleWindow"];

            bool shouldBeInvincible = currentFrame >= startFrame && currentFrame <= endFrame;
            if (shouldBeInvincible && !state.isActive)
            {
                state.isActive = true;
                SetInvulnerable(ctx, true);
            }
            else if (!shouldBeInvincible && state.isActive)
            {
                state.isActive = false;
                SetInvulnerable(ctx, false);
            }
        }

        public override void OnEnd(SkillContext ctx) { SetInvulnerable(ctx, false); }
        public override void OnInterrupt(SkillContext ctx) { SetInvulnerable(ctx, false); }

        private void SetInvulnerable(SkillContext ctx, bool invincible)
        {
            var pc = ctx.caster.GetComponent<PlayerCombat>();
            if (pc != null) pc.SetInvulnerable(invincible);
        }
    }
}