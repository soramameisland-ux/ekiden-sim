"""M0 段階 5(世界)の差分テスト用のゴールデンデータ: 05 の乱数を使わない式(提示スコア・力の進捗・施設Lv・奨学金枠・ランク・名声・資金)を、ランダムな入力で参照モデルと比べる。

使い方: python3 -I gen_golden_05.py [出力先ディレクトリ]
"""
import gzip
import json
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_05 as w  # noqa: E402

REGIONS = ["Hokkaido", "Tohoku", "Kanto", "Chubu", "Kinki", "Chugoku", "Shikoku", "Kyushu"]
GT = ["Early", "Normal", "Late"]


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260106)
    cases = []
    for _ in range(4000):
        gtype = rng.choice(GT)
        A0 = rng.uniform(28, 72)
        gap = rng.uniform(8, 45)
        r = w.Runner(A0, gap, gtype, rng.choice(REGIONS))
        r.grade = rng.randint(1, 4)
        r.prog = rng.uniform(0, 0.95)
        s = w.School(0, rng.choice(REGIONS), "Mid")
        s.prestige = rng.uniform(0, 1000)
        s.coach_q = rng.uniform(-0.3, 0.3)
        s.is_player = rng.random() < 0.3
        fac = None
        if s.is_player:
            fac = dict(coach=rng.randint(1, 5), trainer=rng.randint(1, 5), nutri=rng.randint(1, 5), dorm=rng.randint(1, 5), track=rng.randint(1, 5), hill=rng.randint(1, 5),
                       gym=rng.randint(1, 5), medical=rng.randint(1, 5), scout=rng.randint(1, 5))
            s.fac = dict(fac)
        s.cum_points = rng.uniform(0, 320)
        lv = s.lv()
        p10 = rng.uniform(40, 75)
        yp = rng.randint(0, 40)
        cases.append(dict(
            runner=dict(A0=A0, gap=gap, gtype=gtype, region=r.pref_region, grade=r.grade, prog=r.prog),
            school=dict(region=s.region, prestige=s.prestige, coach_q=s.coach_q, is_player=s.is_player, fac=fac, cum_points=s.cum_points),
            lv=lv, dorm=s.dorm(), schol=s.schol_slots(), rank=s.rank(), p10=p10, offer=w.offer_base(s, r, p10),
            season_power=w.season_power(r, lv), season_power_full=w.season_power(r, lv, 1.0), prog_base=[w.prog_base(gtype, y, lv) for y in range(0, 5)], gap_adj=w.gap_adj(gap),
            fame=[w.K["FAME_ENTRY"] + w.K["FAME_TOP"] * math.exp(-(k - 1) / w.K["FAME_DECAY"]) for k in (1, 2, 5, 10, 20)],
            income=(w.K["INC_BASE"] + w.K["INC_PRESTIGE"] * s.prestige + w.K["INC_PRESTIGE2"] * s.prestige ** 2 + w.K["INC_POINT"] * yp), year_points=yp))
    # season_end(進捗の更新)
    for c in cases:
        r = w.Runner(c["runner"]["A0"], c["runner"]["gap"], c["runner"]["gtype"], c["runner"]["region"])
        r.grade, r.prog = c["runner"]["grade"], c["runner"]["prog"]
        w.season_end(r, c["lv"])
        c["prog_after"] = r.prog
    with gzip.open(os.path.join(out_dir, "world_cases.json.gz"), "wt", encoding="utf-8", compresslevel=9) as f:
        json.dump(cases, f, ensure_ascii=False, separators=(",", ":"))
    print(f"world_cases {len(cases)} 件")


if __name__ == "__main__":
    main()
