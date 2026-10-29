"""M0 の C# 実装(週の解決)を、参照モデルと突き合わせるためのゴールデンデータを出力する(差分テスト用)。

使い方: python3 -I gen_golden_03.py [出力先 JSON]
- 03 §6 の週の解決を、ランダムな状態 3000 件(個性・素養つき 1500 件を含む)で解き、前後の状態とレポートを書き出す。
- 4年間(192 週)の育成を 60 人ぶん解き、毎週の疲労と最後の能力を書き出す(統合の検算)。
C# 側は m0/tests/Domain.Tests/Golden/*.json.gz を読、同じ入力から同じ結果になることを確かめる。
"""
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_10 as m10  # noqa: E402

SKILLS = ["skill.summer_boy", "skill.recovery_plus", "skill.summer_fade", "skill.iron_legs", "skill.injury_prone", "skill.peaking_master"]
FAC_KEYS = ["coach", "trainer", "nutri", "dorm", "track", "hill", "gym", "medical"]
EXTRA_KEYS = ["altitude", "mental", "camp"]                  # L-70 ②: 新設の施設(Lv0 = 未設置 = キーなし)
_XRNG = random.Random(20261029)                              # 新設の施設の抽選だけに使う別の乱数(従来の事例の抽選を動かさない)


def extras(hi):
    return {k: _XRNG.randint(1, hi) for k in EXTRA_KEYS if _XRNG.random() < 0.4}


def rand_menu(rng):
    c = rng.random()
    if c < 0.5:
        th = rng.choice(list(m3.THEMES))
        return list(m3.THEMES[th][rng.randrange(4)])
    if c < 0.65:
        return list(rng.choice(m3.TAPER))
    if c < 0.7:
        return list(m3.SAFE)
    ks = [k for k in m3.KS if k != "ks.cross_train"]
    k1, k2 = rng.sample(ks, 2)
    return [k1, k2, rng.choice(list(m3.MIL))]


def snap(r):
    return dict(stats=list(r["stats"]), xp=list(r["xp"]), fatigue=r["fatigue"], inj=[r["inj"][0], r["inj"][1]], taper_weeks=r["taper_weeks"],
                sharpen_hist=list(r["sharpen_hist"]), form=r["form"], week_levelups=r["week_levelups"])


def make_case(rng, with_indiv, risky=False):
    stats = [rng.randint(25, 85) for _ in range(10)]
    pot = [min(100, s + rng.randint(0, 35)) for s in stats]
    if rng.random() < 0.1:
        pot[rng.randrange(10)] = stats[rng.randrange(10)]            # 上限に届いている能力
    r = m3.new_runner(stats, pot, rng.randint(1, 5), rng.choice(["Early", "Normal", "Late"]))
    r["pot"] = [max(s, p) for s, p in zip(r["stats"], r["pot"])]
    r["xp"] = [rng.randint(0, 600) for _ in range(10)]
    r["fatigue"] = rng.choice([rng.randint(0, 100), rng.randint(30, 90), rng.randint(80, 100)])
    r["motivation"] = rng.randint(1, 5)
    r["taper_weeks"] = rng.randint(0, 5)
    r["sharpen_hist"] = [rng.random() < 0.3 for _ in range(3)]
    if rng.random() < 0.15:
        sev = rng.choice(["Minor", "Moderate", "Severe"])
        r["inj"] = (sev, rng.randint(1, 12))
    r["skills"] = set(rng.sample(SKILLS, rng.choice([0, 0, 1, 2, 3])))
    r["bond_avg"] = rng.random() * 1000.0
    fac = {k: rng.randint(1, 8) for k in FAC_KEYS if rng.random() < 0.9}      # L-70: Lv8 まで
    fac.update(extras(8))
    style = gifts = None
    if with_indiv:
        style = dict(form=rng.choice(["Speed", "Balance", "Long"]), run=rng.choice(["Pack", "Free", "Solo"]), flow=rng.choice(["Chase", "Even", "Lead"]))
        gifts = rng.sample(["Climber", "Descender", "Ace"], rng.choice([0, 0, 1, 2]))
        pot_before = list(r["pot"])
        m10.attach(r, style, gifts)
        r["pot"] = pot_before                                          # 素質の上限の加算は生成の側(別のテスト)。ここでは週の解決だけを見る
    menu = rand_menu(rng)
    camp = rng.random() < 0.2
    safety = rng.random() < 0.85
    individual = rng.random() < 0.25
    if risky:                                                         # 新しい故障の分岐(重症度・期間・施設の短縮)を厚く通す
        r["fatigue"] = rng.randint(60, 100)
        r["inj"] = ("None", 0)
        r["stats"][m3.IDX["DUR"]] = rng.randint(20, 45)
        r["pot"][m3.IDX["DUR"]] = max(r["pot"][m3.IDX["DUR"]], r["stats"][m3.IDX["DUR"]])
        menu = [rng.choice(["ks.repetition", "ks.downhill", "ks.long_run"]), rng.choice(["ks.interval", "ks.hill", "ks.time_trial"]), rng.choice(["mil.high", "mil.max"])]
        camp, safety, individual = rng.random() < 0.5, False, False
        if rng.random() < 0.4:
            r["skills"].add("skill.injury_prone")
    bond_avg = None if rng.random() < 0.5 else rng.random() * 1000.0
    month_index = rng.randint(0, 11)
    seed = rng.getrandbits(63)
    runner_id = rng.randint(1, 100000)
    abs_week = rng.randint(0, 48 * 12)
    before = dict(stats=list(r["stats"]), pot=list(r["pot"]), xp=list(r["xp"]), fatigue=r["fatigue"], motivation=r["motivation"], grade=r["grade"],
                  gtype=r["gtype"], taper_weeks=r["taper_weeks"], sharpen_hist=list(r["sharpen_hist"]), inj=[r["inj"][0], r["inj"][1]],
                  skills=sorted(r["skills"]), bond_avg=r["bond_avg"], style=style, gifts=gifts)
    rep = m3.resolve_week(r, tuple(menu) if menu[2] else (menu[0], menu[1], None), fac, month_index, seed, runner_id, abs_week,
                          camp=camp, safety=safety, individual=individual, bond_avg=bond_avg)
    out_rep = dict(menu=list(rep["menu"]), E=rep["E"], r=rep["r"], L=rep["L"], F_end=rep["F_end"], xp_add=rep["xp_add"], levelups=rep["levelups"],
                   injury_p=rep["injury_p"], injury=list(rep["injury"]) if rep.get("injury") else None, peaking=rep["peaking"],
                   noise=rep["noise"], form_score=rep["form_score"], form=rep["form"])
    return dict(seed=str(seed), runner_id=runner_id, abs_week=abs_week, month_index=month_index, camp=camp, safety=safety, individual=individual,
                bond_avg=bond_avg, fac=fac, menu=menu, before=before, after=snap(r), report=out_rep)


