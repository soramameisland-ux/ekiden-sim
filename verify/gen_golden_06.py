"""M0 段階 6(イベント)の差分テスト用のゴールデンデータ(events_cases.json.gz)。

内容:
  catalogue  : カタログ(手書き + 覚醒。52件)・特殊能力・排他の組(C# のカタログと項目ごとに突き合わせる)
  exprs      : 条件式の表(カタログの条件・選択肢の条件・覚醒の条件 + 乱数で作った式)。構文木の文字列つき
  neg        : 検証に違反するイベント定義(V-01〜V-10)と、参照モデルが返した違反の見出し
  states     : 週の途中の状態(イベントの判定の直前)。ref_model_06.Sim を走らせて event_phase の前後を保存する
  cases      : 状態ごとの 条件式の評価値 / 派生値(refresh)/ 発生したイベントと選んだ選択肢 / 効果を適用したあとの状態

使い方: python3 -I gen_golden_06.py [出力先ディレクトリ]
"""
import copy
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_06 as m6  # noqa: E402
import events_06 as ev6  # noqa: E402

STY = {"form": {"Speed": "S", "Balance": "B", "Long": "L"}, "run": {"Pack": "P", "Free": "F", "Solo": "S"}, "flow": {"Chase": "C", "Even": "E", "Lead": "L"}}
FAC_KEYS = ("dorm", "track", "hill", "gym", "medical", "coach", "trainer", "nutri", "scout")
RACE_KEYS = ("in_lineup", "brake", "section_rank", "section_win", "record")


# ---------------------------------------------------------------- 状態の書き出し
def snap_runner(r):
    sty = r.get("style")
    return dict(
        id=r["id"], stats=list(r["stats"]), pot=list(r["pot"]), xp=list(r["xp"]), fatigue=r["fatigue"], form=r["form"], motivation=r["motivation"],
        grade=r["grade"], inj=[r["inj"][0], r["inj"][1]], inj_age=r["inj_age"], skills=sorted(r["skills"]), group=r["group"], captain=r["captain"],
        overseas=r["overseas"], far=r["far"], near=r["near"], pref=r["pref"], hs=r["hs"], enroll_week=r["enroll_week"],
        ctr={k: r["ctr"][k] for k in sorted(m6.COUNTERS)}, races=r["races"], section_wins=r["section_wins"], brakes=r["brakes"], p_rank=r["p_rank"],
        regular=r["regular"], gap_avg=r["gap_avg"], bond_avg=r["bond_avg"], taper_weeks=r["taper_weeks"],
        race=[r["race"].get(k, 0) for k in RACE_KEYS], last_ev_month=r["last_ev_month"], ev_once=sorted(r["ev_once"]),
        cooldown={k: r["cooldown"][k] for k in sorted(r["cooldown"])},
        style=(STY["form"][sty["form"]] + STY["run"][sty["run"]] + STY["flow"][sty["flow"]]) if sty else None,
        gifts=list(r.get("gifts", ())),
    )


def snap(sim):
    t = sim.team
    return dict(
        abs_week=sim.abs_week, seed=sim.seed, fac={k: sim.fac[k] for k in FAC_KEYS},
        funds=t["funds"], prestige=t["prestige"], morale=t["morale"], coach_rank=t["coach_rank"], scout_pts=t["scout_pts"], guidance_left=t["guidance_left"],
        rank=[t["rank"].get(k, 99) for k in ("fall", "national", "newyear")], last_rank=[t["last_rank"].get(k, 8) for k in ("fall", "national", "newyear")],
        year_points=t["year_points"], year_counts={k: v for k, v in sorted(t["year_counts"].items()) if k != "_total"}, year_total=t["year_counts"].get("_total", 0),
        fired={k: [v["count"], v["last"]] for k, v in sorted(t["fired"].items())},
        pair_cd=sorted([k[0], k[1], k[2], v] for k, v in t.get("pair_cd", {}).items()),
        mods=[[x["kind"], -1 if x["to"] == "team" else x["to"], x["value"], x["until"], x["src"]] for x in t["mods"]],
        ob=[[o["pref"], int(o["corp"]), o["power"], o["role"], o["until"]] for o in t["ob"]],
        bench=sorted(t["bench"]), forced=sorted(t["forced"]),
        bonds=sorted([a, b, v] for (a, b), v in sim.bonds.items()),
        runners=[snap_runner(r) for r in sim.runners],
    )


