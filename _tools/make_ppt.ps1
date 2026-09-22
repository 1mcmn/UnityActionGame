$ErrorActionPreference = 'Stop'
$outPath = 'E:\Unity\My project\开题答辩_邬锦飞.pptx'
Remove-Item $outPath -Force -ErrorAction SilentlyContinue

function RGBv([int]$r,[int]$g,[int]$b){ return $r + $g*256 + $b*65536 }
$DARK  = RGBv 31 56 100
$ACC   = RGBv 68 114 196
$GRAY  = RGBv 89 89 89
$LIGHT = RGBv 217 226 243
$FONT  = '微软雅黑'

$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Add()
$pres.PageSetup.SlideWidth = 960
$pres.PageSetup.SlideHeight = 540

function New-Slide([string]$title){
  $s = $pres.Slides.Add($pres.Slides.Count + 1, 12)
  $tb = $s.Shapes.AddTextbox(1, 40, 26, 880, 50)
  $tr = $tb.TextFrame.TextRange
  $tr.Text = $title
  $tr.Font.Size = 27; $tr.Font.Bold = -1; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
  $tr.Font.Color.RGB = $DARK
  $ln = $s.Shapes.AddShape(1, 40, 80, 880, 3)
  $ln.Fill.ForeColor.RGB = $ACC; $ln.Line.Visible = 0
  return $s
}

function Add-Body($s, [string[]]$lines, [int]$size = 17, [int]$top = 100, [int]$left = 40, [int]$width = 880, [int]$height = 400){
  $tb = $s.Shapes.AddTextbox(1, $left, $top, $width, $height)
  $tb.TextFrame.WordWrap = -1
  $tr = $tb.TextFrame.TextRange
  $tr.Text = ($lines -join "`r")
  $tr.Font.Size = $size; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
  $tr.Font.Color.RGB = $GRAY
  $tr.ParagraphFormat.SpaceWithin = 1.1
  $tr.ParagraphFormat.SpaceAfter = 5
  return $tb
}

function Add-Table($s, $rows, [int]$top, [int]$left = 40, [int]$width = 880, [int]$height = 260, [int]$fs = 12){
  $r = $rows.Count; $c = $rows[0].Count
  $shp = $s.Shapes.AddTable($r, $c, $left, $top, $width, $height)
  $tbl = $shp.Table
  for($i = 0; $i -lt $r; $i++){
    for($j = 0; $j -lt $c; $j++){
      $cell = $tbl.Cell($i + 1, $j + 1)
      $tr = $cell.Shape.TextFrame.TextRange
      $tr.Text = [string]$rows[$i][$j]
      $tr.Font.Size = $fs; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
      $tr.Font.Color.RGB = $GRAY
      if($i -eq 0){ $tr.Font.Bold = -1; $cell.Shape.Fill.ForeColor.RGB = $LIGHT }
    }
  }
  return $shp
}

function Add-Notes($s, [string]$t){
  try { $s.NotesPage.Shapes.Placeholders(2).TextFrame.TextRange.Text = $t } catch { }
}

function Add-Box($s, [string]$text, [int]$left, [int]$top, [int]$w, [int]$h, [int]$fs = 12){
  $sh = $s.Shapes.AddShape(5, $left, $top, $w, $h)   # 5 = 圆角矩形
  $sh.Fill.ForeColor.RGB = $LIGHT
  $sh.Line.ForeColor.RGB = $ACC
  $tr = $sh.TextFrame.TextRange
  $tr.Text = $text
  $tr.Font.Size = $fs; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
  $tr.Font.Color.RGB = $DARK
  return $sh
}

function Add-ArrowDown($s, [int]$left, [int]$top){
  $a = $s.Shapes.AddShape(36, $left, $top, 20, 16)   # 36 = 下箭头
  $a.Fill.ForeColor.RGB = $ACC; $a.Line.Visible = 0
}
function Add-ArrowRight($s, [int]$left, [int]$top){
  $a = $s.Shapes.AddShape(33, $left, $top, 20, 16)   # 33 = 右箭头
  $a.Fill.ForeColor.RGB = $ACC; $a.Line.Visible = 0
}

