# 刃间 / INTERBLADE 菜单原型

独立 React 网页原型，用来确定 Unity 动作游戏菜单的视觉与交互。用户已选择先制作 Web，并允许用抽象构图替代人物头像。

当前预览：[http://127.0.0.1:4173](http://127.0.0.1:4173)。运行 `启动预览.ps1` 可以重新开启本地预览。

## 页面与操作

- **Start Game**：打开光盘存档轮盘。滚轮、左右方向键、箭头按钮、点击光盘和横向拖动均可切换；支持首尾循环。
- **存档**：首次只有新建入口，不再预设多份示例。右上方新建按钮常驻，命名确认后增加光盘并自动选中；记录保存于当前浏览器，刷新后保留。取消不创建记录，存储不可用时明确提示仅本次会话。圆形光盘、中心孔、印刷封面、镜面反射和斜向空间弧线保留参考视频的方向；Continue 进入演示完成页，尚未连接 Unity 战斗场景或真实客户端存档。
- **光盘交互**：悬停时美术表面随鼠标轻微倾斜，离开后回正；点击确定选择后才播放参考视频中的细椭圆描线动画，三条略不规则曲线在独立前景层沿盘缘展开，滚轮浏览立即收起旧强调。已删除此前偏离参考的折角括号和荧光短线。标题、章节和统计随切换退场、错峰入场，快速反向输入也会恢复完整可见。
- **Settings**：仅主音量、音乐音量、音效音量。实际控制本地合成的预览音频；浏览器第一次交互后才启用声音。音量保存于 localStorage，存储不可用时明确显示仅本次会话。
- **Quit Game**：进入退出状态，用户可关闭浏览器页面，也可以返回主菜单。
- **键盘**：主菜单上下选择、Enter 确认；存档页左右切换、轮盘聚焦时 Enter 确认光盘。继续 / 新建由对应按钮执行；Esc 逐层返回，名称弹窗内 Esc 只取消创建。返回主菜单时恢复原入口焦点。
- **减少动态效果**：遵守系统 prefers-reduced-motion，取消视差、文字入场和动态转场，保留选择功能。

## 美术与实现

设计读法：漫画印刷感的动作游戏菜单，浅灰纸面、硬朗排版、荧光黄绿单一强调色和金属刃片构图。

设计参数：DESIGN_VARIANCE=8、MOTION_INTENSITY=6、VISUAL_DENSITY=3。采用用户原先偏好的浅色空间；主视觉、前景线条与网格分别响应鼠标。字体为自托管 Anton / Barlow Condensed，中文使用系统字体。图标统一使用 Phosphor。

该页面属于游戏菜单，taste-skill 的排版、配色、可访问性和去模板化原则用于视觉；屏幕栈、存档状态与导航遵循 game-ui-design / game-ui-ux。保留用户明确要求的网格、半调和滚轮提示；不将营销落地页的条目数量和装饰禁令机械套入游戏。

## 已安装工具

| 项目 | 固定版本 / 来源 | 安装与实际用途 |
|---|---|---|
| GSAP | 3.15.0，[官方安装文档](https://gsap.com/docs/v3/Installation/) | 本目录的 package.json / pnpm-lock.yaml；转场、光盘运动和视差节奏 |
| React Bits | [DavidHDev/react-bits](https://github.com/DavidHDev/react-bits/tree/ca44b3f9ee180676a06d7de8ec6bea84cddff85b)，提交 ca44b3f9ee180676a06d7de8ec6bea84cddff85b | 按官方支持的源码复制方式引入 SplitText 与 AnimatedContent；源码及许可证在 src/vendor/react-bits |
| taste-skill | [Leonxlnx/taste-skill](https://github.com/Leonxlnx/taste-skill/tree/ce26fc25c0e5e8cab638f883de62d9a86ee5e45b/skills/taste-skill)，提交 ce26fc25c0e5e8cab638f883de62d9a86ee5e45b | Codex Skill 名为 design-taste-frontend，安装于 C:/Users/26053/.codex/skills/design-taste-frontend；安装前读取全部 1206 行，安装后 SHA-256 比对一致 |

React Bits 是组件源码集合，本项目没有安装同名但来源不明的 npm 包。已引入的两个组件只需要 GSAP / React；光盘轮盘按视频单独编写。

React Bits 的许可为 MIT + Commons Clause，允许在应用中使用，禁止把其组件本身作为商品、组件包或移植组件再销售/分发。完整授权保留于 src/vendor/react-bits/LICENSE.md。GSAP 采用作者的 Standard no-charge license，参考其随包许可。

## 启动与构建

已有依赖时：

```powershell
cd D:\UnityActionGame\Tools\MenuPrototype
pnpm dev
```

安装锁定依赖及构建：

```powershell
pnpm install --frozen-lockfile --ignore-scripts
pnpm build
pnpm preview
```

本机已有 Codex 提供的 Node / pnpm。预览绑定 127.0.0.1:4173。网页目录独立于 Assets，Unity 包仍由 Packages/manifest.json 管理。

## 验证与素材来源

- 生产构建通过，浏览器脚本/资源错误为 0。
- 本轮浏览器回归脚本为 ../../Logs/menu_saves_revision_qa.cjs；新报告为 ../../Logs/web-ui-install/saves-revision-qa.json。它创建隔离的浏览器会话，从首次空存档开始验证命名、取消、持久化、悬停、点击强调、文字动画，以及多种尺寸下移动中的侧盘边界；不向用户浏览器写入测试记录。
- **最新结果：39 项通过，浏览器错误 0。** 增加参考反馈的平滑细曲线与连续描绘检查；986×554、1280×720、1440×900、2560×1080、390×844、320×568 共 206 次静止/移动姿态的边界采样通过；986×554 整页可见。已查看桌面、较矮桌面、手机与确认动画截图。
- Web 生产构建通过；按项目约定运行 compile_check.bat --nopause，三个 Unity 正式程序集编译退出 0，最新日志为 ../../Logs/web-ui-install/compile-check-reference-ink.log。没有将本轮 Web 视觉接入 Unity。
- 截图在 previews/。首版的 browser-qa.json 与 21 项检查保留历史记录，不能替代本轮回归结果。
- 首版 Lighthouse 13.5.0 桌面模式检查主菜单：性能 98、可访问性 100、最佳实践 100；完整历史报告在 ../../Logs/web-ui-install/lighthouse-desktop.report.html。它没有重新评测本轮的存档弹窗，不是 Unity 战斗性能记录。
- 光盘参考视频：用户提供的 Screenshots/38edc358c8ea5612daff17078ad98cf4.mp4，长度 5.83 秒，已提取并查看六个时点。
- 主视觉 assets/blade-sculpture.png 使用内置 image_gen 生成，1254×1254 RGBA，已检查真实透明通道；最终提示词保留于 assets/blade-sculpture.prompt.txt。此前的 momo-head.png 为旧方向素材，未加载、未打入网页构建。
- 光盘封面使用程序绘制的图形、排版和上述主视觉，不复制视频中的电影封面。

统一动画参数在 src/motion.js，分层、事件、投影边界和 PrimeTween 缓动对应见 [Unity 移植说明](UNITY_MOTION.md)。后续移植到 Unity 时，需单独选择实施方案并接入真实存档与场景加载。本原型用于视觉和交互评审。
