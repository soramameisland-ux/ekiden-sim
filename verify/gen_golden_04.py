"""M0 段階 4(レース)の差分テスト用のゴールデンデータを出力する(04 の参照モデルと、10 の個性の補正)。

使い方: python3 -I gen_golden_04.py [出力先ディレクトリ]
- legs: 区間の乱数・ペース配分の危険度・調子/環境の補正・期待時間・余裕の目安・最良の指示(個性つき・特殊能力つきのランダムな選手)
- hungarian: ハンガリー法(ランダムな行列)
- races: 複数チームのレース(個性・特殊能力・指示・檄つき。繰り上げ・順位まで)
- plan_day: 当日変更・指示(全モード)
- qualifier: 予選会
"""
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_04 as m4  # noqa: E402

SKILLS = ["hold_lead", "pack_runner", "solo_weak", "overtake", "big_stage", "stage_fright", "brake_prone", "ekiden_man", "mountain_god",
          "downhill_master", "ace_section", "anchor", "summer_boy"]
TYPES = list(m4.W)
RACES = ["Fall", "National", "NewYear"]
DISTS = sorted({d for secs in m4.COURSE.values() for d, _, _ in secs} | {5.0, 10.0, 21.0975, 20.0, 8.0, 23.1, 21.3})


def pre(skills):
    return sorted("skill." + s for s in skills)


def rand_runner(rng, power=None, name=None, with_style=None):
    power = rng.gauss(64, 7) if power is None else power
    stats = [max(25, min(95, int(round(power + rng.gauss(0, 6))))) for _ in range(10)]
    r = dict(stats=stats, skills=set(rng.sample(SKILLS, rng.choice([0, 0, 0, 1, 2]))), form=rng.randint(-2, 2), fatigue=rng.choice([0, 0, 30, 60, 70, 85]))
    if with_style if with_style is not None else rng.random() < 0.5:
        r["style"] = dict(form=rng.choice(["Speed", "Balance", "Long"]), run=rng.choice(["Pack", "Free", "Solo"]), flow=rng.choice(["Chase", "Even", "Lead"]))
    if name is not None:
        r["name"] = name
    return r


def dump_runner(r):
    return dict(stats=r["stats"], skills=pre(r["skills"]), form=r["form"], fatigue=r["fatigue"], style=r.get("style"), leading=bool(r.get("leading")), name=r.get("name"))


def rand_env(rng):
    return dict(temp=rng.uniform(-2, 32), wind=rng.uniform(-3, 8), rain=rng.random() < 0.3)


def dump_env(e):
    return dict(temp=e["temp"], wind=e["wind"], rain=e["rain"])


def make_legs(rng, n):
    out = []
    for _ in range(n):
        r = rand_runner(rng)
        r["leading"] = rng.random() < 0.4
        typ = rng.choice(TYPES)
        d = rng.choice(DISTS)
        instr = rng.choice(["attack", "keep", "hold"])
        use_dist = rng.random() < 0.8
        temp = rng.uniform(-2, 32)
        weight = rng.choice([0.9, 1.0, 1.2])
        bms = rng.choice([1.0, 1.0, 0.8])
        master, rid, b = rng.getrandbits(63), rng.randint(1, 999), rng.randint(0, 3599)
        rng_pcg = m3.Pcg32(m3.derive(master, 4, rid, b))
        noise, brake, pos, boost, stall = m4.draw_leg(rng_pcg, r, weight, instr, bms, dist=(d if use_dist else None), temp=temp, typ=typ)
        env = rand_env(rng)
        race = rng.choice(RACES)
        si = rng.randrange(len(m4.COURSE[race]))
        secs = m4.COURSE[race]
        longest = max(range(len(secs)), key=lambda i: secs[i][0])
        rr = dict(r, leading=False)
        out.append(dict(runner=dump_runner(r), typ=typ, d=d, instr=instr, use_dist=use_dist, temp=temp, weight=weight, bms=bms, master=str(master), rid=rid, b=b,
                        draw=[noise, brake, pos, boost, stall], draws=rng_pcg.__dict__.get("n", None),
                        env=dump_env(env), race=race, si=si,
                        form_adj=m4.form_adj(r), env_adj=m4.env_adj(env, r), pace_q=m4.pace_q(r, d, typ, temp), pace_q_notemp=m4.pace_q(r, d, typ, None),
                        rating=m4.rating(r["stats"], typ),
                        section_rating=[m4.section_rating(r, typ, longest=lg, is_last=il) for lg in (False, True) for il in (False, True)],
                        base_time=m4.base_time(d, 1.0, m4.rating(r["stats"], typ)),
                        leg_ev=[m4.leg_ev(rr, si, race, env, k) for k in ("attack", "keep", "hold")],
                        gauge=list(m4.gauge(rr, d, typ, temp)), best_instr=m4.best_instr(rr, si, race, env)))
    return out


