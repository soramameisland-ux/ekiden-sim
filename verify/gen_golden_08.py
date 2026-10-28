"""M0 段階 9(画面のための規則 08 R-01〜R-17)の差分テスト用のゴールデンデータ(screen_cases.json.gz)。

内容: ランク表記・素質のヒント・記録の導出・推奨プラン・グループの自動編成・月末の予測(週ごと・複数週・補正つき)・調子の分布・テーマごとの月の伸び・
      進行の停止の決定表・個別指導の検証・月末レポートの項目の選択・競合度と前評判の印・性格タグ・練習の相性の印

使い方: python3 -I gen_golden_08.py [出力先ディレクトリ]
"""
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_08 as m8  # noqa: E402
import ref_model_10 as m10  # noqa: E402

STY = {"form": {"Speed": "S", "Balance": "B", "Long": "L"}, "run": {"Pack": "P", "Free": "F", "Solo": "S"}, "flow": {"Chase": "C", "Even": "E", "Lead": "L"}}
SKILLS = ["skill.summer_boy", "skill.recovery_plus", "skill.iron_legs", "skill.injury_prone", "skill.summer_fade", "skill.peaking_master"]


def code(style):
    return STY["form"][style["form"]] + STY["run"][style["run"]] + STY["flow"][style["flow"]]


def forecast_cases(rng, n=900):
    out = []
    themes = list(m3.THEMES)
    for k in range(n):
        r = m8.make_state(random.Random(k), grade=rng.randint(1, 4), gtype=rng.choice(["Early", "Normal", "Late"]))
        style = gifts = None
        if k % 2 == 0:
            rec = m10.recruit(4242, k)
            m10.attach(r, rec["style"], rec["gifts"])
            style, gifts = rec["style"], rec["gifts"]
        if rng.random() < 0.25:
            r["inj"] = (rng.choice(["Minor", "Moderate", "Severe"]), rng.randint(1, 6))
        r["skills"] = set(rng.sample(SKILLS, rng.randint(0, 3)))
        r["bond_avg"] = rng.choice([0.0, rng.uniform(0, 900)])
        r["form"] = rng.randint(-2, 2)
        if rng.random() < 0.25:
            r["fatigue"] = rng.randint(78, 99)               # 安全網(F ≥ 85)が働く週も含める
        fac = {key: rng.randint(1, 5) for key in ("coach", "trainer", "nutri", "dorm", "track", "hill", "gym", "medical")}
        weeks = []
        mi0 = rng.randint(0, 11)
        nth = rng.choice([1, 1, 2])
        for t in range(nth):
            kind = rng.random()
            if kind < 0.2:
                menus = [(m3.TAPER[w], False) for w in range(4)]
            else:
                th = rng.choice(themes)
                menus = [(m3.THEMES[th][w], th == "th.camp") for w in range(4)]
            for menu, camp in menus:
                weeks.append(dict(menu=list(menu), month=(mi0 + t) % 12, camp=camp, individual=rng.random() < 0.15))
        mods = None
        if rng.random() < 0.3:
            mods = {}
            if rng.random() < 0.7:
                mods["E_mult"] = rng.uniform(0.8, 1.2)
            if rng.random() < 0.7:
                mods["r_add"] = rng.uniform(-0.06, 0.06)
            if rng.random() < 0.7:
                mods["inj_mult"] = rng.uniform(0.5, 2.0)
            if rng.random() < 0.7:
                mods["form_add"] = float(rng.randint(-4, 4))
        safety = rng.random() < 0.85
        bond = None if rng.random() < 0.6 else rng.uniform(0, 800)
        fc = m8.forecast(r, [(tuple(w["menu"]), w["month"], w["camp"], w["individual"]) for w in weeks], fac, mi0, safety=safety, bond_avg=bond, mods=mods)
        end = fc["end"]
        out.append(dict(
            runner=dict(stats=r["stats"], pot=r["pot"], xp=r["xp"], fatigue=r["fatigue"], motivation=r["motivation"], grade=r["grade"], gtype=r["gtype"], taper_weeks=r["taper_weeks"],
                         sharpen_hist=[bool(x) for x in r["sharpen_hist"]], inj=[r["inj"][0], r["inj"][1]], skills=sorted(r["skills"]), bond_avg=r["bond_avg"], form=r["form"],
                         style=code(style) if style else None, gifts=list(gifts or [])),
            fac=fac, weeks=weeks, safety=safety, bond=bond, mods=mods,
            expected=dict(F=fc["F"], F_max=fc["F_max"], p_injury=fc["p_injury"], levelups=fc["levelups"], stats_after=fc["stats_after"], safety_weeks=fc["safety_weeks"],
                          last=dict(mu=fc["last"]["mu"], sigma=fc["last"]["sigma"], peaking=fc["last"]["peaking"], injured_now=fc["last"]["injured_now"], F_end=fc["last"]["F_end"],
                                    injury_p=fc["last"]["injury_p"]),
                          form=[fc["form"][-2], fc["form"][-1], fc["form"][0], fc["form"][1], fc["form"][2]],
                          end=dict(xp=end["xp"], taper_weeks=end["taper_weeks"], fatigue=end["fatigue"], inj=[end["inj"][0], end["inj"][1]], sharpen_hist=[bool(x) for x in end["sharpen_hist"]])),
        ))
    return out


