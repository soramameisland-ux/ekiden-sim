"""10 個性と素養 の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 個性: 全員が持つ3つの軸(型 = スピード/バランス/ロング、走法 = 集団/どちらも/単独、展開 = 追う/どちらも/逃げる)。
  練習(能力別の経験値の乗数・絆の効き・個別指導の効き)とレース(集団走・単独走の補正の倍率、追う展開の補正、先頭走のブレ)に影響する。
- 素養: まれな隠しの才能(登坂・下り・エース)。成長の乗数・素質の上限・特殊能力の取得条件に影響する。
- 03(成長)・04(レース)の参照モデルにあるフック(`gm` `bond_k` `ind_mult` / `style`)を通して、本物の式の中で検算する。
- 使い方: python3 -I ref_model_10.py [vectors|check|gen|train|neutral|select|bond|race|skill|world|all]
"""
import os
import random
import statistics as st
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_04 as m4  # noqa: E402
import ref_model_08 as m8  # noqa: E402

IDX = m3.IDX
STATS = m3.STATS

K = dict(
    # --- 分布(10 §4)
    PTYPE_W=[("Balanced", 0.35), ("Endurance", 0.25), ("Speed", 0.25), ("Mountain", 0.15)],          # 05 §6.2 の型(入学時の能力の偏り)
    STYLE_FORM_P={"Balanced": [("Speed", 0.15), ("Balance", 0.70), ("Long", 0.15)],
                     "Endurance": [("Speed", 0.05), ("Balance", 0.05), ("Long", 0.90)],
                     "Speed": [("Speed", 0.90), ("Balance", 0.10), ("Long", 0.00)],
                     "Mountain": [("Speed", 0.00), ("Balance", 0.40), ("Long", 0.60)]},
    STYLE_RUN_P=[("Pack", 0.30), ("Free", 0.40), ("Solo", 0.30)],
    STYLE_FLOW_P=[("Chase", 0.30), ("Even", 0.40), ("Lead", 0.30)],
    GIFT_N=[0.80, 0.18, 0.02],                                                                      # 素養の個数 0 / 1 / 2 の確率
    GIFT_KIND_W=[("Climber", 0.38), ("Descender", 0.31), ("Ace", 0.31)],
    # --- 練習(10 §5)
    FORM_UP=0.20, FORM_DOWN=0.20,        # 型: 得意の能力(スピード型は SPD、ロング型は STA)の XP ×1.20 / 苦手の能力の XP ×0.80(平均して中立になるように対称)
    FLOW_UP=0.20, FLOW_DOWN=0.20,        # 展開: 追う型は KCK ×1.20・PAC ×0.80 / 逃げる型は PAC ×1.20・KCK ×0.80
    BOND_K={"Pack": 0.30, "Free": 0.15, "Solo": 0.05},         # 走法: 絆が練習効率に効く係数(03 §5.5・§7.1 の 0.15 が基準)
    IND_MULT={"Pack": 1.0, "Free": 1.0, "Solo": 1.25},         # 走法: 個別指導の週の効率
    GIFT_XP=0.15, GIFT_POT=6, GIFT_ACE_OTHER=0.05, GIFT_ACE_OTHER_POT=2,
    GIFT_REVEAL_WEEKS=12,
    # --- 相性の印(10 §5.4)
    FIT_GOOD=1.05, FIT_OK=0.99,
)

FORM_ST = {"Speed": {"SPD": +1, "STA": -1}, "Long": {"STA": +1, "SPD": -1}, "Balance": {}}
FLOW_ST = {"Chase": {"KCK": +1, "PAC": -1}, "Lead": {"PAC": +1, "KCK": -1}, "Even": {}}
GIFT_STATS = {"Climber": {"CLB": 1.0}, "Descender": {"DWN": 1.0},
              "Ace": dict({s: K["GIFT_ACE_OTHER"] / K["GIFT_XP"] for s in STATS}, MEN=1.0, CON=1.0)}   # 重み(乗数 = 1 + GIFT_XP × 重み)
GIFT_POT_W = {"Climber": {"CLB": 1.0}, "Descender": {"DWN": 1.0}, "Ace": dict({s: K["GIFT_ACE_OTHER_POT"] / K["GIFT_POT"] for s in STATS}, MEN=1.0, CON=1.0)}
JA = {"Speed": "スピード型", "Balance": "バランス型", "Long": "ロング型", "Pack": "集団走◎", "Free": "走法は普通", "Solo": "単独走◎",
      "Chase": "追い上げ型", "Even": "展開は普通", "Lead": "逃げ型", "Climber": "登坂", "Descender": "下り", "Ace": "エース"}

# 入学時の能力の偏り(05 §6.2)
BIAS = {"Balanced": [0, 0, 0, 0, 0, 0], "Endurance": [-8, 6, 4, -6, -2, -3], "Speed": [8, -6, -4, 6, -2, -2], "Mountain": [-4, 2, 0, -2, 10, 6]}


# ================================================================ 生成(10 §4)。乱数は 09 §8.2 の鍵(label 8, a = look_seed, b = 40 個性 / 41 素養)
def pick(w, u):
    acc = 0.0
    for name, p in w:
        acc += p
        if u < acc:
            return name
    return w[-1][0]


def gen_style(look_seed, master, ptype):
    """個性(型・走法・展開)を決める。固定 3 回: ① 型 ② 走法 ③ 展開。"""
    g = m3.Pcg32(m3.derive(master, 8, look_seed, 40))
    u1, u2, u3 = g.uniform(), g.uniform(), g.uniform()
    return dict(form=pick(K["STYLE_FORM_P"][ptype], u1), run=pick(K["STYLE_RUN_P"], u2), flow=pick(K["STYLE_FLOW_P"], u3))


