"""08 画面仕様 の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 画面そのものではなく、画面が表示・提案する値(予測・推奨・見込み・前評判・ランク・記録)の計算と、
  画面カタログの整合性(screens_08.py)、1年度の操作時間の見積もり、レイアウトの収まりを検算する。
- 03 / 04 / 05 の参照モデルを import して、同じ式を使う。
- 使い方: python3 -I ref_model_08.py [vectors|forecast|autoplan|rank|scout|preview|budget|layout|graph|all]
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m3  # noqa: E402

STATS = m3.STATS
IDX = m3.IDX

# ---------------------------------------------------------------- 定数表 (08 §2)
K = dict(
    # 表示のランク (R-01)
    RANK_STEP=10,
    # 推奨プラン (R-04)
    GROUP_A=10, GROUP_B=10,
    # 予測 (R-05)
    WARN_F=70, WARN_F_CAMP=80,
    # 素質のヒント (R-02)
    HINT_LATE=0.85, HINT_CAP=0.95,
)

FAC_LV2 = m3.FAC_LV2


# ================================================================ R-01 ランク表記
RANKS = ["G", "F", "E", "D", "C", "B", "A", "S"]


def rank_letter(v):
    """明示版: S≥90 / A≥80 / B≥70 / C≥60 / D≥50 / E≥40 / F≥30 / G<30。"""
    for lim, ch in ((90, "S"), (80, "A"), (70, "B"), (60, "C"), (50, "D"), (40, "E"), (30, "F")):
        if v >= lim:
            return ch
    return "G"


# ================================================================ R-02 素質のヒント(自校の選手)
def potential_hint(stat, pot):
    """3段階のヒント。'room'=伸びしろあり / 'late'=終盤 / 'cap'=頭打ち間近 / 'max'=限界に到達"""
    if stat >= pot:
        return "max"
    r = stat / pot
    if r >= K["HINT_CAP"]:
        return "cap"
    if r >= K["HINT_LATE"]:
        return "late"
    return "room"


# ================================================================ R-03 記録(PB)の導出
W = {  # 04 §5.1 の区間タイプ別の重み
    "Fast":    dict(SPD=.40, STA=.20, PAC=.15, KCK=.15, MEN=.10),
    "Mid":     dict(SPD=.25, STA=.30, PAC=.25, KCK=.10, MEN=.10),
    "Long":    dict(STA=.35, PAC=.25, SPD=.20, MEN=.10, KCK=.10),
    "Climb":   dict(CLB=.45, STA=.25, PAC=.15, MEN=.10, SPD=.05),
    "Descent": dict(DWN=.45, SPD=.20, STA=.15, PAC=.10, MEN=.10),
}
PB_EVENTS = {"t5000": (5000.0, "Fast"), "t10000": (10000.0, "Mid"), "t_half": (21097.5, "Long")}


def pace50(d_km):
    return 176.0 + 6.0 * math.log(d_km / 5.0)


def rating(stats, typ):
    t = 0.0                                   # 足す順は表の順(D-12。sum() は 3.12 以降は補正つきの和なので使わない)
    for s, w in W[typ].items():
        t += w * stats[IDX[s]]
    return t


def derive_pb(stats, key):
    """能力から記録を導出する(ds = 0.1秒の整数)。形(調子0・環境なし)。"""
    d, typ = PB_EVENTS[key]
    p = rating(stats, typ)
    km = d / 1000.0
    return m3.round_half_up(10.0 * km * (pace50(km) - 0.4 * (p - 50.0)))


def derive_pb_from_p(key, p):
    d, _ = PB_EVENTS[key]
    km = d / 1000.0
    return m3.round_half_up(10.0 * km * (pace50(km) - 0.4 * (p - 50.0)))


def fmt_ds(ds):
    s = ds / 10.0
    h = int(s // 3600)
    m = int((s - h * 3600) // 60)
    sec = s - h * 3600 - m * 60
    return f"{h}:{m:02d}:{sec:04.1f}" if h else f"{m}:{sec:04.1f}"


# ================================================================ R-05 月末予測
def _care(fac):
    return (m3.FC["dorm_r"] * (m3.lvl(fac, "dorm") - 1) + m3.FC["nutri_r"] * (m3.lvl(fac, "nutri") - 1)
            + m3.FC["trainer_r"] * (m3.lvl(fac, "trainer") - 1))


def expected_week(r, menu, fac, month_index, camp=False, safety=True, individual=False, bond_avg=None, mods=None):
    """乱数を使わない1週の予測。r を更新し、レポートを返す(m3.resolve_week の決定的な部分と同じ式)。
    故障は『起きない』ものとして進め、確率 p を返す。調子は平均 mu と標準偏差 sigma を返す。"""
    rep = {}
    injured = r["inj"][0] != "None"
    f0 = r["fatigue"]
    if injured:
        menu = m3.INJURED
    elif safety and not individual and f0 >= m3.K["SAFETY_F"]:
        menu = m3.SAFE
        rep["safety"] = True
    k1, k2, mil = menu
    rep["menu"] = menu
    e_bond = 1.0 + r.get("bond_k", 0.15) * ((r["bond_avg"] if bond_avg is None else bond_avg) / 1000.0)       # 走法(10 §5.2)
    e = (m3.e_fatigue(f0) * m3.MOTIV_E[r["motivation"]] * m3.GROWTH[r["gtype"]][min(r["grade"], 4) - 1]
         * (1.0 + m3.FC["coach"] * (m3.lvl(fac, "coach") - 1)) * e_bond)
    if camp:
        e *= m3.K["CAMP_XP"] * (1.1 if "skill.summer_boy" in r["skills"] else 1.0)
    mods = mods or {}
    e *= mods.get("E_mult", 1.0)                       # 06 §6: 練習の効率(03 §6 手順1。E_MAX の前)
    if individual:
        e *= r.get("ind_mult", 1.0)                    # 個別指導の週の効率(10 §5.2)
    e = min(e, m3.K["E_MAX"])
    gains = [0.0] * 10
    for part in (m3.KS[k1]["gain"], m3.KS[k2]["gain"], m3.MIL[mil]["gain"] if mil else {}):
        for s, g in part.items():
            gains[IDX[s]] += g
    stat_mult = {"SPD": 1 + m3.FC["track"] * (m3.lvl(fac, "track") - 1), "KCK": 1 + m3.FC["track"] * (m3.lvl(fac, "track") - 1),
                 "CLB": 1 + m3.FC["hill"] * (m3.lvl(fac, "hill") - 1), "DWN": 1 + m3.FC["hill"] * (m3.lvl(fac, "hill") - 1),
                 "DUR": 1 + m3.FC["gym_xp"] * (m3.lvl(fac, "gym") - 1), "REC": 1 + m3.FC["dorm_xp"] * (m3.lvl(fac, "dorm") - 1)}
    lv_ups = 0
    for i, s in enumerate(STATS):
        if gains[i] == 0 or r["stats"][i] >= r["pot"][i]:
            continue
        room = (1.0 - r["stats"][i] / r["pot"][i]) ** m3.K["ROOM_EXP"]
        add = m3.round_half_up(gains[i] * m3.K["GAIN_SCALE"] * e * stat_mult.get(s, 1.0) * room * (r["gm"][i] if "gm" in r else 1.0))   # gm: 個性・素養の乗数(10 §5.1)
        r["xp"][i] += add
        n = 0
        while n < m3.K["LEVELUP_MAX_PER_WEEK"] and r["stats"][i] < r["pot"][i]:
            t = m3.round_half_up(10 * (m3.K["T_BASE"] + m3.K["T_SLOPE"] * r["stats"][i]))
            if r["xp"][i] < t:
                break
            r["xp"][i] -= t
            r["stats"][i] += 1
            n += 1
            lv_ups += 1
        if r["stats"][i] >= r["pot"][i]:
            r["xp"][i] = 0
    rep["levelups"] = lv_ups
    rr = (m3.K["R_BASE"] + m3.K["R_REC"] * r["stats"][IDX["REC"]] / 100.0 + _care(fac) + m3.KS[k1]["rb"] + m3.KS[k2]["rb"]
          + (0.05 if "skill.recovery_plus" in r["skills"] else 0.0)
          - (0.05 if "skill.summer_fade" in r["skills"] and 3 <= month_index <= 5 else 0.0))
    rr += mods.get("r_add", 0.0)                       # 06 §6: 疲労の回復率(R_MIN〜R_MAX の前)
    rr = max(m3.K["R_MIN"], min(m3.K["R_MAX"], rr))
    load = m3.week_load(menu, camp)
    f1 = max(0, min(100, m3.round_half_up(f0 * (1 - rr) + load)))
    rep["F_end"], rep["L"] = f1, load
    if injured:
        sev, left = r["inj"]
        left -= 1
        r["inj"] = ("None", 0) if left <= 0 else (sev, left)
        rep["injury_p"] = 0.0
    else:
        m_load = max(0.5, 1.0 + m3.KS[k1]["risk"] + m3.KS[k2]["risk"] + (m3.MIL[mil]["risk"] if mil else 0.0))
        m_dur = 1.6 - r["stats"][IDX["DUR"]] / 100.0
        m_sk = (0.5 if "skill.iron_legs" in r["skills"] else 1.0) * (1.5 if "skill.injury_prone" in r["skills"] else 1.0)
        m_fac = (1.0 - m3.FC["gym_inj"] * (m3.lvl(fac, "gym") - 1)) * (1.0 - m3.FC["trainer_inj"] * (m3.lvl(fac, "trainer") - 1))
        p = m3.K["P0"] * m3.m_fatigue(f0) * m_load * m_dur * m_sk * m_fac * (m3.K["CAMP_INJ"] if camp else 1.0) * mods.get("inj_mult", 1.0)
        rep["injury_p"] = max(0.0, min(0.5, p))
    sharpened_now = (k1 == "ks.sharpen" or k2 == "ks.sharpen")
    r["sharpen_hist"] = r["sharpen_hist"][1:] + [sharpened_now]
    if r["inj"][0] != "None" or injured:
        r["taper_weeks"] = 0
    elif load <= m3.K["TAPER_L_MAX"]:
        r["taper_weeks"] += 1
    else:
        r["taper_weeks"] = 0
    pk = m3.peaking_bonus(r["taper_weeks"], any(r["sharpen_hist"]), r["skills"])
    rep["mu"] = (50 - f1) * 0.5 + max(-8, min(12, pk + mods.get("form_add", 0.0)))   # 06 §6: peaking に form_add を足して −8〜+12 に収める(03 §6 手順6の読み方を確定)
    rep["sigma"] = 8.0 * (1.5 - r["stats"][IDX["CON"]] / 100.0)
    rep["peaking"] = pk
    rep["injured_now"] = r["inj"][0] != "None" or injured
    r["fatigue"] = f1
    return rep


def norm_cdf(x):
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


FORM_EDGES = (20.0, 8.0, -8.0, -20.0)   # 絶好調 ≥20 / 好調 ≥8 / 普通 >-8 / 不調 >-20 / 絶不調


def form_probs(mu, sigma):
    """調子スコア ~ N(mu, sigma) から5段階(+2, +1, 0, -1, -2)の確率。境界は 03 §2: ≥20 / ≥8 / >-8 / >-20。"""
    c = [1.0 - norm_cdf((e - mu) / sigma) for e in FORM_EDGES]      # P(score ≥ e)
    p2 = c[0]
    p1 = c[1] - c[0]
    p0 = c[2] - c[1]
    pm1 = c[3] - c[2]
    pm2 = 1.0 - c[3]
    return {2: p2, 1: p1, 0: p0, -1: pm1, -2: pm2}


def forecast(r0, week_menus, fac, month_index0, camps=None, safety=True, bond_avg=None, mods=None):
    """r0 の状態から、週メニューのリストを順に進めた予測。
    week_menus: [(menu, month_index, camp, individual)]。戻り値は dict。"""
    r = dict(r0)
    r["stats"] = list(r0["stats"])
    r["xp"] = list(r0["xp"])
    r["sharpen_hist"] = list(r0["sharpen_hist"])
    r["skills"] = set(r0["skills"])
    f_path, p_inj_path = [], []
    safety_weeks = 0
    last = None
    surv = 1.0
    for menu, mi, camp, indiv in week_menus:
        rep = expected_week(r, menu, fac, mi, camp=camp, safety=safety, individual=indiv, bond_avg=bond_avg, mods=mods)
        f_path.append(rep["F_end"])
        p_inj_path.append(rep["injury_p"])
        surv *= (1.0 - rep["injury_p"])
        safety_weeks += 1 if rep.get("safety") else 0
        last = rep
    return dict(F=f_path, F_max=max(f_path) if f_path else r0["fatigue"], p_injury=1.0 - surv,
                stats_after=r["stats"], levelups=[r["stats"][i] - r0["stats"][i] for i in range(10)],
                safety_weeks=safety_weeks, last=last, form=form_probs(last["mu"], last["sigma"]) if last else None,
                end=r)


# ================================================================ R-04 推奨プラン
TEMPLATES = {   # 4月〜3月(month_index 0..11)。Balanced は 03 §11 の標準プラン
    "Balanced": ["th.base_build", "th.speed", "th.base_build", "th.mountain", "th.camp", "th.speed",
                 "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.speed", "th.base_build"],
    # スピード寄り: 6月・11月の基礎ビルドをスピード強化にする
    "Speed":    ["th.base_build", "th.speed", "th.speed", "th.mountain", "th.camp", "th.speed",
                 "th.base_build", "th.speed", "th.mountain", "th.recovery", "th.speed", "th.base_build"],
    # ロング寄り: 7月・12月の山対策を基礎ビルドにする(山の月がなくなる代わりに、登坂・下りの伸びが落ちる)
    "Long":     ["th.base_build", "th.speed", "th.base_build", "th.base_build", "th.camp", "th.speed",
                 "th.base_build", "th.base_build", "th.base_build", "th.recovery", "th.speed", "th.base_build"],
}
FOCUS_JA = {"Balanced": "バランス", "Speed": "スピード寄り", "Long": "ロング寄り"}
# 採用しなかった重点(チーム全体を持久・山に振るもの)。全区間タイプで標準に劣るため、チーム全体の重点にはしない(08 §8 R-04)
REJECTED = {
    "Endurance": ["th.base_build", "th.base_build", "th.base_build", "th.mountain", "th.camp", "th.base_build",
                  "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.base_build", "th.base_build"],
    "Mountain":  ["th.base_build", "th.speed", "th.base_build", "th.mountain", "th.camp", "th.mountain",
                  "th.base_build", "th.base_build", "th.mountain", "th.recovery", "th.mountain", "th.base_build"],
}
FOCUS_JA.update({"Endurance": "(持久重視)", "Mountain": "(山重視)"})


def auto_plan(focus, month_index):
    """推奨プラン: 重点ごとの12か月のテンプレートから、その月のテーマを返す(グループによらず同じ)。"""
    return TEMPLATES[focus][month_index]


def plan_week_menu(year_week, theme_by_month, races=m3.RACES, taper=True):
    """m3.week_menu と同じ(注: ピーキングは登録メンバーのみ)。"""
    return m3.week_menu(year_week, theme_by_month, races, taper)


def sim_career(seed, rid, fac, focus="Balanced", guard=False, years=4, gtype="Normal", stats0=None, pot0=None):
    """4年間の育成(03 §11 と同じ設定)。月ごとのテーマは推奨プランのテンプレートどおり。"""
    rng = m3.Pcg32(m3.derive(seed, 99, rid, 0))
    stats0 = stats0 or [max(30, min(70, round(rng.normal(50, 6)))) for _ in range(10)]
    pot0 = pot0 or [min(100, s + rng.randint(15, 35)) for s in stats0]
    r = m3.new_runner(stats0, pot0, 1, gtype)
    inj = sev = inj_w = 0
    forms = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
    race_forms = []
    for y in range(years):
        r["grade"] = y + 1
        plan = list(TEMPLATES[focus])
        for yw in range(48):
            mi, wk = divmod(yw, 4)
            menu, camp = m3.week_menu(yw, plan, m3.RACES, True)
            aw = y * 48 + yw
            rep = m3.resolve_week(r, menu, fac, mi, seed, rid, aw, camp=camp, safety=True,
                                  bond_avg=min(1000.0, aw * 10.0 * 0.7))
            if "injury" in rep:
                inj += 1
                sev += rep["injury"][0] == "Severe"
            inj_w += r["inj"][0] != "None"
            forms[r["form"]] += 1
            if yw in m3.RACES:
                race_forms.append(r["form"])
                r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
    return stats0, r, dict(inj=inj, severe=sev, inj_weeks=inj_w, forms=forms, race_forms=race_forms)


def cmd_autoplan(n=600):
    fac = FAC_LV2
    print("== 推奨プラン(R-04)の検算: 4年間・各%d人。施設とスタッフは Lv2(03 §11 と同じ設定)" % n)
    rows = {}
    TEMPLATES.update(REJECTED)
    for focus in ("Balanced", "Speed", "Long", "Endurance", "Mountain"):
        d = [0.0] * 10
        inj = wk = 0
        rf = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
        ps = {t: 0.0 for t in W}
        for rid in range(n):
            s0, r, st = sim_career(2026, rid, fac, focus)
            for i in range(10):
                d[i] += r["stats"][i] - s0[i]
            inj += st["inj"]
            wk += st["inj_weeks"]
            for v in st["race_forms"]:
                rf[v] += 1
            for t in W:
                ps[t] += rating(r["stats"], t) - rating(s0, t)
        ry = n * 4
        tr = sum(rf.values())
        rows[focus] = dict(d=[x / n for x in d], inj=inj / ry, wk=wk / ry, rf={k: v / tr for k, v in rf.items()}, ps={t: v / n for t, v in ps.items()})
    print("  [4年間の平均上昇(能力) / 区間タイプ別の適性値 P の上昇(標準プランとの差)/ 故障 / 大会週の調子(好調以上)]")
    print("  重点         | " + " ".join(f"{s_:>5}" for s_ in STATS) + " | " + " ".join(f"{t:>7}" for t in W) + " | 故障/人年 離脱週 好調以上")
    base = rows["Balanced"]["ps"]
    for focus, v in rows.items():
        print(f"  {FOCUS_JA[focus]:<8}     | " + " ".join(f"{x:5.1f}" for x in v["d"]) + " | "
              + " ".join(f"{v['ps'][t] - (base[t] if focus != 'Balanced' else 0):+7.2f}" for t in W)
              + f" |  {v['inj']:.3f}    {v['wk']:.2f}   {v['rf'][1] + v['rf'][2]:.2f}")
    print("  (Balanced の行の P は標準プランでの上昇量そのもの: " + ", ".join(f"{t} {base[t]:+.2f}" for t in W) + ")")
    return rows


# ================================================================ forecast の検算
def make_state(rng, grade=2, gtype="Normal"):
    stats = [max(35, min(80, round(rng.gauss(58, 7)))) for _ in range(10)]
    pot = [min(100, s + rng.randint(5, 30)) for s in stats]
    r = m3.new_runner(stats, pot, grade, gtype)
    r["fatigue"] = rng.randint(10, 80)
    r["motivation"] = rng.randint(2, 5)
    r["taper_weeks"] = rng.randint(0, 2)
    r["sharpen_hist"] = [rng.random() < 0.3 for _ in range(3)]
    return r


def cmd_forecast(n=3000):
    rng = random.Random(8)
    fac = dict(coach=3, trainer=2, nutri=2, dorm=3, track=2, hill=2, gym=2, medical=2)
    bad = 0
    maxdiff_mu = 0.0
    checked = 0
    n_ind = n_gu = 0
    for k in range(n):
        r = make_state(random.Random(k), grade=rng.randint(1, 4), gtype=rng.choice(["Early", "Normal", "Late"]))
        if k % 2 == 0:                                   # 半数は個性・素養つき(10 §5。予測も同じ乗数を使う)
            import ref_model_10 as m10
            rec = m10.recruit(4242, k)
            m10.attach(r, rec["style"], rec["gifts"])
            n_ind += 1
        indiv = (k % 5 == 0)                             # 5 件に 1 件は個別指導の週(走法で効率が変わる)
        n_gu += 1 if indiv else 0
        theme = rng.choice(list(m3.THEMES))
        mi = rng.randint(0, 11)
        wk = rng.randint(0, 3)
        menu = m3.THEMES[theme][wk]
        camp = theme == "th.camp"
        r1 = dict(r)
        r1["stats"], r1["xp"] = list(r["stats"]), list(r["xp"])
        r1["sharpen_hist"], r1["skills"] = list(r["sharpen_hist"]), set(r["skills"])
        rep_e = expected_week(r1, menu, fac, mi, camp=camp, individual=indiv)
        r2 = dict(r)
        r2["stats"], r2["xp"] = list(r["stats"]), list(r["xp"])
        r2["sharpen_hist"], r2["skills"] = list(r["sharpen_hist"]), set(r["skills"])
        rep_a = m3.resolve_week(r2, menu, fac, mi, 777, k, 100 + k, camp=camp, individual=indiv)
        if "injury" in rep_a:
            continue                 # 故障が起きた週は、予測(故障なし)と一致しない
        checked += 1
        ok = (rep_e["F_end"] == rep_a["F_end"] and r1["stats"] == r2["stats"] and r1["xp"] == r2["xp"]
              and r1["taper_weeks"] == r2["taper_weeks"] and r1["sharpen_hist"] == r2["sharpen_hist"]
              and abs(rep_e["injury_p"] - rep_a["injury_p"]) < 1e-12)
        d = abs(rep_e["mu"] - (rep_a["form_score"] - rep_a["noise"]))
        maxdiff_mu = max(maxdiff_mu, d)
        if not ok or d > 1e-9:
            bad += 1
    print(f"== 予測の厳密性(週): {checked}件(故障なし。個性・素養つき {n_ind} 件・個別指導の週 {n_gu} 件を含む)で F・能力・XP・調整週・故障確率・調子の中心が一致しない件数 = {bad}、調子の中心の最大差 {maxdiff_mu:.2e}")

    # 調子の分布: 月初から大会週までの予測と、乱数での実測(故障が起きなかった経路)の比較
    print("== 調子の分布の較正(月初に立てた予測 vs 4週後の実測。各 20000 試行 / ケース)")
    cases = []
    for label, r_state, menus in make_cases(fac):
        cases.append((label, r_state, menus))
    for label, r0, menus in cases:
        fc = forecast(r0, menus, fac, 0)
        N = 20000
        cnt = {-2: 0, -1: 0, 0: 0, 1: 0, 2: 0}
        surv = 0
        for t in range(N):
            r = dict(r0)
            r["stats"], r["xp"] = list(r0["stats"]), list(r0["xp"])
            r["sharpen_hist"], r["skills"] = list(r0["sharpen_hist"]), set(r0["skills"])
            ok = True
            rep = None
            for wi, (menu, mi, camp, indiv) in enumerate(menus):
                rep = m3.resolve_week(r, menu, fac, mi, 4242 + t, 1, 1000 + wi, camp=camp)
                if "injury" in rep:
                    ok = False
                    break
            if not ok:
                continue
            surv += 1
            cnt[r["form"]] += 1
        pred = fc["form"]
        pu = 1.0 - fc["p_injury"]
        print(f"  {label}: 故障なしの割合 予測 {pu:.3f} / 実測 {surv / N:.3f} | 好調以上 予測 {pred[1] + pred[2]:.3f} / 実測 {(cnt[1] + cnt[2]) / max(1, surv):.3f}"
              f" | 普通 予測 {pred[0]:.3f} / 実測 {cnt[0] / max(1, surv):.3f} | 不調以下 予測 {pred[-1] + pred[-2]:.3f} / 実測 {(cnt[-1] + cnt[-2]) / max(1, surv):.3f}")




def make_cases(fac):
    base = m3.new_runner([62, 66, 64, 58, 54, 52, 60, 58, 56, 58], [82, 86, 84, 78, 74, 72, 80, 78, 76, 78], 3, "Normal")
    base["fatigue"] = 55
    base["motivation"] = 3
    out = []
    # (a) 基礎ビルド4週(通常)
    out.append(("基礎ビルド4週 F=55", base, [(m3.THEMES["th.base_build"][w], 3, False, False) for w in range(4)]))
    # (b) ピーキング4週(T1〜T4)
    r = dict(base)
    r["fatigue"] = 60
    out.append(("ピーキングT1〜T4 F=60", r, [(m3.TAPER[w], 6, False, False) for w in range(4)]))
    # (c) 夏合宿4週
    r = dict(base)
    r["fatigue"] = 45
    out.append(("夏合宿4週 F=45", r, [(m3.THEMES["th.camp"][w], 4, True, False) for w in range(4)]))
    return out


# ================================================================ R-08 スカウトの見込み(較正)
RIVALRY = [(51.5, "ほぼなし", 1.00), (55.0, "少", 1.00), (59.0, "中", 0.92), (999.0, "多", 0.65)]   # (V の上限, 競合度, 枠があったときの競り勝ち率の実測)


def rivalry(v):
    """競合度: 市場評価 V の帯(05 §6.3)。戻り値: (ラベル, 練習会まで進めたときに、枠があれば競り勝つ割合の実測)。"""
    for lim, lab, win in RIVALRY:
        if v < lim:
            return lab, win
    return RIVALRY[-1][1:]


def cmd_scout(seeds=12, years=25, starts=("Mid", "Low")):
    import ref_model_05 as w
    rows = []
    for start in starts:
        for sd in range(1, seeds + 1):
            w.TRACE = []
            w.TRACE_BY_ID.clear()
            w.run(sd, years, start)
            rows.extend((start, rec[1], rec[2], bool(rec[3]), bool(rec[4]), rec[5], rec[6], rec[7], rec[8]) for rec in w.TRACE if rec[3] is not None)
    w.TRACE = None
    n = len(rows)
    print(f"== スカウトの『競合度』と枠の較正(R-08): 狙った候補 {n} 人(開始校 {'/'.join(starts)}、{seeds}回×{years}年度)")
    got = sum(r[3] for r in rows)
    blocked = sum(r[4] for r in rows)
    comp = [r for r in rows if not r[4]]
    print(f"  入学した {got / n:.3f} / 枠(定員・奨学金)で入れなかった {blocked / n:.3f} / 枠はあったが競り負けた {sum(1 for r in comp if not r[3]) / n:.3f}")
    print("  (参考)pwin(05 §7.3)の較正。0.9〜0.99 で実測72%と悪いので、表示には使わない:")
    print("  pwin の範囲   | 人数 | 勝った割合 | pwin の平均")
    for lo, hi in ((0.0, 0.5), (0.5, 0.9), (0.9, 0.99), (0.99, 0.999), (0.999, 1.01)):
        sel = [r for r in comp if lo <= r[1] < hi]
        if sel:
            print(f"  {lo:.3f}〜{min(hi, 1):.3f} | {len(sel):4d} | {sum(r[3] for r in sel) / len(sel):.3f}     | {sum(r[1] for r in sel) / len(sel):.3f}")
    print("  他校が狙い撃ちした数(競合校数)と結果:")
    for k in range(0, 5):
        sel = [r for r in rows if (r[6] == k if k < 4 else r[6] >= 4)]
        if sel:
            c2 = [r for r in sel if not r[4]]
            print(f"   競合校 {k}{'+' if k == 4 else ' '} | {len(sel):4d}人 | 入学 {sum(r[3] for r in sel) / len(sel):.3f} | 枠で入れなかった {sum(r[4] for r in sel) / len(sel):.3f} | 枠があった中で勝った {sum(r[3] for r in c2) / max(1, len(c2)):.3f}")
    print("  V の帯(市場評価)ごと:")
    for lo, hi in ((0, 45), (45, 51.5), (51.5, 55), (55, 59), (59, 99)):
        sel = [r for r in rows if lo <= r[7] < hi]
        if sel:
            print(f"   V {lo:>4}〜{hi:<4} | {len(sel):4d}人 | 競合校数の平均 {sum(r[6] for r in sel) / len(sel):.2f} | 入学 {sum(r[3] for r in sel) / len(sel):.3f} | 枠で入れなかった {sum(r[4] for r in sel) / len(sel):.3f}")
    top = [r for r in rows if r[2]]
    non = [r for r in rows if not r[2]]
    print(f"  上位級(奨学金枠が要る)の狙い {len(top)}人: 入学 {sum(r[3] for r in top) / max(1, len(top)):.3f}、枠で入れなかった {sum(r[4] for r in top) / max(1, len(top)):.3f} / それ以外 {len(non)}人: 入学 {sum(r[3] for r in non) / max(1, len(non)):.3f}、枠で入れなかった {sum(r[4] for r in non) / max(1, len(non)):.3f}")
    return rows


# ================================================================ R-16 性格タグ(look_seed から決める。規則上の効果はない)
TRAITS = ["Serious", "Competitive", "MyPace", "Passionate", "Careful", "Cheerful", "Quiet", "Caring"]


def traits_of(master, look_seed):
    """固定 3 回: ① 個数(u < 0.5 なら 2 個)② 1 つ目 ③ 2 つ目(個数が 2 のときだけ使う)。残りの種類から一様に選ぶ(重複なし)。鍵は 09 §8.2 の label 8・b = 20。"""
    g = m3.Pcg32(m3.derive(master, 8, look_seed, 20))
    un, u1, u2 = g.uniform(), g.uniform(), g.uniform()
    pool = list(TRAITS)
    out = [pool.pop(min(len(pool) - 1, int(u1 * len(pool))))]
    if un < 0.5:
        out.append(pool.pop(min(len(pool) - 1, int(u2 * len(pool)))))
    return out


# ================================================================ R-09 前評判(他校情報)
PREVIEW_LABELS = [(1, "◎"), (3, "〇"), (6, "▲"), (10, "△")]   # 前評判の順位 → 印(1位 / 2〜3位 / 4〜6位 / 7〜10位)


def preview_mark(rank):
    for lim, ch in PREVIEW_LABELS:
        if rank <= lim:
            return ch
    return "-"


def cmd_preview(seeds=8, years=25, start="Mid"):
    import ref_model_05 as w
    w.TRACE_PREVIEW = []
    for sd in range(1, seeds + 1):
        w.run(sd, years, start)
    rec = w.TRACE_PREVIEW
    w.TRACE_PREVIEW = None
    print(f"== 前評判の較正(R-09): {len(rec)}大会(開始校 {start}、{seeds}回×{years}年度)。前評判 = 登録メンバーの上位 run_size 人の区間適性(Long)の平均の順位")
    for race in ("Fall", "National", "NewYear"):
        rs = [r for r in rec if r[0] == race]
        n = len(rs)
        win_by_rank = [0] * 20
        top3_by_rank = [0] * 20
        cnt = [0] * 20
        rho_sum = 0.0
        for _, pv, order, _p in rs:
            ranked = sorted(pv, key=lambda k: -pv[k])
            pos = {sid: i for i, sid in enumerate(order)}
            for i, sid in enumerate(ranked[:20]):
                cnt[i] += 1
                win_by_rank[i] += (pos[sid] == 0)
                top3_by_rank[i] += (pos[sid] < 3)
            m = len(ranked)
            d2 = sum((i - pos[sid]) ** 2 for i, sid in enumerate(ranked))
            rho_sum += 1.0 - 6.0 * d2 / (m * (m * m - 1))
        print(f"  {race:8s} {n}大会 | 順位相関(前評判の順位と結果の順位)平均 {rho_sum / n:.3f}")
        print("    前評判の順位 : " + " ".join(f"{i + 1:>5}" for i in range(10)))
        print("    優勝した割合 : " + " ".join(f"{win_by_rank[i] / max(1, cnt[i]):5.2f}" for i in range(10)))
        print("    3位以内の割合: " + " ".join(f"{top3_by_rank[i] / max(1, cnt[i]):5.2f}" for i in range(10)))
        marks = {}
        for i in range(20):
            mk = preview_mark(i + 1)
            marks.setdefault(mk, [0, 0, 0])
            marks[mk][0] += cnt[i]
            marks[mk][1] += win_by_rank[i]
            marks[mk][2] += top3_by_rank[i]
        print("    印ごとの優勝率 / 3位以内率: " + " | ".join(f"{k} {v[1] / max(1, v[0]):.2f} / {v[2] / max(1, v[0]):.2f}" for k, v in marks.items()))
    return rec


# ================================================================ R-07 画面を止める回数(割り込み)
def cmd_stops(n_teams=40, years=4, team_size=20):
    """03 の標準プランで A・B グループ(20人)を4年間動かして、割り込み(停止)の回数を数える。
    停止: 疲労の警告(月に1回まで、3週目の終わりに F≥70(夏合宿の月は F≥80)の選手がいるとき)/ 故障(中傷以上で A グループ、または重傷)。"""
    fac = dict(coach=2, trainer=2, nutri=2, dorm=2, track=2, hill=2, gym=2, medical=2)
    warn_months = 0
    warn_total_runners = 0
    inj_stops = 0
    inj_all = 0
    month_counts = [0] * 12
    lv_total = [0] * 12            # 月ごとのレベルアップ件数(全員)
    rk_total = [0] * 12            # 月ごとの『ランク文字が変わった』件数(全員・全能力)
    run_up = [0] * 12              # 月ごとに『能力が合計 +3 以上』上がった選手の数
    for tm in range(n_teams):
        runners = []
        for k in range(team_size):
            rng = m3.Pcg32(m3.derive(31337, 7, tm, k))
            stats0 = [max(35, min(80, round(rng.normal(60, 6)))) for _ in range(10)]
            pot0 = [min(100, s + rng.randint(10, 30)) for s in stats0]
            r = m3.new_runner(stats0, pot0, 1 + k % 4, "Normal")
            runners.append((k, r))
        for y in range(years):
            for yw in range(48):
                mi, wk = divmod(yw, 4)
                menu, camp = m3.week_menu(yw, TEMPLATES["Balanced"], m3.RACES, True)
                for k, r in runners:
                    aw = y * 48 + yw
                    if wk == 0:
                        r["_m0"] = list(r["stats"])
                    rep = m3.resolve_week(r, menu, fac, mi, 5150 + tm, k, aw, camp=camp, bond_avg=min(1000.0, aw * 7.0))
                    lv_total[mi] += rep["levelups"]
                    if wk == 3:
                        m0 = r["_m0"]
                        rk_total[mi] += sum(1 for i in range(10) if rank_letter(m0[i]) != rank_letter(r["stats"][i]))
                        run_up[mi] += 1 if sum(r["stats"][i] - m0[i] for i in range(10)) >= 3 else 0
                    if "injury" in rep:
                        inj_all += 1
                        sev = rep["injury"][0]
                        if sev == "Severe" or (sev == "Moderate" and k < 10):
                            inj_stops += 1
                    if yw in m3.RACES:
                        r["fatigue"] = min(100, r["fatigue"] + m3.round_half_up(m3.RACES[yw] * m3.K["RACE_FATIGUE_PER_KM"]))
                if wk == 2:
                    lim = K["WARN_F_CAMP"] if camp else K["WARN_F"]
                    hot = [k for k, r in runners if r["inj"][0] == "None" and lim <= r["fatigue"] < m3.K["SAFETY_F"]]
                    if hot:
                        warn_months += 1
                        warn_total_runners += len(hot)
                        month_counts[mi] += 1
    ry = n_teams * years
    print(f"== 割り込みの回数(R-07): 20人(A10+B10)・標準プラン・{n_teams}チーム×{years}年度")
    print(f"  疲労の警告の停止: {warn_months / ry:.2f} 回/年度(月ごとの割合: " + " ".join(f"{m}:{c / ry:.2f}" for m, c in enumerate(month_counts)) + f")、1回あたり対象 {warn_total_runners / max(1, warn_months):.1f} 人")
    print(f"  故障: 全体 {inj_all / ry:.1f} 件/年度(20人)、うち停止(重傷、または A 10人の中傷)= {inj_stops / ry:.2f} 件/年度")
    print("  月末レポートの材料(20人・1か月あたりの平均): レベルアップ件数 / ランク文字が変わった件数 / 能力+3以上の選手数")
    print("   月(4月〜3月): " + " ".join(f"{(lv_total[m] / ry):5.1f}/{(rk_total[m] / ry):3.1f}/{(run_up[m] / ry):4.1f}" for m in range(12)))
    print(f"   年平均(月): レベルアップ {sum(lv_total) / ry / 12:.1f} 件、ランク変化 {sum(rk_total) / ry / 12:.2f} 件、能力+3以上 {sum(run_up) / ry / 12:.1f} 人")
    return warn_months / ry, inj_stops / ry


# ================================================================ R-04 グループの自動編成
def auto_groups(p_long_list):
    """力 P_long の高い順に、上位 K_GROUP_A 人を A、次の K_GROUP_B 人を B、残りを C。同値は ID の小さい方が上。
    入力: [(runner_id, P_long)]。出力: {runner_id: 'A'|'B'|'C'}"""
    order = sorted(p_long_list, key=lambda x: (-x[1], x[0]))
    out = {}
    for i, (rid, _) in enumerate(order):
        out[rid] = "A" if i < K["GROUP_A"] else ("B" if i < K["GROUP_A"] + K["GROUP_B"] else "C")
    return out


# ================================================================ R-07 進行の停止(FlowController の決定表)
STOP_CAP_PER_WEEK = 3
RACE_WEEKS_08 = {"Fall": 25, "National": 28, "NewYear": 36}
QUAL_WEEK_08 = 26


def stops_before_week(week, qualifies, seed_school, plan_auto):
    """週の初めに止まる場所(計画・登録・オーダー)。week は年度内の 0〜47。
    qualifies: 出場権のある大会 {'Fall','National','NewYear'}。seed_school: 第三戦のシード(予選会なし)か。"""
    out = []
    if week == 0:
        out.append("AnnualStart")
    if week % 4 == 0 and not plan_auto:
        out.append("MonthPlan")
    for race, t in RACE_WEEKS_08.items():
        if race in qualifies and week == t - 4:
            out.append(f"Entry:{race}")
    if not seed_school and week == QUAL_WEEK_08 - 4:
        out.append("Entry:Qualifier")
    for race, t in RACE_WEEKS_08.items():
        if race in qualifies and week == t:
            out.append(f"Order:{race}")
    return out


def stops_after_week(week, events, injuries, warn, race_kind, event_auto):
    """週の終わり(練習・大会・EventPhase のあと)に止まる場所と順序。
    events: [(event_id, n_choices)]。injuries: [(runner_id, severity, group, registered)]。warn: 疲労が危険域の選手のリスト。
    戻り先: (止まる場所の順序, 月末レポートへ回すもの)。"""
    stops, deferred = [], []
    if race_kind:
        stops.append(f"RaceDay:{race_kind}")
    for ev, n in events:
        if n >= 2 and not event_auto:
            stops.append(f"Event:{ev}")
        else:
            deferred.append(f"Notice:{ev}")
    for rid, sev, grp, reg in injuries:
        sev_rank = {"Minor": 1, "Moderate": 2, "Severe": 3}[sev]
        if race_kind:
            deferred.append(f"Injury:{rid}(当日の画面に表示)")      # 大会週の故障は、当日の画面で扱う(当日変更・自動の代役)
        elif sev_rank == 3 or (sev_rank == 2 and (grp == "A" or reg)):
            stops.append(f"Injury:{rid}")
        else:
            deferred.append(f"Injury:{rid}")
    if week % 4 == 2 and warn:
        stops.append("Warn")
    if len(stops) > STOP_CAP_PER_WEEK:
        deferred += [f"{x}(上限)" for x in stops[STOP_CAP_PER_WEEK:]]
        stops = stops[:STOP_CAP_PER_WEEK]
    if week % 4 == 3:
        stops.append("MonthReport")
    if week == 47:
        stops.append("YearEnd")
    return stops, deferred


# ================================================================ R-06 個別指導(検証)
def validate_menu(ks1, ks2, mil, injured=False):
    """03 §5.5 の検証。戻り値: エラーのリスト(空なら有効)。"""
    errs = []
    if injured:
        errs.append("故障中は指定できない")
    if ks1 == ks2:
        errs.append("ポイント練習の2枠は異なるIDにする")
    for k in (ks1, ks2):
        if k == "ks.cross_train":
            errs.append("ks.cross_train は指定できない")
        if k not in m3.KS:
            errs.append(f"未定義のポイント練習 {k}")
    if mil not in m3.MIL:
        errs.append("走行量は必須")
    return errs


def can_guide(points_left, kind, runner_state, month_week, already_talked=False, menu=None):
    """個別指導の可否。kind='menu'|'talk'。runner_state: dict(injured). month_week: 今の週(0〜3)。
    戻り値: (可否, 理由)。"""
    if points_left < K["GUIDE_COST"]:
        return False, "指導ポイントが足りない"
    if kind == "menu":
        if runner_state.get("injured"):
            return False, "故障中の選手には指定できない"
        if menu is None or validate_menu(*menu):
            return False, "メニューが不正"
        return True, "今週から月末まで有効"
    if kind == "talk":
        if already_talked:
            return False, "面談は1人につき月1回"
        return True, "やる気 +1(03 M3)"
    return False, "不明な種別"


# ================================================================ R-10 月末レポートの項目の選択
REPORT_PRIORITY = {"injury_severe": 100, "injury_moderate": 90, "awaken": 80, "gift_reveal": 75, "rank_up": 60, "big_growth": 50,
                   "return": 40, "notice": 30, "form_up": 20}
K.update(REPORT_MAX=7, REPORT_PER_KIND=3, GUIDE_COST=1)


def select_report_items(items):
    """items: [(kind, key, score)]。優先度の高い順、同じ種別は score の大きい順に最大 REPORT_PER_KIND 件、全体で REPORT_MAX 件。
    戻り値: (表示する項目, 『ほか n件』の件数)。"""
    by_kind = {}
    for it in items:
        by_kind.setdefault(it[0], []).append(it)
    cand = []
    for kind, lst in by_kind.items():
        lst.sort(key=lambda x: (-x[2], str(x[1])))
        cand += [(REPORT_PRIORITY.get(kind, 0), it) for it in lst[: K["REPORT_PER_KIND"]]]
    cand.sort(key=lambda x: (-x[0], -x[1][2], str(x[1][1])))
    shown = [it for _, it in cand[: K["REPORT_MAX"]]]
    return shown, len(items) - len(shown)


# ================================================================ テーマごとの月の伸び(I-10 の表示値)
def theme_gain_table(stats=None, f0=40, grade=2, gtype="Normal"):
    """基準の選手(全能力 58、素質 +25、F=40、施設Lv2)が、各テーマを1か月続けたときの適性値 P の伸び(能力 + XP の端数)。"""
    stats = stats or [58] * 10
    out = {}
    for th in m3.THEMES:
        r = m3.new_runner(stats, [x + 25 for x in stats], grade, gtype)
        r["fatigue"] = f0
        for w in range(4):
            expected_week(r, m3.THEMES[th][w], FAC_LV2, 3, camp=(th == "th.camp"), bond_avg=0.0)
        frac = [r["stats"][i] + r["xp"][i] / (10.0 * (20 + 0.6 * r["stats"][i])) - stats[i] for i in range(10)]
        st_f = [stats[i] + frac[i] for i in range(10)]
        out[th] = dict(dP={t: rating(st_f, t) - rating(stats, t) for t in W}, F_end=r["fatigue"])
    return out


# ================================================================ 画面カタログの検証(graph)
def _adjacent(grid):
    """領域の並び(行 → 列)から、方向キーの隣接(双方向)を作る。縦は列の位置を比例させて対応づける。"""
    adj = {}
    for r, row in enumerate(grid):
        for c, name in enumerate(row):
            adj.setdefault(name, set())
            if c + 1 < len(row):
                adj[name].add(row[c + 1])
                adj.setdefault(row[c + 1], set()).add(name)
            if r + 1 < len(grid):
                nxt = grid[r + 1]
                j = min(len(nxt) - 1, (c * len(nxt)) // len(row))
                adj[name].add(nxt[j])
                adj.setdefault(nxt[j], set()).add(name)
            if r + 1 < len(grid) and len(grid[r + 1]) > len(row):
                for j in range(len(grid[r + 1])):       # 下の行の方が列が多いときは、すべての列から上へ戻れる
                    k = min(len(row) - 1, (j * len(row)) // len(grid[r + 1]))
                    adj.setdefault(grid[r + 1][j], set()).add(row[k])
                    adj.setdefault(row[k], set()).add(grid[r + 1][j])
    return adj


def validate_catalog(verbose=True):
    import screens_08 as sc
    errs = []
    ids = [x["id"] for x in sc.SCREENS]
    if len(ids) != len(set(ids)):
        errs.append("画面IDが重複している")
    tokens = {"boot", "flow", "*"}
    known = set(ids) | tokens
    for x in sc.SCREENS:
        for t in x["entries"] + x["exits"] + ([x["back"]] if x["back"] else []):
            if t not in known:
                errs.append(f"{x['id']}: 未定義の参照 {t}")
        if x["kind"] not in ("screen", "flow", "dialog"):
            errs.append(f"{x['id']}: kind が不正")
        if x["default_focus"] not in {n for row in x["grid"] for n in row}:
            errs.append(f"{x['id']}: 既定のフォーカスが領域にない")
    # 到達性(起点 scr.title。'flow' は進行の疑似ノード、'*' はどこからでも開く)
    edges = {i: set() for i in ids}
    edges["flow"] = set()
    for x in sc.SCREENS:
        for t in x["exits"]:
            if t == "flow":
                edges[x["id"]].add("flow")
            elif t == "*":
                pass
            else:
                edges[x["id"]].add(t)
        for t in x["entries"]:
            if t == "flow":
                edges["flow"].add(x["id"])
            elif t == "*":
                for i in ids:
                    edges[i].add(x["id"])
            elif t in ids:
                edges[t].add(x["id"])
    seen, stack = {"scr.title"}, ["scr.title"]
    while stack:
        n = stack.pop()
        for m in edges.get(n, ()):
            if m not in seen:
                seen.add(m)
                stack.append(m)
    unreach = [i for i in ids if i not in seen]
    if unreach:
        errs.append("到達できない画面: " + ", ".join(unreach))
    # 戻る操作の深さ(通常の画面は 3 回以内でホームかタイトルへ)
    deep = []
    for x in sc.SCREENS:
        if x["kind"] != "screen":
            continue
        d, cur = 0, x["id"]
        while sc.BY_ID[cur]["back"] and cur not in ("scr.home", "scr.title") and d < 10:
            cur = sc.BY_ID[cur]["back"]
            d += 1
        if d > 3:
            deep.append((x["id"], d))
    if deep:
        errs.append(f"戻る操作が3回を超える画面: {deep}")
    # ダイアログの重なり: dlg.* の出口は、通常の画面か dlg.confirm だけ
    for x in sc.SCREENS:
        if x["id"].startswith("dlg.") and x["id"] != "dlg.confirm":
            bad = [t for t in x["exits"] if t.startswith("dlg.") and t not in ("dlg.confirm",)]
            if bad:
                errs.append(f"{x['id']}: ダイアログの上にダイアログ {bad}")
    # 出口と入口の対応(警告): A の出口に B があるのに、B の入口に A も '*' も 'flow' もない
    warn = []
    for x in sc.SCREENS:
        for t in x["exits"]:
            if t in sc.BY_ID and t != x["back"] and x["kind"] != "dialog":
                y = sc.BY_ID[t]
                if x["id"] not in y["entries"] and "*" not in y["entries"] and not (x["kind"] == "flow" and "flow" in y["entries"]):
                    warn.append(f"{x['id']} → {t}")
    if verbose and warn:
        print("  (警告)出口と入口が対応していない遷移:", ", ".join(warn))
    # コマンド・情報の網羅
    cids = [c[0] for c in sc.COMMANDS]
    if len(cids) != len(set(cids)):
        errs.append("コマンドIDが重複している")
    iids = [c[0] for c in sc.INFOS]
    if len(iids) != len(set(iids)):
        errs.append("情報IDが重複している")
    used = {}
    for cid, name, src, prim, sec in sc.COMMANDS:
        for t in [prim] + list(sec):
            if t not in sc.BY_ID:
                errs.append(f"{cid}: 未定義の画面 {t}")
        used.setdefault(prim, []).append(cid)
    info_used = {}
    for iid, name, src, scs in sc.INFOS:
        for t in scs:
            if t not in sc.BY_ID:
                errs.append(f"{iid}: 未定義の画面 {t}")
            info_used.setdefault(t, []).append(iid)
    orphan = [i for i in ids if i not in used and i not in info_used and i not in ("dlg.confirm",)]
    if orphan:
        errs.append("コマンドも情報も持たない画面: " + ", ".join(orphan))
    # パッドのフォーカス(領域の隣接が連結)
    for x in sc.SCREENS:
        adj = _adjacent(x["grid"])
        names = {n for row in x["grid"] for n in row}
        seen2, st2 = {x["default_focus"]}, [x["default_focus"]]
        while st2:
            n = st2.pop()
            for m in adj.get(n, ()):
                if m not in seen2:
                    seen2.add(m)
                    st2.append(m)
        if seen2 != names:
            errs.append(f"{x['id']}: 方向キーで届かない領域 {sorted(names - seen2)}")
    if verbose:
        print(f"== 画面カタログの検証: 画面 {len(ids)}(通常 {sum(1 for x in sc.SCREENS if x['kind'] == 'screen')} / 進行 {sum(1 for x in sc.SCREENS if x['kind'] == 'flow')} / ダイアログ {sum(1 for x in sc.SCREENS if x['kind'] == 'dialog')})、"
              f"コマンド {len(sc.COMMANDS)}、情報要件 {len(sc.INFOS)}")
        print("  違反:", "なし" if not errs else "")
        for e in errs:
            print("   -", e)
        print(f"  到達可能 {len(seen)} / {len(ids) + 1}(進行の疑似ノードを含む)")
    return errs


# ================================================================ R-14 1年度の操作時間(budget)
T = dict(TAP=1.0, READ=2.0, TRANS=0.5, WEEK_ANIM=1.2, SEC_ANIM=6.0, DIGEST_SEC=2.0, NOTICE=4.0, INTERACT=12.0)
BUDGET = dict(year_start=180, plan=480, scout=240, race=720, event=180, year_end=120)   # 01 §3.4(年度末は 08 で追加)
SECTIONS = {"Fall": 6, "National": 8, "NewYear": 10}
SWAPS_USED = {"Fall": 1, "National": 2, "NewYear": 3}      # 当日変更の平均使用数(上限は 1 / 2 / 4)
RACE_BUDGET = {"Fall": 150, "National": 210, "NewYear": 360}   # 01 §3.4: 出雲型2.5 / 全日本型3.5 / 箱根型6 (秒)


def step(taps=0, reads=0, anim=0.0, screens=1):
    return taps * T["TAP"] + reads * T["READ"] + anim + screens * T["TRANS"]


def month_time(plan_auto, special=0.0):
    t = 0.0
    if not plan_auto:
        t += step(taps=5, reads=4)                         # 月間計画: 推奨を見て、0〜2グループを変えて確定
    t += step(anim=4 * T["WEEK_ANIM"], screens=0)          # 4週の解決
    t += step(taps=1, reads=6)                             # 月末レポート
    return t + special


def race_time(name, mode, manual):
    n = SECTIONS[name]
    t = step(taps=3 if manual else 1, reads=4)             # エントリー
    t += step(taps=(2 + 3 * 2 + 1) if manual else 2, reads=n)   # 区間オーダー(おまかせ編成 + 手動の調整2回 / おまかせのみ)
    t += step(taps=1, reads=4)                             # 他校情報・予報
    if mode == "result":
        t += step(taps=1, reads=2)                         # 当日(おまかせ。結果へ)
        t += step(taps=2, reads=6)                         # 結果
        return t
    t += step(taps=1 + (3 * SWAPS_USED[name] if manual else 0), reads=6)   # 当日: 環境・調子・当日変更
    if mode == "full":
        per = step(taps=1.5 + 0.5, reads=1, anim=T["SEC_ANIM"], screens=0)   # 指示 + 檄(平均0.5回)+ 区間の演出
        t += n * per
    else:   # digest
        t += n * step(reads=0.5, anim=T["DIGEST_SEC"], screens=0)
    t += step(taps=2, reads=6)                             # 結果
    return t


def year_time(mode, manual_entry, plan_auto, event_auto, scout_auto):
    out = {}
    out["year_start"] = step(taps=1, reads=2) + step(taps=3, reads=4) + step(taps=2, reads=3) + step(reads=2)   # 入部確認 + 年間方針 + グループ + カレンダー
    out["plan"] = sum(month_time(plan_auto) for _ in range(12))
    n_t = 13
    out["scout"] = (step(taps=10, reads=12) + step(taps=2, reads=5) + n_t * step(taps=4, reads=2, screens=1)) if not scout_auto else step(taps=4, reads=6)
    qual = step(taps=3, reads=4) + step(taps=1, reads=4)   # 予選会(シード外の年度のみ。半分の年度と見なして半分)
    out["race"] = sum(race_time(r, mode, manual_entry) for r in SECTIONS) + 0.5 * qual
    if event_auto:
        out["event"] = (7 + 6) * T["NOTICE"] + 5 * T["NOTICE"]
    else:
        out["event"] = 7 * T["INTERACT"] + 6 * T["NOTICE"] + 5 * T["NOTICE"]
    out["year_end"] = step(taps=2, reads=6) + step(taps=6, reads=6) + step(taps=3, reads=8) + step(taps=3, reads=12)   # 結果・精算 / 強化 / 卒業 / 入学者発表
    return out


def cmd_budget():
    print("== 1年度の操作時間の見積もり(R-14)。機械的な最小時間(タップ・読み取り・演出)。考える時間は含まない")
    print(f"  定数: タップ {T['TAP']}秒 / 読み取り1項目 {T['READ']}秒 / 画面遷移 {T['TRANS']}秒 / 週の演出 {T['WEEK_ANIM']}秒 / 1区間の演出 {T['SEC_ANIM']}秒(ダイジェスト {T['DIGEST_SEC']}秒)")
    modes = [("A フル観戦・手動(既定の設定)", dict(mode="full", manual_entry=True, plan_auto=False, event_auto=False, scout_auto=False)),
             ("B ダイジェスト・手動の計画", dict(mode="digest", manual_entry=True, plan_auto=False, event_auto=False, scout_auto=False)),
             ("C ダイジェスト・おまかせ(計画・イベント自動)", dict(mode="digest", manual_entry=False, plan_auto=True, event_auto=True, scout_auto=False)),
             ("D 結果のみ・おまかせ(最短)", dict(mode="result", manual_entry=False, plan_auto=True, event_auto=True, scout_auto=False))]
    cats = ["year_start", "plan", "scout", "race", "event", "year_end"]
    print("  区分(秒)          | " + " | ".join(f"{c:>10}" for c in cats) + " |   合計(分)")
    print("  予算(01 §3.4)     | " + " | ".join(f"{BUDGET[c]:>10}" for c in cats) + f" | {sum(BUDGET.values()) / 60:6.1f}")
    res = {}
    for label, kw in modes:
        y = year_time(**kw)
        res[label] = y
        tot = sum(y.values())
        print(f"  {label[:1]}                 | " + " | ".join(f"{y[c]:10.0f}" for c in cats) + f" | {tot / 60:6.1f}   {label[2:]}")
    ya = res[modes[0][0]]
    print("  A の余白(予算 − 機械的な最小時間。考える時間に使える): " + ", ".join(f"{c} {BUDGET[c] - ya[c]:+.0f}" for c in cats))
    print("  大会ごと(A。秒): " + ", ".join(f"{r} {race_time(r, 'full', True):.0f}(予算 {RACE_BUDGET[r]})" for r in SECTIONS))
    print("  大会ごと(D。秒): " + ", ".join(f"{r} {race_time(r, 'result', False):.0f}" for r in SECTIONS))
    return res


# ================================================================ レイアウトの収まり(layout)
L = dict(H=360, W_LIST=(640, 720, 800), SAFE=24, HEADER=40, FOOTER=56, MARGIN=12, GAP=6, TOUCH=48, ROW=48, ROW_COMPACT=24,
         TEXT_BODY=14, TEXT_MIN=12, SIDE=160)


def cmd_layout():
    H = L["H"]
    hc = H - L["HEADER"] - L["FOOTER"]
    print(f"== レイアウトの収まり(基準キャンバス 高さ {H}u。1u = 360dp の機種での 1dp。ヘッダ {L['HEADER']}u / フッタ {L['FOOTER']}u → 内容の高さ {hc}u)")
    ok = True
    print(f"  最小タップ領域 {L['TOUCH']}u、行の高さ {L['ROW']}u(操作できる行)/ {L['ROW_COMPACT']}u(読むだけの行)、本文 {L['TEXT_BODY']}u、最小の文字 {L['TEXT_MIN']}u")
    print("  幅(安全域を除く) W' = W − 2×%du" % L["SAFE"])
    rows = []
    for W in L["W_LIST"]:
        wsafe = W - 2 * L["SAFE"]
        o10 = (wsafe - 2 * L["MARGIN"] - 9 * L["GAP"]) / 10          # 区間オーダー: 10区間のカード
        cal = (wsafe - 2 * L["MARGIN"]) / 6                           # 年間予定: 半年(6か月)×2段
        roster_cols = 96 + 28 + 28 + 36 + 36 + 28 + 24 + 24 + 36 + 72  # 部員一覧の列の幅の合計
        roster_room = wsafe - L["SIDE"] - 2 * L["MARGIN"]
        plan_rows = 3 * 56
        vis_rows = (hc - 40) // L["ROW"]                              # エントリーの一覧(概要 40u を除く)の見える行数
        standings = (hc - 80) // L["ROW_COMPACT"]                     # レース進行の順位表(コース 80u を除く)の見える行数
        region_tiles = 3 * 64 + 2 * L["GAP"]                          # スカウト: 県のタイル 3×3(最大9県 = 中部)の高さ
        rows.append((W, wsafe, o10, cal, roster_cols, roster_room, plan_rows, vis_rows, standings, region_tiles))
        ok &= o10 >= L["TOUCH"] and cal >= L["TOUCH"] and 2 * L["ROW"] <= hc and roster_cols <= roster_room and plan_rows <= hc and region_tiles <= hc
    print("  W   | W'  | オーダーのカード幅(10区間) | 年間予定の列幅(6か月×2段) | 部員一覧の列 / 余裕 | 計画の行 / 高さ | エントリーの見える行 | 順位表の見える行 | 県のタイル 3×3")
    for W, wsafe, o10, cal, rc, rr, pr, vr, st_, rt in rows:
        print(f"  {W} | {wsafe} | {o10:5.1f}u(≥{L['TOUCH']} {'○' if o10 >= L['TOUCH'] else '×'}) | {cal:5.1f}u | {rc} / {rr:.0f} {'○' if rc <= rr else '×'} | {pr} / {hc} {'○' if pr <= hc else '×'} | {vr} 行 | {st_} 行 | {rt}u {'○' if rt <= hc else '×'}")
    print("  結果:", "すべて収まる" if ok else "収まらない項目がある")
    return ok


# ================================================================ R-01 ランク表記の分布
def cmd_rank(n=600):
    cnt0 = {r: 0 for r in RANKS}
    cnt1 = {r: 0 for r in RANKS}
    tot = 0
    best = []
    for rid in range(n):
        s0, r, st = sim_career(2026, rid, FAC_LV2, "Balanced")
        for i in range(10):
            cnt0[rank_letter(s0[i])] += 1
            cnt1[rank_letter(r["stats"][i])] += 1
            tot += 1
        best.append(max(r["stats"]))
    print("== ランク表記(R-01)の分布: 03 §11 の標準プラン・4年間・%d人(入学時の能力は平均50、素質は能力+15〜35)" % n)
    print("  ランク  : " + " ".join(f"{r:>6}" for r in reversed(RANKS)))
    print("  入学時  : " + " ".join(f"{cnt0[r] / tot * 100:5.1f}%" for r in reversed(RANKS)))
    print("  卒業時  : " + " ".join(f"{cnt1[r] / tot * 100:5.1f}%" for r in reversed(RANKS)))
    best.sort()
    print(f"  卒業時の最高の能力: 中央値 {best[len(best) // 2]}、上位10% {best[int(len(best) * 0.9)]}、最大 {best[-1]}")


# ================================================================ 仕様書の表の出力(tables)
GROUP_JA = {"M": "起動・設定", "H": "ホームと進行", "T": "育成・部員", "R": "大会", "S": "スカウト", "O": "運営・歴史", "D": "ダイアログ・割り込み"}


def cmd_tables():
    import screens_08 as sc
    no = {x["id"]: i + 1 for i, x in enumerate(sc.SCREENS)}
    print("### 画面の一覧")
    print("| SC | 画面ID | 画面 | 区分 | 分類 | 入口 | 出口 | 戻る |")
    print("|----|--------|------|------|------|------|------|------|")
    kind_ja = {"screen": "通常", "flow": "進行", "dialog": "重なり"}
    def refs(lst):
        out = []
        for t in lst:
            if t in no:
                out.append(f"SC-{no[t]:02d}")
            else:
                out.append({"flow": "進行", "boot": "起動", "*": "どこからでも"}.get(t, t))
        return "・".join(out)
    for x in sc.SCREENS:
        back = f"SC-{no[x['back']]:02d}" if x["back"] else "—"
        print(f"| SC-{no[x['id']]:02d} | `{x['id']}` | {x['ja']} | {kind_ja[x['kind']]} | {GROUP_JA[x['group']]} | {refs(x['entries'])} | {refs(x['exits'])} | {back} |")
    print()
    print("### コマンド")
    print("| ID | 操作 | 出典 | 主な画面 | ほかの画面 |")
    print("|----|------|------|----------|------------|")
    for cid, name, src, prim, sec in sc.COMMANDS:
        print(f"| {cid} | {name} | {src} | SC-{no[prim]:02d} | " + ("・".join(f"SC-{no[t]:02d}" for t in sec) if sec else "—") + " |")
    print()
    print("### 情報要件")
    print("| ID | 表示する情報 | 出典 | 画面 |")
    print("|----|--------------|------|------|")
    for iid, name, src, scs in sc.INFOS:
        print(f"| {iid} | {name} | {src} | " + "・".join(f"SC-{no[t]:02d}" for t in scs) + " |")


# ================================================================ 仕様書とカタログの突き合わせ(doccheck)
def cmd_doccheck(path=None):
    """08a_screen_catalog.md の各画面の節に、カタログで割り当てた情報要件 I-xx とコマンド C-xxx が書かれているかを確かめる。"""
    import re
    import screens_08 as sc
    path = path or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "08a_screen_catalog.md")
    txt = open(path, encoding="utf-8").read()
    blocks = {}
    for m in re.finditer(r"^### (SC-\d+) .*?(?=^### SC-|\Z)", txt, re.S | re.M):
        blocks[m.group(1)] = m.group(0)
    no = {x["id"]: f"SC-{i + 1:02d}" for i, x in enumerate(sc.SCREENS)}

    def ids_in(block):
        out = set(re.findall(r"I-\d+", block))
        for a, b in re.findall(r"I-(\d+)〜I-(\d+)", block):
            out |= {f"I-{k:02d}" for k in range(int(a), int(b) + 1)}
        return out
    missing = []
    for iid, name, src, scs in sc.INFOS:
        for t in scs:
            if iid not in ids_in(blocks.get(no[t], "")):
                missing.append((iid, no[t]))
    cm = []
    for cid, name, src, prim, sec in sc.COMMANDS:
        if cid not in blocks.get(no[prim], ""):
            cm.append((cid, no[prim]))
    print(f"== 仕様書(08a)とカタログの突き合わせ: 画面の節 {len(blocks)} / {len(sc.SCREENS)}、情報要件の記載漏れ {len(missing)}、コマンドの記載漏れ {len(cm)}")
    for x in missing:
        print("   情報", x)
    for x in cm:
        print("   コマンド", x)
    return not missing and not cm and len(blocks) == len(sc.SCREENS)


# ================================================================ 計算例(テストベクタ)
def vectors():
    print("== TV-1 ランク表記 (R-01)")
    print("  値→ランク:", {v: rank_letter(v) for v in (0, 29, 30, 39, 40, 49, 50, 59, 60, 69, 70, 79, 80, 89, 90, 100)})
    print("== TV-2 素質のヒント (R-02)")
    for s_, p_ in ((50, 70), (60, 70), (66, 70), (68, 70), (70, 70), (82, 86)):
        print(f"  stat {s_} / 素質 {p_}: 比 {s_ / p_:.3f} → {potential_hint(s_, p_)}")
    print("== TV-3 記録の導出 (R-03。形=調子0・環境なし)")
    for P in (50, 60, 70, 80, 90):
        st = [P] * 10
        print(f"  全能力 {P}: 5000m {fmt_ds(derive_pb(st, 't5000'))} / 10000m {fmt_ds(derive_pb(st, 't10000'))} / ハーフ {fmt_ds(derive_pb(st, 't_half'))}")
    st = [62, 66, 64, 58, 54, 52, 60, 58, 56, 58]
    print("  例(SPD62 STA66 PAC64 KCK58 CLB54 DWN52 CON60 MEN58 REC56 DUR58): "
          + " / ".join(f"{k} {fmt_ds(derive_pb(st, k))}" for k in PB_EVENTS) + "  (ds: " + ", ".join(str(derive_pb(st, k)) for k in PB_EVENTS) + ")")
    print("== TV-4 グループの自動編成 (R-04)")
    plist = [(i, 72.0 - 0.5 * i) for i in range(1, 27)]
    plist[10] = (11, 67.0)           # 10番(67.0)と11番を同値にして、A と B の境目の同値を ID で決める
    g = auto_groups(plist)
    print("  26人(力 = 72.0 − 0.5×番号、ただし11番は 67.0 = 10番と同値): A = 1〜10 / B = 11〜20 / C =", [k for k, v in g.items() if v == "C"],
          f"(A {sum(1 for v in g.values() if v == 'A')}人・B {sum(1 for v in g.values() if v == 'B')}人・C {sum(1 for v in g.values() if v == 'C')}人)")
    print("== TV-5 推奨プラン (R-04)")
    for f in TEMPLATES:
        print(f"  {FOCUS_JA[f]:<8}: " + " ".join(t.replace('th.', '')[:5] for t in TEMPLATES[f]))
    print("== TV-6 月末の予測 (R-05)。03 TV-3 の入力(F=40、施設Lv2、基礎ビルド4週)")
    r = m3.new_runner([58, 60, 56, 52, 45, 44, 55, 50, 52, 54], [80, 82, 78, 74, 70, 68, 75, 72, 70, 74], 2, "Normal")
    r["fatigue"] = 40
    fc = forecast(r, [(m3.THEMES["th.base_build"][w], 0, False, False) for w in range(4)], FAC_LV2, 0, bond_avg=0.0)
    print(f"  F の推移 {fc['F']}、4週の故障確率 {fc['p_injury'] * 100:.2f}%、安全網の週 {fc['safety_weeks']}、レベルアップ {fc['levelups']}")
    print("  4週目の調子(故障なしの前提): " + ", ".join(f"{k:+d}:{v:.3f}" for k, v in fc["form"].items()) + f"、mu {fc['last']['mu']:.3f}、sigma {fc['last']['sigma']:.3f}")
    print("== TV-7 調子の分布 (R-05)")
    for mu, sg in ((0.0, 8.0), (7.672137, 7.6), (18.5, 8.0), (26.0, 8.0)):
        pr = form_probs(mu, sg)
        print(f"  mu={mu}, sigma={sg}: " + ", ".join(f"{k:+d}:{v:.4f}" for k, v in pr.items()) + f"(好調以上 {pr[1] + pr[2]:.4f})")
    print("== TV-8 進行の停止 (R-07)")
    for w in (0, 4, 21, 22, 24, 25, 28, 32, 36):
        print(f"  週 {w:2d} の初め: シード外・手動 {stops_before_week(w, {'Fall', 'National', 'NewYear'}, False, False)} / シード・計画おまかせ {stops_before_week(w, {'Fall', 'National', 'NewYear'}, True, True)}")
    st_, df_ = stops_after_week(10, [("ev.a", 3), ("ev.b", 1)], [(1, "Severe", "C", False), (2, "Moderate", "B", False), (3, "Minor", "A", True)], [1], None, False)
    print("  週10の終わり(選択ありイベント1・お知らせ1・重傷C・中傷B・軽傷A登録・警告あり):", st_, "/ レポートへ", df_)
    st_, df_ = stops_after_week(35, [("ev.a", 2)], [(5, "Moderate", "A", True)], [], None, True)
    print("  週35の終わり(event_auto・中傷A):", st_, "/ レポートへ", df_)
    st_, df_ = stops_after_week(47, [("team.send_off", 1)], [], [], None, False)
    print("  週47の終わり:", st_, "/ レポートへ", df_)
    st_, df_ = stops_after_week(25, [("ev.a", 2), ("ev.b", 2)], [(1, "Severe", "A", True), (2, "Severe", "B", False)], [1], "Fall", False)
    print("  週25の終わり(第一戦・選択イベント2・重傷2):", st_, "/ レポートへ", df_)
    st_, df_ = stops_after_week(9, [("ev.a", 2), ("ev.b", 2), ("ev.c", 2)], [(1, "Severe", "A", True)], [1], None, False)
    print("  週9の終わり(選択イベント3・重傷1。上限3):", st_, "/ レポートへ", df_)
    print("== TV-10 テーマごとの月の伸び (I-10)。全能力58・素質+25・F=40・2年・標準・施設Lv2・合宿は8月扱い(夏男なし)")
    tg = theme_gain_table()
    for th, v in tg.items():
        print(f"  {th:14s} 月末の F {v['F_end']:3d} | " + " ".join(f"{t}:{v['dP'][t]:+.2f}" for t in W))
    print("== TV-11 個別指導の検証 (R-06)")
    for args in ((4, "menu", dict(injured=False), 1, False, ("ks.hill", "ks.threshold", "mil.std")),
                 (4, "menu", dict(injured=False), 1, False, ("ks.hill", "ks.hill", "mil.std")),
                 (4, "menu", dict(injured=True), 1, False, ("ks.hill", "ks.threshold", "mil.std")),
                 (0, "talk", dict(injured=False), 1, False, None),
                 (2, "talk", dict(injured=False), 1, True, None),
                 (2, "talk", dict(injured=False), 1, False, None),
                 (4, "menu", dict(injured=False), 1, False, ("ks.cross_train", "ks.rest", "mil.low")),
                 (4, "menu", dict(injured=False), 1, False, ("ks.core", "ks.rest", None))):
        print(f"  ポイント{args[0]}・{args[1]}・{args[5]}: {can_guide(*args)}")
    print("== TV-12 月末レポートの項目の選択 (R-10)")
    items = ([("injury_severe", "r12", 1.0)] + [("rank_up", f"r{i}", 70 - i) for i in range(5)] + [("big_growth", f"g{i}", i) for i in range(4)]
             + [("notice", f"n{i}", 0) for i in range(3)] + [("awaken", "a1", 1)] + [("return", "t1", 0)])
    shown, rest = select_report_items(items)
    print("  材料 %d件(重傷1・ランクアップ5・成長4・お知らせ3・覚醒1・復帰1)→ 表示 %d件: " % (len(items), len(shown)) + ", ".join(f"{k}:{key}" for k, key, _ in shown) + f" / ほか {rest} 件")
    print("== TV-9 競合度と前評判の印 (R-08, R-09)")
    print("  競合度(V):", {v_: rivalry(v_)[0] for v_ in (48.0, 51.4, 51.5, 54.9, 55.0, 58.9, 59.0, 66.0)})
    print("  前評判の印:", {r_: preview_mark(r_) for r_ in (1, 2, 3, 4, 6, 7, 10, 11)})


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "vectors"
    if mode == "vectors":
        vectors()
    elif mode == "forecast":
        cmd_forecast()
    elif mode == "autoplan":
        cmd_autoplan(int(sys.argv[2]) if len(sys.argv) > 2 else 600)
    elif mode == "rank":
        cmd_rank()
    elif mode == "layout":
        cmd_layout()
    elif mode == "budget":
        cmd_budget()
    elif mode == "doccheck":
        cmd_doccheck()
    elif mode == "tables":
        cmd_tables()
    elif mode == "graph":
        validate_catalog()
    elif mode == "stops":
        cmd_stops(int(sys.argv[2]) if len(sys.argv) > 2 else 40)
    elif mode == "preview":
        cmd_preview(int(sys.argv[2]) if len(sys.argv) > 2 else 8, int(sys.argv[3]) if len(sys.argv) > 3 else 25)
    elif mode == "scout":
        cmd_scout(int(sys.argv[2]) if len(sys.argv) > 2 else 12, int(sys.argv[3]) if len(sys.argv) > 3 else 25)
    elif mode == "all":
        vectors()
        cmd_forecast(1500)
        cmd_autoplan(400)
        cmd_rank(300)
        validate_catalog()
        cmd_doccheck()
        cmd_budget()
        cmd_layout()
        cmd_stops(20)
        cmd_preview(4, 15)
        cmd_scout(4, 12)
    else:
        print("unknown mode")
