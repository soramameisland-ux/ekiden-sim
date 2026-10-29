"""03 育成ルール仕様 の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 仕様書 03_training_rules_spec.md の数値・手順をそのまま実装している。
- 仕様書の計算例(テストベクタ)はこのモデルから出した値。
- 使い方: python3 -I ref_model_03.py [vectors|calib]
"""
import math
import sys

MASK64 = (1 << 64) - 1
STATS = ["SPD", "STA", "PAC", "KCK", "CLB", "DWN", "CON", "MEN", "REC", "DUR"]
IDX = {s: i for i, s in enumerate(STATS)}

# ---------------------------------------------------------------- 定数表 (03 §2)
K = dict(
    T_BASE=20.0, T_SLOPE=0.6,          # 能力+1の閾値 T(stat) = 20 + 0.6*stat (XP)
    ROOM_EXP=0.7,                      # 素質接近補正の指数
    LEVELUP_MAX_PER_WEEK=3,
    R_BASE=0.40, R_REC=0.20, R_MIN=0.20, R_MAX=0.80,
    E_MAX=2.0,
    P0=0.005,                          # 故障の基礎確率/週
    TAPER_L_MAX=15,
    SAFETY_F=85,
    RACE_FATIGUE_PER_KM=1.6,
    CAMP_XP=1.5, CAMP_LOAD=1.0, CAMP_INJ=1.3,
    GAIN_SCALE=3.0,                    # XP全体の倍率 (K_XP_SCALE)
)

# ポイント練習: gain は 0.1XP 単位 / load / injury risk / r_bonus
KS = {
    "ks.pace_run":    dict(gain={"PAC": 40, "STA": 30, "CON": 20},                      load=9,  risk=0.00, rb=0.0),
    "ks.threshold":   dict(gain={"STA": 55, "PAC": 35, "CON": 15},                      load=10, risk=0.00, rb=0.0),
    "ks.long_run":    dict(gain={"STA": 80, "PAC": 35, "MEN": 30},                      load=15, risk=0.15, rb=0.0),
    "ks.interval":    dict(gain={"SPD": 80, "STA": 25},                                 load=14, risk=0.15, rb=0.0),
    "ks.repetition":  dict(gain={"SPD": 110, "KCK": 45},                                load=13, risk=0.35, rb=0.0),
    "ks.build_up":    dict(gain={"KCK": 65, "PAC": 25},                                 load=10, risk=0.00, rb=0.0),
    "ks.hill":        dict(gain={"CLB": 100, "STA": 25},                                load=12, risk=0.15, rb=0.0),
    "ks.downhill":    dict(gain={"DWN": 100},                                           load=11, risk=0.35, rb=0.0),
    "ks.core":        dict(gain={"DUR": 40, "REC": 20, "SPD": 4, "STA": 4, "PAC": 4, "KCK": 4}, load=4, risk=-0.15, rb=0.0),
    "ks.sharpen":     dict(gain={"KCK": 25, "SPD": 15},                                 load=4,  risk=0.00, rb=0.0),
    "ks.rest":        dict(gain={"REC": 15},                                            load=0,  risk=-0.10, rb=0.10),
    "ks.time_trial":  dict(gain={"MEN": 55, "SPD": 25, "STA": 15, "PAC": 15, "CON": 20}, load=16, risk=0.15, rb=0.0),
    "ks.cross_train": dict(gain={"STA": 15},                                            load=3,  risk=0.00, rb=0.05),
}
MIL = {
    "mil.low":  dict(gain={"STA": 10},             load=2,  risk=0.00),
    "mil.std":  dict(gain={"STA": 18, "PAC": 12},  load=6,  risk=0.10),
    "mil.high": dict(gain={"STA": 30, "PAC": 20},  load=10, risk=0.30),
    "mil.max":  dict(gain={"STA": 42, "PAC": 28},  load=15, risk=0.80),
}