def gen_gifts(look_seed, master):
    """素養を決める。固定 3 回: ① 個数 ② 1つ目の種類 ③ 2つ目の種類(個数が 2 のときだけ使う。2つ目が1つ目と同じなら、次の種類に送る)。"""
    g = m3.Pcg32(m3.derive(master, 8, look_seed, 41))
    un, u1, u2 = g.uniform(), g.uniform(), g.uniform()
    n = 0 if un < K["GIFT_N"][0] else (1 if un < K["GIFT_N"][0] + K["GIFT_N"][1] else 2)
    out = []
    if n >= 1:
        out.append(pick(K["GIFT_KIND_W"], u1))
    if n >= 2:
        second = pick(K["GIFT_KIND_W"], u2)
        if second == out[0]:
            names = [x for x, _ in K["GIFT_KIND_W"]]
            second = names[(names.index(second) + 1) % len(names)]
        out.append(second)
    return out


def gen_stats(rng, ptype):
    """入学時の能力と素質(05 §6.2)。rng は uniform / normal を持つ。"""
    a0 = rng.normal(50.0, 4.0)
    gap = 15 + int(rng.uniform() * 21)
    stats, pot = [], []
    for i in range(10):
        bias = BIAS[ptype][i] if i < 6 else 0
        sd = 3.0 if i < 6 else 4.0
        s = max(20, min(95, m3.round_half_up(a0 + bias + rng.normal(0.0, sd))))
        stats.append(s)
        pot.append(min(100, s + max(2, m3.round_half_up(gap + rng.normal(0.0, 3.0)))))
    return stats, pot


def growth_mult(style, gifts):
    """能力ごとの XP の乗数(10 §5.1)。03 §6 手順3の add に掛ける。"""
    gm = [1.0] * 10
    for s, d in FORM_ST[style["form"]].items():
        gm[IDX[s]] *= 1.0 + (K["FORM_UP"] if d > 0 else -K["FORM_DOWN"])
    for s, d in FLOW_ST[style["flow"]].items():
        gm[IDX[s]] *= 1.0 + (K["FLOW_UP"] if d > 0 else -K["FLOW_DOWN"])
    for g in gifts:
        for s, w in GIFT_STATS[g].items():
            gm[IDX[s]] *= 1.0 + K["GIFT_XP"] * w
    return gm


def attach(r, style, gifts, use_style=True, use_gift=True):
    """選手 r(m3.new_runner の辞書)に個性・素養を付ける。03 のフック(gm / bond_k / ind_mult)と素質の上限を更新する。"""
    st_ = style if use_style else dict(form="Balance", run="Free", flow="Even")
    gf = gifts if use_gift else []
    r["style"], r["gifts"] = st_, list(gf)
    gm = growth_mult(st_, gf)
    for g in gf:
        for s, w in GIFT_POT_W[g].items():
            i = IDX[s]
            r["pot"][i] = min(100, r["pot"][i] + int(round(K["GIFT_POT"] * w)))
    r["gm"] = gm
    r["bond_k"] = K["BOND_K"][st_["run"]]
    r["ind_mult"] = K["IND_MULT"][st_["run"]]
    return r


def attach_sim_recruit(r, master, use_style=True, use_gift=True):
    """06 の簡易チームシミュレーションの入学生 r(id を持つ)に、個性・素養を付ける。能力は 06 のまま(個性は能力の偏りを作らない)。"""
    g = m3.Pcg32(m3.derive(master, 8, r["id"], 10))
    ptype = pick(K["PTYPE_W"], g.uniform())
    style = gen_style(r["id"], master, ptype)
    gifts = gen_gifts(r["id"], master)
    attach(r, style, gifts, use_style, use_gift)
    r["_style0"], r["_gifts0"] = style, gifts
    return r


def recruit(master, rid, use_style=True, use_gift=True):
    """入学生 1 人(rid = look_seed)。能力・素質・個性・素養を鍵つきの乱数で作る。"""
    g = m3.Pcg32(m3.derive(master, 8, rid, 10))
    ptype = pick(K["PTYPE_W"], g.uniform())
    stats, pot = gen_stats(g, ptype)
    style = gen_style(rid, master, ptype)
    gifts = gen_gifts(rid, master)
    return dict(rid=rid, ptype=ptype, stats=stats, pot=pot, style=style, gifts=gifts)


# ================================================================ 練習の相性の印(10 §5.4。画面に出す)
def theme_gains(theme):
    """テーマ(4週)の能力別の xu の合計(走行量を含む)。"""
    tot = [0.0] * 10
    for menu in m3.THEMES[theme]:
        k1, k2, mil = menu
        for part in (m3.KS[k1]["gain"], m3.KS[k2]["gain"], m3.MIL[mil]["gain"] if mil else {}):
            for s, g in part.items():
                tot[IDX[s]] += g
    return tot


def fit_value(r, theme):
    """そのテーマの XP 配分を、選手の乗数で重み付けした平均の乗数。1.00 = 標準。"""
    g = theme_gains(theme)
    tot = 0.0                                 # 足す順は添字の順(D-12。sum() は使わない)
    for x in g:
        tot += x
    acc = 0.0
    for i in range(10):
        acc += g[i] * r["gm"][i]
    return acc / tot if tot else 1.0


def fit_mark(v):
    return "◎" if v >= K["FIT_GOOD"] else ("〇" if v >= K["FIT_OK"] else "△")


def fit_view(style, gifts, revealed, theme):
    """画面に出す相性の値。素養は、判明するまで(gift_revealed = False)入れない(隠しの情報を印から漏らさない。10 §8)。"""
    return fit_value(dict(gm=growth_mult(style, gifts if revealed else [])), theme)


# ================================================================ 4 年間の育成(03 §11 と同じ設定)
def career(rec, template, use_style=True, use_gift=True, fac=None, seed=2026, years=4, individual=False):
    """入学生 rec を、template(m8.TEMPLATES のキー)の月間計画で 4 年間育てる。戻り値: 4 年後の選手(辞書)。"""
    fac = fac or m8.FAC_LV2
    r = m3.new_runner(rec["stats"], rec["pot"], 1, "Normal")
    r["pot"] = list(rec["pot"])
    attach(r, rec["style"], rec["gifts"], use_style, use_gift)
    plan = list(m8.TEMPLATES[template])
    rid = rec["rid"]
    for y in range(years):
        r["grade"] = y + 1
        for yw in range(48):
            mi, wk = divmod(yw, 4)
            menu, camp = m3.week_menu(yw, plan, m3.RACES, True)
            aw = y * 48 + yw
            m3.resolve_week(r, menu, fac, mi, seed, rid, aw, camp=camp, safety=True, individual=individual, bond_avg=min(1000.0, aw * 10.0 * 0.7))
            if yw in m3.RACES:
                r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
    return r