def theme_cases():
    out = []
    for stats, f0, grade, gtype in (([58] * 10, 40, 2, "Normal"), ([50, 62, 55, 48, 60, 52, 58, 56, 54, 50], 55, 3, "Late"), ([66] * 10, 30, 1, "Early")):
        tg = m8.theme_gain_table(stats, f0, grade, gtype)
        out.append(dict(stats=stats, f0=f0, grade=grade, gtype=gtype, table={th: dict(dP=v["dP"], F_end=v["F_end"]) for th, v in tg.items()}))
    return out


def stops_cases(rng):
    before = []
    for week in range(48):
        for qual in (set(), {"Fall"}, {"Fall", "National"}, {"National", "NewYear"}, {"Fall", "National", "NewYear"}, {"NewYear"}):
            for seed in (False, True):
                for auto in (False, True):
                    before.append(dict(week=week, qualifies=sorted(qual), seed=seed, auto=auto, stops=m8.stops_before_week(week, qual, seed, auto)))
    after = []
    for _ in range(3000):
        week = rng.randint(0, 47)
        events = [("ev.%d" % i, rng.choice([1, 2, 3])) for i in range(rng.randint(0, 4))]
        injuries = [(rng.randint(1, 40), rng.choice(["Minor", "Moderate", "Severe"]), rng.choice(["A", "B", "C"]), rng.random() < 0.4) for _ in range(rng.randint(0, 4))]
        warn = [1] if rng.random() < 0.4 else []
        race = rng.choice([None, None, None, "Fall", "National", "NewYear"])
        auto = rng.random() < 0.3
        stops, deferred = m8.stops_after_week(week, events, injuries, warn, race, auto)
        after.append(dict(week=week, events=[list(e) for e in events], injuries=[list(i) for i in injuries], warn=bool(warn), race=race, event_auto=auto, stops=stops, deferred=deferred))
    return before, after


def guide_cases(rng):
    out = []
    ks = list(m3.KS) + ["ks.nope"]
    mils = list(m3.MIL) + [None, "mil.nope"]
    for _ in range(1500):
        menu = None if rng.random() < 0.15 else (rng.choice(ks), rng.choice(ks), rng.choice(mils))
        args = (rng.randint(0, 4), rng.choice(["menu", "talk", "other"]), dict(injured=rng.random() < 0.3), rng.randint(0, 3), rng.random() < 0.3, menu)
        ok, why = m8.can_guide(*args)
        errs = m8.validate_menu(*menu) if menu else None
        out.append(dict(points=args[0], kind=args[1], injured=args[2]["injured"], month_week=args[3], talked=args[4], menu=list(menu) if menu else None, ok=ok, reason=why, errors=errs))
    return out


def report_cases(rng):
    kinds = list(m8.REPORT_PRIORITY) + ["unknown_kind"]
    out = []
    for _ in range(1500):
        items = [(rng.choice(kinds), "k%d" % rng.randint(0, 30), rng.choice([0, 1, 2, 3.5, 60, 70, rng.uniform(0, 100)])) for _ in range(rng.randint(0, 40))]
        shown, rest = m8.select_report_items(items)
        out.append(dict(items=[list(i) for i in items], shown=[list(i) for i in shown], rest=rest))
    return out


