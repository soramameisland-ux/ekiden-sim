"""05 §10.2 の「進捗率の近似式」を、03 の詳細モデルから再現するスクリプト(検算用)。

詳細モデル(ref_model_03)で2400人の4年間を回し、年度末の力 P_long がギャップ(素質 − 入学時)に対して
どれだけ進んだか(進捗率)を、成長タイプ×施設・スタッフのレベル別に出力する。
ref_model_05.py の PROG 表と gap_adj() は、この出力に直線を当てはめた値。
使い方: python3 -I fit_progress_05.py
"""
import os
import random
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m  # noqa: E402

W_LONG = {"STA": .35, "PAC": .25, "SPD": .20, "MEN": .10, "KCK": .10}


def p_long(s):
    return sum(w * s[m.IDX[k]] for k, w in W_LONG.items())


def career_years(rid, a0, gap, gtype, lv):
    fac = dict(coach=lv, trainer=lv, nutri=lv, dorm=lv, track=lv, hill=lv, gym=lv, medical=lv)
    rng = m.Pcg32(m.derive(11, 7, rid, 0))
    s0 = [max(25, min(90, round(rng.normal(a0, 3)))) for _ in range(10)]
    p0 = [min(100, s + max(2, round(rng.normal(gap, 3)))) for s in s0]
    r = m.new_runner(s0, p0, 1, gtype)
    start, pot = p_long(s0), p_long(p0)
    traj = []
    for y in range(4):
        r["grade"] = y + 1
        for yw in range(48):
            menu, camp = m.week_menu(yw, m.STD_PLAN, m.RACES, True)
            aw = y * 48 + yw
            m.resolve_week(r, menu, fac, yw // 4, 2026, 500000 + rid, aw, camp=camp, bond_avg=min(1000.0, aw * 7.0))
            if yw in m.RACES:
                r["fatigue"] = min(100, r["fatigue"] + m.round_half_up(m.RACES[yw] * m.K["RACE_FATIGUE_PER_KM"]))
        traj.append(p_long(r["stats"]))
    return start, pot, traj


def main(n=2400):
    rnd = random.Random(1)
    rows = []
    for rid in range(n):
        a0 = rnd.choice([40, 45, 50, 55, 60, 65])
        gap = rnd.choice([10, 20, 30, 40])
        gtype = rnd.choices(["Early", "Normal", "Late"], [0.25, 0.55, 0.20])[0]
        lv = rnd.choice([1, 2, 3, 4, 5])
        s, pot, tr = career_years(rid, a0, gap, gtype, lv)
        rows.append((a0, gap, gtype, lv, s, pot, tr))
    print("== 年度末の進捗率(累積。ギャップに対する割合)")
    for gt in ("Early", "Normal", "Late"):
        line = []
        for lv in (1, 2, 3, 4, 5):
            v = [[(x - r[4]) / (r[5] - r[4]) for x in r[6]] for r in rows if r[2] == gt and r[3] == lv]
            line.append("Lv%d:%s" % (lv, "/".join(f"{st.mean(x[t] for x in v):.2f}" for t in range(4))))
        print(gt, " ".join(line))
    print("== 4年後の進捗率をギャップ別に(Normal・Lv3)")
    for gap in (10, 20, 30, 40):
        v = [(r[6][3] - r[4]) / (r[5] - r[4]) for r in rows if r[2] == "Normal" and r[3] == 3 and r[1] == gap]
        print(f"gap{gap}: {st.mean(v):.2f}", end="  ")
    print()


if __name__ == "__main__":
    main()