TEMPLATES3 = ("Speed", "Balanced", "Long")
TEMPLATES4 = ("Speed", "Balanced", "Long", "Mountain")


def team_cost(stats_list, race, styles=None):
    rs = []
    for k, s in enumerate(stats_list):
        d = dict(stats=s, skills=set(), form=0, fatigue=0)
        if styles is not None:
            d["style"] = styles[k]
        rs.append(d)
    return m4.best_lineup(rs, race)[1]


def p_equiv(race):
    return m4.K["SLOPE"] * sum(d for d, _, _ in m4.COURSE[race])


def squad_eval(master, n_run=16, races=("Fall", "National", "NewYear"), use_style=True, use_gift=True):
    """1 スクワッドの結果。方針ごとの『チーム適性値の換算』(大きいほど良い)と、卒業時の多様性。"""
    recs = [recruit(master, i) for i in range(n_run)]
    out = {}                                                        # out[i][template] = 4 年後の能力
    for i, rec in enumerate(recs):
        out[i] = {t: career(rec, t, use_style, use_gift)["stats"] for t in TEMPLATES4}
    rng = random.Random(master)
    res = {}
    for race in races:
        pe = p_equiv(race)

        def cost(assign):
            return team_cost([out[i][assign[i]] for i in range(n_run)], race)
        allt = {t: cost([t] * n_run) for t in TEMPLATES4}
        a = allt["Balanced"]
        b = min(allt.values())
        rnd = st.mean(cost([rng.choice(TEMPLATES3) for _ in range(n_run)]) for _ in range(6))
        match = []
        for i, rec in enumerate(recs):
            f = rec["style"]["form"]
            match.append("Speed" if f == "Speed" else ("Long" if f == "Long" else "Balanced"))
        m_ = cost(match)
        # 相性の印が最も高いテーマ(画面の推奨と同じ)
        guided = []
        for i, rec in enumerate(recs):
            r0 = dict(gm=growth_mult(rec["style"] if use_style else dict(form="Balance", run="Free", flow="Even"), rec["gifts"] if use_gift else []))
            guided.append(max(TEMPLATES3, key=lambda t: (sum(fit_value(r0, th) for th in m8.TEMPLATES[t]) / 12.0)))
        g_ = cost(guided)
        assign = list(guided)
        d = g_
        for _ in range(2):                                          # 個別の方針を座標降下で最適化(オラクル。選択肢は 4 テンプレート)
            for i in range(n_run):
                cur = assign[i]
                bf, bc = cur, d
                for t in TEMPLATES4:
                    if t == cur:
                        continue
                    assign[i] = t
                    cc = cost(assign)
                    if cc < bc:
                        bf, bc = t, cc
                assign[i] = bf
                d = bc
        res[race] = dict(a=-a / pe, b=-b / pe, r=-rnd / pe, m=-m_ / pe, g=-g_ / pe, o=-d / pe)
    div = st.pstdev([m8.rating(out[i]["Balanced"], "Fast") - m8.rating(out[i]["Balanced"], "Long") for i in range(n_run)])
    divm = st.pstdev([m8.rating(out[i][match[i]], "Fast") - m8.rating(out[i][match[i]], "Long") for i in range(n_run)])
    return res, dict(div_a=div, div_m=divm)


def cmd_train(n=30):
    m8.TEMPLATES.update(m8.REJECTED)
    print(f"== 練習への影響(10 §5。{n} スクワッド × 16 人。4 年後の能力。チーム適性値の換算。同じ入学生で、個性・素養を『なし』にした場合と比べる)")
    print("  a = 全員バランス / b = 全員が同じ最良の方針 / r = ランダムに 3 グループ(スピード・バランス・ロング)/ m = 型でグループ分け / g = 相性の印が最も高いテーマ(画面の推奨)/ o = 選手ごとの最適(上限)")
    agg = {k: {race: {x: 0.0 for x in "abrmgo"} for race in ("Fall", "National", "NewYear")} for k in ("on", "off")}
    dv = {"on": [0.0, 0.0], "off": [0.0, 0.0]}
    for k in range(n):
        for tag, flag in (("on", True), ("off", False)):
            res, dd = squad_eval(8100 + k, use_style=flag, use_gift=flag)
            for race, x in res.items():
                for key in "abrmgo":
                    agg[tag][race][key] += x[key] / n
            dv[tag][0] += dd["div_a"] / n
            dv[tag][1] += dd["div_m"] / n
    for race in ("Fall", "National", "NewYear"):
        on, off = agg["on"][race], agg["off"][race]
        print(f"  {race:8s} 個性なし: a {off['a']:.2f} b−a {off['b'] - off['a']:+.2f} m−r {off['m'] - off['r']:+.2f} g−b {off['g'] - off['b']:+.2f} o−b {off['o'] - off['b']:+.2f}"
              f" | 個性あり: a {on['a']:.2f}(なしとの差 {on['a'] - off['a']:+.2f}) b−a {on['b'] - on['a']:+.2f} **m−r {on['m'] - on['r']:+.2f} g−r {on['g'] - on['r']:+.2f} g−b {on['g'] - on['b']:+.2f}** o−b {on['o'] - on['b']:+.2f}"
              f" | m−a {on['m'] - on['a']:+.2f} g−a {on['g'] - on['a']:+.2f} o−a {on['o'] - on['a']:+.2f}", flush=True)
    print(f"  卒業時の多様性(速さ向きと長さ向きの差 P_Fast − P_Long の標準偏差): 個性なし {dv['off'][0]:.2f} → 個性あり(全員バランス)に育てたとき {dv['on'][0]:.2f} / 型でグループ分けしたとき {dv['on'][1]:.2f}")
    return agg


