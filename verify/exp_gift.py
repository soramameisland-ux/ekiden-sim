"""素養(gift)の検証(仕様の変更案を決めるための実験。ゲーム本体のコードではない)。

- 隠しの『素養』を選手に持たせると、育成の判断(誰をどう育てるか)と特殊能力の取得が、選手ごとに変わるかを測る。
- 素養の効果: ①該当する能力の XP の乗数(`gm`。03 §6 手順3の `add` に掛ける)②該当する能力の素質(potential)の上限を `delta` 上げる
  ③特殊能力の覚醒の確率(山の神・山下りの達人など)を、素養のある選手に有利にする(実験2)。
- 使い方: python3 -I exp_gift.py [train|skill|world|all]
"""
import os
import random
import statistics as st
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_08 as m8  # noqa: E402

IDX = m3.IDX
STATS = m3.STATS

# 素養 → 影響する能力の重み(乗数 = 1 + m × 重み。素質の上限の加算 = delta × 重み)
GIFTS = {
    "Climber":   {"CLB": 1.0},
    "Descender": {"DWN": 1.0},
    "Speedster": {"SPD": 1.0, "KCK": 1.0},
    "Stayer":    {"STA": 1.0, "PAC": 1.0},
    "Ace":       dict({s: 1.0 / 3.0 for s in STATS}, MEN=1.0, CON=1.0),
}
GIFT_JA = {"Climber": "登坂", "Descender": "下り", "Speedster": "スピード", "Stayer": "持久", "Ace": "エース"}
GIFT_SECTION = {"Climber": "Climb", "Descender": "Descent", "Speedster": "Fast", "Stayer": "Long", "Ace": None}   # その素養が主に効く区間タイプ
GIFT_P = [("none", 0.55), ("one", 0.40), ("two", 0.05)]
GIFT_W = [("Climber", 0.20), ("Descender", 0.15), ("Speedster", 0.25), ("Stayer", 0.25), ("Ace", 0.15)]


# 素養の反対側の能力(上限を neg × 重み だけ下げる。『得意があれば苦手もある』)
OPPOSE = {
    "Climber":   {"SPD": 0.5, "KCK": 0.5},
    "Descender": {"SPD": 0.5, "KCK": 0.5},
    "Speedster": {"STA": 1.0, "PAC": 1.0},
    "Stayer":    {"SPD": 1.0, "KCK": 1.0},
    "Ace":       {},
}


def apply_gifts(r, gifts, m, delta, neg=0.0, mneg=0.0):
    """選手 r に素養を付ける(gm・pot を更新)。gifts が空なら何もしない。素質は『能力 + 2』を下回らない(02 の I-01)。"""
    r["gifts"] = list(gifts)
    if not gifts or (m == 0.0 and delta == 0.0 and neg == 0.0 and mneg == 0.0):
        return r
    gm = [1.0] * 10
    for g in gifts:
        for s, w in GIFTS[g].items():
            i = IDX[s]
            gm[i] += m * w
            r["pot"][i] = min(100, r["pot"][i] + int(round(delta * w)))
        for s, w in OPPOSE[g].items():
            i = IDX[s]
            r["pot"][i] = max(r["stats"][i] + 2, r["pot"][i] - int(round(neg * w)))
            gm[i] = max(0.2, gm[i] - mneg * w)                         # 反対側の成長率を下げる
    r["gm"] = gm
    return r


def draw_gifts(rng):
    u = rng.random()
    n = 0 if u < GIFT_P[0][1] else (1 if u < GIFT_P[0][1] + GIFT_P[1][1] else 2)
    out = []
    while len(out) < n:
        v, acc = rng.random(), 0.0
        pick = GIFT_W[-1][0]
        for g, w in GIFT_W:
            acc += w
            if v < acc:
                pick = g
                break
        if pick not in out:
            out.append(pick)
    return out


