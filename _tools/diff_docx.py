# -*- coding: utf-8 -*-
"""比较两个 docx 的正文段落与表格文字，输出增删差异（供人工确认用）。"""

import sys
import difflib

import docx


def dump(path):
    d = docx.Document(path)
    lines = []
    for p in d.paragraphs:
        t = p.text.strip()
        if t:
            lines.append(t)
    for ti, tb in enumerate(d.tables):
        for row in tb.rows:
            cells = [c.text.strip().replace("\n", " ") for c in row.cells]
            lines.append("[表%d] " % (ti + 1) + " | ".join(cells))
    return lines


def main():
    a, b = sys.argv[1], sys.argv[2]
    la, lb = dump(a), dump(b)
    print("A(旧) 段落数=%d   B(新) 段落数=%d" % (len(la), len(lb)))
    diff = list(difflib.unified_diff(la, lb, lineterm="", n=0))
    if not diff:
        print("两份文件的正文与表格内容完全一致。")
        return
    for line in diff[:400]:
        print(line)


if __name__ == "__main__":
    main()