# ================================================================ 特殊能力の取得(10 §7。06 の 10 年度シミュレーションの拡張)
# 取得条件(06 §9 の DSL)。素養(r.gift.*)・個性(r.is_*)を使う。現行 = 素養・個性を見ない条件
COND_CURRENT = {
    "mountain_god": "r.stat.CLB >= 66 and ctr(r, 'hill_weeks') >= 24 and r.grade >= 2",
    "downhill_master": "r.stat.DWN >= 62 and ctr(r, 'hill_weeks') >= 16",
    "ace_section": "r.grade >= 3 and r.p_rank <= 2 and r.section_wins >= 2",
    "overtake": "ctr(r, 'overtakes') >= 4",
    "hold_lead": "ctr(r, 'lead_sections') >= 2",
    "pack_runner": "ctr(r, 'pack_sections') >= 6",
}
COND_NEW = {
    "mountain_god": "(r.gift.climber == 1 and r.stat.CLB >= 64 and ctr(r, 'hill_weeks') >= 24 and r.grade >= 2) or (r.stat.CLB >= 70 and ctr(r, 'hill_weeks') >= 32 and r.grade >= 3)",
    "downhill_master": "(r.gift.descender == 1 and r.stat.DWN >= 62 and ctr(r, 'hill_weeks') >= 16) or (r.stat.DWN >= 68 and ctr(r, 'hill_weeks') >= 28)",
    "ace_section": "r.grade >= 3 and ((r.gift.ace == 1 and r.p_rank <= 4 and r.races >= 2) or (r.p_rank <= 2 and r.section_wins >= 4))",
    "overtake": "ctr(r, 'overtakes') >= 4 - r.is_chase",
    "hold_lead": "ctr(r, 'lead_sections') >= 2 - r.is_lead",
    "pack_runner": "ctr(r, 'pack_sections') >= 6 - 2 * r.is_pack",
}
SKILL_GIFT = {"mountain_god": "Climber", "downhill_master": "Descender", "ace_section": "Ace"}
SKILL_STYLE = {"overtake": ("flow", "Chase"), "hold_lead": ("flow", "Lead"), "pack_runner": ("run", "Pack")}


def plan_for(r):
    """個性・素養に合わせた育成(画面の推奨と同じ考え方。素養が優先)。"""
    for g in r.get("gifts", []):
        if g in ("Climber", "Descender"):
            return "Mountain"
    f = r["style"]["form"]
    return "Speed" if f == "Speed" else ("Long" if f == "Long" else "Balanced")


def skill_one(args):
    import ref_model_06 as m6
    cfg, seed, years = args
    random.seed(seed * 11 + 3)
    orig_new, orig_rw, orig_ev = m6.Sim.new_recruit, m3.resolve_week, m6.all_events
    allr = []

    def new_recruit(self, year_idx):
        r = orig_new(self, year_idx)                  # 06 の Sim が個性・素養を付ける(Sim.individuality = cfg["indiv"])
        allr.append(r)
        return r

    def resolve_week(r, menu, fac, month_index, seed_, runner_id, abs_week, **kw):
        if cfg["plan"] == "aware" and r.get("style"):
            menu, camp = m3.week_menu(abs_week % 48, list(m8.TEMPLATES[plan_for(r)]), {k: v[1] for k, v in m6.RACE_WEEKS.items()}, True)
            kw["camp"] = camp
        return orig_rw(r, menu, fac, month_index, seed_, runner_id, abs_week, **kw)

    def all_events():
        evs = orig_ev()
        for e in evs:
            sid = e["id"].replace("ev.awk.", "")
            if e["id"].startswith("ev.awk.") and sid in cfg["cond"]:
                e["cond"] = f"not has_skill(r, '{sid}') and ({cfg['cond'][sid]})"
        return evs

    orig_ind = m6.Sim.individuality
    m6.Sim.individuality = cfg["indiv"]
    m6.Sim.new_recruit, m3.resolve_week, m6.all_events = new_recruit, resolve_week, all_events
    try:
        sim = m6.run_sim(seed, years=years, scenario="mid", policy="default")
    finally:
        m6.Sim.new_recruit, m3.resolve_week, m6.all_events = orig_new, orig_rw, orig_ev
        m6.Sim.individuality = orig_ind
    out = []
    for r in allr:
        if r["enroll_week"] >= 0 and r["enroll_week"] <= sim.abs_week - 4 * 48 + 1 and r["grade"] >= 4:
            out.append((r["_style0"], tuple(r["_gifts0"]), tuple(sorted(r["skills"]))))
    return out


SKILL_CFGS = {
    "K0 個性・素養なし(現行)": dict(indiv=False, plan="std", cond=COND_CURRENT),
    "K1 個性・素養あり・現行の条件・全員標準": dict(indiv=True, plan="std", cond=COND_CURRENT),
    "K2 個性・素養あり・現行の条件・個性に合わせた育成": dict(indiv=True, plan="aware", cond=COND_CURRENT),
    "K3 個性・素養あり・新しい条件・全員標準": dict(indiv=True, plan="std", cond=COND_NEW),
    "K4 個性・素養あり・新しい条件・個性に合わせた育成": dict(indiv=True, plan="aware", cond=COND_NEW),
}


def cmd_skill(n=8, years=20, cfgs=None, procs=4, only=None):
    from multiprocessing import Pool
    m8.TEMPLATES.update(m8.REJECTED)
    cfgs = cfgs or SKILL_CFGS
    if only:
        cfgs = {k: v for k, v in cfgs.items() if k.startswith(tuple(only))}
    print(f"== 特殊能力の取得(10 §7。06 の 10 年度シミュレーションの拡張。中堅・{n} 回 × {years} 年度。卒業生について)")
    for name, cfg in cfgs.items():
        with Pool(procs) as pool:
            res = pool.map(skill_one, [(cfg, 3000 + k, years) for k in range(n)])
        grads = [x for r in res for x in r]
        N = len(grads)
        print(f"  [{name}] 卒業生 {N} 人 | 平均の特殊能力 {st.mean(len(x[2]) for x in grads):.2f}個", flush=True)
        for sid, gname in SKILL_GIFT.items():
            have = [x for x in grads if gname in x[1]]
            no = [x for x in grads if gname not in x[1]]
            has = lambda x: "skill." + sid in x[2]
            print(f"      {sid:16s}: 全体 {sum(1 for x in grads if has(x)) / N:.3f}/人 | {JA[gname]}の素養あり {sum(1 for x in have if has(x)) / max(1, len(have)):.3f}({len(have)}人)/ なし {sum(1 for x in no if has(x)) / max(1, len(no)):.3f}({len(no)}人)", flush=True)
        for sid, (ax, val) in SKILL_STYLE.items():
            have = [x for x in grads if x[0][ax] == val]
            no = [x for x in grads if x[0][ax] != val]
            has = lambda x: "skill." + sid in x[2]
            print(f"      {sid:16s}: 全体 {sum(1 for x in grads if has(x)) / N:.3f}/人 | {JA[val]}あり {sum(1 for x in have if has(x)) / max(1, len(have)):.3f}({len(have)}人)/ なし {sum(1 for x in no if has(x)) / max(1, len(no)):.3f}({len(no)}人)", flush=True)