# ---------------------------------------------------------------- 乱数で作る条件式
NUM_VARS_T = ["cal.year", "cal.month", "cal.week", "cal.next_race", "cal.next_race_kind", "race.kind", "race.team_rank", "race.expected_rank",
              "t.funds", "t.prestige", "t.morale", "t.size", "t.coach_rank", "t.scout_pts", "t.guidance", "t.n_g1", "t.n_g2", "t.n_g3", "t.n_g4", "t.n_g3plus",
              "ob.total", "ob.corp", "t.rank.fall", "t.rank.national", "t.rank.newyear", "t.last_rank.fall", "t.last_rank.national", "t.last_rank.newyear",
              "fac.dorm", "fac.track", "fac.hill", "fac.gym", "fac.medical", "staff.coach", "staff.trainer", "staff.nutri", "staff.scout"]
NUM_VARS_R = sorted(m6.RUNNER_FIELDS) + ["stat." + s for s in m6.STATS]
FUNCS_BOOL_R = ["senior_same_pref({w})", "rival_same_hs({w})", "has_skill({w}, '{sk}')"]
SKILL_IDS = [s[0] for s in ev6.SKILLS]


class Gen:
    def __init__(self, rng, roles):
        self.rng, self.roles = rng, roles

    def who(self):
        return self.rng.choice(self.roles)

    def num_atom(self):
        g = self.rng
        k = g.random()
        if k < 0.30:
            return str(g.randint(0, 120)) if g.random() < 0.6 else f"{g.uniform(0, 100):.1f}"
        if k < 0.55 and self.roles:
            return self.who() + "." + g.choice(NUM_VARS_R)
        if k < 0.75:
            return g.choice(NUM_VARS_T)
        if k < 0.82 and self.roles:
            return f"ctr({self.who()}, '{g.choice(sorted(m6.COUNTERS))}')"
        if k < 0.88 and self.roles:
            return f"{g.choice(['same_pref_n', 'ob_level'])}({self.who()})"
        if k < 0.92 and len(self.roles) == 2:
            return "bond(r, p)"
        return str(g.randint(1, 9))

    def num(self, d):
        g = self.rng
        if d <= 0 or g.random() < 0.25:
            return self.num_atom()
        k = g.random()
        if k < 0.50:
            a, b = self.num(d - 1), self.num(d - 1)
            e = f"{a} {g.choice('+-*/')} {b}"
            return f"({e})" if g.random() < 0.5 else e
        if k < 0.60:
            return "-" + self.num_atom()
        if k < 0.75:
            return f"{g.choice(['min', 'max'])}({self.num(d - 1)}, {self.num(d - 1)})"
        if k < 0.82:
            return f"abs({self.num(d - 1)})"
        if k < 0.90:
            return f"clamp({self.num(d - 1)}, {self.num_atom()}, {self.num_atom()})"
        return f"({self.num(d - 1)})"

    def boolean(self, d):
        g = self.rng
        k = g.random()
        if d <= 0 or k < 0.40:
            return f"{self.num(1)} {g.choice(['<', '<=', '>', '>=', '==', '!='])} {self.num(1)}"
        if k < 0.50 and self.roles:
            f = g.choice(FUNCS_BOOL_R)
            return f.format(w=self.who(), sk=g.choice(SKILL_IDS))
        if k < 0.60:
            return "not " + self.boolean(d - 1)
        if k < 0.80:
            a, b = self.boolean(d - 1), self.boolean(d - 1)
            return f"({a}) and ({b})" if g.random() < 0.6 else f"{a} and {b}"
        if k < 0.95:
            a, b = self.boolean(d - 1), self.boolean(d - 1)
            return f"({a}) or ({b})" if g.random() < 0.6 else f"{a} or {b}"
        return g.choice(["true", "false"])