def make_hung(rng, n):
    out = []
    for _ in range(n):
        rows = rng.randint(1, 10)
        cols = rng.randint(rows, 16)
        cost = [[rng.uniform(100, 5000) for _ in range(cols)] for _ in range(rows)]
        out.append(dict(cost=cost, assignment=m4.hungarian(cost)))
    return out


def make_races(rng, n):
    out = []
    for k in range(n):
        race = RACES[k % 3]
        secs = m4.COURSE[race]
        ns = len(secs)
        master = rng.getrandbits(63)
        rid = rng.randint(11, 999)
        nteam = rng.randint(2, 14)
        envs = m4.gen_env(m3.Pcg32(m3.derive(master, 4, rid, 9999)), race)
        # 強さをばらつかせる(繰り上げが起きるチームも作る)
        base = rng.gauss(64, 5)
        teams = []
        for tid in range(nteam):
            pw = base + rng.gauss(0, 4) - (14 if rng.random() < 0.08 else 0)
            lineup = [rand_runner(rng, pw + rng.gauss(0, 3)) for _ in range(ns)]
            instr = [rng.choice(["attack", "keep", "hold"]) for _ in range(ns)] if rng.random() < 0.5 else None
            team = dict(id=tid * 2 + 1, lineup=lineup, geki=rng.choice([0, 0, 2, 3, 4]), geki_min=rng.choice([0.0, 0.0, 0.02, 0.05]), geki_f=rng.choice([1.0, 1.0, 0.7]))
            if instr:
                team["instr"] = instr
            teams.append(team)
        res = m4.simulate_race(race, teams, envs, m4.pcg_for(master, rid))
        out.append(dict(race=race, master=str(master), rid=rid, envs=[dump_env(e) for e in envs],
                        teams=[dict(id=t["id"], lineup=[dump_runner(r) for r in t["lineup"]], instr=t.get("instr"), geki=t["geki"], geki_min=t["geki_min"], geki_f=t["geki_f"]) for t in teams],
                        results=[dict(id=s["id"], sections=s["sections"], cum=s["cum"], total=s["total_ds"], rank=s["rank"], kuriage=s["kuriage"],
                                      events=[[e[0], e[1]] + ([e[2]] if len(e) > 2 else []) for e in s["events"]], geki_left=s["geki"]) for s in res]))
    return out


def make_plan_day(rng, n):
    out = []
    modes = ["plain", "auto", "skilled", "skilled_swap", "skilled_instr", "gauge", "all_attack", "all_hold"]
    for k in range(n):
        race = RACES[k % 3]
        rule = m4.RULES[race]
        entry = [rand_runner(rng, rng.gauss(66, 5), name=i) for i in range(rule["entry"])]
        envs = [rand_env(rng) for _ in range(2 if race == "NewYear" else 1)]
        mode = modes[(k // 3) % len(modes)]
        lineup, instr, swaps = m4.plan_day(entry, race, envs, mode)
        out.append(dict(race=race, mode=mode, entry=[dump_runner(r) for r in entry], envs=[dump_env(e) for e in envs],
                        lineup=[r["name"] for r in lineup], instr=instr, swaps=swaps))
    return out


def make_qualifier(rng, n):
    out = []
    for k in range(n):
        master = rng.getrandbits(63)
        rid = rng.randint(1, 99) * 10 + 4
        teams = [dict(id=t, runners=[rand_runner(rng, rng.gauss(64, 5)) for _ in range(12)]) for t in range(rng.randint(3, 6))]
        env = rand_env(rng)
        res = m4.qualifier(teams, m4.pcg_for(master, rid), env)
        out.append(dict(master=str(master), rid=rid, env=dump_env(env), teams=[dict(id=t["id"], runners=[dump_runner(r) for r in t["runners"]]) for t in teams],
                        results=[dict(total=tot, id=tid, times=times) for tot, tid, times in res]))
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260105)
    data = dict(legs=make_legs(rng, 3000), hungarian=make_hung(rng, 300), races=make_races(rng, 450), plan_day=make_plan_day(rng, 240), qualifier=make_qualifier(rng, 40))
    with gzip.open(os.path.join(out_dir, "race_cases.json.gz"), "wt", encoding="utf-8", compresslevel=9) as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    nk = sum(1 for r in data["races"] for s in r["results"] if s["kuriage"])
    nb = sum(1 for r in data["races"] for s in r["results"] for e in s["events"] if e[0] == "brake")
    ng = sum(1 for r in data["races"] for s in r["results"] for e in s["events"] if e[0] == "geki_brake")
    ns = sum(1 for r in data["races"] for s in r["results"] for e in s["events"] if e[0] == "stall")
    print(f"legs {len(data['legs'])} / hungarian {len(data['hungarian'])} / races {len(data['races'])}(繰り上げ {nk}・ブレーキ {nb}・失速 {ns}・檄 {ng})/ plan_day {len(data['plan_day'])} / qualifier {len(data['qualifier'])}")


if __name__ == "__main__":
    main()