# ================================================================ レースへの影響(10 §6)
def p_per_pct(d=21.0):
    """区間タイムの 1% は、適性値 P の何点分か(04 §5。1 P = K_SLOPE 秒/km。基準ペース pace50)。"""
    return (m4.pace50(d) / m4.K["SLOPE"]) / 100.0


def race_table():
    k = m4.K
    pp = p_per_pct()
    rows = []
    for situ, key in (("集団の追走側(集団走)", "pack"), ("単独走", "solo"), ("追う展開(前に 60 秒以内)", "chase")):
        for label in ("Pack", "Free", "Solo") if key != "chase" else ("Chase", "Even", "Lead"):
            if key == "pack":
                adj = k["PACK_ADJ"] * k["RUN_SCALE"][label]
            elif key == "solo":
                adj = k["SOLO_ADJ"] * k["RUN_SCALE"][label]
            else:
                adj = k["STYLE_CHASE_ADJ"][label]
            rows.append((situ, label, adj))
    return pp, rows


def gen_style_rng(rr, ptype):
    return dict(form=pick(K["STYLE_FORM_P"][ptype], rr.random()), run=pick(K["STYLE_RUN_P"], rr.random()), flow=pick(K["STYLE_FLOW_P"], rr.random()))


def race_trial(race, seed, dP, styles_on, aware):
    """自校(チーム 0)の力が他校より dP だけ高い 10 校のレース。styles_on: 全選手に個性を付ける(対称)。aware: 自校の区間配置を個性も見て決める。"""
    rr = random.Random(seed)
    rosters = {}
    for tid in range(10):
        mean = 66.0 + (dP if tid == 0 else 0.0)
        base = []
        for _ in range(24):
            p = rr.gauss(mean, 3.5)
            pt = rr.choices([a for a, _ in m4.PROFILE_W], [w for _, w in m4.PROFILE_W])[0]
            n10 = [rr.gauss(0, 3) for _ in range(6)] + [rr.gauss(0, 4) for _ in range(4)]
            ptm = {"Balanced": "Balanced", "Endurance": "Endurance", "Speed": "Speed", "Mountain": "Mountain"}[pt]
            sty = gen_style_rng(rr, ptm)
            base.append((p, pt, n10, m4.draw_form(rr), sty))
        rosters[tid] = base
    envs = m4.gen_env(m4.PyRng(rr.random()), race)
    s0 = rr.random()
    teams = []
    for tid in range(10):
        spec = m4.SPEC_SHIFT if tid == 0 else None
        rost = []
        for p, pt, n10, f, sty in rosters[tid]:
            d = dict(stats=m4.expand_stats(p, pt, n10, m4.SPEC_SHIFT[pt] if tid == 0 else None, 0.7 if tid == 0 else 1.0), skills=set(), form=f, fatigue=0)
            if styles_on:
                d["style"] = sty
            rost.append(d)
        if tid == 0:
            entry = m4.pick_player_entry(rost, race)
            if aware and styles_on:
                cost = m4.cost_matrix(entry, race)
                sl = m4.K["SLOPE"]
                for si, (dd, typ, terr) in enumerate(m4.COURSE[race]):
                    for j, rn in enumerate(entry):
                        sty = rn["style"]
                        bonus = 0.0
                        if si > 0:
                            if dP > 0.5:
                                bonus += {"Lead": 0.4, "Chase": 0.0, "Even": 0.0}[sty["flow"]] + {"Solo": 0.2, "Pack": 0.0, "Free": 0.0}[sty["run"]]
                            elif dP < -0.5:
                                bonus += {"Chase": 0.4, "Lead": 0.0, "Even": 0.0}[sty["flow"]] + {"Pack": 0.3, "Solo": 0.0, "Free": 0.0}[sty["run"]]
                            else:
                                bonus += {"Pack": 0.2, "Free": 0.0, "Solo": 0.0}[sty["run"]]
                        cost[si][j] -= bonus * sl * dd
                asg = m4.hungarian(cost)
                lineup = [entry[j] for j in asg]
            else:
                lineup, _ = m4.best_lineup(entry, race)
            instr = [m4.gauge(lineup[si], m4.COURSE[race][si][0], m4.COURSE[race][si][1], m4.day_env(race, envs, si)["temp"])[1] for si in range(len(lineup))]
            teams.append(dict(id=0, lineup=lineup, instr=instr, geki=m4.RULES[race]["geki"]))
        else:
            lineup, instr, _ = m4.plan_day(m4.pick_ai_entry(rost, race), race, envs, "plain")
            teams.append(dict(id=tid, lineup=lineup, instr=instr))

    def hs(*a):
        return hash(a) & 0xffffffff
    res = m4.simulate_race(race, teams, envs, lambda tid, si, s=s0: m4.PyRng(hs(s, tid, si)), geki_policy=True)
    return res[0]["id"] == 0