def fmt_ast(a):
    """C# の Node.ToString() と同じ形(06 の構文木のタプルから)。"""
    k = a[0]
    if k == "num":
        v = a[1]
        return str(int(v)) if float(v) == int(v) and abs(v) < 1e15 else repr(float(v))
    if k == "str":
        return "'" + a[1] + "'"
    if k == "bool":
        return "true" if a[1] else "false"
    if k == "var":
        return a[1]
    if k == "neg":
        return "(neg " + fmt_ast(a[1]) + ")"
    if k == "not":
        return "(not " + fmt_ast(a[1]) + ")"
    if k in ("and", "or"):
        return f"({k} {fmt_ast(a[1])} {fmt_ast(a[2])})"
    if k in ("cmp", "bin"):
        return f"({a[1]} {fmt_ast(a[2])} {fmt_ast(a[3])})"
    if k == "call":
        return "(" + a[1] + "".join(" " + fmt_ast(x) for x in a[2]) + ")"
    raise ValueError(k)


# ---------------------------------------------------------------- 検証に違反するイベント(V-01〜V-10)
def neg_cases(rng):
    base = [e for e in m6.all_events() if e["scope"] == "pair" or e["scope"] == "runner" or e["scope"] == "team"]
    pick = lambda scope: copy.deepcopy(rng.choice([e for e in base if e["scope"] == scope and not e["id"].startswith("ev.awk.") and any(c["fx"] for c in e["choices"])]))
    out = []

    def add(e, label):
        errs = m6.validate_catalogue([e] + ([copy.deepcopy(e)] if label == "V-01" else []))
        out.append(dict(label=label, event=e, errors=errs))

    e = pick("runner"); add(e, "V-01")
    e = pick("runner"); e["cond"] = "r.grade == "; add(e, "V-02 syntax")
    e = pick("runner"); e["cond"] = "r.grade == 1 and q.x"; add(e, "V-02 var")
    e = pick("runner"); e["cond"] = "p.grade >= 2"; add(e, "V-02 role")
    e = pick("runner"); e["cond"] = "max(1)"; add(e, "V-02 arity")
    e = pick("team"); e["cond"] = "r.grade == 1"; add(e, "V-02 team role")
    e = pick("runner"); e["choices"][0]["req"] = "t.funds >=>= 1"; add(e, "V-02 req")
    e = pick("runner"); e["choices"] = []; add(e, "V-03 none")
    e = pick("runner"); e["choices"] = e["choices"] + [dict(e["choices"][0])]; add(e, "V-03 dup")
    e = pick("runner"); e["default"] = ["nonexistent"]; add(e, "V-04 default")
    e = pick("runner"); e["default"] = []; add(e, "V-04 empty")
    e = pick("runner"); e["choices"][-1]["req"] = "t.funds >= 1"; e["default"] = [e["choices"][-1]["id"]]; add(e, "V-04 last req")
    for p in (0.0, 1.5, -0.1):
        e = pick("runner"); e["p"] = p; add(e, "V-05 %s" % p)
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.mot("r", 2)]; add(e, "V-07 mot")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.fat("r", -25)]; add(e, "V-07 fat")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.xp("r", "STA", 500)]; add(e, "V-07 xp")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.xp("r", "STA", 400), ev6.xp("r", "MEN", 400), ev6.xp("r", "REC", 100)]; add(e, "V-07 xp total")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.funds(-2000)]; add(e, "V-07 funds low")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.funds(600)]; add(e, "V-07 funds high")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.mod("r_add", "r", 0.2, 8)]; add(e, "V-08 value")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.mod("E_mult", "team", 1.5, 8)]; add(e, "V-08 emult")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.mod("inj_mult", "r", 1.5, 60)]; add(e, "V-08 weeks")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.mot("p", 1)]; add(e, "V-06 p")
    e = pick("runner"); e["choices"][0]["fx"] = [ev6.bond(10)]; add(e, "V-06 bond")
    e = pick("pair"); e["once"] = "runner"; add(e, "V-09")
    e = pick("runner"); e["phase"] = "race"; e["months"] = [3]; add(e, "V-10")
    return out