def group_cases(rng):
    out = []
    for _ in range(300):
        n = rng.randint(1, 45)
        lst = [(i, float(rng.choice([60.0, 62.5, 65.0, rng.uniform(55, 75)]))) for i in rng.sample(range(1, 200), n)]
        g = m8.auto_groups(lst)
        out.append(dict(p=[list(x) for x in lst], groups={str(k): "ABC".index(v) for k, v in g.items()}))
    return out


def fit_cases():
    out = []
    forms, runs, flows = ("Speed", "Balance", "Long"), ("Pack", "Free", "Solo"), ("Chase", "Even", "Lead")
    gift_sets = [[], ["Climber"], ["Descender"], ["Ace"], ["Climber", "Descender"], ["Climber", "Ace"], ["Descender", "Ace"]]
    for f in forms:
        for r_ in runs:
            for fl in flows:
                for gs in gift_sets:
                    for th in m3.THEMES:
                        st = dict(form=f, run=r_, flow=fl)
                        for revealed in (True, False):
                            v = m10.fit_view(st, gs, revealed, th)
                            out.append(dict(style=code(st), gifts=gs, revealed=revealed, theme=th, value=v, mark=m10.fit_mark(v)))
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260109)
    ranks = [[v, m8.rank_letter(v)] for v in [i * 0.5 for i in range(0, 201)] + [29.999999, 59.999999, 89.9999999, 79.99999999, rng.uniform(0, 100)]]
    hints = [[s, p, m8.potential_hint(s, p)] for s in range(20, 100, 3) for p in range(20, 100, 7)]
    pbs = []
    for _ in range(1500):
        st = [rng.randint(25, 95) for _ in range(10)]
        pbs.append(dict(stats=st, t5000=m8.derive_pb(st, "t5000"), t10000=m8.derive_pb(st, "t10000"), half=m8.derive_pb(st, "t_half")))
    plans = {f: m8.TEMPLATES[f] for f in ("Balanced", "Speed", "Long")}
    forms = []
    for mu in (-30, -20, -8, 0, 7.672137, 8, 18.5, 20, 26, 40):
        for sg in (3.0, 7.6, 8.0, 12.0):
            pr = m8.form_probs(mu, sg)
            forms.append(dict(mu=mu, sigma=sg, p=[pr[-2], pr[-1], pr[0], pr[1], pr[2]]))
    for _ in range(300):
        mu, sg = rng.uniform(-40, 50), rng.uniform(2, 14)
        pr = m8.form_probs(mu, sg)
        forms.append(dict(mu=mu, sigma=sg, p=[pr[-2], pr[-1], pr[0], pr[1], pr[2]]))
    before, after = stops_cases(rng)
    rivalry = [[v, m8.rivalry(v)[0], m8.rivalry(v)[1]] for v in [40.0, 51.4, 51.5, 54.9, 55.0, 58.9, 59.0, 66.0] + [rng.uniform(30, 80) for _ in range(200)]]
    previews = [[r_, m8.preview_mark(r_)] for r_ in range(1, 40)]
    traits = []
    for _ in range(600):
        master, ls = rng.getrandbits(64), rng.getrandbits(32)
        traits.append(dict(master=master, look_seed=ls, traits=m8.traits_of(master, ls)))
    doc = dict(ranks=ranks, hints=hints, pbs=pbs, plans=plans, groups=group_cases(rng), forecast=forecast_cases(rng), forms=forms, themes=theme_cases(),
               stops_before=before, stops_after=after, guide=guide_cases(rng), reports=report_cases(rng), rivalry=rivalry, previews=previews, traits=traits, fit=fit_cases(),
               trait_names=m8.TRAITS)
    path = os.path.join(out_dir, "screen_cases.json.gz")
    with gzip.GzipFile(path, "wb", mtime=0) as f:
        f.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    print("wrote", path, os.path.getsize(path) // 1024, "KB;", {k: len(v) for k, v in doc.items() if isinstance(v, list)})


if __name__ == "__main__":
    main()
