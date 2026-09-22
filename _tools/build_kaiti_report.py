# -*- coding: utf-8 -*-
"""生成《毕业设计（论文）开题报告》提交稿（技能编辑器主线版）。

排版按学校要求：A4、页边距 左3cm/右2.5cm/上下2.5cm、正文小四宋体、
行距固定值 20 磅。生成后由 Word 导出 PDF，再用 poppler 渲染成 PNG 做视觉检查。
"""

import os
import sys

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor
from PIL import Image, ImageDraw, ImageFont

PROJECT = r"E:\Unity\My project"
OUT_DOCX = os.path.join(PROJECT, "开题报告_邬锦飞.docx")
OUT_FIG = os.path.join(PROJECT, "图2-1_技能编辑器总体结构.png")

CN_FONT = "宋体"
EN_FONT = "Times New Roman"


# ---------------------------------------------------------------- 基础工具

def set_font(run, size=12.0, bold=False, cn=CN_FONT, en=EN_FONT):
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.color.rgb = RGBColor(0, 0, 0)
    rpr = run._element.get_or_add_rPr()
    rfonts = rpr.get_or_add_rFonts()
    rfonts.set(qn("w:eastAsia"), cn)
    rfonts.set(qn("w:ascii"), en)
    rfonts.set(qn("w:hAnsi"), en)


def fmt_para(p, first_indent_chars=2.0, space_before=0, space_after=0,
             align=WD_ALIGN_PARAGRAPH.JUSTIFY, line_pt=20.0):
    pf = p.paragraph_format
    pf.alignment = align
    pf.line_spacing_rule = WD_LINE_SPACING.EXACTLY
    pf.line_spacing = Pt(line_pt)
    pf.space_before = Pt(space_before)
    pf.space_after = Pt(space_after)
    if first_indent_chars:
        pf.first_line_indent = Pt(12.0 * first_indent_chars)
    else:
        pf.first_line_indent = Pt(0)
    return p


def add_body(doc, text, size=12.0, indent=2.0, bold=False, space_after=0):
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=indent, space_after=space_after)
    set_font(p.add_run(text), size=size, bold=bold)
    return p


def add_heading1(doc, text):
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, space_before=12, space_after=6,
             align=WD_ALIGN_PARAGRAPH.LEFT)
    p.paragraph_format.keep_with_next = True
    set_font(p.add_run(text), size=14.0, bold=True)
    return p


def add_heading2(doc, text):
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, space_before=8, space_after=4,
             align=WD_ALIGN_PARAGRAPH.LEFT)
    p.paragraph_format.keep_with_next = True
    set_font(p.add_run(text), size=12.0, bold=True)
    return p


def add_heading3(doc, text):
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, space_before=6, space_after=2,
             align=WD_ALIGN_PARAGRAPH.LEFT)
    p.paragraph_format.keep_with_next = True
    set_font(p.add_run(text), size=12.0, bold=True)
    return p


def add_caption(doc, text):
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, space_before=2, space_after=8,
             align=WD_ALIGN_PARAGRAPH.CENTER)
    set_font(p.add_run(text), size=10.5)
    return p


def shade(cell, hex_color):
    tcpr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), hex_color)
    tcpr.append(shd)


def table_borders(table, color="D9D9D9", sz="6"):
    tbl = table._tbl
    tblpr = tbl.tblPr
    borders = OxmlElement("w:tblBorders")
    for edge in ("top", "left", "bottom", "right", "insideH", "insideV"):
        el = OxmlElement("w:" + edge)
        el.set(qn("w:val"), "single")
        el.set(qn("w:sz"), sz)
        el.set(qn("w:space"), "0")
        el.set(qn("w:color"), color)
        borders.append(el)
    tblpr.append(borders)


def cell_text(cell, text, size=10.5, bold=False,
              align=WD_ALIGN_PARAGRAPH.LEFT, line_pt=16.0):
    cell.text = ""
    p = cell.paragraphs[0]
    fmt_para(p, first_indent_chars=0, align=align, line_pt=line_pt,
             space_before=2, space_after=2)
    set_font(p.add_run(text), size=size, bold=bold)


def bottom_border(cell, color="000000", sz="6"):
    tcpr = cell._tc.get_or_add_tcPr()
    borders = OxmlElement("w:tcBorders")
    bottom = OxmlElement("w:bottom")
    bottom.set(qn("w:val"), "single")
    bottom.set(qn("w:sz"), sz)
    bottom.set(qn("w:space"), "0")
    bottom.set(qn("w:color"), color)
    borders.append(bottom)
    tcpr.append(borders)