# ───────────────── 第 1 页 封面 ─────────────────
$s1 = $pres.Slides.Add(1, 12)
$t = $s1.Shapes.AddTextbox(1, 70, 140, 820, 70)
$t.TextFrame.TextRange.Text = "基于Unity的动作游戏与编辑器工具的设计与实现"
$t.TextFrame.TextRange.Font.Size = 32; $t.TextFrame.TextRange.Font.Bold = -1
$t.TextFrame.TextRange.Font.Name = $FONT; $t.TextFrame.TextRange.Font.NameFarEast = $FONT
$t.TextFrame.TextRange.Font.Color.RGB = $DARK
$t2 = $s1.Shapes.AddTextbox(1, 70, 220, 820, 40)
$t2.TextFrame.TextRange.Text = "毕业设计（论文）开题答辩"
$t2.TextFrame.TextRange.Font.Size = 18; $t2.TextFrame.TextRange.Font.Name = $FONT
$t2.TextFrame.TextRange.Font.NameFarEast = $FONT; $t2.TextFrame.TextRange.Font.Color.RGB = $ACC
$t3 = $s1.Shapes.AddTextbox(1, 70, 330, 820, 140)
$t3.TextFrame.TextRange.Text = "姓　名：邬锦飞        学　号：259400232`r专　业：软件工程（专升本）    班　级：一班`r系　别：计算机        指导教师：王欣`r2026 年 9 月"
$t3.TextFrame.TextRange.Font.Size = 16; $t3.TextFrame.TextRange.Font.Name = $FONT
$t3.TextFrame.TextRange.Font.NameFarEast = $FONT; $t3.TextFrame.TextRange.Font.Color.RGB = $GRAY
Add-Notes $s1 "各位老师好，我是邬锦飞，我的课题是《基于Unity的动作游戏与编辑器工具的设计与实现》，下面汇报选题背景、研究内容与技术路线。"

# ───────────────── 第 2 页 选题背景与意义 ─────────────────
$s2 = New-Slide "一、选题背景与意义"
Add-Body $s2 @(
  "◆ 行业现状：动作/ARPG 项目普遍采用""有限状态机 + 数据驱动 + 自研编辑器工具链""，战斗内容生产从代码硬编码转向策划可视化配置",
  "",
  "◆ 现存痛点：多数学生项目与小型团队仍在 Update 中写大量 if-else 状态判断、用角色中心大范围球形检测",
  "      → 动作僵硬、隔空判定、扩展困难、非程序人员无法参与内容生产",
  "",
  "◆ 打击感的本质：不是单一功能，而是命中判定、顿帧、受击硬直、削韧、音效分层、伤害数字等要素的系统叠加",
  "      → 手感 = 打得上 + 打得动 + 有反馈，必须工程化拆解",
  "",
  "◆ 课题意义：工程意义（把主观手感拆成可度量参数）／效率意义（内容生产脱离代码）／学习意义（完整工程实践）"
) 16 100
Add-Notes $s2 "动作游戏的核心竞争力是打击感，但它由多个要素叠加而成。目前常见做法是把伤害、连击窗口、判定范围硬编码在控制器里，且用角色中心大球形检测，导致隔空判定、调参必须改代码。本课题就是把""手感""拆成可配置的工程参数，并用编辑器工具链让内容生产脱离代码。"

# ───────────────── 第 3 页 研究基本内容 ─────────────────
$s3 = New-Slide "二、研究基本内容"
$bl = $s3.Shapes.AddTextbox(1, 40, 96, 860, 40)
$bl.TextFrame.TextRange.Text = "目标：以 Unity 为平台，实现可运行的第三人称动作战斗 Demo + 可视化编辑器工具链"
$bl.TextFrame.TextRange.Font.Size = 15; $bl.TextFrame.TextRange.Font.Bold = -1
$bl.TextFrame.TextRange.Font.Name = $FONT; $bl.TextFrame.TextRange.Font.NameFarEast = $FONT
$bl.TextFrame.TextRange.Font.Color.RGB = $DARK
Add-Table $s3 @(
  @('模块组','包含内容'),
  @('玩家与相机','十态枚举状态机、相机相对输入、Root Motion 位移、环绕相机（俯仰限制 + 帧率无关指数平滑）'),
  @('战斗系统','连招推进与连击窗口、伤害结算、削韧与硬直、击退、无敌帧、弹反、命中判定'),
  @('敌人 AI','数据层 + C# 事件广播、13 态行为状态机'),
  @('技能与反馈','组件化技能（技能库 + 运行时管理器）、顿帧、分层命中音效、伤害数字、对象池'),
  @('工具与 UI','技能编辑器、音效管理器；玩家血条 / 敌人状态条 / Boss 血条；EditMode 单元测试')
) 146 40 880 300 13
Add-Notes $s3 "研究内容分五块：玩家与相机、战斗系统、敌人 AI、技能与反馈、编辑器工具与 UI。其中技能系统和两个编辑器工具是本课题重点，其余是支撑可玩闭环的基础。"