def cmd_race(N=1000):
    pp, rows = race_table()
    print(f"== レースへの影響(10 §6)。区間タイムの 1% = 適性値 P の {pp:.2f} 点分(距離 21 km の基準ペースで換算)")
    for situ in ("集団の追走側(集団走)", "単独走", "追う展開(前に 60 秒以内)"):
        base = [x for x in rows if x[0] == situ]
        ref = [x for x in base if x[1] in ("Free", "Even")][0][2]
        print(f"  {situ}: " + " / ".join(f"{JA[x[1]]} {x[2] * 100:+.2f}%(普通との差 {-(x[2] - ref) * 100 * pp:+.2f} P)" for x in base))
    print(f"  先頭走(先頭の選手の雑音とブレーキ): 逃げ型 雑音×{m4.K['LEAD_NOISE']['Lead']}・ブレーキ率×{m4.K['LEAD_BRAKE']['Lead']} / 追い上げ型 雑音×{m4.K['LEAD_NOISE']['Chase']}・ブレーキ率×{m4.K['LEAD_BRAKE']['Chase']}")
    print(f"== 中立の確認と、個性を見た区間配置の価値(各 {N} 回。同じ乱数。自校の力 ΔP = +3 / 0 / −3。全チームの全選手に個性を付ける)")
    for dP in (3.0, 0.0, -3.0):
        for race in ("Fall", "National", "NewYear"):
            w0 = sum(race_trial(race, 6000 + k, dP, False, False) for k in range(N)) / N
            w1 = sum(race_trial(race, 6000 + k, dP, True, False) for k in range(N)) / N
            w2 = sum(race_trial(race, 6000 + k, dP, True, True) for k in range(N)) / N
            print(f"  ΔP={dP:+.0f} {race:8s}: 個性なし {w0:.3f} | 個性あり・配置は力だけ {w1:.3f}({w1 - w0:+.3f})| 個性あり・配置も個性を見る {w2:.3f}({w2 - w1:+.3f})", flush=True)


# ================================================================ 走法と練習(10 §5.2): 絆の効きと個別指導
def career_bond(rec, template, run, bond_mode="stable", guided=0.0, years=4, seed=2026):
    """走法 run(Pack / Free / Solo)の選手を 4 年間育てる。bond_mode: stable = 同じグループにいる(絆が溜まる)/ churn = 半年ごとにグループを替える(絆が 0 に戻る)。
    guided: 個別指導(1 か月の 4 週のうち、効率が ind_mult になる週の割合)。"""
    fac = m8.FAC_LV2
    r = m3.new_runner(rec["stats"], rec["pot"], 1, "Normal")
    r["pot"] = list(rec["pot"])
    sty = dict(form="Balance", run=run, flow="Even")
    attach(r, sty, [])
    plan = list(m8.TEMPLATES[template])
    rid = rec["rid"]
    for y in range(years):
        r["grade"] = y + 1
        for yw in range(48):
            mi, wk = divmod(yw, 4)
            menu, camp = m3.week_menu(yw, plan, m3.RACES, True)
            aw = y * 48 + yw
            bond = min(1000.0, aw * 7.0) if bond_mode == "stable" else min(1000.0, (aw % 24) * 7.0)
            indiv = (wk < round(4 * guided)) if guided > 0 else False
            m3.resolve_week(r, menu, fac, mi, seed, rid, aw, camp=camp, safety=True, individual=indiv, bond_avg=bond)
            if yw in m3.RACES:
                r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
    return r


def cmd_bond(n=300):
    m8.TEMPLATES.update(m8.REJECTED)
    print(f"== 走法と練習(10 §5.2。{n} 人。個性なし(型・展開は普通)で、走法だけを変えて 4 年間育てた適性値の平均。バランス計画)")
    recs = [recruit(5150, i) for i in range(n)]
    res = {}
    for run in ("Pack", "Free", "Solo"):
        for mode, guided in (("stable", 0.0), ("churn", 0.0), ("stable", 1.0)):
            ps = [m8.rating(career_bond(rec, "Balanced", run, mode, guided)["stats"], "Long") for rec in recs]
            res[(run, mode, guided)] = st.mean(ps)
    base = res[("Free", "stable", 0.0)]
    for run in ("Pack", "Free", "Solo"):
        print(f"  {JA[run]:8s}: 同じグループ(安定)P_Long {res[(run, 'stable', 0.0)]:.2f}(普通・安定との差 {res[(run, 'stable', 0.0)] - base:+.2f})| 半年ごとにグループを替える {res[(run, 'churn', 0.0)]:.2f}({res[(run, 'churn', 0.0)] - res[(run, 'stable', 0.0)]:+.2f})"
              f" | 毎週が個別指導 {res[(run, 'stable', 1.0)]:.2f}({res[(run, 'stable', 1.0)] - res[(run, 'stable', 0.0)]:+.2f})", flush=True)
    return res


# ================================================================ 生成の検算とテストベクタ
def cmd_gen(n=20000):
    import collections
    print(f"== 生成の分布(10 §4。{n} 人。鍵つきの乱数: master_seed = 777)")
    c = {k: collections.Counter() for k in ("form", "run", "flow")}
    cg = collections.Counter()
    cross = collections.Counter()
    for i in range(n):
        r = recruit(777, i)
        for k in c:
            c[k][r["style"][k]] += 1
        cg[len(r["gifts"])] += 1
        for g in r["gifts"]:
            cg[g] += 1
        cross[(r["ptype"], r["style"]["form"])] += 1
    print("  型: " + " / ".join(f"{JA[k]} {v / n:.3f}" for k, v in sorted(c["form"].items())))
    print("  走法: " + " / ".join(f"{JA[k]} {v / n:.3f}" for k, v in sorted(c["run"].items())))
    print("  展開: " + " / ".join(f"{JA[k]} {v / n:.3f}" for k, v in sorted(c["flow"].items())))
    print("  素養の個数: 0 個 %.3f / 1 個 %.3f / 2 個 %.3f | 種類(のべ): " % (cg[0] / n, cg[1] / n, cg[2] / n) + " / ".join(f"{JA[g]} {cg[g] / n:.3f}" for g in GIFT_STATS))
    print("  入学時の能力の型 → 型: " + " | ".join(f"{pt}: " + " ".join(f"{JA[f]} {cross[(pt, f)] / max(1, sum(cross[(pt, ff)] for ff in ('Speed', 'Balance', 'Long'))):.2f}" for f in ("Speed", "Balance", "Long")) for pt in ("Balanced", "Endurance", "Speed", "Mountain")))
    # 固定消費: 個性は 3 回、素養は 3 回(個数・1つ目・2つ目)
    g = m3.Pcg32(m3.derive(777, 8, 5, 40))
    return True


