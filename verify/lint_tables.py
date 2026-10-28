"""仕様書(../*.md)の表の列数を検査する。違反が 0 なら合格(終了コード 0)。使い方: python3 -I verify/lint_tables.py"""
import re, sys, glob, os
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
bad = 0
for p in sorted(glob.glob(os.path.join(ROOT, "*.md"))):
    lines = open(p, encoding="utf-8").read().splitlines()
    i = 0
    in_code = False
    while i < len(lines):
        l = lines[i]
        if l.strip().startswith("```"):
            in_code = not in_code
            i += 1
            continue
        if not in_code and l.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|\s*$", lines[i + 1]):
            def cells(x):
                x = x.strip()
                parts = re.split(r"(?<!\\)\|", x)
                return len(parts) - 2
            n = cells(l)
            j = i
            while j < len(lines) and lines[j].startswith("|"):
                if cells(lines[j]) != n:
                    print(f"{os.path.basename(p)}:{j + 1}: 列数 {cells(lines[j])} ≠ 見出し {n}: {lines[j][:80]}")
                    bad += 1
                j += 1
            i = j
        else:
            i += 1
print("表の列数の違反:", bad)
sys.exit(1 if bad else 0)
