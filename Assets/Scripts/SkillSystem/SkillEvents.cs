using System;
using UnityEngine;

namespace SkillSystem
{
    /// <summary>
    /// 技能与角色之间的轻量事件通道。
    /// 目前只有一个用途：技能【结束或被打断】时，把动画控制权交还给角色，
    /// 由角色自己决定回哪个状态（待机 / 移动混合树），技能不再直接抢动画机。
    ///
    /// 为什么用事件而不是让技能直接操作 Animator：
    /// 技能是数据（ScriptableObject 上的配置），不应该知道角色有几种状态；
    /// 交还控制权的时机由技能决定，具体播什么由角色决定。
    /// </summary>
    public static class SkillEvents
    {
        /// <summary>
        /// 技能请求归还动画控制权。
        /// 参数 1：技能释放者（用来判断"这是不是我身上的技能"）；
        /// 参数 2：技能建议的状态名，可为空 —— 留空表示由接收方自行决定。
        /// </summary>
        public static event Action<Transform, string> AnimationReturnRequested;

        public static void RequestAnimationReturn(Transform caster, string suggestedState)
        {
            if (caster == null) return;
            AnimationReturnRequested?.Invoke(caster, suggestedState ?? "");
        }
    }
}