# ================================================================ 実験1: 育成方針の正誤の差
def career(seed, rid, gifts, m, delta, focus, fac=None, years=4, neg=0.0, mneg=0.0):
    """m8.sim_career と同じ 4 年間の育成(03 §11 と同じ設定)。素養だけを足す。同じ (seed, rid) なら、素養の有無で入学時の能力・乱数が同じ(対比較)。"""
    fac = fac or m8.FAC_LV2
    rng = m3.Pcg32(m3.derive(seed, 99, rid, 0))
    stats0 = [max(30, min(70, round(rng.normal(50, 6)))) for _ in range(10)]
    pot0 = [min(100, s + rng.randint(15, 35)) for s in stats0]
    r = m3.new_runner(stats0, pot0, 1, "Normal")
    apply_gifts(r, gifts, m, delta, neg, mneg)
    plan = list(m8.TEMPLATES[focus])
    for y in range(years):
        r["grade"] = y + 1
        for yw in range(48):
            mi, wk = divmod(yw, 4)
            menu, camp = m3.week_menu(yw, plan, m3.RACES, True)
            aw = y * 48 + yw
            m3.resolve_week(r, menu, fac, mi, seed, rid, aw, camp=camp, safety=True, bond_avg=min(1000.0, aw * 10.0 * 0.7))
            if yw in m3.RACES:
                r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
    return stats0, r


FOCI = ("Balanced", "Speed", "Long", "Endurance", "Mountain")
FOCUS_JA = {"Balanced": "バランス", "Speed": "スピード", "Long": "ロング", "Endurance": "持久重視", "Mountain": "山重視"}
SECS = ("Fast", "Mid", "Long", "Climb", "Descent")


def rating_by_focus(gifts, m, delta, n, seed=2026):
    """foci ごとに、4 年後の区間タイプ別の適性値 P の平均(n 人)。"""
    out = {}
    for f in FOCI:
        acc = {t: 0.0 for t in SECS}
        for rid in range(n):
            _, r = career(seed, rid, gifts, m, delta, f)
            for t in SECS:
                acc[t] += m8.rating(r["stats"], t)
        out[f] = {t: acc[t] / n for t in SECS}
    return out


def cmd_train(n=150, settings=((0.15, 0.0), (0.25, 0.0), (0.25, 6.0))):
    m8.TEMPLATES.update(m8.REJECTED)
    print("== 実験1: 素養のある選手を、方針を変えて 4 年間育てた適性値 P(各 %d 人。対比較: 同じ入学時の能力・同じ乱数。施設・スタッフ Lv2)" % n)
    ctrl = rating_by_focus([], 0.0, 0.0, n)
    print("  [素養なし(現行)] Balanced の P: " + " ".join(f"{t} {ctrl['Balanced'][t]:.1f}" for t in SECS))
    print("    方針ごとの P の差(Balanced との差): " + " | ".join(f"{FOCUS_JA[f]}: " + " ".join(f"{ctrl[f][t] - ctrl['Balanced'][t]:+.2f}" for t in SECS) for f in FOCI if f != "Balanced"))
    res = {}
    for m, delta in settings:
        print(f"== 成長率の乗数 +{int(m * 100)}% / 素質の上限 +{int(delta)}(重み1の能力)")
        for g in GIFTS:
            rr = rating_by_focus([g], m, delta, n)
            res[(m, delta, g)] = rr
            sec = GIFT_SECTION[g]
            if sec:
                tgt = sec
                vals = {f: rr[f][tgt] for f in FOCI}
                cvals = {f: ctrl[f][tgt] for f in FOCI}
                best = max(vals, key=vals.get)
                worst = min(vals, key=vals.get)
                cbest = max(cvals, key=cvals.get)
                print(f"  {GIFT_JA[g]:4s}({tgt:7s}): " + " ".join(f"{FOCUS_JA[f]} {vals[f] - vals['Balanced']:+.2f}" for f in FOCI if f != "Balanced")
                      + f" | 最良 {FOCUS_JA[best]} / 最悪 {FOCUS_JA[worst]}、差 {vals[best] - vals[worst]:.2f}"
                      + f" | 素養なしの同じ区間: 最良 {FOCUS_JA[cbest]}、差 {max(cvals.values()) - min(cvals.values()):.2f}"
                      + f" | 素養なしとの P の差(Balanced): {vals['Balanced'] - cvals['Balanced']:+.2f}", flush=True)
            else:
                avg = {f: st.mean(rr[f][t] for t in SECS) for f in FOCI}
                cavg = {f: st.mean(ctrl[f][t] for t in SECS) for f in FOCI}
                print(f"  {GIFT_JA[g]:4s}(全区間平均): " + " ".join(f"{FOCUS_JA[f]} {avg[f] - avg['Balanced']:+.2f}" for f in FOCI if f != "Balanced")
                      + f" | 素養なしとの P の差(Balanced): {avg['Balanced'] - cavg['Balanced']:+.2f}", flush=True)
    return res


