$ErrorActionPreference = 'Stop'
$outPath = 'E:\Unity\My project\开题答辩_极简版.pptx'
Remove-Item $outPath -Force -ErrorAction SilentlyContinue

function RGBv([int]$r,[int]$g,[int]$b){ return $r + $g*256 + $b*65536 }
$DARK   = RGBv 31 56 100      # 1F3864
$ACC    = RGBv 68 114 196     # 4472C4
$LIGHT  = RGBv 217 226 243    # D9E2F3
$PANEL  = RGBv 242 244 248    # F2F4F8
$GRAY   = RGBv 89 89 89       # 595959
$TEXT   = RGBv 51 51 51       # 333333
$MID    = RGBv 143 170 220    # 8FAADC
$RED    = RGBv 192 0 0        # C00000
$REDBG  = RGBv 251 233 231    # FBE9E7
$GRN    = RGBv 46 125 50      # 2E7D32
$GRNBG  = RGBv 232 245 233    # E8F5E9
$WHITE  = RGBv 255 255 255
$FONT   = '微软雅黑'

$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Add()
$pres.PageSetup.SlideWidth = 960
$pres.PageSetup.SlideHeight = 540

# 返回形状对象：调用方用 $sh = Card ... 接收，或用 $null = Card ... 丢弃（避免污染管道）
function Card($s,[int]$x,[int]$y,[int]$w,[int]$h,[int]$fill,[int]$rounded = 5){
  $sh = $s.Shapes.AddShape($rounded, $x, $y, $w, $h)
  $sh.Fill.ForeColor.RGB = $fill
  $sh.Line.Visible = 0
  return $sh
}

# 不返回任何对象
function Txt($s,[string]$t,[int]$x,[int]$y,[int]$w,[int]$sz,[int]$color,[int]$bold = 0,[int]$align = 1,[int]$h = 40){
  $tb = $s.Shapes.AddTextbox(1, $x, $y, $w, $h)
  $tb.TextFrame.AutoSize = 0
  $tb.TextFrame.WordWrap = 0
  $tb.TextFrame.MarginLeft = 0; $tb.TextFrame.MarginRight = 0
  $tb.TextFrame.MarginTop = 0;  $tb.TextFrame.MarginBottom = 0
  $tr = $tb.TextFrame.TextRange
  $tr.Text = $t
  $tr.Font.Size = $sz; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
  $tr.Font.Color.RGB = $color
  if($bold -ne 0){ $tr.Font.Bold = $bold }
  $tr.ParagraphFormat.Alignment = $align
  $tr.ParagraphFormat.SpaceWithin = 1.15
  $null = $tb
}

function CardTxt($sh,[string]$t,[int]$sz,[int]$color,[int]$align = 2,[int]$bold = 0,[int]$ml = 6){
  $tf = $sh.TextFrame
  $tf.AutoSize = 0; $tf.WordWrap = -1; $tf.VerticalAnchor = 3
  $tf.MarginLeft = $ml; $tf.MarginRight = 6; $tf.MarginTop = 0; $tf.MarginBottom = 0
  $tr = $tf.TextRange
  $tr.Text = $t
  $tr.Font.Size = $sz; $tr.Font.Name = $FONT; $tr.Font.NameFarEast = $FONT
  $tr.Font.Color.RGB = $color
  if($bold -ne 0){ $tr.Font.Bold = $bold }
  $tr.ParagraphFormat.Alignment = $align
}

function ArrowR($s,[int]$x,[int]$y,[int]$color,[int]$w = 20,[int]$h = 16){
  $a = $s.Shapes.AddShape(33, $x, $y, $w, $h)
  $a.Fill.ForeColor.RGB = $color; $a.Line.Visible = 0
  $null = $a
}
function ArrowD($s,[int]$x,[int]$y,[int]$color,[int]$w = 20,[int]$h = 16){
  $a = $s.Shapes.AddShape(36, $x, $y, $w, $h)
  $a.Fill.ForeColor.RGB = $color; $a.Line.Visible = 0
  $null = $a
}