def make_career(rng, k, with_indiv):
    seed = rng.getrandbits(63)
    rid = 1000 + k
    stats0 = [max(30, min(70, int(round(rng.gauss(50, 6))))) for _ in range(10)]
    pot0 = [min(100, s + rng.randint(15, 35)) for s in stats0]
    gtype = rng.choice(["Early", "Normal", "Late"])
    fac = {key: rng.randint(1, 4) for key in FAC_KEYS}
    fac.update(extras(4))
    plan = list(m3.STD_PLAN) if rng.random() < 0.7 else [rng.choice(list(m3.THEMES)) for _ in range(12)]
    r = m3.new_runner(stats0, pot0, 1, gtype)
    r["skills"] = set(rng.sample(SKILLS, rng.choice([0, 0, 1])))
    style = gifts = None
    if with_indiv:
        style = dict(form=rng.choice(["Speed", "Balance", "Long"]), run=rng.choice(["Pack", "Free", "Solo"]), flow=rng.choice(["Chase", "Even", "Lead"]))
        gifts = rng.sample(["Climber", "Descender", "Ace"], rng.choice([0, 1, 1, 2]))
        m10.attach(r, style, gifts)
        pot_attached = list(r["pot"])
    pot_start = list(r["pot"])
    fat = []
    forms = []
    injury_weeks = 0
    for y in range(4):
        r["grade"] = y + 1
        for yw in range(48):
            menu, camp = m3.week_menu(yw, plan, m3.RACES, True)
            aw = y * 48 + yw
            m3.resolve_week(r, menu, fac, yw // 4, seed, rid, aw, camp=camp, safety=True, bond_avg=min(1000.0, aw * 10.0 * 0.7))
            fat.append(r["fatigue"])
            forms.append(r["form"])
            injury_weeks += r["inj"][0] != "None"
            if yw in m3.RACES:
                r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
    return dict(seed=str(seed), runner_id=rid, gtype=gtype, fac=fac, plan=plan, stats0=stats0, pot0=pot_start, skills=sorted(set(r["skills"])), style=style, gifts=gifts,
                fatigue=fat, forms=forms, injury_weeks=injury_weeks, final_stats=list(r["stats"]), final_xp=list(r["xp"]))


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260103)
    cases = [make_case(rng, i % 2 == 1) for i in range(3000)] + [make_case(rng, i % 2 == 1, risky=True) for i in range(2500)]
    with gzip.open(os.path.join(out_dir, "week_cases.json.gz"), "wt", encoding="utf-8", compresslevel=9) as f:
        json.dump(cases, f, ensure_ascii=False, separators=(",", ":"))
    rng = random.Random(20260104)
    careers = [make_career(rng, k, k % 2 == 1) for k in range(60)]
    with gzip.open(os.path.join(out_dir, "careers.json.gz"), "wt", encoding="utf-8", compresslevel=9) as f:
        json.dump(careers, f, ensure_ascii=False, separators=(",", ":"))
    inj = sum(1 for c in cases if c["report"]["injury"])
    print(f"week_cases {len(cases)} 件(新しい故障 {inj} 件・個性つき {sum(1 for c in cases if c['before']['style'])} 件)、careers {len(careers)} 人")


if __name__ == "__main__":
    main()