# ================================================================ 実験1b: チームとしての価値(個別の方針の価値)
import ref_model_04 as m4  # noqa: E402

GIFT_FOCUS = {"Climber": "Mountain", "Descender": "Mountain", "Speedster": "Speed", "Stayer": "Long", "Ace": "Balanced"}
VARIANTS = {
    "V0 素養なし(現行)": None,
    "V1 成長率 +25% のみ": dict(m=0.25, delta=0.0, neg=0.0),
    "V2 成長率 +15%・上限 +6": dict(m=0.15, delta=6.0, neg=0.0),
    "V3 成長率 +15%・上限 +6・反対側 −8": dict(m=0.15, delta=6.0, neg=8.0),
    "V4 成長率 +15%・上限 +8・反対側 −15": dict(m=0.15, delta=8.0, neg=15.0),
    "V5 成長率 ×2・反対側 ×0.6": dict(m=1.0, delta=0.0, neg=0.0, mneg=0.4),
    "V6 成長率 ×1.6・反対側 ×0.7・上限 +6/−8": dict(m=0.6, delta=6.0, neg=8.0, mneg=0.3),
}


def team_cost(runner_stats, race):
    rs = [dict(stats=s, skills=set(), form=0, fatigue=0) for s in runner_stats]
    return m4.best_lineup(rs, race)[1]


def p_equiv(race):
    return m4.K["SLOPE"] * sum(d for d, _, _ in m4.COURSE[race])      # 1 P あたりの総時間(秒)


def squad_value(variant, seed, n_run=16, races=("Fall", "National", "NewYear")):
    """1 つのスクワッド(n_run 人)。方針ごとの『チーム適性値の換算』(大きいほど良い)を返す。"""
    rng = random.Random(seed)
    gifts = [draw_gifts(rng) for _ in range(n_run)]
    outcomes = []      # outcomes[i][focus] = 4 年後の能力
    for i in range(n_run):
        g = gifts[i] if variant else []
        v = variant or dict(m=0.0, delta=0.0, neg=0.0)
        row = {}
        for f in FOCI:
            _, r = career(seed, i, g, v["m"], v["delta"], f, neg=v["neg"], mneg=v.get("mneg", 0.0))
            row[f] = r["stats"]
        outcomes.append(row)
    res = {}
    for race in races:
        pe = p_equiv(race)
        def cost_of(assign):
            return team_cost([outcomes[i][assign[i]] for i in range(n_run)], race)
        allf = {f: cost_of([f] * n_run) for f in FOCI}
        a = allf["Balanced"]
        b = min(allf.values())
        if variant:
            aware = [GIFT_FOCUS[gifts[i][0]] if gifts[i] else "Balanced" for i in range(n_run)]
        else:
            aware = ["Balanced"] * n_run
        c = cost_of(aware)
        best_single = min(allf, key=allf.get)
        assign = list(aware if variant else [best_single] * n_run)
        d = cost_of(assign)
        for _ in range(2):                                           # 個別の方針を座標降下で最適化(オラクル)
            for i in range(n_run):
                cur = assign[i]
                bestf, bestc = cur, d
                for f in FOCI:
                    if f == cur:
                        continue
                    assign[i] = f
                    cc = cost_of(assign)
                    if cc < bestc:
                        bestf, bestc = f, cc
                assign[i] = bestf
                d = bestc
        res[race] = dict(a=-a / pe, b=-b / pe, c=-c / pe, d=-d / pe)
    return res


def cmd_team(n_squads=30, variants=None):
    variants = variants or VARIANTS
    print("== 実験1b: チームとしての価値(%d スクワッド × 16 人。4 年後の能力で最適配置したときの『チーム適性値』。対比較)" % n_squads)
    print("  a = 全員バランス / b = 全員が同じ最良の方針(素養を見ない)/ c = 素養に合う方針を個別に(素養なしはバランス)/ d = 個別の方針を最適化(上限)")
    base = {}
    for name, v in variants.items():
        acc = {race: dict(a=0.0, b=0.0, c=0.0, d=0.0) for race in ("Fall", "National", "NewYear")}
        for k in range(n_squads):
            r = squad_value(v, 7000 + k)
            for race in acc:
                for key in "abcd":
                    acc[race][key] += r[race][key] / n_squads
        if v is None:
            base = acc
        for race, x in acc.items():
            b0 = base[race]["b"] if base else x["b"]
            print(f"  {name:34s} {race:8s}: a {x['a']:.2f} | b {x['b']:.2f}(現行の b との差 {x['b'] - b0:+.2f})| c {x['c']:.2f}(c−b {x['c'] - x['b']:+.2f})| d {x['d']:.2f}(d−b {x['d'] - x['b']:+.2f}、d−c {x['d'] - x['c']:+.2f})", flush=True)
    return base