def cmd_vectors():
    print("== TV-1 生成(master_seed = 20260101、look_seed = 1〜6。10 §4)")
    for i in range(1, 7):
        r = recruit(20260101, i)
        print(f"  look_seed {i}: 入学時の型 {r['ptype']:9s} 型 {r['style']['form']:7s} 走法 {r['style']['run']:4s} 展開 {r['style']['flow']:5s} 素養 {r['gifts']} 能力 {r['stats']} 素質 {r['pot']}")
    print("== TV-2 練習の乗数(能力の順: " + " ".join(STATS) + ")")
    for sty, gf in ((dict(form="Speed", run="Pack", flow="Chase"), []), (dict(form="Long", run="Solo", flow="Lead"), ["Climber"]), (dict(form="Balance", run="Free", flow="Even"), ["Ace"])):
        print(f"  {sty} 素養 {gf}: " + " ".join(f"{x:.4f}" for x in growth_mult(sty, gf)) + f" | 絆の係数 {K['BOND_K'][sty['run']]} 個別指導 ×{K['IND_MULT'][sty['run']]}")
    print("== TV-3 1 週の解決(03 §6。バランス型の標準の入学生と、個性ありの同じ入学生。山対策テーマの第1週)")
    base = [50, 55, 52, 48, 45, 44, 50, 48, 46, 50]
    pot = [75, 80, 77, 73, 70, 69, 75, 73, 71, 75]
    for label, sty, gf, indiv in (("個性なし", None, [], False), ("スピード型・集団走◎・追い上げ型", dict(form="Speed", run="Pack", flow="Chase"), [], False),
                                  ("ロング型・単独走◎・逃げ型(個別指導の週)", dict(form="Long", run="Solo", flow="Lead"), [], True), ("登坂の素養", dict(form="Balance", run="Free", flow="Even"), ["Climber"], False)):
        r = m3.new_runner(base, pot, 2, "Normal")
        if sty:
            attach(r, sty, gf)
        menu = m3.THEMES["th.mountain"][0]
        rep = m3.resolve_week(r, menu, m8.FAC_LV2, 0, 2026, 1, 0, individual=indiv, bond_avg=500.0)
        print(f"  {label}: E {rep['E']:.4f} / XP の追加(xu): {rep['xp_add']}")
    print("== TV-4 相性の印(テーマ別の平均の乗数。10 §5.4)")
    for sty, gf in ((dict(form="Speed", run="Free", flow="Chase"), []), (dict(form="Long", run="Free", flow="Lead"), []), (dict(form="Balance", run="Free", flow="Even"), ["Climber"])):
        r = dict(gm=growth_mult(sty, gf))
        print(f"  {sty['form']}/{sty['flow']} 素養 {gf}: " + " ".join(f"{th.split('.')[1]} {fit_value(r, th):.3f}{fit_mark(fit_value(r, th))}" for th in ("th.base_build", "th.speed", "th.mountain", "th.camp", "th.recovery")))
    sty, gf = dict(form="Balance", run="Free", flow="Even"), ["Climber"]
    print("  画面の相性(素養は判明するまで入れない): 登坂の素養・判明前 mountain " + f"{fit_view(sty, gf, False, 'th.mountain'):.3f}{fit_mark(fit_view(sty, gf, False, 'th.mountain'))}"
          + " / 判明後 mountain " + f"{fit_view(sty, gf, True, 'th.mountain'):.3f}{fit_mark(fit_view(sty, gf, True, 'th.mountain'))}")
    print("== TV-5 レース(04 §7。10 §6): 走法・展開の補正")
    pp, rows = race_table()
    for situ, label, adj in rows:
        print(f"  {situ} {JA[label]}: {adj * 100:+.2f}%")
    print("== TV-6 特殊能力の取得条件(06 §9。素養の有無)")
    import ref_model_06 as m6
    for sid in ("mountain_god", "downhill_master", "ace_section"):
        print(f"  {sid}: {COND_NEW[sid]}")


# ================================================================ 平均の効きと、登録の選び方の価値(10 §5.5・§9)
def cmd_neutral(n=40):
    """個性・素養の『平均の効き』。全員バランス計画で育て、チーム適性値(換算 P)の、個性なしとの差。"""
    m8.TEMPLATES.update(m8.REJECTED)
    cfg = {"なし": (False, False), "個性のみ": (True, False), "素養のみ": (False, True), "個性+素養": (True, True)}
    races = ("Fall", "National", "NewYear")
    acc = {k: {r: [] for r in races} for k in cfg}
    for k in range(n):
        recs = [recruit(9100 + k, i) for i in range(16)]
        for tag, (us, ug) in cfg.items():
            outs = [career(rec, "Balanced", us, ug)["stats"] for rec in recs]
            for race in races:
                acc[tag][race].append(-team_cost(outs, race) / p_equiv(race))
    print(f"== 個性と素養の『平均の効き』(10 §5.5。全員バランス計画。チーム適性値の換算 P の、個性なしとの差。{n} スクワッド × 16 人。同じ入学生)")
    for tag in cfg:
        print(f"  {tag:8s}: " + " ".join(f"{r} {st.mean(acc[tag][r]) - st.mean(acc['なし'][r]):+.2f}(標準誤差 {st.pstdev([a - b for a, b in zip(acc[tag][r], acc['なし'][r])]) / n ** 0.5:.2f})" for r in races), flush=True)


