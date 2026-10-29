"""L-70 ②: 新設の施設(高地・メンタル・合宿所)の効きを、既存の施設・スタッフの効きと同じものさしで測る(検算用)。

詳細モデル(ref_model_03)で 4 年間の選手を回し、卒業時の力 P_long(fit_progress_05 と同じ重み)の平均を、施設の組み合わせごとに出す。
『8 項目すべてが Lv n』のときの P_long を物差しにして、新設 1 つぶんが、8 項目の平均レベル何ぶんに当たるか(`lv 換算`)を求める。
使い方: python3 -I exp_extras_03.py [人数] [係数=値 ...]   例: python3 -I exp_extras_03.py 600 altitude_xp=0.04 mental_xp=0.08 camp_xp=0.03
"""
import os
import random
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m  # noqa: E402

W_LONG = {"STA": .35, "PAC": .25, "SPD": .20, "MEN": .10, "KCK": .10}
EIGHT = ("coach", "trainer", "nutri", "dorm", "track", "hill", "gym", "medical")


def p_long(s):
    return sum(w * s[m.IDX[k]] for k, w in W_LONG.items())


def run(fac, n):
    """n 人の 4 年後の P_long の平均と、4 年間の故障(回/人年)。"""
    rnd = random.Random(7)
    tot, inj = 0.0, 0
    for rid in range(n):
        a0 = rnd.choice([40, 45, 50, 55, 60, 65])
        gap = rnd.choice([10, 20, 30, 40])
        gtype = rnd.choices(["Early", "Normal", "Late"], [0.25, 0.55, 0.20])[0]
        rng = m.Pcg32(m.derive(11, 7, rid, 0))
        s0 = [max(25, min(90, round(rng.normal(a0, 3)))) for _ in range(10)]
        p0 = [min(100, s + max(2, round(rng.normal(gap, 3)))) for s in s0]
        r = m.new_runner(s0, p0, 1, gtype)
        for y in range(4):
            r["grade"] = y + 1
            for yw in range(48):
                menu, camp = m.week_menu(yw, m.STD_PLAN, m.RACES, True)
                aw = y * 48 + yw
                rep = m.resolve_week(r, menu, fac, yw // 4, 2026, 500000 + rid, aw, camp=camp, bond_avg=min(1000.0, aw * 7.0))
                if rep.get("injury"):
                    inj += 1
                if yw in m.RACES:
                    r["fatigue"] = min(100, r["fatigue"] + m.round_half_up(m.RACES[yw] * m.K["RACE_FATIGUE_PER_KM"]))
        tot += p_long(r["stats"])
    return tot / n, inj / (n * 4)


def all8(lv, **extra):
    f = {k: lv for k in EIGHT}
    f.update(extra)
    return f


def main(n=1200):
    base5, _ = run(all8(5), n)
    base8, _ = run(all8(8), n)
    base7, _ = run(all8(7), n)
    base1, _ = run(all8(1), n)
    print(f"8 項目すべて Lv1: P_long {base1:.2f} / Lv5: {base5:.2f} / Lv7: {base7:.2f} / Lv8: {base8:.2f}")
    per_lv5 = (base5 - base1) / 4.0
    per_lv8 = (base8 - base7) / 0.25            # Lv7(5.5)→Lv8(5.75)= 効きの +0.25 あたり
    print(f"  平均レベル 1 あたりの P_long: Lv1→5 {per_lv5:.3f} / Lv5 超(Lv7→8)の効きの 1 あたり {per_lv8:.3f}")
    for name, key in (("高地", "altitude"), ("メンタル", "mental"), ("合宿所", "camp")):
        for lv in (1, 3, 5, 8):
            v, _ = run(all8(8, **{key: lv}), n)
            d = v - base8
            print(f"  {name} Lv{lv} (基準=8 項目 Lv8): ΔP_long {d:+.3f}   換算(Lv5 までの尺度)= 平均レベル {d / per_lv5:+.3f}  /  {d / per_lv5 * 8:+.2f} 項目ぶん")
    v, _ = run(all8(8, altitude=8, mental=8, camp=8), n)
    print(f"  3 つとも Lv8: ΔP_long {v - base8:+.3f}")
    v5, _ = run(all8(5, altitude=5, mental=5, camp=5), n)
    print(f"  (参考)8 項目 Lv5 + 3 つとも Lv5: ΔP_long {v5 - base5:+.3f}")


if __name__ == "__main__":
    for kv in sys.argv[2:]:
        k, v = kv.split("=")
        m.FC[k] = float(v)
    print("係数:", {k: m.FC[k] for k in ("altitude_xp", "mental_xp", "camp_xp", "camp_inj")})
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 1200)