# ───────────────── 第 4 页 系统总体技术框架 ─────────────────
$s4 = New-Slide "三、系统总体技术框架"
Add-Table $s4 @(
  @('类别','技术 / 框架','用途'),
  @('引擎与语言','团结引擎 1.9.2（Unity 2022.3 LTS 内核）、C#','运行环境与开发语言'),
  @('渲染管线','Universal Render Pipeline（URP）','场景与角色渲染'),
  @('角色动画','Animator 状态机 + Blend Tree + Root Motion','移动混合、动作切换、动画驱动位移'),
  @('物理与判定','Rigidbody + OverlapSphere / OverlapCapsule / OverlapBox','移动与命中判定'),
  @('数据层','ScriptableObject + [Serializable] + [SerializeReference]','配置资产化、组件多态内嵌'),
  @('运行时框架','枚举状态机 + 单例管理器 + C# 事件（观察者）','行为组织与模块解耦'),
  @('编辑器扩展','EditorWindow、SerializedObject、ReorderableList、AssetDatabase','编辑器工具界面与数据绑定'),
  @('音效与测试','SoundManager + SoundLibrary + 动画事件中继器；Unity Test Framework','音效体系与质量保障')
) 96 40 880 330 12
Add-Body $s4 @("三层架构：编辑器层（可视化配置）→ 数据层（ScriptableObject 资产，唯一数据源）→ 运行时层（只读资产执行）；层间只通过资产文件通信") 13 432 40 880 60
Add-Notes $s4 "系统分三层：编辑器层负责可视化配置，数据层用 ScriptableObject 承载配置，运行时层只读资产执行；层间只通过资产文件通信，这正是""编辑器修改、游戏即时生效""的机制。技术上用 Animator + Root Motion 做动画，Rigidbody + 物理查询做判定，编辑器侧基于 UnityEditor API。"

# ───────────────── 第 5 页 技术路线① ─────────────────
$s5 = New-Slide "四、技术路线①：数据驱动的战斗参数层"
Add-Body $s5 @(
  "痛点：伤害、连击窗口、判定半径写死在控制器里 → 调手感必须改代码，非程序人员无法参与",
  "做法：以 ScriptableObject 资产承载全部可配置数据；把主观手感拆成可度量参数（单段时长、判定半径、连击窗口百分比、各段伤害、顿帧时长、无敌帧区间）",
  "关键设计：资产优先 + 内联回退 —— 资产未赋值时使用代码默认值，游戏仍可正常运行"
) 15 96
Add-Table $s5 @(
  @('数据资产','主要字段','作用'),
  @('ComboData','attackDuration、attackRadius、comboWindowPercent、comboDamages[]','玩家连招参数'),
  @('EnemyConfig','maxHealth、maxPoise、detectRadius、attackRadius、walkSpeed、attackCooldown、damageReduction 等','敌人属性与行为参数'),
  @('SoundLibrary','SoundItem[]：soundID、clip','音效映射'),
  @('SkillLibrary / SkillConfig','技能索引表 → skillId、skillName、组件列表','技能配置')
) 250 40 880 230 12
Add-Notes $s5 "第一条路线是数据驱动。我把连招、敌人、音效、技能这些参数全部资产化，改数值不用改代码；并且做了""资产优先、内联回退""，资产没配游戏也能跑。"