def cmd_select(n=30):
    """登録 16 人から区間に配る選び方の価値。『総合力の上位だけから選ぶ』と『登録 16 人から大会ごとに最適に選ぶ』の差(P)。"""
    m8.TEMPLATES.update(m8.REJECTED)
    races = ("Fall", "National", "NewYear")

    def overall(stats):
        return st.mean(m8.rating(stats, t) for t in ("Fast", "Mid", "Long", "Climb", "Descent"))
    print(f"== 登録の選び方の価値(10 §5.5。{n} スクワッド × 16 人。『総合力の上位だけから選ぶ』と『登録 16 人から大会ごとに最適に選ぶ』の差。P。大きいほど選び方の価値が大きい)")
    for plan in ("Balanced", "bytype"):
        for tag, flag in (("なし", False), ("あり", True)):
            acc = {r: [] for r in races}
            spread = []
            for k in range(n):
                recs = [recruit(8100 + k, i) for i in range(16)]
                outs = []
                for rec in recs:
                    t = plan if plan != "bytype" else {"Speed": "Speed", "Long": "Long"}.get(rec["style"]["form"], "Balanced")
                    outs.append(career(rec, t, flag, flag)["stats"])
                spread.append(st.pstdev([m8.rating(o, "Fast") - m8.rating(o, "Long") for o in outs]))
                order = sorted(range(16), key=lambda i: -overall(outs[i]))
                for race in races:
                    n_sec = len(m4.COURSE[race])
                    acc[race].append((team_cost([outs[i] for i in order[:n_sec]], race) - team_cost(outs, race)) / p_equiv(race))
            print(f"  育成 {'全員バランス' if plan == 'Balanced' else '型でグループ分け'} / 個性・素養{tag}: " + " ".join(f"{r} {st.mean(v):.2f}" for r, v in acc.items()) + f" | 多様性 {st.mean(spread):.2f}", flush=True)


# ================================================================ 仕様書(10)との突き合わせ
DOC10 = os.path.join(HERE, "..", "10_individuality_spec.md")


def cmd_check():
    """10_individuality_spec.md の定数表・取得条件・テストベクタの数値が、このモデルと一致するかを確かめる。"""
    import io
    import re
    from contextlib import redirect_stdout
    text = open(DOC10, encoding="utf-8").read()
    bad = []
    rows = {}
    for line in text.splitlines():
        if line.startswith("|"):
            for name in re.findall(r"`K_([A-Z0-9_]+)`", line):
                rows.setdefault(name, line)
    for k, v in K.items():
        row = rows.get(k)
        if row is None:
            bad.append(f"定数 K_{k} が文書にない")
        elif isinstance(v, (int, float)) and not any(t in row for t in (f"{v:g}", f"{v:.2f}", f"{v * 100:g}")):
            bad.append(f"定数 K_{k} = {v} が文書の行にない: {row[:60]}")
    for sid, cond in COND_NEW.items():
        if cond not in text and sid in ("mountain_god", "downhill_master", "ace_section"):
            bad.append(f"取得条件 {sid} が文書(TV-6)と違う")
    buf = io.StringIO()
    with redirect_stdout(buf):
        cmd_vectors()
    out = buf.getvalue()
    for tag, pat in (("TV-1 能力", r"能力 \[([\d, ]+)\]"), ("TV-3 E", r"E (\d\.\d{4})")):
        for m in re.finditer(pat, out):
            val = m.group(1).replace(",", "")
            if val not in text:
                bad.append(f"{tag} {val} が文書にない")
    for m in re.finditer(r"(\w+) (\d\.\d{3})([◎〇△])", out):
        if f"{m.group(2)} {m.group(3)}" not in text:
            bad.append(f"TV-4 {m.group(1)} {m.group(2)}{m.group(3)} が文書にない")
    print("== 仕様書(10)との突き合わせ: " + ("すべて一致" if not bad else f"{len(bad)} 件の不一致"))
    for b in bad:
        print("   -", b)
    return len(bad)


# ================================================================ 長期バランス(05 の世界シミュレーションに『個性を活かした上積み』を換算して入れる)
def world_one(args):
    import ref_model_05 as w
    start, seed, bonus = args
    w.K["PLAYER_MODE"] = "auto"
    w.K["PLAYER_RACE_BONUS"] = bonus
    s_ = w.run(seed, 45, start)[0]
    seen = [False] * 3
    c = 46
    for r in s_:
        for i in range(3):
            seen[i] |= bool(r["wins"][i])
        if all(seen):
            c = r["year"]
            break
    late = [r for r in s_ if r["year"] >= 25]
    return (bonus, seed, st.mean(sum(r["wins"]) / 3 for r in late), st.mean(1 if all(r["wins"]) else 0 for r in late), c)


def cmd_world(start="Mid", seeds=range(1, 41), bonuses=(0.0, 0.25, 0.5, 1.0), procs=4):
    from multiprocessing import Pool
    jobs = [(start, sd, b) for b in bonuses for sd in seeds]
    with Pool(procs) as pool:
        res = pool.map(world_one, jobs, chunksize=2)
    print(f"== 長期バランス(10 §9。{start}・おまかせ・{len(list(seeds))} シード × 45 年度。全員の力 P に上積みを足す。05 の PLAYER_RACE_BONUS。同じシードで比較)")
    base = None
    for b in bonuses:
        rows = [r for r in res if r[0] == b]
        win, sweep, clr = st.mean(r[2] for r in rows), st.mean(r[3] for r in rows), st.mean(r[4] for r in rows)
        base = base or (win, sweep, clr)
        print(f"  上積み +{b:.2f} P: 優勝率 {win:.3f}({win - base[0]:+.3f})三冠 {sweep:.3f}({sweep - base[1]:+.3f})クリア年度の平均 {clr:.1f}({clr - base[2]:+.1f})", flush=True)
    return res


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "train"
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 30
    if mode in ("train", "all"):
        cmd_train(n if mode == "train" else 3)
    if mode in ("world", "all"):
        cmd_world(seeds=range(1, (41 if mode == "world" else 9)))
    if mode in ("vectors",):
        cmd_vectors()
    if mode in ("check", "all"):
        if cmd_check():
            sys.exit(1)
    if mode in ("neutral", "all"):
        cmd_neutral(n if mode == "neutral" else 4)
    if mode in ("select", "all"):
        cmd_select(n if mode == "select" else 3)
    if mode in ("gen", "all"):
        cmd_gen(n if mode == "gen" else 5000)
    if mode in ("bond", "all"):
        cmd_bond(n if mode == "bond" else 60)
    if mode in ("race", "all"):
        cmd_race(n if mode == "race" else 200)
    if mode in ("skill", "all"):
        cmd_skill(8 if mode == "skill" else 3, 20 if mode == "skill" else 10)
