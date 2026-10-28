"""T1 の主なベクタの照合(09 §16.2)。`simcli vectors` の出力(C#)と、参照モデル(Python)が作る同じ行を、行ごとに突き合わせる。

使い方:
  python3 -I tools/compare_vectors.py                 # dotnet で SimCli を走らせて照合する(m0 で実行)
  python3 -I tools/compare_vectors.py <出力のファイル>   # すでにある出力と照合する(実機・IL2CPP のビルドの出力など)
終了コード 0 = 全行一致。
"""
import math
import os
import struct
import subprocess
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "verify"))
import ref_model_03 as m3  # noqa: E402
import ref_model_05 as m5  # noqa: E402
import ref_model_07 as m7  # noqa: E402
import ref_model_09 as m9  # noqa: E402

Q6_INPUTS = [5e-07, 1e-07, 0.1 + 0.2, -0.0, 12.3456785, 1e-06, 0.0000015, 123456.7890125, -0.0000005, 0.9999995, 72.4999995, 0.123456789]
TIME_DS = [0, 9, 599, 600, 36000, 7561, 123456, 7577000 // 10]
DIFF_DDS = [-35, -1, 0, 1, 12, 600, 3601]


def f(x, d):
    return f"{x:.{d}f}"


def lines():
    out = []

    def L(k, v):
        out.append(f"{k} {v}")

    for s in ("123456789", "EKSV", ""):
        L(f"crc32[{s}]", f"{m9.crc32(s.encode('ascii')):08x}")
    for i, x in enumerate(Q6_INPUTS):
        L(f"q6[{i}]", m9.fmt6(m9.q6(x)))
    for ds in TIME_DS:
        L(f"fmt.time[{ds}]", m9.fmt_time(ds))
        L(f"fmt.total[{ds}]", m9.fmt_total(ds))
    for d in DIFF_DDS:
        L(f"fmt.diff[{d}]", m9.fmt_diff(d))
    for km in (5.0, 10.5, 21.0975, 0.04):
        L(f"fmt.km[{f(km, 4)}]", m9.fmt_km(km))
    for m in (0, 999, 1500, 12345, 1234567):
        L(f"fmt.money[{m}]", m9.fmt_money(m))
    L("fmt.year[3]", m9.fmt_year(3))
    L("fmt.year[12,5]", m9.fmt_year(12, 5))
    for g in (1, 2, 3, 4):
        L(f"fmt.grade[{g}]", m9.fmt_grade(g))
    for p in (0.0, 0.5, 0.123, 0.9995, 1.0):
        L(f"fmt.pct[{f(p, 4)}]", m9.fmt_pct(p))
    for n in (1, 2, 11, 21, 36):
        L(f"fmt.rank[{n}]", m9.fmt_rank(n))
        L(f"fmt.rank[{n},promoted]", m9.fmt_rank(n, True))
    for master, label, a, b in ((20260101, 3, 2030, 1007), (1, 1, 0, 0), (2 ** 64 - 1, 8, 4000000000, 59), (123456789012345, 4, 31, 9999)):
        L(f"rng.derive[{master},{label},{a},{b}]", f"{m3.derive(master, label, a, b):016x}")
    for seed in (12345, 0, 2 ** 64 - 1):
        g = m3.Pcg32(seed)
        s = ""
        for _ in range(3):
            s += f"{g.u32()} "
        for _ in range(2):
            s += f(g.uniform(), 12) + " "
        for _ in range(2):
            s += f(g.normal(0.0, 1.0), 9) + " "
        s += str(g.randint(3, 9))
        L(f"rng.pcg[{seed}]", s)
    for gk in ("Early", "Normal", "Late"):
        for y in range(5):
            for lv in (1.0, 3.5, 6.0):
                L(f"world.prog_base[{gk},{y},{f(lv, 1)}]", f(m5.prog_base(gk, y, lv), 9))
    for gap in (0.0, 10.0, 24.0, 40.0, 60.0):
        L(f"world.gap_adj[{f(gap, 1)}]", f(m5.gap_adj(gap), 9))
    for master, year, kind in ((20260101, 3, "Rookie"), (20260101, 12, "Intl"), (99, 1, "Rookie"), (987654321, 40, "Intl")):
        r = m7.gen_special(master, year, kind)
        L(f"special[{master},{year},{kind}]", f"{f(r['a0'], 9)} {f(r['gap'], 9)} {r['look_seed']}")

    def rec(lin, cnt, tomb=False, ls=0):
        return dict(lineage=lin, counter=cnt, node=None if tomb else 1, last_synced=ls)

    pairs = [
        (None, None), (None, rec("A", 3)), (rec("A", 3), None),
        (rec("A", 5, ls=3), rec("A", 3)), (rec("A", 3, ls=3), rec("A", 6)), (rec("A", 5, ls=3), rec("A", 6)),
        (rec("A", 3, True, 3), rec("B", 1)), (rec("A", 3, ls=3), rec("B", 1, True)), (rec("A", 3, ls=3), rec("B", 1)), (rec("A", 4, ls=4), rec("A", 4)),
    ]
    for i, (l, c) in enumerate(pairs):
        L(f"sync.decide[{i}]", m9.sync_decide(l, c))
    return out


def main():
    if len(sys.argv) > 1:
        with open(sys.argv[1], encoding="utf-8") as fh:
            got = fh.read().splitlines()
    else:
        m0 = os.path.join(HERE, "..")
        p = subprocess.run(["dotnet", "run", "--project", os.path.join(m0, "tools", "SimCli", "SimCli.csproj"), "-c", "Release", "--no-build", "--", "vectors"],
                           capture_output=True, text=True, encoding="utf-8", check=False)
        if p.returncode != 0:
            print(p.stdout + p.stderr)
            return 2
        got = p.stdout.splitlines()
    want = lines()
    bad = 0
    for i in range(max(len(want), len(got))):
        w = want[i] if i < len(want) else "(なし)"
        g = got[i] if i < len(got) else "(なし)"
        if w != g:
            bad += 1
            print(f"× {i + 1}: 参照 {w!r} / C# {g!r}")
    print(f"T1 の主なベクタ {len(want)} 行: 不一致 {bad}")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