function New-Page([string]$title,[int]$pageNo){
  $s = $pres.Slides.Add($pres.Slides.Count + 1, 12)
  $null = Card $s 0 0 960 8 $DARK 1
  Txt $s $title 60 68 840 30 $DARK 1 1 44
  $null = Card $s 60 120 840 3 $LIGHT 1
  $null = Card $s 60 506 840 1 $LIGHT 1
  Txt $s "基于Unity的动作游戏与编辑器工具的设计与实现" 60 514 600 12 $GRAY 0 1 20
  Txt $s ([string]$pageNo) 860 514 40 12 $GRAY 0 3 20
  return $s
}

function Add-Notes($s,[string]$t){
  try { $s.NotesPage.Shapes.Placeholders(2).TextFrame.TextRange.Text = $t } catch { }
}

# ═══════════════ 封面 ═══════════════
$c = $pres.Slides.Add(1, 12)
$null = Card $c 0 0 960 8 $DARK 1
Txt $c "基于Unity的动作游戏与编辑器工具的设计与实现" 70 150 840 32 $DARK 1 1 48
Txt $c "毕业设计（论文）开题答辩" 70 212 840 18 $ACC 0 1 30
$null = Card $c 70 254 120 4 $ACC 1
Txt $c "姓　名：邬锦飞　　　学　号：259400232`r专　业：软件工程（专升本）　班　级：一班`r系　别：计算机系　　　指导教师：王欣`r2026 年 9 月" 70 320 700 16 $GRAY 0 1 120
Add-Notes $c "各位老师好，我是邬锦飞，课题是《基于Unity的动作游戏与编辑器工具的设计与实现》。下面按项目背景、具体内容、技术与难点三部分汇报。"

# ═══════════════ 1 项目背景（一句话） ═══════════════
$s1 = New-Page "项目背景" 1
$null = Card $s1 60 188 6 164 $ACC 1
Txt $s1 "做一个「数据驱动 + 可视化编辑器工具链」的第三人称动作战斗系统，" 92 194 800 22 $TEXT 0 1 36
Txt $s1 "面向独立开发者与小型团队，解决战斗内容生产依赖代码、手感难以量化、" 92 238 800 22 $TEXT 0 1 36
Txt $s1 "判定与表现不同步的问题。" 92 282 800 22 $TEXT 0 1 36
Add-Notes $s1 "一句话讲背景：动作游戏的战斗内容生产长期依赖代码，手感难以量化，判定与表现容易脱节。这个课题就是把战斗参数资产化，并用可视化编辑器工具链让内容生产脱离代码。"

# ═══════════════ 2 具体内容 · 总览 ═══════════════
$s2 = New-Page "具体内容 · 总览" 2
$info = @(
  @{x = 60;  l = '架构形态'; v = '单机客户端 · 无前后端'},
  @{x = 345; l = '使用者';   v = '2 类：开发者 · 玩家'},
  @{x = 630; l = '功能模块'; v = '6 个'}
)
foreach($i in $info){
  $null = Card $s2 $i.x 146 270 86 $PANEL
  Txt  $s2 $i.l ($i.x + 20) 164 230 13 $GRAY 0 1 24
  Txt  $s2 $i.v ($i.x + 20) 190 240 18 $DARK 1 1 34
}
$mods = @(
  @{x = 60;  y = 272; t = '玩家控制'},
  @{x = 345; y = 272; t = '战斗系统'},
  @{x = 630; y = 272; t = '敌人 AI'},
  @{x = 60;  y = 372; t = '技能系统'},
  @{x = 345; y = 372; t = '数据层'},
  @{x = 630; y = 372; t = '编辑器工具链'}
)
foreach($m in $mods){
  $sh = Card $s2 $m.x $m.y 270 86 $LIGHT
  CardTxt $sh $m.t 20 $DARK 2 1
}
Add-Notes $s2 "内容总览：系统是单机客户端，没有前后端；使用者只有两类，开发者在编辑器里配置，玩家在游戏里操作；功能划分为六个模块，分别是玩家控制、战斗系统、敌人 AI、技能系统、数据层和编辑器工具链。"