# 月のテーマ: 4週 × (ポイント練習1, ポイント練習2, 走行量)
THEMES = {
    "th.base_build": [("ks.pace_run", "ks.threshold", "mil.high"), ("ks.long_run", "ks.pace_run", "mil.high"),
                      ("ks.long_run", "ks.threshold", "mil.high"), ("ks.core", "ks.pace_run", "mil.std")],
    "th.speed":      [("ks.interval", "ks.build_up", "mil.std"), ("ks.repetition", "ks.interval", "mil.std"),
                      ("ks.time_trial", "ks.build_up", "mil.std"), ("ks.core", "ks.pace_run", "mil.low")],
    "th.mountain":   [("ks.hill", "ks.downhill", "mil.std"), ("ks.downhill", "ks.threshold", "mil.std"),
                      ("ks.hill", "ks.downhill", "mil.std"), ("ks.core", "ks.pace_run", "mil.low")],
    "th.camp":       [("ks.long_run", "ks.threshold", "mil.max"), ("ks.long_run", "ks.hill", "mil.max"),
                      ("ks.long_run", "ks.interval", "mil.max"), ("ks.core", "ks.pace_run", "mil.high")],
    "th.recovery":   [("ks.rest", "ks.core", "mil.low"), ("ks.rest", "ks.core", "mil.low"),
                      ("ks.pace_run", "ks.core", "mil.low"), ("ks.pace_run", "ks.core", "mil.std")],
}
# レース連動のピーキング(大会週から逆算して後ろから詰める): T1=3週前 .. T4=大会週
TAPER = [("ks.build_up", "ks.sharpen", "mil.std"), ("ks.sharpen", "ks.pace_run", "mil.low"),
         ("ks.sharpen", "ks.core", "mil.low"), ("ks.sharpen", "ks.rest", "mil.low")]
SAFE = ("ks.core", "ks.rest", "mil.low")
INJURED = ("ks.cross_train", "ks.rest", None)

# 施設・スタッフの効果係数(1レベルあたり。Lv1・不在は効果なし。Lv6 以上は eff_lv で 1/4 の効き)
FC = dict(coach=0.06, track=0.12, hill=0.15, gym_xp=0.15, dorm_xp=0.12, dorm_r=0.02, nutri_r=0.02,
          trainer_r=0.02, gym_inj=0.04, trainer_inj=0.03, trainer_dur=0.03, medical_dur=0.05)

GROWTH = {  # 学年 1..4
    "Early":  [1.3, 1.1, 0.8, 0.6],
    "Normal": [1.0, 1.1, 1.0, 0.9],
    "Late":   [0.7, 0.9, 1.2, 1.3],
}
MOTIV_E = {1: 0.85, 2: 0.92, 3: 1.00, 4: 1.08, 5: 1.15}


def round_half_up(x):
    return int(math.floor(x + 0.5))