# ---------------------------------------------------------------- 状態の採取
def eval_all(sim, race, exprs, n_catalogue, rng):
    """状態 sim で、カタログの条件式をすべてと、乱数で作った式の一部を評価する。[式の番号, r の ID, p の ID, 値] のリスト。"""
    out = []
    rs = sim.runners
    combos = []
    for _ in range(2):
        r, p = rng.sample(rs, 2)
        combos.append((r, p))
    chosen = list(range(n_catalogue)) + rng.sample(range(n_catalogue, len(exprs)), 40)
    for i in chosen:
        x = exprs[i]
        roles = tuple(x["roles"])
        if not roles:
            ctxs = [(None, None)]
        elif roles == ("r",):
            ctxs = [(r, None) for r, _ in combos]
        else:
            ctxs = combos
        for r, p in ctxs:
            v = m6.eval_src(x["src"], m6.Ctx(sim, r, p, race), roles)
            out.append([i, r["id"] if r else None, p["id"] if p else None, float(v)])
    return out


def collect_states(rng_seed, exprs, n_catalogue):
    """ref_model_06.Sim を走らせて、event_phase の直前の状態と、そのあとの状態・発生したイベントを採る。"""
    rng = random.Random(rng_seed)
    cases = []
    orig_phase, orig_choose = m6.event_phase, m6.choose
    log = []

    def choose_logged(sim, ev, ctx, stream):
        c = orig_choose(sim, ev, ctx, stream)
        log.append(c["id"])
        return c

    def perturb(sim):
        """まれにしか起こらない条件(まれな特殊能力・チームの停滞など)も試すため、状態を少し乱す(参照モデルと C# に同じ状態を渡すので、現実的でなくてよい)。"""
        t = sim.team
        for _ in range(rng.randint(1, 3)):
            k = rng.random()
            r = rng.choice(sim.runners)
            if k < 0.20:
                r["brakes"] = rng.randint(0, 5)
            elif k < 0.35:
                r["ctr"]["camp_overload"] = rng.randint(0, 3)
                r["ctr"]["camps"] = rng.randint(0, 5)
            elif k < 0.50:
                r["ctr"]["hill_weeks"] = rng.randint(0, 40)
                r["stats"][4] = rng.randint(55, 78)
                r["pot"][4] = max(r["pot"][4], r["stats"][4])
                r["grade"] = rng.randint(1, 4)
            elif k < 0.60:
                t["morale"] = rng.randint(10, 60)
            elif k < 0.70:
                r["fatigue"], r["motivation"], r["form"] = rng.randint(0, 100), rng.randint(1, 5), rng.randint(-2, 2)
            elif k < 0.78:
                r["inj"] = (rng.choice(["Minor", "Moderate", "Severe"]), rng.randint(1, 10))
                r["inj_age"] = rng.randint(0, 12)
            elif k < 0.86:
                t["funds"] = rng.randint(0, 3000)
            elif k < 0.93:
                r["stats"][7] = rng.randint(40, 70)
                r["ctr"]["peaked_races"] = rng.randint(0, 10)
            else:
                r["skills"] = set(rng.sample(["skill." + sid for sid in SKILL_IDS], rng.randint(0, 8)))

    def phase_logged(sim, race=None):
        if rng.random() < 0.12:
            perturb(sim)
        pre = snap(sim)
        pre_race = None if not race else dict(kind=race["kind"], team_rank=race["team_rank"], expected_rank=race["expected_rank"])
        refreshed = evals = None
        keep_peek = rng.random()
        if keep_peek < 0.010:
            s2 = copy.deepcopy(sim)
            s2.refresh()
            refreshed = [[r["id"], r["p_rank"], r["group"], r["regular"], r["gap_avg"], r["bond_avg"]] for r in s2.runners]
            evals = eval_all(sim, race, exprs, n_catalogue, rng)
        log.clear()
        fired = orig_phase(sim, race)
        if refreshed is None and not fired and rng.random() > 0.08:
            return fired
        post = snap(sim)
        cases.append(dict(pre=pre, race=pre_race, policy=sim.policy, ev_scale=sim.ev_scale,
                          fired=[[e["id"], r["id"] if r else None, p["id"] if p else None, log[i]] for i, (e, r, p) in enumerate(fired)],
                          post=post, refreshed=refreshed, evals=evals))
        return fired

    m6.event_phase, m6.choose = phase_logged, choose_logged
    try:
        for seed in range(rng_seed, rng_seed + 60):
            scen = rng.choice(["mid", "late"])
            policy = rng.choice(["default", "uniform"])
            scale = rng.choice([1.0, 3.0, 8.0, 20.0])
            years = rng.randint(2, 5)
            m6.run_sim(seed, years=years, warm=3, scenario=scen, policy=policy, events_on=True, ev_scale=scale)
    finally:
        m6.event_phase, m6.choose = orig_phase, orig_choose
    return cases