# ═══════════════ 3 模块功能（一） ═══════════════
$s3 = New-Page "模块功能（一）：玩家 · 战斗 · 敌人" 3
$flow = @('输入映射','十态状态机','连招·弹反·格挡','判定窗口','顿帧·音效·数字')
$fx = @(60, 232, 404, 576, 748)
for($i = 0; $i -lt 5; $i++){
  $sh = Card $s3 $fx[$i] 176 148 76 $LIGHT
  CardTxt $sh $flow[$i] 16 $DARK 2 0
  if($i -lt 4){ ArrowR $s3 ($fx[$i] + 152) 206 $ACC }
}
$null = Card $s3 60 316 400 76 $PANEL
$null = Card $s3 60 316 6 76 $ACC 1
Txt $s3 '敌人 AI' 86 334 340 15 $GRAY 0 1 26
Txt $s3 '13 态状态机 · 韧性与受击' 86 360 340 16 $DARK 1 1 30
$null = Card $s3 500 316 396 76 $PANEL
$null = Card $s3 500 316 6 76 $ACC 1
Txt $s3 '数据层' 526 334 340 15 $GRAY 0 1 26
Txt $s3 '血量 · 韧性 · 无敌帧 · 事件' 526 360 350 16 $DARK 1 1 30
Txt $s3 '玩家与敌人共用同一套战斗结算与反馈层' 60 436 700 15 $GRAY 0 1 28
Add-Notes $s3 "第一部分是玩家与战斗：输入映射到十态状态机，战斗包含连招、弹反、格挡，判定由动画帧窗口驱动，命中后统一给顿帧、音效和伤害数字反馈。敌人 AI 是 13 态状态机，带韧性和受击表现；数据层负责血量、韧性、无敌帧和事件广播，玩家和敌人共用同一套结算与反馈。"

# ═══════════════ 4 模块功能（二） ═══════════════
$s4 = New-Page "模块功能（二）：技能系统 · 编辑器工具链" 4
Txt $s4 '技能 = ID + 组件' 60 152 340 20 $DARK 1 1 32
$comp = @('播放动画','判定窗口','伤害','命中反馈','无敌帧')
for($i = 0; $i -lt 5; $i++){
  $sh = Card $s4 60 (188 + $i * 52) 340 44 $LIGHT
  CardTxt $sh $comp[$i] 16 $DARK 1 0 20
}
Txt $s4 '工作闭环' 450 152 450 20 $DARK 1 1 32
$steps = @('编辑器配置（组件 · 参数 · 帧事件）','ScriptableObject 资产（唯一数据源）','运行时 SkillManager 按 ID 释放','游戏表现：动画 · 伤害 · 音效')
for($i = 0; $i -lt 4; $i++){
  $y = 188 + $i * 78
  $fill = if($i -eq 3){ $LIGHT } else { $PANEL }
  $sh = Card $s4 450 $y 450 54 $fill
  if($i -lt 3){ $null = Card $s4 450 $y 6 54 $ACC 1 }
  CardTxt $sh $steps[$i] 17 $DARK 1 0 26
  if($i -lt 3){ ArrowD $s4 665 ($y + 58) $ACC }
}
Txt $s4 '两个工具：技能编辑器 · 音效管理器 —— 只写资产，不改代码' 60 458 800 15 $GRAY 0 1 28
Add-Notes $s4 "技能系统的设计是技能 = ID + 组件列表，播放动画、判定窗口、伤害、命中反馈、无敌帧都是可装配的组件。工作闭环是：在编辑器里配置组件参数和帧事件，写进 ScriptableObject 资产，运行时 SkillManager 按 ID 释放并驱动动画、伤害和音效。工具一共两个：技能编辑器和音效管理器，它们只写资产，不改代码。"