# ================================================================ 実験2: 特殊能力の取得率(06 の参照モデルの 10 年度シミュレーション)
GATE = {"skill.mountain_god": "Climber", "skill.downhill_master": "Descender", "skill.ace_section": "Ace"}   # その特殊能力が『素養のある人に有利』になる対応
SKILL_CFGS = {
    "S0 素養なし(現行)":                    dict(growth=None, gate=None, plan="std"),
    "S1 素養あり・取得に影響なし・全員標準":   dict(growth=dict(m=0.15, delta=6.0, neg=8.0), gate=None, plan="std"),
    "S2 素養あり・取得に影響なし・素養に合わせた育成": dict(growth=dict(m=0.15, delta=6.0, neg=8.0), gate=None, plan="aware"),
    "S3 取得は素養ありに有利(なしは ×0.25)・全員標準": dict(growth=dict(m=0.15, delta=6.0, neg=8.0), gate=0.25, plan="std"),
    "S4 取得は素養ありに有利(なしは ×0.25)・素養に合わせた育成": dict(growth=dict(m=0.15, delta=6.0, neg=8.0), gate=0.25, plan="aware"),
    "S5 取得は素養ありだけ(なしは 0)・素養に合わせた育成": dict(growth=dict(m=0.15, delta=6.0, neg=8.0), gate=0.0, plan="aware"),
}


def skill_exp_one(args):
    import ref_model_06 as m6
    cfg, seed, years = args
    random.seed(seed * 7 + 1)
    orig_new, orig_eval, orig_rw = m6.Sim.new_recruit, m6.eval_src, m3.resolve_week
    allr = []
    gr = cfg["growth"]

    def new_recruit(self, year_idx):
        r = orig_new(self, year_idx)
        gifts = draw_gifts(self.rng)                       # 素養は常に引く(乱数の消費を揃えて、対比較にする)
        if gr:
            apply_gifts(r, gifts, gr["m"], gr["delta"], gr["neg"], gr.get("mneg", 0.0))
        else:
            r["gifts"] = []
        r["true_gifts"] = gifts
        allr.append(r)
        return r

    def eval_src(src, ctx, roles=("r",)):
        ok = orig_eval(src, ctx, roles)
        if ok and cfg["gate"] is not None and ctx.r is not None and "has_skill(r, '" in src:
            sid = src.split("has_skill(r, '")[1].split("'")[0]
            g = GATE.get(sid if sid.startswith("skill.") else "skill." + sid)
            if g and g not in ctx.r.get("gifts", []):
                return random.random() < cfg["gate"]
        return ok

    def resolve_week(r, menu, fac, month_index, seed_, runner_id, abs_week, **kw):
        if cfg["plan"] == "aware" and r.get("gifts"):
            focus = GIFT_FOCUS[r["gifts"][0]]
            menu, camp = m3.week_menu(abs_week % 48, list(m8.TEMPLATES[focus]), {k: v[1] for k, v in m6.RACE_WEEKS.items()}, True)
            kw["camp"] = camp
        return orig_rw(r, menu, fac, month_index, seed_, runner_id, abs_week, **kw)

    m6.Sim.new_recruit, m6.eval_src, m3.resolve_week = new_recruit, eval_src, resolve_week
    try:
        sim = m6.run_sim(seed, years=years, scenario="mid", policy="default")
    finally:
        m6.Sim.new_recruit, m6.eval_src, m3.resolve_week = orig_new, orig_eval, orig_rw
    out = []
    for r in allr:
        if r["enroll_week"] >= 0 and r["enroll_week"] <= sim.abs_week - 4 * 48 + 1 and r["grade"] >= 4:
            out.append((tuple(r["true_gifts"]), tuple(sorted(r["skills"])), m8.rating(r["stats"], "Long"), m8.rating(r["stats"], "Climb"), m8.rating(r["stats"], "Descent"), m8.rating(r["stats"], "Fast")))
    return out


