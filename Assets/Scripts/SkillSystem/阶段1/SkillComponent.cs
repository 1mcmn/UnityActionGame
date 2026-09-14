using System;
using UnityEngine;

namespace SkillSystem
{
    // 组件基类（v2：内嵌模式）。
    // 2026-08-22 决策变更：组件不再是独立 ScriptableObject 资产，而是内嵌在技能配置里
    // （SkillConfig.components 用 [SerializeReference] 存储），在技能编辑器界面里直接添加、命名、调参。
    // "全局组件库/模板"降级为后续扩展（论文展望）。
    // 运行时接口不变。注意：组件是共享配置数据，不能存实例状态；运行时瞬态数据一律放 SkillContext.runtimeData。
    [Serializable]
    public abstract class SkillComponent
    {
        [Tooltip("组件显示名（留空则显示类型名）")]
        public string displayName = "";

        public abstract void OnStart(SkillContext ctx);
        public abstract void OnTick(SkillContext ctx);
        public abstract void OnEnd(SkillContext ctx);
        public abstract void OnInterrupt(SkillContext ctx);
    }
}