# ---------------------------------------------------------------- 结构图

def pick_font(size):
    for path in (r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc",
                 r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def build_figure(path):
    W, H = 1800, 1180
    img = Image.new("RGB", (W, H), "white")
    d = ImageDraw.Draw(img)

    f_layer = pick_font(34)
    f_box = pick_font(30)
    f_small = pick_font(25)
    f_note = pick_font(24)

    gray = (110, 110, 110)
    line = (60, 60, 60)

    def layer(x0, y0, x1, y1, title):
        d.rectangle([x0, y0, x1, y1], outline=line, width=3)
        d.rectangle([x0, y0, x1, y0 + 56], outline=line, width=3)
        tw = d.textlength(title, font=f_layer)
        d.text((x0 + 24, y0 + 10), title, font=f_layer, fill=(0, 0, 0))
        return y0 + 56

    def box(x0, y0, x1, y1, text, font=None):
        font = font or f_box
        d.rectangle([x0, y0, x1, y1], outline=gray, width=2)
        lines = text.split("\n")
        total = len(lines) * (font.size + 8)
        cy = y0 + ((y1 - y0) - total) / 2 + 2
        for ln in lines:
            w = d.textlength(ln, font=font)
            d.text((x0 + ((x1 - x0) - w) / 2, cy), ln, font=font, fill=(0, 0, 0))
            cy += font.size + 8

    def arrow_down(x, y0, y1, label):
        d.line([x, y0, x, y1], fill=line, width=3)
        d.polygon([(x - 12, y1 - 16), (x + 12, y1 - 16), (x, y1)], fill=line)
        w = d.textlength(label, font=f_note)
        d.text((x + 20, (y0 + y1) / 2 - 16), label, font=f_note, fill=(0, 0, 0))

    # 编辑器层
    top = layer(60, 40, 1740, 400, "编辑器层（Unity 编辑器扩展，仅编辑器运行）")
    box(100, top + 22, 700, 390,
        "技能编辑器（EditorWindow）\n技能库 / 技能 / 组件 列表\n组件参数与命名")
    box(730, top + 22, 1180, 390, "帧级时序配置\n帧事件表格\n判定窗口 / 无敌帧区间")
    box(1210, top + 22, 1700, 390, "可视化与校验\n动画时间轴预览 / 音效试听\n配置正确性校验")

    # 数据层
    mid_top = 470
    mid = layer(60, mid_top, 1740, mid_top + 250, "数据层（ScriptableObject 资产：编辑器与运行时的唯一接口）")
    box(100, mid + 22, 620, mid_top + 240, "SkillLibrary\n技能索引表")
    box(650, mid + 22, 1150, mid_top + 240,
        "SkillConfig\n技能 = 标识 + 组件列表\n组件多态内嵌序列化")
    box(1180, mid + 22, 1700, mid_top + 240, "SoundLibrary\n音效标识与音频映射")

    # 运行时层
    low_top = 830
    low = layer(60, low_top, 1740, 1140, "运行时层（动作游戏原型：用于验证工具，不作为研究对象）")
    box(100, low + 22, 620, 1130, "技能管理器\n组件生命周期\n（启动/逐帧/结束/打断）")
    box(650, low + 22, 1150, 1130, "动画与 Root Motion\n判定窗口检测\n命中反馈与顿帧")
    box(1180, low + 22, 1700, 1130, "音效管理\n界面与结算\n（验证配置是否生效）")

    arrow_down(900, 402, 466, "① 编辑器写资产")
    arrow_down(900, 722, 828, "② 运行时读资产")

    img.save(path, dpi=(200, 200))
    return path


# ---------------------------------------------------------------- 正文

COVER_FIELDS = [
    ("题    目", "基于Unity的动作游戏与编辑器工具的设计与实现"),
    ("系", "计算机系"),
    ("专    业", "软件工程"),
    ("姓    名", "邬锦飞"),
    ("班    级", "软件工程一班"),
    ("学    号", "259400232"),
    ("指导教师", "王欣"),
]

S1_1 = [
    "国内游戏公司在动作类、ARPG类项目的研发中，已普遍采用“有限状态机 + 数据驱动 + 自研编辑器工具链”的生产方式。腾讯、网易、米哈游等企业在项目实践中沉淀了命中反馈分层、动作取消窗口、技能组件化装配、连招配置表、动画事件中继等工程经验，战斗内容的生产逐步从代码硬编码转向策划可视化配置，工具链的成熟度已成为影响内容产能的关键因素之一。",
    "在学术与毕业设计层面，国内相关研究多集中于角色动画控制、有限状态机、行为树AI、Unity组件开发等单点技术；以“使用Unity实现一款××游戏”为题的毕业设计数量众多，其重心通常在玩法实现与功能完整度上，编辑器工具多以附属功能的形式出现，缺少对工具自身设计方法（数据模型、配置粒度、配置正确性、可扩展性）的讨论。针对动作游戏中帧级战斗时序——即判定窗口、帧事件音效、无敌帧区间——的可视化配置与配置一致性校验，公开的、可复现的设计方案较少。",
    "从工程实践看，大量学生项目与小型团队仍采用在Update中堆叠条件判断、以角色中心大范围球形检测代替武器级判定的做法：配置参数分散在代码与多个Inspector面板中，参数之间的约束关系（帧号与动画时长、状态名与动画机、音效标识与运行时音效库）没有任何检查手段，错误往往只能在运行时以“功能静默失效”的形式暴露。因此，面向动作游戏战斗内容的可视化配置工具具有明确的实践价值。",
]

S1_2 = [
    "国外在引擎生态与工具化方面起步更早。Unity官方提供了较完整的编辑器扩展能力（EditorWindow、CustomEditor、SerializedObject、ReorderableList、AssetDatabase，以及Handles与Scene视图回调等），使团队内部工具可以直接嵌入编辑器工作流；商业插件中已出现数据驱动的角色与技能系统，从工程实践上验证了“配置化 + 资产化”路线的可行性，但其设计目标是覆盖通用需求，配置项粒度较粗、概念层次较多，对特定品类的战斗时序配置并不直接适用。",
    "在方法与理论层面，Robert Nystrom在《Game Programming Patterns》中系统论述了组件模式、状态模式、观察者模式在游戏开发中的应用，为“以组件装配代替继承扩展”的数据模型提供了依据；Jason Gregory在《Game Engine Architecture》中对角色动画系统、混合树、Root Motion与连续碰撞检测作了完整阐述，是判定与动画时序设计的重要参考。",
    "在产品实践层面，动作游戏的手感被普遍拆解为可度量的要素：命中判定的空间精度（判定体积与武器运动轨迹的匹配）、时间精度（判定起止帧与动画的同步）、反馈强度（顿帧、受击硬直、音效分层与镜头反馈）。FromSoftware系列作品通过韧性、弹反与硬直帧构建战斗节奏；Capcom在《鬼泣》系列中引入连招评级与动作取消机制；Santa Monica Studio在《战神》中采用武器骨骼级命中检测。这些实践说明，判定精度与反馈强度是动作手感的两大支柱，而两者的实现都依赖一套可精细调整、且调整结果可被验证的内容配置方式。",
    "综合来看，国外的研究与实践提供了成熟的配置化范式，但其对象多为通用能力；面向动作游戏帧级时序的配置工具应当如何组织数据、如何在编辑期保证配置的合法性，仍缺少面向中小规模项目的可复现设计与验证方案。这正是本课题的切入点。",
]

S1_3 = [
    "本课题的研究对象是Unity编辑器扩展工具，具体为面向动作游戏的技能配置工具（技能编辑器）；同时实现一个最小可运行的第三人称动作游戏原型，作为验证该工具可用性的验证平台。游戏玩法本身不是本课题的研究内容，其在课题中提供的是真实的配置对象与验证场景。",
    "选题依据来自这一工作场景的具体矛盾：技能与攻击行为的内容生产高度依赖参数调校，而这些参数具有时序性强、相互约束多、出错不报错的特点。以代码硬编码或裸Inspector编辑的方式组织这类数据存在三个问题：修改成本高，任何调参都要经过代码修改与运行验证；协作门槛高，非程序人员难以参与；约束关系无法表达，配置错误在运行期才暴露且多为静默失效，定位成本高。",
    "本课题的意义体现在三个层面。工程层面，把主观的战斗手感拆解为可度量、可配置的参数（判定起止帧、伤害数值、顿帧时长、音效触发帧等），并使编辑器成为这些参数的唯一入口；效率层面，通过“编辑器修改→资产→运行时生效”的链路，使战斗内容的生产脱离代码修改；方法层面，把配置合法性的检查前移到编辑期，用工具手段控制数据驱动架构固有的“错误静默化”风险。",
    "本课题的重点与特色在于：针对动作游戏中帧级战斗时序这一类内容，把配置合法性的检查从运行期前移到编辑期，用可校验、可复现的工具机制降低数据驱动架构固有的静默错误风险；同时以动画帧作为判定、音效与无敌帧的统一时间基准，保证表现与判定一致。",
]

S2_1_INTRO = [
    "本课题的目标是：以Unity为平台，设计并实现一个面向动作游戏的技能配置工具（技能编辑器），使技能内容可以完全通过可视化配置生成并在运行时生效；同时实现一个最小动作游戏原型，用于验证工具配置结果的正确性与可用性。",
    "具体内容包括：技能以“标识 + 组件列表”的方式建模，组件类型包含播放动画、判定窗口、伤害、命中反馈、无敌帧；技能中的时间量统一以动画帧为基准，支持帧事件（在指定帧触发音效）、判定窗口（在帧区间内进行检测）与无敌帧区间；工具提供编辑期静态校验，检查引用完整性、帧号越界、动画状态名与动画机的一致性、音效标识在运行时音效库中的可用性、标识唯一性与组件顺序约束；工具提供动画预览与编辑器内音效试听，减少“改一次、跑一次”的往返。",
]

S2_1_MODULES = [
    "（1）技能库与技能管理模块。以ScriptableObject资产承载技能库与技能配置，提供技能的新增、选择、重命名与移除，并保证编辑器当前编辑的库与运行时实际加载的库可核对，避免“编辑了另一个库”的情况。",
    "（2）组件化技能数据模型模块。技能由若干可配置组件装配而成，组件以多态内嵌方式（基于 Unity 的 SerializeReference）序列化在技能资产内部，不必为每个组件创建独立资产文件；扩展技能能力时，以“新增组件实现 + 界面选择”的方式完成，不需要修改运行时调度代码。",
    "（3）帧级时序配置模块。提供帧事件表格（帧号、音效标识、音量、试听）、判定窗口（起始帧、结束帧、检测形状与体积、绑定节点）与无敌帧区间等配置项，使动画、判定与音效共享同一时间基准。",
    "（4）动画映射模块。技能通过动画机状态名播放动画，并支持为状态指定替换动画：编辑器解析状态原本引用的动画并写入资产，运行时据此建立动画覆盖，使更换攻击动画不必修改动画机资源。",
    "（5）配置正确性校验模块。在编辑器内对技能配置执行静态检查，把运行时静默失效类问题（帧号超出可达范围、状态名不存在、音效标识不在运行时音效库、标识重复、组件顺序错误）转换为编辑期可见的错误提示。",
    "（6）可视化预览与试听模块。在编辑器内采样动画并显示当前帧、总帧数与帧率，支持播放、暂停、逐帧步进与循环；播放头跨过帧事件时试听对应音效；在Scene视图中绘制判定体积线框并提供拖拽手柄，用于调整体积参数与朝向。",
    "（7）运行时消费模块。运行时技能管理器按标识取出技能配置，按组件列表顺序驱动组件生命周期（启动、逐帧、结束、打断），并保证技能被打断时判定窗口与无敌帧状态被清理，不残留到后续行为。",
    "（8）验证平台。最小第三人称动作游戏原型，包含角色操控、攻击与受击流程、敌人状态机与胜负结算，仅用于验证工具配置结果在真实运行环境中的表现。",
]

S2_1_RESULT = [
    "（1）技能编辑器一个，包含技能库管理、组件化配置、帧级时序配置、动画映射、配置校验、可视化预览与试听；",
    "（2）用于验证工具可用性的动作游戏原型一套；",
    "（3）配置正确性校验的错误检出实验数据与技能时序一致性的测量数据；",
    "（4）数据层与技能生命周期的EditMode单元测试（在 Unity 编辑环境中运行的自动化测试，不进入游戏构建包）；",
    "（5）毕业设计论文一篇，以及系统演示视频与答辩材料。",
]

S2_2 = [
    ("功能结构设计",
     [
         "系统按“编辑器层—数据层—运行时层”三层结构组织：编辑器层负责全部可配置内容的编辑与校验；数据层以ScriptableObject资产承载配置，是编辑器与运行时之间唯一的接口；运行时层按资产驱动技能执行。层与层之间不存在直接调用，因此修改配置不需要重新编译运行时代码。",
         "技能配置采用组件化数据模型，即技能 = 标识 + 组件列表。组件为可序列化对象，以多态内嵌（SerializeReference）方式存储在技能资产内部，扩展技能能力时只需新增组件实现并在界面上装配，无需改动运行时调度逻辑。由此带来两个具体问题：一是多态内嵌对象的绘制与增删，编辑器需要按组件的实际类型绘制字段，并处理列表元素删除后残留的空引用；二是资产与场景的生命周期隔离——资产不能引用场景对象，因此判定体积的绑定节点以骨骼名或相对路径记录，运行时再解析，从而实现资产文件与场景资源的解耦。",
         "技能的时序结构以动画帧为统一时间基准：帧事件以帧号记录，判定窗口与无敌帧以帧区间表达，帧率取自动画片段自身的帧率。这样处理的原因在于，动作游戏中判定、动画与音效必须落在同一时间基准上，否则会出现“看到刀挥过却没有打中”或“打中了却没有音效”的现象。需要进一步解决帧号与技能时长的约束关系、动画被替换或播放速度调整后的可达帧换算，以及编辑器预览触发位置与运行时触发位置的一致性验证。",
     ]),
    ("框架搭建",
     [
         "编辑器代码不应进入运行时构建，资产修改也应避免直接改写字段造成序列化丢失。拟采取的措施包括：以程序集定义文件隔离运行时脚本与编辑器脚本；资产写入统一经由序列化接口完成并标记为已修改；为关键编辑操作提供撤销支持；编辑状态通过会话状态保存，避免脚本重编译后丢失。",
         "工具界面基于EditorWindow与IMGUI搭建，使用可重排列表管理技能与组件，通过SerializedObject与SerializedProperty完成属性绑定与写回；调参过程辅以可视化支持，包括Scene视图中的判定体积线框与拖拽手柄、动画时间轴与帧号显示、编辑器内音效试听。",
     ]),
    ("数据库设计（数据资产层）",
     [
         "本课题为单机游戏客户端，不使用传统的关系数据库，本小节描述的是系统的数据资产层设计：全部可配置内容以 Unity 的 ScriptableObject 资产承载，运行时只读，编辑器统一经序列化接口写入，避免直接修改字段造成序列化丢失。主要数据资产及其字段如表1所示。",
     ]),
    ("安全性",
     [
         "单机游戏的安全性主要体现在工程安全、数据正确性与运行健壮性三个方面。工程安全方面，资产修改统一经由序列化接口完成，编辑器侧音效试听与动画预览均使用临时对象，不写入资产。",
         "数据正确性方面，本课题拟在编辑期建立一组校验规则，并保证规则与运行时语义一致，包括引用完整性、时序合法性（帧号是否落在技能可达范围内）、外部一致性（动画状态名是否存在于目标动画机、音效标识是否存在于运行时实际加载的音效库）、标识唯一性与结构约束（组件顺序对执行结果的影响）。这一部分是本课题的重点：数据驱动架构把错误从编译期推迟到运行期，且多数配置错误不会抛出异常，只表现为功能不生效，定位成本高；把校验前移到编辑期，可以直接降低这类风险。",
         "运行健壮性方面，技能被打断时统一执行清理，避免判定窗口与无敌帧状态残留到后续行为；顿帧并发触发时按统一策略恢复时间缩放；运行时输出经日志门控控制，便于定位问题。",
     ]),
    ("功能性测试",
     [
         "测试分两层。EditMode单元测试覆盖数据层与技能生命周期，包括技能查找、组件生命周期顺序、打断清理与帧跨越边界；命令行编译门禁保证提交的代码可编译。功能验证包含两类：一是配置正确性验证，通过注入已知类型的错误配置，统计工具的检出率与平均定位时间，并与无校验的人工排查作对照；二是可用性验证，在游戏原型中验证“编辑器修改后运行时即时生效”，并测量编辑器预览触发帧与运行时实际触发帧的偏差。",
     ]),
    ("其他关键技术问题",
     [
         "顿帧（时间缩放）并发触发时的恢复策略；高频对象（音效播放、伤害数字）的对象池复用与状态重置；编辑器侧音效试听与运行时音效库不一致造成的“编辑器能听、游戏无声”问题；动画事件与业务逻辑的解耦；编辑器长时间运行下的性能与状态一致性。",
     ]),
]

DATA_ASSET_ROWS = [
    ("SkillLibrary 技能库", "技能标识索引表", "运行时按标识查找技能配置的入口"),
    ("SkillConfig 技能配置", "技能标识、显示名、组件列表", "组件以多态内嵌方式序列化在该资产内部"),
    ("组件（内嵌）", "播放动画、判定窗口、伤害、命中反馈、无敌帧等字段", "技能行为的最小配置单元，决定技能的时间与判定行为"),
    ("SoundLibrary 音效库", "音效标识、音频片段", "供运行时按标识查表播放，并作为编辑器侧音效校验的数据源"),
]

S3_1 = [
    "（1）需求分析与资料收集：查阅游戏编程、引擎架构与编辑器扩展相关资料，明确工具边界与配置对象范围。本阶段已完成。",
    "（2）验证平台最小闭环：实现角色控制、攻击与受击流程、敌人状态机与胜负结算，为工具提供真实的配置对象与验证场景。本阶段已完成。",
    "（3）技能系统与数据层：定义技能数据模型（技能库、技能配置、组件），实现运行时技能管理与组件生命周期。本阶段已完成。",
    "（4）技能编辑器主体：实现技能列表与组件配置、帧事件表格、动画预览与编辑器内试听。本阶段主体功能已完成，正在持续完善。",
    "（5）配置正确性校验：实现并扩充编辑期校验规则，配套错误注入实验与回归测试。本阶段正在进行。",
    "（6）可用性完善与验证：判定体积可视化、编辑状态持久化、撤销支持、运行时即时生效验证与时序一致性测量。本阶段正在进行。",
    "（7）论文撰写与答辩准备：整理设计过程、关键技术、实验数据与演示材料。本阶段计划进行。",
]

S3_2 = [
    "（1）文献与资料研究法。以引擎官方文档、游戏编程与引擎架构专著，以及工具化开发与数据驱动开发相关资料为依据，确定设计取舍并在报告中说明比较结果。",
    "（2）原型迭代法。以最小可用闭环为目标，逐步扩展组件类型与配置能力，每一步都在运行环境中验证，避免设计停留在纸面。",
    "（3）对照实验法。对配置校验与判定方案分别设置对照组：配置校验对照无校验的人工排查，判定方案对照以角色为中心的球形检测，以检出率、定位时间与命中一致性等指标进行比较。",
    "（4）单元测试法。对数据层与生命周期逻辑编写自动化测试，防止后续修改引入回归缺陷。",
]

S3_3 = [
    "系统的数据流为：编辑器（配置技能与组件，写入资产）→ 数据层（技能库、技能配置、音效库等ScriptableObject资产）→ 运行时（技能管理器读取资产，按组件顺序驱动，并触发判定、动画、音效与界面反馈）。编辑器层与运行时层之间不存在直接调用，二者唯一的接口是资产本身，因此配置修改不需要重新编译运行时代码。",
    "主要措施包括：使用Git进行版本管理，按功能提交；使用命令行编译检查与单元测试作为提交前的门禁；对关键配置项建立校验规则，把可检出的错误集中在编辑器内提示；对高频创建销毁的对象使用对象池；使用日志门控控制运行时输出，保持控制台信息可读。",
]

S4_HEAD = ["序号", "时间", "内容"]
S4_ROWS = [
    ("1", "第1-2周", "调研动作游戏战斗系统与Unity编辑器扩展接口，完成开题报告与开题答辩（已完成）"),
    ("2", "第3-6周", "游戏核心开发：角色控制、敌人AI、战斗系统与音效系统，形成可运行的最小闭环（已完成）"),
    ("3", "第7-8周", "技能数据模型与运行时技能管理：技能库、技能配置与组件化内嵌序列化（已完成）"),
    ("4", "第9-10周", "技能编辑器主体：技能与组件配置、帧事件表格、动画预览与编辑器内试听（已完成）"),
    ("5", "第11-12周", "配置正确性校验规则、判定体积可视化、运行时即时生效与时序一致性验证（进行中）"),
    ("6", "第13-15周", "毕业论文初稿与修改，整理实验数据与测试结果（计划中）"),
    ("7", "第16周", "制作演示视频与答辩材料，完成毕业答辩（计划中）"),
]

S5_REFS = [
    "[1] 胡凯宁. 基于Unity3D的像素休闲游戏的设计与实现[J]. 电脑编程技巧与维护, 2026(4): 147-149.",
    "[2] 巩云飞. 基于Unity3D的游戏设计与实现[J]. 现代信息科技, 2025, 9(8): 89-92.",
    "[3] 路宜松. 基于Unity引擎的2D角色扮演游戏的设计与实现[D]. 沈阳: 沈阳理工大学, 2021.",
    "[4] 沈士钊. 基于Unity3D引擎的三维角色扮演游戏设计与实现[D]. 武汉: 华中科技大学, 2017.",
    "[5] 邓珊珊, 罗小婷, 莫喜喜, 等. 基于Unity3D的古镇秘探游戏的设计与实现[J]. 电脑知识与技术, 2025, 21(9): 54-56.",
    "[6] 马晓萍, 轩莹莹. 基于Unity 3D的电子游戏设计与实现[J]. 电子技术, 2024, 53(6): 75-79.",
    "[7] 王巍, 高德远. 有限状态机设计策略[J]. 计算机工程与应用, 1999(7): 54-55.",
    "[8] NYSTROM R. Game programming patterns[M]. Genever Benning, 2014.",
    "[9] GREGORY J. Game engine architecture[M]. 3rd ed. Boca Raton: CRC Press, 2018.",
    "[10] Unity Technologies. Unity user manual: editor scripting, ScriptableObject, SerializeReference[EB/OL]. [2026-09-19]. https://docs.unity3d.com/.",
]


def build():
    build_figure(OUT_FIG)

    doc = Document()
    sec = doc.sections[0]
    sec.page_width = Cm(21.0)
    sec.page_height = Cm(29.7)
    sec.left_margin = Cm(3.0)
    sec.right_margin = Cm(2.5)
    sec.top_margin = Cm(2.5)
    sec.bottom_margin = Cm(2.5)

    normal = doc.styles["Normal"]
    normal.font.size = Pt(12)
    normal.font.name = EN_FONT
    normal.element.rPr.rFonts.set(qn("w:eastAsia"), CN_FONT)

    # ---- 封面
    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, align=WD_ALIGN_PARAGRAPH.CENTER,
             line_pt=28, space_after=8)
    set_font(p.add_run("杭州电子科技大学信息工程学院"), size=25, bold=True)

    p = doc.add_paragraph()
    fmt_para(p, first_indent_chars=0, align=WD_ALIGN_PARAGRAPH.CENTER,
             line_pt=32, space_after=36)
    set_font(p.add_run("毕业设计（论文）开题报告"), size=22, bold=True)

    tbl = doc.add_table(rows=len(COVER_FIELDS), cols=2)
    tbl.alignment = WD_TABLE_ALIGNMENT.CENTER
    for i, (label, value) in enumerate(COVER_FIELDS):
        c0, c1 = tbl.rows[i].cells
        c0.width = Cm(3.4)
        c1.width = Cm(11.6)
        cell_text(c0, label, size=12, bold=True, align=WD_ALIGN_PARAGRAPH.RIGHT, line_pt=22)
        cell_text(c1, value, size=12, align=WD_ALIGN_PARAGRAPH.CENTER, line_pt=22)
        bottom_border(c1)
    for r in tbl.rows:
        r.height = Cm(1.1)

    doc.add_paragraph()

    # ---- 一
    add_heading1(doc, "一、综述本课题国内外研究动态，说明选题的依据和意义")
    add_heading2(doc, "1.1 国内研究动态")
    for t in S1_1:
        add_body(doc, t)
    add_heading2(doc, "1.2 国外研究动态")
    for t in S1_2:
        add_body(doc, t)
    add_heading2(doc, "1.3 选题依据与意义")
    for t in S1_3:
        add_body(doc, t)

    # ---- 二
    add_heading1(doc, "二、研究的基本内容，拟解决的主要问题")
    add_heading2(doc, "2.1 基本内容")
    for t in S2_1_INTRO:
        add_body(doc, t)
    add_heading3(doc, "2.1.1 功能模块图")
    pic = doc.add_paragraph()
    pic.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER
    pic.paragraph_format.first_line_indent = Pt(0)
    pic.paragraph_format.space_before = Pt(4)
    pic.paragraph_format.space_after = Pt(2)
    pic.paragraph_format.line_spacing_rule = WD_LINE_SPACING.SINGLE
    pic.paragraph_format.keep_with_next = True
    pic.paragraph_format.keep_together = True
    pic.add_run().add_picture(OUT_FIG, width=Cm(14.0))
    add_caption(doc, "图2-1 技能编辑器总体结构图")
    add_heading3(doc, "2.1.2 模块介绍")
    for t in S2_1_MODULES:
        add_body(doc, t)
    add_heading3(doc, "2.1.3 预期成果")
    for t in S2_1_RESULT:
        add_body(doc, t, indent=0)

    add_heading2(doc, "2.2 拟解决的主要问题")
    for idx, (title, paras) in enumerate(S2_2, start=1):
        add_heading3(doc, "2.2." + str(idx) + " " + title)
        for t in paras:
            add_body(doc, t)
        if title.startswith("数据库设计"):
            cap = doc.add_paragraph()
            fmt_para(cap, first_indent_chars=0, space_before=6, space_after=2,
                     align=WD_ALIGN_PARAGRAPH.CENTER)
            cap.paragraph_format.keep_with_next = True
            set_font(cap.add_run("表1 主要数据资产及其字段"), size=10.5)
            tda = doc.add_table(rows=len(DATA_ASSET_ROWS) + 1, cols=3)
            tda.alignment = WD_TABLE_ALIGNMENT.CENTER
            table_borders(tda)
            wda = [Cm(3.8), Cm(5.5), Cm(6.1)]
            for j, h in enumerate(["数据资产", "主要字段", "说明"]):
                cell = tda.rows[0].cells[j]
                cell.width = wda[j]
                cell_text(cell, h, size=10.5, bold=True, align=WD_ALIGN_PARAGRAPH.CENTER)
                shade(cell, "E8EDF3")
            for i, row in enumerate(DATA_ASSET_ROWS, start=1):
                for j, val in enumerate(row):
                    cell = tda.rows[i].cells[j]
                    cell.width = wda[j]
                    cell_text(cell, val, size=10.5)
            doc.add_paragraph()

    # ---- 三
    add_heading1(doc, "三、研究步骤、方法及措施")
    add_heading2(doc, "3.1 研究步骤")
    for t in S3_1:
        add_body(doc, t, indent=0)
    add_body(doc,
             "说明：以上进度状态按实际完成情况标注。本课题在开题阶段已完成验证平台原型与技能编辑器主体，开题后的工作重点为配置正确性校验、测试与论文撰写，与第四节进度表的标注一致。")
    add_heading2(doc, "3.2 研究方法")
    for t in S3_2:
        add_body(doc, t, indent=0)
    add_heading2(doc, "3.3 技术路线与措施")
    for t in S3_3:
        add_body(doc, t)

    # ---- 四
    add_heading1(doc, "四、研究工作进度")
    t4 = doc.add_table(rows=len(S4_ROWS) + 1, cols=3)
    t4.alignment = WD_TABLE_ALIGNMENT.CENTER
    table_borders(t4)
    widths = [Cm(1.4), Cm(2.8), Cm(11.2)]
    for j, h in enumerate(S4_HEAD):
        cell = t4.rows[0].cells[j]
        cell.width = widths[j]
        cell_text(cell, h, size=10.5, bold=True, align=WD_ALIGN_PARAGRAPH.CENTER)
        shade(cell, "E8EDF3")
    for i, row in enumerate(S4_ROWS, start=1):
        for j, val in enumerate(row):
            cell = t4.rows[i].cells[j]
            cell.width = widths[j]
            align = WD_ALIGN_PARAGRAPH.CENTER if j in (0, 1) else WD_ALIGN_PARAGRAPH.LEFT
            cell_text(cell, val, size=10.5, align=align)

    # ---- 五
    add_heading1(doc, "五、主要参考文献")
    for r in S5_REFS:
        p = doc.add_paragraph()
        fmt_para(p, first_indent_chars=0, align=WD_ALIGN_PARAGRAPH.LEFT,
                 line_pt=18, space_after=2)
        p.paragraph_format.left_indent = Pt(24)
        p.paragraph_format.first_line_indent = Pt(-24)
        set_font(p.add_run(r), size=10.5)

    doc.save(OUT_DOCX)
    print("saved:", OUT_DOCX)


if __name__ == "__main__":
    build()