# ───────────────── 第 6 页 技术路线② ─────────────────
$s6 = New-Slide "四、技术路线②：帧级判定 + 生命周期闭环"
Add-Body $s6 @(
  "◆ 痛点：球形判定与角色朝向导致隔空判定（剑未接触即扣血）；动画播放、伤害判定、音效触发时机不一致；动作/技能被打断后判定区与无敌帧残留",
  "",
  "◆ 做法一（已实现）：以动画帧为时间基准 —— 判定窗口（帧区间）内才开启判定，帧级事件在指定帧触发音效，使动画、判定、音效三者对齐",
  "◆ 做法二（已实现）：判定形状可配置（球 / 胶囊 / 盒），一次挥击对同一目标只结算一次",
  "◆ 做法三（已实现）：技能 = ID + 组件列表（播放动画 / 判定窗口 / 伤害 / 命中反馈 / 无敌帧），组件以 [SerializeReference] 内嵌，可装配可增删",
  "◆ 做法四（已实现）：四态生命周期 释放 → 运行 → 结束 / 打断；打断时统一清理（关闭判定区、复位无敌帧），不残留"
) 15 96
Add-Notes $s6 "第二条路线解决判定与表现不一致。我把判定改为按动画帧驱动，只在判定窗口内开判定、按帧触发音效；判定形状支持球/胶囊/盒；技能由组件装配，并用四态生命周期管理，被打断时统一清理，绝不残留判定区或无敌帧。"

# ───────────────── 第 7 页 技术路线③ ─────────────────
$s7 = New-Slide "四、技术路线③：编辑器工具链与资产解耦"
Add-Body $s7 @(
  "痛点：连招、技能、音效的高频改动都要程序员介入，扩展困难、协作成本高",
  "",
  "◆ 技能编辑器：技能 / 组件列表配置、组件参数与命名、帧事件表格（帧号 / 音效 ID / 音量 / 编辑器内试听）",
  "◆ 音效管理器：音效库表格化编辑（soundID ↔ clip）、编辑器内试听",
  "◆ 闭环：编辑器写资产 → 运行时按 ID 读取执行 → 表现变化；编辑器与运行时零直接依赖"
) 15 96
Add-Box $s7 "策划操作`n（加组件/调参/配帧）" 40 320 150 60 11
Add-ArrowRight $s7 196 342
Add-Box $s7 "EditorWindow`nSerializedObject" 222 320 150 60 11
Add-ArrowRight $s7 378 342
Add-Box $s7 "ScriptableObject `n资产（唯一数据源）" 404 320 150 60 11
Add-ArrowRight $s7 560 342
Add-Box $s7 "SkillManager`n运行时按 ID 释放" 586 320 150 60 11
Add-ArrowRight $s7 742 342
Add-Box $s7 "游戏表现`n动画/伤害/音效" 768 320 152 60 11
Add-Body $s7 @("整条链路不需要改代码、不需要重新编译 —— 这就是""编辑器修改，游戏即时生效""") 14 400 40 880 60
Add-Notes $s7 "第三条路线是工具链。技能编辑器可以配置技能、组件和帧级音效事件，并支持编辑器内试听；音效管理器负责音效库的表格化编辑。两者都只写资产，运行时只读资产，整条链路不需要改代码。"

# ───────────────── 第 8 页 主要问题与应对 ─────────────────
$s8 = New-Slide "五、主要问题与应对"
Add-Table $s8 @(
  @('主要问题','应对思路','当前状态'),
  @('战斗逻辑与数据耦合，调参必须改代码','数据层—逻辑层—表现层三层结构，表现层订阅 C# 事件','已落地'),
  @('隔空判定（球形检测与武器位置无关）','判定窗口 + 可配判定形状；武器级判定为下一步优化','球形已实现，武器级拟采用'),
  @('动作/技能打断后状态残留','四态生命周期 + OnInterrupt 统一清理','已实现'),
  @('高速挥砍漏判风险','连续碰撞检测 / 扫掠体积检测','拟采用'),
  @('顿帧重叠导致时间缩放恢复错乱','拟改为引用计数统一恢复','已知待改进'),
  @('高频对象创建销毁的 GC 压力','音效与伤害数字对象池','已实现'),
  @('敌人动画切换失败','对齐控制器状态名与代码引用','正在解决')
) 96 40 880 330 12
Add-Notes $s8 "主要问题集中在判定精度、打断清理和资源开销上，报告里逐条列了应对方案与当前进展。其中敌人动画控制器状态名对齐是当前正在修复的重点。"