# ═══════════════ 5 技术方案 ═══════════════
$s5 = New-Page "怎么做：技术方案" 5
$layers = @(
  @{y = 150; bg = $DARK;  n = '编辑器层'; t = 'EditorWindow · SerializedObject · ReorderableList'; tc = $LIGHT},
  @{y = 238; bg = $ACC;   n = '数据层';   t = 'ScriptableObject · [SerializeReference] 组件内嵌'; tc = $LIGHT},
  @{y = 326; bg = $MID;   n = '运行时层'; t = '状态机 · Animator + Root Motion · Rigidbody 判定'; tc = $WHITE}
)
foreach($l in $layers){
  $null = Card $s5 60 $l.y 840 70 $l.bg
  Txt  $s5 $l.n 88 ($l.y + 22) 200 20 $WHITE 1 1 32
  Txt  $s5 $l.t 400 ($l.y + 27) 480 15 $l.tc 0 3 28
}
ArrowD $s5 470 224 $ACC
ArrowD $s5 470 312 $ACC
$tech = @('Unity 2022.3','C#','URP','Animator','Rigidbody','Editor 扩展')
for($i = 0; $i -lt 6; $i++){
  $sh = Card $s5 (63 + $i * 140) 424 134 30 $PANEL 5
  CardTxt $sh $tech[$i] 13 $DARK 2 0
}
Add-Notes $s5 "技术方案分三层：编辑器层基于 UnityEditor 的 EditorWindow、SerializedObject 和 ReorderableList 做可视化配置；数据层用 ScriptableObject 存资产，组件用 [SerializeReference] 多态内嵌；运行时层用枚举状态机组织行为，Animator 配合 Root Motion 驱动位移，Rigidbody 和物理查询做命中判定。层与层之间只通过资产文件通信。"

# ═══════════════ 6 难点与对策 ═══════════════
$s6 = New-Page "难点与对策" 6
$rows = @(
  @{y = 160; a = '判定与表现不同步'; b = '帧级判定窗口：判定@帧 · 音效@帧'},
  @{y = 256; a = '打断后状态残留';   b = '四态生命周期 + 打断统一清理'},
  @{y = 352; a = '手感靠感觉调';     b = '参数数据化：时长 · 窗口 · 无敌帧进资产'}
)
foreach($r in $rows){
  $shA = Card $s6 60 $r.y 300 76 $REDBG
  $null = Card $s6 60 $r.y 6 76 $RED 1
  CardTxt $shA $r.a 17 $RED 1 0 24
  ArrowR $s6 372 ($r.y + 30) $RED 24
  $shB = Card $s6 410 $r.y 490 76 $GRNBG
  $null = Card $s6 410 $r.y 6 76 $GRN 1
  CardTxt $shB $r.b 17 $GRN 1 0 24
}
Txt $s6 '三项难点已定位：①③ 已实现，② 修复中' 60 458 700 15 $GRAY 0 1 28
Add-Notes $s6 "三个难点：第一，判定与表现不同步，用帧级判定窗口解决，判定和音效都挂在动画帧上；第二，动作或技能被打断后残留判定区和无敌帧，用四态生命周期和统一的打断清理解决；第三，手感过去靠感觉调，现在把时长、窗口、无敌帧这些参数全部数据化，在编辑器里量化调参。目前第一、三项已实现，第二项在修。"

$cnt = $pres.Slides.Count
$pres.SaveAs($outPath, 24)
$pres.Close()
$ppt.Quit()
Start-Sleep -Seconds 2
"生成完成: " + (Test-Path $outPath) + "  页数: " + $cnt + "  大小: " + [math]::Round((Get-Item $outPath).Length / 1KB, 1) + " KB"
