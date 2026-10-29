"""05 §14.8 の『受け入れの帯』(09 §16.3 の T4 の参照)を、参照モデル(ref_model_05)で取り直す。

使い方: python3 -I band_05.py <Elite|Mid|Low> [シード数 80] [予選会の通過校数 既定は ref_model_04 の QUAL_PASS]
出力: 25 年度〜の優勝率・三冠率・戦力 P の平均、クリア年度の平均と中央値(45 年度までに達しなければ 46)、シード間の標準偏差、
      C# の 120 シードと比べる許容 = 3σ × √(1/N + 1/120)(05 §14.8 と同じ式。強豪・弱小は N = 80、中堅は N = 120 で取った)。
シードは 1..N(C# の `Common.WorldSeed` とは別の系列。参照モデルは自前の乱数)。
"""
import math
import multiprocessing as mp
import os
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_04 as r4  # noqa: E402
import ref_model_05 as w  # noqa: E402

YEARS, FROM = 45, 25
_START = "Mid"
_PASS = None


def _init(start, qual_pass):
    global _START
    _START = start
    if qual_pass is not None:
        r4.K["QUAL_PASS"] = qual_pass


def one(seed):
    stats = w.run(seed, YEARS, _START)[0]
    seen = [False] * 3
    clear = YEARS + 1
    wins = triple = p = 0.0
    n = 0
    for r in stats:
        for i in range(3):
            seen[i] |= bool(r["wins"][i])
        if clear > YEARS and all(seen):
            clear = r["year"]
        if r["year"] >= FROM:
            c = sum(1 for x in r["wins"] if x)
            wins += c
            triple += 1 if c == 3 else 0
            p += r["P"]
            n += 1
    return (wins / (3.0 * n), triple / n, float(clear), p / n)


def main():
    start = sys.argv[1] if len(sys.argv) > 1 else "Mid"
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 80
    qp = int(sys.argv[3]) if len(sys.argv) > 3 else None
    with mp.Pool(os.cpu_count() or 1, initializer=_init, initargs=(start, qp)) as pool:
        rows = pool.map(one, range(1, n + 1), chunksize=1)
    names = ("優勝率", "三冠率", "クリア年度", "戦力 P")
    print(f"== {start}  シード {n}  QUAL_PASS={qp if qp is not None else r4.K['QUAL_PASS']}  ({YEARS} 年度。{FROM} 年度〜)")
    cl = sorted(r[2] for r in rows)
    print("  未クリア(45 年度までに達しない)", sum(1 for r in rows if r[2] > YEARS), "/", n, f"  クリア年度の中央値 {st.median(cl):.1f}  20 年度までにクリア {sum(1 for c in cl if c <= 20) / n:.2f}")
    for i, name in enumerate(names):
        col = [r[i] for r in rows]
        m, sd = st.mean(col), st.pstdev(col)
        tol = 3.0 * sd * math.sqrt(1.0 / n + 1.0 / 120)
        print(f"  {name:8s} 平均 {m:.3f}  σ {sd:.3f}  許容 ±{tol:.3f}")


if __name__ == "__main__":
    main()