def thin(cases, rng, max_fired=130, max_quiet=30):
    """発生ありの状態を max_fired 件、なしを max_quiet 件までにする(ファイルの大きさのため)。評価値・派生値つきの状態は残し、
    どのイベントも少なくとも1回は含まれるようにする。"""
    keep = [c for c in cases if c["evals"] is not None]
    fired = [c for c in cases if c["evals"] is None and c["fired"]]
    quiet = [c for c in cases if c["evals"] is None and not c["fired"]]
    rng.shuffle(fired)
    rng.shuffle(quiet)
    covered = {f[0] for c in keep for f in c["fired"]}
    chosen, rest = [], []
    for c in fired:
        ids = {f[0] for f in c["fired"]}
        if ids - covered:
            chosen.append(c)
            covered |= ids
        else:
            rest.append(c)
    return keep + chosen + rest[:max(0, max_fired - len(chosen))] + quiet[:max_quiet]


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260106)

    # カタログ
    events = m6.all_events()
    catalogue = dict(
        events=[{k: v for k, v in e.items() if k not in ("_roles",)} for e in events],
        skills=[list(s) for s in ev6.SKILLS], exclusive=[list(x) for x in ev6.EXCLUSIVE], violations=m6.validate_catalogue(events),
    )

    # 条件式の表: カタログ + 乱数
    exprs = []
    seen = set()

    def add_expr(src, roles):
        key = (src, tuple(roles))
        if key in seen:
            return
        seen.add(key)
        exprs.append(dict(src=src, roles=list(roles), tree=fmt_ast(m6.parse(src))))

    for e in events:
        roles = ("r", "p") if e["scope"] == "pair" else (("r",) if e["scope"] == "runner" else ())
        if e["cond"]:
            add_expr(e["cond"], roles)
        for c in e["choices"]:
            if c["req"]:
                add_expr(c["req"], roles)
    n_catalogue = len(exprs)
    for _ in range(900):
        roles = rng.choice([("r",), ("r", "p"), ()])
        g = Gen(rng, list(roles))
        src = g.boolean(3) if rng.random() < 0.6 else g.num(3)
        try:
            m6.compiled(src, roles)
        except m6.DslError:
            continue
        add_expr(src, roles)
    print("式:", len(exprs), "(カタログ", n_catalogue, ")")

    cases = thin(collect_states(20260106, exprs, n_catalogue), rng)
    n_fired = sum(1 for c in cases if c["fired"])
    kinds = {}
    for c in cases:
        for f in c["fired"]:
            kinds[f[0]] = kinds.get(f[0], 0) + 1
    print("状態:", len(cases), "(発生あり", n_fired, "/ 評価つき", sum(1 for c in cases if c["evals"] is not None), ")  イベントの種類:", len(kinds), "/ 52")
    print("未発生のイベント:", sorted({e["id"] for e in events} - set(kinds)))
    neg = neg_cases(rng)
    doc = dict(catalogue=catalogue, exprs=exprs, n_catalogue=n_catalogue, neg=neg, cases=cases)
    path = os.path.join(out_dir, "events_cases.json.gz")
    with gzip.GzipFile(path, "wb", mtime=0) as f:
        f.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    print("wrote", path, os.path.getsize(path) // 1024, "KB")


if __name__ == "__main__":
    main()