# ───────────────── 第 9 页 步骤 / 进度 / 成果 ─────────────────
$s9 = New-Slide "六、研究步骤、工作进度与预期成果"
Add-Body $s9 @(
  "研究步骤：① 需求分析与最小可玩闭环原型　② 玩家控制与相机 → 战斗系统 → 敌人 AI 与音效",
  "　　　　　③ 技能系统与编辑器工具链（技能编辑器、音效管理器）　④ 联调优化 → 论文与答辩准备"
) 14 94 40 880 50
Add-Table $s9 @(
  @('时间','工作内容'),
  @('2026.07 - 09','开题报告；玩家控制、第三人称相机与基础战斗'),
  @('2026.09 下旬','敌人 AI 状态机与命中检测优化'),
  @('2026.10','技能系统与编辑器工具链初步开发'),
  @('2026.10 下旬 - 11 上旬','完善技能 / 音效编辑器，优化打击感反馈'),
  @('2026.11','系统模块整合、UI 完善、单元测试；撰写论文初稿'),
  @('2026.11 下旬 - 12','按评阅意见修改论文、录制演示视频、答辩')
) 156 40 880 250 12
Add-Body $s9 @("预期成果：可运行的动作战斗 Demo ／ 技能编辑器 + 音效管理器 ／ 毕业论文 ／ 演示视频与答辩材料") 13 420 40 880 60
Add-Notes $s9 "进度安排与任务书一致：目前游戏闭环已完成，两个编辑器工具已能配置并验证效果，后续重点是手感调优、论文撰写与演示准备。请各位老师批评指正。"

# ───────────────── 备用页 A ─────────────────
$sA = New-Slide "备用页 A：技术选型对比（提问时使用）"
Add-Table $sA @(
  @('决策点','选择','对比方案与理由'),
  @('动画系统','Animator 状态机','Animancer（付费、任务书指定 Animator）；Blend Tree + Trigger 满足需求'),
  @('编辑器 UI','IMGUI（EditorGUILayout）','UI Toolkit（UXML/USS 学习曲线陡）；IMGUI 与任务书技术栈一致'),
  @('数据存储','ScriptableObject','硬编码（不可配）／ JSON（需路径管理）'),
  @('组件序列化','[SerializeReference] 内嵌','独立资产引用（反复创建文件、策划负担重）'),
  @('运行时通信','单例 + C# 事件','全局事件总线（字符串键、难调试）／ DI 框架（超纲）'),
  @('判定时机','动画帧窗口驱动','固定延迟（表现与判定脱节）')
) 96 40 880 330 12

# ───────────────── 备用页 B ─────────────────
$sB = New-Slide "备用页 B：已完成功能清单（演示用）"
Add-Body $sB @(
  "◆ 玩家：十态状态机、Root Motion 位移、连击（动画进度驱动的连击窗口 + 残响窗口）、闪避、格挡与泄力、弹反、受击/倒地",
  "◆ 敌人：13 态 FSM、韧性/僵直/击倒、事件广播、伤害数字",
  "◆ 反馈：顿帧、分层命中音效（前缀随机 + 音高随机）、伤害数字对象池",
  "◆ 技能系统：技能库 + 组件（播放动画 / 判定窗口 / 伤害 / 命中反馈 / 无敌帧）+ 生命周期清理",
  "◆ 编辑器：技能编辑器（组件配置 + 帧事件表格 + 试听）、音效管理器（库编辑 + 试听）",
  "◆ 工程：4 组 EditMode 单元测试、编译门禁脚本"
) 15 100

$pres.SaveAs($outPath, 24)
$pres.Close()
$ppt.Quit()
Start-Sleep -Seconds 2
"生成完成: " + (Test-Path $outPath) + "  大小: " + [math]::Round((Get-Item $outPath).Length / 1KB, 1) + " KB"