# ---------------------------------------------------------------- 乱数 (03 §3)
def mix64(x):  # SplitMix64 finalizer
    x &= MASK64
    x = ((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9) & MASK64
    x = ((x ^ (x >> 27)) * 0x94D049BB133111EB) & MASK64
    return (x ^ (x >> 31)) & MASK64


def derive(master_seed, label, a, b):
    x = mix64(master_seed ^ (label * 0x9E3779B97F4A7C15 & MASK64))
    x = mix64(x ^ (a & MASK64))
    x = mix64(x ^ (b & MASK64))
    return x


class Pcg32:
    MULT = 6364136223846793005

    def __init__(self, seed):
        self.state = 0
        self.inc = ((mix64(seed ^ 0xDA3E39CB94B95BDB) << 1) | 1) & MASK64
        self._step()
        self.state = (self.state + seed) & MASK64
        self._step()

    def _step(self):
        self.state = (self.state * self.MULT + self.inc) & MASK64

    def u32(self):
        old = self.state
        self._step()
        xorshifted = (((old >> 18) ^ old) >> 27) & 0xFFFFFFFF
        rot = old >> 59
        return ((xorshifted >> rot) | (xorshifted << ((-rot) & 31))) & 0xFFFFFFFF

    def uniform(self):  # (0,1)
        return (self.u32() + 0.5) / 4294967296.0

    def randint(self, a, b):
        return a + int(self.uniform() * (b - a + 1))

    def normal(self, mu, sigma):  # Box-Muller: 常に2回消費
        u1, u2 = self.uniform(), self.uniform()
        return mu + sigma * math.sqrt(-2.0 * math.log(u1)) * math.cos(2.0 * math.pi * u2)


L_INJURY, L_FORM = 1, 2


# ---------------------------------------------------------------- 効率・疲労・故障・調子
def e_fatigue(f):
    if f < 40:
        return 1.0
    if f < 70:
        return 1.0 - 0.15 * (f - 40) / 29.0
    if f < 85:
        return 0.7
    return 0.3


def m_fatigue(f):
    if f < 40:
        return 1.0
    if f < 70:
        return 1.0 + 0.5 * (f - 40) / 29.0
    if f < 85:
        return 2.0
    return 4.0


FAC_KNEE, FAC_SLOPE_ABOVE = 5, 0.25      # L-70: 施設・スタッフは Lv8 まで。効果に使うレベルは、Lv5 を超えた分が 1/4 の効き


def eff_lv(lv):
    return float(lv) if lv <= FAC_KNEE else FAC_KNEE + FAC_SLOPE_ABOVE * (lv - FAC_KNEE)


def lvl(fac, key):
    """効果に使うレベル(L-70。Lv5 までは Lv そのまま)。"""
    return eff_lv(fac.get(key, 1))


def menu_of(r, menu):
    return [m for m in menu if m]


def week_load(menu, camp=False):
    k1, k2, mil = menu
    load = KS[k1]["load"] + KS[k2]["load"] + (MIL[mil]["load"] if mil else 0)
    return load * (K["CAMP_LOAD"] if camp else 1.0)


def peaking_bonus(taper_weeks, sharpen_recent, skills):
    if taper_weeks >= 4:
        b = -8
    elif taper_weeks == 0:
        b = 0
    else:
        b = {1: 3, 2: 6, 3: 8}[taper_weeks]
        if not sharpen_recent:
            b = round_half_up(b * 0.5)
        if "skill.peaking_master" in skills:
            b += 4
    return max(-8, min(12, b))


def form_level(score):
    if score >= 20:
        return 2
    if score >= 8:
        return 1
    if score > -8:
        return 0
    if score > -20:
        return -1
    return -2


def new_runner(stats, pot, grade=1, gtype="Normal", rec=None):
    return dict(stats=list(stats), pot=list(pot), xp=[0] * 10, fatigue=0, motivation=3, grade=grade,
                gtype=gtype, taper_weeks=0, sharpen_hist=[False, False, False], inj=("None", 0),
                skills=set(), bond_avg=0.0, form=0, levelups=0, week_levelups=0)


def resolve_week(r, menu, fac, month_index, seed, runner_id, abs_week, camp=False, safety=True,
                 individual=False, bond_avg=None):
    """1選手の1週間を解決する。r を更新して、レポート(dict)を返す。"""
    rep = {}
    injured = r["inj"][0] != "None"
    f0 = r["fatigue"]
    # 1. メニュー決定: 個別 > 故障 > 安全網 > 与えられたメニュー
    if injured:
        menu = INJURED
    elif safety and not individual and f0 >= K["SAFETY_F"]:
        menu = SAFE
    k1, k2, mil = menu
    rep["menu"] = menu
    # 2. 効率 E
    e_bond = 1.0 + r.get("bond_k", 0.15) * ((r["bond_avg"] if bond_avg is None else bond_avg) / 1000.0)    # bond_k: 走法による絆の効き(10 §5.2。無ければ 0.15)
    e = (e_fatigue(f0) * MOTIV_E[r["motivation"]] * GROWTH[r["gtype"]][min(r["grade"], 4) - 1]
         * (1.0 + FC["coach"] * (lvl(fac, "coach") - 1)) * e_bond)
    if camp:
        e *= K["CAMP_XP"] * (1.1 if "skill.summer_boy" in r["skills"] else 1.0)
    if individual:
        e *= r.get("ind_mult", 1.0)                       # 個別指導の週の効率(走法が『単独』のとき上がる。10 §5.2。無ければ 1)
    e = min(e, K["E_MAX"])
    rep["E"] = e
    # 3. XP付与・レベルアップ
    gains = [0.0] * 10
    for part in (KS[k1]["gain"], KS[k2]["gain"], MIL[mil]["gain"] if mil else {}):
        for s, g in part.items():
            gains[IDX[s]] += g
    stat_mult = {"SPD": 1 + FC["track"] * (lvl(fac, "track") - 1), "KCK": 1 + FC["track"] * (lvl(fac, "track") - 1),
                 "CLB": 1 + FC["hill"] * (lvl(fac, "hill") - 1), "DWN": 1 + FC["hill"] * (lvl(fac, "hill") - 1),
                 "DUR": 1 + FC["gym_xp"] * (lvl(fac, "gym") - 1), "REC": 1 + FC["dorm_xp"] * (lvl(fac, "dorm") - 1)}
    lv_ups = 0
    adds = [0] * 10
    rep["xp_add"] = adds
    for i, s in enumerate(STATS):
        if gains[i] == 0 or r["stats"][i] >= r["pot"][i]:
            continue
        room = (1.0 - r["stats"][i] / r["pot"][i]) ** K["ROOM_EXP"]
        add = round_half_up(gains[i] * K["GAIN_SCALE"] * e * stat_mult.get(s, 1.0) * room * (r["gm"][i] if "gm" in r else 1.0))   # gm: 素養による成長率の乗数(検証用のフック。無ければ 1)
        adds[i] = add
        r["xp"][i] += add
        n = 0
        while n < K["LEVELUP_MAX_PER_WEEK"] and r["stats"][i] < r["pot"][i]:
            t = round_half_up(10 * (K["T_BASE"] + K["T_SLOPE"] * r["stats"][i]))
            if r["xp"][i] < t:
                break
            r["xp"][i] -= t
            r["stats"][i] += 1
            n += 1
            lv_ups += 1
        if r["stats"][i] >= r["pot"][i]:
            r["xp"][i] = 0
    rep["levelups"] = lv_ups
    r["week_levelups"] = lv_ups
    # 4. 疲労
    care = (FC["dorm_r"] * (lvl(fac, "dorm") - 1) + FC["nutri_r"] * (lvl(fac, "nutri") - 1)
            + FC["trainer_r"] * (lvl(fac, "trainer") - 1))
    rb = KS[k1]["rb"] + KS[k2]["rb"]
    rr = (K["R_BASE"] + K["R_REC"] * r["stats"][IDX["REC"]] / 100.0 + care + rb
          + (0.05 if "skill.recovery_plus" in r["skills"] else 0.0)
          - (0.05 if "skill.summer_fade" in r["skills"] and 3 <= month_index <= 5 else 0.0))
    rr = max(K["R_MIN"], min(K["R_MAX"], rr))
    load = week_load(menu, camp)
    f1 = max(0, min(100, round_half_up(f0 * (1 - rr) + load)))
    rep["r"], rep["L"], rep["F_end"] = rr, load, f1
    # 5. 故障
    rng_i = Pcg32(derive(seed, L_INJURY, runner_id, abs_week))
    inj_sev, inj_left = r["inj"]
    if injured:
        inj_left -= 1
        if inj_left <= 0:
            inj_sev, inj_left = "None", 0
        rep["injury_p"] = None
    else:
        m_load = 1.0 + KS[k1]["risk"] + KS[k2]["risk"] + (MIL[mil]["risk"] if mil else 0.0)
        m_load = max(0.5, m_load)
        m_dur = 1.6 - r["stats"][IDX["DUR"]] / 100.0
        m_sk = (0.5 if "skill.iron_legs" in r["skills"] else 1.0) * (1.5 if "skill.injury_prone" in r["skills"] else 1.0)
        m_fac = (1.0 - FC["gym_inj"] * (lvl(fac, "gym") - 1)) * (1.0 - FC["trainer_inj"] * (lvl(fac, "trainer") - 1))
        p = K["P0"] * m_fatigue(f0) * m_load * m_dur * m_sk * m_fac * (K["CAMP_INJ"] if camp else 1.0)
        p = max(0.0, min(0.5, p))
        rep["injury_p"] = p
        u = rng_i.uniform()
        if u < p:
            w = (0.4, 0.8) if f0 >= 85 else (0.6, 0.9)   # Minor / Minor+Moderate の累積
            v = rng_i.uniform()
            sev = "Minor" if v < w[0] else ("Moderate" if v < w[1] else "Severe")
            lo, hi = {"Minor": (1, 2), "Moderate": (4, 8), "Severe": (12, 24)}[sev]
            base = rng_i.randint(lo, hi)  # 選ばれた重症度の分だけ1回引く
            mult = max(0.5, 1.0 - FC["medical_dur"] * (lvl(fac, "medical") - 1) - FC["trainer_dur"] * (lvl(fac, "trainer") - 1))
            inj_sev, inj_left = sev, max(1, round_half_up(base * mult))
            rep["injury"] = (sev, inj_left)
    r["inj"] = (inj_sev, inj_left)
    # 6. 調子
    sharpened_now = (k1 == "ks.sharpen" or k2 == "ks.sharpen")
    hist = r["sharpen_hist"][1:] + [sharpened_now]
    r["sharpen_hist"] = hist
    if r["inj"][0] != "None" or injured:
        r["taper_weeks"] = 0
    elif load <= K["TAPER_L_MAX"]:
        r["taper_weeks"] += 1
    else:
        r["taper_weeks"] = 0
    rng_f = Pcg32(derive(seed, L_FORM, runner_id, abs_week))
    sigma = 8.0 * (1.5 - r["stats"][IDX["CON"]] / 100.0)
    noise = rng_f.normal(0.0, sigma)
    pk = peaking_bonus(r["taper_weeks"], any(hist), r["skills"])
    score = (50 - f1) * 0.5 + pk + noise
    r["form"] = -2 if r["inj"][0] != "None" else form_level(score)
    rep.update(peaking=pk, noise=noise, form_score=score, form=r["form"])
    r["fatigue"] = f1
    return rep


# ---------------------------------------------------------------- 年間シミュレーション(検算用)
STD_PLAN = ["th.base_build", "th.speed", "th.base_build", "th.mountain", "th.camp", "th.speed",
            "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.speed", "th.base_build"]
RACES = {25: 8.0, 28: 13.0, 36: 21.5}  # 年内の週番号(0-based) → 想定区間距離km


def week_menu(year_week, plan=STD_PLAN, races=RACES, taper=True):
    month, wk = divmod(year_week, 4)
    camp = plan[month] == "th.camp"
    menu = THEMES[plan[month]][wk]
    if taper:
        prev = -10
        for t in sorted(races):
            start = max(t - 3, prev + 1)
            if start <= year_week <= t:
                menu = TAPER[4 - (t - start + 1) + (year_week - start)]
            prev = t
    return menu, camp


def simulate_career(seed, rid, fac, plan=STD_PLAN, years=4, stats0=None, pot0=None, gtype="Normal",
                    taper=True, races=RACES, safety=True, skills=()):
    rng = Pcg32(derive(seed, 99, rid, 0))
    stats0 = stats0 or [max(30, min(70, round(rng.normal(50, 6)))) for _ in range(10)]
    pot0 = pot0 or [min(100, s + rng.randint(15, 35)) for s in stats0]
    r = new_runner(stats0, pot0, 1, gtype)
    r["skills"] = set(skills)
    inj_events, sev, inj_weeks = 0, 0, 0
    forms = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
    race_forms = []
    start = list(stats0)
    for y in range(years):
        r["grade"] = y + 1
        for yw in range(48):
            menu, camp = week_menu(yw, plan, races, taper)
            aw = y * 48 + yw
            before = r["inj"][0]
            rep = resolve_week(r, menu, fac, yw // 4, seed, rid, aw, camp=camp, safety=safety,
                               bond_avg=min(1000.0, (y * 48 + yw) * 10.0 * 0.7))
            if "injury" in rep:
                inj_events += 1
                sev += rep["injury"][0] == "Severe"
            inj_weeks += r["inj"][0] != "None"
            forms[r["form"]] += 1
            if yw in races:
                race_forms.append(r["form"])
                r["fatigue"] = min(100, r["fatigue"] + round_half_up(races[yw] * K["RACE_FATIGUE_PER_KM"]))
    return start, r, dict(inj=inj_events, severe=sev, inj_weeks=inj_weeks, forms=forms, race_forms=race_forms)


def calib(n=1500, fac=None, **kw):
    fac = fac or dict(coach=2, trainer=2, nutri=2, dorm=2, track=2, hill=2, gym=2, medical=2)
    dstats = [0.0] * 10
    inj = sev = wks = 0
    forms = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
    rf = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
    final_avg = [0.0] * 10
    for rid in range(n):
        s0, r, st = simulate_career(2026, rid, fac, **kw)
        for i in range(10):
            dstats[i] += r["stats"][i] - s0[i]
            final_avg[i] += r["stats"][i]
        inj += st["inj"]
        sev += st["severe"]
        wks += st["inj_weeks"]
        for k, v in st["forms"].items():
            forms[k] += v
        for v in st["race_forms"]:
            rf[v] += 1
    ry = n * 4
    print("  4年間の平均上昇:", {STATS[i]: round(dstats[i] / n, 1) for i in range(10)})
    print(f"  故障: {inj / ry:.2f}回/人年 うち重傷 {sev / max(1, inj) * 100:.0f}% 離脱 {wks / ry:.1f}週/人年")
    tot = sum(forms.values())
    print("  調子分布(全週):", {k: f"{v / tot * 100:.0f}%" for k, v in forms.items()})
    tr = sum(rf.values())
    print("  大会週の調子:", {k: f"{v / tr * 100:.0f}%" for k, v in rf.items()})


FAC_LV2 = dict(coach=2, trainer=2, nutri=2, dorm=2, track=2, hill=2, gym=2, medical=2)


def vectors():
    """仕様書 03 §11 の計算例(テストベクタ)を出力する。"""
    print("== TV-1 乱数")
    seed = derive(123456789, L_INJURY, 7, 100)
    print("derive(123456789, 1, 7, 100) =", seed)
    g = Pcg32(seed)
    print("u32 x3 =", [g.u32() for _ in range(3)])
    g = Pcg32(seed)
    print("uniform x2 =", [round(g.uniform(), 9) for _ in range(2)])
    g = Pcg32(seed)
    print("normal(0,1) =", round(g.normal(0.0, 1.0), 9))
    print("derive(123456789, 2, 7, 100) =", derive(123456789, L_FORM, 7, 100))

    print("== TV-2 テーブル")
    print("E_fatigue:", {f: round(e_fatigue(f), 4) for f in (0, 39, 40, 55, 69, 70, 84, 85, 100)})
    print("m_fatigue:", {f: round(m_fatigue(f), 4) for f in (0, 39, 40, 55, 69, 70, 84, 85, 100)})
    print("T(stat) xu:", {s: round_half_up(10 * (K["T_BASE"] + K["T_SLOPE"] * s)) for s in (30, 50, 55, 80, 99)})
    print("peaking:", {(w, sh): peaking_bonus(w, sh, set()) for w in range(0, 6) for sh in (True, False)})
    print("peaking(調整力○):", {w: peaking_bonus(w, True, {"skill.peaking_master"}) for w in range(0, 5)})
    print("form_level:", {sc: form_level(sc) for sc in (30, 20, 19.9, 8, 7.9, 0, -7.9, -8, -19.9, -20, -30)})

    print("== TV-3 1週の解決(基礎ビルド第1週)")
    r = new_runner([58, 60, 56, 52, 45, 44, 55, 50, 52, 54], [80, 82, 78, 74, 70, 68, 75, 72, 70, 74], grade=2)
    r["fatigue"] = 40
    menu = THEMES["th.base_build"][0]
    rep = resolve_week(r, menu, FAC_LV2, 0, 2026, 1, 0, bond_avg=0)
    print("menu", rep["menu"])
    print("E =", round(rep["E"], 6), " r =", round(rep["r"], 6), " L =", rep["L"], " F_end =", rep["F_end"])
    print("xp_add(0.1XP) =", dict(zip(STATS, rep["xp_add"])))
    print("levelups =", rep["levelups"], " stats =", r["stats"], " xp =", r["xp"])
    print("injury_p =", round(rep["injury_p"], 6), " injury =", rep.get("injury"))
    print("peaking =", rep["peaking"], " noise =", round(rep["noise"], 6), " form_score =", round(rep["form_score"], 6), " form =", rep["form"])

    print("== TV-4 疲労の推移(基礎ビルド4週, F=40開始, REC=52)")
    r = new_runner([58, 60, 56, 52, 45, 44, 55, 50, 52, 54], [99] * 10, grade=2)
    r["fatigue"] = 40
    out = []
    for w in range(4):
        rep = resolve_week(r, THEMES["th.base_build"][w], FAC_LV2, 0, 2026, 1, w)
        out.append((rep["L"], round(rep["r"], 4), rep["F_end"]))
    print(out)

    print("== TV-5 ピーキング(大会週から逆算, F=60開始, CON=50)")
    r = new_runner([58, 60, 56, 52, 45, 44, 50, 50, 52, 54], [99] * 10, grade=3)
    r["fatigue"] = 60
    for k, tw in enumerate(TAPER):
        rep = resolve_week(r, tw, FAC_LV2, 6, 2026, 2, 100 + k)
        base = (50 - rep["F_end"]) * 0.5
        print(f"T{k + 1} menu={tw} L={rep['L']} r={rep['r']:.4f} F_end={rep['F_end']} taper_weeks={r['taper_weeks']} "
              f"peaking={rep['peaking']} base={base:.1f} 決定部分={base + rep['peaking']:.1f}")

    print("== TV-6 故障確率(週, DUR=54, 施設Lv2, 通常)")
    for label, mn in (("標準(閾値+ペース走+標準)", ("ks.threshold", "ks.pace_run", "mil.std")),
                      ("強化(インターバル+ロング走+多)", ("ks.interval", "ks.long_run", "mil.high")),
                      ("追い込み(レペ+下り+極)", ("ks.repetition", "ks.downhill", "mil.max"))):
        row = []
        for f0 in (30, 50, 66, 86):
            rr = new_runner([50] * 5 + [50, 50, 50, 50, 54], [99] * 10)
            rr["fatigue"] = f0
            rp = resolve_week(rr, mn, FAC_LV2, 0, 1, 1, 1, safety=False)
            row.append((f0, round(rp["injury_p"] * 100, 2)))
        print(label, row)

    print("== TV-7 安全網")
    rr = new_runner([50] * 10, [99] * 10)
    rr["fatigue"] = 85
    rp = resolve_week(rr, THEMES["th.camp"][2], FAC_LV2, 4, 1, 1, 1, camp=True)
    print("F=85 開始の合宿第3週 → メニュー", rp["menu"], "F_end", rp["F_end"])
    rr = new_runner([50] * 10, [99] * 10)
    rr["fatigue"] = 84
    rp = resolve_week(rr, THEMES["th.camp"][2], FAC_LV2, 4, 1, 1, 1, camp=True)
    print("F=84 開始の合宿第3週 → メニュー", rp["menu"], "F_end", rp["F_end"])


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "calib"
    if mode == "vectors":
        vectors()
        sys.exit(0)
    if mode == "calib":
        for label, kw in (("標準(ピーキングあり・全員が全大会出走)", {}),
                          ("ピーキングなし", dict(taper=False)),
                          ("夏合宿なし", dict(plan=[t if t != "th.camp" else "th.base_build" for t in STD_PLAN])),
                          ("常に夏合宿(追い込み)", dict(plan=["th.camp"] * 12)),
                          ("安全網なし", dict(safety=False))):
            print(label)
            calib(**kw)
