# -*- coding: utf-8 -*-
"""向记忆库/02_踩坑记录.md 追加一段内容（只追加，不改动已有内容）。"""

import os

PATH = r"E:\Unity\My project\记忆库\02_踩坑记录.md"

ENTRY = """

## 2026-09-22
- **技能播完/被打断后角色卡死在技能动画里**：根因两条——① 技能播的状态 `AT1` 在 `Player_NewAnimCtrl.controller` 里 `m_Transitions: []`（死胡同；对比 at2/at3/at4/Locomotion/Idle 都有出口过渡）；② `PlayAnimationComponent.OnEnd / OnInterrupt` 是空实现，技能结束时没人把 Animator 切回去，而玩家状态机靠设参数（Movement/Run/Trigger）驱动动画，停在 `AT1` 上时按键也救不回来。**解法（事件通信，方案 C）**：新增 `SkillEvents.AnimationReturnRequested`，`PlayAnimationComponent` 新增 `returnStateName` 字段并在 OnEnd/OnInterrupt 抛事件；`PlayerAnimController` 订阅后调用 `ReturnToLocomotion()`（留空则按 `Movement` 参数自动选 Idle 或 Locomotion）。详情见根目录 `错误记录.md` 的 BUG-001
- **经验**：动画机里"后加的状态"最容易漏连出口过渡；代码 CrossFade 进去之后没有过渡就是死胡同。以后新增技能/动作状态，先检查该状态的 `m_Transitions` 是否为空
"""


def main():
    with open(PATH, "r", encoding="utf-8") as f:
        text = f.read()
    if "2026-09-22" in text and "AT1" in text:
        print("已存在 2026-09-22 条目，跳过")
        return
    if not text.endswith("\n"):
        text += "\n"
    text += ENTRY
    with open(PATH, "w", encoding="utf-8") as f:
        f.write(text)
    print("已追加，文件行数：", len(text.splitlines()))


if __name__ == "__main__":
    main()
