"""06 イベント の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 条件式 DSL(字句解析・構文解析・型検査・評価)
- イベント定義の検証(06 §9)
- 週ごとのイベント選択(06 §5。PCG32 の label 5)
- 効果の適用(06 §6)
- 検算用の簡易チームシミュレーション(03 の週の解決を使い、発生頻度・効果の大きさを測る)
- 使い方: python3 -I ref_model_06.py [vectors|validate|sim]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m3  # noqa: E402

STATS = m3.STATS
IX = m3.IDX

# ---------------------------------------------------------------- 定数(06 §2)
K = dict(
    EV_WEEK=2,            # 1週に起こるイベントの上限
    EV_YEAR=22,           # 1年度に起こるイベントの上限(覚醒を除く)
    EV_RUNNER_MONTH=1,    # 1人が1ヶ月に当事者になれる回数
    T_INTERACT=12.0,      # 選択のあるイベントの所要時間(秒)
    T_NOTICE=4.0,         # 選択のないイベント(お知らせ)の所要時間(秒)
    AWAKEN_P={"Blue": 0.10, "Gold": 0.05, "Red": 0.08},   # 条件を満たした週に覚醒する確率
    XP_PER_TARGET_MAX=400,    # 1効果あたり・1人あたりの XP 付与の上限(xu)
    FUNDS_MIN=-1500, FUNDS_MAX=500,
    MOD_WEEKS_MAX=48,
    MOD_CAP=dict(E_mult=(0.80, 1.20), r_add=(-0.06, 0.06), inj_mult=(0.5, 2.0), form_add=(-4, 4), funds_year=(-300, 600), scout_pts=(-4, 8)),
)

# ---------------------------------------------------------------- 条件式 DSL(06 §3)
KEYWORDS = {"and", "or", "not", "true", "false"}
CMP_OPS = ("<=", ">=", "==", "!=", "<", ">")


class DslError(Exception):
    pass


def tokenize(src):
    toks, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c.isspace():
            i += 1
        elif c.isdigit() or (c == "." and i + 1 < n and src[i + 1].isdigit()):
            j = i
            while j < n and (src[j].isdigit() or src[j] == "."):
                j += 1
            toks.append(("num", float(src[i:j]) if "." in src[i:j] else int(src[i:j])))
            i = j
        elif c == "'":
            j = src.index("'", i + 1)
            toks.append(("str", src[i + 1:j]))
            i = j + 1
        elif c.isalpha() or c == "_":
            j = i
            while j < n and (src[j].isalnum() or src[j] in "._"):
                j += 1
            w = src[i:j]
            toks.append(("kw", w) if w in KEYWORDS else ("id", w))
            i = j
        else:
            two = src[i:i + 2]
            if two in CMP_OPS:
                toks.append(("op", two))
                i += 2
            elif c in "<>+-*/(),":
                toks.append(("op", c))
                i += 1
            else:
                raise DslError(f"不正な文字 {c!r}: {src}")
    toks.append(("end", None))
    return toks


class Parser:
    def __init__(self, src):
        self.src, self.toks, self.i = src, tokenize(src), 0

    def peek(self):
        return self.toks[self.i]

    def eat(self, kind=None, val=None):
        t = self.toks[self.i]
        if (kind and t[0] != kind) or (val is not None and t[1] != val):
            raise DslError(f"構文エラー({t}): {self.src}")
        self.i += 1
        return t

    def parse(self):
        e = self.p_or()
        self.eat("end")
        return e

    def p_or(self):
        e = self.p_and()
        while self.peek() == ("kw", "or"):
            self.eat()
            e = ("or", e, self.p_and())
        return e

    def p_and(self):
        e = self.p_not()
        while self.peek() == ("kw", "and"):
            self.eat()
            e = ("and", e, self.p_not())
        return e

    def p_not(self):
        if self.peek() == ("kw", "not"):
            self.eat()
            return ("not", self.p_not())
        return self.p_cmp()

    def p_cmp(self):
        e = self.p_sum()
        t = self.peek()
        if t[0] == "op" and t[1] in CMP_OPS:
            self.eat()
            return ("cmp", t[1], e, self.p_sum())
        return e

    def p_sum(self):
        e = self.p_term()
        while self.peek()[0] == "op" and self.peek()[1] in "+-":
            op = self.eat()[1]
            e = ("bin", op, e, self.p_term())
        return e

    def p_term(self):
        e = self.p_unary()
        while self.peek()[0] == "op" and self.peek()[1] in "*/":
            op = self.eat()[1]
            e = ("bin", op, e, self.p_unary())
        return e

    def p_unary(self):
        if self.peek() == ("op", "-"):
            self.eat()
            return ("neg", self.p_unary())
        return self.p_atom()

    def p_atom(self):
        t = self.peek()
        if t[0] == "num":
            self.eat()
            return ("num", t[1])
        if t[0] == "str":
            self.eat()
            return ("str", t[1])
        if t == ("kw", "true") or t == ("kw", "false"):
            self.eat()
            return ("bool", t[1] == "true")
        if t == ("op", "("):
            self.eat()
            e = self.p_or()
            self.eat("op", ")")
            return e
        if t[0] == "id":
            self.eat()
            if self.peek() == ("op", "("):
                self.eat()
                args = []
                if self.peek() != ("op", ")"):
                    args.append(self.p_or())
                    while self.peek() == ("op", ","):
                        self.eat()
                        args.append(self.p_or())
                self.eat("op", ")")
                return ("call", t[1], args)
            return ("var", t[1])
        raise DslError(f"構文エラー({t}): {self.src}")


def parse(src):
    return Parser(src).parse()


# 変数・関数のカタログ(06 §3.2)
CAL_VARS = {"cal.year", "cal.month", "cal.week", "cal.next_race", "cal.next_race_kind",
            "race.kind", "race.team_rank", "race.expected_rank"}
RUNNER_FIELDS = {"id", "grade", "fatigue", "form", "motivation", "injury", "injury_weeks", "injury_age", "group", "captain",
                 "overseas", "far", "near", "pref", "weeks_in", "bond_avg", "p_rank", "regular", "taper", "races",
                 "section_wins", "brakes", "skills", "gap_avg",
                 "gift.climber", "gift.descender", "gift.ace", "is_speed", "is_long", "is_pack", "is_solo", "is_chase", "is_lead",
                 "race.in_lineup", "race.brake", "race.section_rank", "race.section_win", "race.record"}
TEAM_VARS = {"t.funds", "t.prestige", "t.morale", "t.size", "t.coach_rank", "t.scout_pts", "t.guidance",
             "t.n_g1", "t.n_g2", "t.n_g3", "t.n_g4", "t.n_g3plus", "ob.total", "ob.corp",
             "t.rank.fall", "t.rank.national", "t.rank.newyear",
             "t.last_rank.fall", "t.last_rank.national", "t.last_rank.newyear"}
FAC_IDS = {"dorm", "track", "hill", "gym", "medical"}
STAFF_IDS = {"coach", "trainer", "nutri", "scout"}
COUNTERS = {"hill_weeks", "camps", "camp_overload", "injuries", "injury_free", "peaked_races", "overtakes",
            "lead_sections", "pack_sections", "solo_sections", "big_wins", "ekiden_good"}
FUNCS = {"has_skill": 2, "ctr": 2, "bond": 2, "same_pref_n": 1, "senior_same_pref": 1, "ob_level": 1, "rival_same_hs": 1,
         "min": 2, "max": 2, "abs": 1, "clamp": 3}
ROLES = {"r", "p"}


def is_known_var(name):
    if name in CAL_VARS or name in TEAM_VARS or name in ROLES:
        return True
    if name.startswith("fac.") and name[4:] in FAC_IDS:
        return True
    if name.startswith("staff.") and name[6:] in STAFF_IDS:
        return True
    for role in ("r.", "p."):
        if name.startswith(role):
            f = name[2:]
            if f in RUNNER_FIELDS:
                return True
            if f.startswith("stat.") and f[5:] in STATS:
                return True
    return False


def validate_expr(ast, roles, src=""):
    """未知の変数・関数、引数の数、束縛されていない役割(`p` など)を検出する。"""
    k = ast[0]
    if k in ("num", "str", "bool"):
        return
    if k == "var":
        name = ast[1]
        if not is_known_var(name):
            raise DslError(f"未知の変数 {name}: {src}")
        base = name.split(".")[0]
        if base in ("r", "p") and base not in roles:
            raise DslError(f"束縛されていない役割 {base}: {src}")
        return
    if k == "call":
        if ast[1] not in FUNCS or len(ast[2]) != FUNCS[ast[1]]:
            raise DslError(f"未知の関数または引数の数 {ast[1]}: {src}")
        for a in ast[2]:
            validate_expr(a, roles, src)
        return
    for sub in ast[1:]:
        if isinstance(sub, tuple):
            validate_expr(sub, roles, src)
        elif isinstance(sub, list):
            for s_ in sub:
                validate_expr(s_, roles, src)


def evaluate(ast, ctx):
    """ctx は get(name) と call(fname, args) を持つ。真偽は 1/0 ではなく bool で返す。"""
    k = ast[0]
    if k == "num" or k == "str" or k == "bool":
        return ast[1]
    if k == "var":
        return ctx.get(ast[1])
    if k == "neg":
        return -evaluate(ast[1], ctx)
    if k == "not":
        return not evaluate(ast[1], ctx)
    if k == "and":
        return bool(evaluate(ast[1], ctx)) and bool(evaluate(ast[2], ctx))
    if k == "or":
        return bool(evaluate(ast[1], ctx)) or bool(evaluate(ast[2], ctx))
    if k == "cmp":
        a, b = evaluate(ast[2], ctx), evaluate(ast[3], ctx)
        return {"<": a < b, "<=": a <= b, ">": a > b, ">=": a >= b, "==": a == b, "!=": a != b}[ast[1]]
    if k == "bin":
        a, b = evaluate(ast[2], ctx), evaluate(ast[3], ctx)
        if ast[1] == "+":
            return a + b
        if ast[1] == "-":
            return a - b
        if ast[1] == "*":
            return a * b
        return a / b if b != 0 else 0.0
    if k == "call":
        return ctx.call(ast[1], [evaluate(a, ctx) for a in ast[2]])
    raise DslError(f"評価できない式 {ast}")


_CACHE = {}


def compiled(src, roles=("r",)):
    key = (src, tuple(roles))
    if key not in _CACHE:
        ast = parse(src)
        validate_expr(ast, set(roles), src)
        _CACHE[key] = ast
    return _CACHE[key]


def eval_src(src, ctx, roles=("r",)):
    return evaluate(compiled(src, roles), ctx)


# ---------------------------------------------------------------- イベント定義の検証(06 §9)
def all_events():
    import events_06 as ev6
    out = list(ev6.EVENTS)
    for sid, name, rank, cond in ev6.SKILLS:
        if cond == "false":
            continue
        out.append(dict(
            id=f"ev.awk.{sid}", cat="Awakening", name=f"覚醒: {name}", scope="runner",
            cond=f"not has_skill(r, '{sid}') and ({cond})", p=K["AWAKEN_P"][rank], choices=[dict(id="ok", label=f"『{name}』を覚える", req=None, fx=[dict(op="skill", to="r", id=sid, p=None)])],
            default=["ok"], months=None, weeks=None, phase="normal", cooldown=0, once="runner", max_year=None, priority=95,
            exempt=True, hint=f"{rank}", skill=sid))
    return out


EFFECT_OPS = {"mot", "fat", "xp", "bond", "bondg", "inj", "skill", "funds", "scout", "guide", "mod", "captain", "bench",
              "entry", "ob_role"}
TARGETS = {"r", "p", "all", "g1", "g2", "g3", "g4", "top10", "same_pref", "same_group", "far", "near", "overseas", "captain", "team"}
MOD_KINDS = {"E_mult", "r_add", "inj_mult", "form_add", "funds_year", "scout_pts"}


def validate_catalogue(events=None):
    """06 §9 の検証ルール V-01〜V-10。違反のリストを返す(空なら適合)。"""
    events = events or all_events()
    errs, ids = [], set()
    for e in events:
        roles = ("r", "p") if e["scope"] == "pair" else (("r",) if e["scope"] == "runner" else ())
        eid = e["id"]
        if eid in ids:
            errs.append(f"V-01 {eid}: IDの重複")
        ids.add(eid)
        try:
            compiled(e["cond"], roles)                                     # V-02: 条件式が構文・型に適合
        except DslError as x:
            errs.append(f"V-02 {eid}: {x}")
        if not e["choices"]:
            errs.append(f"V-03 {eid}: 選択肢がない")
        cids = [c["id"] for c in e["choices"]]
        if len(set(cids)) != len(cids):
            errs.append(f"V-03 {eid}: 選択肢IDの重複")
        if not e["default"] or any(d not in cids for d in e["default"]):
            errs.append(f"V-04 {eid}: 既定の選択肢が不正")
        else:
            last = next(c for c in e["choices"] if c["id"] == e["default"][-1])
            if last["req"]:
                errs.append(f"V-04 {eid}: 既定の最後の選択肢に条件がある(必ず選べる選択肢が要る)")
        if not (0.0 < e["p"] <= 1.0):
            errs.append(f"V-05 {eid}: 発生確率が範囲外")
        for c in e["choices"]:
            if c["req"]:
                try:
                    compiled(c["req"], roles)
                except DslError as x:
                    errs.append(f"V-02 {eid}/{c['id']}: {x}")
            xp_total, funds_total = {}, 0
            for fx in c["fx"]:
                if fx["op"] not in EFFECT_OPS:
                    errs.append(f"V-06 {eid}/{c['id']}: 未知の効果 {fx['op']}")
                    continue
                to = fx.get("to")
                if to is not None and to not in TARGETS:
                    errs.append(f"V-06 {eid}/{c['id']}: 未知の対象 {to}")
                if to == "p" and e["scope"] != "pair":
                    errs.append(f"V-06 {eid}/{c['id']}: pair でないイベントで p を使っている")
                if fx["op"] == "bond" and e["scope"] != "pair":
                    errs.append(f"V-06 {eid}/{c['id']}: bond は pair のイベントだけ")
                if fx["op"] == "xp":
                    if fx["stat"] not in STATS and fx["stat"] not in ("weakest", "random"):
                        errs.append(f"V-06 {eid}/{c['id']}: XP の対象能力が不正")
                    if fx["xu"] > K["XP_PER_TARGET_MAX"]:
                        errs.append(f"V-07 {eid}/{c['id']}: XP が上限を超える")
                    xp_total[fx["to"]] = xp_total.get(fx["to"], 0) + fx["xu"]
                if fx["op"] == "mot" and abs(fx["d"]) > 1:
                    errs.append(f"V-07 {eid}/{c['id']}: やる気の変化は ±1 まで")
                if fx["op"] == "fat" and abs(fx["d"]) > 20:
                    errs.append(f"V-07 {eid}/{c['id']}: 疲労の変化は ±20 まで")
                if fx["op"] == "funds":
                    funds_total += fx["d"]
                    if not (K["FUNDS_MIN"] <= fx["d"] <= K["FUNDS_MAX"]):
                        errs.append(f"V-07 {eid}/{c['id']}: 資金の変化が範囲外")
                if fx["op"] == "mod":
                    if fx["kind"] not in MOD_KINDS:
                        errs.append(f"V-08 {eid}/{c['id']}: 未知の補正 {fx['kind']}")
                    lo, hi = K["MOD_CAP"][fx["kind"]]
                    if not (lo <= fx["value"] <= hi) and fx["kind"] not in ("E_mult",):
                        errs.append(f"V-08 {eid}/{c['id']}: 補正の値が範囲外")
                    if fx["kind"] == "E_mult" and not (0.8 <= fx["value"] <= 1.2):
                        errs.append(f"V-08 {eid}/{c['id']}: E_mult の値が範囲外")
                    if fx["weeks"] > K["MOD_WEEKS_MAX"]:
                        errs.append(f"V-08 {eid}/{c['id']}: 補正の期間が長すぎる")
            if any(v > 2 * K["XP_PER_TARGET_MAX"] for v in xp_total.values()):
                errs.append(f"V-07 {eid}/{c['id']}: 1つの選択肢の XP 合計が多すぎる")
        if e["scope"] == "pair" and e.get("once") == "runner":
            errs.append(f"V-09 {eid}: pair に once=runner は使えない")
        if e["phase"] == "race" and e.get("months"):
            errs.append(f"V-10 {eid}: race フェーズのイベントに月の指定は不要")
    return errs


# ---------------------------------------------------------------- 検算用の簡易チームシミュレーション(06 §11)
REGION_PREFS = [("Hokkaido", 1), ("Tohoku", 6), ("Kanto", 7), ("Chubu", 9), ("Kinki", 7), ("Chugoku", 5), ("Shikoku", 4), ("Kyushu", 8)]
REGION_W = {"Hokkaido": .04, "Tohoku": .08, "Kanto": .22, "Chubu": .19, "Kinki": .17, "Chugoku": .05, "Shikoku": .03, "Kyushu": .15}
ADJ = {"Hokkaido": {"Tohoku"}, "Tohoku": {"Hokkaido", "Kanto", "Chubu"}, "Kanto": {"Tohoku", "Chubu"},
       "Chubu": {"Tohoku", "Kanto", "Kinki"}, "Kinki": {"Chubu", "Chugoku", "Shikoku"},
       "Chugoku": {"Kinki", "Shikoku", "Kyushu"}, "Shikoku": {"Kinki", "Chugoku", "Kyushu"}, "Kyushu": {"Chugoku", "Shikoku"}}
PREF_REGION = {}
_pid = 1
for _reg, _n in REGION_PREFS:
    for _ in range(_n):
        PREF_REGION[_pid] = _reg
        _pid += 1
RACE_WEEKS = {25: (1, 8.0, 6), 28: (2, 13.0, 8), 36: (3, 21.5, 10)}    # 年内の週 → (種別, 想定区間距離, 出走人数)
RACE_W = {1: 0.9, 2: 1.0, 3: 1.2}
SKILL_PREFIX = "skill."
LONG_W = {"STA": .35, "PAC": .25, "SPD": .20, "MEN": .10, "KCK": .10}
SEVNUM = {"None": 0, "Minor": 1, "Moderate": 2, "Severe": 3}


def long_p(r):
    s = 0.0                                   # 足す順は D-12(09 §15.3)のとおり STA・PAC・SPD・MEN・KCK。sum() は使わない(3.12 以降は補正付きの和)
    for k, w in LONG_W.items():
        s += w * r["stats"][IX[k]]
    return s


class Sim:
    individuality = True          # 入学生に個性・素養を付ける(10 §4。標準の1種類の練習計画のまま。False なら従来の検算)

    def __init__(self, seed, years=10, warm=3, scenario="mid", policy="default", events_on=True, ev_scale=1.0):
        import random
        import events_06 as ev6
        self.seed, self.policy, self.events_on, self.ev_scale = seed, policy, events_on, ev_scale
        self.rng = random.Random(seed)
        self.events = all_events()
        for e in self.events:
            e["_roles"] = ("r", "p") if e["scope"] == "pair" else (("r",) if e["scope"] == "runner" else ())
        self.skill_rank = {sid: rk for sid, _, rk, _ in ev6.SKILLS}
        self.excl = {}
        for a, b in ev6.EXCLUSIVE:
            self.excl.setdefault(a, set()).add(b)
            self.excl.setdefault(b, set()).add(a)
        self.runners, self.next_id = [], 1
        late = scenario == "late"
        self.team = dict(funds=20000 if late else 1500, prestige=900 if late else 600, morale=50, coach_rank=9 if late else 3,
                         scout_pts=0, guidance=4, fac={k: (5 if late else 2) for k in ("dorm", "track", "hill", "gym", "medical")},
                         staff={k: (5 if late else 2) for k in ("coach", "trainer", "nutri", "scout")},
                         fired={}, mods=[], ob=[], captain_id=None, rank={}, last_rank={"fall": 8, "national": 8, "newyear": 8},
                         year_points=0, year_counts={}, guidance_left=4, bench=set(), forced=set(), late=late)
        for _ in range(72):                                        # 過去の卒業生(OB)
            reg = self.pick_region()
            pref = self.pick_pref(reg)
            self.team["ob"].append(dict(pref=pref, corp=self.rng.random() < 0.35, power=60 + self.rng.gauss(0, 6), role="None", until=0))
        self.fac = {"coach": 2 if not late else 5, "trainer": 2 if not late else 5, "nutri": 2, "dorm": 2, "track": 2, "hill": 2, "gym": 2, "medical": 2, "scout": 2}
        if late:
            self.fac = {k: 5 for k in self.fac}
        self.bonds = {}
        self.abs_week = -warm * 48
        self.warm, self.years = warm, years
        self.metrics = dict(years=years, ev_count={}, ev_year=[], awaken={}, time_s=[], interactive=[], notice=[],
                            xp_event_xu=0, xp_mod_xu=0, xp_train_pts=0, mot_delta=0, fat_delta=0, funds_net=0, weeks_capped=0,
                            runner_years=0, mods_uptime={}, choices={}, drops_cap=0, skills_at_grad=[], skills_by_rank={"Blue": 0, "Gold": 0, "Red": 0},
                            hall=0, grads=0, eligible_weeks={}, cat_count={})
        self._cohort(1)       # 初年度の1年生
        for g in (4, 3, 2):   # 在籍中の上級生も、1年目の入学として扱う(ウォームアップで育つ)
            pass
        self.cohort_years = {}

    # ---- 生成
    def pick_region(self):
        u, acc = self.rng.random() * sum(REGION_W.values()), 0.0
        for reg, w in REGION_W.items():
            acc += w
            if u < acc:
                return reg
        return "Kanto"

    def pick_pref(self, reg):
        prefs = [p for p, rr in PREF_REGION.items() if rr == reg]
        return self.rng.choice(prefs)

    def new_recruit(self, year_idx):
        rng = self.rng
        stats0 = [max(30, min(70, round(rng.gauss(50, 6)))) for _ in range(10)]
        pot0 = [min(100, s + rng.randint(15, 35)) for s in stats0]
        r = m3.new_runner(stats0, pot0, 1, rng.choice(["Early", "Normal", "Normal", "Late"]))
        overseas = rng.random() < 0.03
        pref = 0 if overseas else self.pick_pref(self.pick_region())
        reg = None if overseas else PREF_REGION[pref]
        band = 3 if overseas else (0 if reg == "Kanto" else (1 if reg in ADJ["Kanto"] else 2))
        r.update(id=self.next_id, pref=pref, hs=pref * 5 + rng.randint(0, 4) if pref else 0, overseas=int(overseas), far=int(band >= 2),
                 near=int(band == 0), enroll_week=self.abs_week, ctr={k: 0 for k in COUNTERS}, races=0, section_wins=0, brakes=0,
                 inj_age=99, group=2, captain=0, p_rank=99, regular=0, gap_avg=0.0, grade=1, race=dict(), last_ev_month=-1,
                 ev_hist={}, ev_once=set(), cooldown={})
        self.next_id += 1
        import ref_model_10 as m10                     # 個性・素養(10 §4)。能力は 06 のままで、成長の乗数・絆の効き・素質の上限・取得条件だけが変わる
        m10.attach_sim_recruit(r, self.seed, self.individuality, self.individuality)
        return r

    def _cohort(self, n):
        for _ in range(9):
            r = self.new_recruit(0)
            self.runners.append(r)
            self.init_bonds(r)

    def init_bonds(self, r):
        for o in self.runners:
            if o is r:
                continue
            b = 0
            if r["pref"] and r["pref"] == o["pref"]:
                b += 100
                if r["hs"] == o["hs"]:
                    b += 100
            if b:
                self.bonds[(min(r["id"], o["id"]), max(r["id"], o["id"]))] = b

    def bond(self, a, b):
        return self.bonds.get((min(a["id"], b["id"]), max(a["id"], b["id"])), 0)

    def add_bond(self, a, b, d):
        k = (min(a["id"], b["id"]), max(a["id"], b["id"]))
        v = max(0, min(1000, self.bonds.get(k, 0) + d))
        if v:
            self.bonds[k] = v
        else:
            self.bonds.pop(k, None)

    # ---- 派生値
    def refresh(self):
        healthy = sorted(self.runners, key=lambda r: -long_p(r))
        for i, r in enumerate(healthy):
            r["p_rank"] = i + 1
            r["group"] = 0 if i < 10 else (1 if i < 20 else 2)
            r["regular"] = int(i < 10 and r["inj"][0] == "None")
            r["gap_avg"] = sum(p - s for p, s in zip(r["pot"], r["stats"])) / 10.0
        for r in self.runners:
            peers = [o for o in self.runners if o is not r and o["group"] == r["group"]]
            r["bond_avg"] = (sum(self.bond(r, o) for o in peers) / len(peers)) if peers else 0.0

    def cal(self):
        yw = self.abs_week % 48
        nxt = min(((w - yw) % 48, k) for w, (k, _, _) in RACE_WEEKS.items())
        return yw, nxt

    def ob_level(self, r):
        n = sum(1 for o in self.team["ob"] if o["pref"] == r["pref"])
        return min(5, int(math.sqrt(n)))

    def count_ob(self):
        return len(self.team["ob"]), sum(1 for o in self.team["ob"] if o["corp"])

    # ---- 条件式の文脈
    def ctx(self, r=None, p=None, race=None):
        return Ctx(self, r, p, race)


class Ctx:
    """条件式の文脈。変数は名前で引き、関数は call で評価する(06 §3.2)。"""

    def __init__(self, sim, r=None, p=None, race=None):
        self.s, self.r, self.p, self.race = sim, r, p, race or {}

    def role(self, name):
        return {"r": self.r, "p": self.p}[name]

    def get(self, name):
        s, t = self.s, self.s.team
        yw, nxt = s.cal()
        if name in ("r", "p"):
            return name
        if name == "cal.year":
            return s.abs_week // 48
        if name == "cal.month":
            return yw // 4
        if name == "cal.week":
            return yw % 4
        if name == "cal.next_race":
            return nxt[0]
        if name == "cal.next_race_kind":
            return nxt[1]
        if name == "race.kind":
            return self.race.get("kind", 0)
        if name == "race.team_rank":
            return self.race.get("team_rank", 99)
        if name == "race.expected_rank":
            return self.race.get("expected_rank", 99)
        if name.startswith("fac."):
            return s.fac[name[4:]]
        if name.startswith("staff."):
            return s.fac[name[6:]]
        if name.startswith("t.rank."):
            return t["rank"].get(name[7:], 99)
        if name.startswith("t.last_rank."):
            return t["last_rank"].get(name[12:], 99)
        if name == "t.funds":
            return t["funds"]
        if name == "t.prestige":
            return t["prestige"]
        if name == "t.morale":
            return t["morale"]
        if name == "t.size":
            return len(s.runners)
        if name == "t.coach_rank":
            return t["coach_rank"]
        if name == "t.scout_pts":
            return t["scout_pts"] + s.mod_sum("scout_pts")
        if name == "t.guidance":
            return t["guidance_left"]
        if name.startswith("t.n_g"):
            if name == "t.n_g3plus":
                return sum(1 for r in s.runners if r["grade"] >= 3)
            return sum(1 for r in s.runners if r["grade"] == int(name[5:]))
        if name == "ob.total":
            return s.count_ob()[0]
        if name == "ob.corp":
            return s.count_ob()[1]
        base, field = name[0], name[2:]
        who = self.role(base)
        if field.startswith("stat."):
            return who["stats"][IX[field[5:]]]
        if field.startswith("race."):
            return who["race"].get(field[5:], 0)
        if field == "id":
            return who["id"]
        if field == "injury":
            return SEVNUM[who["inj"][0]]
        if field == "injury_weeks":
            return who["inj"][1]
        if field == "injury_age":
            return who["inj_age"]
        if field == "weeks_in":
            return s.abs_week - who["enroll_week"]
        if field.startswith("gift."):                                   # 素養(10 §2)。無ければ 0
            return 1 if field[5:] in [g.lower() for g in who.get("gifts", ())] else 0
        if field.startswith("is_"):                                     # 個性(10 §2)。無ければ 0
            sty = who.get("style")
            if not sty:
                return 0
            key = {"is_speed": ("form", "Speed"), "is_long": ("form", "Long"), "is_pack": ("run", "Pack"), "is_solo": ("run", "Solo"),
                   "is_chase": ("flow", "Chase"), "is_lead": ("flow", "Lead")}[field]
            return 1 if sty[key[0]] == key[1] else 0
        if field == "skills":
            return len(who["skills"])
        if field == "taper":
            return who["taper_weeks"]
        if field in ("grade", "fatigue", "form", "motivation", "group", "captain", "overseas", "far", "near", "pref", "bond_avg",
                     "p_rank", "regular", "races", "section_wins", "brakes", "gap_avg"):
            return who[field]
        raise DslError(f"未実装の変数 {name}")

    def call(self, f, a):
        s, t = self.s, self.s.team
        if f == "has_skill":
            return (SKILL_PREFIX + a[1]) in self.role(a[0])["skills"]
        if f == "ctr":
            return self.role(a[0])["ctr"][a[1]]
        if f == "bond":
            return s.bond(self.role(a[0]), self.role(a[1]))
        if f == "same_pref_n":
            r = self.role(a[0])
            return sum(1 for o in s.runners if o is not r and r["pref"] and o["pref"] == r["pref"])
        if f == "senior_same_pref":
            r = self.role(a[0])
            return any(o is not r and r["pref"] and o["pref"] == r["pref"] and o["grade"] >= r["grade"] + 1 for o in s.runners)
        if f == "ob_level":
            return s.ob_level(self.role(a[0]))
        if f == "rival_same_hs":
            r = self.role(a[0])
            h = m3.derive(s.seed, 7, r["id"], s.abs_week // 4)
            return (h % 1000) / 1000.0 < 0.40                           # 検算用の近似(出場校の先発に同じ出身高校がいる確率)
        if f == "min":
            return min(a)
        if f == "max":
            return max(a)
        if f == "abs":
            return abs(a[0])
        if f == "clamp":
            return max(a[1], min(a[2], a[0]))
        raise DslError(f"未実装の関数 {f}")


def _mod_sum(self, kind):
    return sum(m["value"] for m in self.team["mods"] if m["kind"] == kind and m["to"] == "team" and m["until"] > self.abs_week)


Sim.mod_sum = _mod_sum


# ---------------------------------------------------------------- 効果の適用(06 §6)
def _levelup(r, i):
    """03 §6 手順3(ApplyXp 用): XP がたまっていれば能力を上げる。"""
    n = 0
    while n < 3 and r["stats"][i] < r["pot"][i]:
        t = m3.round_half_up(10 * (m3.K["T_BASE"] + m3.K["T_SLOPE"] * r["stats"][i]))
        if r["xp"][i] < t:
            break
        r["xp"][i] -= t
        r["stats"][i] += 1
        n += 1
    if r["stats"][i] >= r["pot"][i]:
        r["xp"][i] = 0


def apply_xp(sim, r, i, xu, kind="event"):
    before = r["stats"][i]
    r["xp"][i] += xu
    _levelup(r, i)
    sim.metrics["xp_event_xu" if kind == "event" else "xp_mod_xu"] += xu
    return r["stats"][i] - before


def _targets(sim, to, r, p):
    R = sim.runners
    if to == "r":
        return [r]
    if to == "p":
        return [p]
    if to == "all":
        return list(R)
    if to in ("g1", "g2", "g3", "g4"):
        return [x for x in R if x["grade"] == int(to[1])]
    if to == "top10":
        return [x for x in R if x["p_rank"] <= 10]
    if to == "same_pref":
        return [x for x in R if r["pref"] and x["pref"] == r["pref"]] or [r]
    if to == "same_group":
        return [x for x in R if x["group"] == r["group"]]
    if to == "far":
        return [x for x in R if x["far"]]
    if to == "near":
        return [x for x in R if x["near"]]
    if to == "overseas":
        return [x for x in R if x["overseas"]]
    if to == "captain":
        return [x for x in R if x["captain"]]
    return []


def apply_effects(sim, ev, choice, r, p, stream):
    m, t = sim.metrics, sim.team
    for fx in choice["fx"]:
        op = fx["op"]
        prob = fx.get("p")
        if op in ("mot", "fat", "xp") and fx["to"] not in ("r", "p"):
            targets = _targets(sim, fx["to"], r, p)
            draws = [stream.uniform() for _ in targets] if prob is not None else None
            targets = [x for i, x in enumerate(targets) if draws is None or draws[i] < prob]
        elif op in ("mot", "fat", "xp"):
            if prob is not None and not (stream.uniform() < prob):
                continue
            targets = _targets(sim, fx["to"], r, p)
        if op == "mot":
            for x in targets:
                new = max(1, min(5, x["motivation"] + fx["d"]))
                m["mot_delta"] += new - x["motivation"]
                x["motivation"] = new
        elif op == "fat":
            for x in targets:
                new = max(0, min(100, x["fatigue"] + fx["d"]))
                m["fat_delta"] += new - x["fatigue"]
                x["fatigue"] = new
        elif op == "xp":
            for x in targets:
                st = fx["stat"]
                if st == "weakest":
                    i = min(range(10), key=lambda k: x["stats"][k])
                elif st == "random":
                    i = stream.randint(0, 9)
                else:
                    i = IX[st]
                apply_xp(sim, x, i, fx["xu"])
        elif op == "bond":
            sim.add_bond(r, p, fx["d"])
        elif op == "bondg":
            grp = _targets(sim, fx["to"], r, p)
            for a in range(len(grp)):
                for b in range(a + 1, len(grp)):
                    sim.add_bond(grp[a], grp[b], fx["d"])
        elif op == "inj":
            if r["inj"][0] == "None":
                dur = {"Minor": (1, 2), "Moderate": (4, 8), "Severe": (12, 24)}[fx["sev"]]
                r["inj"] = (fx["sev"], stream.randint(*dur))
                r["inj_age"] = 0
        elif op == "skill":
            if prob is not None and not (stream.uniform() < prob):
                continue
            sid = SKILL_PREFIX + fx["id"]
            if sid not in r["skills"] and not any((SKILL_PREFIX + o) in r["skills"] for o in sim.excl.get(fx["id"], ())) and len(r["skills"]) < 8:
                r["skills"].add(sid)
                rk = sim.skill_rank[fx["id"]]
                m["skills_by_rank"][rk] += 1
                m["awaken"][fx["id"]] = m["awaken"].get(fx["id"], 0) + 1
        elif op == "funds":
            new = max(0, t["funds"] + fx["d"])
            m["funds_net"] += new - t["funds"]
            t["funds"] = new
        elif op == "scout":
            t["scout_pts"] += fx["d"]
        elif op == "guide":
            t["guidance_left"] = max(0, t["guidance_left"] + fx["d"])
        elif op == "mod":
            tgt = "team" if fx["to"] == "team" else r["id"]
            t["mods"] = [x for x in t["mods"] if not (x["kind"] == fx["kind"] and x["to"] == tgt and x["src"] == ev["id"])]
            t["mods"].append(dict(kind=fx["kind"], to=tgt, value=fx["value"], until=sim.abs_week + fx["weeks"], src=ev["id"]))
        elif op == "captain":
            pool = [x for x in sim.runners if x["grade"] >= 3] or sim.runners
            key = {"top_bond": lambda x: x["bond_avg"], "top_p": long_p, "top_men": lambda x: x["stats"][IX["MEN"]]}[fx["select"]]
            for x in sim.runners:
                x["captain"] = 0
            c = max(pool, key=key)
            c["captain"] = 1
        elif op == "bench":
            t["bench"].add(r["id"])
        elif op == "entry":
            t["forced"].add(r["id"])
        elif op == "ob_role":
            mods = {"Coach": ("E_mult", 1.06), "ScoutHelper": ("scout_pts", 4), "Sponsor": ("funds_year", 200)}[fx["role"]]
            cands = [o for o in t["ob"] if o["role"] == "None" and (o["corp"] or fx["role"] != "Coach")]
            if cands:
                o = max(cands, key=lambda o: o["power"])
                o["role"], o["until"] = fx["role"], sim.abs_week + fx["weeks"]
                t["mods"].append(dict(kind=mods[0], to="team", value=mods[1], until=sim.abs_week + fx["weeks"], src=ev["id"]))


def choose(sim, ev, ctx, stream):
    valid = [c for c in ev["choices"] if not c["req"] or eval_src(c["req"], ctx, ev["_roles"])]
    if sim.policy == "uniform":
        return valid[min(len(valid) - 1, int(stream.uniform() * len(valid)))]
    for d in ev["default"]:
        c = next(c for c in ev["choices"] if c["id"] == d)
        if not c["req"] or eval_src(c["req"], ctx, ev["_roles"]):
            return c
    return valid[0]


# ---------------------------------------------------------------- イベントの選択(06 §5)
def event_phase(sim, race=None):
    t, m = sim.team, sim.metrics
    yw = sim.abs_week % 48
    month, wk = divmod(yw, 4)
    year = sim.abs_week // 48
    stream = m3.Pcg32(m3.derive(sim.seed, 5, sim.abs_week, 0))
    fired = []
    used = set()                                      # 今週すでに当事者になった選手(1人1週に1件まで)
    order = sorted(sim.events, key=lambda e: (-e["priority"], e["id"]))
    this_month = year * 12 + month

    def blocked(r, e):
        """1人1ヶ月の制限(exempt でない通常のイベントと覚醒に適用)と、今週すでに当事者になった選手を除く。"""
        if r["id"] in used:
            return True
        return (not e["exempt"] or e["cat"] == "Awakening") and r["last_ev_month"] == this_month
    for e in order:
        if len(fired) >= K["EV_WEEK"]:
            m["drops_cap"] += 1
            break
        if e["phase"] == "race" and not race:
            continue
        if e["months"] is not None and month not in e["months"]:
            continue
        if e["weeks"] is not None and wk not in e["weeks"]:
            continue
        ycount = t["year_counts"].get(e["id"], 0)
        if e["max_year"] is not None and ycount >= e["max_year"]:
            continue
        if not e["exempt"] and t["year_counts"].get("_total", 0) >= K["EV_YEAR"]:
            continue
        # 当事者の列挙
        subjects = []
        if e["scope"] == "team":
            last = t["fired"].get(e["id"], {}).get("last", -9999)
            if sim.abs_week - last >= e["cooldown"]:
                if eval_src(e["cond"], Ctx(sim, None, None, race), ()) if e["cond"] else True:
                    subjects.append((None, None))
        elif e["scope"] == "runner":
            for r in sim.runners:
                if blocked(r, e):
                    continue
                if e["once"] == "runner" and e["id"] in r["ev_once"]:
                    continue
                if sim.abs_week - r["cooldown"].get(e["id"], -9999) < e["cooldown"]:
                    continue
                if eval_src(e["cond"], Ctx(sim, r, None, race), ("r",)):
                    subjects.append((r, None))
        else:
            for r in sim.runners:
                if blocked(r, e):
                    continue
                for pp in sim.runners:
                    if pp is r or blocked(pp, e):
                        continue
                    key = (e["id"], min(r["id"], pp["id"]), max(r["id"], pp["id"]))
                    if sim.abs_week - t.setdefault("pair_cd", {}).get(key, -9999) < e["cooldown"]:
                        continue
                    if eval_src(e["cond"], Ctx(sim, r, pp, race), ("r", "p")):
                        subjects.append((r, pp))
        if not subjects:
            continue
        m["eligible_weeks"][e["id"]] = m["eligible_weeks"].get(e["id"], 0) + 1
        u1, u2 = stream.uniform(), stream.uniform()
        if not (u1 < min(1.0, e["p"] * sim.ev_scale)):
            continue
        r, pp = subjects[min(len(subjects) - 1, int(u2 * len(subjects)))]
        fired.append((e, r, pp))
        for who in (r, pp):
            if who is not None:
                used.add(who["id"])
    for k, (e, r, pp) in enumerate(fired):
        estream = m3.Pcg32(m3.derive(sim.seed, 5, sim.abs_week, 1000 + k))
        ctx = Ctx(sim, r, pp, race)
        choice = choose(sim, e, ctx, estream)
        apply_effects(sim, e, choice, r, pp, estream)
        t["fired"].setdefault(e["id"], dict(count=0, last=0))
        t["fired"][e["id"]]["count"] += 1
        t["fired"][e["id"]]["last"] = sim.abs_week
        t["year_counts"][e["id"]] = t["year_counts"].get(e["id"], 0) + 1
        if not e["exempt"]:
            t["year_counts"]["_total"] = t["year_counts"].get("_total", 0) + 1
        for who in (r, pp):
            if who is not None:
                who["last_ev_month"] = year * 12 + month
                who["cooldown"][e["id"]] = sim.abs_week
                if e["once"] == "runner":
                    who["ev_once"].add(e["id"])
        if e["scope"] == "pair":
            t.setdefault("pair_cd", {})[(e["id"], min(r["id"], pp["id"]), max(r["id"], pp["id"]))] = sim.abs_week
        if sim.abs_week >= 0:
            interactive = len(e["choices"]) >= 2
            m["ev_count"][e["id"]] = m["ev_count"].get(e["id"], 0) + 1
            m["cat_count"][e["cat"]] = m["cat_count"].get(e["cat"], 0) + 1
            m["choices"][(e["id"], choice["id"])] = m["choices"].get((e["id"], choice["id"]), 0) + 1
            if e["cat"] != "Awakening":
                m["interactive" if interactive else "notice"].append(year)
    return fired


# ---------------------------------------------------------------- 週の進行(検算用)
def synthetic_race(sim, yw):
    """大会の結果を乱数で作る(04 の本番式ではなく、イベントの条件に必要な事象だけを再現する)。"""
    kind, km, n = RACE_WEEKS[yw]
    rng, t, m = sim.rng, sim.team, sim.metrics
    u = rng.random()
    team_rank = 1 if u < 0.12 else (rng.randint(2, 3) if u < 0.30 else (rng.randint(4, 10) if u < 0.75 else rng.randint(11, 20)))
    expected = rng.randint(4, 9)
    pool = [r for r in sorted(sim.runners, key=lambda r: -long_p(r)) if r["inj"][0] == "None" and r["id"] not in t["bench"]]
    lineup = pool[:n]
    forced = [r for r in sim.runners if r["id"] in t["forced"] and r not in lineup and r["inj"][0] == "None"]
    in_ids = {r["id"] for r in lineup}
    for r in sim.runners:
        r["race"] = dict(in_lineup=int(r["id"] in in_ids), brake=0, section_rank=0, section_win=0, record=0)
    for r in lineup:
        con, men = r["stats"][IX["CON"]], r["stats"][IX["MEN"]]
        pr = r["p_rank"]
        pb = 0.02 * (1.5 - con / 100) * max(0.5, min(1.5, 1.5 - men / 100)) * RACE_W[kind]
        if SKILL_PREFIX + "brake_prone" in r["skills"]:
            pb *= 1.5
        if SKILL_PREFIX + "big_stage" in r["skills"]:
            pb *= 0.7
        brake = rng.random() < pb
        sr = 1 + int((rng.random() ** 1.6) * 18 * (0.6 + 0.04 * pr))
        sr = min(sr, 20)
        rc = r["race"]
        rc.update(brake=int(brake), section_rank=sr, section_win=int(sr == 1), record=int(rng.random() < 0.001))
        r["races"] += 1
        r["brakes"] += int(brake)
        r["section_wins"] += int(sr == 1)
        c = r["ctr"]
        u2 = rng.random()
        if u2 < 0.06:
            c["lead_sections"] += 1
        elif u2 < 0.51:
            c["pack_sections"] += 1
        elif u2 < 0.86:
            c["solo_sections"] += 1
        if rng.random() < 0.10:
            c["overtakes"] += 1
        if team_rank <= 3 and sr <= 5:
            c["big_wins"] += 1
        if r["form"] >= 1 and team_rank <= 5 and sr <= 3:
            c["ekiden_good"] += 1
        if r["taper_weeks"] >= 2 and r["form"] >= 1:
            c["peaked_races"] += 1
        r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(km * m3.K["RACE_FATIGUE_PER_KM"]))
        # 04 §10 のやる気
        d = 0
        d += 1 if sr == 1 else 0
        d += 1 if team_rank == 1 else 0
        d -= 1 if brake else 0
        d += 1 if team_rank <= expected - 3 else (-1 if team_rank >= expected + 3 else 0)
        r["motivation"] = max(1, min(5, r["motivation"] + max(-1, min(1, d)) if False else r["motivation"] + d))
        r["motivation"] = max(1, min(5, r["motivation"]))
    name = {1: "fall", 2: "national", 3: "newyear"}[kind]
    t["rank"][name] = team_rank
    t["year_points"] += 10 if team_rank == 1 else 6 if team_rank == 2 else 4 if team_rank == 3 else 2 if team_rank <= 6 else 1 if team_rank <= 10 else 0
    t["best_rank_year"] = min(t.get("best_rank_year", 99), team_rank)
    t["bench"].clear() if kind == 3 else None
    return dict(kind=kind, team_rank=team_rank, expected_rank=expected)


def _clamp_mod(kind, v):
    lo, hi = K["MOD_CAP"][kind]
    return max(lo, min(hi, v))


def step(sim):
    t, m = sim.team, sim.metrics
    yw = sim.abs_week % 48
    month, wk = divmod(yw, 4)
    year = sim.abs_week // 48
    active = sim.abs_week >= 0
    rng = sim.rng
    if yw == 0:
        if sim.abs_week > -sim.warm * 48:
            for _ in range(9):
                r = sim.new_recruit(year)
                r["enroll_year"] = year
                sim.runners.append(r)
                sim.init_bonds(r)
        for r in sim.runners:
            r["grade"] = year - r.setdefault("enroll_year", year) + 1
        t["last_rank"] = {k: t["rank"].get(k, 8) for k in ("fall", "national", "newyear")}
        t["rank"], t["year_counts"], t["year_points"], t["best_rank_year"] = {}, {}, 0, 99
        if active:
            m["runner_years"] += len(sim.runners)
            m["ev_year"].append(0)
    if wk == 0:
        t["guidance_left"] = 4
    t["mods"] = [x for x in t["mods"] if x["until"] > sim.abs_week]
    for o in t["ob"]:
        if o["role"] != "None" and o["until"] <= sim.abs_week:
            o["role"] = "None"
    sim.refresh()
    menu, camp = m3.week_menu(yw, races={k: v[1] for k, v in RACE_WEEKS.items()})
    em = _clamp_mod("E_mult", 1.0 * __import__("functools").reduce(lambda a, x: a * x["value"], [x for x in t["mods"] if x["kind"] == "E_mult" and x["to"] == "team"], 1.0))
    ra_team = _clamp_mod("r_add", sum(x["value"] for x in t["mods"] if x["kind"] == "r_add" and x["to"] == "team"))
    for r in sim.runners:
        f0 = r["fatigue"]
        was_inj = r["inj"][0] != "None"
        rep = m3.resolve_week(r, menu, sim.fac, month, sim.seed, r["id"], sim.abs_week, camp=camp, safety=True, bond_avg=r["bond_avg"])
        used = rep["menu"]
        if active:
            m["xp_train_pts"] += rep["levelups"]
        # 個人の補正
        e_r = em * __import__("functools").reduce(lambda a, x: a * x["value"], [x for x in t["mods"] if x["kind"] == "E_mult" and x["to"] == r["id"]], 1.0)
        e_r = _clamp_mod("E_mult", e_r)
        if abs(e_r - 1.0) > 1e-9:
            for i, add in enumerate(rep["xp_add"]):
                extra = m3.round_half_up(add * (e_r - 1.0))
                if extra > 0:
                    apply_xp(sim, r, i, extra, "mod")
                elif extra < 0:
                    r["xp"][i] = max(0, r["xp"][i] + extra)
        ra = _clamp_mod("r_add", ra_team + sum(x["value"] for x in t["mods"] if x["kind"] == "r_add" and x["to"] == r["id"]))
        if abs(ra) > 1e-9:
            r["fatigue"] = max(0, min(100, r["fatigue"] - m3.round_half_up(ra * f0)))
        im = _clamp_mod("inj_mult", __import__("functools").reduce(lambda a, x: a * x["value"], [x for x in t["mods"] if x["kind"] == "inj_mult" and x["to"] in ("team", r["id"])], 1.0))
        if active and im != 1.0:
            m["mods_uptime"]["inj_mult"] = m["mods_uptime"].get("inj_mult", 0) + 1
        p0 = rep.get("injury_p")
        if p0 and "injury" not in rep and not was_inj and im > 1.0 and rng.random() < p0 * (im - 1.0):
            r["inj"] = ("Minor", rng.randint(1, 2))
            rep["injury"] = ("Minor", r["inj"][1])
        if "injury" in rep and im < 1.0 and rng.random() < (1.0 - im):
            r["inj"] = ("None", 0)
            rep.pop("injury")
        # 追跡用の値
        if "injury" in rep:
            r["inj_age"] = 0
            r["ctr"]["injuries"] += 1
            r["ctr"]["injury_free"] = 0
            if rep["injury"][0] == "Severe":
                r["motivation"] = max(1, r["motivation"] - 1)           # 03 M2
        elif r["inj"][0] != "None":
            r["inj_age"] += 1
            r["ctr"]["injury_free"] = 0
        else:
            r["inj_age"] = 99
            r["ctr"]["injury_free"] += 1
        if "ks.hill" in used[:2] and not was_inj:
            r["ctr"]["hill_weeks"] += 1
        if camp:
            r["camp_maxF"] = max(r.get("camp_maxF", 0), r["fatigue"])
        r["f85_run"] = r.get("f85_run", 0) + 1 if r["fatigue"] >= 85 else 0
        if r["f85_run"] >= 2:
            r["m1_pending"] = True
        r["month_gain"] = r.get("month_gain", 0) + rep["levelups"]
    # 絆(03 §7.1)
    gr = {r["id"]: r for r in sim.runners}
    for i, a in enumerate(sim.runners):
        for b in sim.runners[i + 1:]:
            if a["group"] == b["group"] and a["inj"][0] == "None" and b["inj"][0] == "None":
                d = 10 + (1 if a["pref"] and a["pref"] == b["pref"] else 0) + (1 if a["hs"] and a["hs"] == b["hs"] and a["hs"] else 0)
                sim.add_bond(a, b, d)
    race = None
    if yw in RACE_WEEKS:
        race = synthetic_race(sim, yw)
    if sim.events_on and active:
        fired = event_phase(sim, race)
        if m["ev_year"]:
            m["ev_year"][-1] += sum(1 for e, _, _ in fired if e["cat"] != "Awakening")
    for r in sim.runners:
        r["race"] = {}
    # 月次(03 §7)
    if wk == 3:
        for k in list(sim.bonds):
            a, b = gr.get(k[0]), gr.get(k[1])
            if a and b and a["group"] != b["group"]:
                sim.add_bond(a, b, -10)
        avg = sum(r["motivation"] for r in sim.runners) / max(1, len(sim.runners))
        bonus = 10 if t["best_rank_year"] == 1 else (5 if t["best_rank_year"] <= 3 else 0)
        t["morale"] = max(0, min(100, m3.round_half_up(25 * (avg - 1)) + bonus))
        for r in sim.runners:
            if r.pop("m1_pending", False):
                r["motivation"] = max(1, r["motivation"] - 1)
            if t["morale"] >= 70 and r["motivation"] <= 2:
                r["motivation"] += 1
            if t["morale"] <= 30 and r["motivation"] >= 4:
                r["motivation"] -= 1
            if r.get("month_gain", 0) >= 2 and r["motivation"] <= 3:
                r["motivation"] += 1
            r["month_gain"] = 0
        if month == 4:
            for r in sim.runners:
                r["ctr"]["camps"] += 1
                if r.get("camp_maxF", 0) >= 85:
                    r["ctr"]["camp_overload"] += 1
                r["camp_maxF"] = 0
    # 年度末(03・05)
    if yw == 47:
        grads = [r for r in sim.runners if r["grade"] >= 4]
        for r in grads:
            P = sum(r["stats"]) / 10.0
            t["ob"].append(dict(pref=r["pref"], corp=rng.random() < max(0.0, min(0.9, (P - 62) / 14)), power=P, role="None", until=0))
            if active:
                m["grads"] += 1
                m["skills_at_grad"].append(len(r["skills"]))
                m["hall"] += int(r["section_wins"] >= 3 or r["stats"][IX["CLB"]] + r["stats"][IX["STA"]] >= 170)
        sim.runners = [r for r in sim.runners if r["grade"] < 4]
        for k in list(sim.bonds):
            if k[0] not in {r["id"] for r in sim.runners} or k[1] not in {r["id"] for r in sim.runners}:
                sim.bonds.pop(k)
        fy = sum(x["value"] for x in t["mods"] if x["kind"] == "funds_year" and x["to"] == "team")
        t["funds"] += 700 + fy
        if not t["late"]:
            t["funds"] = min(t["funds"], 1100)
        if active:
            m["funds_net"] += fy
    sim.abs_week += 1


def run_sim(seed, years=10, warm=3, scenario="mid", policy="default", events_on=True, ev_scale=1.0):
    sim = Sim(seed, years, warm, scenario, policy, events_on, ev_scale)
    for _ in range((warm + years) * 48):
        step(sim)
    return sim


def sim_report(n=6, years=10, scenario="mid", policy="default", quiet=False, ev_scale=1.0):
    import statistics as st
    sims = [run_sim(1000 + k, years=years, scenario=scenario, policy=policy, ev_scale=ev_scale) for k in range(n)]
    agg = lambda f: [f(s.metrics) for s in sims]
    Y = years
    per_year = {}
    for s in sims:
        for k, v in s.metrics["ev_count"].items():
            per_year[k] = per_year.get(k, 0) + v
    awk = {k: v / (n * Y) for k, v in per_year.items() if k.startswith("ev.awk.")}
    base = {k: v / (n * Y) for k, v in per_year.items() if not k.startswith("ev.awk.")}
    tot_ev = sum(base.values())
    inter = sum(len(s.metrics["interactive"]) for s in sims) / (n * Y)
    notice = sum(len(s.metrics["notice"]) for s in sims) / (n * Y)
    awk_n = sum(awk.values())
    ry = sum(s.metrics["runner_years"] for s in sims)
    res = dict(total=tot_ev, interactive=inter, notice=notice, awakenings=awk_n,
               time=K["T_INTERACT"] * inter + K["T_NOTICE"] * (notice + awk_n),
               per_event=base, per_awk=awk,
               ev_year_sd=st.pstdev([x for s in sims for x in s.metrics["ev_year"]]),
               ev_year_max=max(x for s in sims for x in s.metrics["ev_year"]),
               xp_event_pts=sum(s.metrics["xp_event_xu"] for s in sims) / ry / 560.0,
               xp_mod_pts=sum(s.metrics["xp_mod_xu"] for s in sims) / ry / 560.0,
               xp_train_pts=sum(s.metrics["xp_train_pts"] for s in sims) / ry,
               mot=sum(s.metrics["mot_delta"] for s in sims) / ry,
               fat=sum(s.metrics["fat_delta"] for s in sims) / ry,
               funds=sum(s.metrics["funds_net"] for s in sims) / (n * Y),
               drops=sum(s.metrics["drops_cap"] for s in sims) / (n * Y),
               skills_grad=st.mean(x for s in sims for x in s.metrics["skills_at_grad"]),
               skills_rank={k: sum(s.metrics["skills_by_rank"][k] for s in sims) / (n * Y) for k in ("Blue", "Gold", "Red")},
               inj_uptime=sum(s.metrics["mods_uptime"].get("inj_mult", 0) for s in sims) / ry,
               hall=sum(s.metrics["hall"] for s in sims) / (n * Y),
               choices={}, eligible={})
    for s in sims:
        for k, v in s.metrics["choices"].items():
            res["choices"][k] = res["choices"].get(k, 0) + v / (n * Y)
        for k, v in s.metrics["eligible_weeks"].items():
            res["eligible"][k] = res["eligible"].get(k, 0) + v / (n * Y)
    res["cat_count"] = {}
    for s in sims:
        for k, v in s.metrics["cat_count"].items():
            res["cat_count"][k] = res["cat_count"].get(k, 0) + v / (n * Y)
    if not quiet:
        print(f"== {scenario} / 選択方針={policy} / {n}回 × {Y}年度(ウォームアップ除く)")
        print(f"1年度あたり: イベント {tot_ev:.1f}(選択あり {inter:.1f} / お知らせ {notice:.1f})+ 覚醒 {awk_n:.1f}  → 所要時間 {res['time']:.0f} 秒(標準偏差 {res['ev_year_sd']:.1f} 件、最大 {res['ev_year_max']} 件/年)")
        print(f"週の上限で落ちた回数/年度: {res['drops']:.2f}")
        print("種別: " + ", ".join(f"{k} {v:.1f}" for k, v in sorted(res["cat_count"].items())))
        print(f"特殊能力: 卒業時の平均所持数 {res['skills_grad']:.2f} / 1年度あたりの取得 " + ", ".join(f"{k} {v:.2f}" for k, v in res["skills_rank"].items()))
        print(f"効果の大きさ(選手1人・1年度あたり): イベントの XP {res['xp_event_pts']:.2f} 点分 + 補正の XP {res['xp_mod_pts']:.2f} 点分(練習の成長 {res['xp_train_pts']:.1f} 点に対し {100 * (res['xp_event_pts'] + res['xp_mod_pts']) / res['xp_train_pts']:.1f}%)、やる気 {res['mot']:+.2f}、疲労 {res['fat']:+.1f}、資金(チーム・年度あたり){res['funds']:+.0f} 万円")
    return res


def tune_table(res):
    rows = sorted(res["per_event"].items(), key=lambda x: -x[1])
    for k, v in rows:
        print(f"  {k:32s} {v:5.2f}/年  (該当する週 {res['eligible'].get(k, 0):5.1f}/年)")
    for k, v in sorted(res["per_awk"].items(), key=lambda x: -x[1]):
        print(f"  {k:32s} {v:5.2f}/年")


# ---------------------------------------------------------------- 仕様書用の出力
TO_JA = {"r": "本人", "p": "相手", "all": "全員", "g1": "1年生", "g2": "2年生", "g3": "3年生", "g4": "4年生", "top10": "主力(力の上位10人)",
         "same_pref": "同郷の仲間", "same_group": "同じグループ", "far": "遠方出身", "near": "近郊出身", "overseas": "留学生", "captain": "主将", "team": "チーム"}
MOD_JA = {"E_mult": "練習の効率", "r_add": "疲労の回復率", "inj_mult": "故障率", "form_add": "次の大会の調子", "funds_year": "年間収入", "scout_pts": "スカウトポイント"}
STAT_JA = {"weakest": "最も低い能力", "random": "ランダムな能力"}


def fx_ja(fx):
    op = fx["op"]
    pr = f"({int(fx['p'] * 100)}%)" if fx.get("p") else ""
    to = TO_JA.get(fx.get("to"), "")
    if op == "mot":
        return f"やる気{fx['d']:+d}({to}){pr}"
    if op == "fat":
        return f"疲労{fx['d']:+d}({to}){pr}"
    if op == "xp":
        return f"{STAT_JA.get(fx['stat'], fx['stat'])}に XP {fx['xu']:+d}xu({to}){pr}"
    if op == "bond":
        return f"2人の絆{fx['d']:+d}"
    if op == "bondg":
        return f"絆{fx['d']:+d}({to}の全員の間)"
    if op == "inj":
        return f"{fx['sev']} の故障"
    if op == "skill":
        return f"特殊能力『{fx['id']}』{pr}"
    if op == "funds":
        return f"資金{fx['d']:+d}万円"
    if op == "scout":
        return f"スカウトポイント{fx['d']:+d}"
    if op == "guide":
        return f"指導ポイント{fx['d']:+d}"
    if op == "mod":
        v = fx["value"]
        vs = f"×{v}" if fx["kind"] in ("E_mult", "inj_mult") else f"{v:+g}"
        return f"{MOD_JA[fx['kind']]}{vs}({to}、{fx['weeks']}週)"
    if op == "flag":
        return f"旗 {fx['name']}"
    if op == "captain":
        return {"top_bond": "主将(絆が最も高い3年以上)", "top_p": "主将(力が最も高い3年以上)", "top_men": "主将(MENが最も高い3年以上)"}[fx["select"]]
    if op == "bench":
        return "欠場(控えが走る)"
    if op == "entry":
        return "エントリーに追加"
    if op == "ob_role":
        return f"OBに役割『{fx['role']}』({fx['weeks']}週)"
    return op


def when_ja(e):
    mon = ["4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月", "1月", "2月", "3月"]
    parts = []
    if e["months"]:
        parts.append("・".join(mon[m] for m in e["months"]))
    if e["weeks"]:
        parts.append("第" + "・".join(str(w + 1) for w in e["weeks"]) + "週")
    if e["phase"] == "race":
        parts.append("大会週")
    return " ".join(parts) if parts else "通年"


def md_tables():
    import events_06 as ev6
    cats = [("Team", "チーム"), ("Management", "経営"), ("Individual", "個人"), ("Race", "本番"), ("Hometown", "郷土"), ("Graduation", "卒業")]
    out = []
    for cat, ja in cats:
        rows = [e for e in ev6.EVENTS if e["cat"] == cat]
        if not rows:
            continue
        out.append(f"#### {ja}({len(rows)}件)\n")
        out.append("| ID | 名称 | 範囲 | 時期 | 条件 | 週の確率 | 選択肢 → 効果(既定は★)|")
        out.append("|----|------|------|------|------|----------|--------------------------|")
        for e in rows:
            ch = []
            for c in e["choices"]:
                star = "★" if c["id"] == e["default"][0] else ""
                req = f"〔{c['req']}〕" if c["req"] else ""
                fx = "、".join(fx_ja(f) for f in c["fx"]) or "なし"
                ch.append(f"{star}{c['label']}{req} → {fx}")
            lim = []
            if e["cooldown"]:
                lim.append(f"間隔{e['cooldown']}週")
            if e["once"] == "runner":
                lim.append("1人1回")
            if e["max_year"]:
                lim.append(f"年{e['max_year']}回まで")
            p_txt = f"{e['p']:g}" + (f"({'、'.join(lim)})" if lim else "")
            sc = {"team": "チーム", "runner": "選手", "pair": "2人"}[e["scope"]]
            cond = e["cond"].replace("|", "\\|")
            out.append(f"| `{e['id'].replace('ev.', '')}` | {e['name']} | {sc} | {when_ja(e)} | `{cond}` | {p_txt} | " + "<br>".join(ch) + " |")
        out.append("")
    return "\n".join(out)


def skill_table():
    import events_06 as ev6
    out = ["| ID | 名称 | ランク | 取得条件(条件を満たした週に、ランクごとの確率で覚醒)|", "|----|------|--------|----------------------------------------------------------|"]
    for sid, name, rank, cond in ev6.SKILLS:
        out.append(f"| `skill.{sid}` | {name} | {rank} | " + ("`『ブレーキからの再起』の励ましでのみ取得`" if cond == "false" else f"`{cond}`") + " |")
    return "\n".join(out)


def vectors():
    print("== TV-1 条件式の構文と優先順位")
    for src in ["r.grade == 1 and r.far == 1 or r.overseas == 1", "not r.captain == 1 and r.fatigue >= 60", "max(1, t.last_rank.newyear - 1) + 2 * 3",
                "-r.form < 1"]:
        print(f"  {src!r:60s} → {parse(src)}")
    print("== TV-2 検証(不適合の例)")
    for src, roles in [("r.grade == 1 and q.x", ("r",)), ("p.grade >= 2", ("r",)), ("max(1)", ("r",)), ("r.stat.XXX > 1", ("r",)), ("r.grade == ", ("r",))]:
        try:
            compiled(src, roles)
            print(f"  {src!r}: 適合")
        except DslError as x:
            print(f"  {src!r}: {x}")
    print("== TV-3 評価(固定の選手)")
    sim = Sim(20260101, years=1, warm=0, events_on=False)
    r = sim.runners[0]
    r.update(grade=1, far=1, overseas=0, weeks_in=10, fatigue=65, form=-1, motivation=2, captain=0, pref=13, hs=66, p_rank=5)
    p_ = sim.runners[1]
    p_.update(grade=3, pref=13, hs=66)
    sim.bonds[(min(r["id"], p_["id"]), max(r["id"], p_["id"]))] = 450
    sim.abs_week = 4 * 4 + 2
    for src in ["r.grade == 1 and r.far == 1 and r.overseas == 0 and r.weeks_in <= 16", "r.motivation <= 2 and (r.fatigue >= 60 or r.form <= -1) and r.injury == 0",
                "bond(r, p) >= 400 and p.grade >= r.grade + 2", "cal.month == 4 and cal.week == 2", "same_pref_n(r) >= 1", "max(1, t.last_rank.newyear - 1)"]:
        print(f"  {src:90s} → {eval_src(src, sim.ctx(r, p_), ('r', 'p'))}")
    print("== TV-4 イベントの選択(PCG32。master=20260101, abs_week=100)")
    st_ = m3.Pcg32(m3.derive(20260101, 5, 100, 0))
    us = [st_.uniform() for _ in range(6)]
    print("  scheduler の最初の6つの uniform:", [round(u, 6) for u in us])
    print("  イベントA(p=0.25, 当事者3人): u1=%.4f %s、当事者の番号 floor(u2×3)=%d" % (us[0], "発生" if us[0] < 0.25 else "発生しない", min(2, int(us[1] * 3))))
    print("  イベントB(p=1.0, 当事者2人): u1=%.4f %s、当事者の番号 floor(u2×2)=%d" % (us[2], "発生" if us[2] < 1.0 else "発生しない", min(1, int(us[3] * 2))))
    print("== TV-5 効果の適用")
    r2 = m3.new_runner([60] * 10, [90] * 10)
    r2["xp"][1] = 500
    before = (r2["stats"][1], r2["xp"][1])
    _levelup_demo = dict(stats=r2["stats"], xp=r2["xp"], pot=r2["pot"])
    r2["xp"][1] += 200
    _levelup(r2, 1)
    T = m3.round_half_up(10 * (m3.K["T_BASE"] + m3.K["T_SLOPE"] * 60))
    print(f"  STA60・XP500xu に +200xu: 閾値 T={T}xu → 能力 {before[0]} → {r2['stats'][1]}、XP {before[1] + 200} → {r2['xp'][1]}")
    print("  補正の合成: E_mult = 1.03(OBの来訪)× 1.06(OBコーチ)× 0.9(試験)=", round(1.03 * 1.06 * 0.9, 4), "/ 1.2 × 1.15 → 上限", min(1.2, 1.2 * 1.15))
    print("  排他: 『集団走○』を持つ選手に『単独走×』は付かない(02 §3.7)。上限8個。")
    print("== TV-6 覚醒の確率")
    for rk, pp in K["AWAKEN_P"].items():
        print(f"  {rk}: 条件を満たした週に {pp:.0%}。1ヶ月(4週)以内に覚醒する確率 {1 - (1 - pp) ** 4:.1%}、期待 {1 / pp:.0f} 週")
    print("== TV-7 検証ルール")
    print("  カタログの違反:", validate_catalogue() or "なし", "/ イベント数", len(all_events()))


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "vectors"
    if mode == "vectors":
        vectors()
    elif mode == "validate":
        errs = validate_catalogue()
        print("違反:", errs or "なし", "/ イベント数", len(all_events()))
    elif mode == "sim":
        n = int(sys.argv[2]) if len(sys.argv) > 2 else 8
        for sc, po in (("mid", "default"), ("mid", "uniform"), ("late", "default")):
            sim_report(n, years=10, scenario=sc, policy=po)
    elif mode == "tables":
        print(md_tables())
        print(skill_table())


if __name__ == "__main__":
    main()