def cmd_skill(n=8, years=20, cfgs=None, procs=4, only=None):
    from multiprocessing import Pool
    m8.TEMPLATES.update(m8.REJECTED)
    cfgs = cfgs or SKILL_CFGS
    if only:
        cfgs = {k: v for k, v in cfgs.items() if k.startswith(tuple(only))}
    print("== 実験2: 特殊能力の取得率(06 の 10 年度シミュレーションの拡張。中堅・%d 回 × %d 年度。卒業生について)" % (n, years))
    for name, cfg in cfgs.items():
        with Pool(procs) as pool:
            res = pool.map(skill_exp_one, [(cfg, 3000 + k, years) for k in range(n)])
        grads = [x for r in res for x in r]
        N = len(grads)
        print(f"  [{name}] 卒業生 {N} 人 | 素養を持つ割合 " + " ".join(f"{GIFT_JA[g]} {sum(1 for x in grads if g in x[0]) / N:.2f}" for g in GIFTS)
              + f" | 平均の P: Long {st.mean(x[2] for x in grads):.1f} Climb {st.mean(x[3] for x in grads):.1f} Descent {st.mean(x[4] for x in grads):.1f} Fast {st.mean(x[5] for x in grads):.1f}")
        for sid, g in GATE.items():
            have = [x for x in grads if g in x[0]]
            no = [x for x in grads if g not in x[0]]
            ra = sum(1 for x in have if sid in x[1]) / max(1, len(have))
            rn = sum(1 for x in no if sid in x[1]) / max(1, len(no))
            tot = sum(1 for x in grads if sid in x[1]) / N
            share = sum(1 for x in grads if sid in x[1] and g in x[0]) / max(1, sum(1 for x in grads if sid in x[1]))
            print(f"      {sid:24s}: 全体 {tot:.3f}/人 | {GIFT_JA[g]}の素養あり {ra:.3f}({len(have)}人)/ なし {rn:.3f}({len(no)}人)| 取得者のうち素養あり {share:.2f}", flush=True)
        print("      卒業時の特殊能力の平均数: " + f"{st.mean(len(x[1]) for x in grads):.2f}", flush=True)


# ================================================================ 実験3: 長期バランス(05 の世界シミュレーションに『素養を活かした上積み』を入れる)
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
    return (bonus, seed, st.mean(sum(r["wins"]) / 3 for r in late), st.mean(1 if all(r["wins"]) else 0 for r in late), c, st.mean(r["P"] for r in late))


def cmd_world(start="Mid", seeds=range(1, 41), bonuses=(0.0, 0.5, 1.0, 2.0), procs=4):
    from multiprocessing import Pool
    jobs = [(start, sd, b) for b in bonuses for sd in seeds]
    with Pool(procs) as pool:
        res = pool.map(world_one, jobs, chunksize=2)
    print("== 実験3: 自校の戦力に『素養を活かした上積み』(全員の力 P に加算。05 の PLAYER_RACE_BONUS)を入れた世界シミュレーション(%s・おまかせ・%d シード × 45 年度。同じシードで比較)" % (start, len(list(seeds))))
    base = None
    for b in bonuses:
        rows = [r for r in res if r[0] == b]
        win = st.mean(r[2] for r in rows)
        sweep = st.mean(r[3] for r in rows)
        clr = st.mean(r[4] for r in rows)
        med = st.median(r[4] for r in rows)
        pp = st.mean(r[5] for r in rows)
        if base is None:
            base = (win, sweep, clr)
        print(f"  上積み +{b:.1f} P: 優勝率 {win:.3f}({win - base[0]:+.3f})三冠 {sweep:.3f}({sweep - base[1]:+.3f})クリア年度 平均 {clr:.1f}({clr - base[2]:+.1f})中央値 {med:.1f} / 戦力 P {pp:.1f}", flush=True)
    return res


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "train"
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 150
    if mode in ("train", "all"):
        cmd_train(n)
    if mode in ("skill", "all"):
        cmd_skill(n if mode == "skill" else 4, 20 if mode == "skill" else 12)
    if mode in ("world", "all"):
        cmd_world(seeds=range(1, (41 if mode == "world" else 9)))
    if mode in ("team", "all"):
        m8.TEMPLATES.update(m8.REJECTED)
        cmd_team(n if mode == "team" else 20)
